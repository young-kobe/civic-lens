package frontier

import (
	"context"
	"os"
	"path/filepath"
	"sync"
	"testing"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// newTestFrontier spins up a temp SQLite DB, copies migrations from the
// project root, and returns a migrated Frontier plus a cleanup func.
func newTestFrontier(t *testing.T, maxRetries int) (*Frontier, *db.DB, func()) {
	t.Helper()

	tmpFile, err := os.CreateTemp("", "frontier_test_*.db")
	if err != nil {
		t.Fatal(err)
	}
	tmpFile.Close()

	database, err := db.Open(tmpFile.Name())
	if err != nil {
		os.Remove(tmpFile.Name())
		t.Fatal(err)
	}

	projectRoot, _ := filepath.Abs("../../..")
	srcMigrationsDir := filepath.Join(projectRoot, "data", "migrations")
	migrationsDir := filepath.Join(filepath.Dir(tmpFile.Name()), "migrations")
	os.MkdirAll(migrationsDir, 0755)

	files, _ := os.ReadDir(srcMigrationsDir)
	for _, f := range files {
		if filepath.Ext(f.Name()) == ".sql" {
			content, _ := os.ReadFile(filepath.Join(srcMigrationsDir, f.Name()))
			os.WriteFile(filepath.Join(migrationsDir, f.Name()), content, 0644)
		}
	}

	ctx := context.Background()
	if err := database.Migrate(ctx); err != nil {
		database.Close()
		os.Remove(tmpFile.Name())
		os.RemoveAll(migrationsDir)
		t.Fatal(err)
	}

	cleanup := func() {
		database.Close()
		os.Remove(tmpFile.Name())
		os.RemoveAll(migrationsDir)
	}

	return New(database, maxRetries), database, cleanup
}

func TestFrontierBasicOperations(t *testing.T) {
	f, _, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()

	// Test PushLinks - should add unique URLs
	stats1, err := f.PushLinks(ctx, []string{
		"https://example.com/page1",
		"https://example.com/page2",
	}, 5)
	if err != nil {
		t.Fatal(err)
	}
	if stats1.Added != 2 {
		t.Errorf("PushLinks added %d, want 2", stats1.Added)
	}

	// Test duplicate rejection
	stats2, err := f.PushLinks(ctx, []string{
		"https://example.com/page1", // duplicate
		"https://example.com/page3",
	}, 5)
	if err != nil {
		t.Fatal(err)
	}
	if stats2.Added != 1 {
		t.Errorf("PushLinks added %d, want 1 (should reject duplicate)", stats2.Added)
	}

	// Test ClaimItems
	items, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatal(err)
	}
	if len(items) != 3 {
		t.Errorf("ClaimItems got %d, want 3", len(items))
	}

	// Verify state transition
	for _, item := range items {
		if item.State != model.StateInflight {
			t.Errorf("Claimed item state = %d, want INFLIGHT", item.State)
		}
	}

	// ClaimItems again should return nothing (all inflight)
	items2, err := f.ClaimItems(ctx, 10)
	if err != nil {
		t.Fatal(err)
	}
	if len(items2) != 0 {
		t.Errorf("ClaimItems second call got %d, want 0", len(items2))
	}

	// Test MarkDone
	items[0].HTTPStatus = 200
	items[0].ContentSHA256 = "abc123"
	if err := f.MarkDone(ctx, items[0]); err != nil {
		t.Fatal(err)
	}

	// Test MarkFailed with retry
	if err := f.MarkFailed(ctx, items[1], "timeout", false); err != nil {
		t.Fatal(err)
	}

	// Test Stats
	queued, inflight, done, failed, err := f.Stats(ctx)
	if err != nil {
		t.Fatal(err)
	}
	// 1 done, 1 queued (retry), 1 inflight
	if done != 1 {
		t.Errorf("Stats done = %d, want 1", done)
	}
	if queued != 1 {
		t.Errorf("Stats queued = %d, want 1", queued)
	}
	if inflight != 1 {
		t.Errorf("Stats inflight = %d, want 1", inflight)
	}
	if failed != 0 {
		t.Errorf("Stats failed = %d, want 0", failed)
	}
}

