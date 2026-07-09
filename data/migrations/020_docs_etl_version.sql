-- 020_docs_etl_version.sql
-- Implements invariant B1 "Versioning": stamp the ETL logic version onto every
-- docs row so a later change to the political-keyword filter, the 30-day rule,
-- or the trafilatura extraction can be told apart per row (audit A-9).
-- Mirrors the prompt_version pattern used for ai_outputs.

ALTER TABLE docs ADD COLUMN etl_version TEXT;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (20, strftime('%s', 'now'));
