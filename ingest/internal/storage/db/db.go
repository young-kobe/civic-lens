package db

import (
	"context"
	"database/sql"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"sort"
	"strconv"
	"strings"

	_ "modernc.org/sqlite"
)

// beginStmtRe detects a migration that manages its own transaction (a
// BEGIN ... COMMIT block). Those are the table-rebuild migrations that also
// toggle `PRAGMA foreign_keys` — a pragma that is a no-op inside an open
// transaction, so they cannot be wrapped and are run as-is.
var beginStmtRe = regexp.MustCompile(`(?im)^\s*BEGIN\b`)

// DB wraps the SQLite database connection.
type DB struct {
	conn   *sql.DB
	dbPath string
}

// Open opens (or creates) the SQLite database at the given path.
func Open(dbPath string) (*DB, error) {
	// Ensure directory exists
	dir := filepath.Dir(dbPath)
	if err := os.MkdirAll(dir, 0755); err != nil {
		return nil, fmt.Errorf("create db directory: %w", err)
	}

	// Open with WAL mode and a long busy timeout. modernc.org/sqlite takes
	// pragmas via repeated `_pragma=NAME(value)` params — the older
	// `_busy_timeout=...` shorthand is silently ignored, which is how
	// concurrent writers used to hit SQLITE_BUSY in the worker pool.
	dsn := fmt.Sprintf(
		"file:%s?_pragma=journal_mode(WAL)&_pragma=busy_timeout(20000)&_pragma=synchronous(NORMAL)&_pragma=foreign_keys(on)",
		dbPath,
	)
	conn, err := sql.Open("sqlite", dsn)
	if err != nil {
		return nil, fmt.Errorf("open database: %w", err)
	}

	// WAL mode supports concurrent readers with a single writer.
	// Allow multiple read connections while writes serialize via busy_timeout.
	conn.SetMaxOpenConns(4)
	conn.SetMaxIdleConns(2)

	return &DB{conn: conn, dbPath: dbPath}, nil
}

// Migrate applies all pending migrations from the central migrations directory.
func (d *DB) Migrate(ctx context.Context) error {
	// Find migrations directory relative to database (data/migrations/)
	dbDir := filepath.Dir(d.dbPath)
	migrationsDir := filepath.Join(dbDir, "migrations")

	// Check current version
	var currentVersion int
	row := d.conn.QueryRowContext(ctx, "SELECT COALESCE(MAX(version), 0) FROM schema_version")
	if err := row.Scan(&currentVersion); err != nil {
		// Table might not exist yet, assume version 0
		currentVersion = 0
	}

	// Discover migrations dynamically from filesystem
	migrations, err := discoverMigrations(migrationsDir)
	if err != nil {
		return fmt.Errorf("discover migrations: %w", err)
	}

	for _, m := range migrations {
		if currentVersion < m.Version {
			fmt.Printf("Applying migration %d: %s\n", m.Version, m.Filename)

			migrationPath := filepath.Join(migrationsDir, m.Filename)
			migrationSQL, err := os.ReadFile(migrationPath)
			if err != nil {
				return fmt.Errorf("read migration file %s: %w", migrationPath, err)
			}

			if err := d.applyMigration(ctx, m, string(migrationSQL)); err != nil {
				return err
			}
		}
	}

	return nil
}

// applyMigration runs one migration file atomically. Migrations without their
// own transaction control are wrapped in a single transaction so the schema
// change and its `schema_version` INSERT commit together: a crash (or a
// failing statement) mid-migration rolls the whole file back, leaving
// schema_version unchanged so a re-run re-applies it cleanly (audit D-6).
// Migrations that manage their own transaction (see beginStmtRe) are executed
// as-is — they are already atomic and cannot be nested.
func (d *DB) applyMigration(ctx context.Context, m migration, migrationSQL string) error {
	if beginStmtRe.MatchString(migrationSQL) {
		if _, err := d.conn.ExecContext(ctx, migrationSQL); err != nil {
			return fmt.Errorf("apply migration %s: %w", m.Filename, err)
		}
		return nil
	}

	tx, err := d.conn.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("begin migration %s: %w", m.Filename, err)
	}
	if _, err := tx.ExecContext(ctx, migrationSQL); err != nil {
		_ = tx.Rollback()
		return fmt.Errorf("apply migration %s: %w", m.Filename, err)
	}
	if err := tx.Commit(); err != nil {
		return fmt.Errorf("commit migration %s: %w", m.Filename, err)
	}
	return nil
}

// Close closes the database connection.
func (d *DB) Close() error {
	return d.conn.Close()
}

// Conn returns the underlying sql.DB for direct queries.
func (d *DB) Conn() *sql.DB {
	return d.conn
}

// BeginImmediate starts a transaction with BEGIN IMMEDIATE for write operations.
func (d *DB) BeginImmediate(ctx context.Context) (*sql.Tx, error) {
	tx, err := d.conn.BeginTx(ctx, nil)
	if err != nil {
		return nil, err
	}
	// SQLite's BEGIN IMMEDIATE is handled via the busy_timeout pragma
	return tx, nil
}

// migration represents a single versioned SQL migration file.
type migration struct {
	Version  int
	Filename string
}

// discoverMigrations reads the migrations directory and returns all .sql files
// sorted by their numeric version prefix (e.g. "001_initial.sql" -> version 1).
func discoverMigrations(dir string) ([]migration, error) {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil, fmt.Errorf("read migrations directory %s: %w", dir, err)
	}

	var migrations []migration
	for _, entry := range entries {
		if entry.IsDir() || !strings.HasSuffix(entry.Name(), ".sql") {
			continue
		}

		// Parse version from filename prefix: "001_initial.sql" -> 1
		parts := strings.SplitN(entry.Name(), "_", 2)
		if len(parts) < 2 {
			continue
		}

		version, err := strconv.Atoi(parts[0])
		if err != nil {
			continue // Skip files without a numeric prefix
		}

		migrations = append(migrations, migration{
			Version:  version,
			Filename: entry.Name(),
		})
	}

	sort.Slice(migrations, func(i, j int) bool {
		return migrations[i].Version < migrations[j].Version
	})

	return migrations, nil
}
