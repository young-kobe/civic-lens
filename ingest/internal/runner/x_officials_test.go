package runner

import (
	"context"
	"database/sql"
	"testing"

	_ "modernc.org/sqlite"
)

// newOfficialsTestDB sets up only the x_users_raw + x_posts_raw bits the
// officials cache lookup and post insert need. We don't pull in the full
// migration runner so the test stays hermetic.
func newOfficialsTestDB(t *testing.T) *sql.DB {
	t.Helper()
	db, err := sql.Open("sqlite", ":memory:")
	if err != nil {
		t.Fatalf("open: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })
	if _, err := db.Exec(`
		CREATE TABLE x_users_raw (
			user_id TEXT PRIMARY KEY,
			username TEXT,
			name TEXT,
			location TEXT,
			description TEXT,
			created_at INTEGER,
			followers_count INTEGER,
			following_count INTEGER,
			tweet_count INTEGER,
			listed_count INTEGER,
			verified INTEGER,
			verified_type TEXT,
			profile_image_url TEXT,
			protected INTEGER,
			fetched_at INTEGER,
			raw_hash TEXT
		);
		CREATE TABLE x_posts_raw (
			tweet_id TEXT PRIMARY KEY,
			author_id TEXT,
			conversation_id TEXT,
			created_at INTEGER,
			fetched_at INTEGER,
			text TEXT,
			lang TEXT,
			retweet_count INTEGER,
			reply_count INTEGER,
			like_count INTEGER,
			quote_count INTEGER,
			place_id TEXT,
			place_country_code TEXT,
			place_full_name TEXT,
			context_annotations_json TEXT,
			in_reply_to_user_id TEXT,
			referenced_tweet_id TEXT,
			referenced_tweet_type TEXT,
			raw_hash TEXT,
			extraction_version TEXT,
			is_official_tier INTEGER NOT NULL DEFAULT 0
		);
	`); err != nil {
		t.Fatalf("schema: %v", err)
	}
	return db
}

func TestLookupCachedUserID_ReturnsCachedRow(t *testing.T) {
	db := newOfficialsTestDB(t)
	ctx := context.Background()

	if _, err := db.ExecContext(ctx,
		`INSERT INTO x_users_raw (user_id, username) VALUES ('123', 'POTUS')`,
	); err != nil {
		t.Fatalf("seed: %v", err)
	}

	got, err := lookupCachedUserID(ctx, db, "potus")
	if err != nil {
		t.Fatalf("lookup: %v", err)
	}
	if got != "123" {
		t.Errorf("want user_id 123, got %q", got)
	}
}

func TestLookupCachedUserID_CaseInsensitiveOnUsername(t *testing.T) {
	db := newOfficialsTestDB(t)
	ctx := context.Background()
	if _, err := db.ExecContext(ctx,
		`INSERT INTO x_users_raw (user_id, username) VALUES ('77', 'SenSchumer')`,
	); err != nil {
		t.Fatal(err)
	}

	for _, h := range []string{"senschumer", "SenSchumer", "SENSCHUMER"} {
		got, err := lookupCachedUserID(ctx, db, h)
		if err != nil {
			t.Fatalf("lookup %q: %v", h, err)
		}
		if got != "77" {
			t.Errorf("lookup %q: want 77, got %q", h, got)
		}
	}
}

func TestLookupCachedUserID_MissingReturnsEmpty(t *testing.T) {
	db := newOfficialsTestDB(t)
	got, err := lookupCachedUserID(context.Background(), db, "nobody")
	if err != nil {
		t.Fatalf("lookup: %v", err)
	}
	if got != "" {
		t.Errorf("want empty, got %q", got)
	}
}

// TestInsertOfficialPost verifies the column list lines up with the schema
// and that is_official_tier=1 actually lands in the row. A mismatch in the
// column list (column added to schema but not the insert) would silently
// leave is_official_tier at the default — exactly the failure mode where a
// downstream "tier=officials" filter would quietly miss every official post.
func TestInsertOfficialPost_TagsRowAsOfficial(t *testing.T) {
	db := newOfficialsTestDB(t)
	ctx := context.Background()

	cols := []string{
		"tweet_id", "author_id", "conversation_id", "created_at", "fetched_at",
		"text", "lang", "retweet_count", "reply_count", "like_count", "quote_count",
		"place_id", "place_country_code", "place_full_name",
		"context_annotations_json", "in_reply_to_user_id",
		"referenced_tweet_id", "referenced_tweet_type",
		"raw_hash", "extraction_version", "is_official_tier",
	}
	vals := []any{
		"t1", "123", "", int64(0), int64(0),
		"hello", "en", 0, 0, 0, 0,
		"", "", "",
		"", "",
		"", "",
		"hash", "1.0", 1,
	}
	if err := upsertRow(ctx, db, "x_posts_raw", cols, vals); err != nil {
		t.Fatalf("upsert: %v", err)
	}

	var flag int
	if err := db.QueryRow(`SELECT is_official_tier FROM x_posts_raw WHERE tweet_id='t1'`).Scan(&flag); err != nil {
		t.Fatalf("query: %v", err)
	}
	if flag != 1 {
		t.Errorf("want is_official_tier=1, got %d", flag)
	}
}
