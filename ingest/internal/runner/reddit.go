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

// RunReddit fetches posts from configured subreddits.
func RunReddit(ctx context.Context, a *app.App) (*RedditResult, error) {
	cfg := a.Config

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
	var subredditsProcessed, postsIngested int

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
		hash, _ := a.RawStore.Store(ctx, rawJSON, ".json")

		fmt.Printf("  Got %d posts (raw: %s)\n", len(posts), hash[:8])

		// Insert posts
		for _, post := range posts {
			post.FetchedAt = now
			post.RawHash = hash
			post.ExtractionVersion = "1.0"

			_, err := a.Database.Conn().ExecContext(ctx, `
				INSERT OR REPLACE INTO reddit_posts_raw 
				(fullname, subreddit, created_utc, fetched_at, title, body, score, num_comments, raw_hash, extraction_version)
				VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
			`, post.Fullname, post.Subreddit, post.CreatedUTC, post.FetchedAt,
				post.Title, post.Body, post.Score, post.NumComments, post.RawHash, post.ExtractionVersion)

			if err != nil {
				fmt.Printf("  Insert error: %v\n", err)
			} else {
				postsIngested++
			}
		}

		subredditsProcessed++

		// Brief pause to be polite (public API has rate limits)
		if usePublicAPI {
			time.Sleep(2 * time.Second)
		}
	}

	return &RedditResult{
		SubredditsProcessed: subredditsProcessed,
		PostsIngested:       postsIngested,
	}, nil
}
