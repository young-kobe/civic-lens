package main

import "testing"

// TestDefaultDBPath pins the --db default: it reads CIVIC_DATABASE_URL
// verbatim (empty when unset) so ops can point the binary at Postgres purely
// via env (matching the Python side), without every deployment having to
// remember to pass --db explicitly. An empty result is not defaulted
// further here — db.Open rejects it loudly instead of guessing a DSN.
func TestDefaultDBPath(t *testing.T) {
	t.Run("empty when unset", func(t *testing.T) {
		t.Setenv("CIVIC_DATABASE_URL", "")
		if got := defaultDBPath(); got != "" {
			t.Errorf("defaultDBPath() = %q, want empty", got)
		}
	})

	t.Run("reads CIVIC_DATABASE_URL when set", func(t *testing.T) {
		const dsn = "postgres://user:pass@localhost:5432/civic_lens"
		t.Setenv("CIVIC_DATABASE_URL", dsn)
		if got := defaultDBPath(); got != dsn {
			t.Errorf("defaultDBPath() = %q, want %q", got, dsn)
		}
	})
}
