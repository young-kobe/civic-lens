-- 022_doc_task_state.sql
-- Decouples the pipeline work queue from the ai_outputs data product.
--
-- Before this, "has doc X been processed for task Y" was answered by row
-- existence in ai_outputs. That forced three hacks: transport failures had to
-- persist nothing to re-queue (invisible retries, audit A-3), deterministic
-- stages wrote fake marker rows (citations), and reprocessing a task under a
-- new prompt_version required DELETEing audit rows.
--
-- doc_task_state is now the work queue: one row per (doc, task), status
-- 'done' or 'failed' ('failed' rows still re-queue; they exist for
-- observability — attempt counts and timestamps). ai_outputs becomes an
-- append-only log of actual analysis results. Reprocessing a task under a
-- new prompt is: DELETE FROM doc_task_state WHERE task_type = '<task>';
-- old output rows survive with their prompt_version.

CREATE TABLE IF NOT EXISTS doc_task_state (
    doc_id INTEGER NOT NULL,
    task_type TEXT NOT NULL,
    status TEXT NOT NULL CHECK(status IN ('done', 'failed')),
    prompt_version TEXT,
    attempts INTEGER NOT NULL DEFAULT 1,
    last_attempt_at INTEGER NOT NULL,
    PRIMARY KEY (doc_id, task_type),
    FOREIGN KEY (doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_doc_task_state_task ON doc_task_state(task_type, status);

-- Backfill: every (doc, task) that already has an output row is done.
-- Take prompt_version/created_at from the newest row per pair.
INSERT OR IGNORE INTO doc_task_state
    (doc_id, task_type, status, prompt_version, attempts, last_attempt_at)
SELECT a.doc_id, a.task_type, 'done', a.prompt_version, 1,
       COALESCE(a.created_at, strftime('%s', 'now'))
FROM ai_outputs a
WHERE a.output_id = (
    SELECT MAX(a2.output_id) FROM ai_outputs a2
    WHERE a2.doc_id = a.doc_id AND a2.task_type = a.task_type
);

-- Latest result per (doc, task). Readers that assume one row per pair must
-- read this view instead of ai_outputs — after a reprocess the base table
-- legitimately holds multiple rows per pair (one per prompt_version run).
CREATE VIEW IF NOT EXISTS ai_outputs_latest AS
SELECT a.*
FROM ai_outputs a
WHERE a.output_id = (
    SELECT MAX(a2.output_id) FROM ai_outputs a2
    WHERE a2.doc_id = a.doc_id AND a2.task_type = a.task_type
);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (22, strftime('%s', 'now'));
