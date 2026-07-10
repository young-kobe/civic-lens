"""
Phase 2 enrichment contract tests (UI depth overhaul).

Business rules under test:

* toneTrend — the per-day per-tier series must suppress (net=None) any
  tier-day below the sample floor rather than draw a fake +/-100 spike,
  while always reporting the honest volume; dates ascend.
* Sample engagement/author — X samples carry collection-time engagement
  counts and the author's stored metadata; reddit POSTS carry score and
  comment counts; news carries neither; reddit COMMENTS carry neither
  (their raw table was dropped in migration 005) — we never fabricate a
  shape for data that isn't stored.
* Per-example bot evidence — every flagged example carries the WHY: flag
  confidence, humanized indicators (snake_case slugs never render raw),
  and the model's reasoning.
* Narrative amplification ids — must be the REAL narrative_id so the UI's
  "#narratives?open=<id>" deep link resolves; a synthetic 1..3 index sent
  readers to the wrong story.
"""

import json
import os
import sqlite3
import sys
import tempfile
import time
import unittest
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.reporting.aggregators.bot import BotAggregator
from analysis.src.reporting.aggregators.sentiment import SentimentAggregator

NOW = int(time.time())


def _sentiment(label: str, conf: float = 0.9) -> str:
    return json.dumps({
        "label": label, "confidence": conf,
        "reasoning": "test reasoning", "evidence_spans": ["a verbatim span"],
    })


