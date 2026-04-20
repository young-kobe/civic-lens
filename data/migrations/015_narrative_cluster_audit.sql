-- 015_narrative_cluster_audit.sql
-- Record the clustering semantics each narrative was created under so a
-- later threshold/model change doesn't silently re-interpret old narratives.
-- Audit 2026-04-19 §8: "Clusterer writes no audit metadata." After a
-- threshold change (0.65 → 0.70) there is currently no way to tell whether
-- a narrative was anchored under the old or new rule.

ALTER TABLE narratives ADD COLUMN clustering_mode TEXT;
ALTER TABLE narratives ADD COLUMN clustering_threshold REAL;
ALTER TABLE narratives ADD COLUMN embedding_model TEXT;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (15, strftime('%s', 'now'));
