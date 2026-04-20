"""
Tests for analysis.src.etl.refresh_accounts (walkthrough 037).

All tests run offline — we feed fixture HTML directly to the parser and
construct fake ``ScrapedMember`` lists for the merge tests. No network calls.
"""

import os
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

import yaml

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.etl.refresh_accounts import (
    ScrapedMember,
    parse_members_html,
    merge_members_into_yaml,
    write_atomic_yaml,
    load_yaml,
    _normalize_name,
    _extract_handle,
)


# A deliberately minimal fixture that mimics the UCSD libguide's two-tab table
# structure. The parser should walk every <table> and pick up rows whose first
# cell links to x.com / twitter.com.
SENATORS_FIXTURE = """
<html><body>
  <div class="s-lib-tab" id="s-lib-ctab-25829352-0">
    <table class="s-lg-tabs-container">
      <thead><tr><th>Senator</th><th>State</th><th>Party</th></tr></thead>
      <tbody>
        <tr>
          <td><a href="https://x.com/SenAlsobrooks">Alsobrooks, Angela</a></td>
          <td>MD</td>
          <td>D</td>
        </tr>
        <tr>
          <td><a href="https://twitter.com/SenatorBaldwin">Baldwin, Tammy</a></td>
          <td>WI</td>
          <td>D</td>
        </tr>
      </tbody>
    </table>
  </div>
  <div class="s-lib-tab" id="s-lib-ctab-25829352-1">
    <table>
      <tbody>
        <tr>
          <td><a href="https://x.com/SenToddYoung/">Young, Todd</a></td>
          <td>IN</td>
          <td>R</td>
        </tr>
        <!-- Row with no X link — should be skipped -->
        <tr>
          <td>Ghost, Nobody</td>
          <td>XX</td>
          <td>?</td>
        </tr>
      </tbody>
    </table>
  </div>
</body></html>
"""

REPS_FIXTURE = """
<html><body>
  <table>
    <thead><tr><th>Representative</th><th>State</th><th>Party</th></tr></thead>
    <tbody>
      <tr>
        <td><a href="https://x.com/RepAdams">Adams, Alma</a></td>
        <td>NC</td>
        <td>D</td>
      </tr>
      <tr>
        <td><a href="https://x.com/Robert_Aderholt">Aderholt, Robert</a></td>
        <td>AL</td>
        <td>R</td>
      </tr>
    </tbody>
  </table>
</body></html>
"""


class TestHelpers(unittest.TestCase):
    def test_normalize_name_last_first(self):
        first, last, display = _normalize_name("Adams, Alma")
        self.assertEqual(first, "Alma")
        self.assertEqual(last, "Adams")
        self.assertEqual(display, "Alma Adams")

    def test_normalize_name_plain(self):
        first, last, display = _normalize_name("Pete Hegseth")
        self.assertEqual(first, "Pete")
        self.assertEqual(last, "Hegseth")

    def test_extract_handle_x_com(self):
        handle, url = _extract_handle("https://x.com/SenAlsobrooks")
        self.assertEqual(handle, "SenAlsobrooks")
        self.assertEqual(url, "https://x.com/SenAlsobrooks")

    def test_extract_handle_twitter_com(self):
        handle, url = _extract_handle("https://twitter.com/SenatorBaldwin/")
        self.assertEqual(handle, "SenatorBaldwin")
        # Normalized to x.com form regardless of source.
        self.assertEqual(url, "https://x.com/SenatorBaldwin")

    def test_extract_handle_non_x_returns_none(self):
        self.assertEqual(_extract_handle("https://senate.gov/foo"), (None, None))
        self.assertEqual(_extract_handle(""), (None, None))


