"""
Tests for analysis/src/common/registry.py + analysis/src/etl/registry_sync.py
(Postgres redesign Phase 4).

Two tiers, matching the repo's established convention (see
analysis/tests/test_pg_db.py, tools/test_migrate_sqlite_to_pg.py):

  1. Pure mapping/parsing tests (TestLeanFlattening, TestCanonicalizers,
     TestYamlLoaders) -- no I/O beyond reading small fixture YAML files
     this suite writes to a temp dir; always run.
  2. Integration tests (TestRegistrySyncAgainstRealPostgres) -- gated on
     CIVIC_TEST_DATABASE_URL, skipped cleanly when unset. The pointed-at
     database must already have data/pg-migrations/0001_north_star.sql
     applied (same assumption test_pg_db.py makes); setUpClass applies it
     itself if the `corpus` schema is not yet present, so a genuinely fresh
     throwaway container also works standalone.
"""

from __future__ import annotations

import logging
import os
import unittest
from datetime import datetime, timezone
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import Any, Dict

import yaml

from analysis.src.common import registry as reg

PG_TEST_URL_ENV = "CIVIC_TEST_DATABASE_URL"


class TestLeanFlattening(unittest.TestCase):
    """The flattening constants are the crux of the owner's unified-enum
    decision -- every value in each YAML vocabulary must land on exactly one
    of the 5 corpus.political_lean values, and anything else must fail loud
    (log + 'unknown'), never guess."""

    def test_lean_scale_all_six_outlet_values(self):
        self.assertEqual(reg.flatten_lean_scale("left", context="x"), "democrat")
        self.assertEqual(reg.flatten_lean_scale("center-left", context="x"), "democrat")
        self.assertEqual(reg.flatten_lean_scale("center", context="x"), "independent")
        self.assertEqual(reg.flatten_lean_scale("center-right", context="x"), "republican")
        self.assertEqual(reg.flatten_lean_scale("right", context="x"), "republican")
        self.assertEqual(reg.flatten_lean_scale("mixed", context="x"), "mixed")

    def test_lean_scale_is_case_and_whitespace_insensitive(self):
        self.assertEqual(reg.flatten_lean_scale(" Center-Left ", context="x"), "democrat")

    def test_lean_scale_unrecognized_value_maps_to_unknown_and_logs(self):
        with self.assertLogs("analysis.src.common.registry", level="WARNING") as cm:
            result = reg.flatten_lean_scale("far-left", context="outlet acme.com")
        self.assertEqual(result, "unknown")
        self.assertIn("acme.com", cm.output[0])
        self.assertIn("far-left", cm.output[0])

    def test_lean_scale_absent_value_maps_to_unknown_and_logs(self):
        with self.assertLogs("analysis.src.common.registry", level="WARNING"):
            self.assertEqual(reg.flatten_lean_scale(None, context="x"), "unknown")
        with self.assertLogs("analysis.src.common.registry", level="WARNING"):
            self.assertEqual(reg.flatten_lean_scale("", context="x"), "unknown")

    def test_party_code_all_documented_values(self):
        self.assertEqual(reg.flatten_party_code("R", context="x"), "republican")
        self.assertEqual(reg.flatten_party_code("D", context="x"), "democrat")
        self.assertEqual(reg.flatten_party_code("I", context="x"), "independent")
        self.assertEqual(reg.flatten_party_code("independent-dem", context="x"), "independent")

    def test_party_code_is_case_and_whitespace_insensitive(self):
        self.assertEqual(reg.flatten_party_code(" d ", context="x"), "democrat")

    def test_party_code_other_and_unrecognized_map_to_unknown_and_logs(self):
        with self.assertLogs("analysis.src.common.registry", level="WARNING") as cm:
            result = reg.flatten_party_code("other", context="official @test")
        self.assertEqual(result, "unknown")
        self.assertIn("@test", cm.output[0])

    def test_party_code_absent_value_maps_to_unknown_and_logs(self):
        with self.assertLogs("analysis.src.common.registry", level="WARNING"):
            self.assertEqual(reg.flatten_party_code(None, context="x"), "unknown")


