"""
Contract test for GET /api/v1/bot-activity (Phase 9 strictly-live). Mounts
ONLY analysis/src/api/routers/bots.py on a throwaway FastAPI app -- this
workstream does not touch server.py/routers/__init__.py, so nothing else
is registered. Gated on CIVIC_TEST_DATABASE_URL like every other
integration test in this repo.
"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timezone

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(os.path.dirname(current_dir)))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.tests import pg_fixture
from analysis.tests.contract import conftest

# Fixed (not datetime.now()) so the recorded JSON snapshot stays stable
# across runs -- published_at feeds straight into the response.
_FIXED_PUBLISHED_AT = datetime(2026, 1, 15, 12, 0, tzinfo=timezone.utc)


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class BotActivityContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.bots import router

        app = FastAPI()
        app.include_router(router)
        cls._client = TestClient(app)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate(self):
        # RESTART IDENTITY -- the snapshot asserts a fixed doc id, which
        # must not drift with test execution order within the class.
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.bot_signals, analysis.author_bot_scores, "
                "analysis.runs, corpus.documents, corpus.authors RESTART IDENTITY CASCADE"
            )

    def _seed_one_flagged_doc(self):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            author = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, 'contract-handle', 'contract-handle') "
                "RETURNING author_id",
            ).fetchone()["author_id"]
            doc = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, author_id, published_at, body, source_url, "
                " raw_hash, etl_version) "
                "VALUES ('x_post'::corpus.source_type, 'contract-doc', %s, %s, 'body', "
                "        'https://example.com/contract-doc', 'deadbeef', 'test') "
                "RETURNING doc_id",
                (author, _FIXED_PUBLISHED_AT),
            ).fetchone()["doc_id"]
            run = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('bot'::analysis.task, %s, 'done'::analysis.run_status, "
                "        'test-model', 'hybrid'::analysis.inference_method, 0.9, true) "
                "RETURNING run_id",
                (doc,),
            ).fetchone()["run_id"]
            conn.execute(
                "INSERT INTO analysis.bot_signals (run_id, doc_id, label, burstiness) "
                "VALUES (%s, %s, 'bot'::analysis.bot_label, 0.4)",
                (run, doc),
            )

    def test_bot_activity_response_shape(self):
        self._seed_one_flagged_doc()
        response = self._client.get("/bot-activity", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIn("range", payload)
        self.assertEqual(payload["analyzedDocCount"], 1)
        self.assertEqual(payload["automationRatePct"], 0.0)  # no author_bot_scores row seeded
        self.assertEqual(len(payload["flaggedDocs"]), 1)
        conftest.assert_snapshot_match("bots_basic", payload)

    def test_unknown_window_returns_400(self):
        response = self._client.get("/bot-activity", params={"window": "nonsense"})
        self.assertEqual(response.status_code, 400)

    def test_window_and_custom_range_together_returns_400(self):
        response = self._client.get(
            "/bot-activity", params={"window": "7d", "from": "2026-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 400)

    def test_custom_range_returns_200(self):
        self._seed_one_flagged_doc()
        response = self._client.get(
            "/bot-activity",
            params={"from": "2000-01-01T00:00:00Z", "to": "2100-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(payload["analyzedDocCount"], 1)
        self.assertIsNone(payload["range"]["window"])

    def test_no_params_defaults_to_30d_window(self):
        response = self._client.get("/bot-activity")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["range"]["window"], "30d")


if __name__ == "__main__":
    unittest.main()
