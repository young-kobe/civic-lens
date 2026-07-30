package runner

import (
	"context"
	"fmt"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/x"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// XResult holds X ingestion statistics.
type XResult struct {
	QueriesProcessed int
	PostsIngested    int
	UsersIngested    int
	RequestsMade     int64
	Officials        OfficialsResult
}

// XRunner orchestrates X post ingestion.
type XRunner struct {
	app              *app.App
	client           *x.Client
	queriesProcessed int
	postsIngested    int
	usersIngested    int
}

// NewXRunner creates an XRunner with the given dependencies.
func NewXRunner(a *app.App) *XRunner {
	return &XRunner{app: a}
}

// Run fetches posts from X using configured political queries. Aborts the
// loop cleanly if the month-to-date budget ceiling is reached; partial
// progress is persisted.
func (xr *XRunner) Run(ctx context.Context) (*XResult, error) {
	cfg := xr.app.Config

	// Reset any INFLIGHT rows left behind by a crashed previous run.
	xr.app.Frontier.EnsureRecovered(ctx, cfg.Crawl.StaleInflightAge)

	xr.client = x.NewFromEnv(x.Config{
		BearerToken:     cfg.X.BearerToken,
		UserAgent:       cfg.X.UserAgent,
		MaxRequestsHour: cfg.X.MaxRequestsHour,
	})

	if xr.client.BearerToken() == "" {
		return nil, fmt.Errorf("X bearer token not configured (set x.bearer_token in seeds.yaml or X_BEARER_TOKEN env var)")
	}

	budget, err := NewXBudgetTracker(ctx, xr.app.Database, cfg.X.MonthlyBudgetCents)
	if err != nil {
		return nil, fmt.Errorf("x budget tracker: %w", err)
	}
	start := budget.Summary()
	fmt.Printf("X budget %s: $%0.2f of $%0.2f used (%d posts / %d users / %d reqs)\n",
		start.MonthKey,
		float64(start.EstimatedCents)/100.0,
		float64(start.CeilingCents)/100.0,
		start.PostCount, start.UserCount, start.RequestCount)

	now := time.Now().Unix()

	// Officials pass runs FIRST so a tight monthly budget never starves the
	// verified-officials surface — those accounts are the highest-signal
	// content we collect, and topic queries can degrade more gracefully
	// (their coverage is sample-based to begin with). Failures inside the
	// pass are per-account; they never abort the topic-query loop below.
	// OfficialsListPath was resolved against the seeds.yaml directory by
	// config.Load, so it's always absolute by the time it reaches us here.
	maxPerOfficial := cfg.X.MaxTweetsPerOfficial
	if maxPerOfficial <= 0 {
		maxPerOfficial = 5
	}
	officialsRes, err := xr.runOfficialsPass(ctx, budget, cfg.X.OfficialsListPath, maxPerOfficial)
	if err != nil {
		// A torn-up YAML is the only failure that aborts the pass; everything
		// else is per-account and logged inline. We surface this rather than
		// skipping silently because a malformed registry is an editorial bug
		// the operator must fix.
		fmt.Printf("Officials pass error: %v\n", err)
	} else if officialsRes != nil {
		xr.postsIngested += officialsRes.PostsIngested
		xr.usersIngested += officialsRes.UsersIngested
		fmt.Printf(
			"Officials pass complete: %d/%d handles ok, %d failed, %d skipped, %d posts, %d users\n",
			officialsRes.HandlesSucceeded, officialsRes.HandlesAttempted,
			officialsRes.HandlesFailed, officialsRes.HandlesSkipped,
			officialsRes.PostsIngested, officialsRes.UsersIngested,
		)
	}

	for _, query := range cfg.X.PoliticalQueries {
		if budget.OverBudget() {
			s := budget.Summary()
			fmt.Printf("X budget ceiling hit: $%0.2f / $%0.2f — skipping remaining queries\n",
				float64(s.EstimatedCents)/100.0, float64(s.CeilingCents)/100.0)
			break
		}

		fmt.Printf("Searching X for: %s\n", query)

		resp, rawJSON, err := xr.client.SearchRecentPosts(ctx, query, cfg.X.MaxTweetsPerQuery)
		if err != nil {
			fmt.Printf("  Error: %v\n", err)
			continue
		}

		// Store raw JSON. Skip the whole batch on failure — persisting posts
		// with an empty raw_hash would create untraceable rows (A6/A7), and
		// the old hash[:8] log used to panic on the empty string.
		hash, err := xr.app.RawStore.Store(ctx, rawJSON, ".json")
		if err != nil {
			fmt.Printf("  Raw store error, skipping batch: %v\n", err)
			continue
		}

		posts, users := x.ToModels(resp)
		fmt.Printf("  Got %d posts, %d users (raw: %s)\n", len(posts), len(users), safePrefix(hash, 8))

		// Persist the budget hit BEFORE the DB inserts — if inserts fail we
		// still paid for the API call, and forgetting to record it would
		// let the ceiling drift over time.
		if err := budget.Record(ctx, len(posts), len(users)); err != nil {
			fmt.Printf("  Budget record error: %v\n", err)
		}

		// Insert posts
		for _, post := range posts {
			post.FetchedAt = now
			post.RawHash = hash
			post.ExtractionVersion = "1.0"

			if err := xr.insertPost(ctx, post); err != nil {
				fmt.Printf("  Post insert error: %v\n", err)
			} else {
				xr.postsIngested++
			}
		}

		// Insert users
		for _, user := range users {
			user.FetchedAt = now
			user.RawHash = hash

			if err := xr.insertUser(ctx, user); err != nil {
				fmt.Printf("  User insert error: %v\n", err)
			} else {
				xr.usersIngested++
			}
		}

		xr.queriesProcessed++

		// Check if we have more results (pagination)
		if resp.Meta != nil && resp.Meta.NextToken != "" {
			fmt.Printf("  (More results available, limited by budget)\n")
		}
	}

	end := budget.Summary()
	fmt.Printf("X budget %s now: $%0.2f of $%0.2f used (%d posts / %d users / %d reqs)\n",
		end.MonthKey,
		float64(end.EstimatedCents)/100.0,
		float64(end.CeilingCents)/100.0,
		end.PostCount, end.UserCount, end.RequestCount)

	result := &XResult{
		QueriesProcessed: xr.queriesProcessed,
		PostsIngested:    xr.postsIngested,
		UsersIngested:    xr.usersIngested,
		RequestsMade:     xr.client.RequestCount(),
	}
	if officialsRes != nil {
		result.Officials = *officialsRes
	}
	return result, nil
}

// insertPost upserts a search-query post into raw.x_posts. is_official_tier
// is never in the column list ON CONFLICT DO UPDATE writes, so a topic-query
// match on a tweet already ingested by the officials pass updates the
// metrics/text but leaves an existing is_official_tier=1 intact (I-6).
func (xr *XRunner) insertPost(ctx context.Context, post model.XPost) error {
	return xr.insertPostPostgres(ctx, post)
}

func (xr *XRunner) insertUser(ctx context.Context, user model.XUser) error {
	return xr.insertUserPostgres(ctx, user)
}