class TestParseMembersHtml(unittest.TestCase):
    def test_parse_senators_extracts_three_rows(self):
        members = parse_members_html(SENATORS_FIXTURE, chamber="senate")
        self.assertEqual(len(members), 3)
        handles = {m.handle for m in members}
        self.assertEqual(handles, {"SenAlsobrooks", "SenatorBaldwin", "SenToddYoung"})
        for m in members:
            self.assertEqual(m.chamber, "senate")

    def test_parse_senators_skips_rows_without_x_link(self):
        members = parse_members_html(SENATORS_FIXTURE, chamber="senate")
        self.assertNotIn("Ghost", {m.last_name for m in members})

    def test_parse_senators_state_and_party(self):
        members = parse_members_html(SENATORS_FIXTURE, chamber="senate")
        by_handle = {m.handle: m for m in members}
        self.assertEqual(by_handle["SenAlsobrooks"].state, "MD")
        self.assertEqual(by_handle["SenAlsobrooks"].party, "D")
        self.assertEqual(by_handle["SenToddYoung"].state, "IN")
        self.assertEqual(by_handle["SenToddYoung"].party, "R")

    def test_parse_reps_extracts_two_rows(self):
        members = parse_members_html(REPS_FIXTURE, chamber="house")
        self.assertEqual(len(members), 2)
        handles = {m.handle for m in members}
        self.assertEqual(handles, {"RepAdams", "Robert_Aderholt"})
        self.assertEqual(members[0].chamber, "house")

    def test_parse_name_is_first_name_first(self):
        members = parse_members_html(REPS_FIXTURE, chamber="house")
        by_handle = {m.handle: m for m in members}
        self.assertEqual(by_handle["RepAdams"].name, "Alma Adams")
        self.assertEqual(by_handle["RepAdams"].first_name, "Alma")
        self.assertEqual(by_handle["RepAdams"].last_name, "Adams")

    def test_parse_chamber_param_validated(self):
        with self.assertRaises(ValueError):
            parse_members_html(SENATORS_FIXTURE, chamber="oligarchy")


