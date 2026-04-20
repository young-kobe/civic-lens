# 038 — `inference_method` Column + Analyzer Cleanup + Frontier CHECK Constraint

## Context

Three independent, small items bundled together because they share the same theme — tightening the audit trail (invariant B2) around paths the code was quietly papering over:

1. **`ai_outputs.inference_method`** was missing. A heuristic-fallback row (emitted when the LLM is unavailable, throws, or fails schema validation) was indistinguishable in the DB from a confident validated LLM classification. Any query like "what fraction of sentiment rows came from a real LLM call?" was unanswerable.
2. **Dead heuristic-signal injection** in `analyzer.py`: the analyzer was computing ~10 fields of deterministic signals and passing them as kwargs to `TEXT_ANALYSIS_USER_PROMPT_TEMPLATE.format(...)` — but the template's only placeholder was `{text}`. Python silently discards extra kwargs, so every LLM classification was running dead-code computation.
3. **`pages.state` had no CHECK constraint.** The Go frontier treated `0/1/2/3` (QUEUED/INFLIGHT/DONE/FAILED) by code convention; the database accepted any integer. A stray `UPDATE pages SET state=99` would succeed.

## Changes

### Schema

- `data/migrations/012_ai_outputs_inference_method.sql` — adds `inference_method TEXT` to `ai_outputs` with `CHECK(inference_method IS NULL OR inference_method IN ('llm','heuristic','deterministic'))`. Indexed on the new column. Historical rows land NULL (pre-migration; meaning "unknown").
- `data/migrations/013_pages_state_check.sql` — adds `CHECK(state IN (0,1,2,3))` to `pages`. SQLite does not support `ALTER TABLE ADD CONSTRAINT`, so this uses the canonical 12-step procedure: `PRAGMA foreign_keys=OFF` → `BEGIN` → create `pages_new` with the constraint → `INSERT INTO pages_new SELECT * FROM pages` → `DROP pages` → `ALTER TABLE pages_new RENAME TO pages` → recreate indexes → `COMMIT` → `PRAGMA foreign_keys=ON`. Handles the `articles_raw.url_canon → pages.url_canon` foreign key correctly.

### Engine models

- `analysis/src/engine/models/engine_models.py` — `SentimentResult`, `FavorabilityResult`, and `BotResult` each gain `inference_method: str = "llm"`. Default is `"llm"` because that's the most common path; heuristic callers flip it explicitly.

### Analyzers

- `analysis/src/engine/analyzer.py`:
  - **LLM path** sets `inference_method="llm"` on both `SentimentResult` and `FavorabilityResult`.
  - **Heuristic fallback path** (`_heuristic_classify`) sets `inference_method="heuristic"` on both.
  - **Dead-kwargs cleanup:** the `.format(...)` call is now `TEXT_ANALYSIS_USER_PROMPT_TEMPLATE.format(text=text[:2000])`. The ten signal kwargs that `.format()` was discarding are gone. `_compute_signals()` still runs because its output feeds `deterministic_signals` on the result dataclasses (useful for auditing) and drives the heuristic fallback.
- `analysis/src/engine/bot.py`:
  - `_llm_classify` → `inference_method="llm"`.
  - `_heuristic_classify` → `inference_method="heuristic"`.
  - Empty-text path → `inference_method="heuristic"` (treated as a deterministic default).

### Loader + engines that write to ai_outputs

- `analysis/src/etl/loader.py::save_ai_output` gains an `inference_method: Optional[str] = None` parameter. When None is passed, the column lands NULL — caller hasn't attested, historical rows look the same. The INSERT now includes the column in both the column list and values tuple.
- `analysis/src/engine/citation_extractor.py` — the per-doc marker insert hard-codes `inference_method='deterministic'` (no LLM path exists).
- `analysis/src/scheduler/job_runner.py` — passes `inference_method=result.inference_method` through on bot, sentiment, and favorability saves; hard-codes `"llm"` on the claims save (claim extraction only runs when LLM is available, so there's no heuristic path to mark differently).

### Tests

- `analysis/tests/test_inference_method.py` — 11 new tests:
  - Analyzer heuristic fallback marks sentiment + favorability as `"heuristic"`.
  - Analyzer empty-text output still carries a method.
  - Bot detector heuristic fallback marks `"heuristic"`.
  - Bot detector empty-text output marks method.
  - `save_ai_output` writes `"llm"` when passed.
  - `save_ai_output` writes `"heuristic"` when passed.
  - `save_ai_output` writes NULL when method not passed (back-compat).
  - `save_ai_output`'s CHECK constraint rejects invalid method strings.
  - `pages.state` CHECK accepts 0/1/2/3.
  - `pages.state` CHECK rejects 99.
  - `pages.state` CHECK rejects negative.

## Verification

- Migrations 012 + 013 applied cleanly against the live dev DB.
- New tests: 11/11 pass.
- Full affected-module run (test_inference_method + test_account_classifier + test_propagation + test_rich_aggregators + test_review + test_refresh_accounts + test_engines + test_llm_engines): 85/86 pass. The one remaining failure (`test_deterministic_fallback_favorability`) is pre-existing and unrelated — the heuristic lowercases GOP entities while the test expects the capitalized form. Worth fixing eventually but out of scope here.
- Full Python suite (86 tests) runs to completion without the migration changes breaking anything.

## Operational notes

- Historical `ai_outputs` rows keep `inference_method=NULL`. New writes populate it. Queries that want to measure LLM vs fallback share should filter `WHERE inference_method IS NOT NULL` or bucket NULLs as "unknown (pre-038)".
- After 013, any code that tries to set `pages.state` outside `{0,1,2,3}` will now hit `sqlite3.IntegrityError`. If the Go runner has a bug that sets a bad state, this will surface it loudly — by design.

## Deploy

```powershell
.\run.ps1 migrate
# No code changes to run.ps1 or the UI. Next `analyze` run will populate
# inference_method on every new ai_outputs row.
```

## Remaining roadmap

Unchanged from 037:

| # | Scope |
|---|---|
| 039 | Embedding-mode narrative clustering default + aggregator confidence pre-filtering |
| 040 | Cache + versioning + stubs cleanup (remove bot stub fields, cache geo-sentiment, variable-limit narratives, complete B1 versioning) |
| 041 | Propaganda pipeline — backend (taxonomy, prompts, detector, loader + job_runner wiring) |
| 042 | Propaganda pipeline — surfaces (aggregator, API, UI tab, review-task extension) |
| 043 | Calibration harness (after golden set exists) |
