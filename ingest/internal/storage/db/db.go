// Package db provides the storage layer's database connection and migration
// runner. Postgres is the only backend: Open rejects any DSN that is not
// postgres:// or postgresql:// (see isPostgresDSN). Postgres-specific logic
// lives in db_postgres.go; this file holds the shared surface.
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

// DB wraps a Postgres connection.
type DB struct {
	conn *sql.DB
	dsn  string
}

// isPostgresDSN reports whether dsn names a Postgres connection
// (postgres:// or postgresql://). Open rejects anything else.
func isPostgresDSN(dsn string) bool {
	return strings.HasPrefix(dsn, "postgres://") || strings.HasPrefix(dsn, "postgresql://")
}

// Open opens a Postgres database connection. dsn must be a Postgres
// connection string (postgres:// or postgresql://); anything else is a
// configuration error, not a fallback to another backend.
func Open(dsn string) (*DB, error) {
	if !isPostgresDSN(dsn) {
		return nil, fmt.Errorf("civic-ingest requires a Postgres DSN (postgres:// or postgresql://); got %q", dsn)
	}
	return openPostgres(dsn)
}

// Migrate applies all pending migrations from data/pg-migrations/.
func (d *DB) Migrate(ctx context.Context) error {
	dir, err := resolvePgMigrationsDir()
	if err != nil {
		return err
	}
	return d.migratePostgresDir(ctx, dir)
}

// Close closes the database connection.
func (d *DB) Close() error {
	return d.conn.Close()
}

// Conn returns the underlying sql.DB for direct queries.
func (d *DB) Conn() *sql.DB {
	return d.conn
}

// BeginImmediate starts a transaction for write operations. A plain
// BeginTx on Postgres; the name predates the Postgres cutover and is kept
// so callers (e.g. ArticleWriter.flush) don't need to change.
func (d *DB) BeginImmediate(ctx context.Context) (*sql.Tx, error) {
	return d.conn.BeginTx(ctx, nil)
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
