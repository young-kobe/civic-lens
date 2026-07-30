package runner

import (
	"context"
	"encoding/json"
	"os"
	"testing"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/html"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// pgTestDSNEnv mirrors internal/storage/db's env var of the same name and
// purpose: unset by default so a plain `go test ./...` never accidentally
// writes into a real Postgres instance. Point it at a database that already
// has data/pg-migrations/0001_north_star.sql applied (the Phase 2 task's
// docker-based test flow: spin up postgres:17-alpine, apply the migration,
// run these tests, tear the container down) to exercise the writers below
// against the real target schema.
const pgTestDSNEnv = "CIVIC_TEST_POSTGRES_DSN"

// openTestPostgres returns a *db.DB against the live test instance, or skips
// the test cleanly when CIVIC_TEST_POSTGRES_DSN is unset.
func openTestPostgres(t *testing.T) *db.DB {
	t.Helper()
	dsn := os.Getenv(pgTestDSNEnv)
	if dsn == "" {
		t.Skipf("%s not set; skipping Postgres integration test", pgTestDSNEnv)
	}
	database, err := db.Open(dsn)
	if err != nil {
		t.Fatalf("open postgres: %v", err)
	}
	t.Cleanup(func() { _ = database.Close() })
	return database
}

// TestPostgresArticleUpsertIdempotency drives the real ArticleWriter
// (flush -> flushPostgres) against raw.pages/raw.articles twice for the
// same url_canon and asserts a single row with the second write's fields.
func TestPostgresArticleUpsertIdempotency(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()

	page := &model.Page{URLCanon: "https://pgtest.example.com/article-idem", Domain: "pgtest.example.com"}
	// Row-level cleanup scoped to this test's domain (same convention as the
	// frontier package's cleanupDomains): a leftover queued/inflight pages row
	// breaks the frontier tests' claim-count assertions on a shared instance.
	t.Cleanup(func() {
		_, _ = database.Conn().ExecContext(context.Background(),
			`DELETE FROM raw.articles WHERE domain = 'pgtest.example.com'`)
		_, _ = database.Conn().ExecContext(context.Background(),
			`DELETE FROM raw.pages WHERE domain = 'pgtest.example.com'`)
	})
	meta1 := &html.Metadata{Title: "First title"}
	published := time.Date(2026, 7, 1, 0, 0, 0, 0, time.UTC)
	meta2 := &html.Metadata{Title: "Updated title", PublishedTime: published}

	writeAndFlush(t, database, page, meta1, "HASH1")
	writeAndFlush(t, database, page, meta2, "HASH2")

	var count int
	if err := database.Conn().QueryRowContext(ctx,
		`SELECT COUNT(*) FROM raw.articles WHERE url_canon = $1`, page.URLCanon,
	).Scan(&count); err != nil {
		t.Fatal(err)
	}
	if count != 1 {
		t.Fatalf("want 1 row after two writes, got %d", count)
	}

	var title, hash string
	var publishedAt time.Time
	if err := database.Conn().QueryRowContext(ctx,
		`SELECT title, raw_hash, published_at FROM raw.articles WHERE url_canon = $1`, page.URLCanon,
	).Scan(&title, &hash, &publishedAt); err != nil {
		t.Fatal(err)
	}
	if title != "Updated title" || hash != "HASH2" {
		t.Errorf("row not updated: title=%q hash=%q, want %q/%q", title, hash, "Updated title", "HASH2")
	}
	if !publishedAt.Equal(published) {
		t.Errorf("published_at = %v, want %v", publishedAt, published)
	}
}

// TestPostgresXPostAndUserUpsert exercises insertPost/insertOfficialPost/
// insertUser against raw.x_posts and raw.x_users: JSONB round-trip on
// context_annotations, real BOOLEAN columns
// (is_official_tier/verified), and the I-6 semantic — a topic-query upsert
// must not clear a tier flag an officials-pass upsert set.
func TestPostgresXPostAndUserUpsert(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()
	xr := &XRunner{app: &app.App{Database: database}}

	post := model.XPost{
		TweetID:                "pg-test-tweet-1",
		AuthorID:               "pg-test-author-1",
		CreatedAt:              time.Date(2026, 6, 1, 12, 0, 0, 0, time.UTC).Unix(),
		FetchedAt:              time.Now().Unix(),
		Text:                   "first version",
		ContextAnnotationsJSON: `[{"domain":{"id":"1"}}]`,
		RawHash:                "h1",
		ExtractionVersion:      "1.0",
	}
	if err := xr.insertPost(ctx, post); err != nil {
		t.Fatalf("insert: %v", err)
	}

	post.Text = "second version"
	post.ContextAnnotationsJSON = `[{"domain":{"id":"2"}}]`
	if err := xr.insertOfficialPost(ctx, post); err != nil {
		t.Fatalf("insert official: %v", err)
	}

	var text string
	var official bool
	var annotations []byte
	if err := database.Conn().QueryRowContext(ctx,
		`SELECT text, is_official_tier, context_annotations FROM raw.x_posts WHERE tweet_id = $1`,
		post.TweetID,
	).Scan(&text, &official, &annotations); err != nil {
		t.Fatal(err)
	}
	if text != "second version" {
		t.Errorf("text = %q, want %q", text, "second version")
	}
	if !official {
		t.Error("is_official_tier = false, want true after officials-pass upsert")
	}
	var decoded []map[string]any
	if err := json.Unmarshal(annotations, &decoded); err != nil {
		t.Fatalf("context_annotations not valid JSON: %v", err)
	}
	if len(decoded) != 1 {
		t.Fatalf("want 1 annotation, got %d", len(decoded))
	}

	// I-6 in Postgres: the topic-query path must not clear the tier flag.
	post.Text = "topic pass overlap"
	if err := xr.insertPost(ctx, post); err != nil {
		t.Fatalf("insert topic overlap: %v", err)
	}
	if err := database.Conn().QueryRowContext(ctx,
		`SELECT is_official_tier FROM raw.x_posts WHERE tweet_id = $1`, post.TweetID,
	).Scan(&official); err != nil {
		t.Fatal(err)
	}
	if !official {
		t.Error("topic-path upsert cleared is_official_tier — I-6 regression in the Postgres path")
	}

	user := model.XUser{
		UserID:         "pg-test-user-1",
		Username:       "PGTestUser",
		Verified:       true,
		Protected:      false,
		FollowersCount: 10,
		FetchedAt:      time.Now().Unix(),
		RawHash:        "uh1",
	}
	if err := xr.insertUser(ctx, user); err != nil {
		t.Fatalf("insert user: %v", err)
	}
	user.Verified = false
	user.FollowersCount = 20
	if err := xr.insertUser(ctx, user); err != nil {
		t.Fatalf("insert user update: %v", err)
	}

	var verified bool
	var followers int
	if err := database.Conn().QueryRowContext(ctx,
		`SELECT verified, followers_count FROM raw.x_users WHERE user_id = $1`, user.UserID,
	).Scan(&verified, &followers); err != nil {
		t.Fatal(err)
	}
	if verified {
		t.Error("verified = true, want false after update")
	}
	if followers != 20 {
		t.Errorf("followers_count = %d, want 20", followers)
	}
}

// TestPostgresXBudgetUpsertIncrement asserts ops.x_api_budget accumulates
// across two Record calls and persists across tracker instances, in terms
// of deltas (rather than an absolute total) so the test is safe to re-run
// against a non-empty month row.
func TestPostgresXBudgetUpsertIncrement(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()

	budget, err := NewXBudgetTracker(ctx, database, 0)
	if err != nil {
		t.Fatalf("new tracker: %v", err)
	}
	before := budget.Summary()

	if err := budget.Record(ctx, 25, 18); err != nil {
		t.Fatalf("record 1: %v", err)
	}
	if err := budget.Record(ctx, 10, 7); err != nil {
		t.Fatalf("record 2: %v", err)
	}
	after := budget.Summary()

	if got, want := after.PostCount-before.PostCount, 35; got != want {
		t.Errorf("post_count delta = %d, want %d", got, want)
	}
	if got, want := after.UserCount-before.UserCount, 25; got != want {
		t.Errorf("user_count delta = %d, want %d", got, want)
	}
	if got, want := after.RequestCount-before.RequestCount, 2; got != want {
		t.Errorf("request_count delta = %d, want %d", got, want)
	}
	if got, want := after.EstimatedCents-before.EstimatedCents, 43; got != want {
		t.Errorf("estimated_cents delta = %d, want %d", got, want)
	}

	budget2, err := NewXBudgetTracker(ctx, database, 0)
	if err != nil {
		t.Fatalf("second tracker: %v", err)
	}
	if budget2.Summary().EstimatedCents != after.EstimatedCents {
		t.Errorf("second tracker lost state: got %d, want %d", budget2.Summary().EstimatedCents, after.EstimatedCents)
	}
}

// TestPostgresRedditPostInsert exercises the production dispatch method
// (insertPost -> insertPostPostgres) against raw.reddit_posts: insert twice
// with the same fullname, assert one row with the second write's fields.
func TestPostgresRedditPostInsert(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()
	rr := &RedditRunner{app: &app.App{Database: database}}

	post := model.RedditPost{
		Fullname:          "t3_pgtest1",
		Subreddit:         "politics",
		CreatedUTC:        time.Date(2026, 5, 1, 0, 0, 0, 0, time.UTC).Unix(),
		FetchedAt:         time.Now().Unix(),
		Title:             "first title",
		Body:              "first body",
		Score:             10,
		NumComments:       2,
		RawHash:           "rh1",
		ExtractionVersion: "1.0",
	}
	if err := rr.insertPost(ctx, post); err != nil {
		t.Fatalf("insert: %v", err)
	}

	post.Title = "updated title"
	post.Score = 20
	if err := rr.insertPost(ctx, post); err != nil {
		t.Fatalf("insert update: %v", err)
	}

	var count int
	if err := database.Conn().QueryRowContext(ctx,
		`SELECT COUNT(*) FROM raw.reddit_posts WHERE fullname = $1`, post.Fullname,
	).Scan(&count); err != nil {
		t.Fatal(err)
	}
	if count != 1 {
		t.Fatalf("want 1 row, got %d", count)
	}

	var title string
	var score int
	if err := database.Conn().QueryRowContext(ctx,
		`SELECT title, score FROM raw.reddit_posts WHERE fullname = $1`, post.Fullname,
	).Scan(&title, &score); err != nil {
		t.Fatal(err)
	}
	if title != "updated title" || score != 20 {
		t.Errorf("row not updated: title=%q score=%d", title, score)
	}
}