class TestCanonicalizers(unittest.TestCase):
    """Copied verbatim from entity_registry.py -- must behave identically,
    since the two modules canonicalize the same real-world strings during
    the parallel-stack period."""

    def test_news_domain_strips_www_and_lowercases(self):
        self.assertEqual(reg.canonicalize_news_domain("WWW.NYTimes.COM"), "nytimes.com")

    def test_news_domain_none_and_empty(self):
        self.assertIsNone(reg.canonicalize_news_domain(None))
        self.assertIsNone(reg.canonicalize_news_domain("   "))

    def test_subreddit_strips_r_prefix_both_forms(self):
        self.assertEqual(reg.canonicalize_subreddit("r/politics"), "politics")
        self.assertEqual(reg.canonicalize_subreddit("/r/Politics"), "politics")

    def test_handle_strips_at_and_lowercases(self):
        self.assertEqual(reg.canonicalize_handle("@POTUS"), "potus")

    def test_handle_bare_at_returns_none(self):
        self.assertIsNone(reg.canonicalize_handle("@"))


class TestYamlLoaders(unittest.TestCase):
    """Parse small hand-written fixture YAML files with the same shape as
    the real data/*.yaml registries."""

    def setUp(self):
        self._tmp = TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)

    def _write(self, name: str, payload: Dict[str, Any]) -> Path:
        path = self.root / name
        path.write_text(yaml.safe_dump(payload))
        return path

    def test_load_outlets_parses_fields_and_aliases(self):
        path = self._write("outlets.yaml", {"outlets": [{
            "domain": "Acme.com", "also_domains": ["www.acme.com", "acme.net"],
            "display_name": "Acme News", "blurb": "A test outlet.",
            "partisan_lean": "center-left", "lean_source": "Test Rater 2026",
            "owner": "Acme Corp",
        }]})
        records = reg.load_outlets(path)
        self.assertEqual(len(records), 1)
        rec = records[0]
        self.assertEqual(rec.entity_key, "acme.com")
        # www.acme.com canonicalizes to acme.com == primary, so it must be
        # excluded from aliases (not a distinct alternate key).
        self.assertEqual(rec.aliases, ("acme.net",))
        self.assertEqual(rec.raw_lean, "center-left")
        self.assertEqual(rec.source_citation, "Test Rater 2026")

    def test_load_officials_parses_party_and_term_start(self):
        path = self._write("officials.yaml", {"officials": [{
            "handle": "@TestOfficial", "also_handles": ["@AltHandle"],
            "display_name": "Test Official", "office": "Test Office",
            "party": "D", "blurb": "A test official.",
            "term_start": "2025-01-01", "bio_source": "https://example.com",
        }]})
        records = reg.load_officials(path)
        self.assertEqual(len(records), 1)
        rec = records[0]
        self.assertEqual(rec.entity_key, "testofficial")
        self.assertEqual(rec.aliases, ("althandle",))
        self.assertEqual(rec.raw_party, "D")
        self.assertEqual(rec.term_start, "2025-01-01")

    def test_load_subreddits_has_no_aliases(self):
        path = self._write("subreddits.yaml", {"subreddits": [{
            "subreddit": "TestSub", "display_name": "r/TestSub",
            "blurb": "A test subreddit.", "tilt": "left", "tilt_source": "Test",
        }]})
        records = reg.load_subreddits(path)
        self.assertEqual(records[0].entity_key, "testsub")
        self.assertEqual(records[0].aliases, ())
        self.assertEqual(records[0].raw_lean, "left")

    def test_missing_primary_key_entry_is_dropped(self):
        path = self._write("outlets.yaml", {"outlets": [
            {"domain": "", "display_name": "No domain"},
            {"domain": "good.com", "display_name": "Good"},
        ]})
        records = reg.load_outlets(path)
        self.assertEqual([r.entity_key for r in records], ["good.com"])

    def test_load_political_accounts_flattens_executive_and_congress(self):
        path = self._write("accounts.yaml", {"accounts": {
            "executive_branch": [{
                "name": "Test Exec", "office": "Test Office", "branch": "Executive",
                "accounts": [
                    {"platform": "x", "handle": "@TestExec1", "account_type": "official"},
                    {"platform": "x", "handle": "@TestExec2", "account_type": "personal"},
                ],
            }],
            "congress": {
                "house": [{"name": "Test Rep", "handle": "@TestRep", "party": "R"}],
                "senate": [{"name": "Test Sen", "handle": "@TestSen", "party": "I"}],
            },
        }})
        records = reg.load_political_accounts(path)
        by_handle = {r.handle: r for r in records}
        self.assertEqual(set(by_handle), {"testexec1", "testexec2", "testrep", "testsen"})
        self.assertIsNone(by_handle["testexec1"].raw_party)
        self.assertEqual(by_handle["testrep"].raw_party, "R")
        self.assertEqual(by_handle["testrep"].office_title, "Representative")
        self.assertEqual(by_handle["testsen"].office_title, "Senator")

    def test_missing_file_returns_empty_list(self):
        self.assertEqual(reg.load_outlets(self.root / "nope.yaml"), [])
        self.assertEqual(reg.load_political_accounts(self.root / "nope.yaml"), [])


