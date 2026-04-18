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

	client := reddit.New(reddit.Config{
		UserAgent: cfg.Reddit.UserAgent,
	})

	now := time.Now().Unix()

	for _, subreddit := range cfg.Reddit.Subreddits {
		fmt.Printf("Fetching r/%s...\n", subreddit)

		var posts []model.RedditPost
		var rawJSON []byte
		var err error

		posts, rawJSON, err = client.FetchSubredditPostsPublic(ctx, subreddit, 25)

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

			comments, _, err := client.FetchPostCommentsPublic(ctx, post.Subreddit, postID, 5)
			if err == nil && len(comments) > 0 {
				for _, c := range comments {
					if c.Body != "" && c.Body != "[deleted]" && c.Body != "[removed]" {
						post.Body += fmt.Sprintf("\n\n--- Comment:\n%s", c.Body)
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
	_, err := rr.app.Database.Conn().ExecContext(ctx, `
		INSERT OR REPLACE INTO reddit_posts_raw 
		(fullname, subreddit, created_utc, fetched_at, title, body, score, num_comments, raw_hash, extraction_version)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
	`, post.Fullname, post.Subreddit, post.CreatedUTC, post.FetchedAt,
		post.Title, post.Body, post.Score, post.NumComments, post.RawHash, post.ExtractionVersion)

	return err
}
