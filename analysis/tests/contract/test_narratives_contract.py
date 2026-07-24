"""
Contract test for GET /api/v1/narratives (Phase 9 strictly-live). Mounts
ONLY analysis/src/api/routers/narratives.py on a throwaway FastAPI app --
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
class NarrativesContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.narratives import router

        app = FastAPI()
        app.include_router(router)
        cls._client = TestClient(app)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate(self):
        # RESTART IDENTITY -- the snapshot asserts fixed doc/narrative ids,
        # which must not drift with test execution order within the class.
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.narrative_leans, analysis.citations, "
                "analysis.narrative_docs, analysis.narratives, analysis.clustering_runs, "
                "analysis.claims, analysis.runs, corpus.documents, corpus.authors, "
                "corpus.entities RESTART IDENTITY CASCADE"
            )

    def _seed_one_narrative(self):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            cluster = conn.execute(
                "INSERT INTO analysis.clustering_runs (mode, threshold, started_at) "
                "VALUES ('jaccard', 0.5, now()) RETURNING clustering_run_id",
            ).fetchone()["clustering_run_id"]
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
                "(task, doc_id, status, model_id, inference_method) "
                "VALUES ('claims'::analysis.task, %s, 'done'::analysis.run_status, "
                "        'test-model', 'llm'::analysis.inference_method) "
                "RETURNING run_id",
                (doc,),
            ).fetchone()["run_id"]
            claim = conn.execute(
                "INSERT INTO analysis.claims (run_id, doc_id, claim_text) "
                "VALUES (%s, %s, 'a contract-test claim') RETURNING claim_id",
                (run, doc),
            ).fetchone()["claim_id"]
            narrative = conn.execute(
                "INSERT INTO analysis.narratives (clustering_run_id, name, anchor_claim_id) "
                "VALUES (%s, 'contract narrative', %s) RETURNING narrative_id",
                (cluster, claim),
            ).fetchone()["narrative_id"]
            conn.execute(
                "INSERT INTO analysis.narrative_docs (narrative_id, doc_id, confidence) "
                "VALUES (%s, %s, 0.9)",
                (narrative, doc),
            )

    def test_narratives_response_shape(self):
        self._seed_one_narrative()
        response = self._client.get("/narratives", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIn("range", payload)
        self.assertIn("narratives", payload)
        self.assertEqual(len(payload["narratives"]), 1)
        item = payload["narratives"][0]
        self.assertEqual(item["docCount"], 1)
        self.assertEqual(item["anchorClaimText"], "a contract-test claim")
        self.assertIsNone(item["lean"])
        conftest.assert_snapshot_match("narratives_basic", payload)

    def test_unknown_window_returns_400(self):
        response = self._client.get("/narratives", params={"window": "nonsense"})
        self.assertEqual(response.status_code, 400)

    def test_window_and_custom_range_together_returns_400(self):
        response = self._client.get(
            "/narratives",
            params={"window": "7d", "from": "2026-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 400)

    def test_custom_range_returns_200(self):
        self._seed_one_narrative()
        response = self._client.get(
            "/narratives",
            params={"from": "2000-01-01T00:00:00Z", "to": "2100-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(len(payload["narratives"]), 1)
        self.assertIsNone(payload["range"]["window"])


if __name__ == "__main__":
    unittest.main()
