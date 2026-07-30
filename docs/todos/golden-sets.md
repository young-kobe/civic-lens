# Golden sets for every metric

Owner decision 2026-07-30 (extended same day): every metric gets a
Kobe-verified golden set and a committed baseline — LLM tasks gate
prompt/model/schema changes on measured quality; deterministic tasks gate
RULE changes on verified fixtures over real docs, because deterministic is
not the same as correct (the extractor can be perfectly reproducible and
still miss real citations). Same hard rule everywhere: every golden label
is adjudicated by Kobe; the eval runner refuses unverified files.

Two scoring regimes, one workflow:
- LLM tasks: adjudicated labels -> per-task F1/accuracy baseline with a
  tolerance gate (model output varies).
- Deterministic tasks: adjudicated fixtures -> exact-match replay, zero
  tolerance; a failure is a rule gap or an intended rule change, and the
  fixture diff IS the review artifact.

The coverage map (which tasks, what unit, what machinery):

| task | label unit | machinery |
|---|---|---|
| claims | per-claim span | DONE — `analysis/evals/golden/claims/` + CI gate (`docs/EVALS.md`) |
| propaganda | per-flag | in flight — `docs/todos/propaganda-classifier-v2.md` (builds the sub-label pattern) |
| targets | per-mention | sub-label pattern, cloned from propaganda |
| text (sentiment) | per-doc | run-level — existing evals/golden_labels flow |
| bot | per-doc | run-level |
| account_tier | per-author | run-level, LLM-classified authors only (`method='llm'`) |

Deterministic coverage (fixture regime):

| task | fixture unit | status |
|---|---|---|
| citations | expected edges per doc | planned below |
| leans | expected lean per derived author/entity | planned below |
| narratives | (doc, narrative) membership pairs | planned below |
| bot_rollup | — | EXCLUDED: pure SQL arithmetic over bot labels goldened upstream; a fixture set would test addition. Its real open question is the 0.5 exclusion threshold — `docs/todos/bot-propaganda-signal-calibration.md`. |
| etl admission | — | EXCLUDED: enforced by migrations + unit tests; no judgment involved. |

Sequencing rule: propaganda lands first and proves the sub-label
machinery; nothing below starts until its Phase 0/1 pattern is working
end to end. Kobe's total adjudication across all tasks: roughly 15-19
hours (12-15 LLM + ~3-4 deterministic), split across sessions (progress
persists per run).

## Targets golden set (per-mention — clone of the propaganda pattern)

- [ ] `analysis.mention_evals`: verdict per `analysis.target_mentions`
      mention (stance correct / incorrect / uncertain), rejection-reason
      enum for this domain (`wrong_target` — right span, wrong entity;
      `wrong_stance`; `wrong_topic`; `not_a_target` — no directed mention
      at all; `sarcasm_misread`), plus missed-mention entries
      (reviewer-supplied target + stance + span).
- [ ] Review tab: targets runs render one verdict row per mention (target
      label, stance chip, topic) with the mention grounded in the doc text.
- [ ] Rubric section (extends the propaganda RUBRIC.md structure): stance
      is toward the TARGET in this doc, not the author's general politics;
      topic verdicts use the fixed topic vocabulary; quoting someone
      else's attack is the quoted speaker's stance, not the author's.
- [ ] Stratified sampler (~200 mentions): by source_type, stance,
      topic-vs-General, resolved-entity vs raw_target, collective
      (party-alias) vs individual targets.
- [ ] Curation -> `analysis/evals/golden/targets/`, `verified_by` gate,
      recordings, `baseline_targets.json`, per-stance confusion matrix.

## Sentiment (text) golden set (run-level)

- [ ] Review submit flow captures the corrected label on an `incorrect`
      verdict (it must never be inferred) and mints
      `analysis.golden_labels` rows -- verify the existing path does this
      end to end; fix if it drops the correction.
- [ ] Stratified sampler (~200 docs): by label (incl. MIXED), source_type,
      sarcasm_detected, confidence band, and admission class. Hard cases
      on purpose: sarcasm, quoted hostility, confident-but-neutral wonkery.
- [ ] Curation -> `analysis/evals/golden/text/`, recordings,
      `baseline_text.json`, calibration buckets (the eval-expansion
      calibration report, scoped per task).

## Bot golden set (run-level)

- [ ] Same run-level flow (~150 docs): stratified by verdict
      (bot/suspicious/human), source_type, account age bucket. Rubric
      note: Kobe adjudicates "does the EVIDENCE support the verdict"
      (indicators + reasoning), not ground truth he cannot know -- the
      golden label is "defensible verdict", stated honestly in the rubric.
- [ ] Curation -> `analysis/evals/golden/bot/`, recordings,
      `baseline_bot.json`.

## Account-tier golden set (run-level, per-author)

- [ ] Sample only `method='llm'` author classifications (~100 authors;
      curated_list rows are ground truth already, not model output).
      Review view shows the author profile signals the model saw.
- [ ] Curation -> `analysis/evals/golden/account_tier/`, recordings,
      `baseline_account_tier.json`.

## Citations fixture set (deterministic — rule quality, not stability)

- [ ] Sample ~100 real docs weighted toward citation-bearing content
      (news with outbound links, quoting posts), plus deliberate
      negatives: syndication boilerplate, self-links, link-farm footers,
      bare domain mentions with no citation intent.
- [ ] Kobe verifies the expected edge set per doc (source -> cited doc/URL,
      or "no edges"). Review view shows the doc with candidate links
      highlighted.
- [ ] Curation -> `analysis/evals/golden/citations/`, exact-match replay
      against `engine/citations.py`; score precision/recall of the RULES
      against Kobe's judgment. Misses become either rule fixes (re-run
      fixtures, exact match again) or documented known limitations —
      never silently absorbed.

## Leans fixture set (deterministic derivation)

- [ ] Sample ~50 DERIVED leans (`engine/lean_derivation.py` outputs, not
      registry-seeded facts — those are already ground truth). Kobe
      verifies each derived lean against the inputs the derivation saw.
- [ ] Curation -> `analysis/evals/golden/leans/`, exact-match replay;
      a disagreement is a derivation-rule defect or a rubric case worth
      writing down (e.g. how mixed signals should resolve).

## Narratives membership fixture set (embedding-threshold gate)

- [ ] ~60 verified pairs: docs that DO belong to a named narrative and
      near-miss docs that must NOT cluster into it (same topic, different
      claim). Kobe verdicts membership per pair.
- [ ] Replay: re-run clustering assignment over the fixture docs with the
      configured embedding model/threshold; score membership agreement.
      This is THE gate for changing `CIVIC_NARRATIVE_EMBEDDING_MODEL` or
      the threshold — today those changes ship unmeasured.

## Shared close-out

- [ ] One eval runner invocation scores every task with a golden dir;
      LLM tasks gate against their baseline with the claims-style
      tolerance, deterministic tasks gate exact-match (warn-and-pass
      until each baseline is hand-committed, then hard gate).
- [ ] Audit-trail entry per landed set (analysis bucket), recording the
      v1 numbers whatever they are.
