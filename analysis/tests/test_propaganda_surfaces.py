"""
Tests for the propaganda aggregator + narrative overlay (walkthrough 043).

Covers:
  - PropagandaAggregator computes overall rate, per-technique counts, and
    a news-vs-social split from ai_outputs rows.
  - Pre-excluded rows (inference_method='deterministic') do not inflate
    denominators.
  - NarrativeAggregator populates propaganda_score and bot_pushed_fraction
    from the joined tables, and returns None when the relevant data is
    absent.
"""

import json
import os
import sqlite3
import sys
import tempfile
import time
import unittest
from typing import Optional

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting.aggregators.propaganda import (
    PropagandaAggregator, FLAG_THRESHOLD,
)
from analysis.src.reporting.aggregators.narrative import NarrativeAggregator

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


def _seed_doc(conn, doc_id, source_type, ident, published_at, text="body", domain=None):
    conn.execute(
        """
        INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                           published_at, text, raw_hash)
        VALUES (?, ?, ?, ?, ?, ?, ?)
        """,
        (doc_id, source_type, ident, domain or source_type, published_at,
         text, f"rh-{doc_id}"),
    )


def _seed_propaganda(
    conn, doc_id, *, techniques, overall_score, confidence=0.9,
    inference_method="llm", created_at=None,
):
    payload = {
        "techniques": [
            {"technique": t, "confidence": 0.8, "evidence_span": "verbatim evidence span here"}
            for t in techniques
        ],
        "overall_propaganda_score": overall_score,
        "reasoning": "test",
    }
    conn.execute(
        """
        INSERT INTO ai_outputs
            (doc_id, task_type, output_json, confidence, inference_method, created_at)
        VALUES (?, 'propaganda', ?, ?, ?, ?)
        """,
        (doc_id, json.dumps(payload), confidence, inference_method, created_at or int(time.time())),
    )


