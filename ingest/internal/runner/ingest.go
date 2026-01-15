package runner

import (
	"context"
	"fmt"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/rss"
	"github.com/young-kobe/civic-lens/ingest/internal/util"
)

// IngestResult holds ingestion statistics.
type IngestResult struct {
	TotalDiscovered int64
}

// RunIngest discovers URLs from seed feeds and adds them to the frontier.
func RunIngest(ctx context.Context, a *app.App) (*IngestResult, error) {
	cfg := a.Config
	var totalDiscovered int64

	for _, seed := range cfg.Seeds {
		fmt.Printf("Processing seed: %s (%s)\n", seed.URL, seed.Type)

		domain := util.ExtractDomain(seed.URL)
		result := a.Fetcher.Fetch(ctx, seed.URL, domain)
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
			for _, item := range feed.Items {
				if item.Link != "" {
					links = append(links, item.Link)
				}
			}

			added, _ := a.Frontier.PushLinks(ctx, links, seed.Priority)
			totalDiscovered += added
			fmt.Printf("  Discovered %d links from RSS feed\n", added)
		}
	}

	return &IngestResult{
		TotalDiscovered: totalDiscovered,
	}, nil
}
