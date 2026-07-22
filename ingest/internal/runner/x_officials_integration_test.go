package runner

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"testing"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/x"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/rawstore"
)

// stubXAPI mocks the two endpoints the officials pass touches.
// Counters live on the struct so tests can assert per-handle call counts —
// that's how we verify the cache prevents a redundant lookup on rerun.
type stubXAPI struct {
	t                *testing.T
	server           *httptest.Server
	userLookupCalls  map[string]*int32 // handle (lowercase) → call count
	timelineCalls    map[string]*int32 // user_id → call count
	suspendedHandles map[string]bool
	notFoundUserIDs  map[string]bool
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
		notFoundUserIDs:  map[string]bool{},
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
	if s.notFoundUserIDs[id] {
		w.WriteHeader(http.StatusNotFound)
		_, _ = w.Write([]byte(`{"errors":[{"detail":"User not found"}]}`))
		return
	}
	tweets, _ := s.tweetsByUserID[id]
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

// minimalXSchema applies only the tables runOfficialsPass touches.
// Avoids pulling in the migration runner so the test stays hermetic.
const minimalXSchema = `
CREATE TABLE schema_version (version INTEGER PRIMARY KEY, applied_at INTEGER);
CREATE TABLE x_api_budget (
    month_key TEXT PRIMARY KEY,
    post_count INTEGER NOT NULL DEFAULT 0,
    user_count INTEGER NOT NULL DEFAULT 0,
    request_count INTEGER NOT NULL DEFAULT 0,
    estimated_cents INTEGER NOT NULL DEFAULT 0,
    last_updated INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE x_users_raw (
    user_id TEXT PRIMARY KEY,
    username TEXT, name TEXT, location TEXT, description TEXT,
    created_at INTEGER, followers_count INTEGER, following_count INTEGER,
    tweet_count INTEGER, listed_count INTEGER,
    verified INTEGER, verified_type TEXT,
    profile_image_url TEXT, protected INTEGER,
    fetched_at INTEGER, raw_hash TEXT
);
CREATE TABLE x_posts_raw (
    tweet_id TEXT PRIMARY KEY,
    author_id TEXT, conversation_id TEXT,
    created_at INTEGER, fetched_at INTEGER,
    text TEXT, lang TEXT,
    retweet_count INTEGER, reply_count INTEGER,
    like_count INTEGER, quote_count INTEGER,
    place_id TEXT, place_country_code TEXT, place_full_name TEXT,
    context_annotations_json TEXT, in_reply_to_user_id TEXT,
    referenced_tweet_id TEXT, referenced_tweet_type TEXT,
    raw_hash TEXT, extraction_version TEXT,
    is_official_tier INTEGER NOT NULL DEFAULT 0
);`

// newOfficialsRunnerHarness wires up everything runOfficialsPass needs:
// a real *db.DB with the minimal schema, a real rawstore at a temp dir,
// an x.Client pointed at the stub server, and an XRunner constructed
// against an &app.App carrying both. Returns the XRunner plus the budget
// tracker so tests can assert post-run counters and seed pre-conditions.
func newOfficialsRunnerHarness(t *testing.T, stub *stubXAPI, ceilingCents int) (*XRunner, *XBudgetTracker) {
	t.Helper()
	dbPath := filepath.Join(t.TempDir(), "test.db")
	d, err := db.Open(dbPath)
	if err != nil {
		t.Fatalf("db open: %v", err)
	}
	t.Cleanup(func() { _ = d.Close() })
	if _, err := d.Conn().ExecContext(context.Background(), minimalXSchema); err != nil {
		t.Fatalf("schema: %v", err)
	}

	rawDir := t.TempDir()
	rs, err := rawstore.New(rawDir)
	if err != nil {
		t.Fatalf("rawstore: %v", err)
	}

	client := x.New(x.Config{
		BearerToken:     "test-token",
		UserAgent:       "civic-lens-test",
		MaxRequestsHour: 36000, // effectively unlimited for the test loop
		BaseURL:         stub.server.URL,
	})

	xr := &XRunner{
		app:    &app.App{Database: d, RawStore: rs},
		client: client,
	}

	budget, err := NewXBudgetTracker(context.Background(), d, ceilingCents)
	if err != nil {
		t.Fatalf("budget: %v", err)
	}
	return xr, budget
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

func countRows(t *testing.T, xr *XRunner, query string, args ...any) int {
	t.Helper()
	row := xr.app.Database.Conn().QueryRowContext(context.Background(), query, args...)
	var n int
	if err := row.Scan(&n); err != nil {
		t.Fatalf("count: %v", err)
	}
	return n
}

// ---- tests ----

func TestRunOfficialsPass_SuccessTagsRowsAsOfficial(t *testing.T) {
	stub := newStubXAPI(t)
	stub.registerOfficial("@POTUS", "100", []stubTweet{
		{ID: "t100a", Text: "post one"},
		{ID: "t100b", Text: "post two"},
	})
	stub.registerOfficial("@VP", "200", []stubTweet{
		{ID: "t200a", Text: "post three"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "@POTUS"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "@VP"
    display_name: V
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
`)

	xr, budget := newOfficialsRunnerHarness(t, stub, 0) // ceiling=0 = unlimited

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

	tagged := countRows(t, xr, "SELECT COUNT(*) FROM x_posts_raw WHERE is_official_tier = 1")
	if tagged != 3 {
		t.Errorf("want 3 official-tier rows, got %d", tagged)
	}
	untagged := countRows(t, xr, "SELECT COUNT(*) FROM x_posts_raw WHERE is_official_tier = 0")
	if untagged != 0 {
		t.Errorf("untagged rows leaked: %d", untagged)
	}
}

func TestRunOfficialsPass_FailedHandleDoesNotAbortRun(t *testing.T) {
	stub := newStubXAPI(t)
	// First handle is suspended (lookup returns errors), second succeeds.
	stub.markSuspended("@FormerSecRetired")
	stub.registerOfficial("@POTUS", "100", []stubTweet{
		{ID: "t100a", Text: "still posting"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "@FormerSecRetired"
    display_name: F
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "@POTUS"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
`)
	xr, budget := newOfficialsRunnerHarness(t, stub, 0)

	res, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5)
	if err != nil {
		t.Fatalf("runOfficialsPass: %v", err)
	}

	// One failed (suspended), one succeeded — the failure must not have
	// short-circuited the loop. This is the per-account isolation contract.
	if res.HandlesFailed != 1 {
		t.Errorf("want 1 failed handle, got %d (full: %+v)", res.HandlesFailed, res)
	}
	if res.HandlesSucceeded != 1 {
		t.Errorf("want 1 succeeded handle (POTUS) despite earlier failure, got %d (full: %+v)", res.HandlesSucceeded, res)
	}
	if res.PostsIngested != 1 {
		t.Errorf("want 1 post (POTUS), got %d", res.PostsIngested)
	}
}

func TestRunOfficialsPass_UsesCachedUserIDOnRerun(t *testing.T) {
	stub := newStubXAPI(t)
	stub.registerOfficial("@POTUS", "100", []stubTweet{
		{ID: "t100a", Text: "post"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "@POTUS"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
`)
	xr, budget := newOfficialsRunnerHarness(t, stub, 0)

	if _, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5); err != nil {
		t.Fatalf("first pass: %v", err)
	}

	// First pass should have hit the lookup endpoint exactly once.
	if got := atomic.LoadInt32(stub.userLookupCalls["potus"]); got != 1 {
		t.Fatalf("first pass lookup count: want 1, got %d", got)
	}

	// Second pass: x_users_raw now has the user_id, so the runner must
	// use the cache and skip the user-by-username endpoint entirely.
	if _, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5); err != nil {
		t.Fatalf("second pass: %v", err)
	}
	if got := atomic.LoadInt32(stub.userLookupCalls["potus"]); got != 1 {
		t.Errorf("second pass should be cache-only; lookup count rose to %d", got)
	}
	// Timeline endpoint, by contrast, is hit every pass — the budget
	// tracker is the only thing keeping that bounded.
	if got := atomic.LoadInt32(stub.timelineCalls["100"]); got != 2 {
		t.Errorf("want 2 timeline calls (one per pass), got %d", got)
	}
}

func TestRunOfficialsPass_BudgetExhaustionSkipsRemainingHandles(t *testing.T) {
	stub := newStubXAPI(t)
	// Three officials — but only the first one's API cost should fit the
	// $0.20 ceiling we set. After it spends, the budget guard must skip
	// the remainder cleanly.
	stub.registerOfficial("@POTUS", "100", []stubTweet{
		{ID: "t100a", Text: fmt.Sprintf("post a %d", 1)},
		{ID: "t100b", Text: fmt.Sprintf("post b %d", 2)},
	})
	stub.registerOfficial("@VP", "200", []stubTweet{
		{ID: "t200a", Text: "should not be fetched"},
	})
	stub.registerOfficial("@SpeakerJohnson", "300", []stubTweet{
		{ID: "t300a", Text: "should not be fetched"},
	})
	yamlPath := writeOfficialsYAML(t, `
officials:
  - handle: "@POTUS"
    display_name: P
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "@VP"
    display_name: V
    party: R
    term_start: "2025-01-20"
    bio_source: "x"
  - handle: "@SpeakerJohnson"
    display_name: S
    party: R
    term_start: "2023-10-25"
    bio_source: "x"
`)
	// Budget math (per x_budget.go):
	//   per call: lookup adds 1 user-resource (10c/10 = 1c).
	//   per timeline:  N posts × 5c/10 + M users × 10c/10.
	// Ceiling=4c lets POTUS (3c) and VP (4c) through but stops
	// SpeakerJohnson because the guard fires when estimated >= ceiling.
	// The contract under test is: once the guard trips, the next handle
	// is reported as Skipped (NOT Failed) and its endpoints are never hit.
	xr, budget := newOfficialsRunnerHarness(t, stub, 4)

	res, err := xr.runOfficialsPass(context.Background(), budget, yamlPath, 5)
	if err != nil {
		t.Fatalf("runOfficialsPass: %v", err)
	}

	if res.HandlesSkipped == 0 {
		t.Errorf("expected the budget guard to skip at least one handle (full: %+v)", res)
	}
	if res.HandlesFailed != 0 {
		t.Errorf("budget exhaustion must be reported as Skipped, never Failed; got %d failed (full: %+v)", res.HandlesFailed, res)
	}
	// SpeakerJohnson is the trailing handle; its timeline endpoint must
	// never have been called once the guard fired.
	if got := atomic.LoadInt32(stub.timelineCalls["300"]); got != 0 {
		t.Errorf("SpeakerJohnson timeline must not have been hit after budget exhaust; got %d", got)
	}
	// Final budget must not have drifted past the ceiling — the guard
	// is supposed to stop at-or-before the ceiling, never cross it by
	// more than one in-flight call's worth.
	final := budget.Summary()
	if final.EstimatedCents < final.CeilingCents {
		t.Errorf("budget should be at-or-past ceiling after the guard fired; got %d / %d",
			final.EstimatedCents, final.CeilingCents)
	}
}
