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
// Backing store is the `x_api_budget` table (migration 017). Exactly one row
// per calendar month (UTC). Safe to share across a single runner invocation;
// not goroutine-safe — the X runner is single-threaded by design.
type XBudgetTracker struct {
	db         *sql.DB
	isPostgres bool
	monthKey   string
	ceiling    int // cents; 0 disables the check
	now        func() time.Time
	postCount  int
	userCount  int
	reqCount   int
	estimated  int
}

// NewXBudgetTracker loads the current-month row (creating it if absent) and
// returns a tracker primed for use by the runner. database carries both the
// connection and the backend selector (db.DB.IsPostgres) so the tracker can
// pick x_api_budget (SQLite) vs ops.x_api_budget (Postgres) without the
// caller branching.
func NewXBudgetTracker(ctx context.Context, database *db.DB, ceilingCents int) (*XBudgetTracker, error) {
	return newXBudgetTracker(ctx, database.Conn(), database.IsPostgres(), ceilingCents, time.Now)
}

// newXBudgetTracker is the testable variant with an injectable clock and an
// explicit backend selector, so tests can exercise both SQLite and Postgres
// without needing a real db.DB.
func newXBudgetTracker(
	ctx context.Context,
	conn *sql.DB,
	isPostgres bool,
	ceilingCents int,
	now func() time.Time,
) (*XBudgetTracker, error) {
	t := &XBudgetTracker{db: conn, isPostgres: isPostgres, ceiling: ceilingCents, now: now}
	t.monthKey = now().UTC().Format("2006-01")

	var err error
	if isPostgres {
		err = t.initRowPostgres(ctx)
	} else {
		err = t.initRowSQLite(ctx)
	}
	if err != nil {
		return nil, err
	}

	if err := t.reload(ctx); err != nil {
		return nil, err
	}
	return t, nil
}

// initRowSQLite lazy-inserts the current month so later UPDATEs always find
// a row. The Postgres counterpart, initRowPostgres, lives in
// x_budget_postgres.go.
func (t *XBudgetTracker) initRowSQLite(ctx context.Context) error {
	if _, err := t.db.ExecContext(ctx,
		`INSERT OR IGNORE INTO x_api_budget
		    (month_key, post_count, user_count, request_count, estimated_cents, last_updated)
		 VALUES (?, 0, 0, 0, 0, ?)`,
		t.monthKey, t.now().Unix(),
	); err != nil {
		return fmt.Errorf("x_api_budget init row: %w", err)
	}
	return nil
}

// reload loads the accumulated state for the current month, dispatching to
// the backend this tracker was constructed against. Called both at init and
// after Record persists an increment, so postCount/userCount/reqCount/
// estimated always reflect the authoritative stored totals (including any
// increment a concurrent run committed).
func (t *XBudgetTracker) reload(ctx context.Context) error {
	if t.isPostgres {
		return t.reloadPostgres(ctx)
	}
	row := t.db.QueryRowContext(ctx,
		`SELECT post_count, user_count, request_count, estimated_cents
		 FROM x_api_budget WHERE month_key = ?`,
		t.monthKey,
	)
	if err := row.Scan(&t.postCount, &t.userCount, &t.reqCount, &t.estimated); err != nil {
		return fmt.Errorf("x_api_budget load: %w", err)
	}
	return nil
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

	if t.isPostgres {
		if err := t.recordPostgres(ctx, posts, newUsers, costCents); err != nil {
			return err
		}
	} else if err := t.recordSQLite(ctx, posts, newUsers, costCents); err != nil {
		return err
	}

	// Reload the authoritative totals so OverBudget/Summary reflect the true
	// stored spend, including any increments a concurrent run committed.
	if err := t.reload(ctx); err != nil {
		return fmt.Errorf("x_api_budget reload: %w", err)
	}
	return nil
}

// recordSQLite applies the increment RELATIVE to the stored value in a
// single statement rather than writing an absolute in-memory total. Two
// overlapping `civic-ingest x` runs would otherwise clobber each other's
// increments; `x = x + ?` composes correctly regardless of interleaving. The
// Postgres counterpart, recordPostgres, lives in x_budget_postgres.go.
func (t *XBudgetTracker) recordSQLite(ctx context.Context, posts, newUsers, costCents int) error {
	if _, err := t.db.ExecContext(ctx,
		`UPDATE x_api_budget
		 SET post_count = post_count + ?, user_count = user_count + ?,
		     request_count = request_count + 1,
		     estimated_cents = estimated_cents + ?, last_updated = ?
		 WHERE month_key = ?`,
		posts, newUsers, costCents, t.now().Unix(), t.monthKey,
	); err != nil {
		return fmt.Errorf("x_api_budget update: %w", err)
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
