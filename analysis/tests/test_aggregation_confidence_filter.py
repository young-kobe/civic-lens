"""
Tests for the aggregation_min_confidence pre-filter introduced in
walkthrough 039. Low-confidence ai_outputs rows must not move headline
aggregates, and low-confidence bot flags must not cause exclusion.
"""

import json
import os
import sqlite3
import sys
import tempfile
import time
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting.aggregators.base import get_bot_flagged_doc_ids
from analysis.src.reporting.aggregators.narrative import NarrativeAggregator
from analysis.src.reporting.aggregators.geo import GeoAggregator

MIGRATIONS_DIR = os.path.join(project_root, "data", "migrations")


def _apply_migrations(db_path: str) -> None:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    for name in sorted(os.listdir(MIGRATIONS_DIR)):
        if name.endswith(".sql"):
            with open(os.path.join(MIGRATIONS_DIR, name)) as f:
                cursor.executescript(f.read())
    conn.commit()
    conn.close()


def _seed_doc(conn, doc_id: int, source_type: str, ident: str,
              published_at: int, text: str = "body",
              country_code: str = None):
    conn.execute(
        """
        INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                           published_at, text, raw_hash, place_country_code)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?)
        """,
        (doc_id, source_type, ident, source_type, published_at, text,
         f"rh-{doc_id}", country_code),
    )


def _seed_ai_output(conn, doc_id: int, task_type: str, output: dict,
                     confidence: float):
    conn.execute(
        """
        INSERT INTO ai_outputs
            (doc_id, task_type, output_json, confidence, created_at)
        VALUES (?, ?, ?, ?, ?)
        """,
        (doc_id, task_type, json.dumps(output), confidence, int(time.time())),
    )


class TestBotFlagConfidenceFilter(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        _seed_doc(conn, 1, "x_post", "t1", now)  # high-conf bot
        _seed_doc(conn, 2, "x_post", "t2", now)  # low-conf bot
        _seed_doc(conn, 3, "news",   "n1", now)  # high-conf bot but news → never excluded
        _seed_ai_output(conn, 1, "bot_detection",
                         {"label": "bot", "is_bot": True}, 0.9)
        _seed_ai_output(conn, 2, "bot_detection",
                         {"label": "bot", "is_bot": True}, 0.3)
        _seed_ai_output(conn, 3, "bot_detection",
                         {"label": "bot", "is_bot": True}, 0.9)
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_low_confidence_bot_flag_does_not_exclude(self):
        bots = get_bot_flagged_doc_ids(self.db_path, min_confidence=0.5)
        self.assertIn(1, bots)          # high-conf bot → excluded
        self.assertNotIn(2, bots)       # low-conf bot → NOT excluded
        self.assertNotIn(3, bots)       # news doc is never bot-excluded

    def test_zero_threshold_includes_all(self):
        bots = get_bot_flagged_doc_ids(self.db_path, min_confidence=0.0)
        self.assertEqual(bots, {1, 2})


class TestNarrativeNetSentimentConfidenceFilter(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # One narrative, three supporting docs. Two high-conf POSITIVE,
        # one low-conf NEGATIVE. Without the filter, net would be ~+33; with
        # the filter at 0.5 the negative drops and net becomes +100.
        for doc_id in (1, 2, 3):
            _seed_doc(conn, doc_id, "news", f"n{doc_id}", now)
        _seed_ai_output(conn, 1, "sentiment", {"label": "POSITIVE"}, 0.9)
        _seed_ai_output(conn, 2, "sentiment", {"label": "POSITIVE"}, 0.9)
        _seed_ai_output(conn, 3, "sentiment", {"label": "NEGATIVE"}, 0.2)

        conn.execute(
            """
            INSERT INTO narratives (narrative_id, name, description,
                                     first_seen_at, first_seen_doc_id,
                                     created_at, updated_at)
            VALUES (1, 'Claim A', 'Claim A', ?, 1, ?, ?)
            """,
            (now, now, now),
        )
        for doc_id in (1, 2, 3):
            conn.execute(
                """
                INSERT INTO narrative_docs (narrative_id, doc_id,
                                             discovered_at, confidence)
                VALUES (1, ?, ?, 0.9)
                """,
                (doc_id, now),
            )
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_low_confidence_negative_dropped(self):
        """At aggregation_min_confidence=0.5 the low-conf NEGATIVE is ignored;
        net sentiment is the average of the two confident POSITIVEs."""
        agg = NarrativeAggregator(self.db_path)
        results = agg.get_top_narratives(time_window="all", limit=10)
        self.assertEqual(len(results), 1)
        # (0.9 + 0.9) / 2 = 0.9 → 90.0%
        self.assertAlmostEqual(results[0].net_sentiment, 90.0, places=1)


class TestGeoSentimentConfidenceFilter(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # US posts: one high-conf POSITIVE, one low-conf NEGATIVE.
        _seed_doc(conn, 1, "x_post", "t1", now, country_code="US")
        _seed_doc(conn, 2, "x_post", "t2", now, country_code="US")
        _seed_ai_output(conn, 1, "sentiment", {"label": "POSITIVE"}, 0.9)
        _seed_ai_output(conn, 2, "sentiment", {"label": "NEGATIVE"}, 0.2)
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_post_count_unaffected_low_conf_sentiment_dropped(self):
        """Both posts count toward the country's post_count; only the high-conf
        POSITIVE contributes to the color/avg. The low-conf NEGATIVE becomes
        'no sentiment' for the colored aggregate."""
        agg = GeoAggregator(self.db_path)
        result = agg.get_country_sentiment(time_window="all")
        countries = {c["country_code"]: c for c in result["countries"]}
        self.assertIn("US", countries)
        us = countries["US"]
        # Two posts (geo coverage counts both), one colored sample.
        self.assertEqual(us["post_count"], 2)
        # avg_sentiment = 1 * 0.9 = 0.9 (only the POSITIVE contributes).
        self.assertAlmostEqual(us["avg_sentiment"], 0.9, places=2)


if __name__ == "__main__":
    unittest.main()
