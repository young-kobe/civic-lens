package runner

import (
	"context"
	"database/sql"
	"fmt"
	"log/slog"
	"strings"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/extract/x"
)

// Target resident raw.x_posts counts for `civic-ingest backfill-officials`,
// keyed off corpus.entities.editorial: true for the 16 hand-curated verified
// officials, false for the ~535 accounts promoted wholesale from
// known_political_x_accounts.yaml (see data/pg-migrations/0002_entity_registry_seed.sql
// and docs/DATABASE_SCHEMA.md's corpus.entities section). Editorial officials
// are the highest-signal surface Civic Lens tracks and get a deeper backfill.
const (
	editorialBackfillTarget = 100
	promotedBackfillTarget  = 25
)

// BackfillOfficialsResult summarizes one backfill-officials invocation.
type BackfillOfficialsResult struct {
	OfficialsProcessed int // fetched at least one post this run
	OfficialsSkipped   int // already at/above target, no fetch attempted
	OfficialsErrored   int
	PostsFetched       int
	SpendCents         int
}

// officialRow is one corpus.entities row this command cares about.
type officialRow struct {
	Handle    string // entity_key, already the bare lowercase X handle (see 0002 seed)
	Editorial bool
}

// targetFor returns the minimum resident raw.x_posts count for an official,
// per its curated provenance.
func targetFor(o officialRow) int {
	if o.Editorial {
		return editorialBackfillTarget
	}
	return promotedBackfillTarget
}

// postsNeeded is the shortfall between an official's target and its current
// stored-post count, floored at zero — "already at/above target" is not a
// negative shortfall, it is nothing left to fetch.
func postsNeeded(target, stored int) int {
	if stored >= target {
		return 0
	}
	return target - stored
}

// loadActiveOfficials reads every active official from the entity registry.
// entity_key IS the X handle for kind='official' rows (bare lowercase, no
// leading @ — see docs/DATABASE_SCHEMA.md's corpus.entities section and the
// 0002 seed migration). Alternate handles recorded in corpus.entity_aliases
// (30 rows total across the whole registry, e.g. @POTUS's @realDonaldTrump/
// @WhiteHouse aliases) are deliberately not walked here: the backfill target
// is defined per corpus.entities row (one official, one primary handle), not
// per handle, and the brief that authorized this command scoped it to
// corpus.entities without mentioning entity_aliases.
func loadActiveOfficials(ctx context.Context, conn *sql.DB) ([]officialRow, error) {
	rows, err := conn.QueryContext(ctx, `
		SELECT entity_key, editorial
		FROM corpus.entities
		WHERE kind = 'official' AND active = true
		ORDER BY entity_key
	`)
	if err != nil {
		return nil, fmt.Errorf("query active officials: %w", err)
	}
	defer rows.Close()

	var out []officialRow
	for rows.Next() {
		var o officialRow
		if err := rows.Scan(&o.Handle, &o.Editorial); err != nil {
			return nil, fmt.Errorf("scan official row: %w", err)
		}
		out = append(out, o)
	}
	return out, rows.Err()
}

// countStoredPosts returns how many raw.x_posts rows already carry authorID
// (regardless of is_official_tier — this command cares about total resident
// history, not which pass captured it). This count IS the resumability
// state backfill-officials relies on: no separate progress table, re-running
// after a crash or cap-stop just recomputes this and picks up the shortfall.
func countStoredPosts(ctx context.Context, conn *sql.DB, authorID string) (int, error) {
	var n int
	err := conn.QueryRowContext(ctx,
		`SELECT COUNT(*) FROM raw.x_posts WHERE author_id = $1`, authorID,
	).Scan(&n)
	if err != nil {
		return 0, fmt.Errorf("count stored posts for author %s: %w", authorID, err)
	}
	return n, nil
}

// backfillDeps bundles the two corpus/raw reads the walk needs. Splitting
// them out as injectable functions (rather than calling loadActiveOfficials/
// countStoredPosts directly) is what makes runBackfillOfficials unit-testable
// with fakes for cap-stop/shortfall/resume-skip logic, independent of a real
// Postgres connection — the concrete Postgres-backed implementations are
// exercised separately by the gated integration tests.
type backfillDeps struct {
	listOfficials func(ctx context.Context) ([]officialRow, error)
	storedCount   func(ctx context.Context, authorID string) (int, error)
}

