package robots

import (
	"bufio"
	"context"
	"net/http"
	"strings"
	"sync"
	"time"
)

// Checker provides robots.txt checking with caching.
type Checker struct {
	httpClient *http.Client
	userAgent  string
	cache      map[string]*robotsData
	cacheMu    sync.RWMutex
	cacheTTL   time.Duration
}

type robotsData struct {
	groups    []*group
	fetchedAt time.Time
}

// group is one robots.txt record: one or more User-agent lines followed by the
// Allow/Disallow directives that apply to all of them. Agent tokens are stored
// lowercased for case-insensitive matching.
type group struct {
	agents   []string
	disallow []string
	allow    []string
}

// New creates a new robots.txt checker.
func New(userAgent string) *Checker {
	return &Checker{
		httpClient: &http.Client{Timeout: 10 * time.Second},
		userAgent:  userAgent,
		cache:      make(map[string]*robotsData),
		cacheTTL:   1 * time.Hour,
	}
}

// IsAllowed checks if a URL path is allowed by robots.txt.
func (c *Checker) IsAllowed(ctx context.Context, urlStr string) bool {
	// Extract host
	host := extractHost(urlStr)
	if host == "" {
		return true
	}

	data := c.getRobots(ctx, host)
	if data == nil {
		return true // No robots.txt or fetch failed = allow
	}

	path := extractPath(urlStr)
	return c.checkPath(data, path)
}

func (c *Checker) getRobots(ctx context.Context, host string) *robotsData {
	c.cacheMu.RLock()
	data, ok := c.cache[host]
	c.cacheMu.RUnlock()

	if ok && time.Since(data.fetchedAt) < c.cacheTTL {
		return data
	}

	// Fetch robots.txt
	robotsURL := "https://" + host + "/robots.txt"
	req, err := http.NewRequestWithContext(ctx, "GET", robotsURL, nil)
	if err != nil {
		return nil
	}
	req.Header.Set("User-Agent", c.userAgent)

	resp, err := c.httpClient.Do(req)
	if err != nil {
		// Cache negative result
		c.cacheMu.Lock()
		c.cache[host] = &robotsData{fetchedAt: time.Now()}
		c.cacheMu.Unlock()
		return nil
	}
	defer resp.Body.Close()

	if resp.StatusCode != 200 {
		// Non-200 (404 is the common case): close the body via the deferred
		// call above and cache a negative result.
		c.cacheMu.Lock()
		c.cache[host] = &robotsData{fetchedAt: time.Now()}
		c.cacheMu.Unlock()
		return nil
	}

	data = &robotsData{
		groups:    parseRobots(resp.Body),
		fetchedAt: time.Now(),
	}

	c.cacheMu.Lock()
	c.cache[host] = data
	c.cacheMu.Unlock()

	return data
}

func parseRobots(body interface{ Read([]byte) (int, error) }) []*group {
	scanner := bufio.NewScanner(body)
	var groups []*group
	var current *group
	// sawDirective tracks whether the current group already has directives.
	// Consecutive User-agent lines (no directive between them) share one
	// group; a User-agent line AFTER a directive opens a fresh group.
	sawDirective := false

	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}

		parts := strings.SplitN(line, ":", 2)
		if len(parts) != 2 {
			continue
		}

		key := strings.ToLower(strings.TrimSpace(parts[0]))
		value := strings.TrimSpace(parts[1])

		switch key {
		case "user-agent":
			if current == nil || sawDirective {
				current = &group{}
				groups = append(groups, current)
				sawDirective = false
			}
			current.agents = append(current.agents, strings.ToLower(value))
		case "disallow":
			// A Disallow with an empty value is an explicit "allow all" and
			// carries no path to match, so we skip storing it; the absence of
			// any matching Disallow already yields "allowed".
			if current != nil && value != "" {
				current.disallow = append(current.disallow, value)
			}
			if current != nil {
				sawDirective = true
			}
		case "allow":
			if current != nil && value != "" {
				current.allow = append(current.allow, value)
			}
			if current != nil {
				sawDirective = true
			}
		}
	}

	return groups
}

// selectGroup picks the group governing our user agent: the group whose agent
// token is the longest case-insensitive substring match, falling back to the
// wildcard ("*") group. Most-specific-block-wins.
func (c *Checker) selectGroup(groups []*group) *group {
	ua := strings.ToLower(c.userAgent)
	var best, wildcard *group
	bestLen := -1
	for _, g := range groups {
		for _, a := range g.agents {
			if a == "*" {
				if wildcard == nil {
					wildcard = g
				}
				continue
			}
			if strings.Contains(ua, a) && len(a) > bestLen {
				bestLen = len(a)
				best = g
			}
		}
	}
	if best != nil {
		return best
	}
	return wildcard
}

// checkPath applies the longest-match rule within the governing group: the
// directive (Allow or Disallow) with the longest matching pattern wins, and an
// Allow wins ties. No matching directive means allowed. This is what makes
// `Allow: /` + `Disallow: /private/` correctly block /private/ while allowing
// everything else, instead of the old Allow-first short-circuit that nullified
// every Disallow.
func (c *Checker) checkPath(data *robotsData, path string) bool {
	g := c.selectGroup(data.groups)
	if g == nil {
		return true
	}

	bestLen := -1
	allowed := true
	// Allow patterns first, then Disallow. consider() only overrides on a
	// strictly longer match, or an equal-length Allow — so ties favour Allow.
	consider := func(pattern string, isAllow bool) {
		n, ok := matchLen(pattern, path)
		if !ok {
			return
		}
		if n > bestLen || (n == bestLen && isAllow) {
			bestLen = n
			allowed = isAllow
		}
	}
	for _, p := range g.allow {
		consider(p, true)
	}
	for _, p := range g.disallow {
		consider(p, false)
	}
	return allowed
}

// matchLen reports whether pattern matches the start of path and, if so, the
// length of the matched prefix (used to rank directives). A trailing "*" is a
// wildcard; its contributed length excludes the star.
func matchLen(pattern, path string) (int, bool) {
	if pattern == "" {
		return 0, false
	}
	if strings.HasSuffix(pattern, "*") {
		prefix := strings.TrimSuffix(pattern, "*")
		if strings.HasPrefix(path, prefix) {
			return len(prefix), true
		}
		return 0, false
	}
	if strings.HasPrefix(path, pattern) {
		return len(pattern), true
	}
	return 0, false
}

func extractHost(urlStr string) string {
	// Simple extraction
	urlStr = strings.TrimPrefix(urlStr, "https://")
	urlStr = strings.TrimPrefix(urlStr, "http://")
	if idx := strings.Index(urlStr, "/"); idx != -1 {
		return urlStr[:idx]
	}
	return urlStr
}

func extractPath(urlStr string) string {
	urlStr = strings.TrimPrefix(urlStr, "https://")
	urlStr = strings.TrimPrefix(urlStr, "http://")
	if idx := strings.Index(urlStr, "/"); idx != -1 {
		return urlStr[idx:]
	}
	return "/"
}
