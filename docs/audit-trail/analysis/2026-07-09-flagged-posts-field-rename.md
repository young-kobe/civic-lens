# 2026-07-09 — Rename bot overview `totalFlaggedAccounts` -> `totalFlaggedPosts`

The bot-activity overview exposed a field named `totalFlaggedAccounts`, but the value is
`bot_count + suspicious_count` computed over `ai_outputs.task_type = 'bot_detection'` rows joined
on `doc_id` — i.e. a count of flagged posts/documents, not distinct accounts. The name and the UI
label had drifted apart. Renamed the field to `totalFlaggedPosts` so the snapshot-cache contract
matches what it measures. Cross-links the UI entry
[`ui/2026-07-09-ui-comprehensibility.md`](../ui/2026-07-09-ui-comprehensibility.md).

## What shipped

- `reporting/models/aggregator_models.py`: `BotOverview.totalFlaggedPosts` (dataclass field +
  `to_dict()` key).
- `reporting/aggregators/bot.py`: `_format_bot_activity` populates `totalFlaggedPosts`.
- `scheduler/job_runner.py`: the two `save_snapshots` references (`doc_count=` and the
  `results[...]` summary) read the new field.
- UI side (`types.ts`, `services/fixtures.ts`, `pages/BotActivityProfiler.tsx`) tracks the rename;
  the ticker and card, which already read "posts", are now backed by an honestly named field.

## Why

- The audit flagged the Bot ticker showing this value as "Flagged Posts" while the field said
  "Accounts" — a crossed label/field pair. Confirmed against the aggregation query that the count
  is per bot-detection output row (one per doc), so the label was right and the field name was
  wrong. Renamed the field rather than the label.

## Follow-ups

- The snapshot cache key changes shape (`totalFlaggedPosts`); it repopulates on the next pipeline
  run. No migration needed — the cache is derived, not a source of truth.
