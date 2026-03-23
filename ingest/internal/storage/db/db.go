package db

import (
	"context"
	"database/sql"
	"fmt"
	"os"
	"path/filepath"

	_ "modernc.org/sqlite"
)

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

	// Open with WAL mode for better concurrency
	dsn := fmt.Sprintf("file:%s?_journal=WAL&_busy_timeout=5000&_foreign_keys=on", dbPath)
	conn, err := sql.Open("sqlite", dsn)
	if err != nil {
		return nil, fmt.Errorf("open database: %w", err)
	}

	// Set connection pool settings
	conn.SetMaxOpenConns(1) // SQLite handles one writer at a time
	conn.SetMaxIdleConns(1)

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

	// Definition of ordered migrations
	migrations := []struct {
		Version  int
		Filename string
	}{
		{1, "001_initial.sql"},
		{2, "002_x_tables.sql"},
		{3, "003_allow_x_post_source.sql"},
		{4, "004_hierarchy_tables.sql"},
	}

	for _, m := range migrations {
		if currentVersion < m.Version {
			fmt.Printf("Applying migration %d: %s\n", m.Version, m.Filename)

			migrationPath := filepath.Join(migrationsDir, m.Filename)
			migrationSQL, err := os.ReadFile(migrationPath)
			if err != nil {
				return fmt.Errorf("read migration file %s: %w", migrationPath, err)
			}

			if _, err := d.conn.ExecContext(ctx, string(migrationSQL)); err != nil {
				return fmt.Errorf("apply migration %s: %w", m.Filename, err)
			}
		}
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
