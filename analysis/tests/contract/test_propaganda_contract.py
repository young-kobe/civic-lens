"""
Contract test for GET /api/v1/propaganda (Phase 9 strictly-live). Mounts
ONLY analysis/src/api/routers/propaganda.py on a throwaway FastAPI app --
this workstream does not touch server.py/routers/__init__.py, so nothing
else is registered. Gated on CIVIC_TEST_DATABASE_URL like every other
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
class PropagandaContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.propaganda import router

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
                "TRUNCATE analysis.propaganda_techniques, analysis.propaganda_results, "
                "analysis.runs, corpus.documents, corpus.authors, corpus.entities "
                "RESTART IDENTITY CASCADE"
            )

    def _seed_flagged_doc(self):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            doc = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, body, source_url, "
                " raw_hash, etl_version) "
                "VALUES ('news'::corpus.source_type, 'contract-doc', %s, 'body', "
                "        'https://example.com/contract-doc', 'deadbeef', 'test') "
                "RETURNING doc_id",
                (_FIXED_PUBLISHED_AT,),
            ).fetchone()["doc_id"]
            run = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, confidence) "
                "VALUES ('propaganda'::analysis.task, %s, 'done'::analysis.run_status, "
                "        'propaganda-v1', 'llm'::analysis.inference_method, 0.85) "
                "RETURNING run_id",
                (doc,),
            ).fetchone()["run_id"]
            conn.execute(
                "INSERT INTO analysis.propaganda_results "
                "(run_id, density, summary, techniques_validated) "
                "VALUES (%s, 0.7, 'flagged for loaded language', 1)",
                (run,),
            )
            conn.execute(
                "INSERT INTO analysis.propaganda_techniques "
                "(run_id, technique, evidence_span, confidence) "
                "VALUES (%s, 'loaded_language'::analysis.propaganda_technique, "
                "        'a vile scheme', 0.9)",
                (run,),
            )

    def test_propaganda_response_shape(self):
        self._seed_flagged_doc()
        response = self._client.get("/propaganda", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIn("range", payload)
        self.assertEqual(payload["totalEligibleDocs"], 1)
        self.assertEqual(payload["flaggedDocs"], 1)
        self.assertEqual(len(payload["byTechnique"]), 6)
        self.assertEqual(len(payload["examples"]), 1)
        conftest.assert_snapshot_match("propaganda_basic", payload)

    def test_unknown_window_returns_400(self):
        response = self._client.get("/propaganda", params={"window": "nonsense"})
        self.assertEqual(response.status_code, 400)

    def test_window_and_custom_range_together_returns_400(self):
        response = self._client.get(
            "/propaganda",
            params={"window": "7d", "from": "2026-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 400)

    def test_custom_range_returns_200(self):
        self._seed_flagged_doc()
        response = self._client.get(
            "/propaganda",
            params={"from": "2000-01-01T00:00:00Z", "to": "2100-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(payload["totalEligibleDocs"], 1)
        self.assertIsNone(payload["range"]["window"])


if __name__ == "__main__":
    unittest.main()