class TestPropagandaAggregator(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # Three news docs: 2 flagged (loaded_language + appeal_to_fear;
        # loaded_language alone), 1 clean.
        _seed_doc(conn, 1, "news", "https://a", now, domain="a.com")
        _seed_propaganda(conn, 1, techniques=["loaded_language", "appeal_to_fear"], overall_score=0.7)
        _seed_doc(conn, 2, "news", "https://b", now, domain="b.com")
        _seed_propaganda(conn, 2, techniques=["loaded_language"], overall_score=0.4)
        _seed_doc(conn, 3, "news", "https://c", now, domain="c.com")
        _seed_propaganda(conn, 3, techniques=[], overall_score=0.0)
        # Two social docs: 1 flagged (ad_hominem), 1 clean.
        _seed_doc(conn, 4, "x_post", "t4", now, domain="x.com")
        _seed_propaganda(conn, 4, techniques=["ad_hominem"], overall_score=0.55)
        _seed_doc(conn, 5, "reddit_post", "t5", now, domain="politics")
        _seed_propaganda(conn, 5, techniques=[], overall_score=0.1)
        # Pre-excluded row — must be ignored by aggregator.
        _seed_doc(conn, 6, "x_post", "t6", now)
        _seed_propaganda(
            conn, 6,
            techniques=["loaded_language"], overall_score=0.9,
            inference_method="deterministic",
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_headline_rate_and_mean_score(self):
        agg = PropagandaAggregator(self.db_path)
        result = agg.get_propaganda_overview(time_window="all")
        # 5 eligible docs (pre-excluded not counted).
        self.assertEqual(result.total_eligible_docs, 5)
        # Flagged = docs with score >= FLAG_THRESHOLD AND len(techniques) > 0.
        # Doc1 (0.7), Doc2 (0.4), Doc4 (0.55) qualify. Doc3 has score 0. Doc5 score 0.1.
        self.assertEqual(result.flagged_docs, 3)
        self.assertAlmostEqual(result.propaganda_rate_pct, 60.0, places=1)
        self.assertGreater(result.mean_score, 0.0)

    def test_technique_breakdown(self):
        agg = PropagandaAggregator(self.db_path)
        result = agg.get_propaganda_overview(time_window="all")
        by_tech = {tc.technique: tc.count for tc in result.by_technique}
        # loaded_language appears in docs 1 + 2 = 2 flagged; doc 6 excluded
        self.assertEqual(by_tech["loaded_language"], 2)
        self.assertEqual(by_tech["appeal_to_fear"], 1)
        self.assertEqual(by_tech["ad_hominem"], 1)
        # Techniques never flagged should be 0.
        self.assertEqual(by_tech["whataboutism"], 0)
        self.assertEqual(by_tech["doubt_casting"], 0)

    def test_news_vs_social_split(self):
        agg = PropagandaAggregator(self.db_path)
        result = agg.get_propaganda_overview(time_window="all")
        by_label = {s.label: s for s in result.by_source}
        # News: 3 total, 2 flagged
        self.assertEqual(by_label["News"].total_docs, 3)
        self.assertEqual(by_label["News"].flagged_docs, 2)
        # Social: 2 total (pre-excluded doc 6 stripped), 1 flagged
        self.assertEqual(by_label["Social Media"].total_docs, 2)
        self.assertEqual(by_label["Social Media"].flagged_docs, 1)


class TestNarrativeOverlaySignals(unittest.TestCase):
    """Walkthrough 043: NarrativeSummary carries propaganda_score and
    bot_pushed_fraction derived from supporting docs.
    """

    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)

        # Narrative with two X-post supporters. One author has a high bot
        # score; the other has a clean score.
        conn.execute(
            "INSERT INTO x_users_raw (user_id, username, fetched_at, raw_hash) "
            "VALUES ('u_bot', 'bot', ?, 'rh-u-bot')", (now,),
        )
        conn.execute(
            "INSERT INTO x_users_raw (user_id, username, fetched_at, raw_hash) "
            "VALUES ('u_human', 'human', ?, 'rh-u-human')", (now,),
        )
        conn.execute(
            """
            INSERT INTO x_posts_raw (tweet_id, author_id, created_at, fetched_at,
                                      text, raw_hash, extraction_version)
            VALUES ('t1', 'u_bot', ?, ?, 'body', 'rh-t1', 'v1')
            """,
            (now, now),
        )
        conn.execute(
            """
            INSERT INTO x_posts_raw (tweet_id, author_id, created_at, fetched_at,
                                      text, raw_hash, extraction_version)
            VALUES ('t2', 'u_human', ?, ?, 'body', 'rh-t2', 'v1')
            """,
            (now, now),
        )
        _seed_doc(conn, 1, "x_post", "t1", now)
        _seed_doc(conn, 2, "x_post", "t2", now)
        # Propaganda on both docs — one high, one moderate.
        _seed_propaganda(conn, 1, techniques=["loaded_language"], overall_score=0.8)
        _seed_propaganda(conn, 2, techniques=[], overall_score=0.2)
        # One narrative with both docs.
        conn.execute(
            """
            INSERT INTO narratives
                (narrative_id, name, description, first_seen_at,
                 first_seen_doc_id, created_at, updated_at)
            VALUES (1, 'Claim X', 'Claim X', ?, 1, ?, ?)
            """,
            (now, now, now),
        )
        for doc_id in (1, 2):
            conn.execute(
                """
                INSERT INTO narrative_docs
                    (narrative_id, doc_id, discovered_at, confidence)
                VALUES (1, ?, ?, 0.9)
                """,
                (doc_id, now),
            )
        # Author bot scores: u_bot flagged, u_human clean.
        conn.execute(
            """
            INSERT INTO author_bot_scores
                (platform, author_id, score, variance, sample_count,
                 bot_post_count, suspicious_post_count, updated_at)
            VALUES ('x', 'u_bot', 0.85, 0.0, 3, 2, 1, ?)
            """,
            (now,),
        )
        conn.execute(
            """
            INSERT INTO author_bot_scores
                (platform, author_id, score, variance, sample_count,
                 bot_post_count, suspicious_post_count, updated_at)
            VALUES ('x', 'u_human', 0.1, 0.0, 5, 0, 0, ?)
            """,
            (now,),
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_narrative_includes_propaganda_and_bot_signals(self):
        agg = NarrativeAggregator(self.db_path)
        results = agg.get_top_narratives(time_window="all", limit=5)
        self.assertEqual(len(results), 1)
        n = results[0]
        # Mean propaganda = (0.8 + 0.2) / 2 = 0.5
        self.assertIsNotNone(n.propaganda_score)
        self.assertAlmostEqual(n.propaganda_score, 0.5, places=2)
        # 1 of 2 unique X authors is bot-pushed → 0.5
        self.assertIsNotNone(n.bot_pushed_fraction)
        self.assertAlmostEqual(n.bot_pushed_fraction, 0.5, places=2)


class TestNarrativeOverlayNullsWhenNoData(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        # News-only narrative — no propaganda rows, no X authors. Both overlay
        # signals should return None.
        _seed_doc(conn, 1, "news", "https://a", now)
        conn.execute(
            """
            INSERT INTO narratives
                (narrative_id, name, description, first_seen_at,
                 first_seen_doc_id, created_at, updated_at)
            VALUES (1, 'Claim A', 'Claim A', ?, 1, ?, ?)
            """,
            (now, now, now),
        )
        conn.execute(
            """
            INSERT INTO narrative_docs
                (narrative_id, doc_id, discovered_at, confidence)
            VALUES (1, 1, ?, 0.9)
            """,
            (now,),
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_both_overlay_fields_are_none(self):
        agg = NarrativeAggregator(self.db_path)
        results = agg.get_top_narratives(time_window="all", limit=5)
        self.assertEqual(len(results), 1)
        self.assertIsNone(results[0].propaganda_score)
        self.assertIsNone(results[0].bot_pushed_fraction)


# --------------------------------------------------------------------------- #
#  Phase 3b propaganda entity routing — walkthrough 058.                      #
#  Verifies PropagandaAggregator buckets each scored doc into the correct
#  byNewsOutlet / byOfficial / byGeneralPublic list with per-entity
#  flagged_rate_pct + mean_score + profile payload.
# --------------------------------------------------------------------------- #

from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS, CATCH_ALL_SUBREDDITS, CATCH_ALL_X_USERS,
)


class TestPropagandaEntityRouting(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        self.now = int(time.time())
        self.conn = sqlite3.connect(self.db_path)
        # Seed two X authors: @POTUS (in verified_officials.yaml) + a
        # generic unmatched account.
        self.conn.execute(
            "INSERT INTO x_users_raw (user_id, username, fetched_at, raw_hash) "
            "VALUES ('u_potus', 'POTUS', ?, 'rh-u1')",
            (self.now,),
        )
        self.conn.execute(
            "INSERT INTO x_users_raw (user_id, username, fetched_at, raw_hash) "
            "VALUES ('u_rand', 'random_user', ?, 'rh-u2')",
            (self.now,),
        )
        self.conn.commit()

    def tearDown(self):
        self.conn.close()
        os.unlink(self.db_path)

    def _seed_x_post(self, tweet_id: str, author_id: str) -> None:
        self.conn.execute(
            "INSERT INTO x_posts_raw (tweet_id, author_id, text, created_at, "
            "fetched_at, raw_hash, extraction_version) "
            "VALUES (?, ?, 'body', ?, ?, 'rh-p', '1.0')",
            (tweet_id, author_id, self.now, self.now),
        )

    def _seed_doc(self, doc_id: int, source_type: str, ident: str, domain: Optional[str] = None) -> None:
        _seed_doc(
            self.conn, doc_id, source_type, ident, self.now,
            domain=domain or source_type,
        )

    def _by_key(self, items):
        return {it.key: it for it in items}

    def test_news_outlet_rollup(self):
        """Matched + unmatched news outlets populate byNewsOutlet with
        per-entity flagged rate and the NYT registry profile attached."""
        self._seed_doc(1, "news", "https://nytimes.com/a1", "www.nytimes.com")
        _seed_propaganda(self.conn, 1, techniques=["loaded_language"], overall_score=0.8)
        self._seed_doc(2, "news", "https://nytimes.com/a2", "www.nytimes.com")
        _seed_propaganda(self.conn, 2, techniques=[], overall_score=0.1)  # unflagged
        self._seed_doc(3, "news", "https://other.invalid/a3", "other.invalid")
        _seed_propaganda(self.conn, 3, techniques=["name_calling"], overall_score=0.6)
        self.conn.commit()

        result = PropagandaAggregator(self.db_path).get_propaganda_overview(time_window="all")
        outlets = self._by_key(result.by_news_outlet)

        self.assertIn("nytimes.com", outlets)
        nyt = outlets["nytimes.com"]
        self.assertEqual(nyt.total_docs, 2)
        self.assertEqual(nyt.flagged_docs, 1)
        self.assertEqual(nyt.flagged_rate_pct, 50.0)
        self.assertEqual(nyt.entity_profile["displayName"], "The New York Times")

        self.assertIn(CATCH_ALL_OUTLETS, outlets)
        self.assertEqual(outlets[CATCH_ALL_OUTLETS].total_docs, 1)
        self.assertEqual(outlets[CATCH_ALL_OUTLETS].flagged_docs, 1)

    def test_official_rollup_and_catch_all_x_users(self):
        self._seed_x_post("t_potus_1", "u_potus")
        self._seed_doc(10, "x_post", "t_potus_1")
        _seed_propaganda(self.conn, 10, techniques=["appeal_to_fear"], overall_score=0.7)

        self._seed_x_post("t_rand_1", "u_rand")
        self._seed_doc(11, "x_post", "t_rand_1")
        _seed_propaganda(self.conn, 11, techniques=["loaded_language"], overall_score=0.5)
        self.conn.commit()

        result = PropagandaAggregator(self.db_path).get_propaganda_overview(time_window="all")
        officials = self._by_key(result.by_official)
        public = self._by_key(result.by_general_public)

        self.assertIn("potus", officials)
        self.assertEqual(officials["potus"].total_docs, 1)
        self.assertEqual(officials["potus"].flagged_docs, 1)
        self.assertEqual(officials["potus"].entity_profile["kind"], "official")

        self.assertIn(CATCH_ALL_X_USERS, public)
        self.assertEqual(public[CATCH_ALL_X_USERS].kind, "catch_all")

    def test_subreddit_rollup(self):
        self._seed_doc(20, "reddit_post", "t3_rp1", domain="politics")
        _seed_propaganda(self.conn, 20, techniques=["ad_hominem"], overall_score=0.4)
        self._seed_doc(21, "reddit_post", "t3_rp2", domain="offbrand_sub")
        _seed_propaganda(self.conn, 21, techniques=[], overall_score=0.05)
        self.conn.commit()

        result = PropagandaAggregator(self.db_path).get_propaganda_overview(time_window="all")
        public = self._by_key(result.by_general_public)

        self.assertIn("politics", public)
        self.assertEqual(public["politics"].entity_profile["kind"], "subreddit")
        self.assertIn(CATCH_ALL_SUBREDDITS, public)
        self.assertEqual(public[CATCH_ALL_SUBREDDITS].kind, "catch_all")

    def test_entity_items_sorted_by_mean_score_catch_all_last(self):
        # Two outlets with different mean scores + a catch-all — catch-all
        # should sort last regardless of its mean score.
        self._seed_doc(30, "news", "https://nytimes.com/a30", "www.nytimes.com")
        _seed_propaganda(self.conn, 30, techniques=[], overall_score=0.2)
        self._seed_doc(31, "news", "https://bbc.com/a31", "www.bbc.com")
        _seed_propaganda(self.conn, 31, techniques=["loaded_language"], overall_score=0.9)
        self._seed_doc(32, "news", "https://other.invalid/a32", "other.invalid")
        _seed_propaganda(self.conn, 32, techniques=["loaded_language"], overall_score=0.95)
        self.conn.commit()

        result = PropagandaAggregator(self.db_path).get_propaganda_overview(time_window="all")
        order = [(it.kind, it.key) for it in result.by_news_outlet]
        # bbc.com's mean (0.9) > nytimes.com (0.2); catch-all mean (0.95)
        # is highest but must sink to the end.
        self.assertEqual(order[0][1], "bbc.com")
        self.assertEqual(order[1][1], "nytimes.com")
        self.assertEqual(order[2][0], "catch_all")


if __name__ == "__main__":
    unittest.main()
