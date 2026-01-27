package runner

import (
	"context"
	"fmt"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/x"
)

// XResult holds X ingestion statistics.
type XResult struct {
	QueriesProcessed int
	PostsIngested    int
	UsersIngested    int
	RequestsMade     int64
}

// RunX fetches posts from X using configured political queries.
func RunX(ctx context.Context, a *app.App) (*XResult, error) {
	cfg := a.Config

	client := x.NewFromEnv(x.Config{
		BearerToken:     cfg.X.BearerToken,
		UserAgent:       cfg.X.UserAgent,
		MaxRequestsHour: cfg.X.MaxRequestsHour,
	})

	if client.BearerToken() == "" {
		return nil, fmt.Errorf("X bearer token not configured (set x.bearer_token in seeds.yaml or X_BEARER_TOKEN env var)")
	}

	now := time.Now().Unix()
	var queriesProcessed, postsIngested, usersIngested int

	for _, query := range cfg.X.PoliticalQueries {
		fmt.Printf("Searching X for: %s\n", query)

		resp, rawJSON, err := client.SearchRecentPosts(ctx, query, cfg.X.MaxTweetsPerQuery)
		if err != nil {
			fmt.Printf("  Error: %v\n", err)
			continue
		}

		// Store raw JSON
		hash, _ := a.RawStore.Store(ctx, rawJSON, ".json")

		posts, users := x.ToModels(resp)
		fmt.Printf("  Got %d posts, %d users (raw: %s)\n", len(posts), len(users), hash[:8])

		// Insert posts
		for _, post := range posts {
			post.FetchedAt = now
			post.RawHash = hash
			post.ExtractionVersion = "1.0"

			_, err := a.Database.Conn().ExecContext(ctx, `
				INSERT OR REPLACE INTO x_posts_raw 
				(tweet_id, author_id, conversation_id, created_at, fetched_at, text, lang,
				 retweet_count, reply_count, like_count, quote_count,
				 place_id, place_country_code, place_full_name,
				 context_annotations_json, in_reply_to_user_id,
				 referenced_tweet_id, referenced_tweet_type,
				 raw_hash, extraction_version)
				VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
			`, post.TweetID, post.AuthorID, post.ConversationID,
				post.CreatedAt, post.FetchedAt, post.Text, post.Lang,
				post.RetweetCount, post.ReplyCount, post.LikeCount, post.QuoteCount,
				post.PlaceID, post.PlaceCountryCode, post.PlaceFullName,
				post.ContextAnnotationsJSON, post.InReplyToUserID,
				post.ReferencedTweetID, post.ReferencedTweetType,
				post.RawHash, post.ExtractionVersion)

			if err != nil {
				fmt.Printf("  Post insert error: %v\n", err)
			} else {
				postsIngested++
			}
		}

		// Insert users
		for _, user := range users {
			user.FetchedAt = now
			user.RawHash = hash

			verified := 0
			if user.Verified {
				verified = 1
			}
			protected := 0
			if user.Protected {
				protected = 1
			}

			_, err := a.Database.Conn().ExecContext(ctx, `
				INSERT OR REPLACE INTO x_users_raw 
				(user_id, username, name, location, description, created_at,
				 followers_count, following_count, tweet_count, listed_count,
				 verified, verified_type, profile_image_url, protected,
				 fetched_at, raw_hash)
				VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
			`, user.UserID, user.Username, user.Name, user.Location, user.Description,
				user.CreatedAt, user.FollowersCount, user.FollowingCount,
				user.TweetCount, user.ListedCount, verified, user.VerifiedType,
				user.ProfileImageURL, protected, user.FetchedAt, user.RawHash)

			if err != nil {
				fmt.Printf("  User insert error: %v\n", err)
			} else {
				usersIngested++
			}
		}

		queriesProcessed++

		// Check if we have more results (pagination)
		if resp.Meta != nil && resp.Meta.NextToken != "" {
			fmt.Printf("  (More results available, limited by budget)\n")
		}
	}

	return &XResult{
		QueriesProcessed: queriesProcessed,
		PostsIngested:    postsIngested,
		UsersIngested:    usersIngested,
		RequestsMade:     client.RequestCount(),
	}, nil
}
