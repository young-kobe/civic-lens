package x

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"sync/atomic"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"golang.org/x/time/rate"
)

// Client interfaces with X API v2.
type Client struct {
	httpClient   *http.Client
	bearerToken  string
	userAgent    string
	rateLimiter  *rate.Limiter
	requestCount int64 // Track for cost estimation
}

// Config holds X API credentials and settings.
type Config struct {
	BearerToken     string
	UserAgent       string
	MaxRequestsHour int // Budget control
}

// New creates a new X API v2 client.
func New(cfg Config) *Client {
	// Calculate rate limit from max requests per hour
	// Default to 100/hour if not specified (~$1.50/hour)
	maxReqHour := cfg.MaxRequestsHour
	if maxReqHour <= 0 {
		maxReqHour = 100
	}
	reqPerSec := float64(maxReqHour) / 3600.0

	return &Client{
		httpClient:  &http.Client{Timeout: 30 * time.Second},
		bearerToken: cfg.BearerToken,
		userAgent:   cfg.UserAgent,
		rateLimiter: rate.NewLimiter(rate.Limit(reqPerSec), 1),
	}
}

// NewFromEnv creates a client using the X_BEARER_TOKEN environment variable.
func NewFromEnv(cfg Config) *Client {
	if cfg.BearerToken == "" {
		cfg.BearerToken = os.Getenv("X_BEARER_TOKEN")
	}
	return New(cfg)
}

// RequestCount returns the number of API requests made.
func (c *Client) RequestCount() int64 {
	return atomic.LoadInt64(&c.requestCount)
}

// SearchRecentPosts searches for recent posts matching the query.
// Uses: GET /2/tweets/search/recent
func (c *Client) SearchRecentPosts(ctx context.Context, query string, maxResults int) (*SearchResponse, []byte, error) {
	if err := c.rateLimiter.Wait(ctx); err != nil {
		return nil, nil, fmt.Errorf("rate limiter: %w", err)
	}

	// Build URL with required fields and expansions
	baseURL := "https://api.x.com/2/tweets/search/recent"
	params := url.Values{}
	params.Set("query", query)
	params.Set("max_results", fmt.Sprintf("%d", min(maxResults, 100))) // API max is 100
	params.Set("tweet.fields", "author_id,created_at,text,public_metrics,geo,context_annotations,lang,conversation_id,in_reply_to_user_id,referenced_tweets")
	params.Set("user.fields", "id,username,name,location,description,created_at,public_metrics,verified,verified_type,protected,profile_image_url")
	params.Set("place.fields", "id,full_name,country,country_code")
	params.Set("expansions", "author_id,geo.place_id,referenced_tweets.id")

	reqURL := fmt.Sprintf("%s?%s", baseURL, params.Encode())
	req, err := http.NewRequestWithContext(ctx, "GET", reqURL, nil)
	if err != nil {
		return nil, nil, err
	}

	req.Header.Set("Authorization", "Bearer "+c.bearerToken)
	req.Header.Set("User-Agent", c.userAgent)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, nil, err
	}
	defer resp.Body.Close()

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, nil, err
	}

	atomic.AddInt64(&c.requestCount, 1)

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("API error %d: %s", resp.StatusCode, string(body))
	}

	var searchResp SearchResponse
	if err := json.Unmarshal(body, &searchResp); err != nil {
		return nil, body, fmt.Errorf("parse response: %w", err)
	}

	return &searchResp, body, nil
}

// SearchResponse represents the X API v2 search response.
type SearchResponse struct {
	Data     []TweetData `json:"data"`
	Includes *Includes   `json:"includes,omitempty"`
	Meta     *Meta       `json:"meta,omitempty"`
	Errors   []APIError  `json:"errors,omitempty"`
}

// TweetData represents a tweet from the API.
type TweetData struct {
	ID                 string              `json:"id"`
	Text               string              `json:"text"`
	AuthorID           string              `json:"author_id"`
	CreatedAt          string              `json:"created_at"`
	Lang               string              `json:"lang"`
	ConversationID     string              `json:"conversation_id"`
	InReplyToUserID    string              `json:"in_reply_to_user_id"`
	PublicMetrics      *PublicMetrics      `json:"public_metrics"`
	Geo                *Geo                `json:"geo"`
	ContextAnnotations []ContextAnnotation `json:"context_annotations"`
	ReferencedTweets   []ReferencedTweet   `json:"referenced_tweets"`
}

// PublicMetrics contains engagement counts.
type PublicMetrics struct {
	RetweetCount int `json:"retweet_count"`
	ReplyCount   int `json:"reply_count"`
	LikeCount    int `json:"like_count"`
	QuoteCount   int `json:"quote_count"`
}

// Geo contains place information.
type Geo struct {
	PlaceID string `json:"place_id"`
}

// ContextAnnotation contains topic/entity annotations.
type ContextAnnotation struct {
	Domain struct {
		ID          string `json:"id"`
		Name        string `json:"name"`
		Description string `json:"description"`
	} `json:"domain"`
	Entity struct {
		ID          string `json:"id"`
		Name        string `json:"name"`
		Description string `json:"description"`
	} `json:"entity"`
}

// ReferencedTweet indicates a reply, quote, or retweet.
type ReferencedTweet struct {
	Type string `json:"type"` // replied_to, quoted, retweeted
	ID   string `json:"id"`
}

