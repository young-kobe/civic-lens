# Entity-profile fields, rich sample fields, and collective aliases restored (foundations)

**Date:** 2026-07-27
**Layer:** api
**Todo:** docs/todos/ui-feature-restoration.md

Foundations landed for restoring the pre-Postgres UI's `EntityProfile` and
`ClassificationSample` payloads. Not wired into `server.py` routes yet --
this increment is the migration, models, and query-layer building blocks a
later wave consumes.

## What shipped

- `data/pg-migrations/0007_restore_curated_fields.sql`: adds
  `corpus.entities.founded` (SMALLINT), `.circulation_note` (TEXT),
  `.subscriber_count_proxy` (TEXT), `.account_type` (TEXT) -- all nullable,
  kind-scoped, curated. One-time backfill from the frozen YAML registries
  at git tag `pre-cutover-main`: 21 outlets' founded/circulation_note (all
  of `data/news_outlets.yaml`), 15 subreddits' subscriber_count_proxy (all
  of `data/major_subreddits.yaml`), and 31 accounts' account_type. Curation
  continues DB-native afterward, same convention as `role_title`/`owner`/
  `source_citation`.
  - **Discrepancy found and resolved by omission, not fabrication:**
    `data/known_political_x_accounts.yaml` does not have a flat
    handle -> account_type mapping. Only the 12 executive_branch entries
    nest a per-handle `accounts: [{handle, account_type}, ...]` list (31
    handles). The 437 House members, 100 Senate members, and 15
    affiliated-collective entries carry a bare `handle` with no
    account_type field at all. The migration backfills only the 31 handles
    the source data actually classifies.
- `api/models/common.py`: `EntityProfileModel` (the PG-column ->
  old-UI-field mapping, including the `source_citation` -> `lean_source`/
  `bio_source` split by kind) and `ClassificationSampleModel` +
  `SampleEngagementModel`/`SampleAuthorModel`/`SampleTargetModel`.
- `api/queries/profiles.py`: `fetch_entity_profiles()` (batched
  `entity_id = ANY(...)`), the pure `_map_entity_row()`/`_entity_ui_kind()`
  vocabulary mapping (editorial official -> `official`, non-editorial
  official -> `account`, collective -> `official`, outlet/subreddit
  unchanged), `sampled_account_profile()`, and `catch_all_profile()` for
  the three sentinel buckets (`other-outlets`/`other-x-users`/
  `other-subreddits`), text ported verbatim from the retired
  `reporting/entity_registry.py`.
- `api/queries/constants.py`: `GOP_TARGET_ALIASES`/`DEM_TARGET_ALIASES`
  frozensets, ported verbatim from `_GOP_TARGET_ALIASES`/
  `_DEM_TARGET_ALIASES`, for the gop_collective/dem_collective
  received-tone rollups (not yet wired to a query).
- `api/queries/base.py`: `fetch_rich_sample_fields()` (six batched queries
  over corpus.documents/authors/x_posts/reddit_posts and
  analysis.runs/sentiment_results/target_mentions/narrative_docs/
  narratives -- never a per-doc-id loop) and `build_classification_sample()`.

## Schema discrepancies vs. the task spec

- `corpus.documents` has no `full_text` column -- the actual column is
  `body`. `fetch_rich_sample_fields()` reads `d.body` and maps it into
  `ClassificationSampleModel.full_text` (the old UI's field name), not a
  renamed column.
- `analysis.sentiment_results` carries no `confidence` column of its own;
  confidence lives on `analysis.runs.confidence` (one run can feed
  multiple result tables). `fetch_rich_sample_fields()` reads it from
  `analysis.runs` via the join, per the results-store traceability
  contract.
- The known_political_x_accounts.yaml structure discrepancy above.

## Why

Phase 10's contract rewrite dropped these fields because the Phase 9 API
had no place to carry them (`docs/audit-trail/ui/2026-07-24-phase10-ui-adaptation.md`),
even though the underlying data was never deleted -- `corpus.entities`
just stopped having the columns (they were never migrated from the YAML
registries in the first place), and `corpus.documents`/`analysis.*`
already carry everything `ClassificationSample` needs.

## Verification

`_map_entity_row`/`_entity_ui_kind`/`sampled_account_profile`/
`catch_all_profile` covered by pure unit tests (no DB) in
`analysis/tests/test_api_queries_profiles.py`, including the
`source_citation` -> `lean_source` vs `bio_source` name-trap case. One
PG-gated integration test applies migrations through 0007 and round-trips
a known outlet's backfilled `founded`/`circulation_note` through
`fetch_entity_profiles()`. Full suite: 791 passed, 0 failed (264 skipped
without `CIVIC_TEST_DATABASE_URL`; 0 skipped, all passing against a
throwaway `postgres:17-alpine` with migrations 0001-0007 applied).

## Follow-ups

- Wire `fetch_entity_profiles()`/`fetch_rich_sample_fields()` into
  `server.py` routes and the entity-profile/entity-posts/docs response
  models (separate wave -- out of scope here).
- `GOP_TARGET_ALIASES`/`DEM_TARGET_ALIASES` are ported but not yet
  consumed by any query; the gop_collective/dem_collective received-tone
  rollup itself is a follow-up.
- account_type coverage is 31 of 583 known-account handles (see
  discrepancy above); the remaining 552 have no source classification to
  restore.
