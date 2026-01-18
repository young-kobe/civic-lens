-- 001_initial.sql
-- Civic Lens unified schema (ingestion + analysis)
-- Single source of truth for all database tables

-- ============================================
-- Ingestion Tables
-- ============================================

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

-- ============================================
-- Analysis Tables
-- ============================================

-- Cleaned/Normalized Documents (Input for AI Analysis)
CREATE TABLE IF NOT EXISTS docs (
    doc_id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_type TEXT NOT NULL CHECK(source_type IN ('news', 'reddit', 'reddit_post', 'reddit_comment')),
    ident TEXT UNIQUE NOT NULL,
    domain_or_subreddit TEXT,
    published_at INTEGER,
    fetched_at INTEGER,
    title TEXT,
    text TEXT,
    raw_hash TEXT NOT NULL,
    metadata_json TEXT
);

CREATE INDEX IF NOT EXISTS idx_docs_published_at ON docs(published_at);
CREATE INDEX IF NOT EXISTS idx_docs_ident ON docs(ident);

-- AI Analysis Outputs
CREATE TABLE IF NOT EXISTS ai_outputs (
    output_id INTEGER PRIMARY KEY AUTOINCREMENT,
    doc_id INTEGER NOT NULL,
    model_id TEXT,
    prompt_version TEXT,
    task_type TEXT NOT NULL,
    output_json TEXT NOT NULL,
    confidence REAL,
    created_at INTEGER,
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_ai_outputs_doc_task ON ai_outputs(doc_id, task_type);

-- Story Clusters
CREATE TABLE IF NOT EXISTS clusters (
    cluster_id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT,
    summary TEXT,
    created_at INTEGER,
    clustering_version TEXT
);

CREATE TABLE IF NOT EXISTS cluster_assignments (
    assignment_id INTEGER PRIMARY KEY AUTOINCREMENT,
    cluster_id INTEGER NOT NULL,
    doc_id INTEGER NOT NULL,
    score REAL,
    FOREIGN KEY(cluster_id) REFERENCES clusters(cluster_id),
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_cluster_assign_cluster ON cluster_assignments(cluster_id);

-- ============================================
-- Schema Version Tracking
-- ============================================

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at INTEGER NOT NULL
);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (1, strftime('%s', 'now'));
