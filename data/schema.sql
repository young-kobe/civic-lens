-- Civic Lens SQLite Schema
-- STRICT Mode enabled where possible (SQLite 3.37+)
-- Enforces the invariants defined in the project plan.

-- A3: Frontier / State Machine
CREATE TABLE pages (
    url_canon TEXT PRIMARY KEY,
    state TEXT NOT NULL CHECK(state IN ('QUEUED', 'INFLIGHT', 'DONE', 'FAILED', 'QUEUED_BACKOFF')), 
    next_fetch_at INTEGER, -- Unix Timestamp
    retries INTEGER DEFAULT 0,
    inflight_at INTEGER,
    domain TEXT NOT NULL,
    priority INTEGER DEFAULT 0,
    last_error TEXT,
    etag TEXT,
    last_modified TEXT,
    content_hash TEXT
);

CREATE INDEX idx_pages_state_next_fetch ON pages(state, next_fetch_at);
CREATE INDEX idx_pages_domain ON pages(domain);

CREATE TABLE fetch_events (
    event_id INTEGER PRIMARY KEY AUTOINCREMENT,
    page_url TEXT NOT NULL,
    timestamp INTEGER NOT NULL,
    status INTEGER, -- HTTP Status
    bytes INTEGER,
    latency_ms INTEGER,
    error TEXT,
    FOREIGN KEY(page_url) REFERENCES pages(url_canon)
);

-- A7: Output Tables (Raw Content Pointers)
CREATE TABLE articles_raw (
    article_id INTEGER PRIMARY KEY AUTOINCREMENT,
    url_canon TEXT UNIQUE NOT NULL,
    source TEXT,
    domain TEXT,
    fetched_at INTEGER,
    raw_hash TEXT NOT NULL, -- Points to data/raw/sha256/<hash>.html
    title TEXT,
    published_at INTEGER,
    excerpt TEXT,
    extraction_version TEXT
);

CREATE TABLE reddit_posts_raw (
    post_id INTEGER PRIMARY KEY AUTOINCREMENT,
    fullname TEXT UNIQUE NOT NULL, -- e.g. t3_...
    subreddit TEXT,
    created_utc INTEGER,
    raw_hash TEXT NOT NULL, -- Points to data/raw/sha256/<hash>.json
    title TEXT,
    body TEXT,
    score INTEGER,
    num_comments INTEGER,
    extraction_version TEXT
);

CREATE TABLE reddit_comments_raw (
    comment_id INTEGER PRIMARY KEY AUTOINCREMENT,
    fullname TEXT UNIQUE NOT NULL, -- e.g. t1_...
    post_fullname TEXT,
    created_utc INTEGER,
    raw_hash TEXT NOT NULL,
    body TEXT,
    score INTEGER,
    extraction_version TEXT
);

-- B1: Cleaned/Normalized Documents (Input for AI Analysis)
CREATE TABLE docs (
    doc_id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_type TEXT NOT NULL CHECK(source_type IN ('news', 'reddit', 'reddit_post', 'reddit_comment')),
    ident TEXT UNIQUE NOT NULL, -- url_canon or fullname
    domain_or_subreddit TEXT,
    published_at INTEGER,
    fetched_at INTEGER,
    title TEXT,
    text TEXT,
    raw_hash TEXT NOT NULL, -- Traceability back to raw source
    metadata_json TEXT -- Flexible JSON for extra fields
);

CREATE INDEX idx_docs_published_at ON docs(published_at);

-- C1: AI Analysis Outputs
CREATE TABLE ai_outputs (
    output_id INTEGER PRIMARY KEY AUTOINCREMENT,
    doc_id INTEGER NOT NULL,
    model_id TEXT,
    prompt_version TEXT,
    task_type TEXT NOT NULL, -- 'sentiment', 'bot_score', 'topic', 'stance'
    output_json TEXT NOT NULL,
    confidence REAL,
    created_at INTEGER,
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX idx_ai_outputs_doc_task ON ai_outputs(doc_id, task_type);

-- D1: Story Clusters
CREATE TABLE clusters (
    cluster_id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT,
    summary TEXT,
    created_at INTEGER,
    clustering_version TEXT
);

CREATE TABLE cluster_assignments (
    assignment_id INTEGER PRIMARY KEY AUTOINCREMENT,
    cluster_id INTEGER NOT NULL,
    doc_id INTEGER NOT NULL,
    score REAL, -- distance or membership probability
    FOREIGN KEY(cluster_id) REFERENCES clusters(cluster_id),
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX idx_cluster_assign_cluster ON cluster_assignments(cluster_id);
