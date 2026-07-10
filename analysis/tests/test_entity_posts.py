"""
Entity drill-down read-path tests.

The business rule under test: clicking an entity card must be able to show
EVERY classified post the card's numbers counted — paginated, newest first —
not just the handful of samples the snapshot cache keeps. That means the
drill-down filter has to mirror the sentiment aggregator's routing exactly:
same registry matching, same catch-all exclusions (registry + curated
accounts), same bot and confidence floors. A post that moved a card's net
score but is invisible in its drill-down (or vice versa) is an audit bug.
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

from analysis.src.reporting.entity_posts import MAX_PAGE_SIZE, fetch_entity_posts
from analysis.src.reporting.entity_registry import (
    CATCH_ALL_SUBREDDITS, CATCH_ALL_X_USERS,
)


def _sentiment(label: str, conf: float = 0.9) -> str:
    return json.dumps({
        "label": label, "confidence": conf,
        "reasoning": "test reasoning", "evidence_spans": ["a verbatim span"],
    })


class EntityPostsTests(unittest.TestCase):
    def setUp(self):
        self.db_path = f"test_entity_posts_{uuid.uuid4()}.db"
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()
        cur.executescript("""
            CREATE TABLE docs (
                doc_id INTEGER PRIMARY KEY,
                source_type TEXT NOT NULL,
                ident TEXT UNIQUE NOT NULL,
                domain_or_subreddit TEXT,
                published_at INTEGER,
                title TEXT,
                text TEXT,
                raw_hash TEXT NOT NULL
            );
            CREATE TABLE ai_outputs (
                output_id INTEGER PRIMARY KEY,
                doc_id INTEGER,
                task_type TEXT,
                output_json TEXT,
                confidence REAL,
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
                is_official_tier INTEGER NOT NULL DEFAULT 0
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
                UNIQUE(platform, author_id)
            );
        """)
        ts = 1_735_000_000
        docs = [
            # Three nytimes docs at distinct timestamps for pagination/order.
            (1, "news", "https://nytimes.com/1", "www.nytimes.com", ts + 30, "n1", "b", "h1"),
            (2, "news", "https://nytimes.com/2", "www.nytimes.com", ts + 20, "n2", "b", "h2"),
            (3, "news", "https://nytimes.com/3", "nytimes.com", ts + 10, "n3", "b", "h3"),
            # Unmatched-domain news doc (outlet catch-all territory).
            (4, "news", "https://noname.invalid/1", "noname.invalid", ts, "n4", "b", "h4"),
            # Registry official (@POTUS): two posts.
            (5, "x_post", "tw_potus_1", None, ts + 5, None, "b", "h5"),
            (6, "x_post", "tw_potus_2", None, ts + 6, None, "b", "h6"),
            # Curated affiliated account → named card, not catch-all.
            (7, "x_post", "tw_pundit_1", None, ts, None, "b", "h7"),
            # Genuinely untracked X users: one clean, one bot-flagged.
            (8, "x_post", "tw_rando_1", None, ts, None, "b", "h8"),
            (9, "x_post", "tw_rando_2", None, ts, None, "b", "h9"),
            # Reddit: registry-matched + unmatched.
            (10, "reddit_post", "t3_a", "politics", ts, "r1", "b", "h10"),
            (11, "reddit_post", "t3_b", "offbrandsub", ts, "r2", "b", "h11"),
        ]
        cur.executemany(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit, "
            "published_at, title, text, raw_hash) VALUES (?,?,?,?,?,?,?,?)",
            docs,
        )
        cur.executemany(
            "INSERT INTO x_posts_raw (tweet_id, author_id) VALUES (?,?)",
            [
                ("tw_potus_1", "42"), ("tw_potus_2", "42"),
                ("tw_pundit_1", "777"), ("tw_rando_1", "888"), ("tw_rando_2", "999"),
            ],
        )
        cur.executemany(
            "INSERT INTO x_users_raw (user_id, username) VALUES (?,?)",
            [
                ("42", "POTUS"), ("777", "PunditJane"),
                ("888", "rando_one"), ("999", "rando_two"),
            ],
        )
        cur.execute(
            "INSERT INTO account_profiles (platform, author_id, tier) "
            "VALUES ('x', '777', 'affiliated')"
        )
        sentiment_rows = [
            (doc_id, "sentiment", _sentiment("POSITIVE"), 0.9)
            for doc_id in range(1, 12)
        ]
        cur.executemany(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence) "
            "VALUES (?,?,?,?)",
            sentiment_rows,
        )
        # Doc 1 rerun: a second, later sentiment row — only this one counts.
        cur.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence) "
            "VALUES (1, 'sentiment', ?, 0.9)",
            (_sentiment("NEGATIVE"),),
        )
        # Doc 9 is bot-flagged → excluded everywhere. The exclusion filter
        # reads the canonical label column (migration 023), not the JSON.
        cur.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label) "
            "VALUES (9, 'bot_detection', ?, 0.9, 'bot')",
            (json.dumps({"label": "bot"}),),
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        Path(self.db_path).unlink(missing_ok=True)

    def _fetch(self, kind, key, **kw):
        return fetch_entity_posts(self.db_path, kind, key, window="all", **kw)

    def test_items_carry_topic_attribution(self):
        """Drill-down items stamp `topic` (LLM mention topic, title-keyword
        fallback, else General) so the modal's topic filter matches exactly
        instead of re-guessing with client-side keyword lists."""
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()
        cur.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence) "
            "VALUES (1, 'target_sentiment', '{\"targets\": []}', 0.9)"
        )
        cur.execute(
            "INSERT INTO target_mentions (output_id, doc_id, raw_target, stance, "
            "topic, confidence, created_at) "
            "VALUES (?, 1, 'Donald Trump', 'negative', 'Economy', 0.9, 0)",
            (cur.lastrowid,),
        )
        conn.commit()
        conn.close()

        page = self._fetch("outlet", "nytimes.com", limit=10)
        by_id = {i["doc_id"]: i for i in page["items"]}
        self.assertEqual(by_id[1]["topic"], "Economy")
        # No mentions and no title keyword hit → the honest General bucket.
        self.assertEqual(by_id[2]["topic"], "General")

    def test_outlet_pagination_and_order(self):
        page1 = self._fetch("outlet", "nytimes.com", limit=2, offset=0)
        self.assertEqual(page1["total"], 3)
        self.assertEqual([i["doc_id"] for i in page1["items"]], [1, 2])
        page2 = self._fetch("outlet", "nytimes.com", limit=2, offset=2)
        self.assertEqual([i["doc_id"] for i in page2["items"]], [3])

    def test_rerun_doc_counted_once_with_latest_output(self):
        page = self._fetch("outlet", "nytimes.com", limit=10)
        doc1 = [i for i in page["items"] if i["doc_id"] == 1]
        self.assertEqual(len(doc1), 1)
        self.assertEqual(doc1[0]["label"], "NEGATIVE")

    def test_official_matches_registry_handles(self):
        page = self._fetch("official", "potus")
        self.assertEqual(page["total"], 2)
        self.assertTrue(all(i["source_name"] == "POTUS" for i in page["items"]))

    def test_account_matches_single_handle(self):
        page = self._fetch("account", "punditjane")
        self.assertEqual(page["total"], 1)
        self.assertEqual(page["items"][0]["doc_id"], 7)

    def test_catch_all_x_users_excludes_tracked_curated_and_bots(self):
        """The catch-all drill-down must cover exactly what the card counts:
        no registry officials, no curated accounts, no bot-flagged docs."""
        page = self._fetch("catch_all", CATCH_ALL_X_USERS)
        self.assertEqual(page["total"], 1)
        self.assertEqual(page["items"][0]["doc_id"], 8)

    def test_subreddit_and_catch_all_subreddits(self):
        self.assertEqual(self._fetch("subreddit", "politics")["total"], 1)
        page = self._fetch("catch_all", CATCH_ALL_SUBREDDITS)
        self.assertEqual(page["total"], 1)
        self.assertEqual(page["items"][0]["doc_id"], 11)

    def test_limit_is_capped_server_side(self):
        page = self._fetch("outlet", "nytimes.com", limit=5000)
        self.assertEqual(page["limit"], MAX_PAGE_SIZE)

    def test_unknown_entity_raises(self):
        with self.assertRaises(ValueError):
            self._fetch("outlet", "not-a-real-outlet.example")
        with self.assertRaises(ValueError):
            self._fetch("bogus_kind", "anything")


if __name__ == "__main__":
    unittest.main()
