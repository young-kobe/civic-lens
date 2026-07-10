-- 023_ai_outputs_label.sql
-- Promote the primary categorical result to a real, indexed column.
--
-- Until now the verdict of a row lived only inside output_json, in
-- task-specific (and for bot_detection, doubled) encodings: readers had to
-- parse JSON in Python or json_extract in un-indexable WHERE clauses, and
-- the bot verdict existed as BOTH $.label and $.is_bot, forcing every
-- reader to check two keys. `label` is now the single canonical encoding:
--   sentiment      -> POSITIVE | NEGATIVE | NEUTRAL | MIXED
--   bot_detection  -> bot | suspicious | human | unknown
--   favorability   -> favorable | unfavorable | neutral | mixed
-- Tasks without a scalar verdict (claims, target_sentiment, propaganda)
-- leave it NULL. output_json remains the full audit payload; label is a
-- projection of it, written by loader.save_ai_output.

ALTER TABLE ai_outputs ADD COLUMN label TEXT;

UPDATE ai_outputs SET label = CASE task_type
    WHEN 'sentiment' THEN json_extract(output_json, '$.label')
    WHEN 'favorability' THEN json_extract(output_json, '$.overall_gop_stance')
    WHEN 'bot_detection' THEN COALESCE(
        json_extract(output_json, '$.label'),
        -- Oldest bot rows carried only the boolean encoding.
        CASE json_extract(output_json, '$.is_bot')
            WHEN 1 THEN 'bot'
            WHEN 0 THEN 'human'
        END
    )
END
WHERE task_type IN ('sentiment', 'favorability', 'bot_detection');

CREATE INDEX IF NOT EXISTS idx_ai_outputs_task_label ON ai_outputs(task_type, label);

-- Recreate the latest-row view: its SELECT * column list was captured when
-- migration 022 created it, before `label` existed.
DROP VIEW IF EXISTS ai_outputs_latest;
CREATE VIEW ai_outputs_latest AS
SELECT a.*
FROM ai_outputs a
WHERE a.output_id = (
    SELECT MAX(a2.output_id) FROM ai_outputs a2
    WHERE a2.doc_id = a.doc_id AND a2.task_type = a.task_type
);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (23, strftime('%s', 'now'));
