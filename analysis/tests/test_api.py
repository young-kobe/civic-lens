"""
App-level smoke test (Phase 9 strictly-live): builds the real FastAPI app
(server.py, every router mounted) and exercises it end-to-end against the
PG fixture. Supersedes the pre-redesign subprocess+SQLite version of this
file -- server.py's mounted endpoints changed under it (data.py/cache_utils.py
retired, admin/review/health rewritten against Postgres), so the old
subprocess-against-SQLite version of this test no longer matches reality.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.tests import pg_fixture


class LegacyPrefixAndAuthGateTests(unittest.TestCase):
    """No DB needed -- these paths are rejected before any handler body
    touches a connection (404 routing / 503 admin-token gate)."""

    def test_legacy_unversioned_prefix_returns_404(self):
        from fastapi.testclient import TestClient
        from analysis.src.api.server import app
        response = TestClient(app).get("/api/sentiment")
        self.assertEqual(response.status_code, 404)

    def test_admin_endpoints_reject_without_token(self):
        from fastapi.testclient import TestClient
        from analysis.src.api.server import app
        client = TestClient(app)
        self.assertEqual(client.get("/api/v1/pipeline-runs").status_code, 503)
        self.assertEqual(
            client.get("/api/v1/review/queue", params={"task": "text"}).status_code, 503,
        )


class UnhandledExceptionRecordingTests(unittest.TestCase):
    """The catch-all handler exists so a 500 leaves a durable ops.error_log
    row -- before it, unhandled endpoint exceptions lived only in uvicorn's
    stderr under a 30 MB Docker log rotation. No DB needed: record_error is
    stubbed; what matters is that the handler fires with the right identity
    and still returns a 500 body."""

    def test_unhandled_exception_records_and_returns_500(self):
        from unittest import mock
        from fastapi.testclient import TestClient
        from analysis.src.api import server
        from analysis.src.api.server import app

        # A throwaway route that raises -- registered once; FastAPI ignores
        # duplicates on re-run because unittest imports this module once.
        if not any(getattr(r, "path", None) == "/api/v1/_test_boom" for r in app.routes):
            @app.get("/api/v1/_test_boom")
            def _boom():
                raise RuntimeError("kaboom")

        with mock.patch.object(server, "record_error") as recorded:
            client = TestClient(app, raise_server_exceptions=False)
            response = client.get("/api/v1/_test_boom")

        self.assertEqual(response.status_code, 500)
        self.assertEqual(response.json(), {"detail": "Internal server error"})
        recorded.assert_called_once()
        args, kwargs = recorded.call_args
        self.assertIsInstance(args[0], RuntimeError)
        self.assertEqual(kwargs["source"], "api")
        self.assertEqual(kwargs["component"], "GET /api/v1/_test_boom")


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class AppSmokeTests(unittest.TestCase):
    """Builds the real app (every router mounted) against the PG fixture --
    the integration point every Phase 9 panel/ops workstream ultimately
    shares."""

    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        from fastapi.testclient import TestClient
        from analysis.src.api.server import app
        self.client = TestClient(app)

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def test_health_ok(self):
        response = self.client.get("/health")
        self.assertEqual(response.status_code, 200)
        body = response.json()
        self.assertEqual(body["status"], "ok")
        self.assertEqual(body["api_version"], "v1")

    def test_snapshot_status_with_no_pipeline_runs(self):
        response = self.client.get("/api/v1/snapshot-status")
        self.assertEqual(response.status_code, 200)
        self.assertIsNone(response.json()["pipelineRun"])

    def test_narratives_panel_end_to_end(self):
        response = self.client.get("/api/v1/narratives", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIn("range", payload)
        self.assertIn("narratives", payload)

    def test_docs_drill_down_404_on_unknown_id(self):
        response = self.client.get("/api/v1/docs/999999")
        self.assertEqual(response.status_code, 404)


if __name__ == "__main__":
    unittest.main()
