package runner

import (
	"context"
	"path/filepath"
	"testing"

	"github.com/young-kobe/civic-lens/ingest/internal/extract/html"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// newArticleTestDB creates a temp db.DB with just the pages + articles_raw
// tables the ArticleWriter touches, with foreign keys enforced (db.Open sets
// the pragma). It avoids the full migration machinery.
func newArticleTestDB(t *testing.T) *db.DB {
	t.Helper()
	dir := t.TempDir()
	database, err := db.Open(filepath.Join(dir, "aw_test.db"))
	if err != nil {
		t.Fatalf("open: %v", err)
	}
	t.Cleanup(func() { database.Close() })

	ctx := context.Background()
	if _, err := database.Conn().ExecContext(ctx, `
		CREATE TABLE pages (
			url_canon TEXT PRIMARY KEY,
			url_raw TEXT NOT NULL,
			domain TEXT NOT NULL,
			state INTEGER NOT NULL DEFAULT 0,
			priority INTEGER NOT NULL DEFAULT 0,
			retries INTEGER NOT NULL DEFAULT 0,
			next_fetch_at INTEGER NOT NULL DEFAULT 0,
			inflight_at INTEGER NOT NULL DEFAULT 0,
			http_status INTEGER,
			content_sha256 TEXT,
			etag TEXT,
			last_modified TEXT,
			last_error TEXT
		);
		CREATE TABLE articles_raw (
			url_canon TEXT PRIMARY KEY,
			domain TEXT,
			fetched_at INTEGER NOT NULL,
			published_at INTEGER,
			title TEXT,
			raw_hash TEXT NOT NULL,
			extraction_version TEXT NOT NULL,
			FOREIGN KEY(url_canon) REFERENCES pages(url_canon)
		);
	`); err != nil {
		t.Fatalf("schema: %v", err)
	}
	return database
}

// writeAndFlush runs one WriteFromMeta through a real writer and drains it.
func writeAndFlush(t *testing.T, database *db.DB, page *model.Page, meta *html.Metadata, hash string) {
	t.Helper()
	w := NewArticleWriter(database)
	go w.Start()
	w.WriteFromMeta(page, meta, hash)
	w.Close() // closes the channel, drains, and flushes
}

func insertPageRow(t *testing.T, database *db.DB, urlCanon, domain string) {
	t.Helper()
	if _, err := database.Conn().Exec(
		`INSERT INTO pages (url_canon, url_raw, domain, state) VALUES (?, ?, ?, 2)`,
		urlCanon, urlCanon, domain); err != nil {
		t.Fatalf("seed page %s: %v", urlCanon, err)
	}
}

// TestWriteFromMeta_HostileCanonicalDoesNotOverwriteOtherOutlet is the I-2
// regression: a crawled page declaring og:url / <link rel=canonical> pointing
// at ANOTHER publisher must not overwrite that publisher's articles_raw row.
// The article is keyed off the frontier URL instead.
func TestWriteFromMeta_HostileCanonicalDoesNotOverwriteOtherOutlet(t *testing.T) {
	database := newArticleTestDB(t)

	// Victim outlet already has an article row.
	insertPageRow(t, database, "https://evil.com/hijack", "evil.com")
	if _, err := database.Conn().Exec(
		`INSERT INTO articles_raw (url_canon, domain, fetched_at, title, raw_hash, extraction_version)
		 VALUES ('https://evil.com/hijack', 'evil.com', 1, 'Legit', 'ORIGINAL', 'go-v1.0')`); err != nil {
		t.Fatal(err)
	}
	// The page we actually fetched (its frontier row must exist for the FK).
	insertPageRow(t, database, "https://outleta.com/story", "outleta.com")

	page := &model.Page{URLCanon: "https://outleta.com/story", Domain: "outleta.com"}
	meta := &html.Metadata{Title: "Poison", CanonicalURL: "https://evil.com/hijack"}
	writeAndFlush(t, database, page, meta, "POISON")

	// The victim's row is untouched.
	var victimHash string
	if err := database.Conn().QueryRow(
		`SELECT raw_hash FROM articles_raw WHERE url_canon = 'https://evil.com/hijack'`).Scan(&victimHash); err != nil {
		t.Fatal(err)
	}
	if victimHash != "ORIGINAL" {
		t.Errorf("victim raw_hash = %q, want ORIGINAL (not overwritten)", victimHash)
	}

	// The crawled article landed under the frontier key, not evil.com.
	var ownHash string
	if err := database.Conn().QueryRow(
		`SELECT raw_hash FROM articles_raw WHERE url_canon = 'https://outleta.com/story'`).Scan(&ownHash); err != nil {
		t.Fatalf("article not stored under frontier key: %v", err)
	}
	if ownHash != "POISON" {
		t.Errorf("own raw_hash = %q, want POISON", ownHash)
	}
}

// TestWriteFromMeta_SameDomainCanonicalAccepted asserts a valid same-publisher
// canonical is still honoured (dedup preserved) and that the placeholder pages
// row it needs is QUEUED, not DONE — so the crawler can still fetch that real
// URL rather than have it silently blocked (I-2).
func TestWriteFromMeta_SameDomainCanonicalAccepted(t *testing.T) {
	database := newArticleTestDB(t)
	insertPageRow(t, database, "https://outleta.com/story?utm_source=rss", "outleta.com")

	page := &model.Page{URLCanon: "https://outleta.com/story?utm_source=rss", Domain: "outleta.com"}
	// Declared canonical on the same registrable domain (www. subdomain).
	meta := &html.Metadata{Title: "Clean", CanonicalURL: "https://www.outleta.com/story"}
	writeAndFlush(t, database, page, meta, "HASH1")

	var hash string
	if err := database.Conn().QueryRow(
		`SELECT raw_hash FROM articles_raw WHERE url_canon = 'https://www.outleta.com/story'`).Scan(&hash); err != nil {
		t.Fatalf("article not keyed off same-domain canonical: %v", err)
	}
	if hash != "HASH1" {
		t.Errorf("raw_hash = %q, want HASH1", hash)
	}

	var state int
	if err := database.Conn().QueryRow(
		`SELECT state FROM pages WHERE url_canon = 'https://www.outleta.com/story'`).Scan(&state); err != nil {
		t.Fatal(err)
	}
	if model.PageState(state) != model.StateQueued {
		t.Errorf("placeholder pages state = %d, want StateQueued (crawlable, not silently DONE)", state)
	}
}
