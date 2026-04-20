# 041 — Cache Coverage + B1 Versioning Completion

## Context

Three small, independent cleanup items identified in the business-logic review and not subsumed by earlier walkthroughs:

1. **`/api/geo-sentiment` had no cache.** Every Global Heatmap page load recomputed the country aggregation live. The snapshot pipeline never wrote a `geo_sentiment_*` key.
2. **`/api/narratives` had a limit-keyed cache mismatch.** `save_snapshots` wrote `narratives_{window}_20` (hardcoded); the endpoint built the key as `narratives_{window}_{limit}`. Any request with `limit != 20` missed the cache and triggered live aggregation — including the UI default, which stayed at 20 only by coincidence.
3. **`prompt_versions.user_prompt_template`** existed as a column from migration 007 but was never populated. The audit had `save_ai_output` persisting only `system_prompt`, breaking the B1 reproducibility promise ("every inference is reconstructible from stored prompts").

The earlier planned "remove bot stub fields" item from 040's scope is moot — walkthrough 040 replaced the stubs with real computations instead.

## Changes

### Cache coverage

- `analysis/src/scheduler/job_runner.py::save_snapshots`:
  - Added a cache write for `geo_sentiment_{window}` for each of `24h / 7d / 30d / 90d`. `doc_count` uses the response's `total_posts`.
  - Changed narrative caching: the key drops the `_<limit>` suffix and stores the top-100 per window under `narratives_{window}`. The snapshot writes once per window, not once per (window, limit) pair.
  - `GeoAggregator` imported and instantiated on `AnalysisJobRunner.__init__`.

- `analysis/src/api/server.py`:
  - `get_geo_sentiment` now calls `_get_cached_or_fallback("geo_sentiment_{window}", ...)` instead of computing live.
  - `get_narratives` reads the single `narratives_{window}` key and slices the list to the caller's `limit`. Requests with `limit > 100` skip the cache and compute live (a rare operator path).

### B1 versioning

- `analysis/src/etl/loader.py::save_ai_output` now takes an optional `user_prompt_template` argument and upserts it alongside the system prompt. The SQL moved from `INSERT OR IGNORE` to `INSERT ... ON CONFLICT(prompt_version) DO UPDATE SET ... user_prompt_template = COALESCE(excluded.user_prompt_template, prompt_versions.user_prompt_template)` — so:
  - A first call with both prompts lands the row.
  - A later call for the same version that omits the user template does **not** clobber a previously-stored template (COALESCE preserves the existing value).
  - A later call that supplies a non-null user template updates it.

- `analysis/src/scheduler/job_runner.py` passes each task's user-prompt template constant on every `save_ai_output` call: `BOT_USER_PROMPT_TEMPLATE` for bot detection, `TEXT_ANALYSIS_USER_PROMPT_TEMPLATE` for sentiment + favorability, `CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE` for claims.

### Tests

- `analysis/tests/test_cache_and_versioning.py` — 3 new tests:
  - `test_user_prompt_template_persisted_alongside_system` — verifies first write persists both prompts.
  - `test_user_template_preserved_when_later_call_omits_it` — verifies COALESCE: omitting the user template on a re-save keeps the previously stored one.
  - `test_no_prompt_version_means_no_row` — regression: a `save_ai_output` without `prompt_version`/`system_prompt` writes zero `prompt_versions` rows.

## Verification

- 3/3 new tests pass.
- Affected-module bundle (cache_and_versioning + engines + bot_rework + inference_method + aggregation_confidence_filter + propagation + account_classifier + review + refresh_accounts + rich_aggregators) — 106/106 pass.
- UI typecheck clean.

## Operational notes

- The new cache keys (`narratives_{window}`, `geo_sentiment_{window}`) will appear on the next `analyze -Tasks snapshots` run. The old `narratives_{window}_20` snapshot files can be deleted from `data/cache/` but will simply be ignored — the cache staleness warning will fire once and stop after the new keys take over.
- Historical `prompt_versions` rows (pre-041) keep `user_prompt_template = NULL`. New writes populate the field; old rows are unaffected.

## Deploy

```powershell
.\run.ps1 migrate   # no-op if already at schema_version 14
.\run.ps1 analyze -Tasks snapshots
```

## Remaining roadmap

| # | Scope |
|---|---|
| 042 | Propaganda pipeline — backend (taxonomy, prompts, detector, loader + job_runner wiring; uses `author_bot_scores` as a narrative-overlay signal) |
| 043 | Propaganda pipeline — surfaces (aggregator, API, UI tab, review-task extension) |
| 044 | Calibration harness — reads `ai_output_evals WHERE is_golden=1`, produces accuracy curves per task (now includes `bot_detection` as a first-class calibrated task) |
