
import requests
import json
import time
import subprocess
import sys
import os
import signal
import sqlite3 as sqlite_lib
import unittest

# Ensure project root is in path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir)) 
if project_root not in sys.path:
    sys.path.insert(0, project_root)

# Configuration
API_URL = "http://localhost:8000"
DB_PATH = "data/test_civic_api.db"

class TestAPI(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Setup DB
        if os.path.exists(DB_PATH):
            os.remove(DB_PATH)
        
        conn = sqlite_lib.connect(DB_PATH)
        cursor = conn.cursor()
        
        with open("data/schema.sql", "r") as f:
            schema = f.read()
        cursor.executescript(schema)
        
        cursor.execute("""
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, text, raw_hash)
            VALUES 
            ('news', 'http://test.com/1', 'test.com', 100, 'Test Article 1', 'This is a great, amazing story.', 'hash1'),
            ('reddit_post', 't3_1', 'r/politics', 102, 'Bot spam', 'buy now click here', 'hash3')
        """)
        conn.commit()
        conn.close()

        # Start Server
        env = os.environ.copy()
        env["CIVIC_DB_PATH"] = DB_PATH
        env["PYTHONPATH"] = project_root

        cls.server_cmd = [sys.executable, "analysis/src/main.py"]
        cls.server = subprocess.Popen(cls.server_cmd, env=env)
        
        # Wait for startup
        time.sleep(3)
        if cls.server.poll() is not None:
            raise RuntimeError("Server failed to start")

    @classmethod
    def tearDownClass(cls):
        if cls.server.poll() is None:
            os.kill(cls.server.pid, signal.SIGTERM)
        if os.path.exists(DB_PATH):
            os.remove(DB_PATH)

    def test_health(self):
        try:
            resp = requests.get(f"{API_URL}/health")
            self.assertEqual(resp.status_code, 200)
            self.assertEqual(resp.json()['status'], 'ok')
        except requests.exceptions.ConnectionError:
            self.fail("Could not connect to API server")

    def test_analysis_flow(self):
        # Trigger
        resp = requests.post(f"{API_URL}/api/run/analysis")
        self.assertEqual(resp.status_code, 200)
        
        # Wait for background task
        time.sleep(2)
        
        # Check profiles
        resp = requests.get(f"{API_URL}/api/profiles")
        data = resp.json()
        
        # Validate content
        outlets = {p['outlet']: p for p in data}
        self.assertIn('r/politics', outlets)
        self.assertGreater(outlets['r/politics']['bot_rate'], 0.0)

    def test_clustering_flow(self):
        resp = requests.post(f"{API_URL}/api/run/clustering")
        self.assertEqual(resp.status_code, 200)
        self.assertGreater(resp.json()['clusters_created'], 0)

if __name__ == '__main__':
    unittest.main()
