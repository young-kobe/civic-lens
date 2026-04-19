# 039 — Embedding-Mode Default + Aggregator Confidence Pre-Filter

## Context

Two Tier-1 items from the original audit plan (see the branch's business-logic review), both aimed at making the default pipeline produce more honest aggregates out of the box:

1. **Narrative clustering defaulted to lexical Jaccard.** That silently shattered synonyms — *"Trump won PA"* and *"Trump victory in Pennsylvania"* became two separate narratives even though both describe the same claim. Embedding mode was introduced in walkthrough 033 but shipped as opt-in. The Narratives tab is the product's main differentiator; a synonym-blind default made it look worse than it is.
2. **Aggregators did not pre-filter on `ai_outputs.confidence`.** A row where the LLM said "50/50 on this" moved the net-sentiment needle as much as a 0.9-confidence row. `STRONG_CONFIDENCE_THRESHOLD = 0.7` was used only to *label* strong-vs-mild samples, not to gate inclusion. Low-confidence noise diluted every aggregate.

## Changes

### Settings

- `analysis/src/common/settings.py`:
  - `narrative_similarity_mode` default: `"jaccard"` → `"embedding"`. Safe — the clusterer already falls back to Jaccard per-claim if the embedding call fails (Ollama not running, model not pulled), so environments without Ollama keep working.
  - New `aggregation_min_confidence: float = 0.5`. Floor for `ai_outputs` rows counted in aggregations, configurable via `CIVIC_AGGREGATION_MIN_CONFIDENCE`. Set to `0.0` to disable filtering entirely.

### Aggregators — confidence-gated reads

- `analysis/src/reporting/aggregators/base.py::get_bot_flagged_doc_ids` — now takes `min_confidence` (default `0.5`). A bot flag below threshold is ignored, so content is *not* excluded from sentiment/geo aggregates on a weak bot call. This matters: a 0.3-confidence "bot" guess was silently suppressing legitimate posts from the sentiment dashboard.
- `analysis/src/reporting/aggregators/sentiment.py::get_public_sentiment` — both the sentiment and favorability SELECTs now include `AND a.confidence >= ?`, bound to the settings-read threshold. Bot-flag lookup gets the same threshold.
- `analysis/src/reporting/aggregators/geo.py::get_country_sentiment` — the LEFT JOIN on `ai_outputs` gains `AND a.confidence >= ?` in the ON clause. The LEFT semantics are intentional: posts with low-confidence sentiment still count toward the country's `post_count` (geo coverage stays honest), they just don't contribute to the colored `avg_sentiment` value. Readers see the full footprint and the honest aggregate at the same time.
- `analysis/src/reporting/aggregators/narrative.py::_net_sentiment` — JOIN on `ai_outputs` gains `AND a.confidence >= ?`. A low-confidence half-guess no longer moves a narrative's headline sentiment %.

### Tests

- `analysis/tests/test_aggregation_confidence_filter.py` — 4 new tests:
  - `TestBotFlagConfidenceFilter`:
    - Low-confidence bot flags are *not* excluded; high-confidence bot flags are.
    - News docs are never bot-excluded regardless of confidence (unchanged invariant).
    - Setting `min_confidence=0.0` restores the pre-039 behavior (includes the low-confidence flag).
  - `TestNarrativeNetSentimentConfidenceFilter`: three supporting docs (two `0.9` POSITIVE + one `0.2` NEGATIVE) — net sentiment comes out `+90%`, not the `~+33%` the low-conf row would have produced if it counted.
  - `TestGeoSentimentConfidenceFilter`: two US posts (0.9 POSITIVE + 0.2 NEGATIVE) — `post_count = 2` (both counted for geo coverage), `avg_sentiment = 0.9` (only the confident one contributes to the colored aggregate).

## Verification

- 4 new confidence-filter tests pass.
- Full affected-test bundle (propagation + rich_aggregators + account_classifier + review + inference_method + refresh_accounts + aggregation_confidence_filter) — 80/80 pass.
- Pre-existing `test_llm_engines.test_deterministic_fallback_favorability` still fails for its unrelated reason (case-sensitivity on "Trump"); untouched by 039.

## Operational notes

- **Default threshold is 0.5.** Tune with `CIVIC_AGGREGATION_MIN_CONFIDENCE` in `.env` if the dashboards look too thin or too noisy. Drop to `0.0` to reproduce pre-039 behavior exactly.
- **Narratives tab will re-render differently** after the next `analyze` run. Narratives that were previously split by synonym drift may merge under the embedding clusterer, and narratives whose supporting docs were mostly low-confidence may see their `net_sentiment` numbers shift.
- **Cache keys are unchanged.** Existing snapshots will be rewritten on the next `analyze -Tasks snapshots` run. No migration needed.

## Deploy

```powershell
# Pull the embedding model once per host (skip if already present):
ollama pull nomic-embed-text
# Regenerate snapshots so the UI picks up the new defaults:
.\run.ps1 analyze -Tasks snapshots
```

If Ollama isn't installed on this host, the clusterer logs a fallback warning and keeps running — no need to force `CIVIC_NARRATIVE_SIMILARITY_MODE=jaccard` unless you prefer the determinism.

## Remaining roadmap

| # | Scope |
|---|---|
| 040 | Cache + versioning + stubs cleanup (remove bot stub fields, cache geo-sentiment, variable-limit narratives, complete B1 versioning) |
| 041 | Propaganda pipeline — backend (taxonomy, prompts, detector, loader + job_runner wiring) |
| 042 | Propaganda pipeline — surfaces (aggregator, API, UI tab, review-task extension) |
| 043 | Calibration harness (after golden set exists) |
