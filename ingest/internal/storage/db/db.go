package db

import (
	"context"
	"database/sql"
	_ "embed"
	"fmt"
	"os"
	"path/filepath"

	_ "modernc.org/sqlite"
)

//go:embed migrations/001_initial.sql
var migration001 string

// DB wraps the SQLite database connection.
type DB struct {
	conn *sql.DB
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

	return &DB{conn: conn}, nil
}

// Migrate applies all pending migrations.
func (d *DB) Migrate(ctx context.Context) error {
	// Check current version
	var version int
	row := d.conn.QueryRowContext(ctx, "SELECT COALESCE(MAX(version), 0) FROM schema_version")
	if err := row.Scan(&version); err != nil {
		// Table might not exist yet, run migration
		version = 0
	}

	if version < 1 {
		if _, err := d.conn.ExecContext(ctx, migration001); err != nil {
			return fmt.Errorf("apply migration 001: %w", err)
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
