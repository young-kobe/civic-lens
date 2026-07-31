# 2026-07-30 — API: unhandled 500s recorded durably; GET /admin/errors

The API layer participates in the durable error log
(`analysis/2026-07-30-durable-error-log.md`): a catch-all exception
handler in `api/server.py` writes every unhandled endpoint exception to
`ops.error_log` (`source='api'`, `component="METHOD /path"`) before
returning the 500. Starlette's ServerErrorMiddleware re-raises after the
handler, so uvicorn's stderr traceback behavior is unchanged — this adds
durability, it does not swallow. Previously the API had no exception
handling beyond slowapi's rate-limit handler and exactly one logger
(health); a 500's traceback existed only in rotating container stderr.

Reading the log: `GET /api/v1/admin/errors` (admin-token-gated, on the
existing admin router), newest 50 rows, optional `?source=` filter —
mirrors the `/pipeline-runs` listing shape. Models in `api/models/ops.py`
(`ErrorLogEntryModel` / `ErrorLogResponse`). The raw psql query is on the
table's COMMENT for box-side use.

The `/run/full-pipeline` BackgroundTasks path needs no wrapper: an escaped
`run_pipeline` exception is recorded by the scheduler's own wiring with
its pipeline_run_id.
