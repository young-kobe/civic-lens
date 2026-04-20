-- 009_rename_narrative_origin_column.sql
-- Renames narratives.origin_doc_id -> narratives.first_seen_doc_id.
--
-- Motivation: the column recorded "first doc WE ingested that carried this
-- claim", not "where the claim originated in the world". The old name implied
-- the latter and was patched over with UI disclaimers. Walkthrough 035
-- narrows the product goal to "sampled discourse with narrative overlay" and
-- renames this field end-to-end so the schema, API, and UI all tell the
-- same honest story.
--
-- Semantics are unchanged. Foreign keys and indexes are preserved by
-- ALTER TABLE RENAME COLUMN.

ALTER TABLE narratives RENAME COLUMN origin_doc_id TO first_seen_doc_id;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (9, strftime('%s', 'now'));
