package runner

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/x"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/rawstore"
)

// This file replaces the SQLite-fixture officials-pass tests deleted in the
// Phase 7 decommission (see docs/audit-trail/ingestion/2026-07-28-delete-go-sqlite-backend.md).
// Every test here is gated on CIVIC_TEST_POSTGRES_DSN via openTestPostgres
// (postgres_integration_test.go) and runs against the real raw.x_users /
// raw.x_posts / ops.x_api_budget tables. Row cleanup is scoped to
// pgtest_official_-prefixed handles/tweet/user IDs so it can never touch
// another test's rows on the shared instance, and every test cleans up both
// before (defensive, in case a previous failed run left rows behind) and
// after via t.Cleanup — the same convention as frontier_postgres_test.go's
// cleanupDomains.

// seedTestXUser inserts a minimal raw.x_users row satisfying every NOT NULL
// column, for the cache-lookup tests below.
func seedTestXUser(t *testing.T, database *db.DB, userID, username string) {
	t.Helper()
	_, err := database.Conn().ExecContext(context.Background(), `
		INSERT INTO raw.x_users (user_id, username, fetched_at, raw_hash)
		VALUES ($1, $2, now(), 'seed-hash')
		ON CONFLICT (user_id) DO UPDATE SET username = excluded.username
	`, userID, username)
	if err != nil {
		t.Fatalf("seed raw.x_users %s: %v", userID, err)
	}
}

// cleanupOfficialsTestRows deletes every raw.x_posts/raw.x_users row for the
// given IDs. NEVER truncate — the instance is shared across packages.
func cleanupOfficialsTestRows(database *db.DB, userIDs, tweetIDs []string) {
	ctx := context.Background()
	for _, id := range tweetIDs {
		_, _ = database.Conn().ExecContext(ctx, `DELETE FROM raw.x_posts WHERE tweet_id = $1`, id)
	}
	for _, id := range userIDs {
		_, _ = database.Conn().ExecContext(ctx, `DELETE FROM raw.x_users WHERE user_id = $1`, id)
	}
}

// countTaggedOfficialRows returns how many of the given tweet IDs currently
// carry is_official_tier = true. Scoped to the caller's own tweet IDs rather
// than a bare COUNT(*) — the shared test instance may carry official-tier
// rows from other tests or from real capture data.
func countTaggedOfficialRows(t *testing.T, xr *XRunner, tweetIDs []string) int {
	t.Helper()
	tagged := 0
	for _, id := range tweetIDs {
		var isOfficial bool
		if err := xr.app.Database.Conn().QueryRowContext(context.Background(),
			`SELECT is_official_tier FROM raw.x_posts WHERE tweet_id = $1`, id,
		).Scan(&isOfficial); err != nil {
			t.Fatalf("query tweet %s: %v", id, err)
		}
		if isOfficial {
			tagged++
		}
	}
	return tagged
}

// newFixedMonthBudgetTracker pins a tracker to a synthetic, far-future
// month_key so budget-ceiling assertions never see spend accumulated by an
// earlier run against the shared test database (ops.x_api_budget is keyed
// on real calendar month, and repeated -count=1 invocations within the same
// month would otherwise carry over estimated_cents from the prior run).
func newFixedMonthBudgetTracker(t *testing.T, database *db.DB, monthKey string, ceilingCents int) *XBudgetTracker {
	t.Helper()
	fakeMonth, err := time.Parse("2006-01", monthKey)
	if err != nil {
		t.Fatalf("parse fixed month %q: %v", monthKey, err)
	}
	fakeNow := func() time.Time { return fakeMonth }

	cleanup := func() {
		_, _ = database.Conn().ExecContext(context.Background(),
			`DELETE FROM ops.x_api_budget WHERE month_key = $1`, monthKey)
	}
	cleanup()
	t.Cleanup(cleanup)

	tracker, err := newXBudgetTracker(context.Background(), database.Conn(), ceilingCents, fakeNow)
	if err != nil {
		t.Fatalf("fixed-month budget tracker: %v", err)
	}
	return tracker
}

// newOfficialsPostgresHarness wires an XRunner against the live Postgres
// test instance, a temp-dir rawstore, and an x.Client pointed at the stub
// server.
func newOfficialsPostgresHarness(t *testing.T, stub *stubXAPI) *XRunner {
	t.Helper()
	database := openTestPostgres(t)

	rs, err := rawstore.New(t.TempDir())
	if err != nil {
		t.Fatalf("rawstore: %v", err)
	}

	client := x.New(x.Config{
		BearerToken:     "test-token",
		UserAgent:       "civic-lens-test",
		MaxRequestsHour: 36000, // effectively unlimited for the test loop
		BaseURL:         stub.server.URL,
	})

	return &XRunner{app: &app.App{Database: database, RawStore: rs}, client: client}
}

