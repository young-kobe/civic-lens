package db

import (
	"os"
	"path/filepath"
	"testing"
)

// TestIsPostgresDSN pins the DSN-scheme rule Open() rejects non-Postgres
// DSNs on: only postgres://... and postgresql://... are accepted, so a path
// change here is never accidental — every other string (including a bare
// "file:" DSN) must be rejected.
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

// TestOpenRejectsNonPostgresDSN asserts Open() fails loudly on a DSN that is
// not postgres:// or postgresql:// rather than silently falling back to
// another backend.
func TestOpenRejectsNonPostgresDSN(t *testing.T) {
	cases := []string{
		"data/civic_lens.db",
		"/abs/path/civic_lens.db",
		"",
	}
	for _, dsn := range cases {
		if _, err := Open(dsn); err == nil {
			t.Errorf("Open(%q) = nil error, want a loud rejection", dsn)
		}
	}
}
