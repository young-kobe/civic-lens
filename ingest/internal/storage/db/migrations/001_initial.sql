-- 001_initial.sql
-- Civic Lens ingestion schema

-- Pages (Frontier)
CREATE TABLE IF NOT EXISTS pages (
    url_canon TEXT PRIMARY KEY,
    url_raw TEXT NOT NULL,
    domain TEXT NOT NULL,
    state INTEGER NOT NULL DEFAULT 0,  -- 0=QUEUED, 1=INFLIGHT, 2=DONE, 3=FAILED
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

CREATE INDEX IF NOT EXISTS idx_pages_state_next_fetch ON pages(state, next_fetch_at);
CREATE INDEX IF NOT EXISTS idx_pages_domain ON pages(domain);

-- Articles extracted metadata
CREATE TABLE IF NOT EXISTS articles_raw (
    url_canon TEXT PRIMARY KEY,
    domain TEXT,
    fetched_at INTEGER NOT NULL,
    published_at INTEGER,
    title TEXT,
    raw_hash TEXT NOT NULL,
    extraction_version TEXT NOT NULL,
    FOREIGN KEY(url_canon) REFERENCES pages(url_canon)
);

-- Reddit Posts
CREATE TABLE IF NOT EXISTS reddit_posts_raw (
    fullname TEXT PRIMARY KEY,
    subreddit TEXT,
    created_utc INTEGER,
    fetched_at INTEGER,
    title TEXT,
    body TEXT,
    score INTEGER,
    num_comments INTEGER,
    raw_hash TEXT NOT NULL,
    extraction_version TEXT NOT NULL
);

-- Reddit Comments
CREATE TABLE IF NOT EXISTS reddit_comments_raw (
    fullname TEXT PRIMARY KEY,
    post_fullname TEXT,
    subreddit TEXT,
    created_utc INTEGER,
    fetched_at INTEGER,
    body TEXT,
    score INTEGER,
    raw_hash TEXT NOT NULL,
    extraction_version TEXT NOT NULL
);

-- Schema version tracking
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL
);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (1, strftime('%s', 'now'));
