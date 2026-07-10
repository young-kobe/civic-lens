# 2026-07-10 — CIVIC_RUN_ANALYSIS_ON documented in env templates

`CIVIC_RUN_ANALYSIS_ON` defaults to `social_media`
(analysis/src/common/settings.py) and gates every LLM stage in job_runner —
so a deploy that never sets it analyzes only Reddit + X, news docs never
receive `ai_outputs` rows, and the `ai_outputs JOIN docs` inner join in
every aggregator silently empties the dashboard's news surfaces. This is
why prod's news column was empty: the variable appeared in no deploy file.

Now set to `all` (with a warning comment) in:

- `.env.example` — new "Analysis scope" section.
- `docs/deployment/plan.md` — the `/etc/civic-lens.env` template block.

Manual prod step (tracked in `docs/todos/news-visibility-prod.md`): add
`CIVIC_RUN_ANALYSIS_ON=all` to `/etc/civic-lens.env`, re-run analyze (the
per-task backfill re-queues stored news docs), verify
`SELECT COUNT(*) FROM ai_outputs a JOIN docs d ON a.doc_id=d.doc_id WHERE
d.source_type='news'` rises above 0.

Cross-link: `docs/audit-trail/analysis/2026-07-10-entity-accounts-and-drilldown-reads.md`.
