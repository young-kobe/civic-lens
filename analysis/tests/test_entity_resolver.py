"""
Tests for analysis/src/common/entity_resolver.py -- Postgres redesign Phase 6.

Hermetic: a fake connection stands in for corpus.entities/entity_aliases so
these tests never need a live Postgres.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.common.entity_resolver import EntityResolver


class _FakeResult:
    def __init__(self, rows):
        self._rows = rows

    def fetchall(self):
        return self._rows


class _FakeConn:
    """Returns entity_rows for the corpus.entities query and alias_rows for
    the corpus.entity_aliases query, distinguished by query text -- the
    resolver issues exactly these two queries."""

    def __init__(self, entity_rows, alias_rows=()):
        self._entity_rows = entity_rows
        self._alias_rows = alias_rows

    def execute(self, query, params=None):
        if "entity_aliases" in query:
            return _FakeResult(list(self._alias_rows))
        return _FakeResult(list(self._entity_rows))


def _entity_row(entity_id, entity_key, display_name, active=True):
    return {"entity_id": entity_id, "entity_key": entity_key, "display_name": display_name, "active": active}


def _alias_row(entity_id, alias):
    return {"entity_id": entity_id, "alias": alias}


class EntityResolverExactMatchTests(unittest.TestCase):
    def test_resolves_by_entity_key(self):
        conn = _FakeConn([_entity_row(1, "trump", "Donald Trump")])
        resolver = EntityResolver(conn)
        self.assertEqual(resolver.resolve("trump"), 1)

    def test_resolves_by_display_name(self):
        conn = _FakeConn([_entity_row(1, "trump", "Donald Trump")])
        resolver = EntityResolver(conn)
        self.assertEqual(resolver.resolve("Donald Trump"), 1)

    def test_resolves_with_title_prefix_and_punctuation(self):
        conn = _FakeConn([_entity_row(1, "trump", "Donald Trump")])
        resolver = EntityResolver(conn)
        self.assertEqual(resolver.resolve("President Trump."), 1)

    def test_unresolved_name_returns_none(self):
        conn = _FakeConn([_entity_row(1, "trump", "Donald Trump")])
        resolver = EntityResolver(conn)
        self.assertIsNone(resolver.resolve("Nikki Haley"))

    def test_none_input_returns_none(self):
        conn = _FakeConn([_entity_row(1, "trump", "Donald Trump")])
        resolver = EntityResolver(conn)
        self.assertIsNone(resolver.resolve(None))


class EntityResolverAliasTests(unittest.TestCase):
    def test_resolves_by_alias(self):
        conn = _FakeConn(
            [_entity_row(1, "gop", "Republican Party")],
            [_alias_row(1, "GOP")],
        )
        resolver = EntityResolver(conn)
        self.assertEqual(resolver.resolve("gop"), 1)

    def test_alias_of_inactive_entity_excluded(self):
        conn = _FakeConn(
            [_entity_row(1, "old-key", "Old Entity", active=False)],
            [_alias_row(1, "old-alias")],
        )
        resolver = EntityResolver(conn)
        self.assertIsNone(resolver.resolve("old-alias"))


class EntityResolverLastNameTests(unittest.TestCase):
    def test_unambiguous_last_name_resolves(self):
        conn = _FakeConn([_entity_row(1, "ron-desantis", "Ron DeSantis")])
        resolver = EntityResolver(conn)
        self.assertEqual(resolver.resolve("DeSantis"), 1)

    def test_ambiguous_last_name_does_not_resolve(self):
        conn = _FakeConn([
            _entity_row(1, "kamala-harris", "Kamala Harris"),
            _entity_row(2, "andy-harris", "Andy Harris"),
        ])
        resolver = EntityResolver(conn)
        self.assertIsNone(resolver.resolve("Harris"))

    def test_generational_suffix_ignored_when_deriving_last_name(self):
        conn = _FakeConn([_entity_row(1, "rfk-jr", "Robert F. Kennedy Jr.")])
        resolver = EntityResolver(conn)
        self.assertEqual(resolver.resolve("Kennedy"), 1)


class EntityResolverInactiveExclusionTests(unittest.TestCase):
    def test_inactive_entity_never_resolves(self):
        conn = _FakeConn([_entity_row(1, "retired-key", "Retired Person", active=False)])
        resolver = EntityResolver(conn)
        self.assertIsNone(resolver.resolve("Retired Person"))
        self.assertIsNone(resolver.resolve("retired-key"))

    def test_active_and_inactive_entities_coexist_correctly(self):
        conn = _FakeConn([
            _entity_row(1, "active-key", "Active Person", active=True),
            _entity_row(2, "inactive-key", "Inactive Person", active=False),
        ])
        resolver = EntityResolver(conn)
        self.assertEqual(resolver.resolve("Active Person"), 1)
        self.assertIsNone(resolver.resolve("Inactive Person"))


if __name__ == "__main__":
    unittest.main()
