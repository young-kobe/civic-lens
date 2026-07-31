-- data/pg-migrations/0009_error_log.sql
--
-- Durable error log shared by every backend layer. Until now the only
-- durable error records were ops.task_queue.last_error (one overwritten
-- column), ops.pipeline_runs.stage_summary (a joined-string blob), and
-- analysis.runs.error; everything else -- ETL admission drops, FastAPI
-- 500s, Reddit/X ingest failures, Go DB-write failures -- went to
-- container stdout/stderr under a 30 MB rotating Docker json-file cap and
-- was gone. This table is the durable sink for all of them, written by
-- Python (analysis.src.common.error_log.record_error, plus the FastAPI
-- catch-all handler with source='api') and by Go (ingest/internal/errorlog,
-- a slog.Handler that mirrors Error-level records, source='ingest') --
-- the same dual-writer arrangement ops already has for task_queue and
-- x_api_budget.
--
-- Deliberately no foreign keys: an error writer must never fail because
-- its referent row is missing or already pruned. `task` is plain TEXT,
-- not the analysis.task enum, for the same reason. `context` makes this
-- the fourth JSONB column in the schema, superseding the "third and last
-- JSONB column" note on ops.pipeline_runs.stage_summary in 0001 -- same
-- justification as there: operator-facing diagnostics, never queried
-- relationally.
--
-- Volume safety is the writers' and the scheduler's job, not the schema's:
-- each writer rate-caps itself (falls back to stdout/stderr past 200 rows
-- per rolling hour), crawl-frontier fetch failures stay in
-- raw.pages.last_error and are never routed here, and the analysis
-- pipeline prunes rows older than 30 days at the start of every run.
--
-- Not idempotent by itself -- same one-shot-apply convention as every
-- other file in this directory (the Go migration runner tracks the
-- applied version in ops.schema_migrations, so this file is never
-- replayed).

CREATE TABLE ops.error_log (
    error_id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    source          TEXT NOT NULL,
    component       TEXT NOT NULL,
    message         TEXT NOT NULL,
    traceback       TEXT,
    doc_id          BIGINT,
    task            TEXT,
    pipeline_run_id BIGINT,
    context         JSONB
);

CREATE INDEX idx_error_log_occurred_at
    ON ops.error_log (occurred_at DESC);
CREATE INDEX idx_error_log_source_occurred
    ON ops.error_log (source, occurred_at DESC);

COMMENT ON TABLE ops.error_log IS
    'Durable errors/exceptions from every backend layer. Retention: the analysis pipeline deletes rows older than 30 days at run start. Inspect with: SELECT occurred_at, source, component, message FROM ops.error_log ORDER BY occurred_at DESC LIMIT 50; or GET /api/v1/admin/errors.';
COMMENT ON COLUMN ops.error_log.source IS
    'Which layer wrote the row: analysis (pipeline/ETL/engines), api (FastAPI), or ingest (Go).';
COMMENT ON COLUMN ops.error_log.component IS
    'Where in the layer: Python module name, Go component (e.g. runner.reddit), or "METHOD /path" for API rows.';
COMMENT ON COLUMN ops.error_log.message IS
    'One-line error text, usually str(exc) / err.Error().';
COMMENT ON COLUMN ops.error_log.traceback IS
    'Full Python traceback when available; NULL for Go rows (no traceback equivalent).';
COMMENT ON COLUMN ops.error_log.doc_id IS
    'corpus.documents reference when the error is per-doc. No FK on purpose: error rows must survive their referent.';
COMMENT ON COLUMN ops.error_log.task IS
    'Pipeline task name when the error came from a stage worker. Plain TEXT, not analysis.task, so writers can name anything.';
COMMENT ON COLUMN ops.error_log.pipeline_run_id IS
    'ops.pipeline_runs reference when the error occurred inside a pipeline run. No FK on purpose.';
COMMENT ON COLUMN ops.error_log.context IS
    'Small free-form extras (raw_hash, url, handle, batch size). Diagnostics only, never queried relationally.';
