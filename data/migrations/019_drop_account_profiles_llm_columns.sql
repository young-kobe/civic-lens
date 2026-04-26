-- 019_drop_account_profiles_llm_columns.sql
-- Drop the two account_profiles columns the LLM classifier path used to
-- write — `confidence` (its 0..1 self-reported certainty) and `reasoning`
-- (its free-text justification). Both have been NULL on every row written
-- since the LLM classifier was removed on 2026-04-25; the curated YAML
-- loader never populated them.
--
-- No external query reads either column (verified via grep over the repo:
-- narrative.py only selects tier + faction columns). Tier identification
-- now flows through entity_registry (verified_officials.yaml) for officials
-- and the curated YAML loader for the wider elected/affiliated registry.

BEGIN TRANSACTION;

ALTER TABLE account_profiles DROP COLUMN confidence;
ALTER TABLE account_profiles DROP COLUMN reasoning;

COMMIT;

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (19, strftime('%s', 'now'));
