package frontier

import (
	"context"
	"database/sql"
	"fmt"
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

// New creates a new Frontier.
func New(database *db.DB, maxRetries int) *Frontier {
	return &Frontier{
		db:         database,
		maxRetries: maxRetries,
	}
}

// RecoverStale requeues any items stuck in INFLIGHT state for too long.
// This provides crash recovery.
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

// ClaimItems atomically claims a batch of work items.
// Items are moved from QUEUED to INFLIGHT state.
func (f *Frontier) ClaimItems(ctx context.Context, batchSize int) ([]*model.Page, error) {
	now := time.Now().Unix()
	
	tx, err := f.db.Conn().BeginTx(ctx, nil)
	if err != nil {
		return nil, fmt.Errorf("begin transaction: %w", err)
	}
	defer tx.Rollback()
	
	// Select items ready to be fetched
	rows, err := tx.QueryContext(ctx, `
		SELECT url_canon, url_raw, domain, priority, retries
		FROM pages
		WHERE state = ? AND next_fetch_at <= ?
		ORDER BY priority DESC, next_fetch_at ASC
		LIMIT ?
	`, model.StateQueued, now, batchSize)
	
	if err != nil {
		return nil, fmt.Errorf("query pages: %w", err)
	}
	
	var pages []*model.Page
	var urls []string
	
	for rows.Next() {
		p := &model.Page{State: model.StateInflight, InflightAt: now}
		if err := rows.Scan(&p.URLCanon, &p.URLRaw, &p.Domain, &p.Priority, &p.Retries); err != nil {
			rows.Close()
			return nil, fmt.Errorf("scan page: %w", err)
		}
		pages = append(pages, p)
		urls = append(urls, p.URLCanon)
	}
	rows.Close()
	
	if len(pages) == 0 {
		return nil, nil
	}
	
	// Mark them as INFLIGHT
	stmt, err := tx.PrepareContext(ctx, `
		UPDATE pages SET state = ?, inflight_at = ? WHERE url_canon = ?
	`)
	if err != nil {
		return nil, fmt.Errorf("prepare update: %w", err)
	}
	defer stmt.Close()
	
	for _, u := range urls {
		if _, err := stmt.ExecContext(ctx, model.StateInflight, now, u); err != nil {
			return nil, fmt.Errorf("update page %s: %w", u, err)
		}
	}
	
	if err := tx.Commit(); err != nil {
		return nil, fmt.Errorf("commit: %w", err)
	}
	
	return pages, nil
}

// MarkDone marks a page as successfully fetched.
func (f *Frontier) MarkDone(ctx context.Context, page *model.Page) error {
	_, err := f.db.Conn().ExecContext(ctx, `
		UPDATE pages
		SET state = ?, http_status = ?, content_sha256 = ?, etag = ?, last_modified = ?, last_error = NULL, inflight_at = 0
		WHERE url_canon = ?
	`, model.StateDone, page.HTTPStatus, page.ContentSHA256, page.ETag, page.LastModified, page.URLCanon)
	
	return err
}

// MarkFailed marks a page as failed.
// If permanent is false and retries are available, schedules for retry with backoff.
func (f *Frontier) MarkFailed(ctx context.Context, page *model.Page, errMsg string, permanent bool) error {
	if permanent || page.Retries >= f.maxRetries {
		// Permanent failure
		_, err := f.db.Conn().ExecContext(ctx, `
			UPDATE pages
			SET state = ?, last_error = ?, inflight_at = 0
			WHERE url_canon = ?
		`, model.StateFailed, errMsg, page.URLCanon)
		return err
	}
	
	// Retry with exponential backoff + jitter
	backoff := time.Duration(1<<uint(page.Retries)) * time.Minute
	nextFetch := time.Now().Add(backoff).Unix()
	
	_, err := f.db.Conn().ExecContext(ctx, `
		UPDATE pages
		SET state = ?, retries = retries + 1, next_fetch_at = ?, last_error = ?, inflight_at = 0
		WHERE url_canon = ?
	`, model.StateQueued, nextFetch, errMsg, page.URLCanon)
	
	return err
}

// PushLinks adds new URLs to the frontier.
// Duplicates are ignored (INSERT OR IGNORE).
func (f *Frontier) PushLinks(ctx context.Context, links []string, priority int) (int64, error) {
	if len(links) == 0 {
		return 0, nil
	}
	
	stmt, err := f.db.Conn().PrepareContext(ctx, `
		INSERT OR IGNORE INTO pages (url_canon, url_raw, domain, state, priority, next_fetch_at)
		VALUES (?, ?, ?, ?, ?, ?)
	`)
	if err != nil {
		return 0, fmt.Errorf("prepare insert: %w", err)
	}
	defer stmt.Close()
	
	var added int64
	now := time.Now().Unix()
	
	for _, link := range links {
		canon, err := util.CanonicalizeURL(link)
		if err != nil {
			continue // Skip malformed URLs
		}
		domain := util.ExtractDomain(canon)
		
		result, err := stmt.ExecContext(ctx, canon, link, domain, model.StateQueued, priority, now)
		if err != nil {
			continue // Skip on error
		}
		n, _ := result.RowsAffected()
		added += n
	}
	
	return added, nil
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
