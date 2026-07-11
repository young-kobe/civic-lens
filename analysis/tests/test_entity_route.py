"""
Unit tests for the canonical entity router
(``analysis.src.reporting.entity_registry.route_reporting_entity``).

route_reporting_entity consolidates the tier + catch-all-key + verified-official
fallback + curated-account upgrade that the sentiment / bot / propaganda /
narrative aggregators each re-implemented. WHY it matters: divergent copies of
this branching are a tier-drift risk — a doc could land in different tiers
across the dashboards it feeds. These tests pin every branch so the aggregators
can adopt the shared helper without changing which bucket a doc routes to.

Uses the shipped registry (which carries @POTUS and r/politics — the same
entities the existing routing tests rely on).
"""

import sys
import unittest
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_VERIFIED_OFFICIALS,
    CATCH_ALL_X_USERS,
    canonicalize_handle,
    get_registry,
    route_reporting_entity,
    verified_officials_profile,
)

_FAKE_HANDLE = "zzz_not_a_real_registry_handle_999"


class RouteReportingEntityTests(unittest.TestCase):
    def setUp(self):
        self.reg = get_registry()

    def test_unknown_source_type_returns_none(self):
        self.assertIsNone(route_reporting_entity(self.reg, "podcast", None, None))

    def test_news_matched_outlet(self):
        route = route_reporting_entity(self.reg, "news", "www.nytimes.com", None)
        self.assertEqual(route.tier, "news")
        self.assertEqual(route.kind, "outlet")
        self.assertIsNotNone(route.entity_profile)
        self.assertNotEqual(route.key, CATCH_ALL_OUTLETS)

    def test_news_unmatched_is_catch_all_with_no_profile(self):
        route = route_reporting_entity(self.reg, "news", "unknown-xyz.example", None)
        self.assertEqual(route.tier, "news")
        self.assertEqual(route.key, CATCH_ALL_OUTLETS)
        self.assertEqual(route.kind, "catch_all")
        self.assertIsNone(route.entity_profile)

    def test_official_matched(self):
        route = route_reporting_entity(self.reg, "x_post", "x.com", "POTUS")
        self.assertEqual(route.tier, "officials")
        self.assertEqual(route.kind, "official")
        self.assertIsNotNone(route.entity_profile)

    def test_provenance_only_official_uses_shared_profile(self):
        route = route_reporting_entity(
            self.reg, "x_post", "x.com", _FAKE_HANDLE, is_official_tier=True
        )
        self.assertEqual(route.tier, "officials")
        self.assertEqual(route.key, CATCH_ALL_VERIFIED_OFFICIALS)
        self.assertEqual(route.kind, "catch_all")
        # Shared, populated profile (not None) — bot and sentiment both rely on
        # this identical card.
        self.assertEqual(route.entity_profile, verified_officials_profile())

    def test_unmatched_x_author_is_public_catch_all(self):
        route = route_reporting_entity(self.reg, "x_post", "x.com", _FAKE_HANDLE)
        self.assertEqual(route.tier, "public")
        self.assertEqual(route.key, CATCH_ALL_X_USERS)
        self.assertEqual(route.kind, "catch_all")
        self.assertIsNone(route.entity_profile)

    def test_subreddit_matched(self):
        route = route_reporting_entity(self.reg, "reddit_post", "politics", None)
        self.assertEqual(route.tier, "public")
        self.assertEqual(route.kind, "subreddit")
        self.assertIsNotNone(route.entity_profile)

    def test_subreddit_unmatched_is_catch_all(self):
        route = route_reporting_entity(self.reg, "reddit_post", "zzznotasub999", None)
        self.assertEqual(route.tier, "public")
        self.assertEqual(route.key, CATCH_ALL_SUBREDDITS)
        self.assertEqual(route.kind, "catch_all")
        self.assertIsNone(route.entity_profile)

    def test_elected_official_account_upgrades_to_officials(self):
        route = route_reporting_entity(
            self.reg, "x_post", "x.com", _FAKE_HANDLE,
            account={"tier": "elected_official", "full_name": "Jane Doe",
                     "party": "D", "office_title": "Senator",
                     "account_type": "personal"},
        )
        self.assertEqual(route.tier, "officials")
        self.assertEqual(route.kind, "account")
        self.assertEqual(route.key, canonicalize_handle(_FAKE_HANDLE))
        self.assertIsNotNone(route.entity_profile)

    def test_affiliated_account_stays_public_named_card(self):
        route = route_reporting_entity(
            self.reg, "x_post", "x.com", _FAKE_HANDLE,
            account={"tier": "affiliated", "full_name": "Some PAC",
                     "party": None, "office_title": None,
                     "account_type": "org"},
        )
        self.assertEqual(route.tier, "public")
        self.assertEqual(route.kind, "account")
        self.assertEqual(route.key, canonicalize_handle(_FAKE_HANDLE))
        self.assertIsNotNone(route.entity_profile)


if __name__ == "__main__":
    unittest.main()
