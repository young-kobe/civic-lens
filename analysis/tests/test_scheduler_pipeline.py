"""
Tests for analysis/src/scheduler/pipeline.py -- Postgres redesign Phase 7.

Exercises orchestration only (task validation/ordering, limit/budget
passthrough, ops.pipeline_runs bookkeeping): `_build_registry()` is
monkeypatched to a stub registry and `stages.run_queue_stage`/
`run_global_stage` are stubbed too, so no real engine or LLM backend is
ever touched. The real claim-loop mechanics (claim/complete/fail/retry/
limit/budget) are covered by test_scheduler_stages.py; the registry wiring
to real engines is exercised by the integrator's composition smoke, not
unit tests here.
"""

from __future__ import annotations

import os
import sys
import unittest
from unittest import mock

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.scheduler import pipeline, stages
from analysis.tests import pg_fixture


def _stub_registry():
    """One global stage ('etl') and one queue stage ('citations') -- both
    real STAGE_ORDER names so run_pipeline()'s validation against
    STAGE_ORDER passes, but backed by stub workers with no DB/engine/LLM
    dependency of their own."""
    return {
        "etl": stages.StageSpec("etl", "global", lambda: "ok"),
        "citations": stages.StageSpec(
            "citations", "queue", lambda doc, client, resolver: None, lambda conn, doc_id: doc_id,
        ),
    }


class RunPipelineValidationTests(unittest.TestCase):
    """Pure -- no DB needed, always runs. Unknown-task validation raises
    before any DB access (see run_pipeline's ordering), so this class does
    not need the Postgres fixture."""

    def test_unknown_task_name_raises(self):
        with self.assertRaises(ValueError):
            pipeline.run_pipeline(tasks=["not-a-real-stage"])


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set -- no Postgres server available to test against",
)
class RunPipelineOrchestrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def test_stages_run_in_stage_order_not_caller_order(self):
        call_order = []

        def _fake_global(spec):
            call_order.append(spec.name)
            return stages.StageResult(spec.name, claimed=0, done=1, failed=0, skipped=0, duration_seconds=0.01)

        def _fake_queue(spec, *, concurrency, limit, budget_deadline, **kwargs):
            call_order.append(spec.name)
            self.assertEqual(limit, 7)
            return stages.StageResult(spec.name, claimed=1, done=1, failed=0, skipped=0, duration_seconds=0.01)

        with mock.patch.object(pipeline, "_build_registry", return_value=_stub_registry()), \
             mock.patch.object(stages, "run_global_stage", side_effect=_fake_global), \
             mock.patch.object(stages, "run_queue_stage", side_effect=_fake_queue):
            # Caller lists citations before etl; STAGE_ORDER puts etl first.
            summary = pipeline.run_pipeline(tasks=["citations", "etl"], limit=7)

        self.assertEqual(call_order, ["etl", "citations"])
        self.assertEqual(set(summary.keys()), {"etl", "citations"})

    def test_reset_stale_in_progress_called_before_stages_run(self):
        with mock.patch.object(pipeline, "_build_registry", return_value=_stub_registry()), \
             mock.patch.object(pipeline.queue, "reset_stale_in_progress") as spy_reset, \
             mock.patch.object(stages, "run_global_stage",
                                return_value=stages.StageResult("etl", 0, 1, 0, 0, 0.01)), \
             mock.patch.object(stages, "run_queue_stage",
                                return_value=stages.StageResult("citations", 0, 1, 0, 0, 0.01)):
            pipeline.run_pipeline(tasks=["citations", "etl"])
        spy_reset.assert_called_once()

    def test_pipeline_runs_row_written_with_stage_summary(self):
        from analysis.src.common import db
        with mock.patch.object(pipeline, "_build_registry", return_value=_stub_registry()), \
             mock.patch.object(stages, "run_global_stage",
                                return_value=stages.StageResult("etl", 0, 1, 0, 0, 0.5)), \
             mock.patch.object(stages, "run_queue_stage",
                                return_value=stages.StageResult("citations", 2, 2, 0, 0, 1.2)):
            summary = pipeline.run_pipeline(tasks=["etl", "citations"])

        self.assertEqual(summary["citations"]["done"], 2)
        with db.connection() as conn:
            row = conn.execute(
                "SELECT status, stage_summary FROM ops.pipeline_runs ORDER BY pipeline_run_id DESC LIMIT 1"
            ).fetchone()
        self.assertEqual(row["status"], "done")
        self.assertEqual(row["stage_summary"]["citations"]["done"], 2)
        self.assertEqual(row["stage_summary"]["etl"]["done"], 1)

    def test_pipeline_status_failed_when_a_stage_reports_stage_level_error(self):
        from analysis.src.common import db
        with mock.patch.object(pipeline, "_build_registry", return_value=_stub_registry()), \
             mock.patch.object(stages, "run_global_stage",
                                return_value=stages.StageResult("etl", 0, 0, 1, 0, 0.1, error="boom")), \
             mock.patch.object(stages, "run_queue_stage",
                                return_value=stages.StageResult("citations", 0, 1, 0, 0, 0.01)):
            pipeline.run_pipeline(tasks=["etl", "citations"])

        with db.connection() as conn:
            row = conn.execute(
                "SELECT status FROM ops.pipeline_runs ORDER BY pipeline_run_id DESC LIMIT 1"
            ).fetchone()
        self.assertEqual(row["status"], "failed")

    def test_ordinary_per_doc_failures_do_not_fail_the_pipeline_run(self):
        """A stage's per-doc `failed` count (no `error`) is expected/
        retryable via the queue's attempts mechanism -- it must not flip
        the whole pipeline_runs row to 'failed', unlike a stage-level
        `error` (see the previous test)."""
        from analysis.src.common import db
        with mock.patch.object(pipeline, "_build_registry", return_value=_stub_registry()), \
             mock.patch.object(stages, "run_global_stage",
                                return_value=stages.StageResult("etl", 0, 1, 0, 0, 0.1)), \
             mock.patch.object(stages, "run_queue_stage",
                                return_value=stages.StageResult("citations", 3, 1, 2, 0, 0.5)):
            pipeline.run_pipeline(tasks=["etl", "citations"])

        with db.connection() as conn:
            row = conn.execute(
                "SELECT status FROM ops.pipeline_runs ORDER BY pipeline_run_id DESC LIMIT 1"
            ).fetchone()
        self.assertEqual(row["status"], "done")


if __name__ == "__main__":
    unittest.main()
