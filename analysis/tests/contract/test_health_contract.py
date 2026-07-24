"""
Contract test for GET /health (Phase 9 strictly-live rewrite: PG pool SELECT
1 instead of sqlite3). Mounts ONLY analysis/src/api/routers/health.py.

The "degraded without a reachable DB" case doesn't need a real Postgres --
it runs unconditionally. The "ok with a reachable DB" case is gated on
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


def _build_client():
    from fastapi import FastAPI
    from fastapi.testclient import TestClient
    from analysis.src.api.routers.health import router

    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


class HealthDegradedTests(unittest.TestCase):
    """No CIVIC_DATABASE_URL configured -- runs unconditionally."""

    def setUp(self):
        from analysis.src.common import db as dbmod
        dbmod.close_pool()
        self._prev_url = os.environ.pop("CIVIC_DATABASE_URL", None)

    def tearDown(self):
        from analysis.src.common import db as dbmod
        dbmod.close_pool()
        if self._prev_url is not None:
            os.environ["CIVIC_DATABASE_URL"] = self._prev_url

    def test_health_reports_degraded_without_reachable_db(self):
        response = _build_client().get("/health")
        self.assertEqual(response.status_code, 200)
        body = response.json()
        self.assertEqual(body["status"], "degraded")
        self.assertFalse(body["db_reachable"])


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class HealthOkTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def test_health_reports_ok_with_reachable_db(self):
        response = _build_client().get("/health")
        self.assertEqual(response.status_code, 200)
        body = response.json()
        self.assertEqual(body["status"], "ok")
        self.assertTrue(body["db_reachable"])
        self.assertEqual(body["api_version"], "v1")


if __name__ == "__main__":
    unittest.main()