// RunBackfillOfficials is the `civic-ingest backfill-officials` entry point:
// bootstraps the X client from config the same way Run does, and wires the
// Postgres-backed deps before delegating to runBackfillOfficials — the same
// Run/runOfficialsPass split the existing officials pass uses so the walk
// itself stays testable against a stub client.
func (xr *XRunner) RunBackfillOfficials(ctx context.Context, spendCapCents int) (*BackfillOfficialsResult, error) {
	cfg := xr.app.Config
	xr.client = x.NewFromEnv(x.Config{
		BearerToken:     cfg.X.BearerToken,
		UserAgent:       cfg.X.UserAgent,
		MaxRequestsHour: cfg.X.MaxRequestsHour,
	})
	if xr.client.BearerToken() == "" {
		return nil, fmt.Errorf("X bearer token not configured (set x.bearer_token in seeds.yaml or X_BEARER_TOKEN env var)")
	}

	conn := xr.app.Database.Conn()
	deps := backfillDeps{
		listOfficials: func(ctx context.Context) ([]officialRow, error) {
			return loadActiveOfficials(ctx, conn)
		},
		storedCount: func(ctx context.Context, authorID string) (int, error) {
			return countStoredPosts(ctx, conn, authorID)
		},
	}

	budget, err := NewXBudgetTracker(ctx, xr.app.Database, cfg.X.MonthlyBudgetCents)
	if err != nil {
		return nil, fmt.Errorf("x budget tracker: %w", err)
	}

	return xr.runBackfillOfficials(ctx, spendCapCents, deps, budget)
}

// runBackfillOfficials walks every active official and tops up raw.x_posts
// to its target (editorialBackfillTarget or promotedBackfillTarget) with one
// UserTimeline call per official — the API's max_results ceiling of 100
// already covers both targets in a single page, so no pagination is needed
// to close a shortfall starting from zero (the common case: most officials
// in the registry have never been fetched by the topic-search or YAML-driven
// officials passes). An official whose page mostly overlaps posts already
// stored (upsert no-ops) may still fall short after one run; the
// resumability contract (stored count IS the checkpoint, see
// countStoredPosts) means a later invocation picks up the remainder, same as
// a cap-stop or a crash.
//
// spendCapCents is a single-use ceiling for THIS invocation only, tracked
// in-memory (spent) and independent of the persistent monthly ceiling in
// ops.x_api_budget — but every call's cost is still recorded through the
// existing budget tracker so the monthly ledger reflects backfill spend too.
// Per-official errors are logged and counted, never abort the walk; the
// returned error (non-nil exactly when OfficialsErrored > 0) is what makes
// the command exit nonzero.
func (xr *XRunner) runBackfillOfficials(
	ctx context.Context,
	spendCapCents int,
	deps backfillDeps,
	budget *XBudgetTracker,
) (*BackfillOfficialsResult, error) {
	officials, err := deps.listOfficials(ctx)
	if err != nil {
		return nil, err
	}

	res := &BackfillOfficialsResult{}
	spent := 0
	now := time.Now().Unix()
	var errored []string

	for _, off := range officials {
		if spent >= spendCapCents {
			fmt.Printf("Spend cap reached ($%.2f) — stopping before @%s\n", float64(spendCapCents)/100.0, off.Handle)
			break
		}

		target := targetFor(off)

		userID, hydratedUser, err := xr.resolveOfficialUserID(ctx, off.Handle, budget)
		if err != nil {
			res.OfficialsErrored++
			errored = append(errored, off.Handle)
			slog.Error("official lookup failed", "component", "runner.backfill_officials", "handle", off.Handle, "error", err)
			continue
		}
		// A non-nil hydratedUser means resolveOfficialUserID went to the API
		// rather than the raw.x_users cache — the one case that billed a
		// user-resource (already Recorded internally); track it against our
		// own invocation-scoped cap too.
		if hydratedUser != nil {
			spent += ceilDiv(centsPerUser, centsConversionUnit)
			hydratedUser.FetchedAt = now
			if err := xr.insertUser(ctx, *hydratedUser); err != nil {
				slog.Error("official user insert failed", "component", "runner.backfill_officials", "handle", off.Handle, "error", err)
			}
		}

		storedCount, err := deps.storedCount(ctx, userID)
		if err != nil {
			res.OfficialsErrored++
			errored = append(errored, off.Handle)
			slog.Error("stored-count query failed", "component", "runner.backfill_officials", "handle", off.Handle, "error", err)
			continue
		}

		need := postsNeeded(target, storedCount)
		if need == 0 {
			res.OfficialsSkipped++
			fmt.Printf("  @%s already at target (%d/%d) — skipped\n", off.Handle, storedCount, target)
			continue
		}

		if spent >= spendCapCents {
			fmt.Printf("Spend cap reached ($%.2f) — stopping before @%s's timeline fetch\n", float64(spendCapCents)/100.0, off.Handle)
			break
		}

		if err := xr.fetchAndStoreShortfall(ctx, off.Handle, userID, need, now, budget, &spent, res); err != nil {
			res.OfficialsErrored++
			errored = append(errored, off.Handle)
			continue
		}
		res.OfficialsProcessed++
	}

	res.SpendCents = spent

	if len(errored) > 0 {
		return res, fmt.Errorf("backfill-officials: %d official(s) errored: %s", len(errored), strings.Join(errored, ", "))
	}
	return res, nil
}

