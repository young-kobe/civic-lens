-- 025_target_mentions.sql
-- Normalized (doc, target) stance mentions with identity resolved at WRITE
-- time. Until now the target list lived only inside
-- ai_outputs.output_json and was resolved against the entity registry on
-- every aggregation: unresolved mentions were dropped (only a counter
-- survived), and a registry edit silently remapped history. Each extracted
-- target now lands here once, with the registry resolution frozen as of
-- extraction; raw_target keeps the LLM's original string for audit, and
-- unresolved mentions persist with entity_key NULL instead of vanishing.
--
-- Rows are keyed to their ai_outputs row (output_id): readers join through
-- ai_outputs_latest, so a reprocessed doc's superseded mentions drop out of
-- aggregation automatically while remaining in the table as history.
--
-- Backfill happens in Python (loader.backfill_target_mentions, run by the
-- targets stage) — resolution needs the YAML registry, which SQL can't see.
-- Re-resolving under a changed registry is a deliberate maintenance action:
-- DELETE the affected rows and let the next targets run re-materialize them.

CREATE TABLE IF NOT EXISTS target_mentions (
    mention_id INTEGER PRIMARY KEY AUTOINCREMENT,
    output_id INTEGER NOT NULL,
    doc_id INTEGER NOT NULL,
    raw_target TEXT NOT NULL,
    entity_key TEXT,
    entity_kind TEXT CHECK(entity_kind IN ('official', 'collective')),
    entity_party TEXT,
    stance TEXT NOT NULL CHECK(stance IN ('positive', 'negative', 'neutral', 'mixed')),
    topic TEXT,
    confidence REAL NOT NULL,
    evidence_json TEXT,
    created_at INTEGER NOT NULL,
    FOREIGN KEY(output_id) REFERENCES ai_outputs(output_id),
    FOREIGN KEY(doc_id) REFERENCES docs(doc_id)
);

CREATE INDEX IF NOT EXISTS idx_target_mentions_output ON target_mentions(output_id);
CREATE INDEX IF NOT EXISTS idx_target_mentions_doc ON target_mentions(doc_id);
CREATE INDEX IF NOT EXISTS idx_target_mentions_entity ON target_mentions(entity_key);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (25, strftime('%s', 'now'));
