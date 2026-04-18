
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
CACHE_DIR = "data/test_cache_api"

class TestAPI(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Setup DB and Cache
        if os.path.exists(DB_PATH):
            os.remove(DB_PATH)
        if os.path.exists(CACHE_DIR):
            import shutil
            shutil.rmtree(CACHE_DIR)
        os.makedirs(CACHE_DIR, exist_ok=True)
        
        conn = sqlite_lib.connect(DB_PATH)
        cursor = conn.cursor()
        
        migrations_dir = os.path.join(project_root, "data", "migrations")
        migration_files = sorted([f for f in os.listdir(migrations_dir) if f.endswith(".sql")])
        for m_file in migration_files:
            with open(os.path.join(migrations_dir, m_file), "r") as f:
                schema = f.read()
            cursor.executescript(schema)
        
        cursor.execute("""
            INSERT INTO docs (source_type, ident, domain_or_subreddit, published_at, title, text, raw_hash)
            VALUES 
            ('news', 'http://test.com/1', 'test.com', 100, 'Test Article 1', 'This is a great, amazing story.', 'hash1'),
            ('reddit_post', 't3_1', 'r/politics', 102, 'Bot spam', 'Buy now click here make money fast!', 'hash3')
        """)
        conn.commit()
        conn.close()

        # Start Server
        env = os.environ.copy()
        env["CIVIC_DB_PATH"] = DB_PATH
        env["CIVIC_CACHE_DIR"] = CACHE_DIR
        env["CIVIC_LLM_ENABLED"] = "false"
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
        # if os.path.exists(DB_PATH):
        #     os.remove(DB_PATH)

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
        
        # Wait for background task with polling
        max_retries = 10
        outlets = {}
        for _ in range(max_retries):
            time.sleep(1)
            resp = requests.get(f"{API_URL}/api/profiles")
            data = resp.json()
            outlets = {p['outlet']: p for p in data}
            if 'r/politics' in outlets and outlets['r/politics']['bot_rate'] > 0.0:
                break
        
        self.assertIn('r/politics', outlets)
        self.assertGreater(outlets['r/politics']['bot_rate'], 0.0)

if __name__ == '__main__':
    unittest.main()
