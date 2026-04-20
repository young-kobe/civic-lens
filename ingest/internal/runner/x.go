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

// Run fetches posts from X using configured political queries.
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

	now := time.Now().Unix()

	for _, query := range cfg.X.PoliticalQueries {
		fmt.Printf("Searching X for: %s\n", query)

		resp, rawJSON, err := xr.client.SearchRecentPosts(ctx, query, cfg.X.MaxTweetsPerQuery)
		if err != nil {
			fmt.Printf("  Error: %v\n", err)
			continue
		}

		// Store raw JSON
		hash, _ := xr.app.RawStore.Store(ctx, rawJSON, ".json")

		posts, users := x.ToModels(resp)
		fmt.Printf("  Got %d posts, %d users (raw: %s)\n", len(posts), len(users), hash[:8])

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

	return &XResult{
		QueriesProcessed: xr.queriesProcessed,
		PostsIngested:    xr.postsIngested,
		UsersIngested:    xr.usersIngested,
		RequestsMade:     xr.client.RequestCount(),
	}, nil
}

func (xr *XRunner) insertPost(ctx context.Context, post model.XPost) error {
	return upsertRow(ctx, xr.app.Database.Conn(), "x_posts_raw",
		[]string{
			"tweet_id", "author_id", "conversation_id", "created_at", "fetched_at",
			"text", "lang", "retweet_count", "reply_count", "like_count", "quote_count",
			"place_id", "place_country_code", "place_full_name",
			"context_annotations_json", "in_reply_to_user_id",
			"referenced_tweet_id", "referenced_tweet_type",
			"raw_hash", "extraction_version",
		},
		[]any{
			post.TweetID, post.AuthorID, post.ConversationID, post.CreatedAt, post.FetchedAt,
			post.Text, post.Lang, post.RetweetCount, post.ReplyCount, post.LikeCount, post.QuoteCount,
			post.PlaceID, post.PlaceCountryCode, post.PlaceFullName,
			post.ContextAnnotationsJSON, post.InReplyToUserID,
			post.ReferencedTweetID, post.ReferencedTweetType,
			post.RawHash, post.ExtractionVersion,
		},
	)
}

func (xr *XRunner) insertUser(ctx context.Context, user model.XUser) error {
	verified := 0
	if user.Verified {
		verified = 1
	}
	protected := 0
	if user.Protected {
		protected = 1
	}

	return upsertRow(ctx, xr.app.Database.Conn(), "x_users_raw",
		[]string{
			"user_id", "username", "name", "location", "description", "created_at",
			"followers_count", "following_count", "tweet_count", "listed_count",
			"verified", "verified_type", "profile_image_url", "protected",
			"fetched_at", "raw_hash",
		},
		[]any{
			user.UserID, user.Username, user.Name, user.Location, user.Description, user.CreatedAt,
			user.FollowersCount, user.FollowingCount, user.TweetCount, user.ListedCount,
			verified, user.VerifiedType, user.ProfileImageURL, protected,
			user.FetchedAt, user.RawHash,
		},
	)
}
