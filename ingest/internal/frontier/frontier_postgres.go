package frontier

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"math"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// pgEpochZero is the "never claimed" sentinel stored in raw.pages.inflight_at
// (default to_timestamp(0), see 0001_north_star.sql), mirroring the SQLite
// path's inflight_at = 0 sentinel.
var pgEpochZero = time.Unix(0, 0).UTC()

// pgNow returns the current time truncated to whole-second precision. Every
// TIMESTAMPTZ value this package writes into inflight_at must round-trip
// exactly through model.Page.InflightAt (an int64 Unix-seconds field): a
// claim writes inflight_at = pgNow() and remembers only its .Unix() value on
// the claimed *model.Page, and a later MarkDone/MarkFailed reconstructs
// time.Unix(page.InflightAt, 0) to match it back in the claim-guard WHERE
// clause. time.Now() carries sub-second precision that Postgres would store
// but the int64 round-trip cannot reproduce, so every "now" used for
// inflight_at must go through this truncation, not time.Now() directly.
func pgNow() time.Time {
	return time.Now().UTC().Truncate(time.Second)
}

// pgStateString maps model.PageState to the raw.page_state enum labels.
func pgStateString(s model.PageState) string {
	switch s {
	case model.StateInflight:
		return "inflight"
	case model.StateDone:
		return "done"
	case model.StateFailed:
		return "failed"
	default:
		return "queued"
	}
}

// recoverStalePostgres is the Postgres implementation of RecoverStale: same
// invariant (A3 crash recovery, INFLIGHT -> QUEUED past staleAge) as
// recoverStaleSQLite, expressed with the raw.page_state enum and TIMESTAMPTZ.
func (f *Frontier) recoverStalePostgres(ctx context.Context, staleAge time.Duration) (int64, error) {
	now := pgNow()
	cutoff := now.Add(-staleAge)

	result, err := f.db.Conn().ExecContext(ctx, `
		UPDATE raw.pages
		SET state = 'queued'::raw.page_state, next_fetch_at = $1, inflight_at = $2
		WHERE state = 'inflight'::raw.page_state AND inflight_at < $3 AND inflight_at > $2
	`, now, pgEpochZero, cutoff)
	if err != nil {
		return 0, fmt.Errorf("recover stale: %w", err)
	}
	return result.RowsAffected()
}

// claimItemsPostgres dispatches to the quota-aware or plain claim query.
// The quota is a Postgres-only, additive concept (see DomainQuota's doc
// comment) — a nil f.quota takes the plain path, identical in effect to
// today's unlimited claiming.
func (f *Frontier) claimItemsPostgres(ctx context.Context, batchSize int) ([]*model.Page, error) {
	if f.quota != nil {
		return f.claimItemsPostgresQuota(ctx, batchSize)
	}
	return f.claimItemsPostgresPlain(ctx, batchSize)
}

// claimItemsPostgresPlain claims a batch of QUEUED rows via a single
// writable CTE using FOR UPDATE SKIP LOCKED — this is the plan's marquee
// concurrency win over the SQLite busy-timeout dance: two concurrent
// claimers never see the same candidate row, because the second claimer's
// FOR UPDATE simply skips whatever the first has already locked, instead of
// blocking or retrying on SQLITE_BUSY.
func (f *Frontier) claimItemsPostgresPlain(ctx context.Context, batchSize int) ([]*model.Page, error) {
	now := pgNow()
	rows, err := f.db.Conn().QueryContext(ctx, `
		WITH candidates AS (
			SELECT url_canon
			FROM raw.pages
			WHERE state = 'queued'::raw.page_state AND next_fetch_at <= $1
			ORDER BY priority DESC, next_fetch_at ASC
			LIMIT $2
			FOR UPDATE SKIP LOCKED
		)
		UPDATE raw.pages p
		SET state = 'inflight'::raw.page_state, inflight_at = $1
		FROM candidates c
		WHERE p.url_canon = c.url_canon
		RETURNING p.url_canon, p.url_raw, p.domain, p.priority, p.retries
	`, now, batchSize)
	if err != nil {
		return nil, fmt.Errorf("claim items: %w", err)
	}
	defer rows.Close()
	return scanClaimedPages(rows, now)
}

