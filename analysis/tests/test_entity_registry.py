"""
Tests for the entity-registry module added in walkthrough 057 (Phase 3b
of the UI Redesign Plan).

Covers:
  * Canonicalizers handle expected shapes + edge cases.
  * YAML load succeeds against the real ``data/*.yaml`` registries.
  * Alias keys (also_domains / also_handles) resolve to the same entity
    as the primary key.
  * Unknown inputs return None rather than raising.
  * Registry overriding via the CIVIC_ENTITY_REGISTRY_DIR env var works
    so tests can run hermetically.
"""

from __future__ import annotations

import os
import sys
import textwrap
import unittest
from pathlib import Path

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting import entity_registry as er


class CanonicalizerTests(unittest.TestCase):
    def test_news_domain_strips_www(self):
        self.assertEqual(er.canonicalize_news_domain("www.nytimes.com"), "nytimes.com")
        self.assertEqual(er.canonicalize_news_domain("nytimes.com"), "nytimes.com")

    def test_news_domain_lowercases(self):
        self.assertEqual(er.canonicalize_news_domain("WWW.NYTimes.COM"), "nytimes.com")

    def test_news_domain_trims(self):
        self.assertEqual(er.canonicalize_news_domain("  www.bbc.com  "), "bbc.com")

    def test_news_domain_handles_none_and_empty(self):
        self.assertIsNone(er.canonicalize_news_domain(None))
        self.assertIsNone(er.canonicalize_news_domain(""))
        self.assertIsNone(er.canonicalize_news_domain("   "))

    def test_subreddit_strips_r_prefix_both_forms(self):
        self.assertEqual(er.canonicalize_subreddit("r/politics"), "politics")
        self.assertEqual(er.canonicalize_subreddit("/r/politics"), "politics")
        self.assertEqual(er.canonicalize_subreddit("politics"), "politics")

    def test_subreddit_case_insensitive(self):
        self.assertEqual(er.canonicalize_subreddit("PoliticalDiscussion"),
                         "politicaldiscussion")
        self.assertEqual(er.canonicalize_subreddit("r/Conservative"), "conservative")

    def test_handle_strips_at_and_lowercases(self):
        self.assertEqual(er.canonicalize_handle("@POTUS"), "potus")
        self.assertEqual(er.canonicalize_handle("POTUS"), "potus")
        self.assertEqual(er.canonicalize_handle("@RealDonaldTrump"), "realdonaldtrump")

    def test_handle_handles_none_and_empty(self):
        self.assertIsNone(er.canonicalize_handle(None))
        self.assertIsNone(er.canonicalize_handle(""))
        self.assertIsNone(er.canonicalize_handle("@"))  # just an @ → None after strip


