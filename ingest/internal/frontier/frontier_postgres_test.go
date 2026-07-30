package frontier

import (
	"context"
	"fmt"
	"os"
	"sync"
	"testing"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// pgTestDSNEnv names the env var that opts these tests into a real Postgres
// instance — the same variable internal/storage/db's gated test uses, kept
// as a local copy since it's unexported there. Unset by default, so
// CI-less local runs and CI without a Postgres service both skip cleanly.
const pgTestDSNEnv = "CIVIC_TEST_POSTGRES_DSN"

// rawFixtureSQL idempotently ensures the raw.pages / raw.articles slice of
// the north-star schema (see data/pg-migrations/0001_north_star.sql) that
// the frontier package reads and writes exists. It only ever creates —
// never drops — because raw.pages/raw.articles are shared production
// tables: internal/runner's extraction writer has its own gated Postgres
// tests against the same tables, and both suites may run concurrently
// against one live instance (`go test ./...` with CIVIC_TEST_POSTGRES_DSN
// set runs packages in parallel). A DROP here would be a frontier test
// reaching outside its own ownership and clobbering a sibling package's
// fixture mid-run. The enum type uses a DO block because Postgres has no
// `CREATE TYPE ... IF NOT EXISTS`.
const rawFixtureSQL = `
CREATE SCHEMA IF NOT EXISTS raw;
DO $$ BEGIN
	CREATE TYPE raw.page_state AS ENUM ('queued', 'inflight', 'done', 'failed');
EXCEPTION WHEN duplicate_object THEN NULL;
END $$;
CREATE TABLE IF NOT EXISTS raw.pages (
	url_canon TEXT PRIMARY KEY,
	url_raw TEXT NOT NULL,
	domain TEXT NOT NULL,
	state raw.page_state NOT NULL DEFAULT 'queued',
	priority INTEGER NOT NULL DEFAULT 0,
	retries INTEGER NOT NULL DEFAULT 0,
	next_fetch_at TIMESTAMPTZ NOT NULL DEFAULT to_timestamp(0),
	inflight_at TIMESTAMPTZ NOT NULL DEFAULT to_timestamp(0),
	http_status INTEGER,
	content_sha256 TEXT,
	etag TEXT,
	last_modified TEXT,
	last_error TEXT
);
CREATE TABLE IF NOT EXISTS raw.articles (
	url_canon TEXT PRIMARY KEY REFERENCES raw.pages (url_canon),
	domain TEXT,
	fetched_at TIMESTAMPTZ NOT NULL,
	published_at TIMESTAMPTZ,
	title TEXT,
	raw_hash TEXT NOT NULL,
	extraction_version TEXT NOT NULL
);
`

// newTestFrontierPostgres connects to CIVIC_TEST_POSTGRES_DSN, ensures the
// raw fixture exists (see rawFixtureSQL), and returns a Frontier plus a
// cleanup func that only closes the connection — row-level cleanup for the
// specific domains a test used is the caller's job (see cleanupDomains) so
// concurrently-running sibling packages' rows are never touched. Skips the
// calling test cleanly when the env var is unset.
func newTestFrontierPostgres(t *testing.T, maxRetries int, quota *DomainQuota) (*Frontier, *db.DB, func()) {
	t.Helper()
	dsn := os.Getenv(pgTestDSNEnv)
	if dsn == "" {
		t.Skipf("%s not set; skipping Postgres frontier test", pgTestDSNEnv)
	}

	database, err := db.Open(dsn)
	if err != nil {
		t.Fatalf("Open(%q): %v", dsn, err)
	}

	ctx := context.Background()
	if _, err := database.Conn().ExecContext(ctx, rawFixtureSQL); err != nil {
		database.Close()
		t.Fatalf("ensure raw fixture schema: %v", err)
	}

	return New(database, maxRetries, quota), database, func() { database.Close() }
}

// cleanupDomains deletes every raw.pages/raw.articles row for the given
// domains. Tests call this both before and after their body — before, so
// a prior interrupted run's leftovers cannot cause a spurious PushLinks
// ON CONFLICT no-op; after, so the next run (or the next test in this
// package) starts clean. Scoped by domain rather than a table-wide
// TRUNCATE/DROP so sibling packages' concurrent rows are never touched.
func cleanupDomains(database *db.DB, domains ...string) {
	ctx := context.Background()
	for _, d := range domains {
		database.Conn().ExecContext(ctx, "DELETE FROM raw.articles WHERE domain = $1", d)
		database.Conn().ExecContext(ctx, "DELETE FROM raw.pages WHERE domain = $1", d)
	}
}

// seedDoneArticle inserts a raw.pages row already in 'done' state plus its
// matching raw.articles row, for tests that need pre-existing "already
// fetched" counts to feed the balance-quota window count.
func seedDoneArticle(t *testing.T, database *db.DB, urlCanon, domain string, fetchedAt time.Time) {
	t.Helper()
	ctx := context.Background()
	if _, err := database.Conn().ExecContext(ctx, `
		INSERT INTO raw.pages (url_canon, url_raw, domain, state)
		VALUES ($1, $1, $2, 'done'::raw.page_state)
	`, urlCanon, domain); err != nil {
		t.Fatalf("seed done page %s: %v", urlCanon, err)
	}
	if _, err := database.Conn().ExecContext(ctx, `
		INSERT INTO raw.articles (url_canon, domain, fetched_at, raw_hash, extraction_version)
		VALUES ($1, $2, $3, 'deadbeef', 'test-v1')
	`, urlCanon, domain, fetchedAt); err != nil {
		t.Fatalf("seed article %s: %v", urlCanon, err)
	}
}

func pageState(t *testing.T, database *db.DB, urlCanon string) string {
	t.Helper()
	var state string
	row := database.Conn().QueryRowContext(context.Background(),
		"SELECT state::text FROM raw.pages WHERE url_canon = $1", urlCanon)
	if err := row.Scan(&state); err != nil {
		t.Fatalf("query state for %s: %v", urlCanon, err)
	}
	return state
}

// TestPostgresFrontierEnumStateMachine exercises the full
// queued -> inflight -> done/failed state machine against a real Postgres
// enum column.
func TestPostgresFrontierEnumStateMachine(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	// Stats() counts the whole (shared, unfiltered) raw.pages table by
	// design. Against a live shared Postgres instance other rows legitimately
	// exist (other tests in this suite, other packages' own gated tests), so
	// this asserts the delta this test's own actions produce rather than an
	// absolute count.
	baseQueued, baseInflight, baseDone, baseFailed, err := f.Stats(ctx)
	if err != nil {
		t.Fatalf("baseline Stats: %v", err)
	}

	stats, err := f.PushLinks(ctx, []string{
		"https://example.com/page1",
		"https://example.com/page2",
		"https://example.com/page3",
	}, 5)
	if err != nil {
		t.Fatalf("PushLinks: %v", err)
	}
	if stats.Added != 3 {
		t.Fatalf("Added = %d, want 3", stats.Added)
	}

	items, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatalf("ClaimItems: %v", err)
	}
	if len(items) != 3 {
		t.Fatalf("ClaimItems got %d, want 3", len(items))
	}
	for _, p := range items {
		if got := pageState(t, database, p.URLCanon); got != "inflight" {
			t.Errorf("state for %s = %q, want inflight", p.URLCanon, got)
		}
	}

	items[0].HTTPStatus = 200
	items[0].ContentSHA256 = "abc123"
	if err := f.MarkDone(ctx, items[0]); err != nil {
		t.Fatalf("MarkDone: %v", err)
	}
	if got := pageState(t, database, items[0].URLCanon); got != "done" {
		t.Errorf("state after MarkDone = %q, want done", got)
	}

	if err := f.MarkFailed(ctx, items[1], "timeout", false); err != nil {
		t.Fatalf("MarkFailed (retryable): %v", err)
	}
	if got := pageState(t, database, items[1].URLCanon); got != "queued" {
		t.Errorf("state after retryable MarkFailed = %q, want queued", got)
	}

	if err := f.MarkFailed(ctx, items[2], "403 forbidden", true); err != nil {
		t.Fatalf("MarkFailed (permanent): %v", err)
	}
	if got := pageState(t, database, items[2].URLCanon); got != "failed" {
		t.Errorf("state after permanent MarkFailed = %q, want failed", got)
	}

	queued, inflight, done, failed, err := f.Stats(ctx)
	if err != nil {
		t.Fatalf("Stats: %v", err)
	}
	dq, di, dd, df := queued-baseQueued, inflight-baseInflight, done-baseDone, failed-baseFailed
	if dq != 1 || di != 0 || dd != 1 || df != 1 {
		t.Errorf("Stats delta = (q=%d i=%d d=%d f=%d), want (1,0,1,1)", dq, di, dd, df)
	}
}

