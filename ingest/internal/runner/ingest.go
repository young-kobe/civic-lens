package runner

import (
	"context"
	"fmt"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/rss"
	"github.com/young-kobe/civic-lens/ingest/internal/util"
)

// IngestResult holds ingestion statistics.
type IngestResult struct {
	TotalDiscovered int64
	Skipped         int64 // Items skipped due to age filter
}

// IngestRunner orchestrates URL discovery from seed feeds.
type IngestRunner struct {
	app             *app.App
	totalDiscovered int64
	skipped         int64
}

// NewIngestRunner creates an IngestRunner with the given dependencies.
func NewIngestRunner(a *app.App) *IngestRunner {
	return &IngestRunner{app: a}
}

// Run discovers URLs from seed feeds and adds them to the frontier.
func (ir *IngestRunner) Run(ctx context.Context) (*IngestResult, error) {
	cfg := ir.app.Config

	// Only ingest items from the last 30 days
	cutoff := time.Now().Add(-30 * 24 * time.Hour)

	for _, seed := range cfg.Seeds {
		fmt.Printf("Processing seed: %s (%s)\n", seed.URL, seed.Type)

		domain := util.ExtractDomain(seed.URL)
		result := ir.app.Fetcher.Fetch(ctx, seed.URL, domain)
		if result.Error != nil {
			fmt.Printf("  Error: %v\n", result.Error)
			continue
		}

		if seed.Type == "rss" {
			feed, err := rss.Parse(result.Body)
			if err != nil {
				fmt.Printf("  Parse error: %v\n", err)
				continue
			}

			var links []string
			var seedSkipped int64
			for _, item := range feed.Items {
				if item.Link == "" {
					continue
				}
				// Filter out items older than 30 days
				if !item.Published.IsZero() && item.Published.Before(cutoff) {
					seedSkipped++
					continue
				}
				links = append(links, item.Link)
			}

			stats, err := ir.app.Frontier.PushLinks(ctx, links, seed.Priority)
			if err != nil {
				fmt.Printf("  PushLinks error: %v\n", err)
			}
			ir.totalDiscovered += stats.Added
			ir.skipped += seedSkipped
			fmt.Printf("  Discovered %d links from RSS feed (skipped %d old, malformed %d, db-errors %d)\n",
				stats.Added, seedSkipped, stats.Malformed, stats.DBErrors)
		}
	}

	return &IngestResult{
		TotalDiscovered: ir.totalDiscovered,
		Skipped:         ir.skipped,
	}, nil
}
