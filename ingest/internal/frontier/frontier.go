package frontier

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"log"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
	"github.com/young-kobe/civic-lens/ingest/internal/util"
)

// Frontier manages the crawl queue with state machine transitions.
// State machine: QUEUED -> INFLIGHT -> DONE/FAILED
// Failed items with retries left go back to QUEUED with backoff.
type Frontier struct {
	db         *db.DB
	maxRetries int
}

// PushStats categorizes the outcomes of a PushLinks call so callers/ops
// can distinguish bad input from infrastructure failure.
type PushStats struct {
	Added     int64
	Malformed int64
	DBErrors  int64
}

// New creates a new Frontier.
func New(database *db.DB, maxRetries int) *Frontier {
	return &Frontier{
		db:         database,
		maxRetries: maxRetries,
	}
}

// RecoverStale requeues any items stuck in INFLIGHT state for too long.
// This provides crash recovery and is called automatically by EnsureRecovered.
func (f *Frontier) RecoverStale(ctx context.Context, staleAge time.Duration) (int64, error) {
	cutoff := time.Now().Add(-staleAge).Unix()

	result, err := f.db.Conn().ExecContext(ctx, `
		UPDATE pages
		SET state = ?, next_fetch_at = ?, inflight_at = 0
		WHERE state = ? AND inflight_at < ? AND inflight_at > 0
	`, model.StateQueued, time.Now().Unix(), model.StateInflight, cutoff)

	if err != nil {
		return 0, fmt.Errorf("recover stale: %w", err)
	}

	return result.RowsAffected()
}

// EnsureRecovered runs RecoverStale and logs the result. Every runner
// should call this at startup so INFLIGHT rows cannot leak if the runner
// crashes mid-batch; making it a Frontier method means new runners
// cannot forget the call.
func (f *Frontier) EnsureRecovered(ctx context.Context, staleAge time.Duration) {
	recovered, err := f.RecoverStale(ctx, staleAge)
	if err != nil {
		log.Printf("frontier: recover stale failed: %v", err)
		return
	}
	if recovered > 0 {
		log.Printf("frontier: recovered %d stale inflight items", recovered)
	}
}

// ClaimItems atomically claims a batch of work items via a single
// UPDATE ... RETURNING, moving rows from QUEUED to INFLIGHT.
func (f *Frontier) ClaimItems(ctx context.Context, batchSize int) ([]*model.Page, error) {
	now := time.Now().Unix()

	rows, err := f.db.Conn().QueryContext(ctx, `
		UPDATE pages
		SET state = ?, inflight_at = ?
		WHERE url_canon IN (
			SELECT url_canon
			FROM pages
			WHERE state = ? AND next_fetch_at <= ?
			ORDER BY priority DESC, next_fetch_at ASC
			LIMIT ?
		)
		RETURNING url_canon, url_raw, domain, priority, retries
	`, model.StateInflight, now, model.StateQueued, now, batchSize)
	if err != nil {
		return nil, fmt.Errorf("claim items: %w", err)
	}
	defer rows.Close()

	var pages []*model.Page
	for rows.Next() {
		p := &model.Page{State: model.StateInflight, InflightAt: now}
		if err := rows.Scan(&p.URLCanon, &p.URLRaw, &p.Domain, &p.Priority, &p.Retries); err != nil {
			return nil, fmt.Errorf("scan page: %w", err)
		}
		pages = append(pages, p)
	}
	if err := rows.Err(); err != nil {
		return nil, fmt.Errorf("iterate claimed rows: %w", err)
	}

	return pages, nil
}

// MarkDone marks a page as successfully fetched.
func (f *Frontier) MarkDone(ctx context.Context, page *model.Page) error {
	return f.updatePageState(ctx, page.URLCanon, model.StateDone, map[string]any{
		"http_status":    page.HTTPStatus,
		"content_sha256": page.ContentSHA256,
		"etag":           page.ETag,
		"last_modified":  page.LastModified,
		"last_error":     nil,
	})
}

