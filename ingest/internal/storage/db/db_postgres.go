package db

import (
	"context"
	"database/sql"
	"fmt"
	"os"
	"path/filepath"

	_ "github.com/jackc/pgx/v5/stdlib"
)

// pgMigrationsDir is where Postgres migrations live: `data/pg-migrations/`
// sits alongside `data/migrations/` (SQLite-only). Resolution must not
// depend on the process working directory — in the container, compose sets
// working_dir to the data volume while the migrations ship in the image at
// /app/data/pg-migrations, so a CWD-only lookup silently finds nothing.
const pgMigrationsDir = "data/pg-migrations"

// resolvePgMigrationsDir returns the first existing candidate: the
// CWD-relative dir (repo checkouts, tests) or the dir beside the executable
// (container image). Erroring when neither exists keeps `migrate` loud —
// a missing migrations dir is always a packaging bug, never a valid no-op.
func resolvePgMigrationsDir() (string, error) {
	candidates := []string{pgMigrationsDir}
	if exe, err := os.Executable(); err == nil {
		candidates = append(candidates, filepath.Join(filepath.Dir(exe), pgMigrationsDir))
	}
	for _, dir := range candidates {
		if info, err := os.Stat(dir); err == nil && info.IsDir() {
			return dir, nil
		}
	}
	return "", fmt.Errorf("postgres migrations directory not found (tried %v)", candidates)
}

// openPostgres connects via the pgx stdlib adapter and verifies connectivity
// with a Ping — Open should fail fast on a bad DSN rather than defer the
// error to the first query.
func openPostgres(dsn string) (*DB, error) {
	conn, err := sql.Open("pgx", dsn)
	if err != nil {
		return nil, fmt.Errorf("open postgres database: %w", err)
	}

	// Modest pool: the box is a 3-vCPU host shared with the API and job
	// workers, comparable in spirit to the SQLite connection limits below.
	conn.SetMaxOpenConns(5)
	conn.SetMaxIdleConns(2)

	if err := conn.Ping(); err != nil {
		conn.Close()
		return nil, fmt.Errorf("ping postgres database: %w", err)
	}

	return &DB{conn: conn, dsn: dsn, isPostgres: true}, nil
}

// migratePostgresDir applies all pending migrations from migrationsDir,
// tracking applied versions in ops.schema_migrations (bootstrapped below).
// Split from Migrate/migratePostgres so tests can point it at a temp fixture
// directory instead of the real data/pg-migrations/.
func (d *DB) migratePostgresDir(ctx context.Context, migrationsDir string) error {
	if err := d.bootstrapSchemaMigrations(ctx); err != nil {
		return fmt.Errorf("bootstrap ops.schema_migrations: %w", err)
	}

	var currentVersion int
	row := d.conn.QueryRowContext(ctx, "SELECT COALESCE(MAX(version), 0) FROM ops.schema_migrations")
	if err := row.Scan(&currentVersion); err != nil {
		return fmt.Errorf("query current schema version: %w", err)
	}

	migrations, err := discoverMigrations(migrationsDir)
	if err != nil {
		return fmt.Errorf("discover migrations: %w", err)
	}
	if len(migrations) == 0 {
		// An empty dir means the migrations were not packaged/mounted where
		// we looked; treating it as "nothing to do" would leave a brand-new
		// database schemaless and fail later with confusing query errors.
		return fmt.Errorf("no .sql migrations found in %s", migrationsDir)
	}

	for _, m := range migrations {
		if currentVersion < m.Version {
			fmt.Printf("Applying migration %d: %s\n", m.Version, m.Filename)

			migrationPath := filepath.Join(migrationsDir, m.Filename)
			migrationSQL, err := os.ReadFile(migrationPath)
			if err != nil {
				return fmt.Errorf("read migration file %s: %w", migrationPath, err)
			}

			if err := d.applyPostgresMigration(ctx, m, string(migrationSQL)); err != nil {
				return err
			}
		}
	}

	return nil
}

// bootstrapSchemaMigrations creates the ops schema and schema_migrations
// table if absent, so a brand-new Postgres database can track migration 0001
// the same way it tracks every later one (chicken-and-egg: the table must
// exist before anything can be recorded as applied).
func (d *DB) bootstrapSchemaMigrations(ctx context.Context) error {
	if _, err := d.conn.ExecContext(ctx, "CREATE SCHEMA IF NOT EXISTS ops"); err != nil {
		return fmt.Errorf("create ops schema: %w", err)
	}

	const createTable = `
CREATE TABLE IF NOT EXISTS ops.schema_migrations (
	version INT PRIMARY KEY,
	applied_at TIMESTAMPTZ NOT NULL DEFAULT now()
)`
	if _, err := d.conn.ExecContext(ctx, createTable); err != nil {
		return fmt.Errorf("create ops.schema_migrations: %w", err)
	}
	return nil
}

// applyPostgresMigration runs one migration file and records it in
// ops.schema_migrations inside the same transaction, so a crash mid-migration
// rolls both back together and a re-run applies cleanly — the same D-6
// guarantee as the SQLite path. Unlike SQLite migration files, Postgres
// migration files do not self-track their version; the runner records it.
func (d *DB) applyPostgresMigration(ctx context.Context, m migration, migrationSQL string) error {
	tx, err := d.conn.BeginTx(ctx, nil)
	if err != nil {
		return fmt.Errorf("begin migration %s: %w", m.Filename, err)
	}
	if _, err := tx.ExecContext(ctx, migrationSQL); err != nil {
		_ = tx.Rollback()
		return fmt.Errorf("apply migration %s: %w", m.Filename, err)
	}
	if _, err := tx.ExecContext(ctx, "INSERT INTO ops.schema_migrations (version) VALUES ($1)", m.Version); err != nil {
		_ = tx.Rollback()
		return fmt.Errorf("record migration %s: %w", m.Filename, err)
	}
	if err := tx.Commit(); err != nil {
		return fmt.Errorf("commit migration %s: %w", m.Filename, err)
	}
	return nil
}
