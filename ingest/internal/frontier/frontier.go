// Package frontier manages the crawl queue with state machine transitions
// against Postgres. Postgres-specific SQL (including the FOR UPDATE SKIP
// LOCKED claim and the optional per-domain balance quota) lives in
// frontier_postgres.go. This file holds the shared surface and the
// DomainQuota config type.
package frontier

import (
	"context"
	"log/slog"
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
	// quota configures optional per-domain balance caps. Read only by the
	// claim path (see frontier_postgres.go); nil when seeds.yaml has no
	// crawl_balance section, meaning "unlimited claiming".
	quota *DomainQuota
}

// DomainQuota configures per-domain balance quotas for the claim query
// (production is skewed, e.g. cbsnews 2,404 docs vs npr 65). A nil
// DomainQuota means unlimited claiming for every domain.
type DomainQuota struct {
	// Window bounds how far back the per-domain "already fetched" count is
	// computed, against raw.articles.fetched_at (see frontier_postgres.go's
	// claimItemsPostgresQuota doc comment for why raw.articles rather than
	// raw.pages). Zero/negative falls back to 24h.
	Window time.Duration
	// DefaultMaxPerWindow caps any domain not listed in PerDomain within
	// Window. Zero or negative means unlimited for domains without an
	// explicit override.
	DefaultMaxPerWindow int
	// PerDomain overrides DefaultMaxPerWindow for specific domains, keyed by
	// the lowercased host as util.ExtractDomain produces it (e.g.
	// "www.cbsnews.com"). An explicit cap of 0 blocks the domain outright.
	PerDomain map[string]int
}

// PushStats categorizes the outcomes of a PushLinks call so callers/ops
// can distinguish bad input from infrastructure failure.
type PushStats struct {
	Added     int64
	Malformed int64
	DBErrors  int64
}

// New creates a new Frontier. quota is optional (nil disables balance
// quotas).
func New(database *db.DB, maxRetries int, quota *DomainQuota) *Frontier {
	return &Frontier{
		db:         database,
		maxRetries: maxRetries,
		quota:      quota,
	}
}

// RecoverStale requeues any items stuck in INFLIGHT state for too long.
// This provides crash recovery and is called automatically by EnsureRecovered.
//
// Only rows whose inflight_at is older than staleAge are recovered, so an
// in-progress fetch is skipped as long as staleAge exceeds the fetch timeout
// (the default 10m stale age is far above the 30s request timeout). Even if an
// operator tunes staleAge below the fetch timeout and a still-live row is
// recovered and re-claimed, the claim guard in updatePageState makes the
// original worker's completion a no-op error rather than a clobber, so
// exclusivity (A3) holds regardless. See recoverStalePostgres for the SQL.
func (f *Frontier) RecoverStale(ctx context.Context, staleAge time.Duration) (int64, error) {
	return f.recoverStalePostgres(ctx, staleAge)
}

// EnsureRecovered runs RecoverStale and logs the result. Every runner
// should call this at startup so INFLIGHT rows cannot leak if the runner
// crashes mid-batch; making it a Frontier method means new runners
// cannot forget the call.
func (f *Frontier) EnsureRecovered(ctx context.Context, staleAge time.Duration) {
	recovered, err := f.RecoverStale(ctx, staleAge)
	if err != nil {
		slog.Error("recover stale failed", "component", "frontier", "error", err)
		return
	}
	if recovered > 0 {
		slog.Info("recovered stale inflight items", "component", "frontier", "count", recovered)
	}
}

// ClaimItems atomically claims a batch of work items, moving rows from
// QUEUED to INFLIGHT via a single FOR UPDATE SKIP LOCKED statement (see
// claimItemsPostgres); the per-domain balance quota, when configured, is
// applied there too (claimItemsPostgresQuota).
func (f *Frontier) ClaimItems(ctx context.Context, batchSize int) ([]*model.Page, error) {
	return f.claimItemsPostgres(ctx, batchSize)
}

// MarkDone marks a page as successfully fetched.
func (f *Frontier) MarkDone(ctx context.Context, page *model.Page) error {
	return f.updatePageState(ctx, page, model.StateDone, map[string]any{
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
		return f.updatePageState(ctx, page, model.StateFailed, map[string]any{
			"last_error": errMsg,
		})
	}

	backoff := time.Duration(1<<uint(page.Retries)) * time.Minute
	nextFetch := time.Now().Add(backoff).Unix()

	return f.updatePageState(ctx, page, model.StateQueued, map[string]any{
		"retries":       "retries + 1", // sentinel so the expression is spliced, not parameterized
		"next_fetch_at": nextFetch,
		"last_error":    errMsg,
	})
}

// updatePageState applies a state transition plus a set of column updates.
// The WHERE clause guards on the claim (state = INFLIGHT AND inflight_at =
// the timestamp this worker claimed the row at), so a worker whose row was
// recovered by RecoverStale and re-claimed by another worker matches 0 rows
// and gets a "not claimed" error instead of silently clobbering the new
// claim (A3 exclusivity).
func (f *Frontier) updatePageState(ctx context.Context, page *model.Page, state model.PageState, updates map[string]any) error {
	return f.updatePageStatePostgres(ctx, page, state, updates)
}

// PushLinks adds new URLs to the frontier and returns categorized counts.
// Duplicates are ignored. Malformed URLs and DB insert errors are counted
// separately so ops can distinguish bad input from infrastructure trouble.
func (f *Frontier) PushLinks(ctx context.Context, links []string, priority int) (*PushStats, error) {
	if len(links) == 0 {
		return &PushStats{}, nil
	}
	return f.pushLinksPostgres(ctx, links, priority)
}

// canonicalizeLink resolves a raw link to (canon, domain, ok). Shared with
// pushLinksPostgres so the malformed-URL classification lives in one place.
func canonicalizeLink(link string) (canon, domain string, ok bool) {
	c, err := util.CanonicalizeURL(link)
	if err != nil {
		return "", "", false
	}
	return c, util.ExtractDomain(c), true
}

// Stats returns current frontier statistics.
func (f *Frontier) Stats(ctx context.Context) (queued, inflight, done, failed int64, err error) {
	return f.statsPostgres(ctx)
}
