"""
Phase 3b aggregator-level test — verifies SentimentAggregator routes
docs into the correct byNewsOutlet / byOfficial / byGeneralPublic
buckets based on the real entity registry, and that registry-unmatched
docs fall into the catch-all sentinels.

Also covers the per-topic three-way split (``newsNet`` / ``officialsNet``
/ ``publicNet`` on TopicSentiment rows) produced by the aggregator.

Uses a temp SQLite DB seeded with synthetic docs + ai_outputs rows
designed to exercise each routing branch:

    * registry-matched news outlet (www.nytimes.com → nytimes.com)
    * unmatched news outlet → CATCH_ALL_OUTLETS
    * registry-matched official (@POTUS)
    * unmatched X author → CATCH_ALL_X_USERS
    * registry-matched subreddit (r/politics)
    * unmatched subreddit → CATCH_ALL_SUBREDDITS
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
from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS, CATCH_ALL_SUBREDDITS, CATCH_ALL_X_USERS,
)


def _sentiment_payload(label: str, confidence: float, reasoning: str = "sample reasoning") -> str:
    return json.dumps({
        "label": label,
        "confidence": confidence,
        "reasoning": reasoning,
        "evidence_spans": ["sample evidence span"],
        "sarcasm_detected": False,
    })


class SentimentEntityRoutingTests(unittest.TestCase):
    """End-to-end: seed a DB, run the aggregator, assert the three-way
    rollup buckets contain the right entities in the right tiers."""

    def setUp(self):
        self.db_path = f"test_entity_routing_{uuid.uuid4()}.db"
        self.conn = sqlite3.connect(self.db_path)
        cur = self.conn.cursor()
        cur.executescript("""
            CREATE TABLE docs (
                doc_id INTEGER PRIMARY KEY,
                source_type TEXT NOT NULL,
                ident TEXT UNIQUE NOT NULL,
                domain_or_subreddit TEXT,
                published_at INTEGER,
                fetched_at INTEGER,
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
                confidence REAL,
                created_at INTEGER,
                label TEXT
            );
            CREATE VIEW ai_outputs_latest AS
            SELECT a.* FROM ai_outputs a
            WHERE a.output_id = (
                SELECT MAX(a2.output_id) FROM ai_outputs a2
                WHERE a2.doc_id = a.doc_id AND a2.task_type = a.task_type
            );

            CREATE TABLE target_mentions (
                mention_id INTEGER PRIMARY KEY AUTOINCREMENT,
                output_id INTEGER NOT NULL,
                doc_id INTEGER NOT NULL,
                raw_target TEXT NOT NULL,
                entity_key TEXT,
                entity_kind TEXT,
                entity_party TEXT,
                stance TEXT NOT NULL,
                topic TEXT,
                confidence REAL NOT NULL,
                evidence_json TEXT,
                created_at INTEGER NOT NULL
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
                username TEXT,
                name TEXT,
                profile_image_url TEXT,
                verified_type TEXT,
                followers_count INTEGER DEFAULT 0,
                created_at INTEGER,
                description TEXT
            );
            CREATE TABLE reddit_posts_raw (
                fullname TEXT PRIMARY KEY,
                score INTEGER,
                num_comments INTEGER
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

        # Synthetic docs designed to exercise each routing branch.
        # Every title contains "immigration" so TOPIC_KEYWORDS maps them
        # to the Immigration topic; the per-topic three-way split should
        # then have entries for all three tiers.
        ts = 1_735_000_000  # Dec 2024 — always within any standard window
        docs = [
            # News: registry-matched (www.nytimes.com → nytimes.com)
            (1, "news", "https://nytimes.com/a1", "www.nytimes.com", ts, ts,
             "Immigration reform debate", "body", "h1", "{}"),
            # News: registry-matched via alias (bbc.co.uk → bbc.com)
            (2, "news", "https://bbc.co.uk/a2", "www.bbc.co.uk", ts, ts,
             "Immigration bill update", "body", "h2", "{}"),
            # News: unmatched → catch_all_outlets
            (3, "news", "https://noname.invalid/a3", "noname.invalid", ts, ts,
             "Immigration coverage recap", "body", "h3", "{}"),
            # X: matched official (@POTUS, author_id 42)
            (4, "x_post", "tweet_42_1", None, ts, ts,
             "Immigration policy statement", "body", "h4", "{}"),
            # X: unmatched author → catch_all_x_users
            (5, "x_post", "tweet_999_1", None, ts, ts,
             "Immigration take", "body", "h5", "{}"),
            # Reddit: matched subreddit (r/politics)
            (6, "reddit_post", "t3_rp1", "politics", ts, ts,
             "Immigration thread", "body", "h6", "{}"),
            # Reddit: unmatched subreddit → catch_all_subreddits
            (7, "reddit_post", "t3_rp2", "offbrandsub", ts, ts,
             "Immigration post", "body", "h7", "{}"),
            # X: not in registry, but curated as 'affiliated' → named
            # per-account card in the public column, not the catch-all.
            (8, "x_post", "tweet_777_1", None, ts, ts,
             "Immigration commentary", "body", "h8", "{}"),
            # X: not in registry, curated as 'elected_official' → upgraded
            # to the officials column as a named account card.
            (9, "x_post", "tweet_888_1", None, ts, ts,
             "Immigration statement from office", "body", "h9", "{}"),
        ]
        cur.executemany(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
            "published_at, fetched_at, title, text, raw_hash, metadata_json) "
            "VALUES (?,?,?,?,?,?,?,?,?,?)",
            docs,
        )

        # X author → user rows so the aggregator's LEFT JOIN resolves
        # the handle. Handles are stored lowercase so they match the
        # registry's canonicalized form (@POTUS → potus).
        cur.executemany(
            "INSERT INTO x_posts_raw (tweet_id, author_id) VALUES (?, ?)",
            [
                ("tweet_42_1", "42"), ("tweet_999_1", "999"),
                ("tweet_777_1", "777"), ("tweet_888_1", "888"),
            ],
        )
        cur.executemany(
            "INSERT INTO x_users_raw (user_id, username) VALUES (?, ?)",
            [
                ("42", "POTUS"), ("999", "generic_person"),
                ("777", "PunditJane"), ("888", "MayorSmith"),
            ],
        )
        cur.executemany(
            "INSERT INTO account_profiles (platform, author_id, tier, full_name, party, office_title) "
            "VALUES ('x', ?, ?, ?, ?, ?)",
            [
                ("777", "affiliated", "Jane Pundit", "D", None),
                ("888", "elected_official", "Mayor Smith", "R", "Mayor"),
            ],
        )

        # All POSITIVE with high confidence so routing is the only variable
        # worth asserting on. Each doc gets one sentiment row.
        cur.executemany(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence) "
            "VALUES (?, ?, ?, ?)",
            [
                (doc_id, "sentiment", _sentiment_payload("POSITIVE", 0.9), 0.9)
                for doc_id in range(1, 10)
            ],
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

    def _by_key(self, items):
        return {it.key: it for it in items}

    def test_news_outlets_registry_matched(self):
        outlets = self._by_key(self.result.byNewsOutlet)
        # Doc 1 → nytimes.com (direct), Doc 2 → bbc.com (via bbc.co.uk alias).
        self.assertIn("nytimes.com", outlets)
        self.assertIn("bbc.com", outlets)
        self.assertEqual(outlets["nytimes.com"].volume, 1)
        self.assertEqual(outlets["nytimes.com"].kind, "outlet")
        self.assertEqual(outlets["bbc.com"].volume, 1)

    def test_news_outlet_catch_all(self):
        outlets = self._by_key(self.result.byNewsOutlet)
        self.assertIn(CATCH_ALL_OUTLETS, outlets)
        self.assertEqual(outlets[CATCH_ALL_OUTLETS].kind, "catch_all")
        self.assertEqual(outlets[CATCH_ALL_OUTLETS].volume, 1)

    def test_official_registry_matched(self):
        officials = self._by_key(self.result.byOfficial)
        self.assertIn("potus", officials)
        self.assertEqual(officials["potus"].kind, "official")
        self.assertEqual(officials["potus"].volume, 1)

    def test_unmatched_x_author_goes_to_catch_all_x_users(self):
        public = self._by_key(self.result.byGeneralPublic)
        self.assertIn(CATCH_ALL_X_USERS, public)
        self.assertEqual(public[CATCH_ALL_X_USERS].kind, "catch_all")
        self.assertEqual(public[CATCH_ALL_X_USERS].volume, 1)

    def test_subreddit_registry_matched(self):
        public = self._by_key(self.result.byGeneralPublic)
        self.assertIn("politics", public)
        self.assertEqual(public["politics"].kind, "subreddit")
        self.assertEqual(public["politics"].volume, 1)

    def test_unmatched_subreddit_catch_all(self):
        public = self._by_key(self.result.byGeneralPublic)
        self.assertIn(CATCH_ALL_SUBREDDITS, public)
        self.assertEqual(public[CATCH_ALL_SUBREDDITS].kind, "catch_all")
        self.assertEqual(public[CATCH_ALL_SUBREDDITS].volume, 1)

    def test_affiliated_account_gets_named_card_not_catch_all(self):
        """A curated 'affiliated' author must not vanish into "Other X
        users" — the classified data exists, so the reader gets a named,
        source-labeled card."""
        public = self._by_key(self.result.byGeneralPublic)
        self.assertIn("punditjane", public)
        item = public["punditjane"]
        self.assertEqual(item.kind, "account")
        self.assertEqual(item.volume, 1)
        self.assertEqual(item.entity_profile["displayName"], "Jane Pundit")
        self.assertEqual(item.entity_profile["party"], "D")
        # The catch-all still only holds the genuinely unclassified author.
        self.assertEqual(public[CATCH_ALL_X_USERS].volume, 1)

    def test_elected_official_account_upgrades_to_officials_column(self):
        """A curated 'elected_official' author outside the editorial
        registry belongs with officials, not the public."""
        officials = self._by_key(self.result.byOfficial)
        self.assertIn("mayorsmith", officials)
        self.assertEqual(officials["mayorsmith"].kind, "account")
        public = self._by_key(self.result.byGeneralPublic)
        self.assertNotIn("mayorsmith", public)

    def test_entity_profile_attached_for_matched(self):
        """Registry-matched entities should carry the YAML-sourced profile
        payload; catch-alls get the lightweight placeholder."""
        outlets = self._by_key(self.result.byNewsOutlet)
        nyt = outlets["nytimes.com"].entity_profile
        self.assertEqual(nyt["kind"], "outlet")
        self.assertEqual(nyt["displayName"], "The New York Times")
        self.assertIn("lean", nyt)
        self.assertIsNotNone(nyt["lean"])

        other = outlets[CATCH_ALL_OUTLETS].entity_profile
        self.assertEqual(other["kind"], "catch_all")
        self.assertIsNone(other["lean"])

    def test_entities_sorted_volume_desc_catchall_last(self):
        """Real registry entities lead the list; catch-alls sink to the end."""
        items = self.result.byNewsOutlet
        kinds = [it.kind for it in items]
        self.assertEqual(kinds, sorted(kinds, key=lambda k: k == "catch_all"))

    def test_topic_three_way_split_populated(self):
        """The Immigration topic should have net scores for all three
        tiers — every seeded doc has "immigration" in its title so
        TOPIC_KEYWORDS routes them all to the same topic."""
        imm_topics = [t for t in self.result.byTopic if t.topic == "Immigration"]
        self.assertEqual(len(imm_topics), 1)
        t = imm_topics[0]
        self.assertIsNotNone(t.newsNet, "news tier should have a net score")
        self.assertIsNotNone(t.officialsNet, "officials tier should have a net score")
        self.assertIsNotNone(t.publicNet, "public tier should have a net score")
        # All docs are POSITIVE so net scores should be +100.0.
        self.assertEqual(t.newsNet, 100.0)
        self.assertEqual(t.officialsNet, 100.0)
        self.assertEqual(t.publicNet, 100.0)
        # Volumes: 3 news (nyt + bbc + unmatched), 2 officials (registry
        # @POTUS + curated elected_official upgrade), 4 public (1 catch-all
        # X + 1 curated affiliated + 2 reddit).
        self.assertEqual(t.newsVolume, 3)
        self.assertEqual(t.officialsVolume, 2)
        self.assertEqual(t.publicVolume, 4)


if __name__ == "__main__":
    unittest.main()
