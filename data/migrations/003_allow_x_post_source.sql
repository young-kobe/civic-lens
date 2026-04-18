-- 003_allow_x_post_source.sql
-- Relax check constraint on docs.source_type to allow 'x_post'

PRAGMA foreign_keys=off;

BEGIN TRANSACTION;

-- Create new table with updated CHECK constraint
CREATE TABLE IF NOT EXISTS docs_new (
    doc_id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_type TEXT NOT NULL CHECK(source_type IN ('news', 'reddit', 'reddit_post', 'reddit_comment', 'x_post')),
    ident TEXT UNIQUE NOT NULL,
    domain_or_subreddit TEXT,
    published_at INTEGER,
    fetched_at INTEGER,
    title TEXT,
    text TEXT,
    raw_hash TEXT NOT NULL,
    metadata_json TEXT
);

-- Copy data
INSERT INTO docs_new (doc_id, source_type, ident, domain_or_subreddit, published_at, fetched_at, title, text, raw_hash, metadata_json)
SELECT doc_id, source_type, ident, domain_or_subreddit, published_at, fetched_at, title, text, raw_hash, metadata_json
FROM docs;

-- Drop old table
DROP TABLE docs;

-- Rename new table
ALTER TABLE docs_new RENAME TO docs;

-- Recreate indexes
CREATE INDEX IF NOT EXISTS idx_docs_published_at ON docs(published_at);
CREATE INDEX IF NOT EXISTS idx_docs_ident ON docs(ident);

-- Record migration
INSERT INTO schema_version (version, applied_at) VALUES (3, strftime('%s', 'now'));

COMMIT;

PRAGMA foreign_keys=on;
