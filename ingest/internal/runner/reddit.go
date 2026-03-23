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

	// Use public .json endpoints (no auth needed)
	usePublicAPI := cfg.Reddit.ClientID == ""
	if usePublicAPI {
		fmt.Println("Using public Reddit .json endpoints (no API credentials)")
	}

	client := reddit.New(reddit.Config{
		ClientID:     cfg.Reddit.ClientID,
		ClientSecret: cfg.Reddit.ClientSecret,
		UserAgent:    cfg.Reddit.UserAgent,
	})

	now := time.Now().Unix()

	for _, subreddit := range cfg.Reddit.Subreddits {
		fmt.Printf("Fetching r/%s...\n", subreddit)

		var posts []model.RedditPost
		var rawJSON []byte
		var err error

		if usePublicAPI {
			posts, rawJSON, err = client.FetchSubredditPostsPublic(ctx, subreddit, 25)
		} else {
			posts, rawJSON, err = client.FetchSubredditPosts(ctx, subreddit, 25)
		}

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

			if err := rr.insertPost(ctx, post); err != nil {
				fmt.Printf("  Insert error: %v\n", err)
			} else {
				rr.postsIngested++
			}
		}

		rr.subredditsProcessed++

		// Brief pause to be polite (public API has rate limits)
		if usePublicAPI {
			time.Sleep(2 * time.Second)
		}
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
