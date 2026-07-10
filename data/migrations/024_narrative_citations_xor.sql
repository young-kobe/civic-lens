-- 024_narrative_citations_xor.sql
-- Tighten the target CHECK to the XOR the schema prose (and every writer)
-- already promises: an edge points at exactly one of an ingested doc
-- (target_doc_id) or an external URL (target_url), never both. The old
-- CHECK only required "at least one", so citation_extractor hand-wrote NULL
-- into the unused column and the DB would silently accept a both-set row.
--
-- SQLite has no ALTER CHECK, so we rebuild (same pattern as migration 016).
-- Any hypothetical both-set row keeps target_doc_id — the owned-doc edge is
-- the stronger claim; the URL is redundant with docs.ident on that doc.

BEGIN TRANSACTION;

CREATE TABLE narrative_citations_new (
    citation_id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_doc_id INTEGER NOT NULL,
    target_doc_id INTEGER,
    target_url TEXT,
    link_type TEXT NOT NULL CHECK(link_type IN ('url_citation', 'quote', 'reply', 'retweet')),
    discovered_at INTEGER NOT NULL,
    CHECK ((target_doc_id IS NULL) <> (target_url IS NULL)),
    FOREIGN KEY(source_doc_id) REFERENCES docs(doc_id),
    FOREIGN KEY(target_doc_id) REFERENCES docs(doc_id)
);

INSERT INTO narrative_citations_new (citation_id, source_doc_id, target_doc_id, target_url, link_type, discovered_at)
SELECT citation_id, source_doc_id, target_doc_id,
       CASE WHEN target_doc_id IS NOT NULL THEN NULL ELSE target_url END,
       link_type, discovered_at
FROM narrative_citations;

DROP TABLE narrative_citations;
ALTER TABLE narrative_citations_new RENAME TO narrative_citations;

CREATE INDEX IF NOT EXISTS idx_narrative_citations_source ON narrative_citations(source_doc_id);
CREATE INDEX IF NOT EXISTS idx_narrative_citations_target ON narrative_citations(target_doc_id);
CREATE INDEX IF NOT EXISTS idx_narrative_citations_target_url ON narrative_citations(target_url);

COMMIT;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (24, strftime('%s', 'now'));