class SampleEnrichmentTests(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()
        cur.executescript("""
            CREATE TABLE docs (
                doc_id INTEGER PRIMARY KEY, source_type TEXT, ident TEXT,
                domain_or_subreddit TEXT, published_at INTEGER,
                title TEXT, text TEXT
            );
            CREATE TABLE ai_outputs (
                output_id INTEGER PRIMARY KEY, doc_id INT, task_type TEXT,
                output_json TEXT, confidence REAL, label TEXT,
                inference_method TEXT
            );
            CREATE VIEW ai_outputs_latest AS SELECT * FROM ai_outputs;
            CREATE TABLE x_posts_raw (
                tweet_id TEXT PRIMARY KEY, author_id TEXT,
                is_official_tier INTEGER DEFAULT 0,
                retweet_count INTEGER, reply_count INTEGER,
                like_count INTEGER, quote_count INTEGER
            );
            CREATE TABLE x_users_raw (
                user_id TEXT PRIMARY KEY, username TEXT, name TEXT,
                profile_image_url TEXT, verified_type TEXT,
                followers_count INTEGER, created_at INTEGER
            );
            CREATE TABLE reddit_posts_raw (
                fullname TEXT PRIMARY KEY, score INTEGER, num_comments INTEGER
            );
            CREATE TABLE account_profiles (
                platform TEXT, author_id TEXT, tier TEXT, full_name TEXT,
                party TEXT, office_title TEXT, account_type TEXT
            );
            CREATE TABLE target_mentions (
                mention_id INTEGER PRIMARY KEY AUTOINCREMENT,
                output_id INTEGER, doc_id INTEGER, raw_target TEXT,
                entity_key TEXT, entity_kind TEXT, entity_party TEXT,
                stance TEXT, topic TEXT, confidence REAL, evidence_json TEXT
            );
            CREATE TABLE narratives (
                narrative_id INTEGER PRIMARY KEY, name TEXT
            );
            CREATE TABLE narrative_docs (
                narrative_id INTEGER, doc_id INTEGER
            );
        """)

        # Six same-day news docs (clears the daily suppression floor),
        # one X post + one reddit post + one reddit comment on the same day.
        for i in range(6):
            cur.execute(
                "INSERT INTO docs VALUES (?,?,?,?,?,?,?)",
                (i + 1, "news", f"https://ex.com/{i}", "ex.com",
                 NOW - 3600, f"story {i}", "body"),
            )
            cur.execute(
                "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label, inference_method) VALUES (?,?,?,?,?,?)",
                (i + 1, "sentiment", _sentiment("NEGATIVE"), 0.9, "NEGATIVE", "llm"),
            )
        cur.execute(
            "INSERT INTO docs VALUES (101,'x_post','111','x.com',?,?,?)",
            (NOW - 3600, "tweet", "tweet text"),
        )
        cur.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label, inference_method) VALUES (101,'sentiment',?,0.8,'POSITIVE','llm')",
            (_sentiment("POSITIVE", 0.8),),
        )
        cur.execute(
            "INSERT INTO x_posts_raw VALUES ('111','u1',0,10,2,50,1)")
        cur.execute(
            "INSERT INTO x_users_raw VALUES ('u1','someuser','Some User','https://img/x.png','blue',1234,?)",
            (NOW - 86400 * 400,),
        )
        cur.execute(
            "INSERT INTO docs VALUES (201,'reddit_post','t3_abc','politics',?,?,?)",
            (NOW - 3600, "reddit post", "post body"),
        )
        cur.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label, inference_method) VALUES (201,'sentiment',?,0.7,'NEUTRAL','llm')",
            (_sentiment("NEUTRAL", 0.7),),
        )
        cur.execute("INSERT INTO reddit_posts_raw VALUES ('t3_abc', 321, 45)")
        cur.execute(
            "INSERT INTO docs VALUES (202,'reddit_comment','t1_def','politics',?,?,?)",
            (NOW - 3600, "reddit comment", "comment body"),
        )
        cur.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label, inference_method) VALUES (202,'sentiment',?,0.7,'NEUTRAL','llm')",
            (_sentiment("NEUTRAL", 0.7),),
        )

        # Bot detection: the X post is flagged, with slug + prose indicators;
        # it also supports a narrative whose REAL id must ride the payload.
        cur.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label, inference_method) VALUES (101,'bot_detection',?,0.85,'bot','llm')",
            (json.dumps({
                "label": "bot",
                "indicators": ["zero_followers_following_listed", "New account (12 days)"],
                "reasoning": "Repetitive posting pattern.",
            }),),
        )
        cur.execute("INSERT INTO narratives VALUES (742, 'A recurring claim')")
        cur.execute("INSERT INTO narrative_docs VALUES (742, 101)")
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def _sentiment_result(self):
        return SentimentAggregator(self.db_path).get_public_sentiment(
            "7d", bot_docs=set(),
        ).to_dict()

    def _all_samples(self, result):
        for t in result["byTopic"]:
            yield from t["classificationSamples"]
        for bucket in result.get("distributionSamples", {}).values():
            yield from bucket

    # ---------- toneTrend ----------

    def test_tone_trend_suppresses_low_sample_tiers(self):
        result = self._sentiment_result()
        trend = result["toneTrend"]
        self.assertEqual(len(trend), 1)
        day = trend[0]
        # 6 news docs clear the floor → real net; 3 public docs (x + 2
        # reddit) are below it → suppressed net, honest volume.
        self.assertEqual(day["news"]["net"], -100.0)
        self.assertEqual(day["news"]["volume"], 6)
        self.assertIsNone(day["public"]["net"])
        self.assertEqual(day["public"]["volume"], 3)
        self.assertIsNone(day["officials"]["net"])
        self.assertEqual(day["officials"]["volume"], 0)

    def test_tone_trend_dates_ascend(self):
        # Add a second, older day of news docs and confirm ordering.
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()
        for i in range(6):
            cur.execute(
                "INSERT INTO docs VALUES (?,?,?,?,?,?,?)",
                (300 + i, "news", f"https://ex.com/old/{i}", "ex.com",
                 NOW - 86400 * 2, f"old {i}", "body"),
            )
            cur.execute(
                "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label, inference_method) VALUES (?,?,?,?,?,?)",
                (300 + i, "sentiment", _sentiment("POSITIVE"), 0.9, "POSITIVE", "llm"),
            )
        conn.commit()
        conn.close()
        trend = self._sentiment_result()["toneTrend"]
        self.assertEqual(len(trend), 2)
        self.assertLess(trend[0]["date"], trend[1]["date"])
        self.assertEqual(trend[0]["news"]["net"], 100.0)

    # ---------- engagement + author on samples ----------

    def test_x_sample_carries_engagement_and_author(self):
        result = self._sentiment_result()
        x = [s for s in self._all_samples(result) if s["source_type"] == "x_post"]
        self.assertTrue(x)
        self.assertEqual(
            x[0]["engagement"],
            {"retweets": 10, "replies": 2, "likes": 50, "quotes": 1},
        )
        author = x[0]["author"]
        self.assertEqual(author["handle"], "someuser")
        self.assertEqual(author["display_name"], "Some User")
        self.assertEqual(author["avatar_url"], "https://img/x.png")
        self.assertEqual(author["verified_type"], "blue")
        self.assertEqual(author["followers_count"], 1234)

    def test_reddit_post_sample_carries_score_but_no_author(self):
        result = self._sentiment_result()
        rd = [s for s in self._all_samples(result) if s["source_type"] == "reddit_post"]
        self.assertTrue(rd)
        self.assertEqual(rd[0]["engagement"], {"score": 321, "num_comments": 45})
        # Reddit stores no author at ingest — never fabricated.
        self.assertIsNone(rd[0]["author"])

    def test_news_and_reddit_comment_samples_carry_no_enrichment(self):
        result = self._sentiment_result()
        by_type = {}
        for s in self._all_samples(result):
            by_type.setdefault(s["source_type"], s)
        self.assertIsNone(by_type["news"]["engagement"])
        self.assertIsNone(by_type["news"]["author"])
        # reddit_comments_raw was dropped in migration 005 — no counts exist.
        self.assertIsNone(by_type["reddit_comment"]["engagement"])
        self.assertIsNone(by_type["reddit_comment"]["author"])

    # ---------- per-example bot evidence ----------

    def test_flagged_example_carries_evidence(self):
        data = BotAggregator(self.db_path).get_bot_activity("7d").to_dict()
        examples = [
            ex
            for tier in ("by_news_outlet", "by_official", "by_general_public")
            for entity in data["overview"][tier]
            for ex in entity["samples"]
        ]
        self.assertTrue(examples)
        ex = examples[0]
        self.assertEqual(ex["confidence"], 0.85)
        self.assertEqual(ex["reasoning"], "Repetitive posting pattern.")
        # Slug indicators humanize; prose passes through untouched.
        self.assertIn("Zero followers following listed", ex["indicators"])
        self.assertIn("New account (12 days)", ex["indicators"])

    def test_amplification_id_is_real_narrative_id(self):
        data = BotAggregator(self.db_path).get_bot_activity("7d").to_dict()
        amps = data["narrativeAmplification"]
        self.assertTrue(amps)
        self.assertEqual(amps[0]["id"], 742)
        self.assertEqual(amps[0]["narrative"], "A recurring claim")


if __name__ == "__main__":
    unittest.main()
