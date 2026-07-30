-- data/pg-migrations/0008_drop_favorability_stances.sql
--
-- Drops analysis.favorability_stances and its enum (Phase 7 post-cutover
-- decommission). The writer was removed 2026-07-25 (engine/text.py
-- sentiment-only rewrite; results/store.py has no save_favorability_stances
-- path) and every reader was repointed to analysis.target_mentions joined to
-- corpus.entities.lean. The drop was deliberately deferred past the full
-- recompute and the side-by-side acceptance pass because data loss is
-- irreversible; both are behind us (cutover 2026-07-26 + 48h stable).
--
-- Existing rows were Republican-only (the old prompt scoped favorability to
-- GOP entities) — do not resurrect them as a general-purpose stance source.
-- The rows remain readable in the pre-cutover pg_dump and the archived
-- SQLite cold artifact. See
-- docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md.
--
-- Not idempotent by itself -- same one-shot-apply convention as every other
-- file in this directory (the Go migration runner tracks the applied
-- version in ops.schema_migrations, so this file is never replayed against
-- an already-migrated database).

DROP TABLE analysis.favorability_stances;

DROP TYPE analysis.favorability_label;