// claimWithQuotaSQL is claimItemsPostgresPlain's query plus a per-domain
// balance quota. JUDGMENT CALL: the windowed "already fetched" count reads
// raw.articles.fetched_at rather than raw.pages, because raw.pages carries
// no completion timestamp — inflight_at is reset to the sentinel on every
// terminal transition (see updatePageStatePostgres), so a time-windowed count
// is only recoverable from raw.articles, which the extraction writer stamps
// with the real fetch time. This also scopes quotas to the news crawl (the
// domain-skew problem the plan calls out) since reddit/x posts have no
// domain. Preference ordering ranks candidates by how far under their quota
// their domain is (fetched/cap ratio ascending) rather than by raw fetched
// count, so a heavily-capped niche domain and a loosely-capped major outlet
// are compared on the same relative scale.
const claimWithQuotaSQL = `
WITH domain_counts AS (
	SELECT domain, COUNT(*) AS fetched_n
	FROM raw.articles
	WHERE fetched_at >= $1
	GROUP BY domain
),
domain_caps AS (
	SELECT key AS domain, value::int AS cap_n
	FROM jsonb_each_text($2::jsonb)
),
candidates AS (
	SELECT p.url_canon
	FROM raw.pages p
	LEFT JOIN domain_counts dc ON dc.domain = p.domain
	LEFT JOIN domain_caps dcap ON dcap.domain = p.domain
	WHERE p.state = 'queued'::raw.page_state
	  AND p.next_fetch_at <= $3
	  AND COALESCE(dc.fetched_n, 0) < COALESCE(dcap.cap_n, $4)
	ORDER BY COALESCE(dc.fetched_n, 0)::float8 / GREATEST(COALESCE(dcap.cap_n, $4), 1) ASC,
	         p.priority DESC, p.next_fetch_at ASC
	LIMIT $5
	FOR UPDATE SKIP LOCKED
)
UPDATE raw.pages p
SET state = 'inflight'::raw.page_state, inflight_at = $3
FROM candidates c
WHERE p.url_canon = c.url_canon
RETURNING p.url_canon, p.url_raw, p.domain, p.priority, p.retries
`

// claimItemsPostgresQuota is claimItemsPostgresPlain with the per-domain
// balance quota applied (see claimWithQuotaSQL's doc comment for the SQL
// design). Domains at or over their cap are excluded outright; domains under
// cap are preferred in proportion to how far under they are.
func (f *Frontier) claimItemsPostgresQuota(ctx context.Context, batchSize int) ([]*model.Page, error) {
	now := pgNow()
	windowStart := now.Add(-f.quota.effectiveWindow())
	capsJSON, err := f.quota.perDomainJSON()
	if err != nil {
		return nil, fmt.Errorf("marshal domain caps: %w", err)
	}

	rows, err := f.db.Conn().QueryContext(ctx, claimWithQuotaSQL,
		windowStart, capsJSON, now, f.quota.effectiveDefaultCap(), batchSize)
	if err != nil {
		return nil, fmt.Errorf("claim items (quota): %w", err)
	}
	defer rows.Close()
	return scanClaimedPages(rows, now)
}