// writeOfficialsYAML drops a verified_officials.yaml into a temp dir for
// the test and returns its path.
func writeOfficialsYAML(t *testing.T, body string) string {
	t.Helper()
	path := filepath.Join(t.TempDir(), "officials.yaml")
	if err := os.WriteFile(path, []byte(body), 0o600); err != nil {
		t.Fatalf("write yaml: %v", err)
	}
	return path
}

// ---- stub X API ----

// stubXAPI mocks the two endpoints the officials pass touches. Counters
// live on the struct so tests can assert per-handle call counts — that's
// how the cache-reuse and budget-exhaustion tests verify a handle's
// endpoints were (or were not) hit.
type stubXAPI struct {
	t                *testing.T
	server           *httptest.Server
	userLookupCalls  map[string]*int32 // handle (lowercase) -> call count
	timelineCalls    map[string]*int32 // user_id -> call count
	suspendedHandles map[string]bool
	tweetsByUserID   map[string][]stubTweet
	usernameToID     map[string]string
}

type stubTweet struct {
	ID   string
	Text string
}

func newStubXAPI(t *testing.T) *stubXAPI {
	t.Helper()
	s := &stubXAPI{
		t:                t,
		userLookupCalls:  map[string]*int32{},
		timelineCalls:    map[string]*int32{},
		suspendedHandles: map[string]bool{},
		tweetsByUserID:   map[string][]stubTweet{},
		usernameToID:     map[string]string{},
	}
	mux := http.NewServeMux()
	mux.HandleFunc("/2/users/by/username/", s.handleUserLookup)
	mux.HandleFunc("/2/users/", s.handleTimeline) // catch /2/users/:id/tweets
	s.server = httptest.NewServer(mux)
	t.Cleanup(s.server.Close)
	return s
}

func normalizeStubHandle(h string) string {
	return strings.ToLower(strings.TrimPrefix(strings.TrimSpace(h), "@"))
}

func (s *stubXAPI) registerOfficial(handle, userID string, tweets []stubTweet) {
	lc := normalizeStubHandle(handle)
	s.usernameToID[lc] = userID
	s.tweetsByUserID[userID] = tweets
	if _, ok := s.userLookupCalls[lc]; !ok {
		var n int32
		s.userLookupCalls[lc] = &n
	}
	if _, ok := s.timelineCalls[userID]; !ok {
		var n int32
		s.timelineCalls[userID] = &n
	}
}

func (s *stubXAPI) markSuspended(handle string) {
	lc := normalizeStubHandle(handle)
	s.suspendedHandles[lc] = true
	if _, ok := s.userLookupCalls[lc]; !ok {
		var n int32
		s.userLookupCalls[lc] = &n
	}
}

func (s *stubXAPI) handleUserLookup(w http.ResponseWriter, r *http.Request) {
	parts := strings.Split(strings.TrimPrefix(r.URL.Path, "/2/users/by/username/"), "/")
	handle := strings.ToLower(parts[0])
	if counter, ok := s.userLookupCalls[handle]; ok {
		atomic.AddInt32(counter, 1)
	} else {
		// Track unexpected lookups too — surfacing them helps spot a runner
		// regression that calls the API for the wrong handle.
		var n int32 = 1
		s.userLookupCalls[handle] = &n
	}
	if s.suspendedHandles[handle] {
		w.WriteHeader(http.StatusOK)
		_, _ = w.Write([]byte(`{"errors":[{"detail":"User has been suspended","title":"Suspended"}]}`))
		return
	}
	userID, ok := s.usernameToID[handle]
	if !ok {
		w.WriteHeader(http.StatusNotFound)
		_, _ = w.Write([]byte(`{"errors":[{"detail":"Could not find user","title":"Not Found"}]}`))
		return
	}
	resp := map[string]any{
		"data": map[string]any{
			"id":            userID,
			"username":      handle,
			"name":          handle,
			"verified":      true,
			"verified_type": "government",
		},
	}
	body, _ := json.Marshal(resp)
	w.Header().Set("Content-Type", "application/json")
	_, _ = w.Write(body)
}

