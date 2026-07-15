# 2026-07-14 — Narratives off by default; narrative/bot snapshots retired

The full pipeline now runs `etl, bot, text, targets, propaganda, citations, claims, accounts, bot_rollup, snapshots` by default. Narrative clustering is a legacy stage: still implemented, still runnable via `--tasks narratives`, its tables (`narratives`, `narrative_docs`) intact — but excluded from the default run and marked in code as a candidate for eventual removal. `save_snapshots()` no longer writes `narratives_{window}` or `bot_activity_{window}` cache keys.

## What shipped

- `analysis/src/scheduler/job_runner.py`: `narratives` removed from the default stage set (explicit `--tasks narratives` still works); narrative and bot-activity snapshot blocks removed from `save_snapshots()`; legacy note on `run_narrative_clustering`.
- `analysis/src/engine/narrative_clusterer.py`, `analysis/src/engine/bot.py`: module docstrings mark both as legacy surfaces, candidates for removal someday.
- Bot detection and `bot_rollup` keep running by default: `get_bot_flagged_doc_ids` feeds the sentiment aggregator's bot-exclusion, `author_bot_scores` feeds account rollups, and bot signals are a candidate amplification input for the upcoming disinformation tracker.
- Narrative overlays inside the sentiment/outlet aggregators remain (table-existence-guarded reads over tables that still exist); they simply stop receiving new narrative rows unless the stage is run explicitly.

## Why

- At current data volume narrative clusters and bot scores were too low-signal to publish; they are replaced by the accountability and disinformation trackers (`docs/todos/accountability-disinfo.md`), whose extracted-claims substrate (`task_type='claims'`) was previously consumed only by the narrative clusterer.

## Follow-ups

- Decide eventual removal or repurposing of the narrative stage once the disinformation tracker is live.
- Cross-links: `docs/audit-trail/ui/2026-07-14-retire-narratives-bots-tabs.md`, `docs/audit-trail/api/2026-07-14-retire-narratives-bots-endpoints.md`.