// MarkFailed marks a page as failed.
// If permanent is false and retries are available, schedules for retry with backoff.
func (f *Frontier) MarkFailed(ctx context.Context, page *model.Page, errMsg string, permanent bool) error {
	if permanent || page.Retries >= f.maxRetries {
		return f.updatePageState(ctx, page.URLCanon, model.StateFailed, map[string]any{
			"last_error": errMsg,
		})
	}

	backoff := time.Duration(1<<uint(page.Retries)) * time.Minute
	nextFetch := time.Now().Add(backoff).Unix()

	return f.updatePageState(ctx, page.URLCanon, model.StateQueued, map[string]any{
		"retries":       "retries + 1", // sentinel so the expression is spliced, not parameterized
		"next_fetch_at": nextFetch,
		"last_error":    errMsg,
	})
}

// updatePageState applies a state transition plus a set of column updates
// in a single UPDATE. `inflight_at` is always reset to 0. Values are
// parameterized except for the "retries + 1" sentinel, which is spliced
// as an expression so SQLite can evaluate it.
func (f *Frontier) updatePageState(ctx context.Context, urlCanon string, state model.PageState, updates map[string]any) error {
	// Build SET clauses in a stable order so prepared-statement reuse is possible.
	args := []any{state, 0} // state, inflight_at
	setClauses := "state = ?, inflight_at = ?"
	for _, col := range []string{"http_status", "content_sha256", "etag", "last_modified", "last_error", "next_fetch_at", "retries"} {
		v, ok := updates[col]
		if !ok {
			continue
		}
		if col == "retries" {
			// Spliced expression — only "retries + 1" is accepted.
			expr, isExpr := v.(string)
			if !isExpr || expr != "retries + 1" {
				return fmt.Errorf("updatePageState: invalid retries expression")
			}
			setClauses += ", retries = retries + 1"
			continue
		}
		setClauses += ", " + col + " = ?"
		args = append(args, v)
	}
	args = append(args, urlCanon)

	query := "UPDATE pages SET " + setClauses + " WHERE url_canon = ?"
	_, err := f.db.Conn().ExecContext(ctx, query, args...)
	return err
}

// PushLinks adds new URLs to the frontier and returns categorized
// counts. Duplicates are ignored (INSERT OR IGNORE). Malformed URLs
// and DB insert errors are counted separately so ops can distinguish
// bad input from infrastructure trouble.
func (f *Frontier) PushLinks(ctx context.Context, links []string, priority int) (*PushStats, error) {
	stats := &PushStats{}
	if len(links) == 0 {
		return stats, nil
	}

	stmt, err := f.db.Conn().PrepareContext(ctx, `
		INSERT OR IGNORE INTO pages (url_canon, url_raw, domain, state, priority, next_fetch_at)
		VALUES (?, ?, ?, ?, ?, ?)
	`)
	if err != nil {
		return stats, fmt.Errorf("prepare insert: %w", err)
	}
	defer stmt.Close()

	now := time.Now().Unix()

	for _, link := range links {
		canon, err := util.CanonicalizeURL(link)
		if err != nil {
			stats.Malformed++
			continue
		}
		domain := util.ExtractDomain(canon)

		result, err := stmt.ExecContext(ctx, canon, link, domain, model.StateQueued, priority, now)
		if err != nil {
			stats.DBErrors++
			continue
		}
		n, _ := result.RowsAffected()
		stats.Added += n
	}

	if stats.DBErrors > 0 {
		return stats, errors.New("one or more DB insert errors occurred during PushLinks")
	}
	return stats, nil
}

// Stats returns current frontier statistics.
func (f *Frontier) Stats(ctx context.Context) (queued, inflight, done, failed int64, err error) {
	row := f.db.Conn().QueryRowContext(ctx, `
		SELECT
			SUM(CASE WHEN state = 0 THEN 1 ELSE 0 END),
			SUM(CASE WHEN state = 1 THEN 1 ELSE 0 END),
			SUM(CASE WHEN state = 2 THEN 1 ELSE 0 END),
			SUM(CASE WHEN state = 3 THEN 1 ELSE 0 END)
		FROM pages
	`)

	var q, i, d, fa sql.NullInt64
	if err = row.Scan(&q, &i, &d, &fa); err != nil {
		return
	}

	return q.Int64, i.Int64, d.Int64, fa.Int64, nil
}