func (s *stubXAPI) handleTimeline(w http.ResponseWriter, r *http.Request) {
	// Path shape: /2/users/<id>/tweets
	rest := strings.TrimPrefix(r.URL.Path, "/2/users/")
	id := strings.TrimSuffix(rest, "/tweets")
	if counter, ok := s.timelineCalls[id]; ok {
		atomic.AddInt32(counter, 1)
	}
	tweets := s.tweetsByUserID[id]
	tweetData := make([]map[string]any, 0, len(tweets))
	for _, tw := range tweets {
		tweetData = append(tweetData, map[string]any{
			"id":         tw.ID,
			"text":       tw.Text,
			"author_id":  id,
			"created_at": "2026-04-25T10:00:00.000Z",
			"lang":       "en",
		})
	}
	resp := map[string]any{
		"data": tweetData,
		"meta": map[string]any{"result_count": len(tweets)},
	}
	body, _ := json.Marshal(resp)
	w.Header().Set("Content-Type", "application/json")
	_, _ = w.Write(body)
}

// ---- lookupCachedUserIDPostgres ----

// TestLookupCachedUserIDPostgres_ReturnsCachedRow verifies the cache hit path
// resolveOfficialUserID relies on to skip a billed API lookup on rerun.
func TestLookupCachedUserIDPostgres_ReturnsCachedRow(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()
	const userID = "pgtest-official-cache-uid-1"
	const username = "pgtest_official_cache_potus"

	cleanup := func() {
		_, _ = database.Conn().ExecContext(context.Background(), `DELETE FROM raw.x_users WHERE user_id = $1`, userID)
	}
	cleanup()
	t.Cleanup(cleanup)

	seedTestXUser(t, database, userID, username)

	got, err := lookupCachedUserIDPostgres(ctx, database.Conn(), username)
	if err != nil {
		t.Fatalf("lookup: %v", err)
	}
	if got != userID {
		t.Errorf("want user_id %q, got %q", userID, got)
	}
}

// TestLookupCachedUserIDPostgres_CaseInsensitiveOnUsername matters because
// verified_officials.yaml handles and X API responses vary in case; a
// case-sensitive lookup would silently miss the cache and re-bill the
// lookup endpoint on every rerun.
func TestLookupCachedUserIDPostgres_CaseInsensitiveOnUsername(t *testing.T) {
	database := openTestPostgres(t)
	ctx := context.Background()
	const userID = "pgtest-official-cache-uid-2"
	const username = "PgTestOfficialCaseMixed"

	cleanup := func() {
		_, _ = database.Conn().ExecContext(context.Background(), `DELETE FROM raw.x_users WHERE user_id = $1`, userID)
	}
	cleanup()
	t.Cleanup(cleanup)

	seedTestXUser(t, database, userID, username)

	for _, h := range []string{"pgtestofficialcasemixed", "PgTestOfficialCaseMixed", "PGTESTOFFICIALCASEMIXED"} {
		got, err := lookupCachedUserIDPostgres(ctx, database.Conn(), h)
		if err != nil {
			t.Fatalf("lookup %q: %v", h, err)
		}
		if got != userID {
			t.Errorf("lookup %q: want %q, got %q", h, userID, got)
		}
	}
}

// TestLookupCachedUserIDPostgres_MissingReturnsEmpty verifies a handle never
// fetched before returns an empty ID (not an error) — resolveOfficialUserID
// depends on this to fall through to the API lookup.
func TestLookupCachedUserIDPostgres_MissingReturnsEmpty(t *testing.T) {
	database := openTestPostgres(t)
	got, err := lookupCachedUserIDPostgres(context.Background(), database.Conn(), "pgtest_official_cache_nobody")
	if err != nil {
		t.Fatalf("lookup: %v", err)
	}
	if got != "" {
		t.Errorf("want empty, got %q", got)
	}
}

// ---- runOfficialsPass ----

