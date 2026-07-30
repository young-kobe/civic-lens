package runner

import (
	"context"
	"database/sql"
	"fmt"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// X API per-resource cents costs, per https://docs.x.com/x-api pricing as of
// 2026-04-20. Tracked here rather than in config because the prices are an
// X-side fact, not a Civic Lens policy. If X changes pricing, bump these.
const (
	centsPerPost        = 5  // 0.5 cent per tweet returned
	centsPerUser        = 10 // 1 cent per hydrated user
	centsConversionUnit = 10 // rates are in tenths-of-a-cent; integer math only
)

// XBudgetTracker is the persistent monthly-spend guard for the X API runner.
// Backing store is `ops.x_api_budget`. Exactly one row per calendar month
// (UTC). Safe to share across a single runner invocation; not
// goroutine-safe — the X runner is single-threaded by design.
type XBudgetTracker struct {
	db        *sql.DB
	monthKey  string
	ceiling   int // cents; 0 disables the check
	now       func() time.Time
	postCount int
	userCount int
	reqCount  int
	estimated int
}

// NewXBudgetTracker loads the current-month row (creating it if absent) and
// returns a tracker primed for use by the runner.
func NewXBudgetTracker(ctx context.Context, database *db.DB, ceilingCents int) (*XBudgetTracker, error) {
	return newXBudgetTracker(ctx, database.Conn(), ceilingCents, time.Now)
}

// newXBudgetTracker is the testable variant with an injectable clock, so
// tests can pin month_key selection without needing a real db.DB.
func newXBudgetTracker(
	ctx context.Context,
	conn *sql.DB,
	ceilingCents int,
	now func() time.Time,
) (*XBudgetTracker, error) {
	t := &XBudgetTracker{db: conn, ceiling: ceilingCents, now: now}
	t.monthKey = now().UTC().Format("2006-01")

	if err := t.initRowPostgres(ctx); err != nil {
		return nil, err
	}

	if err := t.reload(ctx); err != nil {
		return nil, err
	}
	return t, nil
}

// reload loads the accumulated state for the current month. Called both at
// init and after Record persists an increment, so postCount/userCount/
// reqCount/estimated always reflect the authoritative stored totals
// (including any increment a concurrent run committed).
func (t *XBudgetTracker) reload(ctx context.Context) error {
	return t.reloadPostgres(ctx)
}

// OverBudget reports whether the tracker has already hit its ceiling. Called
// BEFORE each SearchRecentPosts call so the runner can break its query loop
// cleanly (logging) rather than making a request that could push past the
// cap — one extra call is ~1-2 cents of slop, acceptable but avoidable.
func (t *XBudgetTracker) OverBudget() bool {
	if t.ceiling <= 0 {
		return false
	}
	return t.estimated >= t.ceiling
}

// Record bumps the counters after a successful API call and persists the
// new totals. Callers pass the number of Post resources returned and the
// number of *newly-hydrated* users (not total users in the response — the
// same user seen twice in the same month is still billed twice by X, but
// within a single API response each unique user is billed once).
func (t *XBudgetTracker) Record(ctx context.Context, posts, newUsers int) error {
	// Round the per-call cost UP to the next whole cent. The rates are in
	// tenths-of-a-cent, so 5 tweets (25 tenths = 2.5c) must bill 3c, never 2c
	// — truncating undercounts spend and lets OverBudget release calls past
	// the ceiling.
	costCents := ceilDiv(posts*centsPerPost+newUsers*centsPerUser, centsConversionUnit)

	if err := t.recordPostgres(ctx, posts, newUsers, costCents); err != nil {
		return err
	}

	// Reload the authoritative totals so OverBudget/Summary reflect the true
	// stored spend, including any increments a concurrent run committed.
	if err := t.reload(ctx); err != nil {
		return fmt.Errorf("x_api_budget reload: %w", err)
	}
	return nil
}

// ceilDiv returns ceil(a/b) for non-negative integers (b > 0).
func ceilDiv(a, b int) int {
	if a <= 0 {
		return 0
	}
	return (a + b - 1) / b
}

// Summary returns the current month's tallies for logging or the admin UI.
func (t *XBudgetTracker) Summary() XBudgetSummary {
	return XBudgetSummary{
		MonthKey:       t.monthKey,
		PostCount:      t.postCount,
		UserCount:      t.userCount,
		RequestCount:   t.reqCount,
		EstimatedCents: t.estimated,
		CeilingCents:   t.ceiling,
	}
}

// XBudgetSummary is a value type safe to pass to logs or JSON encoders.
type XBudgetSummary struct {
	MonthKey       string
	PostCount      int
	UserCount      int
	RequestCount   int
	EstimatedCents int
	CeilingCents   int
}
