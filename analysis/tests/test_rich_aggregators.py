import unittest
import sqlite3
import json
import os
from analysis.src.reporting.aggregators import (
    OutletAggregator,
    SentimentAggregator,
    BotAggregator,
)


class TestRichAggregators(unittest.TestCase):
    def setUp(self):
        import uuid
        self.db_path = f"test_rich_data_{uuid.uuid4()}.db"
        self.conn = sqlite3.connect(self.db_path)
        self.cursor = self.conn.cursor()

        # Cleanup
        self.cursor.executescript("""
            DROP TABLE IF EXISTS docs;
            DROP TABLE IF EXISTS ai_outputs;
        """)

        # Create schema (matches 001_initial.sql + the X tables from 002).
        # x_posts_raw + x_users_raw are required by the sentiment aggregator's
        # LEFT JOIN for author-handle resolution (walkthrough 057). Even an
        # empty pair of tables is enough — the join is a no-op when no rows
        # match the x_post source_type.
        self.cursor.executescript("""
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

        # Seed docs (source_type matches CHECK constraint: news, reddit_post, etc.)
        docs = [
            (1, "news", "http://a.com/1", "a.com", 1700000000, 1700000000,
             "Immigration Reform Bill", "content about immigration", "hash1", "{}"),
            (2, "news", "http://b.com/2", "b.com", 1700086400, 1700086400,
             "Climate Policy Update", "content about climate", "hash2", "{}"),
            (3, "reddit_post", "t3_123", "politics", 1700000000, 1700000000,
             "Reddit Thread", "content about politics", "hash3", "{}"),
            (4, "x_post", "tweet_bot_1", "x.com", 1700000000, 1700000000,
             "Bot Spam", "spam content", "hash4", "{}"),
        ]
        self.cursor.executemany(
            """INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
               published_at, fetched_at, title, text, raw_hash, metadata_json)
               VALUES (?,?,?,?,?,?,?,?,?,?)""",
            docs,
        )

        # AI Outputs (include reasoning for classification transparency)
        sentiment_pos = json.dumps({
            "label": "POSITIVE", "confidence": 0.9,
            "reasoning": "Positive framing of reform bill",
            "evidence_spans": ["reform bill passed"],
            "sarcasm_detected": False,
        })
        sentiment_neg = json.dumps({
            "label": "NEGATIVE", "confidence": 0.8,
            "reasoning": "Sarcastic tone detected",
            "evidence_spans": ["oh great another policy"],
            "sarcasm_detected": True,
        })
        sentiment_neutral = json.dumps({"label": "NEUTRAL", "confidence": 0.7})
        sentiment_bot = json.dumps({"label": "POSITIVE", "confidence": 0.9})

        outputs = [
            (1, "sentiment", sentiment_pos, 0.9),
            (2, "sentiment", sentiment_neg, 0.8),
            (3, "sentiment", sentiment_neutral, 0.7),
            (1, "favorability", json.dumps({
                "overall_gop_stance": "favorable",
                "gop_entities_found": ["Trump"],
            }), 0.9),
            (2, "favorability", json.dumps({
                "overall_gop_stance": "unfavorable",
                "gop_entities_found": ["DeSantis"],
            }), 0.8),
            (4, "bot_detection", json.dumps({"label": "bot"}), 0.99),
            (4, "sentiment", sentiment_bot, 0.9),
        ]
        for doc_id, task, output, conf in outputs:
            # Mirror save_ai_output / migration 023: label column projects
            # the payload's scalar verdict.
            payload = json.loads(output)
            label = payload.get("label") or payload.get("overall_gop_stance")
            self.cursor.execute(
                "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, label, created_at) VALUES (?,?,?,?,?,?)",
                (doc_id, task, output, conf, label, 1700000000),
            )

        self.conn.commit()
        self.conn.close()
        self.outlet_agg = OutletAggregator(self.db_path)
        self.sentiment_agg = SentimentAggregator(self.db_path)
        self.bot_agg = BotAggregator(self.db_path)

    def tearDown(self):
        if os.path.exists(self.db_path):
            try:
                os.remove(self.db_path)
            except PermissionError:
                pass

    def test_get_public_sentiment_rich(self):
        sentiment = self.sentiment_agg.get_public_sentiment(time_window="all").to_dict()
        # 4 docs total, doc 4 is bot -> 3 non-bot sentiment results
        self.assertEqual(sentiment['overview']['volume'], 3)
        self.assertEqual(sentiment['excluded_bot_content'], 1)
        self.assertIn('byPlatform', sentiment)
        platforms = {x['platform']: x for x in sentiment['byPlatform']}
        self.assertIn('News Outlets', platforms)
        self.assertEqual(platforms['News Outlets']['volume'], 2)

    def test_sentiment_has_topic_classification_samples(self):
        """Verify byTopic entries include classificationSamples and sarcasm_rate."""
        sentiment = self.sentiment_agg.get_public_sentiment(time_window="all").to_dict()
        self.assertIn('byTopic', sentiment)
        for topic in sentiment['byTopic']:
            self.assertIn('classificationSamples', topic)
            self.assertIn('sarcasm_rate', topic)
            for sample in topic['classificationSamples']:
                self.assertIn('reasoning', sample)
                self.assertIn('evidence_spans', sample)
                self.assertIn('sarcasm_detected', sample)

    def test_sentiment_favorability_merged(self):
        """Verify GOP favorability is merged into sentiment response."""
        sentiment = self.sentiment_agg.get_public_sentiment(time_window="all").to_dict()
        self.assertIn('gopFavorability', sentiment)
        fav = sentiment['gopFavorability']
        self.assertEqual(fav['sampleSize'], 2)
        self.assertIn('netFavorability', fav)

    def test_get_outlet_profiles_rich(self):
        profiles_dc = self.outlet_agg.get_outlet_profiles()
        profiles = [p.to_dict() for p in profiles_dc]
        outlets = {p['outlet']: p for p in profiles}
        self.assertIn('x.com', outlets)
        self.assertEqual(outlets['x.com']['bot_rate'], 1.0)
        self.assertEqual(outlets['a.com']['bot_rate'], 0.0)


if __name__ == '__main__':
    unittest.main()