// TestRunOfficialsPass_SuccessTagsRowsAsOfficial exercises the happy path
// end to end: every post pulled from a verified official's timeline lands in
// raw.x_posts with is_official_tier = true, so downstream tier-routing never
// has to re-classify it.
func TestRunOfficialsPass_SuccessTagsRowsAsOfficial(t *testing.T) {
	stub := newStubXAPI(t)
	stub.registerOfficial("pgtest_official_success_potus", "pgtest-official-success-uid-100", []stubTweet{
		{ID: "pgtest-official-success-t100a", Text: "post one"},
		{ID: "pgtest-official-success-t100b", Text: "post two"},
	})
	stub.registerOfficial("pgtest_official_success_vp", "pgtest-official-success-uid-200", []stubTweet{
		{ID: "pgtest-official-success-t200a", Text: "post three"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "pgtest_official_success_potus"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "pgtest_official_success_vp"
    display_name: V
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
`)

	xr := newOfficialsPostgresHarness(t, stub)
	tweetIDs := []string{"pgtest-official-success-t100a", "pgtest-official-success-t100b", "pgtest-official-success-t200a"}
	userIDs := []string{"pgtest-official-success-uid-100", "pgtest-official-success-uid-200"}
	cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs)
	t.Cleanup(func() { cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs) })

	budget, err := NewXBudgetTracker(context.Background(), xr.app.Database, 0)
	if err != nil {
		t.Fatalf("budget: %v", err)
	}

	res, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5)
	if err != nil {
		t.Fatalf("runOfficialsPass: %v", err)
	}

	if res.HandlesAttempted != 2 || res.HandlesSucceeded != 2 || res.HandlesFailed != 0 {
		t.Errorf("counters: %+v", res)
	}
	if res.PostsIngested != 3 {
		t.Errorf("want 3 posts ingested, got %d", res.PostsIngested)
	}

	if tagged := countTaggedOfficialRows(t, xr, tweetIDs); tagged != 3 {
		t.Errorf("want 3 official-tier rows, got %d", tagged)
	}
}

// TestRunOfficialsPass_FailedHandleDoesNotAbortRun is the per-account
// isolation contract: one suspended/renamed handle must log and continue,
// never short-circuit the remaining handles in the same pass.
func TestRunOfficialsPass_FailedHandleDoesNotAbortRun(t *testing.T) {
	stub := newStubXAPI(t)
	stub.markSuspended("pgtest_official_failed_retired")
	stub.registerOfficial("pgtest_official_failed_potus", "pgtest-official-failed-uid-100", []stubTweet{
		{ID: "pgtest-official-failed-t100a", Text: "still posting"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "pgtest_official_failed_retired"
    display_name: F
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "pgtest_official_failed_potus"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
`)

	xr := newOfficialsPostgresHarness(t, stub)
	tweetIDs := []string{"pgtest-official-failed-t100a"}
	userIDs := []string{"pgtest-official-failed-uid-100"}
	cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs)
	t.Cleanup(func() { cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs) })

	budget, err := NewXBudgetTracker(context.Background(), xr.app.Database, 0)
	if err != nil {
		t.Fatalf("budget: %v", err)
	}

	res, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5)
	if err != nil {
		t.Fatalf("runOfficialsPass: %v", err)
	}

	// One failed (suspended), one succeeded — the failure must not have
	// short-circuited the loop.
	if res.HandlesFailed != 1 {
		t.Errorf("want 1 failed handle, got %d (full: %+v)", res.HandlesFailed, res)
	}
	if res.HandlesSucceeded != 1 {
		t.Errorf("want 1 succeeded handle despite earlier failure, got %d (full: %+v)", res.HandlesSucceeded, res)
	}
	if res.PostsIngested != 1 {
		t.Errorf("want 1 post, got %d", res.PostsIngested)
	}
}