// TestPostgresFrontierInflightReset asserts the A3 crash-recovery invariant
// (INFLIGHT -> QUEUED past staleAge) holds on the Postgres backend too.
func TestPostgresFrontierInflightReset(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	f.PushLinks(ctx, []string{"https://example.com/stale"}, 0)
	items, err := f.ClaimItems(ctx, 1)
	if err != nil || len(items) != 1 {
		t.Fatalf("ClaimItems: %v (len=%d)", err, len(items))
	}

	oldTime := time.Now().Add(-1 * time.Hour).UTC()
	if _, err := database.Conn().ExecContext(ctx,
		"UPDATE raw.pages SET inflight_at = $1 WHERE url_canon = $2", oldTime, items[0].URLCanon,
	); err != nil {
		t.Fatalf("backdate inflight_at: %v", err)
	}

	recovered, err := f.RecoverStale(ctx, 10*time.Minute)
	if err != nil {
		t.Fatalf("RecoverStale: %v", err)
	}
	if recovered != 1 {
		t.Fatalf("RecoverStale recovered %d, want 1", recovered)
	}
	if got := pageState(t, database, items[0].URLCanon); got != "queued" {
		t.Errorf("state after RecoverStale = %q, want queued", got)
	}

	items2, err := f.ClaimItems(ctx, 1)
	if err != nil || len(items2) != 1 {
		t.Fatalf("re-claim after recovery: err=%v len=%d", err, len(items2))
	}
}

