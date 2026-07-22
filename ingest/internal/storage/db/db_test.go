package db

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

// TestIsPostgresDSN pins the DSN-scheme rule that Open()/Migrate() branch on:
// only postgres://... and postgresql://... select the Postgres backend, so a
// path change here is never accidental — every other string (including a
// bare "file:" DSN) must keep taking the SQLite path.
func TestIsPostgresDSN(t *testing.T) {
	cases := []struct {
		dsn  string
		want bool
	}{
		{"postgres://user:pass@localhost:5432/civic_lens", true},
		{"postgresql://user:pass@localhost:5432/civic_lens", true},
		{"data/civic_lens.db", false},
		{"/abs/path/civic_lens.db", false},
		{"file:data/civic_lens.db?_pragma=busy_timeout(20000)", false},
		{"", false},
	}
	for _, c := range cases {
		if got := isPostgresDSN(c.dsn); got != c.want {
			t.Errorf("isPostgresDSN(%q) = %v, want %v", c.dsn, got, c.want)
		}
	}
}

func TestDiscoverMigrations(t *testing.T) {
	tmpDir, err := os.MkdirTemp("", "migrations_test")
	if err != nil {
		t.Fatalf("Failed to create temp dir: %v", err)
	}
	defer os.RemoveAll(tmpDir)

	files := []string{
		"002_second.sql",
		"001_first.sql",
		"004_fourth.sql",
		"10_tenth.sql",
		"invalid.sql",
		"003_third.txt", // Wrong extension
	}

	for _, f := range files {
		if err := os.WriteFile(filepath.Join(tmpDir, f), []byte("SELECT 1;"), 0644); err != nil {
			t.Fatalf("Failed to create file %s: %v", f, err)
		}
	}

	migrations, err := discoverMigrations(tmpDir)
	if err != nil {
		t.Fatalf("discoverMigrations failed: %v", err)
	}

	if len(migrations) != 4 {
		t.Fatalf("Expected 4 valid migrations, got %d", len(migrations))
	}

	expectedVersions := []int{1, 2, 4, 10}
	for i, m := range migrations {
		if m.Version != expectedVersions[i] {
			t.Errorf("Migration %d: expected version %d, got %d", i, expectedVersions[i], m.Version)
		}
	}
}

func TestMigrate(t *testing.T) {
	tmpDir, err := os.MkdirTemp("", "db_test")
	if err != nil {
		t.Fatalf("Failed to create temp dir: %v", err)
	}
	defer os.RemoveAll(tmpDir)

	dbPath := filepath.Join(tmpDir, "test.db")
	migrationsDir := filepath.Join(tmpDir, "migrations")

	if err := os.MkdirAll(migrationsDir, 0755); err != nil {
		t.Fatalf("Failed to create migrations dir: %v", err)
	}

	// Create initial schema_version table migration
	schemaSQL := `
CREATE TABLE schema_version (version INTEGER PRIMARY KEY);
INSERT INTO schema_version (version) VALUES (0);
CREATE TABLE test_table (id INTEGER);
`
	if err := os.WriteFile(filepath.Join(migrationsDir, "001_init.sql"), []byte(schemaSQL), 0644); err != nil {
		t.Fatalf("Failed to write migration: %v", err)
	}

	// Create second migration
	updateSQL := `
UPDATE schema_version SET version = 2;
ALTER TABLE test_table ADD COLUMN name TEXT;
`
	if err := os.WriteFile(filepath.Join(migrationsDir, "002_update.sql"), []byte(updateSQL), 0644); err != nil {
		t.Fatalf("Failed to write migration: %v", err)
	}

	database, err := Open(dbPath)
	if err != nil {
		t.Fatalf("Failed to open db: %v", err)
	}
	defer database.Close()

	ctx := context.Background()

	// First pass
	if err := database.Migrate(ctx); err != nil {
		t.Fatalf("Migrate failed: %v", err)
	}

	// Verify
	var count int
	err = database.conn.QueryRowContext(ctx, "SELECT COUNT(*) FROM test_table").Scan(&count)
	if err != nil {
		t.Errorf("Failed to query test_table: %v", err)
	}

	// Second pass should not re-run (or crash)
	if err := database.Migrate(ctx); err != nil {
		t.Fatalf("Second Migrate shouldn't fail: %v", err)
	}
}

