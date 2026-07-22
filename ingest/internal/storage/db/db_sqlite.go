package db

import (
	"context"
	"database/sql"
	"fmt"
	"os"
	"path/filepath"
	"regexp"

	_ "modernc.org/sqlite"
)

// beginStmtRe detects a migration that manages its own transaction (a
// BEGIN ... COMMIT block). Those are the table-rebuild migrations that also
// toggle `PRAGMA foreign_keys` — a pragma that is a no-op inside an open
// transaction, so they cannot be wrapped and are run as-is. SQLite-only.
var beginStmtRe = regexp.MustCompile(`(?im)^\s*BEGIN\b`)

// openSQLite opens (or creates) the SQLite database at the given path.
func openSQLite(dbPath string) (*DB, error) {
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

	return &DB{conn: conn, dsn: dbPath}, nil
}

// migrateSQLite applies all pending migrations from the central migrations
// directory (data/migrations/, resolved relative to the db file).
func (d *DB) migrateSQLite(ctx context.Context) error {
	// Find migrations directory relative to database (data/migrations/)
	dbDir := filepath.Dir(d.dsn)
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

// applyMigration runs one SQLite migration file atomically. Migrations
// without their own transaction control are wrapped in a single transaction
// so the schema change and its `schema_version` INSERT commit together: a
// crash (or a failing statement) mid-migration rolls the whole file back,
// leaving schema_version unchanged so a re-run re-applies it cleanly (audit
// D-6). Migrations that manage their own transaction (see beginStmtRe) are
// executed as-is — they are already atomic and cannot be nested.
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
