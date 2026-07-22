package db

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

// pgTestDSNEnv names the env var that opts this test into a real Postgres
// instance. It is intentionally distinct from CIVIC_DATABASE_URL (the
// runtime config var) so pointing the CLI at Postgres never accidentally
// makes `go test` write into it. Unset by default, so CI-less local runs and
// CI without a Postgres service both skip cleanly.
const pgTestDSNEnv = "CIVIC_TEST_POSTGRES_DSN"

// TestPostgresMigrate verifies the Postgres runner end to end against a real
// server: bootstrap creates ops.schema_migrations, migrations apply in
// numeric-prefix order and are recorded, a re-run is a no-op, and a failing
// migration rolls back atomically (the same D-6 guarantee as SQLite) — using
// a temp fixture directory rather than the real data/pg-migrations/, per the
// instruction not to depend on that file's contents.
func TestPostgresMigrate(t *testing.T) {
	dsn := os.Getenv(pgTestDSNEnv)
	if dsn == "" {
		t.Skipf("%s not set; skipping Postgres integration test", pgTestDSNEnv)
	}

	database, err := Open(dsn)
	if err != nil {
		t.Fatalf("Open(%q): %v", dsn, err)
	}
	defer database.Close()
	if !database.isPostgres {
		t.Fatalf("Open(%q) did not select the Postgres backend", dsn)
	}

	ctx := context.Background()
	// Start from a clean slate so re-running this test against a persistent
	// dev instance behaves the same as a fresh container.
	if _, err := database.conn.ExecContext(ctx, "DROP SCHEMA IF EXISTS ops CASCADE; DROP TABLE IF EXISTS fixture_t"); err != nil {
		t.Fatalf("reset test schema: %v", err)
	}

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
