# 2026-07-10 — Decouple the pipeline work queue from ai_outputs

The analysis pipeline now tracks "has doc X been processed for task Y" in a
dedicated `doc_task_state` table (migration 022) instead of inferring it from
row existence in `ai_outputs`. `ai_outputs` is an append-only log of actual
analysis results; the state table is the work queue. Reprocessing a task
under a new prompt version is now `DELETE FROM doc_task_state WHERE
task_type = '<task>'` — old output rows survive with their prompt_version,
and readers see only the newest row per (doc, task) via the new
`ai_outputs_latest` view.

## What shipped

- `data/migrations/022_doc_task_state.sql`: `doc_task_state` (PK
  `(doc_id, task_type)`, status `done|failed`, `prompt_version`, `attempts`,
  `last_attempt_at`), backfilled from the newest existing `ai_outputs` row
  per pair so nothing reprocesses on upgrade; `ai_outputs_latest` view
  (newest `output_id` per (doc, task)).
- `etl/loader.py`: `get_unprocessed_docs` queues off `doc_task_state`
  (missing row OR status='failed'); `save_ai_output` upserts a 'done' state
  row in the same transaction; new `upsert_task_state` (static, for engines
  managing their own transaction) and `mark_task_failed`.
- `scheduler/job_runner.py`: the targets / claims / propaganda transport-
  failure paths call `mark_task_failed` instead of persisting nothing — the
  doc still re-queues (unchanged audit A-3 contract), but retries are now
  visible as `attempts` / `last_attempt_at` instead of silent.
- `engine/citation_extractor.py`: the per-doc processed marker is a
  `doc_task_state` upsert in the same per-doc transaction as the edge
  writes. It no longer writes fake `ai_outputs` rows (`task_type='citations'`,
  `{"edges_written": N}`) — that was work-queue state masquerading as an
  analysis result. Pre-existing marker rows are left in place and inert.
- One-row-per-(doc, task) readers moved to `ai_outputs_latest`: all
  aggregators (`base.fetch_task_rows`, `base.get_bot_flagged_doc_ids`,
  sentiment via base, bot, outlet, movers, narrative, propaganda),
  `entity_posts` (its hand-rolled `MAX(output_id)` subquery `_LATEST_ROW_SQL`
  is deleted — the view is the single implementation now), the
  narrative-clusterer pending-claims scan, the author bot rollup, the review
  queue sampler, and `review.get_stats` totals. `review.submit`'s lookup by
  `output_id` stays on the base table so evals can reference any historical
  row (golden sets survive reprocessing).

## Why

- Row-existence-as-state forced every stage to encode "done vs retry vs
  empty" through the shape of its output rows: failures had to persist
  nothing (invisible retries), and deterministic stages wrote marker rows.
- Bumping a prompt version silently left the whole existing corpus on the
  old version; forcing a re-run meant deleting `ai_outputs` rows, destroying
  exactly the audit history the table exists to preserve (invariant B1).
- Aggregators assumed exactly one row per (doc, task) while `entity_posts`
  already deduped — two code paths disagreeing about the same data. The view
  makes latest-row semantics the single shared contract before reprocessing
  makes duplicates legal.

## Behavior notes

- 'failed' state rows re-queue forever, same as the old persist-nothing
  behavior — no retry cap was added. The difference is observability.
- Bot pre-exclusion rows (label='human', inference_method='deterministic')
  still write real `ai_outputs` rows: they are policy verdicts consumed by
  aggregate denominators, not queue markers.
- The text stage still queues on task_type='sentiment' and writes both
  sentiment and favorability rows; both now get state rows.

## Follow-ups

- Data-shape survey (this initiative's second half) flagged: bot verdict
  stored under two JSON keys (`label` vs `is_bot`), `label` bucketing forced
  into Python (no promoted column), `author_bot_scores` rollup drift risk,
  narrative_citations XOR enforced in code but not schema, write-only
  narrative clustering-audit columns, and read-time target-entity
  resolution. Untracked until prioritized — see PR discussion.
