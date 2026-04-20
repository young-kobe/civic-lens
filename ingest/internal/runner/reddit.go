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

		// Store raw JSON
		hash, _ := rr.app.RawStore.Store(ctx, rawJSON, ".json")

		fmt.Printf("  Got %d posts (raw: %s)\n", len(posts), hash[:8])

		// Insert posts
		for _, post := range posts {
			post.FetchedAt = now
			post.RawHash = hash
			post.ExtractionVersion = "1.0"

			postID := post.Fullname
			if len(postID) > 3 && postID[:3] == "t3_" {
				postID = postID[3:]
			}

			bodies, _, err := client.FetchPostCommentBodiesPublic(ctx, post.Subreddit, postID, 5)
			if err == nil {
				for _, body := range bodies {
					if body != "[deleted]" && body != "[removed]" {
						post.Body += fmt.Sprintf("\n\n--- Comment:\n%s", body)
					}
				}
			}
			time.Sleep(500 * time.Millisecond)

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
