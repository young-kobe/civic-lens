
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

    def test_health_is_unversioned(self):
        """Health is mounted at the app root — infra probes shouldn't have
        to track the API version."""
        resp = requests.get(f"{API_URL}/health")
        self.assertEqual(resp.status_code, 200)
        body = resp.json()
        self.assertIn(body["status"], ("ok", "degraded"))
        self.assertEqual(body["api_version"], "v1")

    def test_data_endpoints_under_v1(self):
        """Data endpoints live under /api/v1 after the versioning switch."""
        for path in ("/api/v1/sentiment", "/api/v1/bot-activity",
                     "/api/v1/narratives", "/api/v1/propaganda"):
            resp = requests.get(f"{API_URL}{path}")
            # With an empty/seed DB the aggregators may return empty payloads,
            # but the endpoint must respond 200 with JSON.
            self.assertEqual(resp.status_code, 200, f"{path} -> {resp.status_code}")
            resp.json()  # raises if not JSON

    def test_legacy_prefix_returns_404(self):
        """The unversioned /api/* prefix is retired — clients must migrate."""
        resp = requests.get(f"{API_URL}/api/sentiment")
        self.assertEqual(resp.status_code, 404)

    def test_admin_endpoints_reject_without_token(self):
        """Admin endpoints require CIVIC_ADMIN_TOKEN; tests boot without one,
        so they must return 503 (not 401) — misconfigured loudly."""
        resp = requests.get(f"{API_URL}/api/v1/cache-status")
        self.assertEqual(resp.status_code, 503)

    def test_review_endpoints_reject_without_token(self):
        """The review routes are the API's only eval write surface
        (ai_output_evals) and /review/queue leaks raw scraped text, so they
        share the admin gate and must fail closed — a deploy that forgets
        CIVIC_ADMIN_TOKEN gets 503 for everyone, never an open write path."""
        resp = requests.get(f"{API_URL}/api/v1/review/queue", params={"task": "claims"})
        self.assertEqual(resp.status_code, 503)
        resp = requests.post(f"{API_URL}/api/v1/review/submit", json={"ai_output_id": 1})
        self.assertEqual(resp.status_code, 503)

if __name__ == '__main__':
    unittest.main()
