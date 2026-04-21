package reddit

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/model"
)

// maxRespBody caps how much of a Reddit response we will read. A hostile
// or misbehaving endpoint can't park the crawler reading a 10 GB body
// (audit §1.11). Matches the limit already in place for crawl HTML.
const maxRespBody = 10 << 20 // 10 MiB

// Client interfaces with Reddit's public JSON endpoints.
type Client struct {
	httpClient *http.Client
	userAgent  string
}

// Config holds Reddit client settings.
type Config struct {
	UserAgent string
}

// New creates a new Reddit client for the public .json endpoints.
func New(cfg Config) *Client {
	return &Client{
		httpClient: &http.Client{Timeout: 30 * time.Second},
		userAgent:  cfg.UserAgent,
	}
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

	body, err := io.ReadAll(io.LimitReader(resp.Body, maxRespBody))
	if err != nil {
		return nil, nil, err
	}

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("HTTP error: %s", resp.Status)
	}

	posts, err := parsePosts(body)
	return posts, body, err
}

// FetchPostCommentBodiesPublic fetches comment bodies for a post using the public .json endpoint.
// Only Body text is returned — the full comment structure is not persisted downstream.
func (c *Client) FetchPostCommentBodiesPublic(ctx context.Context, subreddit, postID string, limit int) ([]string, []byte, error) {
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

	body, err := io.ReadAll(io.LimitReader(resp.Body, maxRespBody))
	if err != nil {
		return nil, nil, err
	}

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("HTTP error: %s", resp.Status)
	}

	bodies, err := parseCommentBodies(body)
	return bodies, body, err
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

func parseCommentBodies(data []byte) ([]string, error) {
	// Reddit returns an array: [post, comments]
	var listings []struct {
		Data struct {
			Children []struct {
				Kind string `json:"kind"`
				Data struct {
					Body string `json:"body"`
				} `json:"data"`
			} `json:"children"`
		} `json:"data"`
	}

	if err := json.Unmarshal(data, &listings); err != nil {
		return nil, err
	}

	if len(listings) < 2 {
		return nil, nil
	}

	var bodies []string
	for _, child := range listings[1].Data.Children {
		if child.Kind != "t1" { // t1 = comment
			continue
		}
		if child.Data.Body != "" {
			bodies = append(bodies, child.Data.Body)
		}
	}

	return bodies, nil
}
