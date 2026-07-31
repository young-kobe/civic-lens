"""
Tests for analysis/src/scheduler/stages.py -- Postgres redesign Phase 7.

Stub workers/loaders throughout (no LLM, no real engines): `spec.worker`
returns a locally-defined RunOutcome-shaped stub (run_id/status/error) --
not imported from analysis/src/results/store.py, which does not export one
yet (a concurrent closure agent is adding it). `stages._new_llm_client` is
patched to a stub so run_queue_stage never constructs a real LLM backend.
"""

from __future__ import annotations

import dataclasses
import os
import sys
import threading
import time
import unittest
from typing import List, Optional
from unittest import mock

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.scheduler import stages
from analysis.src.scheduler.constants import MAX_TASK_ATTEMPTS
from analysis.tests import pg_fixture

UTC_NOW_SQL = "now()"


@dataclasses.dataclass
class _StubOutcome:
    """Local duck-type for the RunOutcome contract a concurrent closure
    agent is adding to results/store.py -- run_id/status/error only."""

    run_id: int
    status: str
    error: Optional[str] = None


def _stub_loader(conn, doc_id: int) -> int:
    """Trivial input loader: the stub worker only needs the doc_id, not a
    real engine DocInput dataclass."""
    return doc_id


def _stub_worker_factory(status: str = "done", error: Optional[str] = None, record: Optional[List[int]] = None):
    """Builds a (doc_input, client, resolver) -> _StubOutcome worker. When
    `record` is given, every claimed doc_id is appended to it (used to
    assert no doc is ever double-claimed under concurrency)."""

    def _worker(doc_input, client, resolver):
        if record is not None:
            record.append(doc_input)
        return _StubOutcome(run_id=doc_input, status=status, error=error)

    return _worker


class StageSpecValidationTests(unittest.TestCase):
    """Pure -- no DB needed, always runs."""

    def test_invalid_kind_raises(self):
        with self.assertRaises(ValueError):
            stages.StageSpec("x", "bogus", lambda: None)

    def test_queue_kind_without_input_loader_raises(self):
        with self.assertRaises(ValueError):
            stages.StageSpec("x", "queue", lambda *_: None)

    def test_global_kind_does_not_require_input_loader(self):
        spec = stages.StageSpec("x", "global", lambda: None)
        self.assertIsNone(spec.input_loader)


