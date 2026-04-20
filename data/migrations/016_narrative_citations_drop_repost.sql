-- 016_narrative_citations_drop_repost.sql
-- Drop the unused 'repost' link_type. No writer in the codebase emits it
-- (citation_extractor.py only produces url_citation/quote/reply/retweet),
-- and carrying a dead enum value makes UI consumers defensive about cases
-- that never occur (audit 2026-04-19 §8).
--
-- SQLite has no ALTER CHECK, so we rebuild: new table with tightened CHECK,
-- copy rows (defensively migrating any stray 'repost' to 'retweet' since
-- that's the semantics it would have mapped to), drop old, rename.

BEGIN TRANSACTION;

CREATE TABLE narrative_citations_new (
    citation_id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_doc_id INTEGER NOT NULL,
    target_doc_id INTEGER,
    target_url TEXT,
    link_type TEXT NOT NULL CHECK(link_type IN ('url_citation', 'quote', 'reply', 'retweet')),
    discovered_at INTEGER NOT NULL,
    CHECK ((target_doc_id IS NOT NULL) OR (target_url IS NOT NULL)),
    FOREIGN KEY(source_doc_id) REFERENCES docs(doc_id),
    FOREIGN KEY(target_doc_id) REFERENCES docs(doc_id)
);

INSERT INTO narrative_citations_new (citation_id, source_doc_id, target_doc_id, target_url, link_type, discovered_at)
SELECT citation_id, source_doc_id, target_doc_id, target_url,
       CASE WHEN link_type = 'repost' THEN 'retweet' ELSE link_type END,
       discovered_at
FROM narrative_citations;

DROP TABLE narrative_citations;
ALTER TABLE narrative_citations_new RENAME TO narrative_citations;

CREATE INDEX IF NOT EXISTS idx_narrative_citations_source ON narrative_citations(source_doc_id);
CREATE INDEX IF NOT EXISTS idx_narrative_citations_target ON narrative_citations(target_doc_id);
CREATE INDEX IF NOT EXISTS idx_narrative_citations_target_url ON narrative_citations(target_url);

COMMIT;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (16, strftime('%s', 'now'));
