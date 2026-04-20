package runner

import (
	"context"
	"database/sql"
	"fmt"
	"time"
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
// Backing store is the `x_api_budget` table (migration 017). Exactly one row
// per calendar month (UTC). Safe to share across a single runner invocation;
// not goroutine-safe — the X runner is single-threaded by design.
type XBudgetTracker struct {
	db         *sql.DB
	monthKey   string
	ceiling    int // cents; 0 disables the check
	now        func() time.Time
	postCount  int
	userCount  int
	reqCount   int
	estimated  int
}

// NewXBudgetTracker loads the current-month row (creating it if absent) and
// returns a tracker primed for use by the runner.
func NewXBudgetTracker(ctx context.Context, db *sql.DB, ceilingCents int) (*XBudgetTracker, error) {
	return newXBudgetTracker(ctx, db, ceilingCents, time.Now)
}

// newXBudgetTracker is the testable variant with an injectable clock.
func newXBudgetTracker(
	ctx context.Context,
	db *sql.DB,
	ceilingCents int,
	now func() time.Time,
) (*XBudgetTracker, error) {
	t := &XBudgetTracker{db: db, ceiling: ceilingCents, now: now}
	t.monthKey = now().UTC().Format("2006-01")

	// Lazy-insert the current month so later UPDATEs always find a row.
	if _, err := db.ExecContext(ctx,
		`INSERT OR IGNORE INTO x_api_budget
		    (month_key, post_count, user_count, request_count, estimated_cents, last_updated)
		 VALUES (?, 0, 0, 0, 0, ?)`,
		t.monthKey, now().Unix(),
	); err != nil {
		return nil, fmt.Errorf("x_api_budget init row: %w", err)
	}

	// Load accumulated state.
	row := db.QueryRowContext(ctx,
		`SELECT post_count, user_count, request_count, estimated_cents
		 FROM x_api_budget WHERE month_key = ?`,
		t.monthKey,
	)
	if err := row.Scan(&t.postCount, &t.userCount, &t.reqCount, &t.estimated); err != nil {
		return nil, fmt.Errorf("x_api_budget load: %w", err)
	}
	return t, nil
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
	t.postCount += posts
	t.userCount += newUsers
	t.reqCount++
	t.estimated += posts*centsPerPost/centsConversionUnit +
		newUsers*centsPerUser/centsConversionUnit

	_, err := t.db.ExecContext(ctx,
		`UPDATE x_api_budget
		 SET post_count = ?, user_count = ?, request_count = ?,
		     estimated_cents = ?, last_updated = ?
		 WHERE month_key = ?`,
		t.postCount, t.userCount, t.reqCount, t.estimated, t.now().Unix(), t.monthKey,
	)
	if err != nil {
		return fmt.Errorf("x_api_budget update: %w", err)
	}
	return nil
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