// TestPostgresFrontierSkipLockedNeverBlocks pins the exact mechanism behind
// the plan's marquee concurrency win: FOR UPDATE SKIP LOCKED must skip a
// row locked by another in-flight transaction rather than wait for it, and
// must never return that row while the lock holds. This is deterministic
// (unlike a pure goroutine race) because the lock is held open manually.
func TestPostgresFrontierSkipLockedNeverBlocks(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	f.PushLinks(ctx, []string{
		"https://example.com/locked",
		"https://example.com/free",
	}, 0)

	tx, err := database.Conn().BeginTx(ctx, nil)
	if err != nil {
		t.Fatalf("BeginTx: %v", err)
	}
	defer tx.Rollback()

	var locked string
	row := tx.QueryRowContext(ctx,
		"SELECT url_canon FROM raw.pages WHERE url_canon = $1 FOR UPDATE", "https://example.com/locked")
	if err := row.Scan(&locked); err != nil {
		t.Fatalf("lock row: %v", err)
	}

	claimCtx, cancel := context.WithTimeout(ctx, 5*time.Second)
	defer cancel()
	items, err := f.ClaimItems(claimCtx, 10)
	if err != nil {
		t.Fatalf("ClaimItems while a row is locked (should skip, not block/error): %v", err)
	}
	if len(items) != 1 || items[0].URLCanon != "https://example.com/free" {
		t.Fatalf("ClaimItems while locked returned %+v, want only the free row", items)
	}

	if err := tx.Rollback(); err != nil {
		t.Fatalf("rollback: %v", err)
	}
	items2, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatalf("ClaimItems after unlock: %v", err)
	}
	if len(items2) != 1 || items2[0].URLCanon != "https://example.com/locked" {
		t.Fatalf("ClaimItems after unlock returned %+v, want the previously-locked row", items2)
	}
}

// TestPostgresFrontierClaimExclusivityUnderConcurrency exercises real
// concurrent claimers (separate goroutines drawing from the connection
// pool) to confirm no two of them ever receive the same row — the
// aggregate correctness property the SKIP LOCKED claim query exists to
// guarantee, on top of the deterministic single-lock proof above.
func TestPostgresFrontierClaimExclusivityUnderConcurrency(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	const total = 60
	links := make([]string, total)
	for i := 0; i < total; i++ {
		links[i] = fmt.Sprintf("https://example.com/concurrent/%d", i)
	}
	if _, err := f.PushLinks(ctx, links, 0); err != nil {
		t.Fatalf("PushLinks: %v", err)
	}

	var (
		mu      sync.Mutex
		claimed = map[string]int{}
		wg      sync.WaitGroup
	)
	const workers = 6
	for w := 0; w < workers; w++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for {
				items, err := f.ClaimItems(ctx, 5)
				if err != nil {
					t.Errorf("ClaimItems: %v", err)
					return
				}
				if len(items) == 0 {
					return
				}
				mu.Lock()
				for _, p := range items {
					claimed[p.URLCanon]++
				}
				mu.Unlock()
			}
		}()
	}
	wg.Wait()

	if len(claimed) != total {
		t.Fatalf("claimed %d distinct rows, want %d", len(claimed), total)
	}
	for url, n := range claimed {
		if n != 1 {
			t.Errorf("row %s claimed %d times, want exactly 1", url, n)
		}
	}
}

