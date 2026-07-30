package runner

import (
	"testing"

	"github.com/young-kobe/civic-lens/ingest/internal/extract/html"
	"github.com/young-kobe/civic-lens/ingest/internal/model"
	"github.com/young-kobe/civic-lens/ingest/internal/storage/db"
)

// writeAndFlush runs one WriteFromMeta through a real writer and drains it.
// Used by the gated Postgres integration tests (postgres_integration_test.go).
func writeAndFlush(t *testing.T, database *db.DB, page *model.Page, meta *html.Metadata, hash string) {
	t.Helper()
	w := NewArticleWriter(database)
	go w.Start()
	w.WriteFromMeta(page, meta, hash)
	w.Close() // closes the channel, drains, and flushes
}

// TestValidateCanonical_HostileCrossDomainRejected is the I-2 regression: a
// crawled page declaring og:url / <link rel=canonical> pointing at ANOTHER
// publisher must not be honoured — WriteFromMeta falls back to the frontier
// URL instead, so that outlet's articles_raw row can never be overwritten.
func TestValidateCanonical_HostileCrossDomainRejected(t *testing.T) {
	if got := validateCanonical("https://evil.com/hijack", "outleta.com"); got != "" {
		t.Errorf("validateCanonical cross-domain = %q, want empty (rejected)", got)
	}
}

// TestValidateCanonical_SameRegistrableDomainAccepted asserts a declared
// canonical on the same registrable domain (including a www. subdomain) is
// honoured, preserving dedup across URL variants of one article.
func TestValidateCanonical_SameRegistrableDomainAccepted(t *testing.T) {
	got := validateCanonical("https://www.outleta.com/story", "outleta.com")
	if got != "https://www.outleta.com/story" {
		t.Errorf("validateCanonical same-domain = %q, want the declared canonical accepted", got)
	}
}

// TestValidateCanonical_EmptyOrUnparseableRejected asserts an empty or
// unparseable declared canonical falls back to the frontier URL rather than
// erroring.
func TestValidateCanonical_EmptyOrUnparseableRejected(t *testing.T) {
	if got := validateCanonical("", "outleta.com"); got != "" {
		t.Errorf("validateCanonical empty = %q, want empty", got)
	}
	if got := validateCanonical("not a url at all \x7f", "outleta.com"); got != "" {
		t.Errorf("validateCanonical unparseable = %q, want empty", got)
	}
}
