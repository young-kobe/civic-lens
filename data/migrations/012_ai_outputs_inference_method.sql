-- 012_ai_outputs_inference_method.sql
-- Adds inference_method to ai_outputs so audits can tell an LLM classification
-- apart from a heuristic-fallback result (walkthrough 038).
--
-- Without this, a heuristic fallback emitted on LLM failure is indistinguishable
-- from a confident LLM call in the database — which quietly violates the
-- traceability invariant (B2) once the pipeline is under load and LLM calls
-- start dropping. New writes populate this column; historical rows (pre-012)
-- stay NULL and are understood as "unknown — pre-dates the column".
--
-- Allowed values:
--   'llm'            — classification came from the LLM response (post-validation).
--   'heuristic'      — LLM unavailable / threw / failed schema validation; the
--                      deterministic fallback produced the row.
--   'deterministic'  — no LLM path exists for this task (e.g. citations).
--   NULL             — historical pre-migration row.

ALTER TABLE ai_outputs ADD COLUMN inference_method TEXT
    CHECK(inference_method IS NULL OR inference_method IN ('llm', 'heuristic', 'deterministic'));

CREATE INDEX IF NOT EXISTS idx_ai_outputs_method ON ai_outputs(inference_method);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (12, strftime('%s', 'now'));
