package runner

import (
	"context"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/extract/html"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
	"github.com/young-kobe/civic-lens/ingest/internal/util"
)

const (
	defaultBatchSize     = 50
	defaultFlushInterval = 2 * time.Second
)

// articleEntry holds data for one article row insertion.
type articleEntry struct {
	CanonURL    string
	Domain      string
	FetchedAt   int64
	PublishedAt int64
	Title       string
	RawHash     string
	Version     string
}

// ArticleWriter batches article inserts into transactions to reduce
// per-row DB contention in the crawl loop.
type ArticleWriter struct {
	database      *db.DB
	ch            chan articleEntry
	batchSize     int
	flushInterval time.Duration
	done          chan struct{}
	// ctx is the lifetime context for DB writes. Closed by Close() so
	// pending transactions abort cleanly if the caller has cancelled.
	ctx    context.Context
	cancel context.CancelFunc
}

// NewArticleWriter creates a writer with a buffered channel.
func NewArticleWriter(database *db.DB) *ArticleWriter {
	ctx, cancel := context.WithCancel(context.Background())
	return &ArticleWriter{
		database:      database,
		ch:            make(chan articleEntry, defaultBatchSize*2),
		batchSize:     defaultBatchSize,
		flushInterval: defaultFlushInterval,
		done:          make(chan struct{}),
		ctx:           ctx,
		cancel:        cancel,
	}
}

// Write enqueues an article for batched insertion.
func (w *ArticleWriter) Write(entry articleEntry) {
	w.ch <- entry
}

// WriteFromMeta creates an articleEntry from page metadata and enqueues it.
//
// The page-declared canonical (<link rel="canonical"> or og:url) is untrusted:
// a hostile or misconfigured page could point it at another publisher's URL to
// overwrite that outlet's articles_raw row. We accept the declared canonical
// only when it parses, canonicalizes, and shares a registrable domain with the
// page we actually fetched; otherwise we key the article off the frontier URL.
func (w *ArticleWriter) WriteFromMeta(page *model.Page, meta *html.Metadata, hash string) {
	canonURL := page.URLCanon
	if declared := validateCanonical(meta.CanonicalURL, page.Domain); declared != "" {
		canonURL = declared
	}

	var publishedAt int64
	if !meta.PublishedTime.IsZero() {
		publishedAt = meta.PublishedTime.Unix()
	}

	w.Write(articleEntry{
		CanonURL:    canonURL,
		Domain:      page.Domain,
		FetchedAt:   time.Now().Unix(),
		PublishedAt: publishedAt,
		Title:       meta.Title,
		RawHash:     hash,
		Version:     "go-v1.0",
	})
}

// validateCanonical returns the canonicalized declared canonical URL when it
// is safe to key an article off it, or "" to signal the caller should fall
// back to the frontier URL. "Safe" means: non-empty, parseable/canonicalizable,
// and same registrable domain as the fetched page. fetchedDomain is the host
// of the URL we actually retrieved (page.Domain).
func validateCanonical(declared, fetchedDomain string) string {
	if declared == "" {
		return ""
	}
	canon, err := util.CanonicalizeURL(declared)
	if err != nil {
		return ""
	}
	if !util.SameRegistrableDomain(util.ExtractDomain(canon), fetchedDomain) {
		return ""
	}
	return canon
}

// Start begins the background flush loop. Call this in a goroutine.
func (w *ArticleWriter) Start() {
	defer close(w.done)

	buf := make([]articleEntry, 0, w.batchSize)
	ticker := time.NewTicker(w.flushInterval)
	defer ticker.Stop()

	for {
		select {
		case entry, ok := <-w.ch:
			if !ok {
				// Channel closed — flush remaining using a fresh, short-lived
				// context so we don't lose the drain if w.ctx is cancelled.
				drainCtx, drainCancel := context.WithTimeout(context.Background(), 10*time.Second)
				w.flush(drainCtx, buf)
				drainCancel()
				return
			}
			buf = append(buf, entry)
			if len(buf) >= w.batchSize {
				w.flush(w.ctx, buf)
				buf = buf[:0]
			}

		case <-ticker.C:
			if len(buf) > 0 {
				w.flush(w.ctx, buf)
				buf = buf[:0]
			}
		}
	}
}

// Close signals the writer to stop and waits for it to drain.
func (w *ArticleWriter) Close() {
	close(w.ch)
	<-w.done
	w.cancel()
}

// flush writes a batch of articles in a single transaction. See
// flushPostgres (article_writer_postgres.go) for the statement text.
func (w *ArticleWriter) flush(ctx context.Context, batch []articleEntry) {
	if len(batch) == 0 {
		return
	}
	w.flushPostgres(ctx, batch)
}
