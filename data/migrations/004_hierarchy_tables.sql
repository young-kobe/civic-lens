-- 004_hierarchy_tables.sql
-- Content hierarchy and engagement tracking

-- Author profiles for hierarchy classification
CREATE TABLE IF NOT EXISTS author_profiles (
    author_id TEXT PRIMARY KEY,
    platform TEXT NOT NULL,
    username TEXT,
    display_name TEXT,
    follower_count INTEGER DEFAULT 0,
    avg_engagement REAL DEFAULT 0,
    -- Classification
    hierarchy_level TEXT DEFAULT 'individual',  -- 'individual', 'group', 'organization'
    influence_score REAL DEFAULT 0,
    -- X API specific
    is_identity_verified INTEGER DEFAULT 0,
    verified_type TEXT,  -- 'blue', 'business', 'government'
    account_created_at INTEGER,
    updated_at INTEGER
);

-- Engagement tracking for high-value content identification
CREATE TABLE IF NOT EXISTS engagement_metrics (
    metric_id INTEGER PRIMARY KEY AUTOINCREMENT,
    doc_id INTEGER NOT NULL,
    like_count INTEGER DEFAULT 0,
    retweet_count INTEGER DEFAULT 0,
    reply_count INTEGER DEFAULT 0,
    quote_count INTEGER DEFAULT 0,
    -- Weighted: likes + (2 * retweets) + (1.5 * replies) + (1.5 * quotes)
    engagement_score REAL GENERATED ALWAYS AS (
        like_count + (retweet_count * 2.0) + (reply_count * 1.5) + (quote_count * 1.5)
    ) STORED,
    hierarchy_level TEXT,
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_engagement_score
    ON engagement_metrics(engagement_score DESC);

CREATE INDEX IF NOT EXISTS idx_author_hierarchy
    ON author_profiles(hierarchy_level);