@unittest.skipUnless(
    os.environ.get(PG_TEST_URL_ENV),
    f"{PG_TEST_URL_ENV} not set -- no Postgres server available to test against",
)
class TestRegistrySyncAgainstRealPostgres(unittest.TestCase):
    """Full sync from fixture YAML into a live Postgres, covering the
    behaviors named in the task: idempotent re-run, active=false flip on a
    removed key, lean_source preservation, alias reconciliation, and
    author_profiles linking (present vs. not-yet-ingested author)."""

    @classmethod
    def setUpClass(cls):
        from analysis.src.common import db as db_module

        cls._prev_url = os.environ.get("CIVIC_DATABASE_URL")
        os.environ["CIVIC_DATABASE_URL"] = os.environ[PG_TEST_URL_ENV]
        db_module.close_pool()
        cls._ensure_schema_applied()

    @classmethod
    def tearDownClass(cls):
        from analysis.src.common import db as db_module

        db_module.close_pool()
        if cls._prev_url is None:
            os.environ.pop("CIVIC_DATABASE_URL", None)
        else:
            os.environ["CIVIC_DATABASE_URL"] = cls._prev_url

    @classmethod
    def _ensure_schema_applied(cls) -> None:
        """Apply 0001_north_star.sql if the `corpus` schema is not already
        present, so this suite is self-contained against a genuinely fresh
        throwaway container (not just one someone pre-migrated by hand)."""
        from analysis.src.common.db import connection

        repo_root = Path(__file__).resolve().parents[2]
        ddl_path = repo_root / "data" / "pg-migrations" / "0001_north_star.sql"
        with connection() as conn:
            exists = conn.execute(
                "SELECT 1 FROM pg_namespace WHERE nspname = 'corpus'"
            ).fetchone()
            if exists is None:
                conn.execute(ddl_path.read_text())

    def setUp(self):
        from analysis.src.common.db import connection

        with connection() as conn:
            conn.execute(
                "TRUNCATE corpus.entities, corpus.entity_aliases, "
                "corpus.author_profiles, corpus.authors RESTART IDENTITY CASCADE"
            )
        self._tmp = TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = Path(self._tmp.name)
        self._write_fixture_yaml()

    def _write_fixture_yaml(self, *, drop_second_outlet: bool = False) -> None:
        outlets = [{
            "domain": "acme.com", "also_domains": ["acmenews.com"],
            "display_name": "Acme News", "blurb": "A test outlet.",
            "partisan_lean": "center-left", "lean_source": "Test Rater 2026",
            "owner": "Acme Corp",
        }]
        if not drop_second_outlet:
            outlets.append({
                "domain": "second.com", "also_domains": [],
                "display_name": "Second News", "blurb": "Another test outlet.",
                "partisan_lean": "right", "lean_source": "Test Rater 2026",
                "owner": "Second Corp",
            })
        (self.root / "news_outlets.yaml").write_text(yaml.safe_dump({"outlets": outlets}))
        (self.root / "verified_officials.yaml").write_text(yaml.safe_dump({"officials": [{
            "handle": "@testofficial", "also_handles": ["@testalt"],
            "display_name": "Test Official", "office": "Test Office",
            "party": "D", "blurb": "A test official.",
            "term_start": "2025-01-01", "bio_source": "https://example.com",
        }]}))
        (self.root / "major_subreddits.yaml").write_text(yaml.safe_dump({"subreddits": [{
            "subreddit": "testsub", "display_name": "r/testsub",
            "blurb": "A test subreddit.", "tilt": "mixed", "tilt_source": "Test",
        }]}))
        (self.root / "known_political_x_accounts.yaml").write_text(yaml.safe_dump({"accounts": {
            "executive_branch": [
                {
                    "name": "Test Official", "office": "Test Office", "branch": "Executive",
                    "accounts": [{"platform": "x", "handle": "@testofficial", "account_type": "official"}],
                },
                {
                    "name": "Test Exec Multi", "office": "Test Deputy", "branch": "Executive",
                    "accounts": [
                        {"platform": "x", "handle": "@testexecofficial", "account_type": "official"},
                        {"platform": "x", "handle": "@testexecpersonal", "account_type": "personal"},
                    ],
                },
            ],
            "congress": {
                "house": [{"name": "Test Rep", "handle": "@testrep", "party": "R"}],
                "senate": [],
            },
        }}))

    def _sync(self) -> Dict[str, int]:
        from analysis.src.etl.registry_sync import sync_registry

        return sync_registry(data_dir=self.root)

    def _fetch_entity(self, entity_key: str) -> Dict[str, Any]:
        from analysis.src.common.db import connection

        with connection() as conn:
            return conn.execute(
                "SELECT * FROM corpus.entities WHERE entity_key = %(k)s", {"k": entity_key},
            ).fetchone()

    def test_full_sync_creates_entities_and_aliases(self):
        counts = self._sync()
        self.assertEqual(counts["outlets"], 2)
        self.assertEqual(counts["officials"], 1)
        self.assertEqual(counts["subreddits"], 1)
        acme = self._fetch_entity("acme.com")
        self.assertEqual(acme["lean"], "democrat")  # center-left -> democrat
        self.assertTrue(acme["active"])
        official = self._fetch_entity("testofficial")
        self.assertEqual(official["lean"], "democrat")  # party D -> democrat

    def test_lean_source_preserves_original_yaml_string(self):
        self._sync()
        acme = self._fetch_entity("acme.com")
        self.assertEqual(acme["lean_source"], "center-left")
        official = self._fetch_entity("testofficial")
        self.assertEqual(official["lean_source"], "D")

    def test_alias_rows_created(self):
        self._sync()
        from analysis.src.common.db import connection

        official = self._fetch_entity("testofficial")
        with connection() as conn:
            aliases = {
                r["alias"] for r in conn.execute(
                    "SELECT alias FROM corpus.entity_aliases WHERE entity_id = %(id)s",
                    {"id": official["entity_id"]},
                ).fetchall()
            }
        self.assertEqual(aliases, {"testalt"})

    def test_alias_removed_when_dropped_from_yaml(self):
        self._sync()
        # Re-sync with the also_handles list emptied out.
        (self.root / "verified_officials.yaml").write_text(yaml.safe_dump({"officials": [{
            "handle": "@testofficial", "also_handles": [],
            "display_name": "Test Official", "office": "Test Office",
            "party": "D", "blurb": "A test official.",
            "term_start": "2025-01-01", "bio_source": "https://example.com",
        }]}))
        self._sync()
        from analysis.src.common.db import connection

        official = self._fetch_entity("testofficial")
        with connection() as conn:
            aliases = conn.execute(
                "SELECT alias FROM corpus.entity_aliases WHERE entity_id = %(id)s",
                {"id": official["entity_id"]},
            ).fetchall()
        self.assertEqual(aliases, [])

    def test_idempotent_rerun_is_a_true_no_op(self):
        self._sync()
        before = self._fetch_entity("acme.com")
        self._sync()
        after = self._fetch_entity("acme.com")
        # synced_at must NOT bump on an unchanged re-sync -- that is the
        # documented idempotency contract (zero rows touched, not just an
        # idempotent overwrite).
        self.assertEqual(before["synced_at"], after["synced_at"])
        self.assertEqual(before["entity_id"], after["entity_id"])

    def test_active_flips_false_on_removed_key_never_deletes(self):
        self._sync()
        self._write_fixture_yaml(drop_second_outlet=True)
        self._sync()
        second = self._fetch_entity("second.com")
        self.assertIsNotNone(second)  # row still present -- never DELETEd
        self.assertFalse(second["active"])

    def test_removed_entity_reactivates_if_it_reappears(self):
        self._sync()
        self._write_fixture_yaml(drop_second_outlet=True)
        self._sync()
        self._write_fixture_yaml(drop_second_outlet=False)
        self._sync()
        second = self._fetch_entity("second.com")
        self.assertTrue(second["active"])

    def test_author_profile_linked_when_author_exists(self):
        from analysis.src.common.db import connection

        with connection() as conn:
            conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, '111', '@testofficial')"
            )
        counts = self._sync()
        self.assertEqual(counts["author_profiles_synced"], 1)
        self.assertEqual(counts["author_profiles_skipped"], 3)  # 3 other handles, no author row
        official_entity = self._fetch_entity("testofficial")
        with connection() as conn:
            profile = conn.execute(
                "SELECT * FROM corpus.author_profiles ap "
                "JOIN corpus.authors a ON a.author_id = ap.author_id "
                "WHERE a.handle = '@testofficial'"
            ).fetchone()
        self.assertEqual(profile["tier"], "elected_official")
        self.assertEqual(profile["method"], "curated_list")
        self.assertEqual(profile["entity_id"], official_entity["entity_id"])

    def test_author_profile_skipped_without_matching_author(self):
        counts = self._sync()
        self.assertEqual(counts["author_profiles_synced"], 0)
        self.assertEqual(counts["author_profiles_skipped"], 4)

    # -- Officials-promotion (known_political_x_accounts.yaml -> entities) --

    def test_promotion_count_matches_yaml(self):
        # Fixture has 3 person-groups in known_political_x_accounts.yaml:
        # testofficial (editorial overlap, not promoted), testrep and
        # testexecofficial/testexecpersonal (2 groups promoted).
        counts = self._sync()
        self.assertEqual(counts["officials_promoted"], 2)
        self.assertEqual(counts["officials_promotion_editorial_dedup"], 1)

    def test_promoted_official_lean_from_party_code(self):
        self._sync()
        rep = self._fetch_entity("testrep")
        self.assertEqual(rep["kind"], "official")
        self.assertEqual(rep["lean"], "republican")  # party R -> republican
        self.assertEqual(rep["lean_source"], "R")

    def test_editorial_flag_assigned_both_ways(self):
        self._sync()
        for key in ("acme.com", "testofficial", "testsub"):
            self.assertTrue(self._fetch_entity(key)["editorial"], key)
        self.assertFalse(self._fetch_entity("testrep")["editorial"])

    def test_editorial_overlap_is_one_row_not_duplicated(self):
        # @testofficial is in both verified_officials.yaml AND
        # known_political_x_accounts.yaml (executive_branch) -- the
        # editorial row must win, not get a second entity or get
        # downgraded to editorial=false.
        self._sync()
        official = self._fetch_entity("testofficial")
        self.assertTrue(official["editorial"])
        self.assertEqual(official["blurb"], "A test official.")  # editorial content survives
        from analysis.src.common.db import connection

        with connection() as conn:
            count = conn.execute(
                "SELECT count(*) AS n FROM corpus.entities WHERE display_name = 'Test Official'"
            ).fetchone()
        self.assertEqual(count["n"], 1)

    def test_promoted_multi_handle_person_is_one_entity_with_alias(self):
        # Test Exec Multi has 2 X handles and no verified_officials.yaml
        # entry -- must still be exactly one entity row (keyed on the
        # "official"-type handle listed first), not one per handle.
        self._sync()
        primary = self._fetch_entity("testexecofficial")
        self.assertIsNotNone(primary)
        self.assertFalse(primary["editorial"])
        self.assertIsNone(self._fetch_entity("testexecpersonal"))  # not its own entity row
        from analysis.src.common.db import connection

        with connection() as conn:
            aliases = {
                r["alias"] for r in conn.execute(
                    "SELECT alias FROM corpus.entity_aliases WHERE entity_id = %(id)s",
                    {"id": primary["entity_id"]},
                ).fetchall()
            }
        self.assertEqual(aliases, {"testexecpersonal"})

    def test_promoted_official_idempotent_rerun_is_a_true_no_op(self):
        self._sync()
        before = self._fetch_entity("testrep")
        self._sync()
        after = self._fetch_entity("testrep")
        self.assertEqual(before["synced_at"], after["synced_at"])
        self.assertEqual(before["entity_id"], after["entity_id"])

    def test_author_profile_links_to_promoted_entity(self):
        from analysis.src.common.db import connection

        with connection() as conn:
            conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, '222', '@testrep')"
            )
        self._sync()
        rep_entity = self._fetch_entity("testrep")
        with connection() as conn:
            profile = conn.execute(
                "SELECT * FROM corpus.author_profiles ap "
                "JOIN corpus.authors a ON a.author_id = ap.author_id "
                "WHERE a.handle = '@testrep'"
            ).fetchone()
        self.assertIsNotNone(profile["entity_id"])  # previously always NULL for the long tail
        self.assertEqual(profile["entity_id"], rep_entity["entity_id"])


if __name__ == "__main__":
    unittest.main()
