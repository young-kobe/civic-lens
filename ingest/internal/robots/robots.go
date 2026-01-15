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
	rules     []rule
	fetchedAt time.Time
}

type rule struct {
	userAgent string
	disallow  []string
	allow     []string
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
	if err != nil || resp.StatusCode != 200 {
		// Cache negative result
		c.cacheMu.Lock()
		c.cache[host] = &robotsData{fetchedAt: time.Now()}
		c.cacheMu.Unlock()
		return nil
	}
	defer resp.Body.Close()

	data = &robotsData{
		rules:     parseRobots(resp.Body),
		fetchedAt: time.Now(),
	}

	c.cacheMu.Lock()
	c.cache[host] = data
	c.cacheMu.Unlock()

	return data
}

func parseRobots(body interface{ Read([]byte) (int, error) }) []rule {
	scanner := bufio.NewScanner(body)
	var rules []rule
	var current *rule

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
			if current != nil {
				rules = append(rules, *current)
			}
			current = &rule{userAgent: value}
		case "disallow":
			if current != nil && value != "" {
				current.disallow = append(current.disallow, value)
			}
		case "allow":
			if current != nil && value != "" {
				current.allow = append(current.allow, value)
			}
		}
	}

	if current != nil {
		rules = append(rules, *current)
	}

	return rules
}

func (c *Checker) checkPath(data *robotsData, path string) bool {
	// Find applicable rules
	var applicable *rule
	for i := range data.rules {
		r := &data.rules[i]
		if r.userAgent == "*" || strings.Contains(c.userAgent, r.userAgent) {
			applicable = r
			break
		}
	}

	if applicable == nil {
		return true
	}

	// Check allow first (more specific)
	for _, pattern := range applicable.allow {
		if matchPath(pattern, path) {
			return true
		}
	}

	// Check disallow
	for _, pattern := range applicable.disallow {
		if matchPath(pattern, path) {
			return false
		}
	}

	return true
}

func matchPath(pattern, path string) bool {
	if pattern == "" {
		return false
	}
	if strings.HasSuffix(pattern, "*") {
		return strings.HasPrefix(path, strings.TrimSuffix(pattern, "*"))
	}
	return strings.HasPrefix(path, pattern)
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
