-- 021_docs_schema_cleanup.sql
-- Data-layer remediation (audit docs/audit-trail/ingestion/2026-07-09-adversarial-review-data-layer.md).
--
-- D-12: backfill migration 004's missing schema_version row. 004 was fully
-- IF NOT EXISTS and 005 dropped its tables, so no data moves — but the version
-- table lied ([1,2,3,5,...]). Guarded so already-migrated DBs are untouched.
--
-- D-10: drop docs.place_country_code + idx_docs_country. The column was
-- write-only: the loader wrote it, but the only country reader (bot.py) reads
-- place_country_code out of metadata_json, and no aggregator ever queried the
-- column, so the index was dead.
--
-- D-13: drop docs.fetched_at. All three ETL INSERT paths omit it and no reader
-- exists — it was NULL on every row.
--
-- SQLite 3.35+ ALTER TABLE ... DROP COLUMN is used directly (project sqlite is
-- 3.37); the index must be dropped first because DROP COLUMN refuses a column
-- referenced by an index. The Go migration runner wraps this whole file in one
-- transaction, so no explicit BEGIN/COMMIT here.

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (4, strftime('%s', 'now'));

DROP INDEX IF EXISTS idx_docs_country;
ALTER TABLE docs DROP COLUMN place_country_code;
ALTER TABLE docs DROP COLUMN fetched_at;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (21, strftime('%s', 'now'));