func TestFrontierRecoverStale(t *testing.T) {
	f, database, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()

	// Add and claim items
	f.PushLinks(ctx, []string{"https://example.com/stale"}, 0)
	items, _ := f.ClaimItems(ctx, 1)
	if len(items) != 1 {
		t.Fatal("Expected 1 claimed item")
	}

	// Manually set inflight_at to 1 hour ago
	oldTime := time.Now().Add(-1 * time.Hour).Unix()
	database.Conn().ExecContext(ctx, "UPDATE pages SET inflight_at = ? WHERE url_canon = ?",
		oldTime, items[0].URLCanon)

	// Recover stale with 10 minute threshold
	recovered, err := f.RecoverStale(ctx, 10*time.Minute)
	if err != nil {
		t.Fatal(err)
	}
	if recovered != 1 {
		t.Errorf("RecoverStale recovered %d, want 1", recovered)
	}

	// Item should be claimable again
	items2, _ := f.ClaimItems(ctx, 1)
	if len(items2) != 1 {
		t.Errorf("After recovery, ClaimItems got %d, want 1", len(items2))
	}
}

// TestFrontierMarkFailedPermanent asserts that permanent=true moves the
// page straight to StateFailed regardless of retries remaining.
func TestFrontierMarkFailedPermanent(t *testing.T) {
	f, database, cleanup := newTestFrontier(t, 5)
	defer cleanup()

	ctx := context.Background()
	f.PushLinks(ctx, []string{"https://example.com/perm"}, 0)
	items, _ := f.ClaimItems(ctx, 1)
	if len(items) != 1 {
		t.Fatal("expected 1 claimed item")
	}

	if err := f.MarkFailed(ctx, items[0], "403 forbidden", true); err != nil {
		t.Fatal(err)
	}

	var state int
	var errMsg string
	row := database.Conn().QueryRowContext(ctx, `SELECT state, last_error FROM pages WHERE url_canon = ?`, items[0].URLCanon)
	if err := row.Scan(&state, &errMsg); err != nil {
		t.Fatal(err)
	}
	if model.PageState(state) != model.StateFailed {
		t.Errorf("state = %d, want StateFailed", state)
	}
	if errMsg != "403 forbidden" {
		t.Errorf("last_error = %q, want %q", errMsg, "403 forbidden")
	}
}

// TestFrontierMarkFailedRetryBackoff asserts that a retryable failure
// schedules next_fetch_at into the future with exponential backoff.
func TestFrontierMarkFailedRetryBackoff(t *testing.T) {
	f, database, cleanup := newTestFrontier(t, 5)
	defer cleanup()

	ctx := context.Background()
	f.PushLinks(ctx, []string{"https://example.com/retry"}, 0)
	items, _ := f.ClaimItems(ctx, 1)

	// After 2 prior retries, next backoff should be 1<<2 = 4 minutes.
	items[0].Retries = 2
	before := time.Now().Unix()
	if err := f.MarkFailed(ctx, items[0], "timeout", false); err != nil {
		t.Fatal(err)
	}

	var state int
	var retries int
	var nextFetch int64
	row := database.Conn().QueryRowContext(ctx, `SELECT state, retries, next_fetch_at FROM pages WHERE url_canon = ?`, items[0].URLCanon)
	if err := row.Scan(&state, &retries, &nextFetch); err != nil {
		t.Fatal(err)
	}
	if model.PageState(state) != model.StateQueued {
		t.Errorf("state = %d, want StateQueued", state)
	}
	// The SQL expression `retries = retries + 1` increments the DB value
	// (starting at 0 for a freshly-pushed row), not the in-memory struct,
	// so we only assert it got bumped at least once.
	if retries < 1 {
		t.Errorf("retries = %d, want >= 1 (incremented)", retries)
	}
	// Backoff uses page.Retries=2 => 1<<2 = 4 minutes.
	minExpected := before + int64((4 * time.Minute).Seconds()) - 2
	if nextFetch < minExpected {
		t.Errorf("next_fetch_at = %d, want >= %d (backoff respected)", nextFetch, minExpected)
	}
}

