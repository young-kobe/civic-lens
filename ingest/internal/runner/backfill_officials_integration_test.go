package runner

import (
	"context"
	"fmt"
	"testing"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// TestPostgresLoadActiveOfficials exercises loadActiveOfficials against a
// real corpus.entities table: seeds one editorial official, one promoted
// official, and one inactive official, and asserts the query returns only
// the two active rows with the correct editorial flag — the exact contract
// RunBackfillOfficials depends on to pick a target per official.
func TestPostgresLoadActiveOfficials(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()

	seedTestEntity(t, database, "bf-editorial-handle", "official", true, true)
	seedTestEntity(t, database, "bf-promoted-handle", "official", false, true)
	seedTestEntity(t, database, "bf-inactive-handle", "official", true, false)
	// A non-official kind row must never surface here.
	seedTestEntity(t, database, "bf-outlet.example.com", "outlet", true, true)

	rows, err := loadActiveOfficials(ctx, database.Conn())
	if err != nil {
		t.Fatalf("loadActiveOfficials: %v", err)
	}

	byHandle := make(map[string]bool) // handle -> editorial
	present := make(map[string]bool)
	for _, r := range rows {
		byHandle[r.Handle] = r.Editorial
		present[r.Handle] = true
	}

	if !present["bf-editorial-handle"] || !byHandle["bf-editorial-handle"] {
		t.Errorf("bf-editorial-handle missing or editorial=false: present=%v editorial=%v",
			present["bf-editorial-handle"], byHandle["bf-editorial-handle"])
	}
	if !present["bf-promoted-handle"] || byHandle["bf-promoted-handle"] {
		t.Errorf("bf-promoted-handle missing or editorial=true: present=%v editorial=%v",
			present["bf-promoted-handle"], byHandle["bf-promoted-handle"])
	}
	if present["bf-inactive-handle"] {
		t.Error("bf-inactive-handle (active=false) must not be returned")
	}
	if present["bf-outlet.example.com"] {
		t.Error("kind=outlet row must not be returned by an official-scoped query")
	}
}

// TestPostgresCountStoredPosts_ResumeState verifies the resumability
// contract directly: countStoredPosts reflects however many raw.x_posts
// rows currently carry an author_id, so a second backfill-officials
// invocation against the same database sees the first run's progress
// without any separate checkpoint table.
func TestPostgresCountStoredPosts_ResumeState(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()
	// Unique per test run (not just per-test-file-run): this test executes
	// against a persistent shared DSN, not a scratch database, so a fixed
	// author/tweet ID would silently no-op on ON CONFLICT DO NOTHING the
	// second time this test runs against the same server and the delta
	// assertion below would read 0 instead of 2.
	suffix := time.Now().UnixNano()
	authorID := fmt.Sprintf("bf-resume-author-%d", suffix)

	before, err := countStoredPosts(ctx, database.Conn(), authorID)
	if err != nil {
		t.Fatalf("countStoredPosts (before): %v", err)
	}

	seedTestXPost(t, database, fmt.Sprintf("bf-resume-tweet-%d-a", suffix), authorID)
	seedTestXPost(t, database, fmt.Sprintf("bf-resume-tweet-%d-b", suffix), authorID)

	after, err := countStoredPosts(ctx, database.Conn(), authorID)
	if err != nil {
		t.Fatalf("countStoredPosts (after): %v", err)
	}
	if got, want := after-before, 2; got != want {
		t.Errorf("stored-post count delta = %d, want %d — resume state would drift", got, want)
	}

	// A distinct author_id must not see this author's posts — countStoredPosts
	// is scoped per-official, not global.
	unrelated, err := countStoredPosts(ctx, database.Conn(), "bf-resume-author-unrelated")
	if err != nil {
		t.Fatalf("countStoredPosts (unrelated): %v", err)
	}
	if unrelated != 0 {
		t.Errorf("unrelated author_id count = %d, want 0", unrelated)
	}
}

// seedTestEntity inserts a minimal corpus.entities row. ON CONFLICT DO
// NOTHING keyed on entity_key so a rerun of this test against a database
// that already has the row (unlikely given the bf- prefixed test handles,
// but cheap insurance) does not fail on the UNIQUE constraint.
func seedTestEntity(t *testing.T, database *db.DB, entityKey, kind string, editorial, active bool) {
	t.Helper()
	_, err := database.Conn().ExecContext(context.Background(), `
		INSERT INTO corpus.entities (entity_key, kind, display_name, active, editorial)
		VALUES ($1, $2::corpus.entity_kind, $1, $3, $4)
		ON CONFLICT (entity_key) DO NOTHING
	`, entityKey, kind, active, editorial)
	if err != nil {
		t.Fatalf("seed corpus.entities %s: %v", entityKey, err)
	}
}

// seedTestXPost inserts a minimal raw.x_posts row for the given tweetID/
// authorID, satisfying every NOT NULL column.
func seedTestXPost(t *testing.T, database *db.DB, tweetID, authorID string) {
	t.Helper()
	now := time.Now().UTC()
	_, err := database.Conn().ExecContext(context.Background(), `
		INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, raw_hash, extraction_version)
		VALUES ($1, $2, $3, $3, 'seed text', 'seed-hash', '1.0')
		ON CONFLICT (tweet_id) DO NOTHING
	`, tweetID, authorID, now)
	if err != nil {
		t.Fatalf("seed raw.x_posts %s: %v", tweetID, err)
	}
}
