# Propaganda classifier v2: Kobe-verified golden set, calibration, then the prompt

Order is deliberate: measure first, change the prompt only against numbers.
Reuses the claims eval harness (`analysis/evals/` — golden JSON, replay
recordings, span-IoU scoring, CI gate; see `docs/EVALS.md`). The propaganda
stub in `docs/todos/eval-expansion.md` moved here.

**Hard rule for this initiative: every golden label is adjudicated by Kobe.**
No LLM output, and nothing Claude writes, enters `analysis/evals/golden/propaganda/`
without his explicit verdict. The eval runner refuses unverified files.

## Phase 0 — Per-flag verdicts and the labeling rubric (prerequisite)

The run-level `analysis.evals` verdict cannot express "this run's
loaded_language flag is right, its appeal_to_fear flag is wrong." Kobe rates
every label, so verdicts attach to flags, not runs.

- [ ] Migration: `analysis.technique_evals` — verdict per
      `analysis.propaganda_techniques.technique_id` (correct / incorrect /
      uncertain), a `reason` enum for rejections, reviewer, notes,
      timestamp. Plus a way to record a MISSED technique (false negative):
      reviewer-supplied technique + verbatim span for a doc, not tied to a
      model flag.
- [ ] Rejection-reason vocabulary (the enum): `factual_report` (span reports
      a real event/fact, no rhetorical device), `attributed_quote`,
      `sarcasm_misread`, `wrong_technique` (device present, different
      technique — feeds the confusion matrix), `span_not_representative`,
      `measured_opinion`. These reasons ARE the product: Phase 2's
      false-positive breakdown and Phase 3's few-shot hard negatives come
      straight from them.
- [ ] Review tab: propaganda runs render one verdict row per flag (technique,
      span highlighted in the doc, confidence) + an "add missed technique"
      affordance; run-level verdict derives from the flags (all correct ->
      correct), not the other way around.
- [ ] Labeling rubric (`analysis/evals/golden/propaganda/RUBRIC.md`), written
      first and signed off by Kobe before any adjudication. Must decide the
      framing-vs-truth rule explicitly: the verdict rates the rhetorical
      framing of the flagged span, never the truth of the underlying event —
      a factual repost of a real event is not appeal_to_fear (reject as
      `factual_report`), but a true event packaged to bypass reasoning
      ("this PROVES they're coming for your kids") still is. Scariness alone
      earns nothing; truth alone clears nothing.

## Phase 1 — Golden set (~300 docs, Kobe adjudicates all of them)

- [ ] Deterministic stratified sampler (script under `analysis/evals/`), documented in the file it writes:
      - source_type split (x_post / reddit_post / news) roughly matching corpus shares
      - density bands: 0, (0, 0.3], the 0.3 evidence-cap value itself, (0.3, 0.6], (0.6, 1]
      - at least 25 flagged examples per technique (all six)
      - hard negatives on purpose: attributed quotes ("Critics say X is a 'radical extremist'"), sarcasm, strong-but-measured opinion, factual reporting on charged topics
      - carried over from the eval-expansion stub: negatives the `_has_loaded_language` pre-filter kills, and one example with a technique near the 800-char clamp
- [ ] Queue the sample through the Review tab (`review/service.py` — propaganda is already a `ReviewTaskType`; add a way to pin this batch ahead of lowest-confidence ordering). Kobe verdicts every FLAG per the Phase 0 rubric (per-flag correct/incorrect + reason, plus missed techniques). Budget: ~300 docs at 45–60s with per-flag rating ≈ 4–5 hours, splittable.
- [ ] Curation script: per-flag adjudications -> `analysis/evals/golden/propaganda/propaganda-NNN.json` (doc text; expected techniques with verbatim spans; rejected flags kept as named negatives with their reason codes; expected density band). Corrections come from Kobe's verdicts/notes only — never inferred.
- [ ] Sign-off gate: each golden file carries `verified_by`; the runner hard-fails on any file without it. Golden set is locked only when Kobe has verified every file.
- [ ] Record replay recordings for the current model (claims-harness pattern) so scoring runs offline.

## Phase 2 — Score and calibrate (report-only, no gate yet)

- [ ] Scoring: (span IoU >= 0.3 AND technique match) micro-F1 per technique + overall, plus a technique confusion matrix and a false-positive class breakdown (quoted-speech, sarcasm, opinion).
- [ ] Confidence calibration buckets (runs.confidence vs eval verdicts) for the propaganda task — the eval-expansion calibration report, scoped here first. Caveat in output: buckets under 20 rows are directional.
- [ ] Commit `analysis/evals/baseline_propaganda.json` as the v1 floor.
- [ ] Audit-trail entry recording the v1 numbers — whatever they are (fail loud; bad numbers are the point of measuring).

## Phase 3 — propaganda-v2 prompt (gated on Phase 2)

- [ ] Fix the confidence/density conflation first: the engine currently writes density into `runs.confidence`. Schema/prompt emit a separate model confidence; `results/store.py` stores confidence in `runs.confidence`, density stays in `propaganda_results.density`. Trace every reader of `runs.confidence` where task='propaganda' (review-queue ordering, any API min_conf filter) before flipping.
- [ ] Prompt changes only for failure classes Phase 2 actually shows. Candidates, in likely order: sharpen technique definitions against the SemEval/PTC propaganda-technique taxonomy (Da San Martino et al. — our six map onto theirs); add 2–3 few-shot exemplars per weak technique, drawn from Kobe's corrected failures and kept disjoint from golden/ (contamination guard, same rule as the claims few-shot pool box).
- [ ] Per-flag target attribution (candidate, same gate): the schema could ask which target each technique is deployed for/against, replacing the doc-level `PropagandaExample.targets` join (shipped 2026-07-30) with a real technique->target edge. Only with golden-set verification of the edge itself — Kobe's per-flag verdicts would then rate (technique, span, target) triples, and the rubric needs a target-attribution rule first.
- [ ] Bump `PROPAGANDA_PROMPT_VERSION` to `propaganda-v2`; re-record recordings; ship only if v2 beats the v1 baseline outside the harness's F1 tolerance. Record the before/after in the audit trail either way.

## Phase 4 — the train-our-own-model checkpoint (decision, not work)

- [ ] Revisit only when BOTH hold: >= 1,000 Kobe-adjudicated propaganda labels exist, AND v2-era F1 has plateaued across two prompt versions (or cost/volume makes per-doc LLM calls the bottleneck). Then evaluate a CPU-servable fine-tuned encoder against the same golden set. Until then: no training work.
