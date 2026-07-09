# Analysis review remediation

Fixes for the confirmed findings in `docs/audit-trail/analysis/2026-07-09-adversarial-review.md` (IDs referenced below). Wave 1 stops bad data entering `ai_outputs` — everything downstream trusts that table. Waves are PR-sized, each with its own audit-trail entry.

## Wave 1 — stop corrupting ai_outputs

- [ ] **A-1: clamp confidences at the three unguarded read sites.** `analyzer.py:292,320,333` and `bot.py:369` — apply the same `max(0.0, min(1.0, x))` used by claims/propaganda, with the 0-100 heuristic first (value in (1,100] -> /100, log warning) since the coercion helper in `base.py` is unreachable with stripped schemas. Alternative if preferred: validate against an internal bounded schema after parse (keep the Gemini-facing schema stripped). Regression tests: response with `"confidence": 85` -> stored 0.85; `"confidence": 1.5` -> 1.0.
- [ ] **A-3a: claims — distinguish transport failure from "no claims".** `claim_extractor.py:113-115` must not return a normal empty result on exception; raise or return a failure sentinel, and `job_runner.py:482-492` must skip the doc (no `ai_outputs` row) so it re-queues next run. Test: extractor raises -> doc still returned by `get_unprocessed_docs`.
- [ ] **A-3b: propaganda — same fix.** `propaganda_detector.py:153-155` returns bare `PropagandaResult()` on exception with `inference_method` defaulting to `"llm"` (`engine_models.py:111`), contradicting its own docstring. Skip-on-failure like A-3a; make the dataclass default not claim "llm".
- [ ] **A-13: stop prompt_versions.task_type flip-flopping.** `loader.py:416-433` — either stop rewriting `task_type` on conflict or key the table by (prompt_version, task_type).

## Wave 2 — headline-number honesty

- [ ] **A-2: fix the propaganda denominator.** `aggregators/propaganda.py:199-201` — include `inference_method='deterministic'` clean verdicts in `total_eligible_docs` (they are real "no propaganda" results from the walkthrough-048 pre-filter); keep them out of the flagged numerator. Same fix in `narrative.py:274` per-narrative means. Update the stale docstrings on BOTH sides (propaganda.py:12-15, detector docstring). Test with a fixture: 10 docs, 6 deterministic-clean, 1 flagged -> rate 10%, not 25%.
- [ ] **A-6: fix narrative net-sentiment to match its contract.** `narrative.py:464-483` — count NEUTRAL/MIXED rows in the denominator as 0 (per the docstring), or change the docstring and the UI copy to say "average over polarized posts" — pick one; the sentiment page's formula is the precedent. Fixture test with 49 NEUTRAL + 1 POSITIVE.
- [ ] **A-5: apply bot + confidence filters in movers.** `movers.py:83-98` — add `get_bot_flagged_doc_ids` exclusion and the `aggregation_min_confidence` floor to match `sentiment.py:99-118`. Overlaps `backend-aggregator-audit.md` item 3 (single confidence-filter helper) — implementing that helper here satisfies both.
- [ ] **A-4: fix or remove the dead favorability mover.** `movers.py:199-207` reads payload keys (`target`, `label`) that `FavorabilityResult.to_dict()` never emits. Either read `overall_gop_stance`/`overall_confidence` correctly or delete the mover and its `/api/movers` field — do not leave a permanently-null API field.

## Wave 3 — C1 + ETL honesty

- [ ] **A-7: synthesize X permalinks in sentiment samples.** `sentiment.py:314-320` — thread `x_handle` into `_build_sample_dict` and emit `https://x.com/{handle}/status/{id}` like narrative.py:78-79 does. Every classification sample on the sentiment page must have a working source link (invariant C1). Test: x_post sample dict has non-null url.
- [ ] **A-8: decide NULL published_at policy.** Either stamp `published_at = fetched-at-now` at ETL time (docs then appear in windows) or reject the doc at ETL (no LLM spend on invisible docs). Do not keep paying to analyze docs no window can ever show. Document the choice in the loader.
- [ ] **A-9: implement ETL versioning or amend B1.** Add an `etl_version` constant stamped onto docs rows (mirror the prompt_version pattern), or edit INVARIANTS.md B1 to drop the promise. Conscious decision in the PR description.

## Wave 4 — robustness

- [ ] **A-10: per-doc transaction in narrative clustering.** `narrative_clusterer.py:275-285` — commit once per doc (all claims), not per claim, so a crash cannot leave a half-clustered doc that is excluded from re-processing.
- [ ] **A-11: dedupe supporting-docs drill-down.** `narrative.py:497-517` — copy the dedupe-by-doc_id pattern propaganda.py:344-347 already documents.
- [ ] **A-12: align backend retry policy.** `ollama.py:33` -> default `max_retries=3` to match Gemini (or set both from one settings value); remove Gemini's pointless post-final-attempt backoff sleep (`gemini.py:145`).

## Exit criteria

Full suite green plus the new regression tests above (each Wave-1/2 item ships with one); the eval harness replay gate stays green (A-1/A-3 touch the claim path the harness exercises — recordings should NOT need re-recording since prompts are untouched; if the fingerprint trips, something changed that shouldn't have). Each wave's audit-trail entry names the A-N ids it closes. When every box is ticked, delete this file.