// Includes contains expanded objects.
type Includes struct {
	Users  []UserData  `json:"users"`
	Places []PlaceData `json:"places"`
	Tweets []TweetData `json:"tweets"`
}

// UserData represents a user from the API.
type UserData struct {
	ID              string       `json:"id"`
	Username        string       `json:"username"`
	Name            string       `json:"name"`
	Location        string       `json:"location"`
	Description     string       `json:"description"`
	CreatedAt       string       `json:"created_at"`
	Verified        bool         `json:"verified"`
	VerifiedType    string       `json:"verified_type"`
	Protected       bool         `json:"protected"`
	PublicMetrics   *UserMetrics `json:"public_metrics"`
	ProfileImageURL string       `json:"profile_image_url"`
}

// UserMetrics contains user engagement counts.
type UserMetrics struct {
	FollowersCount int `json:"followers_count"`
	FollowingCount int `json:"following_count"`
	TweetCount     int `json:"tweet_count"`
	ListedCount    int `json:"listed_count"`
}

// PlaceData represents a place from the API.
type PlaceData struct {
	ID          string `json:"id"`
	FullName    string `json:"full_name"`
	Country     string `json:"country"`
	CountryCode string `json:"country_code"`
}

// Meta contains pagination info.
type Meta struct {
	NewestID    string `json:"newest_id"`
	OldestID    string `json:"oldest_id"`
	ResultCount int    `json:"result_count"`
	NextToken   string `json:"next_token"`
}

// APIError represents an API error.
type APIError struct {
	Title  string `json:"title"`
	Type   string `json:"type"`
	Detail string `json:"detail"`
	Status int    `json:"status"`
}

// ToModels converts the search response to internal model types.
func (r *SearchResponse) ToModels() ([]model.XPost, []model.XUser) {
	if r == nil || len(r.Data) == 0 {
		return nil, nil
	}

	// Build lookup maps for includes
	userMap := make(map[string]*UserData)
	placeMap := make(map[string]*PlaceData)

	if r.Includes != nil {
		for i := range r.Includes.Users {
			u := &r.Includes.Users[i]
			userMap[u.ID] = u
		}
		for i := range r.Includes.Places {
			p := &r.Includes.Places[i]
			placeMap[p.ID] = p
		}
	}

	var posts []model.XPost
	var users []model.XUser

	// Track seen users
	seenUsers := make(map[string]bool)

	for _, tweet := range r.Data {
		post := model.XPost{
			TweetID:        tweet.ID,
			AuthorID:       tweet.AuthorID,
			ConversationID: tweet.ConversationID,
			Text:           tweet.Text,
			Lang:           tweet.Lang,
		}

		// Parse created_at (format: "Wed Jan 06 18:40:40 +0000 2021")
		if t, err := time.Parse(time.RubyDate, tweet.CreatedAt); err == nil {
			post.CreatedAt = t.Unix()
		} else if t, err := time.Parse(time.RFC3339, tweet.CreatedAt); err == nil {
			post.CreatedAt = t.Unix()
		}

		// Public metrics
		if tweet.PublicMetrics != nil {
			post.RetweetCount = tweet.PublicMetrics.RetweetCount
			post.ReplyCount = tweet.PublicMetrics.ReplyCount
			post.LikeCount = tweet.PublicMetrics.LikeCount
			post.QuoteCount = tweet.PublicMetrics.QuoteCount
		}

		// Geo/Place
		if tweet.Geo != nil && tweet.Geo.PlaceID != "" {
			post.PlaceID = tweet.Geo.PlaceID
			if place, ok := placeMap[tweet.Geo.PlaceID]; ok {
				post.PlaceCountryCode = place.CountryCode
				post.PlaceFullName = place.FullName
			}
		}

		// Context annotations (serialize to JSON)
		if len(tweet.ContextAnnotations) > 0 {
			if data, err := json.Marshal(tweet.ContextAnnotations); err == nil {
				post.ContextAnnotationsJSON = string(data)
			}
		}

		// Reply/reference info
		post.InReplyToUserID = tweet.InReplyToUserID
		if len(tweet.ReferencedTweets) > 0 {
			post.ReferencedTweetID = tweet.ReferencedTweets[0].ID
			post.ReferencedTweetType = tweet.ReferencedTweets[0].Type
		}

		posts = append(posts, post)

		// Collect users
		if !seenUsers[tweet.AuthorID] {
			if userData, ok := userMap[tweet.AuthorID]; ok {
				user := model.XUser{
					UserID:          userData.ID,
					Username:        userData.Username,
					Name:            userData.Name,
					Location:        userData.Location,
					Description:     userData.Description,
					Verified:        userData.Verified,
					VerifiedType:    userData.VerifiedType,
					Protected:       userData.Protected,
					ProfileImageURL: userData.ProfileImageURL,
				}

				// Parse user created_at
				if t, err := time.Parse(time.RFC3339, userData.CreatedAt); err == nil {
					user.CreatedAt = t.Unix()
				}

				// User metrics
				if userData.PublicMetrics != nil {
					user.FollowersCount = userData.PublicMetrics.FollowersCount
					user.FollowingCount = userData.PublicMetrics.FollowingCount
					user.TweetCount = userData.PublicMetrics.TweetCount
					user.ListedCount = userData.PublicMetrics.ListedCount
				}

				users = append(users, user)
				seenUsers[tweet.AuthorID] = true
			}
		}
	}

	return posts, users
}
