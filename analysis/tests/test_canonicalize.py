"""
Tests for analysis/src/common/canonicalize.py -- the shared NEW-STACK
canonicalization functions (URL, news domain, subreddit, entity name).
Pure functions, no DB. Canonicalize-specific cases moved out of
test_engine_citations.py and test_entity_resolver.py when those functions
were consolidated here; those files keep their resolver/engine-behavior
tests.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.common import canonicalize


class CanonicalizeUrlTests(unittest.TestCase):
    """Ported unchanged from the old TestCanonicalizeURL
    (test_narrative_pipeline.py) -- same rules, new module."""

    def test_lowercase_host_and_scheme(self):
        self.assertEqual(
            canonicalize.canonicalize_url("HTTPS://Example.COM/Path"),
            "https://example.com/Path",
        )

    def test_strip_www_and_trailing_slash(self):
        self.assertEqual(
            canonicalize.canonicalize_url("http://www.example.com/path/"),
            "http://example.com/path",
        )

    def test_strip_fragment(self):
        self.assertEqual(
            canonicalize.canonicalize_url("https://example.com/path#section"),
            "https://example.com/path",
        )

    def test_strip_trailing_punctuation(self):
        self.assertEqual(
            canonicalize.canonicalize_url("https://example.com/path,"),
            "https://example.com/path",
        )

    def test_query_is_kept(self):
        self.assertEqual(
            canonicalize.canonicalize_url("https://example.com/path?id=1"),
            "https://example.com/path?id=1",
        )


class CanonicalizeNewsDomainTests(unittest.TestCase):
    def test_strips_www(self):
        self.assertEqual(canonicalize.canonicalize_news_domain("www.nytimes.com"), "nytimes.com")

    def test_already_bare_domain_unchanged(self):
        self.assertEqual(canonicalize.canonicalize_news_domain("nytimes.com"), "nytimes.com")

    def test_lowercases(self):
        self.assertEqual(canonicalize.canonicalize_news_domain("WWW.NYTimes.COM"), "nytimes.com")

    def test_strips_surrounding_whitespace(self):
        self.assertEqual(canonicalize.canonicalize_news_domain("  www.bbc.com  "), "bbc.com")

    def test_none_and_empty_return_none(self):
        self.assertIsNone(canonicalize.canonicalize_news_domain(None))
        self.assertIsNone(canonicalize.canonicalize_news_domain(""))
        self.assertIsNone(canonicalize.canonicalize_news_domain("   "))


class CanonicalizeSubredditTests(unittest.TestCase):
    def test_strips_r_slash_prefix(self):
        self.assertEqual(canonicalize.canonicalize_subreddit("r/politics"), "politics")

    def test_strips_leading_slash_r_slash_prefix(self):
        self.assertEqual(canonicalize.canonicalize_subreddit("/r/politics"), "politics")

    def test_bare_name_unchanged(self):
        self.assertEqual(canonicalize.canonicalize_subreddit("politics"), "politics")

    def test_lowercases(self):
        self.assertEqual(canonicalize.canonicalize_subreddit("PoliticalDiscussion"), "politicaldiscussion")
        self.assertEqual(canonicalize.canonicalize_subreddit("r/Conservative"), "conservative")


class CanonicalizeEntityNameTests(unittest.TestCase):
    def test_none_and_empty_return_none(self):
        self.assertIsNone(canonicalize.canonicalize_entity_name(None))
        self.assertIsNone(canonicalize.canonicalize_entity_name(""))

    def test_lowercases(self):
        self.assertEqual(canonicalize.canonicalize_entity_name("Donald Trump"), "donald trump")

    def test_strips_leading_at(self):
        self.assertEqual(canonicalize.canonicalize_entity_name("@POTUS"), "potus")

    def test_strips_trailing_punctuation(self):
        self.assertEqual(canonicalize.canonicalize_entity_name("Trump."), "trump")
        self.assertEqual(canonicalize.canonicalize_entity_name("Trump,"), "trump")

    def test_strips_possessive(self):
        self.assertEqual(canonicalize.canonicalize_entity_name("Trump's"), "trump")
        self.assertEqual(canonicalize.canonicalize_entity_name("Trump’s"), "trump")

    def test_strips_leading_article(self):
        self.assertEqual(canonicalize.canonicalize_entity_name("the Republican Party"), "republican party")

    def test_strips_leading_title_prefix(self):
        self.assertEqual(canonicalize.canonicalize_entity_name("President Trump"), "trump")
        self.assertEqual(canonicalize.canonicalize_entity_name("Senator Ted Cruz"), "ted cruz")

    def test_collapses_internal_whitespace(self):
        self.assertEqual(canonicalize.canonicalize_entity_name("Donald   Trump"), "donald trump")


if __name__ == "__main__":
    unittest.main()
