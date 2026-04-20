package httpclient

import (
	"context"
	"fmt"
	"io"
	"math/rand"
	"net/http"
	"sync"
	"time"

	"golang.org/x/time/rate"
)

// Fetcher provides rate-limited HTTP fetching with retries.
type Fetcher struct {
	client       *http.Client
	userAgent    string
	maxRetries   int
	limiters     map[string]*rate.Limiter
	limitersMu   sync.Mutex
	ratePerSec   float64
	maxRedirects int
}

// Config holds fetcher configuration.
type Config struct {
	UserAgent      string
	RequestTimeout time.Duration
	MaxRetries     int
	RatePerSec     float64
	MaxRedirects   int
}

// New creates a new Fetcher.
func New(cfg Config) *Fetcher {
	transport := &http.Transport{
		MaxIdleConns:        100,
		MaxIdleConnsPerHost: 10,
		IdleConnTimeout:     90 * time.Second,
	}

	client := &http.Client{
		Transport: transport,
		Timeout:   cfg.RequestTimeout,
		CheckRedirect: func(req *http.Request, via []*http.Request) error {
			if len(via) >= cfg.MaxRedirects {
				return fmt.Errorf("stopped after %d redirects", cfg.MaxRedirects)
			}
			// Guard against redirect-chain SSRF: a trusted initial host can
			// 302 us to 169.254.169.254 or 127.0.0.1 (audit §1.10).
			if err := validateURL(req.URL.String()); err != nil {
				return fmt.Errorf("redirect target rejected: %w", err)
			}
			return nil
		},
	}

	return &Fetcher{
		client:       client,
		userAgent:    cfg.UserAgent,
		maxRetries:   cfg.MaxRetries,
		limiters:     make(map[string]*rate.Limiter),
		ratePerSec:   cfg.RatePerSec,
		maxRedirects: cfg.MaxRedirects,
	}
}

// getRateLimiter returns a per-domain rate limiter.
func (f *Fetcher) getRateLimiter(domain string) *rate.Limiter {
	f.limitersMu.Lock()
	defer f.limitersMu.Unlock()

	if l, ok := f.limiters[domain]; ok {
		return l
	}

	l := rate.NewLimiter(rate.Limit(f.ratePerSec), 1)
	f.limiters[domain] = l
	return l
}

// FetchResult contains the result of a fetch operation.
type FetchResult struct {
	StatusCode   int
	Body         []byte
	ETag         string
	LastModified string
	Error        error
}

// Fetch retrieves a URL with rate limiting and retries.
func (f *Fetcher) Fetch(ctx context.Context, urlStr string, domain string) *FetchResult {
	// Reject private / loopback / non-http(s) URLs up-front so a poisoned
	// seeds.yaml or DB row can't reach local services. Redirect targets
	// are validated inside CheckRedirect (audit §1.10).
	if err := validateURL(urlStr); err != nil {
		return &FetchResult{Error: fmt.Errorf("url rejected: %w", err)}
	}
	limiter := f.getRateLimiter(domain)

	var lastErr error
	for attempt := 0; attempt <= f.maxRetries; attempt++ {
		// Wait for rate limiter
		if err := limiter.Wait(ctx); err != nil {
			return &FetchResult{Error: fmt.Errorf("rate limit wait: %w", err)}
		}

		result := f.doFetch(ctx, urlStr)
		if result.Error == nil {
			return result
		}

		lastErr = result.Error

		// Don't retry on context cancelled
		if ctx.Err() != nil {
			return &FetchResult{Error: ctx.Err()}
		}

		// Exponential backoff with jitter
		if attempt < f.maxRetries {
			backoff := time.Duration(1<<uint(attempt))*100*time.Millisecond +
				time.Duration(rand.Intn(100))*time.Millisecond
			select {
			case <-time.After(backoff):
			case <-ctx.Done():
				return &FetchResult{Error: ctx.Err()}
			}
		}
	}

	return &FetchResult{Error: fmt.Errorf("max retries exceeded: %w", lastErr)}
}

func (f *Fetcher) doFetch(ctx context.Context, urlStr string) *FetchResult {
	req, err := http.NewRequestWithContext(ctx, "GET", urlStr, nil)
	if err != nil {
		return &FetchResult{Error: fmt.Errorf("create request: %w", err)}
	}

	req.Header.Set("User-Agent", f.userAgent)
	req.Header.Set("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
	req.Header.Set("Accept-Language", "en-US,en;q=0.5")

	resp, err := f.client.Do(req)
	if err != nil {
		return &FetchResult{Error: fmt.Errorf("http request: %w", err)}
	}
	defer resp.Body.Close()

	// Read body with limit
	body, err := io.ReadAll(io.LimitReader(resp.Body, 10*1024*1024)) // 10MB limit
	if err != nil {
		return &FetchResult{Error: fmt.Errorf("read body: %w", err)}
	}

	return &FetchResult{
		StatusCode:   resp.StatusCode,
		Body:         body,
		ETag:         resp.Header.Get("ETag"),
		LastModified: resp.Header.Get("Last-Modified"),
	}
}