// scanClaimedPages scans the RETURNING columns shared by both the plain and
// quota-aware claim queries.
func scanClaimedPages(rows *sql.Rows, claimedAt time.Time) ([]*model.Page, error) {
	var pages []*model.Page
	for rows.Next() {
		p := &model.Page{State: model.StateInflight, InflightAt: claimedAt.Unix()}
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

// effectiveWindow returns q.Window, defaulting to 24h when unset — mirrors
// the same default config.Load applies when a crawl_balance section omits
// the window key, kept here too so a DomainQuota built directly (as the
// gated Postgres tests do) gets the same default without going through
// config.Load.
func (q *DomainQuota) effectiveWindow() time.Duration {
	if q.Window <= 0 {
		return 24 * time.Hour
	}
	return q.Window
}

// effectiveDefaultCap returns q.DefaultMaxPerWindow, or a large sentinel
// when unset (0 or negative) so "no default configured" reads as
// unlimited rather than blocking every domain not explicitly listed.
func (q *DomainQuota) effectiveDefaultCap() int {
	if q.DefaultMaxPerWindow <= 0 {
		return math.MaxInt32
	}
	return q.DefaultMaxPerWindow
}

// perDomainJSON marshals PerDomain for binding as a jsonb query parameter.
// A nil map marshals to "null", which jsonb_each_text rejects, so nil is
// normalized to an empty object first.
func (q *DomainQuota) perDomainJSON() (string, error) {
	domains := q.PerDomain
	if domains == nil {
		domains = map[string]int{}
	}
	b, err := json.Marshal(domains)
	if err != nil {
		return "", err
	}
	return string(b), nil
}

// pgSetClause builds the SET clause (starting at placeholder $3, since $1
// and $2 are always state and inflight_at) and its ordered args for
// updatePageStatePostgres, mirroring updatePageStateSQLite's column set
// exactly so the two backends stay behaviorally equivalent.
func pgSetClause(updates map[string]any) (string, []any, error) {
	clause := ""
	var args []any
	argN := 2
	for _, col := range []string{"http_status", "content_sha256", "etag", "last_modified", "last_error", "next_fetch_at", "retries"} {
		v, ok := updates[col]
		if !ok {
			continue
		}
		if col == "retries" {
			expr, isExpr := v.(string)
			if !isExpr || expr != "retries + 1" {
				return "", nil, fmt.Errorf("updatePageState: invalid retries expression")
			}
			clause += ", retries = retries + 1"
			continue
		}
		if col == "next_fetch_at" {
			v = time.Unix(v.(int64), 0).UTC()
		}
		argN++
		clause += fmt.Sprintf(", %s = $%d", col, argN)
		args = append(args, v)
	}
	return clause, args, nil
}

// updatePageStatePostgres is the Postgres implementation of updatePageState
// — same claim-guard semantics (A3 exclusivity) as updatePageStateSQLite,
// expressed with $N placeholders, an enum cast on state, and TIMESTAMPTZ.
func (f *Frontier) updatePageStatePostgres(ctx context.Context, page *model.Page, state model.PageState, updates map[string]any) error {
	setClause, extraArgs, err := pgSetClause(updates)
	if err != nil {
		return err
	}

	args := append([]any{pgStateString(state), pgEpochZero}, extraArgs...)
	base := len(args)
	args = append(args, page.URLCanon, pgStateString(model.StateInflight), time.Unix(page.InflightAt, 0).UTC())

	query := fmt.Sprintf(
		"UPDATE raw.pages SET state = $1::raw.page_state, inflight_at = $2%s WHERE url_canon = $%d AND state = $%d::raw.page_state AND inflight_at = $%d",
		setClause, base+1, base+2, base+3,
	)
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

// pushLinksPostgres is the Postgres implementation of PushLinks.
func (f *Frontier) pushLinksPostgres(ctx context.Context, links []string, priority int) (*PushStats, error) {
	stats := &PushStats{}

	stmt, err := f.db.Conn().PrepareContext(ctx, `
		INSERT INTO raw.pages (url_canon, url_raw, domain, state, priority, next_fetch_at)
		VALUES ($1, $2, $3, $4::raw.page_state, $5, $6)
		ON CONFLICT (url_canon) DO NOTHING
	`)
	if err != nil {
		return stats, fmt.Errorf("prepare insert: %w", err)
	}
	defer stmt.Close()

	now := pgNow()

	for _, link := range links {
		canon, domain, ok := canonicalizeLink(link)
		if !ok {
			stats.Malformed++
			continue
		}

		result, err := stmt.ExecContext(ctx, canon, link, domain, pgStateString(model.StateQueued), priority, now)
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

// statsPostgres returns current frontier statistics.
func (f *Frontier) statsPostgres(ctx context.Context) (queued, inflight, done, failed int64, err error) {
	row := f.db.Conn().QueryRowContext(ctx, `
		SELECT
			COUNT(*) FILTER (WHERE state = 'queued'),
			COUNT(*) FILTER (WHERE state = 'inflight'),
			COUNT(*) FILTER (WHERE state = 'done'),
			COUNT(*) FILTER (WHERE state = 'failed')
		FROM raw.pages
	`)
	err = row.Scan(&queued, &inflight, &done, &failed)
	return
}
