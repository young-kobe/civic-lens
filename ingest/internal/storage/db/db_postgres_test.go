package db

import (
	"context"
	"database/sql"
	"fmt"
	"net/url"
	"os"
	"path/filepath"
	"testing"
	"time"
)

// pgTestDSNEnv names the env var that opts this test into a real Postgres
// instance. It is intentionally distinct from CIVIC_DATABASE_URL (the
// runtime config var) so pointing the CLI at Postgres never accidentally
// makes `go test` write into it. Unset by default, so CI-less local runs and
// CI without a Postgres service both skip cleanly.
const pgTestDSNEnv = "CIVIC_TEST_POSTGRES_DSN"

// scratchDatabaseName returns a unique, never-before-seen database name for
// this test run. bootstrapSchemaMigrations hardcodes the "ops" schema and
// "ops.schema_migrations" table (by design — Postgres has exactly one ops
// schema in production), so this test cannot isolate itself by schema or row
// alone: its fixture migrations reuse version numbers 1 and 2, which collide
// with the real 0001_north_star.sql bootstrap already applied to a shared
// DSN. Isolating by whole database means this test's own bootstrap-and-drop
// of "ops" never touches the real ops schema (in particular
// ops.x_api_budget, read by internal/runner's gated budget test) and can
// never record a phantom migration version against it.
func scratchDatabaseName() string {
	return fmt.Sprintf("civiclens_test_migrate_%d_%d", os.Getpid(), time.Now().UnixNano())
}

// withDatabase returns dsn rewritten to point at a different database name
// on the same server, so the caller-supplied DSN's own database is only ever
// used as an administrative connection (CREATE/DROP DATABASE), never touched
// directly by this test.
func withDatabase(dsn, dbName string) (string, error) {
	u, err := url.Parse(dsn)
	if err != nil {
		return "", fmt.Errorf("parse dsn: %w", err)
	}
	u.Path = "/" + dbName
	return u.String(), nil
}

// TestPostgresMigrate verifies the Postgres runner end to end against a real
// server: bootstrap creates ops.schema_migrations, migrations apply in
// numeric-prefix order and are recorded, a re-run is a no-op, and a failing
// migration rolls back atomically (the D-6 guarantee) — using
// a temp fixture directory rather than the real data/pg-migrations/, per the
// instruction not to depend on that file's contents.
//
// The whole test runs inside a throwaway database (see scratchDatabaseName)
// created and dropped on the connection identified by pgTestDSNEnv, so it is
// safe to run alongside other packages' gated Postgres tests against one
// shared DSN.
func TestPostgresMigrate(t *testing.T) {
	dsn := os.Getenv(pgTestDSNEnv)
	if dsn == "" {
		t.Skipf("%s not set; skipping Postgres integration test", pgTestDSNEnv)
	}

	adminConn, err := sql.Open("pgx", dsn)
	if err != nil {
		t.Fatalf("open admin connection %q: %v", dsn, err)
	}
	defer adminConn.Close()

	scratchDB := scratchDatabaseName()
	if _, err := adminConn.Exec(fmt.Sprintf("CREATE DATABASE %s", scratchDB)); err != nil {
		t.Fatalf("create scratch database %s: %v", scratchDB, err)
	}
	defer func() {
		// database.Close() (deferred below, so it runs before this per LIFO
		// order) releases every pooled connection first; WITH (FORCE) is
		// extra insurance against any straggler.
		if _, err := adminConn.Exec(fmt.Sprintf("DROP DATABASE IF EXISTS %s WITH (FORCE)", scratchDB)); err != nil {
			t.Errorf("drop scratch database %s: %v", scratchDB, err)
		}
	}()

	scratchDSN, err := withDatabase(dsn, scratchDB)
	if err != nil {
		t.Fatalf("build scratch dsn: %v", err)
	}

	database, err := Open(scratchDSN)
	if err != nil {
		t.Fatalf("Open(%q): %v", scratchDSN, err)
	}
	defer database.Close()

	ctx := context.Background()

	migrationsDir := t.TempDir()
	writeFile(t, migrationsDir, "0001_init.sql", `
CREATE TABLE fixture_t (id INT PRIMARY KEY);
`)
	writeFile(t, migrationsDir, "0002_add_column.sql", `
ALTER TABLE fixture_t ADD COLUMN name TEXT;
`)

	if err := database.migratePostgresDir(ctx, migrationsDir); err != nil {
		t.Fatalf("first migratePostgresDir: %v", err)
	}
	assertSchemaVersion(t, database, 2)

	// Re-run must be a no-op, not a "relation already exists" error.
	if err := database.migratePostgresDir(ctx, migrationsDir); err != nil {
		t.Fatalf("second migratePostgresDir should be a no-op, got: %v", err)
	}
	assertSchemaVersion(t, database, 2)

	// A migration that fails partway must roll back entirely, leaving
	// schema_version unchanged so a corrected re-run applies cleanly.
	writeFile(t, migrationsDir, "0003_bad.sql", `
ALTER TABLE fixture_t ADD COLUMN bad_col TEXT;
THIS IS NOT VALID SQL;
`)
	if err := database.migratePostgresDir(ctx, migrationsDir); err == nil {
		t.Fatal("expected migratePostgresDir to fail on invalid SQL")
	}
	assertSchemaVersion(t, database, 2)

	var hasBadCol bool
	row := database.conn.QueryRowContext(ctx, `
SELECT EXISTS (
	SELECT 1 FROM information_schema.columns
	WHERE table_name = 'fixture_t' AND column_name = 'bad_col'
)`)
	if err := row.Scan(&hasBadCol); err != nil {
		t.Fatalf("check bad_col: %v", err)
	}
	if hasBadCol {
		t.Fatal("bad_col should not exist after the failed migration rolled back")
	}
}

func writeFile(t *testing.T, dir, name, contents string) {
	t.Helper()
	if err := os.WriteFile(filepath.Join(dir, name), []byte(contents), 0644); err != nil {
		t.Fatalf("write %s: %v", name, err)
	}
}

func assertSchemaVersion(t *testing.T, database *DB, want int) {
	t.Helper()
	var got int
	row := database.conn.QueryRowContext(context.Background(), "SELECT COALESCE(MAX(version), 0) FROM ops.schema_migrations")
	if err := row.Scan(&got); err != nil {
		t.Fatalf("query ops.schema_migrations: %v", err)
	}
	if got != want {
		t.Fatalf("schema version = %d, want %d", got, want)
	}
}
