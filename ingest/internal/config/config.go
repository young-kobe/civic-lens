package config

import (
	"bytes"
	"os"
	"path/filepath"
	"time"

	"gopkg.in/yaml.v3"
)

// Config holds all ingestion configuration.
type Config struct {
	Database     DatabaseConfig      `yaml:"database"`
	Crawl        CrawlConfig         `yaml:"crawl"`
	Reddit       RedditConfig        `yaml:"reddit"`
	X            XConfig             `yaml:"x"`
	Seeds        []SeedConfig        `yaml:"seeds"`
	CrawlBalance *CrawlBalanceConfig `yaml:"crawl_balance"`
	// DomainFilter is Python-ETL-only (analysis/src/etl/documents.py's
	// DomainFilterConfig reads it directly from seeds.yaml); the Go side
	// never acts on it. Declared here purely so KnownFields(true) strict
	// decoding below doesn't reject seeds.yaml's domain_filter section.
	DomainFilter map[string]any `yaml:"domain_filter"`
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
	// MonthlyBudgetCents caps the X API spend per calendar-month (UTC).
	// Enforced by a persistent counter in the x_api_budget table. When the
	// estimated spend hits this value, the runner aborts remaining queries
	// for the month. Zero disables the check — but always set this in
	// production. (walkthrough 048.)
	MonthlyBudgetCents int `yaml:"monthly_budget_cents"`
	// OfficialsListPath points at the editorial verified-officials YAML
	// (default: data/verified_officials.yaml). The X runner walks this
	// list every pass and pulls each account's user-timeline; posts land
	// with x_posts_raw.is_official_tier=1 so downstream stages skip the
	// LLM tier classifier for them. Empty string falls back to the default.
	OfficialsListPath string `yaml:"officials_list_path"`
	// MaxTweetsPerOfficial bounds the per-account timeline pull. The X API
	// minimum on /2/users/:id/tweets is 5; the client pads up if a smaller
	// value is configured. Default 5 keeps the officials pass at the 37
	// handles AllHandles() returns (16 primaries + 21 alternates) × 5 tweets
	// on the X v2 retail price card.
	MaxTweetsPerOfficial int `yaml:"max_tweets_per_official"`
}

// SeedConfig holds a seed URL or feed.
type SeedConfig struct {
	URL      string `yaml:"url"`
	Type     string `yaml:"type"` // "rss", "html", "sitemap"
	Priority int    `yaml:"priority"`
}

// CrawlBalanceConfig configures optional per-domain balance quotas for the
// Postgres frontier claim query (Postgres-redesign Phase 2, Task 2 —
// production is heavily skewed, e.g. cbsnews 2,404 docs vs npr 65). Quotas
// apply ONLY when the ingestor is pointed at Postgres; the SQLite path is
// frozen production behavior and never reads this section. An absent
// section (a nil Config.CrawlBalance) means unlimited claiming for every
// domain — unchanged, backward-compatible behavior.
type CrawlBalanceConfig struct {
	// Window bounds how far back the per-domain "already fetched" count is
	// computed (see frontier.DomainQuota). Defaults to 24h when the section
	// is present but this key is omitted.
	Window time.Duration `yaml:"window"`
	// DefaultMaxPerWindow caps any domain not listed in Domains within
	// Window. Omitted or zero means unlimited for domains without an
	// explicit override.
	DefaultMaxPerWindow int `yaml:"default_max_per_window"`
	// Domains overrides DefaultMaxPerWindow per domain, keyed by the
	// lowercased host as util.ExtractDomain produces it (e.g.
	// "www.cbsnews.com"). An explicit cap of 0 blocks the domain outright.
	Domains map[string]int `yaml:"domains"`
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

	// Strict decoding: an unknown or misspelled key (e.g. `max_concurency:`)
	// is a hard error instead of silently falling back to the default. A typo
	// in seeds.yaml that quietly disables a politeness setting is exactly the
	// class of bug this catches (I-12b).
	dec := yaml.NewDecoder(bytes.NewReader(data))
	dec.KnownFields(true)
	if err := dec.Decode(cfg); err != nil {
		return nil, err
	}

	// Resolve config-file paths (read-only, source-controlled) relative to
	// the seeds.yaml directory, not the process working directory. This is
	// what makes deploy work where `WorkingDirectory=/var/lib/civic-lens`
	// (the runtime data root) but seeds.yaml + verified_officials.yaml
	// live at `/opt/civic-lens/data/`. Runtime-data paths (database.path,
	// database.raw_dir) stay relative to the working directory because
	// those ARE meant to live next to the runtime data, not the binary.
	seedsDir := filepath.Dir(path)
	if cfg.X.OfficialsListPath == "" {
		cfg.X.OfficialsListPath = filepath.Join(seedsDir, "verified_officials.yaml")
	} else if !filepath.IsAbs(cfg.X.OfficialsListPath) {
		cfg.X.OfficialsListPath = filepath.Join(seedsDir, cfg.X.OfficialsListPath)
	}

	// A present crawl_balance section with no window key defaults to 24h
	// (frontier.DomainQuota applies the same default independently, so a
	// DomainQuota built without going through config.Load also behaves this
	// way).
	if cfg.CrawlBalance != nil && cfg.CrawlBalance.Window <= 0 {
		cfg.CrawlBalance.Window = 24 * time.Hour
	}

	return cfg, nil
}