// TestMigrateAtomicRollback verifies the D-6 guarantee: a migration that fails
// partway through leaves schema_version unchanged and rolls back every
// statement it had already run, so a corrected re-run applies cleanly instead
// of wedging on "duplicate column name".
func TestMigrateAtomicRollback(t *testing.T) {
	tmpDir, err := os.MkdirTemp("", "db_rollback_test")
	if err != nil {
		t.Fatalf("Failed to create temp dir: %v", err)
	}
	defer os.RemoveAll(tmpDir)

	dbPath := filepath.Join(tmpDir, "test.db")
	migrationsDir := filepath.Join(tmpDir, "migrations")
	if err := os.MkdirAll(migrationsDir, 0755); err != nil {
		t.Fatalf("Failed to create migrations dir: %v", err)
	}

	base := `
CREATE TABLE schema_version (version INTEGER PRIMARY KEY, applied_at INTEGER NOT NULL);
CREATE TABLE t (id INTEGER);
INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (1, 0);
`
	if err := os.WriteFile(filepath.Join(migrationsDir, "001_base.sql"), []byte(base), 0644); err != nil {
		t.Fatalf("Failed to write base migration: %v", err)
	}

	// Migration 002 adds a column (valid) then hits an invalid statement.
	// No BEGIN/COMMIT, so the runner owns the transaction and must roll the
	// ADD COLUMN back when the bad statement fails.
	badSecond := `
ALTER TABLE t ADD COLUMN a INTEGER;
THIS IS NOT VALID SQL;
INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (2, 0);
`
	secondPath := filepath.Join(migrationsDir, "002_second.sql")
	if err := os.WriteFile(secondPath, []byte(badSecond), 0644); err != nil {
		t.Fatalf("Failed to write bad migration: %v", err)
	}

	database, err := Open(dbPath)
	if err != nil {
		t.Fatalf("Failed to open db: %v", err)
	}
	defer database.Close()
	ctx := context.Background()

	if err := database.Migrate(ctx); err == nil {
		t.Fatal("expected Migrate to fail on the invalid statement, got nil")
	}

	// schema_version must still be 1 — migration 002 rolled back entirely.
	var version int
	if err := database.conn.QueryRowContext(ctx, "SELECT COALESCE(MAX(version), 0) FROM schema_version").Scan(&version); err != nil {
		t.Fatalf("query schema_version: %v", err)
	}
	if version != 1 {
		t.Fatalf("expected schema_version 1 after failed migration, got %d", version)
	}

	// The ADD COLUMN must have rolled back too — otherwise a re-run would
	// wedge on "duplicate column name".
	if columnExists(t, database, "t", "a") {
		t.Fatal("column 'a' should not exist after the failed migration rolled back")
	}

	// Correct 002 and re-run: it must now apply cleanly.
	goodSecond := `
ALTER TABLE t ADD COLUMN a INTEGER;
INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (2, 0);
`
	if err := os.WriteFile(secondPath, []byte(goodSecond), 0644); err != nil {
		t.Fatalf("Failed to rewrite migration: %v", err)
	}
	if err := database.Migrate(ctx); err != nil {
		t.Fatalf("re-run after fix should succeed, got: %v", err)
	}
	if err := database.conn.QueryRowContext(ctx, "SELECT COALESCE(MAX(version), 0) FROM schema_version").Scan(&version); err != nil {
		t.Fatalf("query schema_version: %v", err)
	}
	if version != 2 {
		t.Fatalf("expected schema_version 2 after re-run, got %d", version)
	}
	if !columnExists(t, database, "t", "a") {
		t.Fatal("column 'a' should exist after the corrected migration applied")
	}
}

func columnExists(t *testing.T, database *DB, table, column string) bool {
	t.Helper()
	rows, err := database.conn.Query("SELECT name FROM pragma_table_info(?)", table)
	if err != nil {
		t.Fatalf("pragma_table_info(%s): %v", table, err)
	}
	defer rows.Close()
	for rows.Next() {
		var name string
		if err := rows.Scan(&name); err != nil {
			t.Fatalf("scan column name: %v", err)
		}
		if name == column {
			return true
		}
	}
	return false
}
