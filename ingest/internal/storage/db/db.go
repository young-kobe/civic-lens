// Package db provides the storage layer's database connection and migration
// runner. Two backends are supported from one binary: SQLite, the live
// production path until the Phase 11 cutover of the Postgres redesign, and
// Postgres, the new path being built out ahead of that cutover. Open selects
// the backend by DSN scheme (see isPostgresDSN), so callers do not need to
// branch themselves. SQLite-specific logic lives in db_sqlite.go and
// Postgres-specific logic in db_postgres.go; this file holds the shared
// surface and dispatch.
package db

import (
	"context"
	"database/sql"
	"fmt"
	"os"
	"sort"
	"strconv"
	"strings"
)

// DB wraps a SQL database connection — SQLite or Postgres, selected in Open
// by the DSN scheme.
type DB struct {
	conn       *sql.DB
	dsn        string
	isPostgres bool
}

// isPostgresDSN reports whether dsn names a Postgres connection
// (postgres:// or postgresql://) rather than a SQLite file path.
func isPostgresDSN(dsn string) bool {
	return strings.HasPrefix(dsn, "postgres://") || strings.HasPrefix(dsn, "postgresql://")
}

// Open opens a database connection. dsn is either a SQLite file path or a
// Postgres connection string (postgres:// / postgresql://) — the scheme
// picks the backend so callers do not need to branch themselves.
func Open(dsn string) (*DB, error) {
	if isPostgresDSN(dsn) {
		return openPostgres(dsn)
	}
	return openSQLite(dsn)
}

// Migrate applies all pending migrations, dispatching to the SQLite or
// Postgres runner per the backend selected in Open.
func (d *DB) Migrate(ctx context.Context) error {
	if d.isPostgres {
		return d.migratePostgresDir(ctx, pgMigrationsDir)
	}
	return d.migrateSQLite(ctx)
}

// Close closes the database connection.
func (d *DB) Close() error {
	return d.conn.Close()
}

// Conn returns the underlying sql.DB for direct queries.
func (d *DB) Conn() *sql.DB {
	return d.conn
}

// IsPostgres reports whether this DB was opened against a Postgres DSN.
// Callers use it to select backend-specific SQL during the dual-backend
// period (see the package comment).
func (d *DB) IsPostgres() bool {
	return d.isPostgres
}

// BeginImmediate starts a transaction with BEGIN IMMEDIATE for write operations.
func (d *DB) BeginImmediate(ctx context.Context) (*sql.Tx, error) {
	tx, err := d.conn.BeginTx(ctx, nil)
	if err != nil {
		return nil, err
	}
	// On SQLite, immediate-mode semantics come from the busy_timeout pragma
	// rather than a literal BEGIN IMMEDIATE. On Postgres this is a plain
	// BeginTx; the name is historical until the Phase 2 writer rework.
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
