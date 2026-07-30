# Propaganda classifier v2: Kobe-verified golden set, calibration, then the prompt

Order is deliberate: measure first, change the prompt only against numbers.
Reuses the claims eval harness (`analysis/evals/` — golden JSON, replay
recordings, span-IoU scoring, CI gate; see `docs/EVALS.md`). The propaganda
stub in `docs/todos/eval-expansion.md` moved here.

**Hard rule for this initiative: every golden label is adjudicated by Kobe.**
No LLM output, and nothing Claude writes, enters `analysis/evals/golden/propaganda/`
without his explicit verdict. The eval runner refuses unverified files.

## Phase 1 — Golden set (~300 docs, Kobe adjudicates all of them)

- [ ] Deterministic stratified sampler (script under `analysis/evals/`), documented in the file it writes:
      - source_type split (x_post / reddit_post / news) roughly matching corpus shares
      - density bands: 0, (0, 0.3], the 0.3 evidence-cap value itself, (0.3, 0.6], (0.6, 1]
      - at least 25 flagged examples per technique (all six)
      - hard negatives on purpose: attributed quotes ("Critics say X is a 'radical extremist'"), sarcasm, strong-but-measured opinion, factual reporting on charged topics
      - carried over from the eval-expansion stub: negatives the `_has_loaded_language` pre-filter kills, and one example with a technique near the 800-char clamp
- [ ] Queue the sample through the existing Review tab (`review/service.py` — propaganda is already a `ReviewTaskType`; add a way to pin this batch ahead of lowest-confidence ordering). Kobe verdicts each run correct / incorrect / uncertain, with notes naming missed or wrong techniques. Budget: ~300 docs at 30–45s ≈ 3–4 hours, splittable.
- [ ] Curation script: adjudicated evals -> `analysis/evals/golden/propaganda/propaganda-NNN.json` (doc text, expected techniques with verbatim spans, expected density band). "Incorrect" runs get their corrected expected label from Kobe's notes or a second pass — never inferred.
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
- [ ] Bump `PROPAGANDA_PROMPT_VERSION` to `propaganda-v2`; re-record recordings; ship only if v2 beats the v1 baseline outside the harness's F1 tolerance. Record the before/after in the audit trail either way.

## Phase 4 — the train-our-own-model checkpoint (decision, not work)

- [ ] Revisit only when BOTH hold: >= 1,000 Kobe-adjudicated propaganda labels exist, AND v2-era F1 has plateaued across two prompt versions (or cost/volume makes per-doc LLM calls the bottleneck). Then evaluate a CPU-servable fine-tuned encoder against the same golden set. Until then: no training work.
