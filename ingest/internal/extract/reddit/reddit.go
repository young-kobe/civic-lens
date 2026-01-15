package reddit

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// Client interfaces with Reddit's API.
type Client struct {
	httpClient   *http.Client
	userAgent    string
	accessToken  string
	tokenExpiry  time.Time
	clientID     string
	clientSecret string
}

// Config holds Reddit API credentials.
type Config struct {
	ClientID     string
	ClientSecret string
	UserAgent    string
}

// New creates a new Reddit API client.
func New(cfg Config) *Client {
	return &Client{
		httpClient:   &http.Client{Timeout: 30 * time.Second},
		userAgent:    cfg.UserAgent,
		clientID:     cfg.ClientID,
		clientSecret: cfg.ClientSecret,
	}
}

// authenticate obtains an OAuth2 token using client credentials.
func (c *Client) authenticate(ctx context.Context) error {
	if c.accessToken != "" && time.Now().Before(c.tokenExpiry) {
		return nil
	}

	data := url.Values{}
	data.Set("grant_type", "client_credentials")

	req, err := http.NewRequestWithContext(ctx, "POST", "https://www.reddit.com/api/v1/access_token", strings.NewReader(data.Encode()))
	if err != nil {
		return err
	}

	req.SetBasicAuth(c.clientID, c.clientSecret)
	req.Header.Set("User-Agent", c.userAgent)
	req.Header.Set("Content-Type", "application/x-www-form-urlencoded")

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()

	if resp.StatusCode != 200 {
		return fmt.Errorf("auth failed: %s", resp.Status)
	}

	var tokenResp struct {
		AccessToken string `json:"access_token"`
		ExpiresIn   int    `json:"expires_in"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&tokenResp); err != nil {
		return err
	}

	c.accessToken = tokenResp.AccessToken
	c.tokenExpiry = time.Now().Add(time.Duration(tokenResp.ExpiresIn-60) * time.Second)
	return nil
}

// FetchSubredditPostsPublic fetches posts using public .json endpoint (no auth required).
func (c *Client) FetchSubredditPostsPublic(ctx context.Context, subreddit string, limit int) ([]model.RedditPost, []byte, error) {
	url := fmt.Sprintf("https://www.reddit.com/r/%s/new.json?limit=%d", subreddit, limit)
	req, err := http.NewRequestWithContext(ctx, "GET", url, nil)
	if err != nil {
		return nil, nil, err
	}

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

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("HTTP error: %s", resp.Status)
	}

	posts, err := parsePosts(body)
	return posts, body, err
}

// FetchPostCommentsPublic fetches comments using public .json endpoint (no auth required).
func (c *Client) FetchPostCommentsPublic(ctx context.Context, subreddit, postID string, limit int) ([]model.RedditComment, []byte, error) {
	url := fmt.Sprintf("https://www.reddit.com/r/%s/comments/%s.json?limit=%d", subreddit, postID, limit)
	req, err := http.NewRequestWithContext(ctx, "GET", url, nil)
	if err != nil {
		return nil, nil, err
	}

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

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("HTTP error: %s", resp.Status)
	}

	comments, err := parseComments(body, subreddit)
	return comments, body, err
}

// FetchSubredditPosts fetches recent posts from a subreddit.
func (c *Client) FetchSubredditPosts(ctx context.Context, subreddit string, limit int) ([]model.RedditPost, []byte, error) {
	if err := c.authenticate(ctx); err != nil {
		return nil, nil, fmt.Errorf("auth: %w", err)
	}

	url := fmt.Sprintf("https://oauth.reddit.com/r/%s/new?limit=%d", subreddit, limit)
	req, err := http.NewRequestWithContext(ctx, "GET", url, nil)
	if err != nil {
		return nil, nil, err
	}

	req.Header.Set("Authorization", "Bearer "+c.accessToken)
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

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("API error: %s", resp.Status)
	}

	posts, err := parsePosts(body)
	return posts, body, err
}

// FetchPostComments fetches comments for a post.
func (c *Client) FetchPostComments(ctx context.Context, subreddit, postID string, limit int) ([]model.RedditComment, []byte, error) {
	if err := c.authenticate(ctx); err != nil {
		return nil, nil, fmt.Errorf("auth: %w", err)
	}

	url := fmt.Sprintf("https://oauth.reddit.com/r/%s/comments/%s?limit=%d", subreddit, postID, limit)
	req, err := http.NewRequestWithContext(ctx, "GET", url, nil)
	if err != nil {
		return nil, nil, err
	}

	req.Header.Set("Authorization", "Bearer "+c.accessToken)
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

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("API error: %s", resp.Status)
	}

	comments, err := parseComments(body, subreddit)
	return comments, body, err
}

func parsePosts(data []byte) ([]model.RedditPost, error) {
	var listing struct {
		Data struct {
			Children []struct {
				Data struct {
					Name        string  `json:"name"`
					Subreddit   string  `json:"subreddit"`
					Title       string  `json:"title"`
					Selftext    string  `json:"selftext"`
					Score       int     `json:"score"`
					NumComments int     `json:"num_comments"`
					CreatedUTC  float64 `json:"created_utc"`
				} `json:"data"`
			} `json:"children"`
		} `json:"data"`
	}

	if err := json.Unmarshal(data, &listing); err != nil {
		return nil, err
	}

	var posts []model.RedditPost
	for _, child := range listing.Data.Children {
		d := child.Data
		posts = append(posts, model.RedditPost{
			Fullname:    d.Name,
			Subreddit:   d.Subreddit,
			Title:       d.Title,
			Body:        d.Selftext,
			Score:       d.Score,
			NumComments: d.NumComments,
			CreatedUTC:  int64(d.CreatedUTC),
		})
	}

	return posts, nil
}

func parseComments(data []byte, subreddit string) ([]model.RedditComment, error) {
	// Reddit returns an array: [post, comments]
	var listings []struct {
		Data struct {
			Children []struct {
				Kind string `json:"kind"`
				Data struct {
					Name       string  `json:"name"`
					Body       string  `json:"body"`
					Score      int     `json:"score"`
					CreatedUTC float64 `json:"created_utc"`
					LinkID     string  `json:"link_id"`
				} `json:"data"`
			} `json:"children"`
		} `json:"data"`
	}

	if err := json.Unmarshal(data, &listings); err != nil {
		return nil, err
	}

	var comments []model.RedditComment
	if len(listings) < 2 {
		return comments, nil
	}

	for _, child := range listings[1].Data.Children {
		if child.Kind != "t1" { // t1 = comment
			continue
		}
		d := child.Data
		comments = append(comments, model.RedditComment{
			Fullname:     d.Name,
			PostFullname: d.LinkID,
			Subreddit:    subreddit,
			Body:         d.Body,
			Score:        d.Score,
			CreatedUTC:   int64(d.CreatedUTC),
		})
	}

	return comments, nil
}
