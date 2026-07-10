"""
D-4: x_posts_raw.is_official_tier now has an analysis-side reader.

The Go ingestor sets is_official_tier=1 on posts pulled via the
verified-officials timeline. Before D-4 no analysis code read the flag, so a
post whose stored handle didn't canonicalize to the editorial officials
registry fell to the general-public tier. This test seeds exactly that case —
an is_official_tier=1 post by an author NOT in verified_officials.yaml — and
asserts the SentimentAggregator routes it into the officials tier (via the
verified-officials provenance sentinel), not general public. A control post
with is_official_tier=0 and the same unmatched handle must still fall to
general public, proving the flag is what moved it.
"""

import json
import sqlite3
import sys
import unittest
import uuid
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.reporting.aggregators import SentimentAggregator
from analysis.src.reporting.aggregators.sentiment import CATCH_ALL_VERIFIED_OFFICIALS
from analysis.src.reporting.entity_registry import CATCH_ALL_X_USERS


def _payload(label: str, confidence: float) -> str:
    return json.dumps({
        "label": label, "confidence": confidence,
        "reasoning": "r", "evidence_spans": ["e"], "sarcasm_detected": False,
    })


class OfficialTierProvenanceRoutingTests(unittest.TestCase):

    def setUp(self):
        self.db_path = f"test_official_tier_{uuid.uuid4()}.db"
        self.conn = sqlite3.connect(self.db_path)
        cur = self.conn.cursor()
        cur.executescript("""
            CREATE TABLE docs (
                doc_id INTEGER PRIMARY KEY,
                source_type TEXT NOT NULL,
                ident TEXT UNIQUE NOT NULL,
                domain_or_subreddit TEXT,
                published_at INTEGER,
                title TEXT,
                text TEXT,
                raw_hash TEXT NOT NULL,
                metadata_json TEXT DEFAULT '{}'
            );
            CREATE TABLE ai_outputs (
                output_id INTEGER PRIMARY KEY,
                doc_id INTEGER,
                task_type TEXT,
                output_json TEXT,
                confidence REAL
            );
            CREATE TABLE x_posts_raw (
                tweet_id TEXT PRIMARY KEY,
                author_id TEXT,
                is_official_tier INTEGER NOT NULL DEFAULT 0,
                retweet_count INTEGER DEFAULT 0,
                reply_count INTEGER DEFAULT 0,
                like_count INTEGER DEFAULT 0,
                quote_count INTEGER DEFAULT 0
            );
            CREATE TABLE x_users_raw (
                user_id TEXT PRIMARY KEY,
                username TEXT
            );
            CREATE TABLE account_profiles (
                profile_id INTEGER PRIMARY KEY AUTOINCREMENT,
                platform TEXT NOT NULL,
                author_id TEXT NOT NULL,
                tier TEXT NOT NULL,
                full_name TEXT,
                party TEXT,
                office_title TEXT,
                account_type TEXT,
                UNIQUE(platform, author_id)
            );
        """)
        ts = 1_735_000_000
        docs = [
            # is_official_tier=1, handle NOT in the officials registry.
            (1, "x_post", "tw_official", None, ts, "Immigration statement", "b", "h1", "{}"),
            # Control: is_official_tier=0, same unmatched handle style.
            (2, "x_post", "tw_public", None, ts, "Immigration take", "b", "h2", "{}"),
        ]
        cur.executemany(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
            "published_at, title, text, raw_hash, metadata_json) VALUES (?,?,?,?,?,?,?,?,?)",
            docs,
        )
        cur.executemany(
            "INSERT INTO x_posts_raw (tweet_id, author_id, is_official_tier) VALUES (?,?,?)",
            [("tw_official", "500", 1), ("tw_public", "501", 0)],
        )
        cur.executemany(
            "INSERT INTO x_users_raw (user_id, username) VALUES (?,?)",
            [("500", "not_a_registered_official"), ("501", "also_not_registered")],
        )
        cur.executemany(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence) VALUES (?,?,?,?)",
            [(1, "sentiment", _payload("POSITIVE", 0.9), 0.9),
             (2, "sentiment", _payload("POSITIVE", 0.9), 0.9)],
        )
        self.conn.commit()
        self.conn.close()

        self.result = SentimentAggregator(self.db_path).get_public_sentiment(
            time_window="all", bot_docs=set(),
        )

    def tearDown(self):
        try:
            Path(self.db_path).unlink()
        except FileNotFoundError:
            pass

    def _keys(self, items):
        return {it.key: it for it in items}

    def test_is_official_tier_post_routes_to_officials(self):
        officials = self._keys(self.result.byOfficial)
        self.assertIn(CATCH_ALL_VERIFIED_OFFICIALS, officials)
        self.assertEqual(officials[CATCH_ALL_VERIFIED_OFFICIALS].volume, 1)
        self.assertEqual(officials[CATCH_ALL_VERIFIED_OFFICIALS].kind, "catch_all")

    def test_control_post_without_flag_stays_public(self):
        public = self._keys(self.result.byGeneralPublic)
        self.assertIn(CATCH_ALL_X_USERS, public)
        self.assertEqual(public[CATCH_ALL_X_USERS].volume, 1)
        # And the flagged post must NOT have leaked into general public.
        officials = self._keys(self.result.byOfficial)
        self.assertNotIn(CATCH_ALL_VERIFIED_OFFICIALS, public)
        self.assertIn(CATCH_ALL_VERIFIED_OFFICIALS, officials)


if __name__ == "__main__":
    unittest.main()
