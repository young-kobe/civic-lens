# Golden sets for every LLM-derived metric

Owner decision 2026-07-30: every metric an LLM produces gets a Kobe-verified
golden set and a committed baseline, so prompt/model/schema changes are
gated on measured quality everywhere, not just claims. Same hard rule as
the propaganda initiative: every golden label is adjudicated by Kobe; the
eval runner refuses unverified files.

The coverage map (which tasks, what unit, what machinery):

| task | label unit | machinery |
|---|---|---|
| claims | per-claim span | DONE — `analysis/evals/golden/claims/` + CI gate (`docs/EVALS.md`) |
| propaganda | per-flag | in flight — `docs/todos/propaganda-classifier-v2.md` (builds the sub-label pattern) |
| targets | per-mention | sub-label pattern, cloned from propaganda |
| text (sentiment) | per-doc | run-level — existing evals/golden_labels flow |
| bot | per-doc | run-level |
| account_tier | per-author | run-level, LLM-classified authors only (`method='llm'`) |

Not in scope, with reasons: `citations` / `leans` / `bot_rollup` are
deterministic (no LLM judgment to golden; unit tests are their gate);
`narratives` is embedding-only — no LLM label exists, so it gets a
different instrument (doc-to-narrative membership spot-checks) as a later,
separate initiative, not a label golden set forced onto it.

Sequencing rule: propaganda lands first and proves the sub-label
machinery; nothing below starts until its Phase 0/1 pattern is working
end to end. Kobe's total adjudication across all tasks: roughly 12-15
hours, split across sessions (progress persists per run).

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

## Shared close-out

- [ ] One eval runner invocation scores every task with a golden dir;
      per-task baselines all gate the same way claims does (warn-and-pass
      until each baseline is hand-committed, then hard gate).
- [ ] Audit-trail entry per landed set (analysis bucket), recording the
      v1 numbers whatever they are.
