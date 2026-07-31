"""
Tests for analysis/src/common/error_log.py -- the durable ops.error_log
writer. The never-raise contract and the rate cap run ungated (no DB
needed to verify the fallback path); actual row shape, traceback capture,
and retention pruning are gated on CIVIC_TEST_DATABASE_URL.
"""

from __future__ import annotations

import os
import unittest
from contextlib import contextmanager
from unittest import mock

from analysis.src.common import error_log
from analysis.tests import pg_fixture


def _fresh_rate_window():
    """Reset the module's rolling-window state so each test starts with a
    full write budget."""
    error_log._window_start = 0.0
    error_log._window_writes = 0
    error_log._cap_row_written = False


class RecordErrorNeverRaisesTests(unittest.TestCase):
    """record_error is the error path's error path: if it can raise, one
    failure becomes two and the pipeline dies inside its own except block.
    That is the invariant these tests encode."""

    def setUp(self):
        _fresh_rate_window()
        # The window is module-global state; leave it clean for whatever
        # test runs next in this process (the cap test exhausts it).
        self.addCleanup(_fresh_rate_window)

    def test_db_unavailable_falls_back_to_stdout_and_returns(self):
        exc = ValueError("boom")
        with mock.patch("analysis.src.common.db.connection", side_effect=RuntimeError("no DSN")):
            with self.assertLogs(level="ERROR") as captured:
                error_log.record_error(exc, component="tests")
        joined = "\n".join(captured.output)
        self.assertIn("boom", joined)
        # The fallback carries the traceback -- str(exc) alone is exactly
        # the information loss this module exists to end.
        self.assertIn("ValueError", joined)

    def test_no_exception_object_uses_message(self):
        with mock.patch("analysis.src.common.db.connection", side_effect=RuntimeError("no DSN")):
            with self.assertLogs(level="ERROR") as captured:
                error_log.record_error(None, component="tests", message="embed returned None")
        self.assertIn("embed returned None", "\n".join(captured.output))

    def test_rate_cap_stops_db_writes_within_window(self):
        calls = []

        @contextmanager
        def fake_connection():
            calls.append(1)
            yield mock.MagicMock()

        with mock.patch("analysis.src.common.db.connection", fake_connection):
            with self.assertLogs(level="ERROR"):
                for _ in range(error_log.RATE_CAP_PER_HOUR + 10):
                    error_log.record_error(ValueError("x"), component="tests")
        # Budget writes plus exactly one cap-marker row; the 9 overflow
        # calls must not touch the DB at all.
        self.assertEqual(len(calls), error_log.RATE_CAP_PER_HOUR + 1)


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ErrorLogIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        prev_url = pg_fixture.begin_test(self._dsn)
        self.addCleanup(pg_fixture.end_test, prev_url)
        _fresh_rate_window()
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute("TRUNCATE ops.error_log RESTART IDENTITY")

    def _rows(self):
        from analysis.src.common import db
        with db.connection() as conn:
            return conn.execute(
                "SELECT * FROM ops.error_log ORDER BY error_id"
            ).fetchall()

    def test_row_carries_full_traceback_and_context(self):
        try:
            raise ValueError("engine exploded")
        except ValueError as exc:
            error_log.record_error(
                exc, component="engine.text", doc_id=42, task="text",
                context={"model": "test-model"},
            )
        rows = self._rows()
        self.assertEqual(len(rows), 1)
        row = rows[0]
        self.assertEqual(row["source"], "analysis")
        self.assertEqual(row["component"], "engine.text")
        self.assertEqual(row["message"], "engine exploded")
        self.assertEqual(row["doc_id"], 42)
        self.assertEqual(row["task"], "text")
        self.assertEqual(row["context"], {"model": "test-model"})
        # A real multi-line traceback, not str(exc) -- the reason this
        # table exists instead of ops.task_queue.last_error.
        self.assertIn("Traceback (most recent call last)", row["traceback"])
        self.assertIn("test_row_carries_full_traceback_and_context", row["traceback"])

    def test_prune_deletes_only_rows_past_retention(self):
        error_log.record_error(ValueError("recent"), component="tests")
        from analysis.src.common import db
        with db.connection() as conn:
            conn.execute(
                "INSERT INTO ops.error_log (occurred_at, source, component, message) "
                "VALUES (now() - interval '31 days', 'analysis', 'tests', 'ancient')"
            )
        with db.connection() as conn:
            pruned = error_log.prune(conn)
        self.assertEqual(pruned, 1)
        rows = self._rows()
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["message"], "recent")


if __name__ == "__main__":
    unittest.main()