// TestPostgresFrontierQuotaDefersCappedDomain asserts a domain at or over
// its window cap is excluded from claiming entirely, even when it has
// queued work, while an under-cap domain's queued work is unaffected.
func TestPostgresFrontierQuotaDefersCappedDomain(t *testing.T) {
	quota := &DomainQuota{
		Window:              time.Hour,
		DefaultMaxPerWindow: 100,
		PerDomain:           map[string]int{"capped.example.com": 2},
	}
	f, database, cleanup := newTestFrontierPostgres(t, 3, quota)
	defer cleanup()
	cleanupDomains(database, "capped.example.com", "open.example.com")
	defer cleanupDomains(database, "capped.example.com", "open.example.com")
	ctx := context.Background()

	now := time.Now().UTC()
	seedDoneArticle(t, database, "https://capped.example.com/done1", "capped.example.com", now)
	seedDoneArticle(t, database, "https://capped.example.com/done2", "capped.example.com", now)

	if _, err := f.PushLinks(ctx, []string{
		"https://capped.example.com/queued1",
		"https://capped.example.com/queued2",
		"https://open.example.com/queued1",
		"https://open.example.com/queued2",
	}, 0); err != nil {
		t.Fatalf("PushLinks: %v", err)
	}

	items, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatalf("ClaimItems: %v", err)
	}
	if len(items) != 2 {
		t.Fatalf("claimed %d items, want 2 (capped domain deferred entirely)", len(items))
	}
	for _, p := range items {
		if p.Domain != "open.example.com" {
			t.Errorf("claimed %s (domain %s), want only open.example.com rows", p.URLCanon, p.Domain)
		}
	}
}

// TestPostgresFrontierQuotaPrefersUnderRepresentedDomain asserts that, among
// domains both under cap, the claim query prefers the one furthest below its
// quota — the mechanism that fixes the production cbsnews-vs-npr skew.
func TestPostgresFrontierQuotaPrefersUnderRepresentedDomain(t *testing.T) {
	quota := &DomainQuota{
		Window:              time.Hour,
		DefaultMaxPerWindow: 10,
	}
	f, database, cleanup := newTestFrontierPostgres(t, 3, quota)
	defer cleanup()
	cleanupDomains(database, "prolific.example.com", "quiet.example.com")
	defer cleanupDomains(database, "prolific.example.com", "quiet.example.com")
	ctx := context.Background()

	now := time.Now().UTC()
	for i := 0; i < 8; i++ {
		seedDoneArticle(t, database,
			fmt.Sprintf("https://prolific.example.com/done%d", i), "prolific.example.com", now)
	}

	if _, err := f.PushLinks(ctx, []string{
		"https://prolific.example.com/queued1",
		"https://quiet.example.com/queued1",
	}, 5); err != nil {
		t.Fatalf("PushLinks: %v", err)
	}

	items, err := f.ClaimItems(ctx, 1)
	if err != nil {
		t.Fatalf("ClaimItems: %v", err)
	}
	if len(items) != 1 {
		t.Fatalf("claimed %d items, want 1", len(items))
	}
	if items[0].Domain != "quiet.example.com" {
		t.Errorf("claimed domain = %s, want quiet.example.com (further under its quota)", items[0].Domain)
	}
}

// TestPostgresFrontierNoQuota_Unlimited asserts a nil DomainQuota (no
// crawl_balance section) means every queued row across every domain is
// claimable.
func TestPostgresFrontierNoQuota_Unlimited(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "a.example.com", "b.example.com")
	defer cleanupDomains(database, "a.example.com", "b.example.com")
	ctx := context.Background()

	if _, err := f.PushLinks(ctx, []string{
		"https://a.example.com/1", "https://a.example.com/2", "https://b.example.com/1",
	}, 0); err != nil {
		t.Fatalf("PushLinks: %v", err)
	}
	items, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatalf("ClaimItems: %v", err)
	}
	if len(items) != 3 {
		t.Fatalf("claimed %d, want 3 (no quota configured => unlimited)", len(items))
	}
}

