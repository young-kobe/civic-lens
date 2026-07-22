package runner

import (
	"context"
	"fmt"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/reddit"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// RedditResult holds Reddit ingestion statistics.
type RedditResult struct {
	SubredditsProcessed int
	PostsIngested       int
}

// RedditRunner orchestrates Reddit post ingestion.
type RedditRunner struct {
	app                 *app.App
	subredditsProcessed int
	postsIngested       int
}

// NewRedditRunner creates a RedditRunner with the given dependencies.
func NewRedditRunner(a *app.App) *RedditRunner {
	return &RedditRunner{app: a}
}

// Run fetches posts from configured subreddits.
func (rr *RedditRunner) Run(ctx context.Context) (*RedditResult, error) {
	cfg := rr.app.Config

	// Reset any INFLIGHT rows left behind by a crashed previous run.
	rr.app.Frontier.EnsureRecovered(ctx, cfg.Crawl.StaleInflightAge)

	client := reddit.New(reddit.Config{
		UserAgent: cfg.Reddit.UserAgent,
	})

	now := time.Now().Unix()

	for _, subreddit := range cfg.Reddit.Subreddits {
		fmt.Printf("Fetching r/%s...\n", subreddit)

		posts, rawJSON, err := client.FetchSubredditPostsPublic(ctx, subreddit, 25)
		if err != nil {
			fmt.Printf("  Error: %v\n", err)
			continue
		}

		// Store raw JSON. Skip the subreddit on failure rather than persist
		// posts with an empty raw_hash (A6/A7 traceability); the old hash[:8]
		// log also panicked on the empty string.
		hash, err := rr.app.RawStore.Store(ctx, rawJSON, ".json")
		if err != nil {
			fmt.Printf("  Raw store error, skipping subreddit: %v\n", err)
			continue
		}

		fmt.Printf("  Got %d posts (raw: %s)\n", len(posts), safePrefix(hash, 8))

		// Insert posts. NOTE: we intentionally do NOT merge comment bodies
		// into post.Body. The comments endpoint's raw JSON is a separate blob
		// with its own hash, and reddit_posts_raw.raw_hash points at the
		// listing JSON — merging comment text here would persist records whose
		// raw_hash does not cover them, breaking A6/A7 traceability (I-11).
		// Reddit ingestion is currently disabled; before re-enabling comment
		// collection, store the comments-endpoint JSON via RawStore and write
		// the comment rows to reddit_comments_raw keyed by that hash.
		for _, post := range posts {
			post.FetchedAt = now
			post.RawHash = hash
			post.ExtractionVersion = "1.0"

			if err := rr.insertPost(ctx, post); err != nil {
				fmt.Printf("  Insert error: %v\n", err)
			} else {
				rr.postsIngested++
			}
		}

		rr.subredditsProcessed++
	}

	return &RedditResult{
		SubredditsProcessed: rr.subredditsProcessed,
		PostsIngested:       rr.postsIngested,
	}, nil
}

func (rr *RedditRunner) insertPost(ctx context.Context, post model.RedditPost) error {
	if rr.app.Database.IsPostgres() {
		return rr.insertPostPostgres(ctx, post)
	}
	return upsertRow(ctx, rr.app.Database.Conn(), "reddit_posts_raw",
		[]string{
			"fullname", "subreddit", "created_utc", "fetched_at", "title",
			"body", "score", "num_comments", "raw_hash", "extraction_version",
		},
		[]any{
			post.Fullname, post.Subreddit, post.CreatedUTC, post.FetchedAt, post.Title,
			post.Body, post.Score, post.NumComments, post.RawHash, post.ExtractionVersion,
		},
	)
}
