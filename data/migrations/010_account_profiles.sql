-- 010_account_profiles.sql
-- Author-tier classification for the narrative overlay (walkthrough 036).
--
-- Each row is a per-platform author classified into one of three tiers:
--   elected_official      — current/former elected officials and institutional
--                           accounts of elected bodies (e.g. @potus, @whitehouse,
--                           @senategop). Source: curated YAML at
--                           data/known_accounts.yaml (classification_method='curated_list').
--   affiliated            — politically affiliated figures: journalists covering
--                           politics, pundits, party strategists, PACs, and
--                           think-tank accounts. Source: curated YAML for major
--                           orgs; LLM classifier for individuals
--                           (classification_method='llm').
--   general_public        — everyone else. NOT stored as a row — absence from
--                           this table means "default to general_public".
--
-- Authors classified as the default tier are not persisted; only affirmative
-- classifications land here. This keeps the table small (curated + classified
-- individuals only) while letting the aggregator query be a simple LEFT JOIN.

CREATE TABLE IF NOT EXISTS account_profiles (
    profile_id INTEGER PRIMARY KEY AUTOINCREMENT,
    platform TEXT NOT NULL CHECK(platform IN ('x', 'reddit')),
    author_id TEXT NOT NULL,
    display_name TEXT,
    tier TEXT NOT NULL CHECK(tier IN ('elected_official', 'affiliated', 'general_public')),
    classification_method TEXT NOT NULL CHECK(classification_method IN ('curated_list', 'llm')),
    classified_at INTEGER NOT NULL,
    confidence REAL,           -- NULL for curated_list; 0..1 for llm
    reasoning TEXT,            -- optional; populated by llm path
    notes TEXT,                -- optional; populated by curated list
    UNIQUE(platform, author_id)
);

CREATE INDEX IF NOT EXISTS idx_account_profiles_tier ON account_profiles(tier);
CREATE INDEX IF NOT EXISTS idx_account_profiles_platform_author ON account_profiles(platform, author_id);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (10, strftime('%s', 'now'));
