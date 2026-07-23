-- data/pg-migrations/0003_admission_class.sql
--
-- Adds corpus.documents.admission_class (Postgres redesign Phase 8 prep,
-- 2026-07-23, owner-approved design). General discourse stays a 30-day
-- SAMPLE (existing rule, unchanged) -- but an X post authored by a tracked
-- ACTIVE OFFICIAL (corpus.entities kind='official', active=true, resolved
-- through corpus.author_profiles.entity_id) is PUBLIC RECORD: admitted by
-- analysis/src/etl/documents.py regardless of the 30-day window, labeled
-- 'official_record'. See analysis/src/etl/documents.py's X-section comment
-- and analysis/src/etl/constants.py's OFFICIAL_RECORD_PER_AUTHOR_CAP for
-- the ETL-side admission/cap logic.
--
-- Applies cleanly on top of 0001_north_star.sql + 0002_entity_registry_seed.sql:
-- ADD COLUMN ... NOT NULL DEFAULT 'sampled' backfills every pre-existing
-- corpus.documents row to 'sampled' in the same statement, no separate
-- UPDATE needed.
--
-- Not idempotent by itself -- CREATE TYPE has no IF NOT EXISTS in Postgres,
-- and the ADD COLUMN isn't guarded either. Same one-shot-apply convention as
-- every other file in this directory (see 0002's header): the Go migration
-- runner (ingest/internal/storage/db/db.go) tracks the applied version in
-- ops.schema_migrations, so this file is never replayed against an
-- already-migrated database.

CREATE TYPE corpus.admission_class AS ENUM ('sampled', 'official_record');
COMMENT ON TYPE corpus.admission_class IS 'sampled = ordinary 30-day-window discourse sample; official_record = X post by a tracked active official, admitted regardless of age (see corpus.documents.admission_class).';

ALTER TABLE corpus.documents ADD COLUMN admission_class corpus.admission_class NOT NULL DEFAULT 'sampled';
COMMENT ON COLUMN corpus.documents.admission_class IS 'sampled (default) = ordinary 30-day-window discourse sample; official_record = X post authored by a tracked active official (corpus.entities kind=''official'', active=true, joined via corpus.author_profiles.entity_id), admitted by analysis/src/etl/documents.py regardless of the 30-day recency filter, capped at OFFICIAL_RECORD_PER_AUTHOR_CAP per author (analysis/src/etl/constants.py). News/reddit and non-official X docs always get the default.';