// TestRunOfficialsPass_UsesCachedUserIDOnRerun verifies the budget-saving
// cache contract: once a handle's user_id lands in raw.x_users, a second
// pass must skip the billed user-by-username lookup entirely and only hit
// the timeline endpoint.
func TestRunOfficialsPass_UsesCachedUserIDOnRerun(t *testing.T) {
	stub := newStubXAPI(t)
	stub.registerOfficial("pgtest_official_rerun_potus", "pgtest-official-rerun-uid-100", []stubTweet{
		{ID: "pgtest-official-rerun-t100a", Text: "post"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "pgtest_official_rerun_potus"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
`)

	xr := newOfficialsPostgresHarness(t, stub)
	tweetIDs := []string{"pgtest-official-rerun-t100a"}
	userIDs := []string{"pgtest-official-rerun-uid-100"}
	cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs)
	t.Cleanup(func() { cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs) })

	budget, err := NewXBudgetTracker(context.Background(), xr.app.Database, 0)
	if err != nil {
		t.Fatalf("budget: %v", err)
	}

	if _, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5); err != nil {
		t.Fatalf("first pass: %v", err)
	}
	if got := atomic.LoadInt32(stub.userLookupCalls["pgtest_official_rerun_potus"]); got != 1 {
		t.Fatalf("first pass lookup count: want 1, got %d", got)
	}

	// Second pass: raw.x_users now has the user_id, so the runner must use
	// the cache and skip the user-by-username endpoint entirely.
	if _, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5); err != nil {
		t.Fatalf("second pass: %v", err)
	}
	if got := atomic.LoadInt32(stub.userLookupCalls["pgtest_official_rerun_potus"]); got != 1 {
		t.Errorf("second pass should be cache-only; lookup count rose to %d", got)
	}
	// Timeline endpoint, by contrast, is hit every pass — the budget
	// tracker is the only thing keeping that bounded.
	if got := atomic.LoadInt32(stub.timelineCalls["pgtest-official-rerun-uid-100"]); got != 2 {
		t.Errorf("want 2 timeline calls (one per pass), got %d", got)
	}
}

// TestRunOfficialsPass_BudgetExhaustionSkipsRemainingHandles verifies the
// budget guard fires cleanly: once the ceiling is reached, remaining
// handles are reported Skipped (never Failed) and their API endpoints are
// never called at all.
func TestRunOfficialsPass_BudgetExhaustionSkipsRemainingHandles(t *testing.T) {
	stub := newStubXAPI(t)
	stub.registerOfficial("pgtest_official_budget_potus", "pgtest-official-budget-uid-100", []stubTweet{
		{ID: "pgtest-official-budget-t100a", Text: "post a"},
	})
	stub.registerOfficial("pgtest_official_budget_vp", "pgtest-official-budget-uid-200", []stubTweet{
		{ID: "pgtest-official-budget-t200a", Text: "post b"},
	})
	stub.registerOfficial("pgtest_official_budget_speaker", "pgtest-official-budget-uid-300", []stubTweet{
		{ID: "pgtest-official-budget-t300a", Text: "should not be fetched"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "pgtest_official_budget_potus"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "pgtest_official_budget_vp"
    display_name: V
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "pgtest_official_budget_speaker"
    display_name: S
    party: R
    term_start: "2023-10-25"
    bio_source: "x"
`)

	xr := newOfficialsPostgresHarness(t, stub)
	// Speaker's tweet/user IDs are deliberately absent from this cleanup
	// list: the guard must never insert them in the first place.
	tweetIDs := []string{"pgtest-official-budget-t100a", "pgtest-official-budget-t200a"}
	userIDs := []string{"pgtest-official-budget-uid-100", "pgtest-official-budget-uid-200"}
	cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs)
	t.Cleanup(func() { cleanupOfficialsTestRows(xr.app.Database, userIDs, tweetIDs) })

	// Budget math (centsPerPost=5, centsPerUser=10, centsConversionUnit=10;
	// costs are always rounded UP): each handle here has exactly 1 tweet
	// and no expansion users, so its lookup (0 posts, 1 user) costs
	// ceilDiv(10,10)=1c and its timeline pull (1 post, 0 users) costs
	// ceilDiv(5,10)=1c — 2c per handle. POTUS + VP together cost exactly
	// 4c, which is also the ceiling: by the time the loop reaches Speaker,
	// OverBudget() is already true at the TOP of the loop (before any
	// call), so Speaker's endpoints must never be hit at all.
	budget := newFixedMonthBudgetTracker(t, xr.app.Database, "2199-08", 4)

	res, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5)
	if err != nil {
		t.Fatalf("runOfficialsPass: %v", err)
	}

	if res.HandlesSucceeded != 2 {
		t.Errorf("want 2 succeeded handles (POTUS, VP), got %d (full: %+v)", res.HandlesSucceeded, res)
	}
	if res.HandlesFailed != 0 {
		t.Errorf("budget exhaustion must be reported as Skipped, never Failed; got %d failed (full: %+v)", res.HandlesFailed, res)
	}
	if res.HandlesSkipped != 1 {
		t.Errorf("want 1 skipped handle (Speaker), got %d (full: %+v)", res.HandlesSkipped, res)
	}
	if got := atomic.LoadInt32(stub.userLookupCalls["pgtest_official_budget_speaker"]); got != 0 {
		t.Errorf("Speaker lookup must not have been called after budget exhaust; got %d", got)
	}
	if got := atomic.LoadInt32(stub.timelineCalls["pgtest-official-budget-uid-300"]); got != 0 {
		t.Errorf("Speaker timeline must not have been called after budget exhaust; got %d", got)
	}
	// Final budget must not have drifted past the ceiling by more than one
	// in-flight call's worth — the guard is supposed to stop at-or-before
	// the ceiling, not run away.
	final := budget.Summary()
	if final.EstimatedCents < final.CeilingCents {
		t.Errorf("budget should be at-or-past ceiling after the guard fired; got %d / %d",
			final.EstimatedCents, final.CeilingCents)
	}
}
