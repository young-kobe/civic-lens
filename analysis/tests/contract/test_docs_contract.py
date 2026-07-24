"""
Contract test for GET /api/v1/docs/{doc_id} (Phase 9 strictly-live). Mounts
ONLY analysis/src/api/routers/docs.py on a throwaway FastAPI app. Gated on
CIVIC_TEST_DATABASE_URL like every other integration test in this repo.
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

_FIXED_PUBLISHED_AT = datetime(2026, 1, 15, 12, 0, tzinfo=timezone.utc)


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class DocsContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.docs import router

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
                "TRUNCATE analysis.citations, analysis.runs, corpus.documents, "
                "corpus.authors RESTART IDENTITY CASCADE"
            )

    def _seed_doc(self) -> int:
        from analysis.src.common import db
        with db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, title, body, source_url, "
                " raw_hash, etl_version) "
                "VALUES ('news'::corpus.source_type, 'contract-doc', %s, 'Contract Title', "
                "        'contract body', 'https://example.com/contract-doc', 'deadbeef', 'test') "
                "RETURNING doc_id",
                (_FIXED_PUBLISHED_AT,),
            ).fetchone()
            return row["doc_id"]

    def test_unknown_doc_id_returns_404(self):
        response = self._client.get("/docs/999999")
        self.assertEqual(response.status_code, 404)

    def test_doc_response_shape(self):
        doc_id = self._seed_doc()
        response = self._client.get(f"/docs/{doc_id}")
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertEqual(payload["docId"], doc_id)
        self.assertEqual(payload["sourceType"], "news")
        self.assertEqual(payload["admissionClass"], "sampled")
        self.assertEqual(payload["analysisResults"], [])
        self.assertEqual(payload["citationsOut"], [])
        self.assertEqual(payload["citationsIn"], [])
        conftest.assert_snapshot_match("docs_basic", payload)


if __name__ == "__main__":
    unittest.main()
