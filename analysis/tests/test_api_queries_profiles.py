"""
Tests for analysis/src/api/queries/profiles.py -- the EntityProfile
restoration (docs/audit-trail/api/2026-07-27-entity-profile-restoration.md).

Two tiers, matching the repo's established convention:

  1. Pure-core tests (no DB) -- `_map_entity_row`'s vocabulary mapping:
     editorial/non-editorial official, collective, outlet, subreddit, and
     the source_citation name trap (lean_source vs bio_source landing on
     the right model field depending on kind).
  2. An integration test gated on CIVIC_TEST_DATABASE_URL: applies
     migrations through 0007, asserts a known outlet's founded/
     circulation_note backfill landed, and that `fetch_entity_profiles`
     round-trips those values.
"""

from __future__ import annotations

import os
import unittest
from datetime import date

from analysis.src.api.queries import profiles
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

def _row(**overrides):
    base = {
        "entity_id": 1,
        "entity_key": "test-key",
        "kind": "official",
        "editorial": True,
        "display_name": "Test Entity",
        "blurb": "A blurb.",
        "role_title": None,
        "term_start": None,
        "owner": None,
        "source_citation": None,
        "lean_source": None,
        "founded": None,
        "circulation_note": None,
        "subscriber_count_proxy": None,
        "account_type": None,
    }
    base.update(overrides)
    return base


class EntityUiKindTests(unittest.TestCase):
    def test_editorial_official_maps_to_official(self):
        self.assertEqual(profiles._entity_ui_kind("official", True), "official")

    def test_non_editorial_official_maps_to_account(self):
        self.assertEqual(profiles._entity_ui_kind("official", False), "account")

    def test_collective_maps_to_official_regardless_of_editorial(self):
        self.assertEqual(profiles._entity_ui_kind("collective", True), "official")
        self.assertEqual(profiles._entity_ui_kind("collective", False), "official")

    def test_outlet_and_subreddit_pass_through(self):
        self.assertEqual(profiles._entity_ui_kind("outlet", True), "outlet")
        self.assertEqual(profiles._entity_ui_kind("subreddit", True), "subreddit")


class MapEntityRowTests(unittest.TestCase):
    def test_editorial_official_kind(self):
        model = profiles._map_entity_row(_row(kind="official", editorial=True))
        self.assertEqual(model.kind, "official")

    def test_non_editorial_official_kind(self):
        model = profiles._map_entity_row(_row(kind="official", editorial=False))
        self.assertEqual(model.kind, "account")

    def test_collective_kind(self):
        model = profiles._map_entity_row(_row(kind="collective", editorial=False))
        self.assertEqual(model.kind, "official")

    def test_outlet_lean_and_citation_mapping(self):
        row = _row(
            kind="outlet", editorial=True, entity_key="nytimes.com",
            lean_source="center-left", source_citation="AllSides 2024 (Lean Left)",
        )
        model = profiles._map_entity_row(row)
        self.assertEqual(model.kind, "outlet")
        # Name trap: model.lean <- lean_source (the curated string), NOT the
        # flattened corpus.entities.lean enum (never passed to this function).
        self.assertEqual(model.lean, "center-left")
        # model.lean_source is the CITATION, from source_citation.
        self.assertEqual(model.lean_source, "AllSides 2024 (Lean Left)")
        # bio_source/party are official/collective-only -- must stay None here.
        self.assertIsNone(model.bio_source)
        self.assertIsNone(model.party)

    def test_subreddit_lean_and_citation_mapping(self):
        row = _row(
            kind="subreddit", editorial=True, entity_key="politics",
            lean_source="left", source_citation="Community sidebar",
        )
        model = profiles._map_entity_row(row)
        self.assertEqual(model.kind, "subreddit")
        self.assertEqual(model.lean, "left")
        self.assertEqual(model.lean_source, "Community sidebar")
        self.assertIsNone(model.bio_source)

    def test_official_party_and_bio_source_mapping(self):
        row = _row(
            kind="official", editorial=True, entity_key="potus",
            lean_source="R", source_citation="whitehouse.gov/administration",
            role_title="President",
        )
        model = profiles._map_entity_row(row)
        # Name trap: model.party <- lean_source (party code), model.bio_source
        # <- source_citation. model.lean/model.lean_source stay None -- those
        # are outlet/subreddit-only fields.
        self.assertEqual(model.party, "R")
        self.assertEqual(model.bio_source, "whitehouse.gov/administration")
        self.assertIsNone(model.lean)
        self.assertIsNone(model.lean_source)
        self.assertEqual(model.office, "President")

    def test_collective_party_and_bio_source_mapping(self):
        row = _row(
            kind="collective", editorial=True, entity_key="thedemocrats",
            lean_source="D", source_citation="Party platform",
        )
        model = profiles._map_entity_row(row)
        self.assertEqual(model.party, "D")
        self.assertEqual(model.bio_source, "Party platform")

    def test_term_start_formatted_as_iso_date_string(self):
        row = _row(kind="official", editorial=True, term_start=date(2023, 1, 3))
        model = profiles._map_entity_row(row)
        self.assertEqual(model.term_start, "2023-01-03")

    def test_term_start_none_stays_none(self):
        model = profiles._map_entity_row(_row(kind="official", editorial=True))
        self.assertIsNone(model.term_start)

    def test_restored_columns_pass_through_as_is(self):
        row = _row(
            kind="outlet", editorial=True, founded=1851, circulation_note="~10M (proxy)",
        )
        model = profiles._map_entity_row(row)
        self.assertEqual(model.founded, 1851)
        self.assertEqual(model.circulation_note, "~10M (proxy)")

    def test_blurb_defaults_to_empty_string_not_none(self):
        model = profiles._map_entity_row(_row(blurb=None))
        self.assertEqual(model.blurb, "")