class TestMergeMembersIntoYaml(unittest.TestCase):
    def _seed_yaml(self) -> dict:
        # An existing YAML with one house entry that has a district number,
        # one that will be "removed" (no longer in scrape), one that will be
        # unchanged, and one executive_branch entry we want preserved.
        return {
            "schema_version": 1,
            "generated_at_utc": "2026-04-01T00:00:00Z",
            "accounts": {
                "executive_branch": [
                    {"name": "Donald J. Trump", "office": "President",
                     "branch": "Executive",
                     "accounts": [{"platform": "x", "handle": "@POTUS"}]},
                ],
                "congress": {
                    "senate": [],
                    "house": [
                        {
                            "name": "Alma Adams",
                            "handle": "@RepAdams",
                            "chamber": "House",
                            "state_or_district": "NC12",  # district we want preserved
                            "party": "D",
                            "source_status": "source_file",
                        },
                        {
                            "name": "Ghost Member",
                            "handle": "@RepGhost",
                            "chamber": "House",
                            "state_or_district": "CA99",
                            "party": "R",
                            "source_status": "source_file",
                        },
                    ],
                },
            },
            "counts": {"executive_branch": 1, "house": 2, "senate": 0, "total_entries": 3},
            "affiliated": [{"handle": "GOP", "notes": "RNC"}],
        }

    def test_merge_preserves_district_when_state_matches(self):
        existing = self._seed_yaml()
        scraped = [
            ScrapedMember(name="Alma Adams", first_name="Alma", last_name="Adams",
                          chamber="house", handle="RepAdams",
                          url="https://x.com/RepAdams", state="NC", party="D"),
        ]
        merged, diff = merge_members_into_yaml(existing, scraped, chamber="house")
        house = merged["accounts"]["congress"]["house"]
        self.assertEqual(len(house), 1)
        self.assertEqual(house[0]["state_or_district"], "NC12")
        self.assertEqual(diff.preserved_districts, 1)

    def test_merge_drops_member_not_in_scrape(self):
        existing = self._seed_yaml()
        scraped = [
            ScrapedMember(name="Alma Adams", first_name="Alma", last_name="Adams",
                          chamber="house", handle="RepAdams",
                          url="https://x.com/RepAdams", state="NC", party="D"),
        ]
        _, diff = merge_members_into_yaml(existing, scraped, chamber="house")
        self.assertEqual(len(diff.removed), 1)
        self.assertIn("RepGhost", diff.removed[0])

    def test_merge_adds_new_member(self):
        existing = self._seed_yaml()
        scraped = [
            ScrapedMember(name="Alma Adams", first_name="Alma", last_name="Adams",
                          chamber="house", handle="RepAdams",
                          url="https://x.com/RepAdams", state="NC", party="D"),
            ScrapedMember(name="New Person", first_name="New", last_name="Person",
                          chamber="house", handle="RepNew",
                          url="https://x.com/RepNew", state="TX", party="R"),
        ]
        merged, diff = merge_members_into_yaml(existing, scraped, chamber="house")
        self.assertEqual(len(diff.added), 1)
        self.assertIn("RepNew", diff.added[0])
        handles = {h["handle"].lstrip("@") for h in merged["accounts"]["congress"]["house"]}
        self.assertEqual(handles, {"RepAdams", "RepNew"})

    def test_merge_preserves_executive_branch_and_affiliated(self):
        existing = self._seed_yaml()
        scraped = [
            ScrapedMember(name="Alma Adams", first_name="Alma", last_name="Adams",
                          chamber="house", handle="RepAdams",
                          url="https://x.com/RepAdams", state="NC", party="D"),
        ]
        merged, _ = merge_members_into_yaml(existing, scraped, chamber="house")
        # Executive branch + affiliated should be untouched
        self.assertEqual(
            merged["accounts"]["executive_branch"],
            existing["accounts"]["executive_branch"],
        )
        self.assertEqual(merged["affiliated"], existing["affiliated"])

    def test_merge_updates_counts(self):
        existing = self._seed_yaml()
        scraped_house = [
            ScrapedMember(name="Alma Adams", first_name="Alma", last_name="Adams",
                          chamber="house", handle="RepAdams",
                          url="https://x.com/RepAdams", state="NC", party="D"),
        ]
        merged, _ = merge_members_into_yaml(existing, scraped_house, chamber="house")
        self.assertEqual(merged["counts"]["house"], 1)
        self.assertEqual(merged["counts"]["senate"], 0)
        # total = exec(1) + house(1) + senate(0)
        self.assertEqual(merged["counts"]["total_entries"], 2)

    def test_merge_reports_party_change(self):
        existing = self._seed_yaml()
        scraped = [
            ScrapedMember(name="Alma Adams", first_name="Alma", last_name="Adams",
                          chamber="house", handle="RepAdams",
                          url="https://x.com/RepAdams", state="NC", party="R"),  # flipped
        ]
        _, diff = merge_members_into_yaml(existing, scraped, chamber="house")
        self.assertTrue(any("party" in c for c in diff.changed),
                        f"expected party change in diff.changed, got {diff.changed}")


class TestAtomicWrite(unittest.TestCase):
    def test_roundtrip_preserves_payload(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "out.yaml"
            payload = {
                "schema_version": 1,
                "accounts": {
                    "congress": {
                        "house": [{"name": "A", "handle": "@A"}],
                        "senate": [],
                    },
                },
            }
            write_atomic_yaml(path, payload)
            loaded = load_yaml(path)
            self.assertEqual(loaded, payload)

    def test_atomic_write_uses_temp_file(self):
        with tempfile.TemporaryDirectory() as td:
            path = Path(td) / "out.yaml"
            write_atomic_yaml(path, {"k": "v"})
            # temp file should be cleaned up by os.replace
            self.assertFalse((path.with_suffix(path.suffix + ".tmp")).exists())
            self.assertTrue(path.exists())


if __name__ == "__main__":
    unittest.main()
