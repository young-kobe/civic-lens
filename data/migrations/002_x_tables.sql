-- 002_x_tables.sql
-- X (Twitter) API v2 data tables

-- X Posts (tweets)
CREATE TABLE IF NOT EXISTS x_posts_raw (
    tweet_id TEXT PRIMARY KEY,
    author_id TEXT NOT NULL,
    conversation_id TEXT,
    created_at INTEGER NOT NULL,
    fetched_at INTEGER NOT NULL,
    text TEXT NOT NULL,
    lang TEXT,
    -- Public metrics
    retweet_count INTEGER DEFAULT 0,
    reply_count INTEGER DEFAULT 0,
    like_count INTEGER DEFAULT 0,
    quote_count INTEGER DEFAULT 0,
    -- Geo (from includes.places expansion)
    place_id TEXT,
    place_country_code TEXT,
    place_full_name TEXT,
    -- Context annotations (JSON array for topic detection)
    context_annotations_json TEXT,
    -- Reply/quote context
    in_reply_to_user_id TEXT,
    referenced_tweet_id TEXT,
    referenced_tweet_type TEXT,  -- 'replied_to', 'quoted', 'retweeted'
    -- Storage
    raw_hash TEXT NOT NULL,
    extraction_version TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_x_posts_author ON x_posts_raw(author_id);
CREATE INDEX IF NOT EXISTS idx_x_posts_created ON x_posts_raw(created_at);
CREATE INDEX IF NOT EXISTS idx_x_posts_country ON x_posts_raw(place_country_code);

-- X Users (from includes.users expansion)
CREATE TABLE IF NOT EXISTS x_users_raw (
    user_id TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    name TEXT,
    location TEXT,  -- Self-declared, freeform
    description TEXT,
    created_at INTEGER,
    -- Public metrics
    followers_count INTEGER DEFAULT 0,
    following_count INTEGER DEFAULT 0,
    tweet_count INTEGER DEFAULT 0,
    listed_count INTEGER DEFAULT 0,
    -- Verification
    verified INTEGER DEFAULT 0,
    verified_type TEXT,  -- 'blue', 'business', 'government'
    -- Profile
    profile_image_url TEXT,
    protected INTEGER DEFAULT 0,
    -- Storage
    fetched_at INTEGER NOT NULL,
    raw_hash TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_x_users_username ON x_users_raw(username);
CREATE INDEX IF NOT EXISTS idx_x_users_created ON x_users_raw(created_at);

-- Update schema version
INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (2, strftime('%s', 'now'));
