# 2026-07-25 — Query layer's bot exclusion reads a flagged-post share, not a score

Every panel that excludes bot-scored authors from a discourse denominator
(`bots.py`, `entities.py`, `propaganda.py`, `narratives.py`, `movers.py`,
`sentiment.py`) now gates on `analysis.author_bot_scores.bot_post_count +
.suspicious_post_count` over `.sample_count` -- a SHARE of an author's
confidence-floored analyzed posts labelled bot/suspicious -- instead of the
retired additive `.score` column. See the paired
`docs/audit-trail/analysis/2026-07-25-bot-exclusion-gate.md` entry for the
engine-side half (the confidence floor and the rollup SQL) and
`docs/audit-trail/analysis/2026-07-25-llm-only-judgments.md` for why `score`
existed as a duplicate in the first place.

## What shipped

- **`analysis/src/api/queries/constants.py`**: `BOT_SCORE_AUTHOR_EXCLUSION`
  is retired; `BOT_FLAGGED_SHARE_EXCLUSION = 0.5` replaces it. Same numeric
  value, entirely different meaning -- 0.5 now reads "at least half this
  author's analyzed posts were labelled bot or suspicious", which is
  interpretable in a way the old additive score never was.
- **Six query modules updated** to the new predicate. The common SQL shape,
  repeated per-module matching each file's existing convention (a shared
  `_BOT_EXCLUSION_SQL` local constant in `entities.py`/`sentiment.py`,
  inline per-query in `propaganda.py`/`narratives.py`/`movers.py`/`bots.py`):
  ```sql
  (b.bot_post_count + b.suspicious_post_count)::float
      / NULLIF(b.sample_count, 0) >= %(bot_floor)s
  ```
  - `bots.py`: `_fetch_author_bot_scores` now selects a computed
    `flagged_share` column instead of `score`; `_build_flagged_accounts`,
    `get_bot_activity`'s automation-rate calc, and
    `_build_bot_pushed_narratives` all key off it.
  - `entities.py`: `_BOT_EXCLUSION_SQL` plus four more inline `NOT EXISTS`
    blocks (`_AUTHORED_DOCS_SQL`, `_AUTHORED_MODEL_IDS_SQL`,
    `_STANCE_RECEIVED_SQL`, `_STANCE_EXPRESSED_SQL`) converted.
  - `propaganda.py`: three inline `NOT EXISTS` blocks converted
    (`_fetch_eligible_docs`, `_fetch_technique_evidence`,
    `_fetch_flagged_samples`).
  - `narratives.py`: `_bot_pushed_fraction` renamed its parameter from
    `author_bot_scores: Sequence[float]` to
    `flagged_shares: Sequence[Optional[float]]`; `_fetch_bot_rows` now
    computes `flagged_share` in SQL rather than selecting `ab.score`;
    `_fetch_propaganda_rows`'s exclusion `NOT EXISTS` converted too.
  - `movers.py`: the `LEFT JOIN ... (abs.score IS NULL OR abs.score <
    threshold)` pattern becomes `(abs.author_id IS NULL OR flagged_share <
    threshold)` -- `abs.author_id IS NULL` is the correct equivalent for "no
    author_bot_scores row at all" now that there is no nullable `score`
    column to test directly.
  - `sentiment.py`: `_BOT_EXCLUSION_SQL` converted (two call sites).
  - `docs.py` (found via grep, not in the prompt's scanned list):
    `_bot_fields`'s raw passthrough SELECT for the doc drill-down dropped
    `score` from its column list -- it would otherwise fail at query time
    the moment 0005 applied.
- **`analysis/src/api/models/bots.py`**: `FlaggedAccount.bot_score: float`
  renamed to `flagged_post_share: float`, with an updated docstring. A field
  named `bot_score` holding a share, post-rename, would have been dishonest
  -- CamelModel serializes it as `flaggedPostShare` over the wire.
- **UI**: `ui/src/types.ts`'s `FlaggedAccount.botScore` renamed to
  `flaggedPostShare`; `ui/src/pages/BotActivityProfiler.tsx`'s three
  consumers (`FlaggedAccountModal`'s stat tile, its label text "Bot score" ->
  "Flagged post share", and `FlaggedAccountsCard`'s rate display) updated to
  match. See `docs/audit-trail/ui/2026-07-25-bot-exclusion-gate.md`.
- **Contract snapshot**: `analysis/tests/contract/snapshots/bots_basic.json`
  needed NO change -- its `flaggedAccounts` array is empty in that fixture
  (no `author_bot_scores` row is seeded), so the renamed field never
  appears in the recorded payload. Verified by re-running the full gated
  contract-test tier against a throwaway Postgres; it passed unchanged.

## Why

- The old threshold read `author_bot_scores.score`, a duplicate of
  `llm_text_likelihood` since 2026-07-25 (see the cross-linked analysis
  entry) -- an account-exclusion gate keyed on how machine-written TEXT
  reads is the wrong axis. The new gate reads the model's own
  bot/suspicious/human LABEL, aggregated as a share, which is what an
  account-behavior exclusion should measure.
- **The prior threshold's calibration doesn't carry over.** The retired
  `BOT_SCORE_AUTHOR_EXCLUSION = 0.5` was tuned (loosely) against the
  now-deleted additive `_aggregate_score` formula's output distribution.
  `BOT_FLAGGED_SHARE_EXCLUSION = 0.5` is kept at the same numeral because it
  is independently a reasonable starting point ("at least half"), not
  because the old tuning transfers -- this is a FRESH choice that needs
  validation against real data before the next acceptance pass, exactly as
  flagged in the analysis-layer entry.
- Renaming `bot_score` to `flagged_post_share` rather than silently
  redefining it under the same name is a labeling-discipline requirement
  (`.agent/rules/media-analysis.md`): a field's name is part of its
  contract with every UI consumer, and a share is not a score.

## Follow-ups

- Same as the analysis-layer entry: validate `BOT_FLAGGED_SHARE_EXCLUSION`
  against real production `author_bot_scores` data once the pipeline has
  accumulated rows under the new rollup.
- If a future workstream wants to de-duplicate the six near-identical
  `(bot_post_count + suspicious_post_count) / sample_count >= threshold`
  SQL fragments into one shared `queries/base.py` helper, that is a
  Rule-2-simplicity call for whoever touches this predicate next -- this
  change kept the existing per-module duplication convention rather than
  introducing a new abstraction mid-migration.
