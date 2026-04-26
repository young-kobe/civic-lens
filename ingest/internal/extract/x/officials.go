package x

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"strings"
	"sync/atomic"

	"gopkg.in/yaml.v3"

	"github.com/young-kobe/civic-lens/ingest/internal/extract/x/models"
)

// VerifiedOfficial is one row from data/verified_officials.yaml. Handles are
// normalized to bare lowercase (no leading @) so the X API user-by-username
// endpoint accepts them directly. Editorial fields (display_name, office,
// blurb) live in the YAML for the analysis layer and are not used by the
// ingestion path — but parsing them here keeps the file's schema honest at
// load time, so a malformed entry surfaces during the ingest run, not later
// in the aggregator.
type VerifiedOfficial struct {
	Handle      string
	AlsoHandles []string
	DisplayName string
	Office      string
	Party       string
	TermStart   string
	BioSource   string
}

// AllHandles returns the primary handle plus alternates, in load order, for
// timeline-pull iteration. The X API user-by-username endpoint is the same
// for every alias so each one is fetched independently — the editorial layer
// in the YAML cross-references handles to the same person, but the X user_id
// space is keyed on handle, not person.
func (o VerifiedOfficial) AllHandles() []string {
	out := make([]string, 0, 1+len(o.AlsoHandles))
	if o.Handle != "" {
		out = append(out, o.Handle)
	}
	for _, h := range o.AlsoHandles {
		if h != "" {
			out = append(out, h)
		}
	}
	return out
}

type officialsFile struct {
	Officials []struct {
		Handle      string   `yaml:"handle"`
		AlsoHandles []string `yaml:"also_handles"`
		DisplayName string   `yaml:"display_name"`
		Office      string   `yaml:"office"`
		Party       string   `yaml:"party"`
		TermStart   string   `yaml:"term_start"`
		BioSource   string   `yaml:"bio_source"`
	} `yaml:"officials"`
}

// LoadVerifiedOfficials reads data/verified_officials.yaml (or test-supplied
// path) and returns the officials list with every handle normalized to bare
// lowercase. Returns an empty slice + nil error when the file is absent —
// the runner skips the officials pass in that case rather than failing the
// whole X ingest run.
func LoadVerifiedOfficials(path string) ([]VerifiedOfficial, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		if os.IsNotExist(err) {
			return nil, nil
		}
		return nil, fmt.Errorf("read %s: %w", path, err)
	}
	var parsed officialsFile
	if err := yaml.Unmarshal(data, &parsed); err != nil {
		return nil, fmt.Errorf("parse %s: %w", path, err)
	}
	out := make([]VerifiedOfficial, 0, len(parsed.Officials))
	for _, row := range parsed.Officials {
		primary := normalizeHandle(row.Handle)
		if primary == "" {
			continue
		}
		alts := make([]string, 0, len(row.AlsoHandles))
		for _, alt := range row.AlsoHandles {
			if h := normalizeHandle(alt); h != "" {
				alts = append(alts, h)
			}
		}
		out = append(out, VerifiedOfficial{
			Handle:      primary,
			AlsoHandles: alts,
			DisplayName: row.DisplayName,
			Office:      row.Office,
			Party:       row.Party,
			TermStart:   row.TermStart,
			BioSource:   row.BioSource,
		})
	}
	return out, nil
}

func normalizeHandle(raw string) string {
	return strings.ToLower(strings.TrimPrefix(strings.TrimSpace(raw), "@"))
}

// UserLookupResponse is the shape of GET /2/users/by/username/:username.
type UserLookupResponse struct {
	Data   *models.UserData  `json:"data"`
	Errors []models.APIError `json:"errors"`
}

// UserByUsername looks up a user by their X username. Used to resolve
// editorial handles in verified_officials.yaml to the stable numeric
// author_id required by the timeline endpoint. Counts as one user-resource
// against the X API budget (10 cents per the centsPerUser tracker).
func (c *Client) UserByUsername(ctx context.Context, username string) (*models.UserData, []byte, error) {
	if err := c.rateLimiter.Wait(ctx); err != nil {
		return nil, nil, fmt.Errorf("rate limiter: %w", err)
	}

	baseURL := fmt.Sprintf("%s/2/users/by/username/%s", c.baseURL, url.PathEscape(username))
	params := url.Values{}
	params.Set("user.fields", "id,username,name,location,description,created_at,public_metrics,verified,verified_type,protected,profile_image_url")

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

	body, err := io.ReadAll(io.LimitReader(resp.Body, 1<<20))
	if err != nil {
		return nil, nil, err
	}

	atomic.AddInt64(&c.requestCount, 1)

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("user lookup %s: API %d: %s", username, resp.StatusCode, string(body))
	}

	var parsed UserLookupResponse
	if err := json.Unmarshal(body, &parsed); err != nil {
		return nil, body, fmt.Errorf("parse user lookup: %w", err)
	}
	if parsed.Data == nil {
		// API returns `errors` (e.g. suspended, not-found) without `data`.
		// Don't blow up the whole officials run for one missing handle —
		// the caller logs and moves to the next official.
		if len(parsed.Errors) > 0 {
			return nil, body, fmt.Errorf("user lookup %s: %s", username, parsed.Errors[0].Detail)
		}
		return nil, body, fmt.Errorf("user lookup %s: empty response", username)
	}
	return parsed.Data, body, nil
}

// UserTimeline fetches recent posts for the supplied user_id via
// GET /2/users/:id/tweets. Same response envelope as recent search, so
// callers reuse ToModels() to turn the response into XPost/XUser slices.
// max_results is capped at 100 by the API.
func (c *Client) UserTimeline(ctx context.Context, userID string, maxResults int) (*models.SearchResponse, []byte, error) {
	if err := c.rateLimiter.Wait(ctx); err != nil {
		return nil, nil, fmt.Errorf("rate limiter: %w", err)
	}

	baseURL := fmt.Sprintf("%s/2/users/%s/tweets", c.baseURL, url.PathEscape(userID))
	params := url.Values{}
	if maxResults > 100 {
		maxResults = 100
	}
	if maxResults < 5 {
		// X requires max_results >= 5 on this endpoint; pad up so a small
		// per-official cap doesn't 400 out the whole run.
		maxResults = 5
	}
	params.Set("max_results", fmt.Sprintf("%d", maxResults))
	params.Set("tweet.fields", "author_id,created_at,text,public_metrics,geo,context_annotations,lang,conversation_id,in_reply_to_user_id,referenced_tweets")
	params.Set("user.fields", "id,username,name,location,description,created_at,public_metrics,verified,verified_type,protected,profile_image_url")
	params.Set("place.fields", "id,full_name,country,country_code")
	params.Set("expansions", "author_id,geo.place_id,referenced_tweets.id")
	params.Set("exclude", "retweets")

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

	body, err := io.ReadAll(io.LimitReader(resp.Body, 10<<20))
	if err != nil {
		return nil, nil, err
	}

	atomic.AddInt64(&c.requestCount, 1)

	if resp.StatusCode != 200 {
		return nil, body, fmt.Errorf("user timeline %s: API %d: %s", userID, resp.StatusCode, string(body))
	}

	var searchResp models.SearchResponse
	if err := json.Unmarshal(body, &searchResp); err != nil {
		return nil, body, fmt.Errorf("parse user timeline: %w", err)
	}
	return &searchResp, body, nil
}
