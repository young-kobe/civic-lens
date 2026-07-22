package runner

import (
	"context"
	"fmt"
	"log"
)

// flushPostgres is the Postgres counterpart to flushSQLite: same batching and
// transaction shape, targeting raw.pages/raw.articles with $-placeholders and
// TIMESTAMPTZ columns. See flushSQLite for the placeholder-pages rationale
// (a validated same-domain canonical can point at a URL raw.pages does not
// have a row for yet, and raw.articles.url_canon FKs to raw.pages).
func (w *ArticleWriter) flushPostgres(ctx context.Context, batch []articleEntry) {
	tx, err := w.database.BeginImmediate(ctx)
	if err != nil {
		log.Printf("ArticleWriter: begin tx failed: %v", err)
		return
	}

	// Placeholder pages row: QUEUED (not DONE), same reasoning as the SQLite
	// path — this is honest, not-yet-fetched crawl work for the same
	// publisher, not a substitute for actually fetching it.
	pageStmt, err := tx.PrepareContext(ctx, `
		INSERT INTO raw.pages (url_canon, url_raw, domain, state, priority, retries, next_fetch_at, inflight_at)
		VALUES ($1, $2, $3, 'queued', 0, 0, to_timestamp(0), to_timestamp(0))
		ON CONFLICT (url_canon) DO NOTHING
	`)
	if err != nil {
		log.Printf("ArticleWriter: prepare pages upsert failed: %v", err)
		tx.Rollback()
		return
	}
	defer pageStmt.Close()

	stmt, err := tx.PrepareContext(ctx, `
		INSERT INTO raw.articles (url_canon, domain, fetched_at, published_at, title, raw_hash, extraction_version)
		VALUES ($1, $2, $3, $4, $5, $6, $7)
		ON CONFLICT (url_canon) DO UPDATE SET
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
		if _, err := pageStmt.ExecContext(ctx, e.CanonURL, e.CanonURL, e.Domain); err != nil {
			log.Printf("ArticleWriter: pages upsert %s failed: %v", e.CanonURL, err)
			continue
		}
		if _, err := stmt.ExecContext(ctx, e.CanonURL, e.Domain, unixTime(e.FetchedAt), unixOrNil(e.PublishedAt), e.Title, e.RawHash, e.Version); err != nil {
			log.Printf("ArticleWriter: insert %s failed: %v", e.CanonURL, err)
		}
	}

	if err := tx.Commit(); err != nil {
		log.Printf("ArticleWriter: commit failed: %v", err)
	} else {
		fmt.Printf("ArticleWriter: flushed %d articles\n", len(batch))
	}
}
