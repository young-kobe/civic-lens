# 2026-07-08 — Golden-set eval harness for claim extraction

The analysis layer now has a labeled evaluation pipeline for its highest-leverage LLM stage. `analysis/evals/` holds a 50-example golden set for claim extraction, a deterministic scorer (span-anchored micro P/R/F1), and a runner that scores the live `ClaimExtractor` code path in two modes: replay (recorded model responses, no keys, what CI runs) and live (real backend, opt-in, refreshes recordings). Full operator documentation lives in `docs/EVALS.md`; the CI gate is recorded in the infra entry of the same date (`../infra/2026-07-08-eval-regression-gate-ci.md`).

## What shipped

- `analysis/evals/harness.py` — `ReplayLLMClient` (replays a recorded response through `BaseLLMClient.parse_json_response`, so live schema validation runs during replay), `RecordingLLMClient` (wraps the real backend, captures parsed responses), and `claim_extraction_fingerprint()` (SHA-256 of system prompt + user template + schema + prompt version).
- `analysis/evals/scoring.py` — claims matched by char-level IoU (>= 0.3) of their verbatim evidence spans in the source text; greedy one-to-one matching; micro-averaged precision/recall/F1. No LLM judge anywhere in scoring.
- `analysis/evals/run_eval.py` — CLI (`python -m analysis.evals.run_eval`) with `--mode replay|live`, `--record`, `--include-pending`, `--gate`, `--write-baseline`, `--report-json`, per-example FP/FN diffs printed as claim text.
- `analysis/evals/golden/claims/claims-001..050.json` — 50 draft examples (multi-claim ledes, single-claim posts, opinion-wrapped claims, 10 no-claim negatives, edge cases: sarcasm, negation, attribution, whataboutism, numeric claims). All are `input_provenance: synthetic-draft` and `review_status: PENDING_HUMAN_REVIEW` — none count as ground truth until hand-verified.
- `analysis/evals/baseline_claims.json` — committed gate floor; `baseline: null` until labels are verified and live outputs recorded.
- `analysis/src/engine/claim_extractor.py` — `ClaimExtractor.__init__` accepts an optional `llm_client` so the harness (and tests) can inject replay/recording clients; production callers are unchanged.
- `analysis/tests/test_eval_runner.py` — 15 tests covering span matching, replay determinism, pipeline validation running during replay, fingerprint staleness, gate states, and golden-set integrity (fabricated spans fail the load).

## Why

- Publish-readiness review (2026-07) called for public evidence of LLM verification discipline: the repo asserted invariants (B2 evidence spans, confidence everywhere) but had no way to show a prompt/model change did not regress output quality before deploy.
- Claim extraction was picked first because narratives — the flagship overlay — are built from claims, the stage has no heuristic fallback (quality is entirely prompt/model-determined), and span anchoring makes scoring deterministic rather than judge-dependent.
- The walkthrough-035 plan deferred a calibration harness until the review queue accumulated golden rows (`ai_output_evals WHERE is_golden=1`); this entry lands the harness now with file-based golden examples instead, since the review queue's labeled volume never materialized. The DB-backed golden rows remain usable as a future example source (`docs/EVALS.md` bootstrapping section).

## Follow-ups

- Hand-verify the 50 draft labels (flip to `VERIFIED`), record live outputs, write the first baseline — until then the CI gate is inactive by design.
- Replace/augment synthetic drafts with real pipeline inputs pulled from the production DB.
- Extend the harness to sentiment (label agreement rate) and propaganda (technique-level P/R) once the claims gate is active.
