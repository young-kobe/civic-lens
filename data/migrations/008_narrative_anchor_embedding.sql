-- 008_narrative_anchor_embedding.sql
-- Cache the embedding vector for each narrative's anchor claim so that
-- embedding-mode clustering doesn't have to re-embed every existing
-- narrative on every run. Stored as JSON array of floats; NULL when the
-- narrative was created in jaccard mode (or the embedding call failed).

ALTER TABLE narratives ADD COLUMN anchor_embedding_json TEXT;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (8, strftime('%s', 'now'));
