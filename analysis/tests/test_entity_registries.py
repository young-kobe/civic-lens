"""
Schema tests for the entity registries added in walkthrough 054
(Phase 2 of the UI Redesign Plan).

Validates that:
  * All three YAMLs parse.
  * Every entry has the required fields per docs/ui-redesign-plan.md.
  * Enumerated fields (partisan_lean, tilt, party) use only allowed values.
  * Blurbs are neither missing nor boilerplate-short.
  * Domains / handles / subreddit names are unique within each file
    (catches copy-paste duplication).
  * Verified-officials handles are consistent in formatting (leading @).

Run: python -m unittest analysis.tests.test_entity_registries
"""

import os
import sys
import unittest

import yaml as yaml_lib

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

DATA_DIR = os.path.join(project_root, "data")

OUTLETS_PATH = os.path.join(DATA_DIR, "news_outlets.yaml")
OFFICIALS_PATH = os.path.join(DATA_DIR, "verified_officials.yaml")
SUBREDDITS_PATH = os.path.join(DATA_DIR, "major_subreddits.yaml")

ALLOWED_LEANS = {"left", "center-left", "center", "center-right", "right", "mixed"}
ALLOWED_TILTS = {"left", "center", "right", "mixed"}
ALLOWED_PARTIES = {"R", "D", "I", "independent-dem", "other"}

# Terse but not empty. If someone writes "n/a" we want to catch it.
MIN_BLURB_LEN = 80
MAX_BLURB_LEN = 1000


def _load_yaml(path: str):
    with open(path, "r", encoding="utf-8") as f:
        return yaml_lib.safe_load(f)


class NewsOutletsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = _load_yaml(OUTLETS_PATH)
        cls.entries = cls.data["outlets"]

    def test_count_matches_plan(self):
        self.assertEqual(
            len(self.entries), 20,
            "Phase 2 of the UI Redesign Plan requires exactly 20 outlets; "
            "see docs/ui-redesign-plan.md 'Outlet coverage list'."
        )

    def test_required_fields_present(self):
        required = {
            "domain", "also_domains", "display_name", "blurb",
            "partisan_lean", "lean_source", "owner",
        }
        for entry in self.entries:
            missing = required - entry.keys()
            self.assertFalse(
                missing,
                f"{entry.get('display_name', '<unknown>')} missing fields: {missing}",
            )

    def test_lean_values_allowed(self):
        for entry in self.entries:
            self.assertIn(
                entry["partisan_lean"], ALLOWED_LEANS,
                f"{entry['display_name']} has invalid partisan_lean: {entry['partisan_lean']!r}",
            )

    def test_lean_has_citation(self):
        for entry in self.entries:
            self.assertTrue(
                entry.get("lean_source"),
                f"{entry['display_name']} missing lean_source citation",
            )

    def test_blurbs_reasonable_length(self):
        for entry in self.entries:
            blurb = (entry.get("blurb") or "").strip()
            self.assertGreaterEqual(
                len(blurb), MIN_BLURB_LEN,
                f"{entry['display_name']} blurb too short ({len(blurb)} chars)",
            )
            self.assertLessEqual(
                len(blurb), MAX_BLURB_LEN,
                f"{entry['display_name']} blurb too long ({len(blurb)} chars)",
            )

    def test_domains_unique(self):
        seen = set()
        for entry in self.entries:
            domain = entry["domain"].lower()
            self.assertNotIn(domain, seen, f"Duplicate domain: {domain}")
            seen.add(domain)

    def test_domains_well_formed(self):
        for entry in self.entries:
            domain = entry["domain"]
            # No scheme, no trailing slash, lowercase, at least one dot.
            self.assertFalse(
                domain.startswith("http"),
                f"{entry['display_name']} domain should not include scheme: {domain!r}",
            )
            self.assertFalse(
                domain.endswith("/"),
                f"{entry['display_name']} domain should not have trailing slash: {domain!r}",
            )
            self.assertIn(
                ".", domain,
                f"{entry['display_name']} domain missing TLD: {domain!r}",
            )
            self.assertEqual(
                domain, domain.lower(),
                f"{entry['display_name']} domain should be lowercase: {domain!r}",
            )

    def test_founded_year_if_present(self):
        for entry in self.entries:
            if "founded" not in entry:
                continue
            founded = entry["founded"]
            self.assertIsInstance(founded, int, f"{entry['display_name']} founded must be int")
            self.assertGreaterEqual(founded, 1600)
            self.assertLessEqual(founded, 2100)


class VerifiedOfficialsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = _load_yaml(OFFICIALS_PATH)
        cls.entries = cls.data["officials"]

    def test_count_matches_plan(self):
        self.assertEqual(
            len(self.entries), 16,
            "Phase 2 of the UI Redesign Plan requires exactly 16 officials; "
            "see docs/ui-redesign-plan.md 'Officials coverage list'."
        )

    def test_required_fields_present(self):
        required = {
            "handle", "display_name", "office", "party",
            "blurb", "term_start", "bio_source",
        }
        for entry in self.entries:
            missing = required - entry.keys()
            self.assertFalse(
                missing,
                f"{entry.get('display_name', '<unknown>')} missing fields: {missing}",
            )

    def test_handles_start_with_at(self):
        for entry in self.entries:
            handle = entry["handle"]
            self.assertTrue(
                handle.startswith("@"),
                f"{entry['display_name']} handle must start with @: {handle!r}",
            )
            for also in entry.get("also_handles") or []:
                self.assertTrue(
                    also.startswith("@"),
                    f"{entry['display_name']} also_handle must start with @: {also!r}",
                )

    def test_handles_unique(self):
        seen = set()
        for entry in self.entries:
            primary = entry["handle"].lower()
            self.assertNotIn(primary, seen, f"Duplicate primary handle: {primary}")
            seen.add(primary)

    def test_party_values_allowed(self):
        for entry in self.entries:
            self.assertIn(
                entry["party"], ALLOWED_PARTIES,
                f"{entry['display_name']} has invalid party: {entry['party']!r}",
            )

    def test_term_start_iso_format(self):
        for entry in self.entries:
            term_start = str(entry["term_start"])
            # yaml may auto-parse dates into datetime.date; str() gives ISO back.
            self.assertRegex(
                term_start, r"^\d{4}-\d{2}-\d{2}$",
                f"{entry['display_name']} term_start not ISO date: {term_start!r}",
            )

    def test_blurbs_reasonable_length(self):
        for entry in self.entries:
            blurb = (entry.get("blurb") or "").strip()
            self.assertGreaterEqual(
                len(blurb), MIN_BLURB_LEN,
                f"{entry['display_name']} blurb too short ({len(blurb)} chars)",
            )
            self.assertLessEqual(
                len(blurb), MAX_BLURB_LEN,
                f"{entry['display_name']} blurb too long ({len(blurb)} chars)",
            )

    def test_bio_source_is_url(self):
        for entry in self.entries:
            src = entry["bio_source"]
            self.assertTrue(
                src.startswith("http://") or src.startswith("https://"),
                f"{entry['display_name']} bio_source must be a URL: {src!r}",
            )


class MajorSubredditsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = _load_yaml(SUBREDDITS_PATH)
        cls.entries = cls.data["subreddits"]

    def test_count_matches_plan(self):
        self.assertEqual(
            len(self.entries), 10,
            "Phase 2 of the UI Redesign Plan requires exactly 10 subreddits; "
            "see docs/ui-redesign-plan.md 'Subreddit coverage list'."
        )

    def test_required_fields_present(self):
        required = {"subreddit", "display_name", "blurb", "tilt", "tilt_source"}
        for entry in self.entries:
            missing = required - entry.keys()
            self.assertFalse(
                missing,
                f"{entry.get('display_name', '<unknown>')} missing fields: {missing}",
            )

    def test_subreddit_names_no_prefix(self):
        for entry in self.entries:
            name = entry["subreddit"]
            self.assertFalse(
                name.startswith("r/"),
                f"{entry['display_name']} subreddit should not have r/ prefix: {name!r}",
            )
            self.assertFalse(
                name.startswith("/r/"),
                f"{entry['display_name']} subreddit should not have /r/ prefix: {name!r}",
            )

    def test_subreddit_names_unique_case_insensitive(self):
        # Reddit usernames/subreddit names are case-preserving but not
        # case-sensitive on lookup. Enforce uniqueness on lowered form.
        seen = set()
        for entry in self.entries:
            name = entry["subreddit"].lower()
            self.assertNotIn(name, seen, f"Duplicate subreddit (case-insensitive): {name}")
            seen.add(name)

    def test_tilt_values_allowed(self):
        for entry in self.entries:
            self.assertIn(
                entry["tilt"], ALLOWED_TILTS,
                f"{entry['display_name']} has invalid tilt: {entry['tilt']!r}",
            )

    def test_blurbs_reasonable_length(self):
        for entry in self.entries:
            blurb = (entry.get("blurb") or "").strip()
            self.assertGreaterEqual(
                len(blurb), MIN_BLURB_LEN,
                f"{entry['display_name']} blurb too short ({len(blurb)} chars)",
            )
            self.assertLessEqual(
                len(blurb), MAX_BLURB_LEN,
                f"{entry['display_name']} blurb too long ({len(blurb)} chars)",
            )


if __name__ == "__main__":
    unittest.main()
