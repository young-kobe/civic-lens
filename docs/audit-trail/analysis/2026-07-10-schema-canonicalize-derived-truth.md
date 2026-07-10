# 2026-07-10 — Canonicalize derived truths in the schema

Follow-up to the same-day doc_task_state decoupling. Five facts that the
schema should own were re-derived (or double-encoded) in Python; each now
has a single canonical home. `ai_outputs.output_json` remains the full
audit payload — the new column is a projection of it, never a replacement.

## What shipped

- `data/migrations/023_ai_outputs_label.sql`: `ai_outputs.label` — the
  row's primary categorical verdict (sentiment POSITIVE/NEGATIVE/NEUTRAL/
  MIXED, bot bot/suspicious/human/unknown, favorability stance). Backfills
  from JSON, including the boolean-only `is_bot` encoding the oldest bot
  rows carried; indexed on (task_type, label); recreates `ai_outputs_latest`
  so the view's captured column list includes the new column. List-shaped
  tasks (claims, target_sentiment, propaganda) leave it NULL.
- `etl/loader.py::save_ai_output` takes `label`; the bot, pre-excluded-bot,
  sentiment, and favorability writers in `scheduler/job_runner.py` pass it.
- Bot-verdict readers now filter the column: `base.get_bot_flagged_doc_ids`
  is a pure SQL filter (no JSON parse loop), and
  `entity_posts._BOT_EXCLUSION_SQL` dropped its two-key `json_extract`
  (which ran un-indexed on every drill-down request). The dual encoding
  (`$.label` vs `$.is_bot`) can no longer drift between readers.
- `data/migrations/024_narrative_citations_xor.sql`: rebuilds
  `narrative_citations` with `CHECK ((target_doc_id IS NULL) <>
  (target_url IS NULL))` — the XOR the prose promised and
  citation_extractor hand-enforced; the DB previously accepted a both-set
  row, which the citation-detail split would count twice. A `task_type`
  CHECK on ai_outputs was considered and deliberately NOT added: new tasks
  arrive regularly (target_sentiment landed this week) and each would
  force a full-table rebuild migration.
- `run_bot_detection` now triggers `run_account_bot_rollup` whenever it
  processed docs, so `author_bot_scores` — a pure derivation of
  bot_detection rows — can no longer go stale when `-Tasks bot` runs
  without `bot_rollup`. The standalone task remains for manual repair
  (idempotent full recompute).
- `clustering_mode` / `clustering_threshold` / `embedding_model`
  (write-only since migration 015) are surfaced as a `clustering`
  sub-object on `NarrativeSummary` (`{mode, threshold, embedding_model}`),
  flowing into the narratives snapshot. Null for narratives created before
  migration 015.

## Why

- The 2026-07-10 data-shape survey found readers defending against two
  encodings of the bot verdict, invariants enforced only by writer
  discipline, a rollup with no freshness tie to its source rows, and audit
  columns nothing read back — all "schema truths" living in code.

## Follow-ups

- UI: nothing renders `clustering` yet; add to the narrative drill-down
  modal when the Narratives page is next touched (types.ts has no entry).
- The survey's remaining large item — persisting resolved
  `target_entity_id` at write time instead of read-time registry
  resolution — is deliberately deferred to its own initiative.
- `run_bot_detection`'s rollup trigger has no direct unit test:
  `AnalysisJobRunner` is not unit-instantiable (constructs LLM clients).
  The rollup logic itself is unchanged and covered via BotAggregator tests.
