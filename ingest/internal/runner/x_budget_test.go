package runner

import (
	"context"
	"database/sql"
	"testing"
	"time"

	_ "modernc.org/sqlite"
)

// newTestDB creates an in-memory SQLite DB with just the schema bits
// x_api_budget needs. We don't want to pull in the full migration machinery
// since that reads from disk and the test should be hermetic.
func newTestDB(t *testing.T) *sql.DB {
	t.Helper()
	db, err := sql.Open("sqlite", ":memory:")
	if err != nil {
		t.Fatalf("open: %v", err)
	}
	t.Cleanup(func() { _ = db.Close() })
	if _, err := db.Exec(`
		CREATE TABLE x_api_budget (
			month_key       TEXT PRIMARY KEY,
			post_count      INTEGER NOT NULL DEFAULT 0,
			user_count      INTEGER NOT NULL DEFAULT 0,
			request_count   INTEGER NOT NULL DEFAULT 0,
			estimated_cents INTEGER NOT NULL DEFAULT 0,
			last_updated    INTEGER NOT NULL DEFAULT 0
		);`); err != nil {
		t.Fatalf("create table: %v", err)
	}
	return db
}

// frozenClock returns a fixed time, useful when asserting month_key selection.
func frozenClock(ts time.Time) func() time.Time { return func() time.Time { return ts } }

func TestXBudgetTracker_InitInsertsMonthRow(t *testing.T) {
	db := newTestDB(t)
	clock := frozenClock(time.Date(2026, 5, 1, 0, 0, 0, 0, time.UTC))

	b, err := newXBudgetTracker(context.Background(), db, 2500, clock)
	if err != nil {
		t.Fatalf("init: %v", err)
	}
	if b.monthKey != "2026-05" {
		t.Fatalf("want month_key=2026-05, got %q", b.monthKey)
	}

	var rows int
	if err := db.QueryRow(`SELECT COUNT(*) FROM x_api_budget WHERE month_key = '2026-05'`).Scan(&rows); err != nil {
		t.Fatal(err)
	}
	if rows != 1 {
		t.Fatalf("want 1 row for month, got %d", rows)
	}
}

func TestXBudgetTracker_RecordAccumulates(t *testing.T) {
	db := newTestDB(t)
	clock := frozenClock(time.Date(2026, 5, 15, 0, 0, 0, 0, time.UTC))
	b, err := newXBudgetTracker(context.Background(), db, 0, clock)
	if err != nil {
		t.Fatal(err)
	}

	// Call 1: 25 tweets, 18 new users. Raw cost = 25*5 + 18*10 = 305 tenths
	// of a cent = 30.5c, rounded UP to 31c (never truncated to 30).
	if err := b.Record(context.Background(), 25, 18); err != nil {
		t.Fatal(err)
	}
	// Call 2: 10 tweets, 7 new users. 10*5 + 7*10 = 120 tenths = 12c exactly.
	// Running total: 43 cents.
	if err := b.Record(context.Background(), 10, 7); err != nil {
		t.Fatal(err)
	}

	s := b.Summary()
	if s.PostCount != 35 || s.UserCount != 25 || s.RequestCount != 2 {
		t.Errorf("counters: posts=%d users=%d reqs=%d", s.PostCount, s.UserCount, s.RequestCount)
	}
	if s.EstimatedCents != 43 {
		t.Errorf("want 43 cents (ceil), got %d", s.EstimatedCents)
	}

	// DB side should reflect the same.
	var dbCents int
	if err := db.QueryRow(`SELECT estimated_cents FROM x_api_budget WHERE month_key = '2026-05'`).Scan(&dbCents); err != nil {
		t.Fatal(err)
	}
	if dbCents != 43 {
		t.Errorf("db persisted %d cents, want 43", dbCents)
	}
}

func TestXBudgetTracker_OverBudgetHonoursCeiling(t *testing.T) {
	db := newTestDB(t)
	clock := frozenClock(time.Date(2026, 5, 15, 0, 0, 0, 0, time.UTC))

	// $0.40 ceiling. Zero usage: not over.
	b, err := newXBudgetTracker(context.Background(), db, 40, clock)
	if err != nil {
		t.Fatal(err)
	}
	if b.OverBudget() {
		t.Fatal("fresh tracker reported OverBudget")
	}

	// Record 100 tweets + 20 users -> 50 + 20 = 70 cents. Over the 40-cent cap.
	if err := b.Record(context.Background(), 100, 20); err != nil {
		t.Fatal(err)
	}
	if !b.OverBudget() {
		t.Fatalf("want OverBudget=true at %d cents / %d cap", b.Summary().EstimatedCents, b.Summary().CeilingCents)
	}
}

func TestXBudgetTracker_CeilingZeroDisables(t *testing.T) {
	db := newTestDB(t)
	clock := frozenClock(time.Date(2026, 5, 15, 0, 0, 0, 0, time.UTC))
	b, err := newXBudgetTracker(context.Background(), db, 0, clock)
	if err != nil {
		t.Fatal(err)
	}

	// Record a huge amount — ceiling=0 means "disabled", OverBudget must stay false.
	if err := b.Record(context.Background(), 10000, 10000); err != nil {
		t.Fatal(err)
	}
	if b.OverBudget() {
		t.Fatal("ceiling=0 should disable the guard")
	}
}

func TestXBudgetTracker_PersistsAcrossInstances(t *testing.T) {
	db := newTestDB(t)
	clock := frozenClock(time.Date(2026, 5, 15, 10, 0, 0, 0, time.UTC))

	b1, err := newXBudgetTracker(context.Background(), db, 100, clock)
	if err != nil {
		t.Fatal(err)
	}
	if err := b1.Record(context.Background(), 50, 30); err != nil {
		t.Fatal(err)
	}

	// Simulate a fresh run mid-month — the next runner invocation should
	// pick up where the last one left off rather than restart from zero.
	b2, err := newXBudgetTracker(context.Background(), db, 100, clock)
	if err != nil {
		t.Fatal(err)
	}
	s := b2.Summary()
	// 50 posts * 5 / 10 = 25, 30 users * 10 / 10 = 30, total 55.
	if s.EstimatedCents != 55 {
		t.Fatalf("instance #2 lost state: got %d cents, want 55", s.EstimatedCents)
	}
	if s.PostCount != 50 || s.UserCount != 30 {
		t.Fatalf("counters lost: %+v", s)
	}
}

func TestXBudgetTracker_MonthRollover(t *testing.T) {
	db := newTestDB(t)

	// End of April: spend 30 cents.
	aprClock := frozenClock(time.Date(2026, 4, 30, 23, 0, 0, 0, time.UTC))
	bApr, _ := newXBudgetTracker(context.Background(), db, 100, aprClock)
	_ = bApr.Record(context.Background(), 40, 20)

	// Start of May: fresh tracker should see a fresh row at $0 used.
	mayClock := frozenClock(time.Date(2026, 5, 1, 0, 0, 0, 0, time.UTC))
	bMay, err := newXBudgetTracker(context.Background(), db, 100, mayClock)
	if err != nil {
		t.Fatal(err)
	}
	if bMay.Summary().EstimatedCents != 0 {
		t.Fatalf("new month leaked prior balance: %+v", bMay.Summary())
	}
	if bMay.monthKey != "2026-05" {
		t.Fatalf("want monthKey=2026-05 got %q", bMay.monthKey)
	}
}
