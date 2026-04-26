-- 018_x_posts_official_tier.sql
-- Tag posts that came in via the explicit verified-officials timeline path
-- so downstream stages can treat them as officials-tier without re-running
-- the LLM account classifier. Default 0 — all existing rows and any post
-- ingested via the topic-search queries stay untagged. The Go ingestor
-- writes 1 only when a post arrived via the user-timeline pull seeded from
-- data/verified_officials.yaml.
--
-- Tier classification for officials-on-X otherwise flows through
-- entity_registry.resolve_entity() (verified_officials.yaml lookup) and
-- account_profiles (curated load from known_political_x_accounts.yaml).
-- This column is the *provenance* signal: "we explicitly fetched this
-- because the author is on the editorial officials list," distinct from
-- "the author happens to be on a list."

BEGIN TRANSACTION;

ALTER TABLE x_posts_raw ADD COLUMN is_official_tier INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_x_posts_raw_official_tier
    ON x_posts_raw(is_official_tier) WHERE is_official_tier = 1;

COMMIT;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (18, strftime('%s', 'now'));