// TestPostgresFrontierPushLinksMalformed asserts that bad URLs are counted
// separately from DB errors instead of being silently swallowed.
func TestPostgresFrontierPushLinksMalformed(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	stats, err := f.PushLinks(ctx, []string{
		"https://example.com/good",
		"http://[::1:bad",        // malformed — unclosed bracket in host
		"http://%ZZ.example.com", // malformed — invalid percent-escape
	}, 0)
	if err != nil {
		t.Fatal(err)
	}
	if stats.Added != 1 {
		t.Errorf("Added = %d, want 1", stats.Added)
	}
	if stats.Malformed != 2 {
		t.Errorf("Malformed = %d, want 2", stats.Malformed)
	}
	if stats.DBErrors != 0 {
		t.Errorf("DBErrors = %d, want 0", stats.DBErrors)
	}
}

// TestPostgresFrontierMarkDoneWrongKeyIsLoud asserts that a MarkDone whose
// key does not match a live claimed row returns an error instead of
// silently updating nothing. Regression for I-1b (zero-row UPDATE used to
// be swallowed) and the reason the crawler must not mutate the frontier
// key: a mutated key lands here as a loud error, and the real claimed row
// is left untouched rather than clobbered.
func TestPostgresFrontierMarkDoneWrongKeyIsLoud(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	f.PushLinks(ctx, []string{"https://example.com/real"}, 0)
	items, err := f.ClaimItems(ctx, 1)
	if err != nil || len(items) != 1 {
		t.Fatalf("ClaimItems: %v (len=%d)", err, len(items))
	}

	// Simulate the old bug: key swapped to the page-declared canonical.
	stuck := *items[0]
	stuck.URLCanon = "https://example.com/declared-canonical"
	stuck.HTTPStatus = 200
	if err := f.MarkDone(ctx, &stuck); err == nil {
		t.Fatal("MarkDone with a non-matching key should error, got nil")
	}

	if got := pageState(t, database, items[0].URLCanon); got != "inflight" {
		t.Errorf("claimed row state = %q, want inflight (untouched)", got)
	}
}

// TestPostgresFrontierMarkDoneStaleReclaimGuard asserts a worker whose row
// was recovered and re-claimed by another worker cannot clobber the new
// claim. Regression for I-7: MarkDone/MarkFailed guard on (state = INFLIGHT
// AND inflight_at = the timestamp this worker claimed at), so a late
// completion from the original worker no-ops loudly instead of flipping the
// re-claimed row.
func TestPostgresFrontierMarkDoneStaleReclaimGuard(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	f.PushLinks(ctx, []string{"https://example.com/race"}, 0)
	itemsA, err := f.ClaimItems(ctx, 1)
	if err != nil || len(itemsA) != 1 {
		t.Fatalf("ClaimItems: %v (len=%d)", err, len(itemsA))
	}
	a := itemsA[0] // worker A's claim, inflight_at = a.InflightAt

	// Worker B re-claims after a stale recovery. We simulate B's fresh claim
	// by advancing inflight_at on the row while it stays INFLIGHT.
	newClaim := time.Unix(a.InflightAt+100, 0).UTC()
	if _, err := database.Conn().ExecContext(ctx,
		`UPDATE raw.pages SET inflight_at = $1 WHERE url_canon = $2`, newClaim, a.URLCanon); err != nil {
		t.Fatal(err)
	}

	// Worker A finishes late and tries to mark done under its stale claim.
	a.HTTPStatus = 200
	a.ContentSHA256 = "stale"
	if err := f.MarkDone(ctx, a); err == nil {
		t.Fatal("stale MarkDone should error under the claim guard, got nil")
	}

	if got := pageState(t, database, a.URLCanon); got != "inflight" {
		t.Errorf("row clobbered: state=%q, want inflight under B's claim", got)
	}
}

// TestPostgresFrontierMarkFailedExhaustsRetries asserts that reaching the
// retry ceiling promotes the failure to permanent without the caller asking.
func TestPostgresFrontierMarkFailedExhaustsRetries(t *testing.T) {
	f, database, cleanup := newTestFrontierPostgres(t, 3, nil)
	defer cleanup()
	cleanupDomains(database, "example.com")
	defer cleanupDomains(database, "example.com")
	ctx := context.Background()

	f.PushLinks(ctx, []string{"https://example.com/exhausted"}, 0)
	items, err := f.ClaimItems(ctx, 1)
	if err != nil || len(items) != 1 {
		t.Fatalf("ClaimItems: %v (len=%d)", err, len(items))
	}

	items[0].Retries = 3 // already at ceiling
	if err := f.MarkFailed(ctx, items[0], "still failing", false); err != nil {
		t.Fatal(err)
	}

	if got := pageState(t, database, items[0].URLCanon); got != "failed" {
		t.Errorf("state = %q, want failed (retries exhausted)", got)
	}
}
