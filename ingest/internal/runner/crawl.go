// Package runner contains the business logic for CLI commands.
package runner

import (
	"context"
	"fmt"
	"log"
	"strings"
	"sync/atomic"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/html"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/util"
	"golang.org/x/sync/semaphore"
)

// CrawlOptions configures the crawl runner.
type CrawlOptions struct {
	Duration time.Duration
}

// CrawlResult holds crawl statistics.
type CrawlResult struct {
	Fetched int64
	Stored  int64
	Errors  int64
}

// CrawlRunner orchestrates the main crawl loop.
type CrawlRunner struct {
	app     *app.App
	opts    CrawlOptions
	fetched int64
	stored  int64
	errors  int64
}

// NewCrawlRunner creates a CrawlRunner with the given dependencies.
func NewCrawlRunner(a *app.App, opts CrawlOptions) *CrawlRunner {
	return &CrawlRunner{app: a, opts: opts}
}

// Run executes the main crawl loop.
func (cr *CrawlRunner) Run(ctx context.Context) (*CrawlResult, error) {
	cfg := cr.app.Config

	// Setup context with timeout
	ctx, cancel := context.WithTimeout(ctx, cr.opts.Duration)
	defer cancel()

	// Recover stale items
	recovered, _ := cr.app.Frontier.RecoverStale(ctx, cfg.Crawl.StaleInflightAge)
	if recovered > 0 {
		fmt.Printf("Recovered %d stale items\n", recovered)
	}

	// Worker pool
	sem := semaphore.NewWeighted(int64(cfg.Crawl.MaxConcurrency))

	fmt.Printf("Starting crawl for %v with %d workers\n", cr.opts.Duration, cfg.Crawl.MaxConcurrency)

	for {
		select {
		case <-ctx.Done():
			goto done
		default:
		}

		items, err := cr.app.Frontier.ClaimItems(ctx, 10)
		if err != nil {
			log.Printf("Claim error: %v", err)
			time.Sleep(1 * time.Second)
			continue
		}

		if len(items) == 0 {
			time.Sleep(1 * time.Second)
			continue
		}

		for _, item := range items {
			if err := sem.Acquire(ctx, 1); err != nil {
				break
			}

			go func(page *model.Page) {
				defer sem.Release(1)
				cr.processPage(ctx, page)
			}(item)
		}
	}

done:
	// Wait for in-flight workers
	sem.Acquire(context.Background(), int64(cfg.Crawl.MaxConcurrency))

	return &CrawlResult{
		Fetched: cr.fetched,
		Stored:  cr.stored,
		Errors:  cr.errors,
	}, nil
}

func (cr *CrawlRunner) processPage(ctx context.Context, page *model.Page) {
	// Check robots.txt
	if cr.app.Robots != nil && !cr.app.Robots.IsAllowed(ctx, page.URLRaw) {
		cr.app.Frontier.MarkFailed(ctx, page, "disallowed by robots.txt", true)
		return
	}

	// Fetch
	result := cr.app.Fetcher.Fetch(ctx, page.URLRaw, page.Domain)
	atomic.AddInt64(&cr.fetched, 1)

	if result.Error != nil {
		atomic.AddInt64(&cr.errors, 1)
		cr.app.Frontier.MarkFailed(ctx, page, result.Error.Error(), false)
		return
	}

	page.HTTPStatus = result.StatusCode
	page.ETag = result.ETag
	page.LastModified = result.LastModified

	if result.StatusCode < 200 || result.StatusCode >= 300 {
		atomic.AddInt64(&cr.errors, 1)
		cr.app.Frontier.MarkFailed(ctx, page, fmt.Sprintf("HTTP %d", result.StatusCode), result.StatusCode >= 400 && result.StatusCode < 500)
		return
	}

	// Store raw content
	ext := ".html"
	if strings.Contains(page.URLRaw, ".json") {
		ext = ".json"
	}
	hash, err := cr.app.RawStore.Store(ctx, result.Body, ext)
	if err != nil {
		atomic.AddInt64(&cr.errors, 1)
		cr.app.Frontier.MarkFailed(ctx, page, err.Error(), false)
		return
	}

	page.ContentSHA256 = hash
	atomic.AddInt64(&cr.stored, 1)

	// Extract metadata and links
	meta, _ := html.Extract(result.Body, page.URLRaw)
	if meta != nil && len(meta.Outlinks) > 0 {
		// Only add links from same domain to keep crawl focused
		var sameDomainLinks []string
		for _, link := range meta.Outlinks {
			if util.ExtractDomain(link) == page.Domain {
				sameDomainLinks = append(sameDomainLinks, link)
			}
		}
		cr.app.Frontier.PushLinks(ctx, sameDomainLinks, 0)
	}

	// Update canonical if found
	if meta != nil && meta.CanonicalURL != "" {
		page.URLCanon = meta.CanonicalURL
	}

	// Insert article metadata
	if meta != nil {
		cr.insertArticle(ctx, page, meta, hash)
	}

	cr.app.Frontier.MarkDone(ctx, page)
}

func (cr *CrawlRunner) insertArticle(ctx context.Context, page *model.Page, meta *html.Metadata, hash string) {
	conn := cr.app.Database.Conn()

	// Determine canonical URL - prefer extracted, fallback to page URL
	canonURL := page.URLCanon
	if meta.CanonicalURL != "" {
		canonURL = meta.CanonicalURL
	}

	// Convert published time to Unix timestamp
	var publishedAt int64
	if !meta.PublishedTime.IsZero() {
		publishedAt = meta.PublishedTime.Unix()
	}

	// Insert or update article metadata
	_, err := conn.ExecContext(ctx, `
		INSERT INTO articles_raw (url_canon, domain, fetched_at, published_at, title, raw_hash, extraction_version)
		VALUES (?, ?, ?, ?, ?, ?, ?)
		ON CONFLICT(url_canon) DO UPDATE SET
			fetched_at = excluded.fetched_at,
			published_at = excluded.published_at,
			title = excluded.title,
			raw_hash = excluded.raw_hash,
			extraction_version = excluded.extraction_version
	`,
		canonURL,
		page.Domain,
		time.Now().Unix(),
		publishedAt,
		meta.Title,
		hash,
		"go-v1.0",
	)

	if err != nil {
		log.Printf("Failed to insert article %s: %v", canonURL, err)
	}
}
