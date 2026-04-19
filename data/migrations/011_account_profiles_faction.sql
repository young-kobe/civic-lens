-- 011_account_profiles_faction.sql
-- Richer per-account metadata so the UI can render "Rep Adams (D, NC-12)",
-- "Sec. Hegseth (R, DoD)", or "Democratic PAC" next to narrative rows
-- (walkthrough 036 extension).
--
-- All new columns are nullable and populated by the curated loader when the
-- source YAML provides them. The LLM classifier leaves them null — the LLM
-- is classifying TIER, not faction metadata.

ALTER TABLE account_profiles ADD COLUMN full_name TEXT;

-- Party affiliation for individuals. Two-letter code when clean:
--   'D' | 'R' | 'I' | 'L' (Libertarian) | 'G' (Green) | NULL
ALTER TABLE account_profiles ADD COLUMN party TEXT;

-- Branch of government (or higher-level category for non-electeds):
--   'executive' | 'legislative' | 'judicial'
--   'party_org' | 'pac' | 'think_tank' | 'media' | 'strategist'
--   NULL for unclassified or general_public.
ALTER TABLE account_profiles ADD COLUMN branch TEXT;

-- Legislative chamber when branch='legislative':
--   'senate' | 'house' | NULL
ALTER TABLE account_profiles ADD COLUMN chamber TEXT;

-- State or district identifier:
--   'NY' (senator), 'CA33' (house district), NULL (not applicable)
ALTER TABLE account_profiles ADD COLUMN state_or_district TEXT;

-- Role / office title in plain English:
--   'President', 'Vice President', 'Senator', 'Representative',
--   'Secretary of Defense', 'Governor', 'Director of National Intelligence',
--   NULL when unknown.
ALTER TABLE account_profiles ADD COLUMN office_title TEXT;

-- Source YAML's account_type label for this handle:
--   'official' | 'official_role' | 'personal' | 'personal/political' |
--   'personal/official' | 'institutional' | 'previous/personal' | NULL
ALTER TABLE account_profiles ADD COLUMN account_type TEXT;

CREATE INDEX IF NOT EXISTS idx_account_profiles_party ON account_profiles(party);
CREATE INDEX IF NOT EXISTS idx_account_profiles_branch ON account_profiles(branch);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (11, strftime('%s', 'now'));