class RunGlobalStageTests(unittest.TestCase):
    """Pure -- no DB needed, always runs."""

    def test_success_records_done(self):
        spec = stages.StageSpec("acct", "global", lambda: "ok")
        result = stages.run_global_stage(spec)
        self.assertEqual((result.done, result.failed), (1, 0))
        self.assertIsNone(result.error)

    def test_raising_worker_records_failed_and_error(self):
        def _boom():
            raise RuntimeError("kaboom")

        spec = stages.StageSpec("acct", "global", _boom)
        result = stages.run_global_stage(spec)
        self.assertEqual((result.done, result.failed), (0, 1))
        self.assertEqual(result.error, "kaboom")


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set -- no Postgres server available to test against",
)
class RunQueueStageIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute("TRUNCATE ops.task_queue, ops.error_log, corpus.documents CASCADE")
        # run_queue_stage always constructs a fresh LLM client per worker
        # thread (see stages._new_llm_client) -- stubbed here so these
        # hermetic tests never need a reachable LLM backend.
        self._llm_patch = mock.patch.object(stages, "_new_llm_client", return_value=None)
        self._llm_patch.start()

    def tearDown(self):
        self._llm_patch.stop()
        pg_fixture.end_test(self._prev_url)

    def _insert_doc(self, conn, natural_key: str) -> int:
        row = conn.execute(
            "INSERT INTO corpus.documents (source_type, natural_key, published_at, body, "
            "source_url, raw_hash, etl_version) VALUES ('news', %s, now(), 'body', "
            "'https://example.com', %s, 'pg-1') RETURNING doc_id",
            (natural_key, ("h" + natural_key).ljust(64, "0")),
        ).fetchone()
        return row["doc_id"]

    def _insert_queue_row(self, conn, doc_id: int, *, task: str = "citations", status: str = "pending", attempts: int = 0):
        conn.execute(
            "INSERT INTO ops.task_queue (doc_id, task, status, attempts) "
            "VALUES (%s, %s::analysis.task, %s::ops.task_status, %s)",
            (doc_id, task, status, attempts),
        )

    def _queue_row(self, conn, doc_id: int, task: str = "citations"):
        return conn.execute(
            "SELECT status, attempts, last_error FROM ops.task_queue WHERE doc_id = %s AND task = %s::analysis.task",
            (doc_id, task),
        ).fetchone()

    # -- claim/complete --------------------------------------------------

    def test_claim_and_complete_marks_queue_row_done(self):
        from analysis.src.common import db
        with db.connection() as conn:
            doc_id = self._insert_doc(conn, "n1")
            self._insert_queue_row(conn, doc_id)

        spec = stages.StageSpec("citations", "queue", _stub_worker_factory(status="done"), _stub_loader)
        result = stages.run_queue_stage(
            spec, concurrency=1, limit=None, budget_deadline=None,
        )
        self.assertEqual((result.claimed, result.done, result.failed), (1, 1, 0))
        self.assertIsNone(result.error)

        with db.connection() as conn:
            row = self._queue_row(conn, doc_id)
        self.assertEqual(row["status"], "done")

    def test_concurrent_workers_never_double_claim(self):
        from analysis.src.common import db
        doc_ids = []
        with db.connection() as conn:
            for i in range(6):
                doc_id = self._insert_doc(conn, f"concurrent-{i}")
                self._insert_queue_row(conn, doc_id)
                doc_ids.append(doc_id)

        claimed_record: List[int] = []
        record_lock = threading.Lock()

        def _worker(doc_input, client, resolver):
            with record_lock:
                claimed_record.append(doc_input)
            return _StubOutcome(run_id=doc_input, status="done")

        spec = stages.StageSpec("citations", "queue", _worker, _stub_loader)
        result = stages.run_queue_stage(
            spec, concurrency=4, limit=None, budget_deadline=None,
        )
        self.assertEqual(result.done, 6)
        self.assertEqual(sorted(claimed_record), sorted(doc_ids))
        self.assertEqual(len(claimed_record), len(set(claimed_record)), "a doc_id was claimed more than once")

    # -- fail --------------------------------------------------------------

    def test_failed_outcome_marks_row_failed_with_attempts_and_error(self):
        from analysis.src.common import db
        with db.connection() as conn:
            doc_id = self._insert_doc(conn, "n2")
            self._insert_queue_row(conn, doc_id)

        spec = stages.StageSpec("citations", "queue", _stub_worker_factory(status="failed", error="boom"), _stub_loader)
        result = stages.run_queue_stage(
            spec, concurrency=1, limit=None, budget_deadline=None,
        )
        self.assertEqual((result.done, result.failed), (0, 1))

        with db.connection() as conn:
            row = self._queue_row(conn, doc_id)
        self.assertEqual(row["status"], "failed")
        self.assertEqual(row["attempts"], 1)
        self.assertEqual(row["last_error"], "boom")

    def test_loader_exception_marks_row_failed_with_exception_text(self):
        from analysis.src.common import db
        with db.connection() as conn:
            doc_id = self._insert_doc(conn, "n3")
            self._insert_queue_row(conn, doc_id)

        def _broken_loader(conn, doc_id):
            raise RuntimeError("loader exploded")

        spec = stages.StageSpec("citations", "queue", _stub_worker_factory(), _broken_loader)
        result = stages.run_queue_stage(
            spec, concurrency=1, limit=None, budget_deadline=None,
        )
        self.assertEqual((result.done, result.failed), (0, 1))
        with db.connection() as conn:
            row = self._queue_row(conn, doc_id)
        self.assertEqual(row["status"], "failed")
        self.assertIn("loader exploded", row["last_error"])

    def test_loader_exception_writes_durable_error_log_row(self):
        """last_error is a single overwritten column with no traceback --
        ops.error_log is where the failure has to survive with enough
        context to debug after the stdout log rotates away."""
        from analysis.src.common import db
        with db.connection() as conn:
            doc_id = self._insert_doc(conn, "n3b")
            self._insert_queue_row(conn, doc_id)

        def _broken_loader(conn, doc_id):
            raise RuntimeError("loader exploded durably")

        spec = stages.StageSpec("citations", "queue", _stub_worker_factory(), _broken_loader)
        stages.run_queue_stage(spec, concurrency=1, limit=None, budget_deadline=None)

        with db.connection() as conn:
            rows = conn.execute(
                "SELECT source, component, doc_id, task, traceback FROM ops.error_log"
            ).fetchall()
        self.assertEqual(len(rows), 1)
        row = rows[0]
        self.assertEqual(row["source"], "analysis")
        self.assertEqual(row["component"], "scheduler.stages")
        self.assertEqual(row["doc_id"], doc_id)
        self.assertEqual(row["task"], "citations")
        self.assertIn("RuntimeError: loader exploded durably", row["traceback"])

    def test_success_after_retry_clears_stale_last_error(self):
        """A doc that fails then succeeds must stop advertising the old
        error -- before this, _MARK_DONE_SQL left last_error in place and
        a 'done' row still carried a failure message forever."""
        from analysis.src.common import db
        with db.connection() as conn:
            doc_id = self._insert_doc(conn, "n3c")
            self._insert_queue_row(conn, doc_id, status="failed", attempts=1)
            conn.execute(
                "UPDATE ops.task_queue SET last_error = 'stale failure' WHERE doc_id = %s",
                (doc_id,),
            )

        spec = stages.StageSpec("citations", "queue", _stub_worker_factory(status="done"), _stub_loader)
        result = stages.run_queue_stage(spec, concurrency=1, limit=None, budget_deadline=None)
        self.assertEqual(result.done, 1)

        with db.connection() as conn:
            row = self._queue_row(conn, doc_id)
        self.assertEqual(row["status"], "done")
        self.assertIsNone(row["last_error"])

    # -- retry / requeue policy ---------------------------------------------

    def test_requeue_failed_under_max_attempts_only(self):
        from analysis.src.common import db
        with db.connection() as conn:
            under_max_doc = self._insert_doc(conn, "under-max")
            at_max_doc = self._insert_doc(conn, "at-max")
            self._insert_queue_row(conn, under_max_doc, status="failed", attempts=MAX_TASK_ATTEMPTS - 1)
            self._insert_queue_row(conn, at_max_doc, status="failed", attempts=MAX_TASK_ATTEMPTS)

        requeued = stages.requeue_failed_under_max_attempts("citations")
        self.assertEqual(requeued, 1)

        with db.connection() as conn:
            under_max_row = self._queue_row(conn, under_max_doc)
            at_max_row = self._queue_row(conn, at_max_doc)
        self.assertEqual(under_max_row["status"], "pending")
        self.assertEqual(at_max_row["status"], "failed")

    # -- limit / budget ------------------------------------------------------

    def test_limit_bounds_claimed_rows(self):
        from analysis.src.common import db
        with db.connection() as conn:
            for i in range(5):
                doc_id = self._insert_doc(conn, f"limit-{i}")
                self._insert_queue_row(conn, doc_id)

        spec = stages.StageSpec("citations", "queue", _stub_worker_factory(status="done"), _stub_loader)
        result = stages.run_queue_stage(
            spec, concurrency=2, limit=2, budget_deadline=None,
        )
        self.assertEqual(result.claimed, 2)
        self.assertEqual(result.done, 2)

        with db.connection() as conn:
            pending_count = conn.execute(
                "SELECT count(*) AS n FROM ops.task_queue WHERE status = 'pending'"
            ).fetchone()["n"]
        self.assertEqual(pending_count, 3)

    def test_budget_already_exceeded_claims_nothing(self):
        from analysis.src.common import db
        with db.connection() as conn:
            doc_id = self._insert_doc(conn, "budget-1")
            self._insert_queue_row(conn, doc_id)

        spec = stages.StageSpec("citations", "queue", _stub_worker_factory(status="done"), _stub_loader)
        result = stages.run_queue_stage(
            spec, concurrency=1, limit=None, budget_deadline=time.monotonic() - 1,
        )
        self.assertEqual((result.claimed, result.done), (0, 0))

        with db.connection() as conn:
            row = self._queue_row(conn, doc_id)
        self.assertEqual(row["status"], "pending")


if __name__ == "__main__":
    unittest.main()
