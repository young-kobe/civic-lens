# Eval expansion: calibration, metamorphic tests, disagreement sampling

Follow-on evals for the analysis layer beyond the claims golden set
(`docs/EVALS.md`). Context feeding groundwork (sentence-boundary
truncation, triviality pre-filter, reasoning bounds) landed in
`docs/audit-trail/analysis/2026-07-09-llm-context-optimizations.md`.

## Calibration report

- [ ] Join `ai_outputs.confidence` with `ai_output_evals.is_correct` per
      task_type into confidence buckets (0.5-0.6, ..., 0.9-1.0) with
      observed accuracy per bucket.
- [ ] Expose as part of `/review/stats` (admin-gated) so the Review tab
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

- [ ] `analysis/evals/golden/propaganda/` following the claims schema,
      with technique labels per span.
- [ ] Metric: (span IoU >= 0.3 AND technique match) micro-F1, plus a
      technique-confusion breakdown.
- [ ] Include negatives that the `_has_loaded_language` pre-filter kills,
      and one example with a technique near the 800-char clamp.

## Few-shot example pool

- [ ] Once review-queue corrections accumulate: curate a prompt-example
      pool from corrected failures, disjoint from `analysis/evals/golden/`
      (eval-contamination guard), and add 2-3 examples to the claims and
      text-analysis prompts (bumps prompt versions; re-record evals).
