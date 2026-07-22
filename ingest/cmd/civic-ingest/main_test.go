package main

import "testing"

// TestDefaultDBPath pins the --db default precedence: CIVIC_DATABASE_URL,
// when set, must win over the SQLite default so ops can point the binary at
// Postgres purely via env (matching the Python side), without every
// deployment having to remember to pass --db explicitly.
func TestDefaultDBPath(t *testing.T) {
	t.Run("falls back to sqlite path when unset", func(t *testing.T) {
		t.Setenv("CIVIC_DATABASE_URL", "")
		if got := defaultDBPath(); got != "data/civic_lens.db" {
			t.Errorf("defaultDBPath() = %q, want %q", got, "data/civic_lens.db")
		}
	})

	t.Run("prefers CIVIC_DATABASE_URL when set", func(t *testing.T) {
		const dsn = "postgres://user:pass@localhost:5432/civic_lens"
		t.Setenv("CIVIC_DATABASE_URL", dsn)
		if got := defaultDBPath(); got != dsn {
			t.Errorf("defaultDBPath() = %q, want %q", got, dsn)
		}
	})
}
