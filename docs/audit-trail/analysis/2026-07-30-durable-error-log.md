# 2026-07-30 — Durable error log: ops.error_log + record_error()

Every layer now has a durable sink for errors and exceptions:
`ops.error_log` (migration `0009_error_log.sql`), written by
`analysis/src/common/error_log.py::record_error()` on the Python side and
by the Go ingest layer's slog mirror (see the ingestion entry, same date).
Before this, the only durable error records were `ops.task_queue.last_error`
(one overwritten column), `ops.pipeline_runs.stage_summary` (a joined
string), and `analysis.runs.error`; ETL drops, FastAPI 500s, and all
Reddit/X ingest failures lived only in container stdout/stderr under a
30 MB rotating Docker cap. Nothing anywhere captured a traceback.

## What shipped

- `data/pg-migrations/0009_error_log.sql`: one table, both writers
  (`source` = analysis | api | ingest), full Python tracebacks, nullable
  doc_id/task/pipeline_run_id with NO foreign keys (an error writer must
  never fail because its referent is missing), `context` JSONB for small
  extras. Indexed on `occurred_at` and `(source, occurred_at)`.
- `common/error_log.py::record_error()`: never raises — on any failure
  (pool unopened, DB down, cap hit) it falls back to stdout logging with
  the traceback and returns. Process-wide rate cap: past 200 rows per
  rolling hour it writes one marker row and goes stdout-only, so an error
  storm cannot bloat the small prod disk. Thread-safe via the shared pool.
- Wiring: `scheduler/stages.py` (per-doc pre-run raise, worker-thread
  abort, global-stage failure), `scheduler/pipeline.py` (escaped pipeline
  exception, with pipeline_run_id), the five engine failure handlers
  (text/claims/propaganda/targets/bot — the only place engine tracebacks
  are captured; `analysis.runs.error` still gets `str(exc)`),
  `etl/documents.py` extraction failures (a doc silently dropping from the
  corpus is now durably recorded with its raw_hash), the three LLM-backend
  embed swallows (the real reason clustering is incomplete —
  `clustering_runs.embedding_failures` only keeps a count), and the Gemini
  client-init swallow (the pipeline otherwise runs on with the LLM
  silently disabled).
- Retention: `_prune_error_log()` at pipeline start deletes rows older
  than 30 days, in its own try/except so pruning can never block a run.
  The analyze timer makes this run every 6 hours in prod.
- Adjacent fail-loud fix: `_MARK_DONE_SQL` now sets `last_error = NULL`,
  so a doc that succeeds on retry stops advertising its stale failure.
- Crawl-frontier fetch failures deliberately do NOT route here — already
  durable in `raw.pages.last_error`, and they are the thousands-of-rows
  storm source the rate cap should never have to absorb.

Cross-links: `api/2026-07-30-durable-error-log.md` (catch-all handler +
admin listing), `ingestion/2026-07-30-slog-error-log.md` (Go side),
`ui/2026-07-30-error-boundary.md`.

## Why

- Prod errors were undebuggable after the fact: Docker's json-file driver
  keeps 3x10 MB per container, so by the time a bad analyze run was
  noticed, its stderr was usually gone. `str(exc)` without a traceback was
  the best surviving record even inside the retention window.
- Postgres is the system's single source of truth and the box has no room
  for a log-shipping stack; one table with the same dual-writer ownership
  `ops` already has (Go: x_api_budget; Python: task_queue/pipeline_runs)
  is the smallest durable design.

## Follow-ups

- Deferred on purpose (simplicity-first): severity column, dedup of
  repeated errors, a client-error beacon endpoint, a Review-tab errors
  panel. None are built; none are promised.
