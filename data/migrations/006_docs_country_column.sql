-- 006_docs_country_column.sql
-- Promote place_country_code from metadata_json JSON blob to a first-class column.
-- The geographic aggregator parses the JSON on every query; a real column plus
-- an index makes the heatmap path O(indexed lookup) instead of O(all X docs).

ALTER TABLE docs ADD COLUMN place_country_code TEXT;

-- Backfill existing X docs from metadata_json.
UPDATE docs
SET place_country_code = json_extract(metadata_json, '$.place_country_code')
WHERE source_type = 'x_post'
  AND metadata_json IS NOT NULL
  AND json_extract(metadata_json, '$.place_country_code') IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_docs_country ON docs(place_country_code);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (6, strftime('%s', 'now'));
