"""
Tests for analysis/src/engine/account_tier.py -- Postgres redesign Phase 6.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (analysis/tests/test_engine_citations.py, test_etl_authors.py):

  1. Pure-core tests (no DB) -- handle canonicalization, and _index_entities'
     matching/exclusion rules against seed-style entity_key/alias values.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with both data/pg-migrations/0001_north_star.sql and
     0002_entity_registry_seed.sql applied (the real curated seed, not a
     fixture registry) -- this module's whole job is matching against that
     real data.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine import account_tier
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class CanonicalizeHandleTests(unittest.TestCase):
    def test_strips_leading_at(self):
        self.assertEqual(account_tier._canonicalize_handle("@POTUS"), "potus")

    def test_lowercases_and_strips_whitespace(self):
        self.assertEqual(account_tier._canonicalize_handle("  Jim_Jordan  "), "jim_jordan")

    def test_bare_at_yields_none(self):
        self.assertIsNone(account_tier._canonicalize_handle("@"))

    def test_none_input_yields_none(self):
        self.assertIsNone(account_tier._canonicalize_handle(None))

    def test_empty_string_yields_none(self):
        self.assertIsNone(account_tier._canonicalize_handle(""))

    def test_handle_without_at_unchanged_besides_case(self):
        self.assertEqual(account_tier._canonicalize_handle("AustinScottGA08"), "austinscottga08")


class IndexEntitiesTests(unittest.TestCase):
    """_index_entities is given rows already restricted to kind='official'
    by the caller's SQL -- these fixtures use realistic seed-style
    entity_key/alias values (0002_entity_registry_seed.sql's actual
    convention: lowercased X handle as entity_key) but are synthetic, not a
    live-DB read."""

    def test_editorial_entity_matches_by_entity_key(self):
        # editorial=true rows (e.g. verified_officials.yaml) carry no field
        # this module reads besides entity_key/active/elected --
        # editorial-ness is irrelevant to matching.
        entity_rows = [{"entity_id": 1, "entity_key": "potus", "active": True, "elected": True}]
        lookup = account_tier._index_entities(entity_rows, [])
        self.assertEqual(lookup["potus"], (1, True))

    def test_promoted_entity_matches_by_entity_key(self):
        entity_rows = [{"entity_id": 2, "entity_key": "jim_jordan", "active": True, "elected": True}]
        lookup = account_tier._index_entities(entity_rows, [])
        self.assertEqual(lookup["jim_jordan"], (2, True))

    def test_alias_resolves_to_owning_entity(self):
        entity_rows = [
            {"entity_id": 3, "entity_key": "leaderjohnthune", "active": True, "elected": True}
        ]
        alias_rows = [{"entity_id": 3, "alias": "senjohnthune"}]
        lookup = account_tier._index_entities(entity_rows, alias_rows)
        self.assertEqual(lookup["senjohnthune"], (3, True))

    def test_inactive_entity_excluded_from_key_and_alias(self):
        entity_rows = [
            {"entity_id": 4, "entity_key": "retiredsenator", "active": False, "elected": True}
        ]
        alias_rows = [{"entity_id": 4, "alias": "retiredsenaltalt"}]
        lookup = account_tier._index_entities(entity_rows, alias_rows)
        self.assertNotIn("retiredsenator", lookup)
        self.assertNotIn("retiredsenaltalt", lookup)

    def test_entity_key_wins_over_colliding_alias(self):
        entity_rows = [
            {"entity_id": 5, "entity_key": "samekey", "active": True, "elected": True},
            {"entity_id": 6, "entity_key": "other", "active": True, "elected": False},
        ]
        alias_rows = [{"entity_id": 6, "alias": "samekey"}]
        lookup = account_tier._index_entities(entity_rows, alias_rows)
        self.assertEqual(lookup["samekey"], (5, True))

    def test_unmatched_handle_absent_from_lookup(self):
        entity_rows = [{"entity_id": 1, "entity_key": "potus", "active": True, "elected": True}]
        lookup = account_tier._index_entities(entity_rows, [])
        self.assertNotIn("totally_unknown_handle", lookup)

    def test_appointed_entity_carries_elected_false(self):
        # An appointed/institutional official (e.g. a cabinet secretary)
        # is matched exactly like an elected one -- only the stored
        # `elected` value differs, and it's this module's job to surface
        # it unchanged so _tier_for can derive 'affiliated'.
        entity_rows = [
            {"entity_id": 7, "entity_key": "agpambondi", "active": True, "elected": False}
        ]
        lookup = account_tier._index_entities(entity_rows, [])
        self.assertEqual(lookup["agpambondi"], (7, False))

    def test_null_elected_entity_carries_none(self):
        entity_rows = [
            {"entity_id": 8, "entity_key": "unclassifiedofficial", "active": True, "elected": None}
        ]
        lookup = account_tier._index_entities(entity_rows, [])
        self.assertEqual(lookup["unclassifiedofficial"], (8, None))


class TierForTests(unittest.TestCase):
    """_tier_for is the explicit elected -> tier mapping this module was
    upgraded to make: it must never fall back to 'elected_official' for
    anything but elected=True, since that would over-claim elected status
    for an appointed/institutional account."""

    def test_elected_true_yields_elected_official(self):
        self.assertEqual(
            account_tier._tier_for(True, entity_id=1, handle="jim_jordan"),
            account_tier.ELECTED_OFFICIAL_TIER,
        )

    def test_elected_false_yields_affiliated(self):
        self.assertEqual(
            account_tier._tier_for(False, entity_id=2, handle="agpambondi"),
            account_tier.AFFILIATED_TIER,
        )

    def test_elected_none_yields_affiliated_with_warning(self):
        with self.assertLogs(account_tier.logger, level="WARNING") as cm:
            tier = account_tier._tier_for(None, entity_id=3, handle="somehandle")
        self.assertEqual(tier, account_tier.AFFILIATED_TIER)
        self.assertTrue(any("elected=NULL" in msg for msg in cm.output))


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL. Real seed data
# (0001 + 0002), not a fixture registry -- proves this module matches what
# the real curated registry actually contains.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ClassifyAuthorsIntegrationTests(unittest.TestCase):
    # A promoted (editorial=false), elected member from the real seed
    # (known_political_x_accounts.yaml -> 0002_entity_registry_seed.sql):
    # Jim Jordan, Representative, entity_key='jim_jordan', elected=true.
    KNOWN_HANDLE = "jim_jordan"
    # A promoted (editorial=false), appointed agency head from the real
    # seed's executive-branch classification block: John Ratcliffe,
    # Director of the Central Intelligence Agency, entity_key='ciadirector',
    # elected=false (see 0002_entity_registry_seed.sql's commented
    # classification block: agency-head basis).
    KNOWN_AFFILIATED_HANDLE = "ciadirector"

    @classmethod
    def setUpClass(cls):
        import psycopg
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn, seed=True)
        with psycopg.connect(cls._dsn, autocommit=True) as conn:
            cls._known_entity_id = conn.execute(
                "SELECT entity_id FROM corpus.entities WHERE entity_key = %s", (cls.KNOWN_HANDLE,)
            ).fetchone()[0]
            cls._known_affiliated_entity_id = conn.execute(
                "SELECT entity_id FROM corpus.entities WHERE entity_key = %s",
                (cls.KNOWN_AFFILIATED_HANDLE,),
            ).fetchone()[0]

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_mutable_tables()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_mutable_tables(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            # corpus.entities/entity_aliases are the seeded registry --
            # left untouched across tests in this class.
            conn.execute("TRUNCATE corpus.author_profiles, corpus.authors, raw.x_users CASCADE")

    def _insert_x_user(self, conn, user_id, username):
        conn.execute(
            "INSERT INTO raw.x_users (user_id, username, name, description, location, "
            "profile_image_url, verified, verified_type, followers_count, following_count, "
            "created_at, fetched_at, raw_hash) VALUES (%s, %s, %s, '', '', '', false, NULL, "
            "0, 0, now(), now(), 'h'||repeat('0', 63))",
            (user_id, username, username),
        )

    def _profile_row(self, author_id):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            return conn.execute(
                "SELECT * FROM corpus.author_profiles WHERE author_id = %s", (author_id,)
            ).fetchone()

    def _author_id_for_handle(self, handle):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            return conn.execute(
                "SELECT author_id FROM corpus.authors WHERE handle = %s", (handle,)
            ).fetchone()["author_id"]

    def test_known_promoted_official_gets_profile(self):
        from analysis.src.etl import authors as authors_mod
        from analysis.src.common import db as dbmod
        # Mixed case on ingest, proving canonicalization -- not a
        # pre-lowercased fixture pass-through.
        with dbmod.connection() as conn:
            self._insert_x_user(conn, "u_known", "Jim_Jordan")
        authors_mod.sync_x_authors()

        result = account_tier.classify_authors()
        self.assertEqual(result.matched, 1)

        author_id = self._author_id_for_handle("Jim_Jordan")
        profile = self._profile_row(author_id)
        self.assertIsNotNone(profile)
        self.assertEqual(profile["tier"], "elected_official")
        self.assertEqual(profile["method"], "curated_list")
        self.assertEqual(profile["entity_id"], self._known_entity_id)
        self.assertIsNotNone(profile["classified_at"])

    def test_known_affiliated_official_gets_profile(self):
        from analysis.src.etl import authors as authors_mod
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            self._insert_x_user(conn, "u_affiliated", "CIADirector")
        authors_mod.sync_x_authors()

        result = account_tier.classify_authors()
        self.assertEqual(result.matched, 1)

        author_id = self._author_id_for_handle("CIADirector")
        profile = self._profile_row(author_id)
        self.assertIsNotNone(profile)
        self.assertEqual(profile["tier"], "affiliated")
        self.assertEqual(profile["method"], "curated_list")
        self.assertEqual(profile["entity_id"], self._known_affiliated_entity_id)
        self.assertIsNotNone(profile["classified_at"])

    def test_unknown_handle_gets_no_row(self):
        from analysis.src.etl import authors as authors_mod
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            self._insert_x_user(conn, "u_unknown", "totally_unknown_handle_123")
        authors_mod.sync_x_authors()

        result = account_tier.classify_authors()
        self.assertEqual(result.matched, 0)
        self.assertEqual(result.unmatched, 1)

        author_id = self._author_id_for_handle("totally_unknown_handle_123")
        self.assertIsNone(self._profile_row(author_id))

    def test_rerun_is_zero_delta(self):
        from analysis.src.etl import authors as authors_mod
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            self._insert_x_user(conn, "u_known", "jim_jordan")
            self._insert_x_user(conn, "u_unknown", "totally_unknown_handle_123")
        authors_mod.sync_x_authors()

        first = account_tier.classify_authors()
        self.assertEqual(first.matched, 1)
        with dbmod.connection() as conn:
            count_after_first = conn.execute(
                "SELECT COUNT(*) AS n FROM corpus.author_profiles"
            ).fetchone()["n"]

        second = account_tier.classify_authors()
        self.assertEqual(second.matched, 0)
        with dbmod.connection() as conn:
            count_after_second = conn.execute(
                "SELECT COUNT(*) AS n FROM corpus.author_profiles"
            ).fetchone()["n"]
        self.assertEqual(count_after_first, count_after_second)

    def test_hand_edited_profile_survives_rerun(self):
        from analysis.src.etl import authors as authors_mod
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            self._insert_x_user(conn, "u_known", "jim_jordan")
        authors_mod.sync_x_authors()
        account_tier.classify_authors()

        author_id = self._author_id_for_handle("jim_jordan")
        # Simulate a pgAdmin curation edit: hand-override the tier/notes.
        with dbmod.connection() as conn:
            conn.execute(
                "UPDATE corpus.author_profiles SET notes = %s WHERE author_id = %s",
                ("hand-verified by owner", author_id),
            )

        result = account_tier.classify_authors()
        self.assertEqual(result.matched, 0)  # already-profiled author never re-selected

        profile = self._profile_row(author_id)
        self.assertEqual(profile["notes"], "hand-verified by owner")


if __name__ == "__main__":
    unittest.main()
