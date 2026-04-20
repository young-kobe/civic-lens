# 043 — Propaganda Surfaces (Aggregator, API, UI, Narrative Overlay)

## Context

Walkthrough 042 landed the per-doc propaganda detector; rows pile up in `ai_outputs` under `task_type='propaganda'` with validated evidence spans. 043 turns that into three surfaces:

1. A **Propaganda tab** in the UI with technique breakdown, News-vs-Social split, and flagged examples.
2. A **narrative overlay** — each `NarrativeSummary` now carries `propaganda_score` + `bot_pushed_fraction`. This is the long-promised correlation: a narrative with both numbers high means *"a claim that's heavily propaganda-textured AND pushed mostly by bot-looking X accounts."*
3. **Review task extension** so the existing queue (walkthrough 034) can collect `task_type='propaganda'` labels for the eventual calibration harness.

## Changes

### Backend — aggregator

- `analysis/src/reporting/aggregators/propaganda.py` (new) — `PropagandaAggregator.get_propaganda_overview(time_window)` returns:
  - `total_eligible_docs`, `flagged_docs`, `propaganda_rate_pct`, `mean_score` — headline numbers.
  - `by_technique` — per-technique counts + share of flagged docs (all six techniques reported; `0` when never flagged).
  - `by_source` — News vs Social Media with their own total/flagged/rate/mean.
  - `examples` — up to 10 most-recent flagged docs with title, preview, `overall_score`, and the validated per-technique evidence spans so UI readers can audit what the classifier is calling out.
  - `disclaimer` — same honest-labeling pattern as the sentiment tab.
  - Rows with `inference_method='deterministic'` (pre-exclusion markers) are filtered out of every denominator. A flagged doc requires `overall_propaganda_score >= 0.3` AND at least one validated technique.

- `analysis/src/reporting/aggregators/__init__.py` exports `PropagandaAggregator`.

### Backend — narrative overlay

- `analysis/src/reporting/aggregators/narrative.py` gains two private helpers:
  - `_propaganda_score(narrative_id, cutoff)` — mean `overall_propaganda_score` across supporting docs that have a propaganda row (excluding deterministic markers). Returns `None` when no supporting doc has been through propaganda detection.
  - `_bot_pushed_fraction(narrative_id, cutoff)` — joins `narrative_docs → docs (x_post) → x_posts_raw → author_bot_scores` to compute the fraction of unique X supporting authors whose `score >= 0.5` OR `bot_post_count > 0`. Returns `None` when no supporting doc is an X post with a resolved author.
- `NarrativeSummary` (`aggregator_models.py`) gains `propaganda_score: Optional[float]` and `bot_pushed_fraction: Optional[float]`, serialized through `to_dict()`.

### Backend — API + cache

- `analysis/src/api/server.py` imports `PropagandaAggregator`, instantiates it, adds `GET /api/propaganda?window=7d` using `_get_cached_or_fallback`.
- `analysis/src/scheduler/job_runner.py::save_snapshots` writes `propaganda_{24h,7d,30d,90d}` per-window after the existing geo caching block.

### Frontend — Propaganda tab

- `ui/src/types.ts` gains `PropagandaTechniqueName`, `PropagandaTechniqueSpan`, `PropagandaTechniqueCount`, `PropagandaSourceSplit`, `PropagandaExample`, `PropagandaOverview` interfaces. `NarrativeSummary` gains `propaganda_score` + `bot_pushed_fraction` (nullable).
- `ui/src/services/api.ts` adds `fetchPropaganda(window)`.
- `ui/src/pages/Propaganda.tsx` (new) renders: a plain-language disclaimer, three overview metric cards (flagged rate, mean score, window), a Technique Breakdown card with horizontal bars, a News-vs-Social split card, and a Recent Flagged Examples card. Each example renders its verbatim evidence span per technique so reviewers can audit the call.
- `ui/src/pages/index.ts` exports `Propaganda`.
- `ui/src/App.tsx`: `'propaganda'` tab inserted between `'narratives'` and `'bots'`; new case in `renderPage`.
- `ui/src/pages/Home.tsx`: a Propaganda TabCard describes the tab to first-time visitors ("An LLM scans each doc for six specific techniques... and must quote a verbatim phrase from the source as evidence").

### Frontend — Narrative overlay badges

`ui/src/pages/Narratives.tsx` — each narrative row now shows small badges under the source-mix bar when data is available:
- `prop 0.65` — mean propaganda score. Red styling when ≥ 0.4.
- `bot-pushed 40%` — fraction of X authors in the high-score bucket. Amber styling when ≥ 30%.

Narratives without overlay data (e.g. news-only narratives before propaganda has run) simply don't render the badges. No broken empty states.

### Frontend — Review task

- `ui/src/types.ts::ReviewTaskType` adds `'propaganda'`.
- `ui/src/pages/Review.tsx` adds Propaganda to the task selector and the `LABEL_OPTIONS_BY_TASK` (no label options — reviewers judge correctness overall, same as `'claims'`). `modelLabel()` formats propaganda model output as `"N techniques (score 0.42)"`.

### Tests

`analysis/tests/test_propaganda_surfaces.py` — 6 new tests:
- `TestPropagandaAggregator`:
  - `test_headline_rate_and_mean_score` — eligible count excludes pre-excluded rows; flagged docs match `FLAG_THRESHOLD` + techniques.
  - `test_technique_breakdown` — per-technique counts correct across 5 eligible docs.
  - `test_news_vs_social_split` — source buckets use `SOCIAL_PLATFORMS`/`NEWS_PLATFORMS` sets.
- `TestNarrativeOverlaySignals`:
  - `test_narrative_includes_propaganda_and_bot_signals` — with 2 X authors (one high `author_bot_scores.score`, one clean) + two propaganda rows, `propaganda_score = 0.5` and `bot_pushed_fraction = 0.5`.
- `TestNarrativeOverlayNullsWhenNoData`:
  - `test_both_overlay_fields_are_none` — news-only narrative with no propaganda rows + no X authors returns `None` for both signals.

## Verification

- 6 new tests pass; affected bundle (`test_propaganda_surfaces` + `test_propaganda` + `test_propagation` + `test_account_classifier` + `test_review` + `test_rich_aggregators`) — **64/64 pass**.
- UI typecheck clean.

## The "bot-pushed + propaganda" correlation is now a first-class signal

The narrative-overlay field names intentionally tell the story:
- `propaganda_score` is a mean, so it moves with how heavily the narrative's supporting docs use the six techniques.
- `bot_pushed_fraction` is the 040-rollup expressed at narrative scope — it answers "of the unique X accounts carrying this claim, how many look automated?"

A narrative with both high is the shape the user asked for in the bot-rework review: *"bot-driven posts that don't align with what real humans are saying."* The Narratives tab now surfaces this as two small badges without requiring a new visualization component.

## Deploy

```powershell
.\run.ps1 analyze -Tasks snapshots
# (If you haven't run propaganda yet:)
.\run.ps1 analyze -Tasks propaganda,snapshots
```

No migration. The cache will pick up `propaganda_{window}` keys on the next save_snapshots. Narrative cache already uses `narratives_{window}` (041); the aggregator automatically starts populating the new overlay fields.

## Remaining roadmap

| # | Scope |
|---|---|
| 044 | Calibration harness — reads `ai_output_evals WHERE is_golden=1`, produces accuracy curves for `sentiment`, `favorability`, `bot_detection`, `claims`, `propaganda`. Depends on human-reviewed golden rows, which you can start collecting now via the Review tab. |
