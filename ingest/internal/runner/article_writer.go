package runner

import (
	"context"
	"fmt"
	"log"
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

// flush writes a batch of articles in a single transaction, dispatching to
// the SQLite or Postgres statement text for the backend this writer's
// database was opened against (see db.DB.IsPostgres).
func (w *ArticleWriter) flush(ctx context.Context, batch []articleEntry) {
	if len(batch) == 0 {
		return
	}
	if w.database.IsPostgres() {
		w.flushPostgres(ctx, batch)
		return
	}
	w.flushSQLite(ctx, batch)
}

// flushSQLite writes a batch of articles in a single transaction against the
// SQLite backend. This is the live production path — kept byte-identical
// while the Postgres path (flushPostgres, article_writer_postgres.go) is
// built out alongside it.
func (w *ArticleWriter) flushSQLite(ctx context.Context, batch []articleEntry) {
	tx, err := w.database.BeginImmediate(ctx)
	if err != nil {
		log.Printf("ArticleWriter: begin tx failed: %v", err)
		return
	}

	// When WriteFromMeta keys an article off a validated same-domain
	// canonical that differs from the URL we fetched, that canonical URL is
	// often NOT in the `pages` table yet (only the fetched url_canon is).
	// articles_raw has a FK to pages(url_canon) so the insert would fail with
	// SQLITE_CONSTRAINT_FOREIGNKEY (code 787) and the article would be
	// dropped. We upsert a placeholder pages row before the article insert.
	//
	// The placeholder is QUEUED (state=0), NOT DONE: this is a real,
	// same-publisher URL we have not fetched, so it is honest crawl work.
	// Marking it DONE would permanently block the crawler from ever fetching
	// it (PushLinks's INSERT OR IGNORE would no-op on the existing row). When
	// the canonical equals the URL we just fetched, the row already exists as
	// INFLIGHT/DONE and INSERT OR IGNORE leaves it untouched.
	pageStmt, err := tx.PrepareContext(ctx, `
		INSERT OR IGNORE INTO pages (url_canon, url_raw, domain, state, priority, retries, next_fetch_at, inflight_at)
		VALUES (?, ?, ?, 0, 0, 0, 0, 0)
	`)
	if err != nil {
		log.Printf("ArticleWriter: prepare pages upsert failed: %v", err)
		tx.Rollback()
		return
	}
	defer pageStmt.Close()

	stmt, err := tx.PrepareContext(ctx, `
		INSERT INTO articles_raw (url_canon, domain, fetched_at, published_at, title, raw_hash, extraction_version)
		VALUES (?, ?, ?, ?, ?, ?, ?)
		ON CONFLICT(url_canon) DO UPDATE SET
			fetched_at = excluded.fetched_at,
			published_at = excluded.published_at,
			title = excluded.title,
			raw_hash = excluded.raw_hash,
			extraction_version = excluded.extraction_version
	`)
	if err != nil {
		log.Printf("ArticleWriter: prepare failed: %v", err)
		tx.Rollback()
		return
	}
	defer stmt.Close()

	for _, e := range batch {
		// Ensure a pages row exists for this canonical URL so the
		// articles_raw FK resolves. No-op when the row already exists.
		if _, err := pageStmt.ExecContext(ctx, e.CanonURL, e.CanonURL, e.Domain); err != nil {
			log.Printf("ArticleWriter: pages upsert %s failed: %v", e.CanonURL, err)
			continue
		}
		if _, err := stmt.ExecContext(ctx, e.CanonURL, e.Domain, e.FetchedAt, e.PublishedAt, e.Title, e.RawHash, e.Version); err != nil {
			log.Printf("ArticleWriter: insert %s failed: %v", e.CanonURL, err)
		}
	}

	if err := tx.Commit(); err != nil {
		log.Printf("ArticleWriter: commit failed: %v", err)
	} else {
		fmt.Printf("ArticleWriter: flushed %d articles\n", len(batch))
	}
}
