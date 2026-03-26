package db

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

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
