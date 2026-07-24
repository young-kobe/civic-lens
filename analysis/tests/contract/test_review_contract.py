"""
Contract test for the /review/* routes (Phase 9 strictly-live rewrite).
Mounts ONLY analysis/src/api/routers/review.py on a throwaway FastAPI app,
with require_admin_token wired against a fixed test token. Gated on
CIVIC_TEST_DATABASE_URL like every other integration test in this repo.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(os.path.dirname(current_dir)))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.tests import pg_fixture

_TEST_TOKEN = "contract-test-admin-token"


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ReviewContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        cls._prev_admin_token = os.environ.get("CIVIC_ADMIN_TOKEN")
        os.environ["CIVIC_ADMIN_TOKEN"] = _TEST_TOKEN

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.review import router

        app = FastAPI()
        app.include_router(router)
        cls._client = TestClient(app, headers={"X-Admin-Token": _TEST_TOKEN})

    @classmethod
    def tearDownClass(cls):
        if cls._prev_admin_token is None:
            os.environ.pop("CIVIC_ADMIN_TOKEN", None)
        else:
            os.environ["CIVIC_ADMIN_TOKEN"] = cls._prev_admin_token

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate()
        from analysis.src.results import store
        store.register_prompt_version("text_v1", "text", "system prompt")

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.golden_labels, analysis.evals, analysis.sentiment_results, "
                "analysis.runs, analysis.prompt_versions, corpus.documents "
                "RESTART IDENTITY CASCADE"
            )

    def _seed_run(self, confidence: float) -> int:
        from analysis.src.results import store
        with store.db.connection() as conn:
            doc = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, body, source_url, raw_hash, etl_version) "
                "VALUES ('news'::corpus.source_type, 'review-doc', now(), 'body', "
                "        'https://example.com/review-doc', 'deadbeef', 'test') RETURNING doc_id",
            ).fetchone()
        handle = store.open_run(
            "text", doc_id=doc["doc_id"], model_id="gemini-3.5-flash",
            prompt_version="text_v1", inference_method="llm",
        )
        handle.save_sentiment(store.SentimentRow(label="positive", score=0.5))
        return handle.finish("done", confidence=confidence).run_id

    def test_missing_token_rejected(self):
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.review import router
        from fastapi import FastAPI
        app = FastAPI()
        app.include_router(router)
        unauth_client = TestClient(app)
        response = unauth_client.get("/review/queue", params={"task": "text"})
        self.assertEqual(response.status_code, 401)

    def test_queue_and_submit_round_trip(self):
        # review/queue and review/submit return plain dicts from
        # review/service.py, not a CamelModel -- snake_case, matching the
        # old (pre-redesign) review endpoints' convention.
        run_id = self._seed_run(0.3)
        queue_response = self._client.get("/review/queue", params={"task": "text"})
        self.assertEqual(queue_response.status_code, 200)
        items = queue_response.json()
        self.assertEqual(len(items), 1)
        # Not snapshot-tested (unlike the other Phase 9 contract tests):
        # completed_at comes from the run's real now() completion time, so
        # the payload is not byte-stable across runs the way a seeded
        # published_at is.
        self.assertEqual(items[0]["run_id"], run_id)
        self.assertEqual(items[0]["confidence"], 0.3)
        self.assertEqual(items[0]["model_id"], "gemini-3.5-flash")
        self.assertEqual(items[0]["prompt_version"], "text_v1")
        self.assertEqual(items[0]["doc"]["source_type"], "news")
        self.assertEqual(items[0]["doc"]["admission_class"], "sampled")

        submit_response = self._client.post(
            "/review/submit",
            json={"run_id": run_id, "verdict": "correct"},
        )
        self.assertEqual(submit_response.status_code, 200)

        empty_queue = self._client.get("/review/queue", params={"task": "text"})
        self.assertEqual(empty_queue.json(), [])

    def test_submit_unknown_run_returns_404(self):
        response = self._client.post(
            "/review/submit", json={"run_id": 999999, "verdict": "correct"},
        )
        self.assertEqual(response.status_code, 404)

    def test_stats_reflects_submitted_verdict(self):
        run_id = self._seed_run(0.5)
        self._client.post("/review/submit", json={"run_id": run_id, "verdict": "incorrect"})
        stats_response = self._client.get("/review/stats", params={"task": "text"})
        self.assertEqual(stats_response.status_code, 200)
        row = stats_response.json()["per_task"][0]
        self.assertEqual(row["incorrect"], 1)


if __name__ == "__main__":
    unittest.main()
