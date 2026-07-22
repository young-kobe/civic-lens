package frontier

import (
	"context"
	"database/sql"
	"errors"
	"fmt"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// recoverStaleSQLite is the SQLite implementation of RecoverStale — moved
// here unchanged (byte-identical behavior) from the pre-dual-backend
// frontier.go; this is the live production path until the Phase 11 cutover.
func (f *Frontier) recoverStaleSQLite(ctx context.Context, staleAge time.Duration) (int64, error) {
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

// claimItemsSQLite atomically claims a batch of work items via a single
// UPDATE ... RETURNING, moving rows from QUEUED to INFLIGHT.
func (f *Frontier) claimItemsSQLite(ctx context.Context, batchSize int) ([]*model.Page, error) {
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

// updatePageStateSQLite applies a state transition plus a set of column
// updates in a single UPDATE. `inflight_at` is always reset to 0. Values are
// parameterized except for the "retries + 1" sentinel, which is spliced
// as an expression so SQLite can evaluate it.
func (f *Frontier) updatePageStateSQLite(ctx context.Context, page *model.Page, state model.PageState, updates map[string]any) error {
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
	args = append(args, page.URLCanon, model.StateInflight, page.InflightAt)

	query := "UPDATE pages SET " + setClauses + " WHERE url_canon = ? AND state = ? AND inflight_at = ?"
	result, err := f.db.Conn().ExecContext(ctx, query, args...)
	if err != nil {
		return err
	}
	rows, err := result.RowsAffected()
	if err != nil {
		return fmt.Errorf("updatePageState rows affected: %w", err)
	}
	if rows == 0 {
		return fmt.Errorf("updatePageState: %s not updated (not INFLIGHT under this claim; re-claimed or wrong key)", page.URLCanon)
	}
	return nil
}

// pushLinksSQLite adds new URLs to the frontier and returns categorized
// counts. Duplicates are ignored (INSERT OR IGNORE).
func (f *Frontier) pushLinksSQLite(ctx context.Context, links []string, priority int) (*PushStats, error) {
	stats := &PushStats{}

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
		canon, domain, ok := canonicalizeLink(link)
		if !ok {
			stats.Malformed++
			continue
		}

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

// statsSQLite returns current frontier statistics.
func (f *Frontier) statsSQLite(ctx context.Context) (queued, inflight, done, failed int64, err error) {
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
