# 2026-07-09 — Analysis-layer remediation (audit A-1..A-13)

Closes every confirmed finding from `2026-07-09-adversarial-review.md`. The
through-line is honesty of the numbers that reach `ai_outputs` and the
dashboard: out-of-range confidences no longer persist, transient LLM outages
no longer freeze as permanent "nothing found" verdicts, and the headline
propaganda / net-sentiment / mover numbers now match their documented
contracts. Regression tests for every Wave-1/2 item live in
`analysis/tests/test_analysis_remediation.py`.

## What shipped

### Wave 1 — stop corrupting ai_outputs
- **A-1** `llm/base.py::normalize_confidence` clamps every LLM confidence to
  `[0,1]`, dividing a leaked 0-100 percentage (`[2,100] -> /100`, logged) and
  clamping a slight overshoot (e.g. `1.5 -> 1.0`). Wired into the three
  unguarded read sites in `engine/analyzer.py` (sentiment / entity-stance /
  favorability) and `engine/bot.py` (confidence + `llm_text_likelihood`). The
  schema coercer stays unreachable by design (Gemini-facing schemas omit
  `minimum`/`maximum`); this is the reachable equivalent.
- **A-3a** `ClaimExtractionResult.extraction_failed` distinguishes a transport
  failure / unavailable client from a real empty result; `job_runner`
  `run_claim_extraction` skips failed docs (no row written) so they re-queue.
- **A-3b** `PropagandaResult.detection_failed` + the dataclass default
  `inference_method` is now `None` (a bare result is not an LLM verdict);
  `run_propaganda_detection` skips failed groups.
- **A-13** `loader.save_ai_output` no longer rewrites `prompt_versions.task_type`
  on conflict, so the sentiment/favorability shared prompt version stops
  flip-flopping the audit column twice per doc (first writer wins).

### Wave 2 — headline-number honesty
- **A-2** `aggregators/propaganda.py::_fetch_rows` and `narrative.py::_propaganda_score`
  now include `inference_method='deterministic'` pre-filter-clean rows in the
  denominator / per-narrative mean (they are real score-0 "no propaganda"
  verdicts) while they stay out of the flagged numerator. Stale docstrings on
  both sides rewritten.
- **A-6** `narrative.py::_net_sentiment` counts NEUTRAL/MIXED supporting docs in
  the denominator as 0, matching the docstring and the sentiment page (49
  NEUTRAL + 1 POSITIVE now reads ~+2, not +90).
- **A-5** `aggregators/movers.py` applies the `get_bot_flagged_doc_ids`
  exclusion and the `aggregation_min_confidence` floor in both `_entity_stats`
  and `_gop_favorability`, via the new shared `base.get_aggregation_min_confidence`
  helper (also satisfies backend-aggregator-audit item 3).
- **A-4** the GOP favorability mover reads `overall_gop_stance` (the key the
  favorability writer actually emits) instead of the never-emitted
  `target`/`label`, so `/api/movers.favorability_mover` is populated instead of
  permanently null. **Decision: fixed, not deleted** — the data exists, the
  reader was wrong.

### Wave 3 — C1 + ETL honesty
- **A-7** `sentiment.py::_build_sample_dict` synthesizes the X permalink
  (`https://x.com/{handle}/status/{id}`); `x_handle` is threaded through the
  topic / strength / entity sample collectors. Every X sentiment sample is now
  auditable (invariant C1).
- **A-8** ETL now **stamps** `published_at = ETL-time` for NULL/0-dated docs
  (`loader.stamp_published_at`) rather than rejecting them. **Decision:
  stamp, not reject** — dropping real content because a source omitted a date
  is its own honesty failure, and stamping keeps the (already paid-for)
  analysis visible in time windows. Documented on `is_recent` /
  `stamp_published_at`.
- **A-9** implemented ETL versioning: `loader.ETL_VERSION` is stamped onto every
  `docs` row via the new `etl_version` column (migration
  `020_docs_etl_version.sql`) and logged per run. **Decision: implement B1, not
  amend it** — INVARIANTS.md B1 Versioning is now checked with the mechanism.

### Wave 4 — robustness
- **A-10** `narrative_clusterer.run` commits once per doc (grouping its claims)
  so a mid-doc crash rolls back the whole doc and it re-clusters cleanly.
- **A-11** `narrative.py::_top_supporting_docs` dedupes by `doc_id` (the guard
  `propaganda.py` already documents) so duplicate `ai_outputs` rows can't
  render a supporting doc twice.
- **A-12** `ollama.py` default `max_retries=3` (was 1) to match Gemini;
  `gemini.py` no longer sleeps its backoff after the final failed attempt.

## Why

Point-in-time adversarial review found fabricated/mislabeled data reaching
`ai_outputs` (the table everything downstream trusts) plus biased headline
numbers on an honesty-first dashboard. See the companion findings entry
`2026-07-09-adversarial-review.md` for the traced failure paths.

## Verification

- `python -m unittest discover analysis/tests`: green except `test_api` (needs
  a live server on :8000 — environmental, pre-existing).
- Eval harness replay gate (`analysis.evals.run_eval --gate`): **GATE INACTIVE**
  (no baseline committed), 0 stale / 0 missing recordings — prompts, schema,
  and prompt version are untouched so the fingerprint is unchanged and no
  re-recording was needed.

## Follow-ups

- Backend-aggregator-audit items 2 and 4-8 remain open (broader dedup of the
  aggregator layer); A-5 closed only item 3.
