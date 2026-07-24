"""
Contract test for GET /api/v1/outlet-profiles (Phase 9 strictly-live).
Mounts ONLY analysis/src/api/routers/outlets.py on a throwaway FastAPI app
-- this workstream does not touch server.py/routers/__init__.py, so
nothing else is registered. Gated on CIVIC_TEST_DATABASE_URL like every
other integration test in this repo.
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

from analysis.src.api.queries.constants import MIN_TARGET_SAMPLE_N
from analysis.tests import pg_fixture
from analysis.tests.contract import conftest

_FIXED_PUBLISHED_AT = datetime(2026, 1, 15, 12, 0, tzinfo=timezone.utc)


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class OutletProfilesContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.outlets import router

        app = FastAPI()
        app.include_router(router)
        cls._client = TestClient(app)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.sentiment_results, analysis.runs, corpus.documents "
                "RESTART IDENTITY CASCADE"
            )

    def _seed_outlet_at_floor(self):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            for i in range(MIN_TARGET_SAMPLE_N):
                doc = conn.execute(
                    "INSERT INTO corpus.documents "
                    "(source_type, natural_key, domain_or_subreddit, published_at, body, "
                    " source_url, raw_hash, etl_version) "
                    "VALUES ('news'::corpus.source_type, %s, 'example.com', %s, 'body', %s, "
                    "        'deadbeef', 'test') RETURNING doc_id",
                    (f"contract-doc-{i}", _FIXED_PUBLISHED_AT, f"https://example.com/{i}"),
                ).fetchone()["doc_id"]
                run = conn.execute(
                    "INSERT INTO analysis.runs "
                    "(task, doc_id, status, model_id, inference_method, confidence, is_current) "
                    "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, "
                    "        'test-model', 'llm'::analysis.inference_method, 0.9, true) "
                    "RETURNING run_id",
                    (doc,),
                ).fetchone()["run_id"]
                conn.execute(
                    "INSERT INTO analysis.sentiment_results (run_id, label) "
                    "VALUES (%s, 'positive'::analysis.sentiment_label)",
                    (run,),
                )

    def test_outlet_profiles_response_shape(self):
        self._seed_outlet_at_floor()
        response = self._client.get("/outlet-profiles", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIn("range", payload)
        self.assertIn("disclaimer", payload)
        self.assertEqual(len(payload["outlets"]), 1)
        self.assertEqual(payload["outlets"][0]["outletKey"], "example.com")
        self.assertEqual(payload["outlets"][0]["netTone"], 100.0)
        conftest.assert_snapshot_match("outlets_basic", payload)

    def test_unknown_window_returns_400(self):
        response = self._client.get("/outlet-profiles", params={"window": "nonsense"})
        self.assertEqual(response.status_code, 400)

    def test_window_and_custom_range_together_returns_400(self):
        response = self._client.get(
            "/outlet-profiles", params={"window": "7d", "from": "2026-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 400)

    def test_custom_range_returns_200(self):
        self._seed_outlet_at_floor()
        response = self._client.get(
            "/outlet-profiles",
            params={"from": "2000-01-01T00:00:00Z", "to": "2100-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(len(payload["outlets"]), 1)
        self.assertIsNone(payload["range"]["window"])


if __name__ == "__main__":
    unittest.main()
