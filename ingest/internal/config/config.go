package config

import (
	"os"
	"time"

	"gopkg.in/yaml.v3"
)

// Config holds all ingestion configuration.
type Config struct {
	Database DatabaseConfig `yaml:"database"`
	Crawl    CrawlConfig    `yaml:"crawl"`
	Reddit   RedditConfig   `yaml:"reddit"`
	X        XConfig        `yaml:"x"`
	Seeds    []SeedConfig   `yaml:"seeds"`
}

// DatabaseConfig holds database paths.
type DatabaseConfig struct {
	Path   string `yaml:"path"`    // e.g., data/civic_lens.db
	RawDir string `yaml:"raw_dir"` // e.g., data/raw
}

// CrawlConfig holds crawl behavior settings.
type CrawlConfig struct {
	MaxConcurrency   int           `yaml:"max_concurrency"`
	RequestTimeout   time.Duration `yaml:"request_timeout"`
	MaxRetries       int           `yaml:"max_retries"`
	RateLimitPerSec  float64       `yaml:"rate_limit_per_sec"`
	MaxRedirects     int           `yaml:"max_redirects"`
	UserAgent        string        `yaml:"user_agent"`
	StaleInflightAge time.Duration `yaml:"stale_inflight_age"`
}

// RedditConfig holds Reddit API settings.
type RedditConfig struct {
	UserAgent  string   `yaml:"user_agent"`
	Subreddits []string `yaml:"subreddits"`
}

// XConfig holds X (Twitter) API settings.
type XConfig struct {
	BearerToken       string   `yaml:"bearer_token"`
	UserAgent         string   `yaml:"user_agent"`
	MaxRequestsHour   int      `yaml:"max_requests_hour"`
	PoliticalQueries  []string `yaml:"political_queries"`
	MaxTweetsPerQuery int      `yaml:"max_tweets_per_query"`
}

// SeedConfig holds a seed URL or feed.
type SeedConfig struct {
	URL      string `yaml:"url"`
	Type     string `yaml:"type"` // "rss", "html", "sitemap"
	Priority int    `yaml:"priority"`
}

// Load reads configuration from a YAML file.
func Load(path string) (*Config, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}

	cfg := &Config{
		// Defaults
		Crawl: CrawlConfig{
			MaxConcurrency:   10,
			RequestTimeout:   30 * time.Second,
			MaxRetries:       3,
			RateLimitPerSec:  1.0,
			MaxRedirects:     5,
			UserAgent:        "CivicLens/1.0 (+https://github.com/young-kobe/civic-lens)",
			StaleInflightAge: 10 * time.Minute,
		},
		Database: DatabaseConfig{
			Path:   "data/civic_lens.db",
			RawDir: "data/raw",
		},
	}

	if err := yaml.Unmarshal(data, cfg); err != nil {
		return nil, err
	}

	return cfg, nil
}
