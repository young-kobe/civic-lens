"""
Tests for analysis/src/api/queries/base.py -- Postgres redesign Phase 9
strictly-live pre-wave (window cutoff, admission_class predicates, and the
evidence-sample row builder every panel query will use).

Two tiers, matching the repo's established convention
(analysis/tests/test_engine_citations.py, test_engine_account_tier.py):

  1. Pure-core tests (no DB) -- window_cutoff, admission_label,
     split_admission_counts, build_sample_doc.
  2. An integration test gated on CIVIC_TEST_DATABASE_URL, confirming
     build_sample_doc works against a real corpus.documents-shaped row
     (not just a synthetic dict) with 0001-0004 applied.
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timedelta, timezone

from analysis.src.api.queries import base
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class WindowCutoffTests(unittest.TestCase):
    def test_24h_cutoff_is_about_a_day_ago(self):
        cutoff = base.window_cutoff("24h")
        expected = datetime.now(timezone.utc) - timedelta(hours=24)
        self.assertLess(abs((cutoff - expected).total_seconds()), 5)

    def test_90d_cutoff_is_about_90_days_ago(self):
        cutoff = base.window_cutoff("90d")
        expected = datetime.now(timezone.utc) - timedelta(days=90)
        self.assertLess(abs((cutoff - expected).total_seconds()), 5)

    def test_unknown_window_raises(self):
        with self.assertRaises(ValueError):
            base.window_cutoff("all")


class ResolveTimeRangeTests(unittest.TestCase):
    """Presets are conveniences over arbitrary ranges (owner decision
    2026-07-24): historical data stays queryable forever, so 'all' and
    explicit date bounds must resolve, not error."""

    def test_preset_window_resolves_to_lower_bound_only(self):
        start, end = base.resolve_time_range(window="7d")
        expected = datetime.now(timezone.utc) - timedelta(days=7)
        self.assertLess(abs((start - expected).total_seconds()), 5)
        self.assertIsNone(end)

    def test_all_resolves_unbounded(self):
        self.assertEqual(base.resolve_time_range(window="all"), (None, None))

    def test_explicit_range_passes_through(self):
        date_from = datetime(2026, 1, 1, tzinfo=timezone.utc)
        date_to = datetime(2026, 3, 1, tzinfo=timezone.utc)
        self.assertEqual(
            base.resolve_time_range(date_from=date_from, date_to=date_to),
            (date_from, date_to),
        )

    def test_open_ended_from_is_allowed(self):
        date_from = datetime(2026, 1, 1, tzinfo=timezone.utc)
        self.assertEqual(base.resolve_time_range(date_from=date_from), (date_from, None))

    def test_window_and_explicit_range_together_rejected(self):
        with self.assertRaises(ValueError):
            base.resolve_time_range(
                window="7d", date_from=datetime(2026, 1, 1, tzinfo=timezone.utc)
            )

    def test_nothing_at_all_rejected(self):
        with self.assertRaises(ValueError):
            base.resolve_time_range()

    def test_inverted_range_rejected(self):
        with self.assertRaises(ValueError):
            base.resolve_time_range(
                date_from=datetime(2026, 3, 1, tzinfo=timezone.utc),
                date_to=datetime(2026, 1, 1, tzinfo=timezone.utc),
            )

    def test_unknown_window_rejected(self):
        with self.assertRaises(ValueError):
            base.resolve_time_range(window="14d")


class AdmissionHelpersTests(unittest.TestCase):
    def test_admission_label_sampled(self):
        self.assertEqual(base.admission_label("sampled"), "sampled discourse")

    def test_admission_label_official_record(self):
        self.assertEqual(base.admission_label("official_record"), "official record")

    def test_admission_label_unknown_raises(self):
        with self.assertRaises(ValueError):
            base.admission_label("bogus")

    def test_split_admission_counts(self):
        rows = [
            {"admission_class": "sampled"},
            {"admission_class": "sampled"},
            {"admission_class": "official_record"},
        ]
        self.assertEqual(base.split_admission_counts(rows), (2, 1))

    def test_split_admission_counts_unknown_raises(self):
        with self.assertRaises(ValueError):
            base.split_admission_counts([{"admission_class": "bogus"}])


class BuildSampleDocTests(unittest.TestCase):
    def test_builds_full_sample_from_row(self):
        row = {
            "doc_id": 1, "source_url": "https://example.com/a",
            "snippet": "a headline", "confidence": 0.9,
            "published_at": "2026-07-24T00:00:00Z",
        }
        sample = base.build_sample_doc(row, admission_class="sampled")
        self.assertEqual(sample["doc_id"], 1)
        self.assertEqual(sample["source_url"], "https://example.com/a")
        self.assertEqual(sample["admission_class"], "sampled")

    def test_missing_source_url_raises(self):
        row = {"doc_id": 1, "source_url": None, "confidence": 0.9}
        with self.assertRaises(ValueError):
            base.build_sample_doc(row, admission_class="sampled")

    def test_missing_confidence_raises(self):
        row = {"doc_id": 1, "source_url": "https://example.com/a", "confidence": None}
        with self.assertRaises(ValueError):
            base.build_sample_doc(row, admission_class="sampled")


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class BuildSampleDocAgainstRealRowTests(unittest.TestCase):
    """Confirms build_sample_doc works against a dict_row straight off
    corpus.documents (0001-0004 applied, serving schema dropped) -- not
    just a synthetic dict."""

    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._seed_doc()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _seed_doc(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute("TRUNCATE corpus.documents, raw.articles, raw.pages CASCADE")
            conn.execute(
                "INSERT INTO raw.pages (url_canon, url_raw, domain) VALUES "
                "('https://example.com/a', 'https://example.com/a', 'example.com')"
            )
            conn.execute(
                "INSERT INTO raw.articles (url_canon, fetched_at, raw_hash, extraction_version) "
                "VALUES ('https://example.com/a', now(), 'h'||repeat('0', 63), 'v1')"
            )
            row = conn.execute(
                "INSERT INTO corpus.documents (source_type, natural_key, published_at, body, "
                "source_url, raw_hash, etl_version) VALUES "
                "('news', 'https://example.com/a', now(), 'body text', "
                "'https://example.com/a', 'h'||repeat('0', 63), 'pg-1') RETURNING doc_id"
            ).fetchone()
            self._doc_id = row[0]

    def test_build_sample_doc_from_real_row(self):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "SELECT doc_id, source_url, published_at, title AS snippet, %(conf)s AS confidence "
                "FROM corpus.documents WHERE doc_id = %(doc_id)s",
                {"conf": 0.8, "doc_id": self._doc_id},
            ).fetchone()
        sample = base.build_sample_doc(row, admission_class="sampled")
        self.assertEqual(sample["doc_id"], self._doc_id)
        self.assertEqual(sample["source_url"], "https://example.com/a")
        self.assertEqual(sample["confidence"], 0.8)
        self.assertEqual(sample["admission_class"], "sampled")


if __name__ == "__main__":
    unittest.main()
