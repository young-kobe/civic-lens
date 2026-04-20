-- 013_pages_state_check.sql
-- Adds CHECK(state IN (0,1,2,3)) to the pages frontier table (walkthrough 038).
-- SQLite doesn't support ALTER TABLE ADD CONSTRAINT, so this uses the canonical
-- table-rebuild procedure: create new table with constraint, copy data, drop
-- old, rename. Foreign keys are disabled for the duration and re-enabled after
-- (articles_raw.url_canon → pages.url_canon FK would otherwise block the drop).
--
-- State enum remains as-is (kept as INTEGER for backward compat with Go
-- runner code):
--   0 = QUEUED   1 = INFLIGHT   2 = DONE   3 = FAILED
--
-- The Go runner already treats these codes as the frontier state machine; the
-- constraint just makes the invariant DB-enforced rather than code-convention.

PRAGMA foreign_keys = OFF;

BEGIN TRANSACTION;

CREATE TABLE pages_new (
    url_canon TEXT PRIMARY KEY,
    url_raw TEXT NOT NULL,
    domain TEXT NOT NULL,
    state INTEGER NOT NULL DEFAULT 0 CHECK(state IN (0, 1, 2, 3)),
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

INSERT INTO pages_new SELECT * FROM pages;

DROP TABLE pages;
ALTER TABLE pages_new RENAME TO pages;

CREATE INDEX IF NOT EXISTS idx_pages_state_next_fetch ON pages(state, next_fetch_at);
CREATE INDEX IF NOT EXISTS idx_pages_domain ON pages(domain);

COMMIT;

PRAGMA foreign_keys = ON;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (13, strftime('%s', 'now'));
