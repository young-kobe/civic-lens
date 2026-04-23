package runner

import (
	"context"
	"fmt"
	"log"
	"time"

	"github.com/young-kobe/civic-lens/ingest/internal/extract/html"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
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
func (w *ArticleWriter) WriteFromMeta(page *model.Page, meta *html.Metadata, hash string) {
	canonURL := page.URLCanon
	if meta.CanonicalURL != "" {
		canonURL = meta.CanonicalURL
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

// flush writes a batch of articles in a single transaction.
func (w *ArticleWriter) flush(ctx context.Context, batch []articleEntry) {
	if len(batch) == 0 {
		return
	}

	tx, err := w.database.BeginImmediate(ctx)
	if err != nil {
		log.Printf("ArticleWriter: begin tx failed: %v", err)
		return
	}

	// When WriteFromMeta prefers the HTML's <link rel="canonical"> over the
	// URL we actually fetched, the canonical URL is often NOT in the `pages`
	// table yet (only the fetched url_canon is). articles_raw has a FK to
	// pages(url_canon) so the insert fails with SQLITE_CONSTRAINT_FOREIGNKEY
	// (code 787) and the article is dropped. Fix: upsert a DONE pages row
	// for the canonical URL before the article insert. The placeholder row
	// carries state=2 (DONE) + next_fetch_at=0 so the frontier never picks
	// it up as work; url_raw mirrors url_canon since we don't have a
	// separate raw form for these synthetic rows.
	pageStmt, err := tx.PrepareContext(ctx, `
		INSERT OR IGNORE INTO pages (url_canon, url_raw, domain, state, priority, retries, next_fetch_at, inflight_at)
		VALUES (?, ?, ?, 2, 0, 0, 0, 0)
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
