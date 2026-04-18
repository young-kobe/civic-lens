-- 005_drop_dead_tables.sql
-- Remove tables that were defined but are no longer populated or read:
--   author_profiles, engagement_metrics (migration 004) — no writers anywhere
--   reddit_comments_raw (migration 001) — no loader fetches or ingests comments
--   clusters, cluster_assignments (migration 001) — TF-IDF clustering was
--       removed in walkthrough 029; narrative-propagation uses dedicated
--       tables introduced in migration 007 instead.

DROP INDEX IF EXISTS idx_engagement_score;
DROP INDEX IF EXISTS idx_author_hierarchy;
DROP INDEX IF EXISTS idx_cluster_assign_cluster;

DROP TABLE IF EXISTS engagement_metrics;
DROP TABLE IF EXISTS author_profiles;
DROP TABLE IF EXISTS reddit_comments_raw;
DROP TABLE IF EXISTS cluster_assignments;
DROP TABLE IF EXISTS clusters;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (5, strftime('%s', 'now'));
