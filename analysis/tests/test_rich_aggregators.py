
import unittest
import sqlite3
import json
import os
from analysis.src.reporting.aggregators import Aggregator

class TestRichAggregators(unittest.TestCase):
    def setUp(self):
        import uuid
        self.db_path = f"test_rich_data_{uuid.uuid4()}.db"
        self.conn = sqlite3.connect(self.db_path)
        self.cursor = self.conn.cursor()
        
        # Cleanup
        self.cursor.executescript("""
            DROP TABLE IF EXISTS docs;
            DROP TABLE IF EXISTS clusters;
            DROP TABLE IF EXISTS cluster_assignments;
            DROP TABLE IF EXISTS ai_outputs;
        """)
        
        # Create Schema
        self.cursor.executescript("""
            CREATE TABLE docs (
                doc_id INTEGER PRIMARY KEY,
                url TEXT,
                title TEXT,
                published_at INTEGER,
                source_type TEXT,
                ident TEXT, 
                domain_or_subreddit TEXT,
                content TEXT,
                metadata_json TEXT DEFAULT '{}'
            );
            
            CREATE TABLE clusters (
                cluster_id INTEGER PRIMARY KEY,
                name TEXT,
                summary TEXT,
                created_at INTEGER
            );
            
            CREATE TABLE cluster_assignments (
                assignment_id INTEGER PRIMARY KEY,
                cluster_id INTEGER,
                doc_id INTEGER
            );
            
            CREATE TABLE ai_outputs (
                output_id INTEGER PRIMARY KEY,
                doc_id INTEGER,
                task_type TEXT, -- sentiment, favorability, bot_detection
                output_json TEXT,
                confidence REAL,
                created_at INTEGER
            );
        """)
        
        # Seed Data
        # Docs
        docs = [
            (1, "http://a.com/1", "Story A1", 1700000000, "news_article", "a.com", "a.com", "content", json.dumps({"description": "desc"})),
            (2, "http://b.com/2", "Story A2", 1700086400, "news_article", "b.com", "b.com", "content", json.dumps({"description": "desc"})),
            (3, "http://r.com/3", "Reddit Thread", 1700000000, "reddit_post", "t3_123", "politics", "content", json.dumps({"description": "desc"})),
            (4, "http://bot.com/4", "Bot Spam", 1700000000, "news_article", "bot.com", "bot.com", "content", "{}"),
        ]
        self.cursor.executemany("INSERT INTO docs VALUES (?,?,?,?,?,?,?,?,?)", docs)
        
        # Clusters (Cluster 1 has docs 1, 2, 3)
        self.cursor.execute("INSERT INTO clusters VALUES (1, 'Test Cluster', 'Summary of test cluster', 1700000000)")
        self.cursor.executemany("INSERT INTO cluster_assignments (cluster_id, doc_id) VALUES (?,?)", [(1,1), (1,2), (1,3)])
        
        # AI Outputs
        outputs = [
            (1, 1, "sentiment", json.dumps({"label": "POSITIVE"}), 0.9),
            (2, 2, "sentiment", json.dumps({"label": "NEGATIVE"}), 0.8),
            (3, 3, "sentiment", json.dumps({"label": "NEUTRAL"}), 0.7),
            (4, 1, "favorability", json.dumps({"overall_gop_stance": "favorable", "gop_entities_found": ["Trump"]}), 0.9),
            (5, 2, "favorability", json.dumps({"overall_gop_stance": "unfavorable", "gop_entities_found": ["DeSantis"]}), 0.8),
            (6, 4, "bot_detection", json.dumps({"label": "bot"}), 0.99), # Bot doc
            (7, 4, "sentiment", json.dumps({"label": "POSITIVE"}), 0.9), # Bot sentiment (should be ignored)
        ]
        for i, o in enumerate(outputs):
            self.cursor.execute("INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence, created_at) VALUES (?,?,?,?,?)", (o[1], o[2], o[3], o[4], 1700000000))
            
        self.conn.commit()
        self.conn.close() # Close to release lock
        self.aggregator = Aggregator(self.db_path)

    def tearDown(self):
        # file removal handles cleanup
        if os.path.exists(self.db_path):
            try:
                try:
                    os.remove(self.db_path)
                except PermissionError:
                    pass
            except Exception:
                pass

    def test_get_stories_rich(self):
        stories_dc = self.aggregator.get_stories()
        stories = [s.to_dict() for s in stories_dc]
        self.assertEqual(len(stories), 1)
        s = stories[0]
        self.assertEqual(s['title'], 'Test Cluster')
        self.assertEqual(s['articleCount'], 3)
        self.assertTrue('timeline' in s)
        self.assertTrue('momentum' in s)
        self.assertTrue('sourceMix' in s)
        # Check source mix
        sources = {x['type']: x['value'] for x in s['sourceMix']}
        self.assertEqual(sources.get('news_article'), 2)
        self.assertEqual(sources.get('reddit_post'), 1)

    def test_get_public_sentiment_rich(self):
        sentiment = self.aggregator.get_public_sentiment().to_dict()
        self.assertEqual(sentiment['overview']['volume'], 3) # 4 docs, 1 is bot
        self.assertEqual(sentiment['excluded_bot_content'], 1)
        self.assertTrue('byPlatform' in sentiment)
        # Check platform breakdown
        platforms = {x['platform']: x for x in sentiment['byPlatform']}
        self.assertIn('news_article', platforms)
        self.assertEqual(platforms['news_article']['volume'], 2)

    def test_get_gop_favorability_rich(self):
        fav = self.aggregator.get_gop_favorability().to_dict()
        self.assertEqual(fav['overall']['sampleSize'], 2) # docs 1 and 2 have fav data (doc 3 no fav data, doc 4 bot)
        self.assertTrue('trend' in fav)
        self.assertTrue('byPlatform' in fav)
        self.assertEqual(len(fav['overall']), 8)

    def test_get_outlet_profiles_rich(self):
        profiles_dc = self.aggregator.get_outlet_profiles()
        profiles = [p.to_dict() for p in profiles_dc]
        # Should contain a.com, b.com, politics, bot.com
        outlets = {p['outlet']: p for p in profiles}
        self.assertIn('bot.com', outlets)
        self.assertEqual(outlets['bot.com']['bot_rate'], 1.0)
        self.assertEqual(outlets['a.com']['bot_rate'], 0.0)

if __name__ == '__main__':
    unittest.main()
