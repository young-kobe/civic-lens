"""
Contract test for GET /api/v1/snapshot-status and GET /api/v1/eval-accuracy
(Phase 9 strictly-live). Mounts ONLY analysis/src/api/routers/status.py on
a throwaway FastAPI app. Gated on CIVIC_TEST_DATABASE_URL like every other
integration test in this repo.
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


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class StatusContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.status import router

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
            conn.execute("TRUNCATE ops.pipeline_runs RESTART IDENTITY CASCADE")

    def test_snapshot_status_with_no_runs_yet(self):
        response = self._client.get("/snapshot-status")
        self.assertEqual(response.status_code, 200)
        self.assertIsNone(response.json()["pipelineRun"])

    def test_snapshot_status_reads_latest_run(self):
        from analysis.src.common import db
        with db.connection() as conn:
            conn.execute(
                "INSERT INTO ops.pipeline_runs (started_at, completed_at, status, stage_summary) "
                "VALUES (now() - interval '1 hour', now() - interval '50 minutes', 'done', "
                "        '{\"etl\": {\"inserted\": 3}}'::jsonb)"
            )
            conn.execute(
                "INSERT INTO ops.pipeline_runs (started_at, status) VALUES (now(), 'running')"
            )
        response = self._client.get("/snapshot-status")
        self.assertEqual(response.status_code, 200)
        run = response.json()["pipelineRun"]
        self.assertEqual(run["status"], "running")
        self.assertIsNone(run["completedAt"])

    def test_eval_accuracy_shape(self):
        response = self._client.get("/eval-accuracy")
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIn("perTask", payload)
        self.assertIn("minReviewN", payload)
        self.assertEqual(payload["perTask"], [])


if __name__ == "__main__":
    unittest.main()
