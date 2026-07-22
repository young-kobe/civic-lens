package runner

import (
	"context"
	"fmt"
)

// initRowPostgres lazy-inserts the current month into ops.x_api_budget so
// later UPDATEs always find a row. Counterpart to initRowSQLite.
func (t *XBudgetTracker) initRowPostgres(ctx context.Context) error {
	if _, err := t.db.ExecContext(ctx,
		`INSERT INTO ops.x_api_budget
		    (month_key, post_count, user_count, request_count, estimated_cents, last_updated)
		 VALUES ($1, 0, 0, 0, 0, $2)
		 ON CONFLICT (month_key) DO NOTHING`,
		t.monthKey, t.now(),
	); err != nil {
		return fmt.Errorf("x_api_budget init row: %w", err)
	}
	return nil
}

// reloadPostgres loads the accumulated state for the current month from
// ops.x_api_budget. Counterpart to the SQLite branch inlined in reload.
func (t *XBudgetTracker) reloadPostgres(ctx context.Context) error {
	row := t.db.QueryRowContext(ctx,
		`SELECT post_count, user_count, request_count, estimated_cents
		 FROM ops.x_api_budget WHERE month_key = $1`,
		t.monthKey,
	)
	if err := row.Scan(&t.postCount, &t.userCount, &t.reqCount, &t.estimated); err != nil {
		return fmt.Errorf("x_api_budget load: %w", err)
	}
	return nil
}

// recordPostgres applies the increment relative to the stored value, same
// composability rationale as recordSQLite (x_budget.go).
func (t *XBudgetTracker) recordPostgres(ctx context.Context, posts, newUsers, costCents int) error {
	if _, err := t.db.ExecContext(ctx,
		`UPDATE ops.x_api_budget
		 SET post_count = post_count + $1, user_count = user_count + $2,
		     request_count = request_count + 1,
		     estimated_cents = estimated_cents + $3, last_updated = $4
		 WHERE month_key = $5`,
		posts, newUsers, costCents, t.now(), t.monthKey,
	); err != nil {
		return fmt.Errorf("x_api_budget update: %w", err)
	}
	return nil
}