class RealRegistryTests(unittest.TestCase):
    """Load the actual data/*.yaml registries and verify they parse + look up."""

    @classmethod
    def setUpClass(cls):
        cls.reg = er.load_registry()  # reads data/*.yaml

    def test_outlets_loaded(self):
        self.assertGreaterEqual(len(self.reg.primary_outlets()), 20)

    def test_outlet_primary_lookup(self):
        outlet = self.reg.get_outlet("nytimes.com")
        self.assertIsNotNone(outlet)
        self.assertEqual(outlet.domain, "nytimes.com")
        self.assertEqual(outlet.display_name, "The New York Times")

    def test_outlet_lookup_strips_www(self):
        # The DB stores "www.bbc.com"; aggregator must resolve via
        # canonicalize_news_domain on the way in.
        outlet = self.reg.get_outlet("www.bbc.com")
        self.assertIsNotNone(outlet)
        self.assertEqual(outlet.domain, "bbc.com")

    def test_outlet_alias_lookup(self):
        # bbc.co.uk should resolve to the BBC entity via also_domains.
        outlet = self.reg.get_outlet("www.bbc.co.uk")
        self.assertIsNotNone(outlet)
        self.assertEqual(outlet.display_name, "BBC")

    def test_outlet_unknown_returns_none(self):
        self.assertIsNone(self.reg.get_outlet("nosuchoutlet.invalid"))
        self.assertIsNone(self.reg.get_outlet(None))
        self.assertIsNone(self.reg.get_outlet(""))

    def test_officials_loaded(self):
        self.assertGreaterEqual(len(self.reg.primary_officials()), 16)

    def test_official_primary_lookup(self):
        official = self.reg.get_official("@POTUS")
        self.assertIsNotNone(official)
        self.assertEqual(official.office, "President of the United States")

    def test_official_alias_lookup(self):
        # @realDonaldTrump is an also_handle of the same entity as @POTUS.
        a = self.reg.get_official("@POTUS")
        b = self.reg.get_official("@realDonaldTrump")
        self.assertIsNotNone(a)
        self.assertIsNotNone(b)
        self.assertEqual(a.handle, b.handle)

    def test_official_case_insensitive(self):
        a = self.reg.get_official("@POTUS")
        b = self.reg.get_official("@potus")
        c = self.reg.get_official("potus")
        self.assertEqual(a, b)
        self.assertEqual(b, c)

    def test_subreddits_loaded(self):
        self.assertGreaterEqual(len(self.reg.primary_subreddits()), 10)

    def test_subreddit_lookup_case_insensitive(self):
        # DB stores "PoliticalDiscussion"; registry YAML stores the same;
        # the map is keyed lowercase, so both raw forms resolve.
        a = self.reg.get_subreddit("PoliticalDiscussion")
        b = self.reg.get_subreddit("politicaldiscussion")
        c = self.reg.get_subreddit("r/politicaldiscussion")
        self.assertIsNotNone(a)
        self.assertEqual(a, b)
        self.assertEqual(a, c)

    def test_subreddit_unknown_returns_none(self):
        self.assertIsNone(self.reg.get_subreddit("notarealsubreddit"))
        self.assertIsNone(self.reg.get_subreddit(None))

    def test_profile_dicts_have_expected_shape(self):
        outlet = self.reg.get_outlet("nytimes.com")
        d = outlet.profile_dict()
        self.assertEqual(d["kind"], "outlet")
        self.assertEqual(d["key"], "nytimes.com")
        self.assertIn("displayName", d)
        self.assertIn("lean", d)
        self.assertIn("leanSource", d)

        official = self.reg.get_official("@POTUS")
        d = official.profile_dict()
        self.assertEqual(d["kind"], "official")
        self.assertIn("displayName", d)
        self.assertIn("office", d)
        self.assertIn("party", d)

        sub = self.reg.get_subreddit("politics")
        d = sub.profile_dict()
        self.assertEqual(d["kind"], "subreddit")
        self.assertIn("lean", d)
        self.assertIn("leanSource", d)


class SingletonReloadTests(unittest.TestCase):
    """The module-level singleton caches; reload_registries() should
    pick up on-disk changes via a temp directory override."""

    def test_reload_picks_up_new_outlet(self):
        # Baseline load from real data.
        er.reload_registries()
        self.assertIsNone(er.get_registry().get_outlet("newentity.example"))

        # Override the data dir to a tmp with a minimal registry.
        import tempfile
        with tempfile.TemporaryDirectory() as tmpdir:
            outlets_yaml = textwrap.dedent(
                """\
                schema_version: 1
                outlets:
                  - domain: newentity.example
                    also_domains: []
                    display_name: New Entity
                    blurb: A temporary registry entry used only in unit tests.
                    partisan_lean: center
                    lean_source: test fixture
                    owner: null
                """
            )
            (Path(tmpdir) / "news_outlets.yaml").write_text(outlets_yaml, encoding="utf-8")

            os.environ["CIVIC_ENTITY_REGISTRY_DIR"] = tmpdir
            try:
                er.reload_registries()
                outlet = er.get_registry().get_outlet("newentity.example")
                self.assertIsNotNone(outlet)
                self.assertEqual(outlet.display_name, "New Entity")
            finally:
                del os.environ["CIVIC_ENTITY_REGISTRY_DIR"]
                er.reload_registries()  # restore the process singleton to real data


if __name__ == "__main__":
    unittest.main()