// fetchAndStoreShortfall pulls up to `need` recent posts for one official's
// userID, stores the raw response, upserts the posts (tagged official-tier,
// same as the existing officials pass) and any hydrated users, and records
// spend both against the invocation-scoped cap (*spent) and the persistent
// monthly budget. Split out of runBackfillOfficials to keep that function
// under the file's ~60-line-per-function convention.
func (xr *XRunner) fetchAndStoreShortfall(
	ctx context.Context,
	handle, userID string,
	need int,
	fetchedAt int64,
	budget *XBudgetTracker,
	spent *int,
	res *BackfillOfficialsResult,
) error {
	resp, rawJSON, err := xr.client.UserTimeline(ctx, userID, need)
	if err != nil {
		slog.Error("official timeline fetch failed", "component", "runner.backfill_officials", "handle", handle, "error", err)
		return err
	}

	hash, err := xr.app.RawStore.Store(ctx, rawJSON, ".json")
	if err != nil {
		// Never persist official posts with an empty raw_hash — that breaks
		// A6/A7 traceability, matching the existing officials pass.
		slog.Error("official raw store failed", "component", "runner.backfill_officials", "handle", handle, "error", err)
		return err
	}
	posts, hydrated := x.ToModels(resp)

	if err := budget.Record(ctx, len(posts), len(hydrated)); err != nil {
		slog.Error("budget record failed", "component", "runner.backfill_officials", "handle", handle, "error", err)
	}
	*spent += ceilDiv(len(posts)*centsPerPost+len(hydrated)*centsPerUser, centsConversionUnit)

	insertedHere := 0
	for _, post := range posts {
		post.FetchedAt = fetchedAt
		post.RawHash = hash
		post.ExtractionVersion = "1.0"
		if err := xr.insertOfficialPost(ctx, post); err != nil {
			slog.Error("official post insert failed", "component", "runner.backfill_officials", "handle", handle, "error", err)
			continue
		}
		insertedHere++
	}
	res.PostsFetched += insertedHere

	for _, user := range hydrated {
		user.FetchedAt = fetchedAt
		user.RawHash = hash
		if err := xr.insertUser(ctx, user); err != nil {
			slog.Error("official user expansion insert failed", "component", "runner.backfill_officials", "handle", handle, "error", err)
		}
	}

	fmt.Printf("  @%s: +%d fetched (raw: %s)\n", handle, insertedHere, safePrefix(hash, 8))
	return nil
}
