package runner

import (
	"context"
	"strings"
	"sync/atomic"
	"testing"
)

// TestPostsNeeded_ShortfallMath exercises the pure shortfall calculation
// backfill-officials relies on for its resumability contract: a stored
// count at or above target never yields a negative "need more" value, it
// yields exactly zero (nothing left to fetch).
func TestPostsNeeded_ShortfallMath(t *testing.T) {
	cases := []struct {
		name           string
		target, stored int
		want           int
	}{
		{"fresh official, editorial target", 100, 0, 100},
		{"partially backfilled, promoted target", 25, 10, 15},
		{"exactly at target", 25, 25, 0},
		{"already past target", 25, 40, 0},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if got := postsNeeded(c.target, c.stored); got != c.want {
				t.Errorf("postsNeeded(%d, %d) = %d, want %d", c.target, c.stored, got, c.want)
			}
		})
	}
}

// TestTargetFor_EditorialVsPromoted pins the two named constants to the
// provenance column that selects between them — a regression here would
// silently under- or over-fetch an entire provenance tier.
func TestTargetFor_EditorialVsPromoted(t *testing.T) {
	if got := targetFor(officialRow{Handle: "leaderjohnthune", Editorial: true}); got != editorialBackfillTarget {
		t.Errorf("editorial target = %d, want %d", got, editorialBackfillTarget)
	}
	if got := targetFor(officialRow{Handle: "austinscottga08", Editorial: false}); got != promotedBackfillTarget {
		t.Errorf("promoted target = %d, want %d", got, promotedBackfillTarget)
	}
}

// fakeStoredCounts is a backfillDeps.storedCount fake keyed by the numeric
// X user_id the stub API hands back for each handle, so tests can pin an
// official's starting stored-post count without touching a real raw.x_posts
// table.
func fakeStoredCounts(counts map[string]int) func(ctx context.Context, authorID string) (int, error) {
	return func(_ context.Context, authorID string) (int, error) {
		return counts[authorID], nil
	}
}

func fakeOfficialsList(rows ...officialRow) func(ctx context.Context) ([]officialRow, error) {
	return func(context.Context) ([]officialRow, error) {
		return rows, nil
	}
}

// TestRunBackfillOfficials_SkipsWhenAlreadyAtTarget verifies the resume-skip
// contract: an official whose stored count already meets its target must
// never trigger a timeline fetch (the expensive call this command exists to
// bound), and must be counted as skipped, not processed.
func TestRunBackfillOfficials_SkipsWhenAlreadyAtTarget(t *testing.T) {
	stub := newStubXAPI(t)
	stub.registerOfficial("senschumer", "77", []stubTweet{{ID: "t1", Text: "should not be fetched"}})
	xr, budget := newOfficialsRunnerHarness(t, stub, 0)

	deps := backfillDeps{
		listOfficials: fakeOfficialsList(officialRow{Handle: "senschumer", Editorial: false}),
		storedCount:   fakeStoredCounts(map[string]int{"77": promotedBackfillTarget}),
	}

	res, err := xr.runBackfillOfficials(context.Background(), 1_000_000, deps, budget)
	if err != nil {
		t.Fatalf("runBackfillOfficials: %v", err)
	}
	if res.OfficialsSkipped != 1 {
		t.Errorf("OfficialsSkipped = %d, want 1", res.OfficialsSkipped)
	}
	if res.OfficialsProcessed != 0 {
		t.Errorf("OfficialsProcessed = %d, want 0", res.OfficialsProcessed)
	}
	if res.PostsFetched != 0 {
		t.Errorf("PostsFetched = %d, want 0", res.PostsFetched)
	}
	if got := atomic.LoadInt32(stub.timelineCalls["77"]); got != 0 {
		t.Errorf("timeline calls = %d, want 0 — resume-skip must never fetch", got)
	}
}

