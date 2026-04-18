# 029 — Clustering Removal & LLM Pipeline Hardening

## Context

Audit of analysis, data, and UI layers (2026-04-16) surfaced three load-bearing issues:

1. TF-IDF clustering (backend + UI) was orphaned — the `StoryClusters` tab was commented out in `App.tsx`, but the clustering step still ran every pipeline and wrote to `clusters` / `cluster_assignments`.
2. The LLM reasoning pipeline could not credibly claim >95% accuracy: evidence spans returned by the model were stored verbatim with no validation, schema responses were not validated post-parse, and the exact prompt text used per inference was not recoverable.
3. Dead UI code (`GOPFavorability.tsx`, `fetchFavorability`, `fetchBotProfiles`) pointed at a missing `/api/favorability` endpoint and failed silently.

This walkthrough removes the clustering feature end-to-end and closes three LLM hardening gaps (#1, #3, #5 from the audit).

## Changes

### Clustering removal

- `ui/src/App.tsx` — removed `StoryClusters` import and routing; default tab now renders `PublicSentiment` instead of the disabled clusters page.
- `ui/src/pages/StoryClusters.tsx` — deleted (358 LoC).
- `ui/src/pages/GOPFavorability.tsx` — deleted (271 LoC, was not routed and called a nonexistent endpoint).
- `ui/src/pages/index.ts` — removed `StoryClusters` export.
- `ui/src/services/api.ts` — removed `fetchStories`, `fetchFavorability`, `fetchBotProfiles`, `ContentTypeFilter`; removed `avg_favorability` from `CountryStats`.
- `ui/src/services/transformers.ts` — removed `transformStories`, `transformFavorability`.
- `ui/src/types.ts` — removed `Cluster`, `ContentType`, `SourceMixItem`, `TimelinePoint`, `Article`, `FavorabilityData`.
- `ui/src/components/charts/StackedBar.tsx` — deleted (dead after `StoryClusters` removal); removed from `charts/index.ts`.
- `ui/src/pages/GlobalHeatmap.tsx` — removed the `GOP Favorability` row from the country tooltip (meaningless for non-US countries).
- `analysis/src/engine/clustering.py` — deleted.
- `analysis/src/reporting/aggregators/story.py` — deleted.
- `analysis/src/reporting/aggregators/__init__.py` — removed the legacy `Aggregator` wrapper class and `StoryAggregator` export; callers switched to direct domain aggregators.
- `analysis/src/reporting/aggregators/constants.py` — removed story-only constants (`ARTICLE_SOURCE_TYPES`, `SOURCE_DISPLAY_NAMES`, `SOURCE_TYPE_MAP`).
- `analysis/src/reporting/models/aggregator_models.py` + `__init__.py` — removed `MomentumData`, `SourceMixItem`, `TimelinePoint`, `ArticlePreview`, `StoryCluster`.
- `analysis/src/etl/loader.py` — removed `get_all_docs_for_clustering` and `save_clusters`.
- `analysis/src/api/server.py` — rewritten to use domain aggregators directly; dropped `/api/stories` and `/api/run/clustering` endpoints; dropped `ContentClusterer` init.
- `analysis/src/scheduler/job_runner.py` — rewritten to use domain aggregators directly; removed `run_clustering` step, the `"clusters"` summary key, and the `"clustering"` task option from argparse help.
- `analysis/src/common/settings.py` — removed `model_sentiment` (dead, never read) and `clustering_threshold`.
- `analysis/tests/test_engines.py` — removed `test_clustering` and `ContentClusterer` import.
- `analysis/tests/test_rich_aggregators.py` — switched from `Aggregator` wrapper to direct domain aggregators; dropped `test_get_stories_rich` and the clusters schema/fixture.
- `data/cache/stories_*.json` — deleted (12 stale snapshot files).

### LLM hardening (audit gaps 1, 3, 5)

- `analysis/src/engine/analyzer.py` — added `_validate_evidence_spans(spans, source_text)`. Drops any span shorter than 4 words or not a case-insensitive substring of the source text. Applied to `sentiment_evidence_spans`, each `entity_stances[].evidence_spans`, and the overall favorability confidence. If the model returned evidence but none verified, confidence is capped at `UNVERIFIED_EVIDENCE_CONFIDENCE_CAP = 0.3`. This enforces invariant B2 (traceability) — fabricated or paraphrased evidence no longer passes through to the DB.
- `analysis/src/llm/base.py` — introduced `SchemaValidationError` and a minimal JSON-schema validator (`_validate_against_schema`) that checks required fields, types, enums, and numeric ranges. `parse_json_response` now takes an optional `schema` argument and validates after parsing. A malformed response raises an error, triggering the existing heuristic fallback rather than silently defaulting via `.get(..., 0.5)`.
- `analysis/src/llm/gemini.py`, `analysis/src/llm/ollama.py` — pass `schema=response_schema` through to `parse_json_response`.
- `analysis/src/etl/loader.py` — `save_ai_output` accepts an optional `system_prompt` argument and embeds it under `output_json._system_prompt`. Every inference is now self-describing — you can reconstruct what prompt text produced a given row without consulting git history of `prompts.py`.
- `analysis/src/scheduler/job_runner.py` — passes the relevant `*_SYSTEM_PROMPT` constant to `save_ai_output` for both bot-detection and sentiment/favorability rows.

## Verification

- `cd ui && npm run typecheck` — clean.
- `cd ui && npm run build` — 1159 modules, 3.85s, no errors.
- `analysis/tests/test_engines` — 6/6 pass.
- `analysis/tests/test_rich_aggregators` — 4/4 pass (after removing the clusters test).
- Import smoke test on every rewritten module passes.

## What this does NOT yet close

Audit gaps #2 (confidence calibration) and #4 (golden-set regression harness) remain open. Closing them requires a hand-labeled golden set (~100–500 docs) and a calibration study; that is the next step before any `>95% accuracy` claim in UI copy or documentation.
