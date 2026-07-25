# 2026-07-25 — Bot-exclusion gate moves from a numeric score to a labelled-share threshold

The author-level bot-exclusion gate no longer reads a numeric "bot score".
`analysis.bot_signals.score` and `analysis.author_bot_scores.score`/
`.variance` are dropped (`data/pg-migrations/0005_drop_bot_score.sql`) --
since the 2026-07-25 removal of the hand-tuned `_aggregate_score`, `score`
had become a literal copy of `llm_text_likelihood` (how machine-written the
TEXT reads), which is the wrong signal for an ACCOUNT-level exclusion
decision and a duplicate column besides (see
`docs/audit-trail/analysis/2026-07-25-llm-only-judgments.md`, this change's
direct predecessor). The gate is now: an author is excluded once at least
half their confidence-floored analyzed posts were LABELLED bot or
suspicious by the model -- a "label AND confidence" gate, not a threshold
formula standing in for a judgment.

## What shipped

- **`data/pg-migrations/0005_drop_bot_score.sql`** (new, one-shot,
  non-idempotent, same convention as 0003/0004): `ALTER TABLE ... DROP
  COLUMN` on `analysis.bot_signals.score` and
  `analysis.author_bot_scores.score`/`.variance`. `llm_text_likelihood` /
  `llm_text_likelihood_mean` are untouched -- they remain the genuine
  LLM-text signal, just no longer duplicated into `score`.
- **`analysis/src/engine/bot_detection.py`**:
  - `BotAnalysis` drops its `score` field; `analyze()`/`_llm_analysis()`/
    `process()` no longer construct or persist one.
  - `refresh_author_bot_scores()`'s rollup SQL
    (`_UPSERT_AUTHOR_BOT_SCORES_SQL`) drops `AVG(bs.score)`/
    `VAR_POP(bs.score)` and now counts `bot_post_count`/
    `suspicious_post_count` with `COUNT(*) FILTER (WHERE bs.label = 'bot'
    AND r.confidence >= %(min_conf)s)` (and the `suspicious` equivalent) --
    the confidence floor. `sample_count` (the denominator) is NOT
    confidence-floored: it counts every analyzed post regardless of how
    confident any one label was.
  - The `bs.score IS NOT NULL` guard (on both the DELETE-stale and the
    UPSERT queries) is replaced by `bs.label != 'unknown'::analysis.bot_label`
    -- the precise semantic equivalent, since `score` was NULL for, and
    only for, the 'unknown' (empty-text) label. This is a like-for-like
    swap, not a loosening: the same rows are excluded as before.
- **`analysis/src/engine/constants.py`**: new
  `BOT_LABEL_MIN_CONFIDENCE = 0.5` -- the confidence floor a bot/suspicious
  label must clear to count toward `bot_post_count`/`suspicious_post_count`.
  Matches the repo-wide `aggregation_min_confidence` default
  (`common/settings.py`, walkthrough 039) rather than inventing a new one.
- **`analysis/src/results/store.py`** (necessary cross-file edit -- see
  Scope note below): `BotSignalsRow` drops its `score` field; the
  `analysis.bot_signals` INSERT in `RunHandle._write_results()` no longer
  writes that column. `store.py` is the sole writer of `analysis.bot_signals`
  (the traceability-contract module every engine goes through), so it had to
  change in lockstep with the DROP COLUMN or every bot-engine run would fail
  at INSERT time the moment 0005 was applied.
- **`analysis/tests/pg_fixture.py`**: added `MIGRATION_0005` and applies it
  in `reset_schema()` -- every gated test module's baseline schema now
  includes the dropped columns, not just 0001-0004.
- **Tests** (`analysis/tests/test_engine_bot.py`,
  `test_result_store.py`): updated for the new `BotAnalysis`/`BotSignalsRow`
  shape. `RefreshAuthorBotScoresIntegrationTests` rewritten around
  `bot_post_count`/`suspicious_post_count`/`sample_count` instead of
  `score`/`variance`, plus two new tests encoding the two halves of the
  redesign: `test_low_confidence_bot_labels_do_not_count_toward_bot_post_count`
  (a 'bot' label below `BOT_LABEL_MIN_CONFIDENCE` must not count -- the
  confidence floor earning its place) and
  `test_high_llm_text_likelihood_with_human_label_does_not_count_as_bot` (a
  post that READS machine-written but is LABELLED human must not count --
  the whole point of moving off `llm_text_likelihood`).

## Why

- **`score` had no reader that wasn't also a reader of
  `llm_text_likelihood`.** Two columns holding one fact is the kind of
  duplication `.agent/rules/invariants.md` and DRY both flag; it also meant
  the account-exclusion gate (`api/queries/constants.py`'s old
  `BOT_SCORE_AUTHOR_EXCLUSION`) was silently keyed on a TEXT-style signal
  instead of an ACCOUNT-behavior signal, which is the wrong axis for "should
  this account's posts be excluded from discourse panels".
- **The prior threshold was calibrated against a formula that no longer
  exists.** The deleted `_aggregate_score` was a hand-tuned additive
  formula; `BOT_SCORE_AUTHOR_EXCLUSION = 0.5` was tuned against its output
  distribution. That formula is gone, so 0.5 as a flagged-post-SHARE
  threshold (`BOT_FLAGGED_SHARE_EXCLUSION`, see the paired
  `docs/audit-trail/api/2026-07-25-bot-exclusion-gate.md` entry) is a FRESH
  choice, not a re-derivation -- it needs validation against real production
  data before the next acceptance pass, same as the api-layer entry states.
- **A low-confidence guess must not silence an author.** Without the
  confidence floor, one throwaway low-confidence 'bot' guess on an
  otherwise-clean author could tip their flagged share over the exclusion
  threshold. `BOT_LABEL_MIN_CONFIDENCE` makes that impossible: only labels
  the model itself was reasonably sure about count toward the numerator.

## Scope note

`analysis/src/results/store.py` is not normally owned by this workstream
(the owning boundary is `analysis/src/engine/bot_detection.py` +
`analysis/src/api/queries/*.py` + `analysis/tests/**`), but it is the ONLY
writer of `analysis.bot_signals` (see `docs/DATABASE_SCHEMA.md`'s
Result-store write semantics section) and its `BotSignalsRow`/INSERT
statement both referenced the column 0005 drops. Leaving it unedited would
have broken every bot-engine run the moment the migration applied. The edit
is mechanical and minimal: remove the `score` field/column, nothing else.

## Follow-ups

- Validate `BOT_FLAGGED_SHARE_EXCLUSION = 0.5` against real production data
  once the pipeline has run long enough to accumulate `author_bot_scores`
  rows under the new rollup -- flagged shares should be reviewed the same
  way the old score threshold was meant to be (see the 2026-07-25
  llm-only-judgments entry's follow-up, now superseded by this one).
- `analysis/src/reporting/**` and `analysis/src/scheduler/job_runner.py`
  (the retired SQLite stack, still running in production via the systemd
  timer) have their OWN, separate `BOT_SCORE_AUTHOR_EXCLUSION` constant
  (`reporting/aggregators/sentiment/target_tone.py:53`) over a SQLite
  `author_bot_scores` table with its own `score` column. That stack is
  deliberately untouched by this change -- it is a different table, in a
  different database, read by different code, and is retired at Phase 11,
  not before.
