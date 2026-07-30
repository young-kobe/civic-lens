# Eval expansion: calibration, metamorphic tests, disagreement sampling

Follow-on evals for the analysis layer beyond the claims golden set
(`docs/EVALS.md`). Context feeding groundwork (sentence-boundary
truncation, triviality pre-filter, reasoning bounds) landed in
`docs/audit-trail/analysis/2026-07-09-llm-context-optimizations.md`.

## Calibration report

- [ ] Join `analysis.runs.confidence` with `analysis.evals.verdict` per
      task into confidence buckets (0.5-0.6, ..., 0.9-1.0) with
      observed accuracy per bucket (`analysis/src/review/service.py` already
      owns the `analysis.evals` writes to build this off of).
- [ ] Expose alongside `GET /eval-accuracy` (`analysis/src/api/routers/status.py`,
      backed by `review_service.get_public_accuracy()`) so the Review tab
      can render a reliability table.
- [ ] Document the caveat: buckets with < 20 reviewed rows are
      directional, not conclusive.

## Metamorphic (perturbation) test suite

- [ ] Define invariance rules: neutral-boilerplate append keeps labels;
      entity swap moves favorability target; negation flips sentiment;
      paragraph reorder preserves claim set.
- [ ] Runner samples pipeline docs, applies perturbations, diffs outputs.
      Live-mode only (costs tokens); report-only, no gate initially.

## Cross-backend disagreement sampling

- [ ] Periodic job: run a doc sample through both Gemini and Ollama,
      record per-task disagreement rate over time.
- [ ] Route disagreeing docs into the review queue ahead of
      lowest-confidence ordering.

## Propaganda golden set

Moved to `docs/todos/propaganda-classifier-v2.md` (2026-07-30) — grown into
its own initiative: Kobe-verified golden set, calibration report, then a
propaganda-v2 prompt gated on the numbers. Two details preserved there in
spirit; keep them in mind during Phase 1 sampling: include negatives the
`_has_loaded_language` pre-filter kills, and one example with a technique
near the 800-char clamp.

## Flash-Lite switch (gated on golden-set eval)

- [ ] Hand-verify golden-set labels and commit the
      `analysis/evals/baseline_claims.json` baseline (gate is warn-and-pass
      until this lands).
- [ ] Re-record golden-set recordings against `gemini-2.5-flash-lite`; run
      `python -m analysis.evals.run_eval --gate` (0.02 F1 tolerance).
- [ ] On pass: set `CIVIC_GEMINI_MODEL=gemini-2.5-flash-lite` in
      /etc/civic-lens.env. On fail: stay on the current model and record the
      result in an audit-trail entry either way.

## Few-shot example pool

- [ ] Once review-queue corrections accumulate: curate a prompt-example
      pool from corrected failures, disjoint from `analysis/evals/golden/`
      (eval-contamination guard), and add 2-3 examples to the claims and
      text-analysis prompts (bumps prompt versions; re-record evals).