// TestRunBackfillOfficials_FetchesShortfallWhenBelowTarget verifies an
// official below target gets its shortfall fetched and stored, and is
// counted as processed.
func TestRunBackfillOfficials_FetchesShortfallWhenBelowTarget(t *testing.T) {
	stub := newStubXAPI(t)
	stub.registerOfficial("austinscottga08", "55", []stubTweet{
		{ID: "t1", Text: "post one"},
		{ID: "t2", Text: "post two"},
	})
	xr, budget := newOfficialsRunnerHarness(t, stub, 0)

	deps := backfillDeps{
		listOfficials: fakeOfficialsList(officialRow{Handle: "austinscottga08", Editorial: false}),
		storedCount:   fakeStoredCounts(map[string]int{"55": promotedBackfillTarget - 2}),
	}

	res, err := xr.runBackfillOfficials(context.Background(), 1_000_000, deps, budget)
	if err != nil {
		t.Fatalf("runBackfillOfficials: %v", err)
	}
	if res.OfficialsProcessed != 1 {
		t.Errorf("OfficialsProcessed = %d, want 1", res.OfficialsProcessed)
	}
	if res.PostsFetched != 2 {
		t.Errorf("PostsFetched = %d, want 2", res.PostsFetched)
	}
	if got := atomic.LoadInt32(stub.timelineCalls["55"]); got != 1 {
		t.Errorf("timeline calls = %d, want 1", got)
	}
}

// TestRunBackfillOfficials_CapStopsCleanly verifies the invocation-scoped
// --spend-cap-usd ceiling stops the walk before a later official's API
// calls happen at all, once an earlier official's spend reaches it.
func TestRunBackfillOfficials_CapStopsCleanly(t *testing.T) {
	stub := newStubXAPI(t)
	// Fresh lookup (not cached) costs 1 cent; the timeline pull below costs
	// ceil(2*5/10) = 1 cent. Total for the first official: 2 cents.
	stub.registerOfficial("senschumer", "77", []stubTweet{
		{ID: "t1", Text: "a"}, {ID: "t2", Text: "b"},
	})
	stub.registerOfficial("chuckgrassley", "88", []stubTweet{
		{ID: "t3", Text: "should not be fetched"},
	})
	xr, budget := newOfficialsRunnerHarness(t, stub, 0)

	deps := backfillDeps{
		listOfficials: fakeOfficialsList(
			officialRow{Handle: "senschumer", Editorial: false},
			officialRow{Handle: "chuckgrassley", Editorial: false},
		),
		storedCount: fakeStoredCounts(map[string]int{"77": 0, "88": 0}),
	}

	// Cap = 2 cents: covers exactly the first official's lookup + fetch,
	// leaving nothing for the second.
	res, err := xr.runBackfillOfficials(context.Background(), 2, deps, budget)
	if err != nil {
		t.Fatalf("runBackfillOfficials: %v", err)
	}
	if res.OfficialsProcessed != 1 {
		t.Errorf("OfficialsProcessed = %d, want 1", res.OfficialsProcessed)
	}
	if got := atomic.LoadInt32(stub.userLookupCalls["chuckgrassley"]); got != 0 {
		t.Errorf("second official's lookup calls = %d, want 0 — cap must stop cleanly before it", got)
	}
	if got := atomic.LoadInt32(stub.timelineCalls["88"]); got != 0 {
		t.Errorf("second official's timeline calls = %d, want 0 — cap must stop cleanly before it", got)
	}
	if res.SpendCents < 2 {
		t.Errorf("SpendCents = %d, want >= 2 (the cap)", res.SpendCents)
	}
}

// TestRunBackfillOfficials_ErrorCountedButLoopContinues verifies per-official
// failures (a suspended/renamed handle) are logged and counted rather than
// aborting the remaining walk, and that the aggregated error is exactly what
// signals the caller to exit nonzero.
func TestRunBackfillOfficials_ErrorCountedButLoopContinues(t *testing.T) {
	stub := newStubXAPI(t)
	stub.markSuspended("markwarner")
	stub.registerOfficial("chriscoons", "99", []stubTweet{{ID: "t1", Text: "still posting"}})
	xr, budget := newOfficialsRunnerHarness(t, stub, 0)

	deps := backfillDeps{
		listOfficials: fakeOfficialsList(
			officialRow{Handle: "markwarner", Editorial: false},
			officialRow{Handle: "chriscoons", Editorial: false},
		),
		storedCount: fakeStoredCounts(map[string]int{"99": 0}),
	}

	res, err := xr.runBackfillOfficials(context.Background(), 1_000_000, deps, budget)
	if err == nil {
		t.Fatal("want a non-nil error when an official errored, got nil")
	}
	if !strings.Contains(err.Error(), "markwarner") {
		t.Errorf("error %q does not name the failed handle", err.Error())
	}
	if res.OfficialsErrored != 1 {
		t.Errorf("OfficialsErrored = %d, want 1", res.OfficialsErrored)
	}
	if res.OfficialsProcessed != 1 {
		t.Errorf("OfficialsProcessed = %d, want 1 (chriscoons must still run after markwarner's failure)", res.OfficialsProcessed)
	}
}
