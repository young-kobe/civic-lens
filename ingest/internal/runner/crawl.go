// Package runner contains the business logic for CLI commands.
package runner

import (
	"context"
	"fmt"
	"log/slog"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/app"
	"github.com/young-kobe/civic-lens/ingest/internal/extract/html"
	"github.com/young-kobe/civic-lens/ingest/internal/httpclient"
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
	writer  *ArticleWriter
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

	// Start batched article writer
	cr.writer = NewArticleWriter(cr.app.Database)
	go cr.writer.Start()
	defer cr.writer.Close()

	cr.app.Frontier.EnsureRecovered(ctx, cfg.Crawl.StaleInflightAge)

	sem := semaphore.NewWeighted(int64(cfg.Crawl.MaxConcurrency))
	var wg sync.WaitGroup

	fmt.Printf("Starting crawl for %v with %d workers\n", cr.opts.Duration, cfg.Crawl.MaxConcurrency)

loop:
	for {
		select {
		case <-ctx.Done():
			break loop
		default:
		}

		items, err := cr.app.Frontier.ClaimItems(ctx, 10)
		if err != nil {
			if ctx.Err() != nil {
				break loop
			}
			slog.Error("claim items failed", "component", "runner.crawl", "error", err)
			time.Sleep(1 * time.Second)
			continue
		}

		if len(items) == 0 {
			select {
			case <-ctx.Done():
				break loop
			case <-time.After(1 * time.Second):
			}
			continue
		}

		for _, item := range items {
			if err := sem.Acquire(ctx, 1); err != nil {
				break loop
			}

			wg.Add(1)
			go func(page *model.Page) {
				defer wg.Done()
				defer sem.Release(1)
				cr.processPage(ctx, page)
			}(item)
		}
	}

	// Wait for all in-flight workers to finish. We deliberately wait
	// unbounded here: the outer ctx is already cancelled so workers will
	// observe it and exit quickly. A timeout-based drain risks orphaning
	// DB writes mid-transition.
	wg.Wait()

	return &CrawlResult{
		Fetched: cr.fetched,
		Stored:  cr.stored,
		Errors:  cr.errors,
	}, nil
}

// processPage runs the full per-page pipeline. Each stage returns early
// on failure after marking the page with the appropriate permanence.
func (cr *CrawlRunner) processPage(ctx context.Context, page *model.Page) {
	if !cr.checkRobots(ctx, page) {
		return
	}

	result, ok := cr.fetchPage(ctx, page)
	if !ok {
		return
	}

	hash, ok := cr.storeRaw(ctx, page, result)
	if !ok {
		return
	}

	cr.extractAndEnqueue(ctx, page, result.Body, hash)

	if err := cr.app.Frontier.MarkDone(ctx, page); err != nil && ctx.Err() == nil {
		// Warn, not Error: this is the documented claim-guard race
		// (frontier.go's RecoverStale doc comment) — a stale-recovered row
		// re-claimed by another worker makes THIS worker's completion a
		// benign no-op, not an operator-actionable failure. It can fire at
		// per-page volume under normal operation, so it must not reach the
		// durable mirror.
		slog.Warn("mark done failed", "component", "runner.crawl", "url", page.URLCanon, "error", err)
	}
}

// failPage is the single tagging point for MarkFailed. It records the
// error, increments the counter, and delegates retry-vs-permanent to the
// frontier's retry policy.
func (cr *CrawlRunner) failPage(ctx context.Context, page *model.Page, category, reason string, permanent bool) {
	atomic.AddInt64(&cr.errors, 1)
	if err := cr.app.Frontier.MarkFailed(ctx, page, fmt.Sprintf("%s: %s", category, reason), permanent); err != nil && ctx.Err() == nil {
		// Warn, not Error: same claim-guard race as MarkDone above — the
		// page's actual failure reason is already durable in
		// raw.pages.last_error via MarkFailed itself, so this call site is
		// only about MarkFailed's own write failing, which at scale is
		// dominated by the benign race, not genuine infra trouble.
		slog.Warn("mark failed write failed", "component", "runner.crawl", "url", page.URLCanon, "error", err)
	}
}

func (cr *CrawlRunner) checkRobots(ctx context.Context, page *model.Page) bool {
	if cr.app.Robots == nil {
		return true
	}
	if cr.app.Robots.IsAllowed(ctx, page.URLRaw) {
		return true
	}
	cr.failPage(ctx, page, "robots", "disallowed by robots.txt", true)
	return false
}

func (cr *CrawlRunner) fetchPage(ctx context.Context, page *model.Page) (*httpclient.FetchResult, bool) {
	result := cr.app.Fetcher.Fetch(ctx, page.URLRaw, page.Domain)
	atomic.AddInt64(&cr.fetched, 1)

	if result.Error != nil {
		cr.failPage(ctx, page, "fetch", result.Error.Error(), false)
		return nil, false
	}

	page.HTTPStatus = result.StatusCode
	page.ETag = result.ETag
	page.LastModified = result.LastModified

	if result.StatusCode < 200 || result.StatusCode >= 300 {
		permanent := result.StatusCode >= 400 && result.StatusCode < 500
		cr.failPage(ctx, page, "http", fmt.Sprintf("status %d", result.StatusCode), permanent)
		return nil, false
	}

	return result, true
}

func (cr *CrawlRunner) storeRaw(ctx context.Context, page *model.Page, result *httpclient.FetchResult) (string, bool) {
	ext := ".html"
	if strings.Contains(page.URLRaw, ".json") {
		ext = ".json"
	}
	hash, err := cr.app.RawStore.Store(ctx, result.Body, ext)
	if err != nil {
		cr.failPage(ctx, page, "store", err.Error(), false)
		return "", false
	}

	page.ContentSHA256 = hash
	atomic.AddInt64(&cr.stored, 1)
	return hash, true
}

func (cr *CrawlRunner) extractAndEnqueue(ctx context.Context, page *model.Page, body []byte, hash string) {
	meta, _ := html.Extract(body, page.URLRaw)
	if meta == nil {
		return
	}

	// Only enqueue same-domain links to keep the crawl focused.
	if len(meta.Outlinks) > 0 {
		var sameDomainLinks []string
		for _, link := range meta.Outlinks {
			if util.ExtractDomain(link) == page.Domain {
				sameDomainLinks = append(sameDomainLinks, link)
			}
		}
		if len(sameDomainLinks) > 0 {
			if _, err := cr.app.Frontier.PushLinks(ctx, sameDomainLinks, 0); err != nil && ctx.Err() == nil {
				slog.Error("push links failed", "component", "runner.crawl", "url", page.URLCanon, "error", err)
			}
		}
	}

	// Do NOT overwrite page.URLCanon here: it is the frontier key that
	// MarkDone matches on. The page-declared <link rel="canonical"> is
	// untrusted and often differs (trailing slash, tracking params, or an
	// entirely different domain); mutating the key would leave the fetched
	// row stuck INFLIGHT and refetched forever. WriteFromMeta validates the
	// declared canonical on its own and keys articles_raw off it when safe.
	cr.writer.WriteFromMeta(page, meta, hash)
}