// TestFrontierMarkFailedExhaustsRetries asserts that reaching the retry
// ceiling promotes the failure to permanent without the caller asking.
func TestFrontierMarkFailedExhaustsRetries(t *testing.T) {
	f, database, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()
	f.PushLinks(ctx, []string{"https://example.com/exhausted"}, 0)
	items, _ := f.ClaimItems(ctx, 1)

	items[0].Retries = 3 // already at ceiling
	if err := f.MarkFailed(ctx, items[0], "still failing", false); err != nil {
		t.Fatal(err)
	}

	var state int
	row := database.Conn().QueryRowContext(ctx, `SELECT state FROM pages WHERE url_canon = ?`, items[0].URLCanon)
	if err := row.Scan(&state); err != nil {
		t.Fatal(err)
	}
	if model.PageState(state) != model.StateFailed {
		t.Errorf("state = %d, want StateFailed (retries exhausted)", state)
	}
}

// TestFrontierConcurrentMarkDone exercises parallel MarkDone calls to
// confirm the state transition tolerates the worker-pool pattern used
// by the crawler.
func TestFrontierConcurrentMarkDone(t *testing.T) {
	f, _, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()
	var urls []string
	for i := 0; i < 20; i++ {
		urls = append(urls, "https://example.com/concurrent/"+itoa(i))
	}
	f.PushLinks(ctx, urls, 0)
	items, err := f.ClaimItems(ctx, 20)
	if err != nil {
		t.Fatal(err)
	}
	if len(items) != 20 {
		t.Fatalf("claimed %d, want 20", len(items))
	}

	var wg sync.WaitGroup
	for _, it := range items {
		wg.Add(1)
		go func(p *model.Page) {
			defer wg.Done()
			p.HTTPStatus = 200
			p.ContentSHA256 = "deadbeef"
			if err := f.MarkDone(ctx, p); err != nil {
				t.Errorf("MarkDone: %v", err)
			}
		}(it)
	}
	wg.Wait()

	_, _, done, _, err := f.Stats(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if done != 20 {
		t.Errorf("done = %d, want 20 after concurrent MarkDone", done)
	}
}

// TestFrontierPushLinksMalformed asserts that bad URLs are counted
// separately from DB errors instead of being silently swallowed.
func TestFrontierPushLinksMalformed(t *testing.T) {
	f, _, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()
	stats, err := f.PushLinks(ctx, []string{
		"https://example.com/good",
		"http://[::1:bad",          // malformed — unclosed bracket in host
		"http://%ZZ.example.com",   // malformed — invalid percent-escape
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

// TestMarkDoneUsesFrontierKey asserts MarkDone transitions the row that was
// actually claimed to DONE. Regression for I-1: the crawler used to overwrite
// page.URLCanon with the page-declared <link rel="canonical"> before MarkDone,
// so the UPDATE matched the wrong key (or none) and the fetched row stayed
// INFLIGHT, to be recovered and refetched forever.
func TestMarkDoneUsesFrontierKey(t *testing.T) {
	f, database, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()
	f.PushLinks(ctx, []string{"https://example.com/article?utm_source=x"}, 0)
	items, _ := f.ClaimItems(ctx, 1)
	if len(items) != 1 {
		t.Fatal("expected 1 claimed item")
	}
	items[0].HTTPStatus = 200
	items[0].ContentSHA256 = "abc123"
	if err := f.MarkDone(ctx, items[0]); err != nil {
		t.Fatalf("MarkDone: %v", err)
	}

	var state int
	row := database.Conn().QueryRowContext(ctx, `SELECT state FROM pages WHERE url_canon = ?`, items[0].URLCanon)
	if err := row.Scan(&state); err != nil {
		t.Fatal(err)
	}
	if model.PageState(state) != model.StateDone {
		t.Errorf("state = %d, want StateDone", state)
	}
}

// TestMarkDoneWrongKeyIsLoud asserts that a MarkDone whose key does not match
// a live claimed row returns an error instead of silently updating nothing.
// Regression for I-1b (zero-row UPDATE used to be swallowed) and the reason
// the crawler must not mutate the frontier key: a mutated key lands here as a
// loud error, and the real claimed row is left untouched rather than clobbered.
func TestMarkDoneWrongKeyIsLoud(t *testing.T) {
	f, database, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()
	f.PushLinks(ctx, []string{"https://example.com/real"}, 0)
	items, _ := f.ClaimItems(ctx, 1)
	if len(items) != 1 {
		t.Fatal("expected 1 claimed item")
	}

	// Simulate the old bug: key swapped to the page-declared canonical.
	stuck := *items[0]
	stuck.URLCanon = "https://example.com/declared-canonical"
	stuck.HTTPStatus = 200
	if err := f.MarkDone(ctx, &stuck); err == nil {
		t.Fatal("MarkDone with a non-matching key should error, got nil")
	}

	var state int
	row := database.Conn().QueryRowContext(ctx, `SELECT state FROM pages WHERE url_canon = ?`, items[0].URLCanon)
	if err := row.Scan(&state); err != nil {
		t.Fatal(err)
	}
	if model.PageState(state) != model.StateInflight {
		t.Errorf("claimed row state = %d, want StateInflight (untouched)", state)
	}
}

// TestMarkDoneStaleReclaimGuard asserts a worker whose row was recovered and
// re-claimed by another worker cannot clobber the new claim. Regression for
// I-7: MarkDone/MarkFailed guard on (state = INFLIGHT AND inflight_at = the
// timestamp this worker claimed at), so a late completion from the original
// worker no-ops loudly instead of flipping the re-claimed row.
func TestMarkDoneStaleReclaimGuard(t *testing.T) {
	f, database, cleanup := newTestFrontier(t, 3)
	defer cleanup()

	ctx := context.Background()
	f.PushLinks(ctx, []string{"https://example.com/race"}, 0)
	itemsA, _ := f.ClaimItems(ctx, 1)
	if len(itemsA) != 1 {
		t.Fatal("expected 1 claimed item")
	}
	a := itemsA[0] // worker A's claim, inflight_at = a.InflightAt

	// Worker B re-claims after a stale recovery. We simulate B's fresh claim
	// by advancing inflight_at on the row while it stays INFLIGHT.
	newClaim := a.InflightAt + 100
	if _, err := database.Conn().ExecContext(ctx,
		`UPDATE pages SET inflight_at = ? WHERE url_canon = ?`, newClaim, a.URLCanon); err != nil {
		t.Fatal(err)
	}

	// Worker A finishes late and tries to mark done under its stale claim.
	a.HTTPStatus = 200
	a.ContentSHA256 = "stale"
	if err := f.MarkDone(ctx, a); err == nil {
		t.Fatal("stale MarkDone should error under the claim guard, got nil")
	}

	var state int
	var inflight int64
	row := database.Conn().QueryRowContext(ctx, `SELECT state, inflight_at FROM pages WHERE url_canon = ?`, a.URLCanon)
	if err := row.Scan(&state, &inflight); err != nil {
		t.Fatal(err)
	}
	if model.PageState(state) != model.StateInflight || inflight != newClaim {
		t.Errorf("row clobbered: state=%d inflight_at=%d, want INFLIGHT under B's claim (%d)", state, inflight, newClaim)
	}
}

// itoa is a tiny helper to avoid pulling in strconv at the test site.
func itoa(n int) string {
	if n == 0 {
		return "0"
	}
	var buf [20]byte
	i := len(buf)
	neg := n < 0
	if neg {
		n = -n
	}
	for n > 0 {
		i--
		buf[i] = byte('0' + n%10)
		n /= 10
	}
	if neg {
		i--
		buf[i] = '-'
	}
	return string(buf[i:])
}
