# 2026-07-25 — Analysis judgments come from the model, not from word lists

Owner rule (2026-07-25): the LLM makes every analysis judgment. Keyword lists,
proximity windows, and hand-tuned threshold formulas may measure things and may
feed a prompt, but they must not produce or override a verdict. Two live paths
in the Postgres pipeline broke that rule and are gone.

## What shipped

**The propaganda loaded-language pre-filter is deleted**
(`analysis/src/engine/propaganda.py`). A scan of the first 600 characters
against a negative-word/intensifier lexicon used to decide `density=0.0`, write
zero techniques, and skip the LLM call entirely. Every queued doc now reaches
the model. Removed with it: `_has_loaded_language()`, `_WORD_RE`,
`_PRE_FILTER_REASONING`, and the `PROPAGANDA_LOADED_LEXICON` /
`PROPAGANDA_PRE_FILTER_SCAN_CHARS` constants in `engine/constants.py`.
`NEGATIVE_WORDS` and `INTENSIFIERS` stay — the retired
`engine/propaganda_detector.py` and `engine/analyzer.py` still import them and
still run in production until the Phase 11 timer flip.

`PropagandaAnalysis.inference_method` went too. With the pre-filter gone
`analyze()` only ever returned `'llm'`, so the field was a constant threaded
through the dataclass; `process()` now passes the literal at the `open_run`
call. The `analysis.runs.inference_method` column is untouched and still
meaningful for other tasks (citations write `'deterministic'`, bot writes
`'hybrid'`).

**The bot aggregate score is deleted** (`analysis/src/engine/bot_detection.py`).
`_aggregate_score()` was a hand-tuned additive formula (+0.3 spam keywords,
+0.2 repetition, +0.15 low sentence-length variance, +0.18 hedge rate, +0.2
follow-ratio anomaly, and so on) that produced `analysis.bot_signals.score` —
a numeric bot verdict the model never saw or ratified. `score` is now the
model's own `llm_text_likelihood`. The deterministic signal battery still runs:
it feeds the prompt and populates the typed stylometric columns
(`burstiness`, `type_token_ratio`, `template_score`) and `raw_response`.

The bot label was already LLM-only and stays that way — an LLM failure raises
and `process()` records a failed run rather than degrading to heuristics.

**Propaganda gained the trivial-content gate** the other LLM stages already
had (`engine/text.py`, `targets.py`, `claims.py`). It was the only stage
paying a full call for every @-mention-and-link-only post, on a corpus that is
mostly short posts. This is the opposite of the pre-filter above and the
distinction is the whole point: the keyword filter *asserted* a verdict
("no propaganda here") from a word list, while this gate *declines to judge*
content with no substantive words, writing a `done` `deterministic` run with
no `propaganda_results` row. An absent row now means unanalyzable;
`density = 0.0` keeps meaning the model looked and found nothing.

Consequence for the acceptance gates: a `propaganda` run with no result row is
no longer automatically a failure. `docs/deployment/phase8-acceptance-gates.md`
is updated — a missing row is a failure only when `inference_method = 'llm'`.

## Behaviour changes to watch

**The verified-account de-bias gate is gone.** `_aggregate_score` hard-zeroed
the score for `verified_type='government'` and capped `'business'` at 0.3. It
was itself a hand-tuned rule overriding the model, so it went with the formula.
Nothing replaces it. `verified_type` is still measured, still reaches the
prompt, and still lands in `raw_response`.

**`score` and `llm_text_likelihood` now hold the same value.** By extension
`analysis.author_bot_scores.score` and `.llm_text_likelihood_mean` become equal
too, since the rollup averages both. Two columns, one fact. Resolving that
needs a migration plus a change to the sentiment panel's author-exclusion
predicate, so it is deliberately not in this change.

**The exclusion gate changed meaning.** `api/queries/constants.py`'s
`BOT_SCORE_AUTHOR_EXCLUSION = 0.5` filters authors out of the sentiment panel
by `author_bot_scores.score`. That threshold was calibrated against the old
additive formula and now reads a different quantity. `llm_text_likelihood`
measures how machine-written the *text* looks, not whether an *account* is
bot-operated — press-release prose scores high on the first without being the
second. The threshold needs recalibrating against real data before the next
acceptance pass, and the panel's exclusion counts should be checked for a jump.

## Why

Both paths let a fixed rule stand in for a language judgment, and neither was
visible downstream. The propaganda pre-filter stamped its rows
`inference_method='deterministic'`, but nothing in `analysis/src/api/` filters
on that column, so a keyword-gated zero rendered exactly like an LLM-verified
zero. The bot score shared one row-level `'hybrid'` method value with the
LLM-sourced label, so no query could tell which half of the row came from the
model. `.agent/rules/invariants.md` rule 1 forbids presenting heuristic
analysis as fact; these two made it structurally impossible to tell the
difference at read time.

## Tests

`analysis/tests/test_engine_propaganda.py` — the two pre-filter tests are
replaced by `EveryDocReachesTheLLMTests`, asserting that neutral and empty text
both still reach the model, plus a process-level test that a neutral doc
persists as an `'llm'` run with a non-NULL `prompt_version_id`. These are the
regression guard: they fail if a short-circuit is reintroduced.

`analysis/tests/test_engine_bot.py` — `SignalBatteryTests` no longer asserts on
an aggregate score; it checks that the battery measures what it claims to. The
verified-type test now documents that `verified_type` is captured and gates
nothing. The hybrid test asserts `score == llm_text_likelihood`.

Full suite: 1099 tests, 0 failures, 242 skipped (unchanged skip count).

## Follow-ups

- Recalibrate `BOT_SCORE_AUTHOR_EXCLUSION` against the new score, or switch the
  gate to the model's `label`/`confidence`, which is the truer bot-account
  signal. Decide before Phase 11 acceptance.
- Collapse the `score` / `llm_text_likelihood` duplication once the exclusion
  predicate is settled.
- Propaganda rows written before this change carry a keyword-gated
  `density=0.0` with no LLM call behind them. They are only corrected by a
  propaganda re-run; until then the panel mixes both vintages.
- Decide whether a replacement de-bias protection for verified official
  accounts is wanted, given they are the accounts the project tracks.
