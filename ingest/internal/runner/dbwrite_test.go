package runner

import (
	"context"
	"database/sql"
	"testing"

	_ "modernc.org/sqlite"
)

// newXPostsDB creates an in-memory DB with a minimal x_posts_raw table that
// carries the is_official_tier provenance column (default 0).
func newXPostsDB(t *testing.T) *sql.DB {
	t.Helper()
	db, err := sql.Open("sqlite", ":memory:")
	if err != nil {
		t.Fatalf("open: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })
	if _, err := db.Exec(`
		CREATE TABLE x_posts_raw (
			tweet_id         TEXT PRIMARY KEY,
			text             TEXT,
			raw_hash         TEXT,
			is_official_tier INTEGER NOT NULL DEFAULT 0
		);`); err != nil {
		t.Fatalf("create table: %v", err)
	}
	return db
}

// TestUpsertRowOnConflict_PreservesOfficialTier is the I-6 regression: a
// topic-query insert (which does not list is_official_tier) must NOT erase the
// flag set by a prior officials-pass insert. INSERT OR REPLACE used to delete
// and re-insert the row, resetting the column to its default.
func TestUpsertRowOnConflict_PreservesOfficialTier(t *testing.T) {
	db := newXPostsDB(t)
	ctx := context.Background()

	// Officials pass wrote the tweet with is_official_tier = 1.
	if _, err := db.ExecContext(ctx,
		`INSERT INTO x_posts_raw (tweet_id, text, raw_hash, is_official_tier) VALUES ('t1', 'from officials', 'h0', 1)`); err != nil {
		t.Fatal(err)
	}

	// Topic query matches the same tweet minutes later; it does not touch tier.
	if err := upsertRowOnConflict(ctx, db, "x_posts_raw", "tweet_id",
		[]string{"tweet_id", "text", "raw_hash"},
		[]any{"t1", "from topic query", "h1"}); err != nil {
		t.Fatalf("upsert: %v", err)
	}

	var tier int
	var text, hash string
	if err := db.QueryRowContext(ctx,
		`SELECT is_official_tier, text, raw_hash FROM x_posts_raw WHERE tweet_id = 't1'`).Scan(&tier, &text, &hash); err != nil {
		t.Fatal(err)
	}
	if tier != 1 {
		t.Errorf("is_official_tier = %d, want 1 (preserved)", tier)
	}
	// The rest of the row is still refreshed by the topic pass.
	if text != "from topic query" || hash != "h1" {
		t.Errorf("row not updated: text=%q hash=%q", text, hash)
	}
}

// TestUpsertRowOnConflict_NewRowGetsDefaultTier asserts a brand-new topic-only
// tweet lands with the column default (0), i.e. untagged.
func TestUpsertRowOnConflict_NewRowGetsDefaultTier(t *testing.T) {
	db := newXPostsDB(t)
	ctx := context.Background()

	if err := upsertRowOnConflict(ctx, db, "x_posts_raw", "tweet_id",
		[]string{"tweet_id", "text", "raw_hash"},
		[]any{"t2", "topic only", "h2"}); err != nil {
		t.Fatalf("upsert: %v", err)
	}

	var tier int
	if err := db.QueryRowContext(ctx,
		`SELECT is_official_tier FROM x_posts_raw WHERE tweet_id = 't2'`).Scan(&tier); err != nil {
		t.Fatal(err)
	}
	if tier != 0 {
		t.Errorf("is_official_tier = %d, want 0 (default)", tier)
	}
}

// TestSafePrefixHandlesShortString is the I-3 panic guard: a failed
// RawStore.Store now yields an empty hash string, and the raw-JSON log line
// must not panic on it (the old hash[:8] did).
func TestSafePrefixHandlesShortString(t *testing.T) {
	if got := safePrefix("", 8); got != "" {
		t.Errorf("safePrefix(\"\", 8) = %q, want empty", got)
	}
	if got := safePrefix("abc", 8); got != "abc" {
		t.Errorf("safePrefix(\"abc\", 8) = %q, want abc", got)
	}
	if got := safePrefix("abcdefghij", 8); got != "abcdefgh" {
		t.Errorf("safePrefix truncation = %q, want abcdefgh", got)
	}
}