class SampledAccountProfileTests(unittest.TestCase):
    def test_kind_and_account_type(self):
        model = profiles.sampled_account_profile("someuser")
        self.assertEqual(model.kind, "account")
        self.assertEqual(model.account_type, "sampled")
        self.assertEqual(model.key, "someuser")
        self.assertEqual(model.display_name, "@someuser")

    def test_bio_folded_into_blurb(self):
        model = profiles.sampled_account_profile("someuser", "Some User", "A bio.")
        self.assertIn("A bio.", model.blurb)
        self.assertIn("Not in our tracked registries", model.blurb)


class CatchAllProfileTests(unittest.TestCase):
    def test_other_outlets_text_matches_pre_postgres_wording(self):
        model = profiles.catch_all_profile(profiles.CATCH_ALL_OUTLETS)
        self.assertEqual(model.display_name, "Other news outlets")
        self.assertEqual(
            model.blurb,
            "News docs whose domain is not in the tracked outlet registry.",
        )

    def test_other_x_users_text_matches_pre_postgres_wording(self):
        model = profiles.catch_all_profile(profiles.CATCH_ALL_X_USERS)
        self.assertEqual(model.display_name, "Other X users")

    def test_other_subreddits_text_matches_pre_postgres_wording(self):
        model = profiles.catch_all_profile(profiles.CATCH_ALL_SUBREDDITS)
        self.assertEqual(model.display_name, "Other subreddits")


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class FetchEntityProfilesIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        # seed=True: this test's assertions are against the real curated
        # nytimes.com row seeded by 0002_entity_registry_seed.sql plus the
        # 0007 backfill, not a hand-inserted fixture row.
        pg_fixture.reset_schema(cls._dsn, seed=True)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def test_0007_backfill_landed_on_a_known_outlet(self):
        import psycopg

        with psycopg.connect(self._dsn) as conn:
            row = conn.execute(
                "SELECT founded, circulation_note FROM corpus.entities "
                "WHERE entity_key = 'nytimes.com'"
            ).fetchone()
        self.assertEqual(row[0], 1851)
        self.assertEqual(row[1], "~10M paid digital subscribers (proxy)")

    def test_fetch_entity_profiles_round_trips_backfilled_columns(self):
        from analysis.src.common import db as dbmod

        with dbmod.connection() as conn:
            entity_id = conn.execute(
                "SELECT entity_id FROM corpus.entities WHERE entity_key = 'nytimes.com'"
            ).fetchone()["entity_id"]
            result = profiles.fetch_entity_profiles(conn, [entity_id])

        self.assertIn(entity_id, result)
        model = result[entity_id]
        self.assertEqual(model.kind, "outlet")
        self.assertEqual(model.key, "nytimes.com")
        self.assertEqual(model.founded, 1851)
        self.assertEqual(model.circulation_note, "~10M paid digital subscribers (proxy)")
        self.assertEqual(model.entity_id, entity_id)


if __name__ == "__main__":
    unittest.main()
