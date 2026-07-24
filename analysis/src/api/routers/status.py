"""
Ops freshness + public accuracy endpoints (Phase 9 strictly-live). Neither
route is admin-gated: both are the public-facing honesty surfaces the UI
reads on every page mount.
"""

from __future__ import annotations

from typing import Any, Dict

from fastapi import APIRouter

from analysis.src.api.models.ops import PipelineRunModel, SnapshotStatusResponse
from analysis.src.common import db
from analysis.src.review import service as review_service

router = APIRouter(tags=["status"])

_LATEST_PIPELINE_RUN_SQL = """
    SELECT pipeline_run_id, status, started_at, completed_at, stage_summary
    FROM ops.pipeline_runs
    ORDER BY pipeline_run_id DESC
    LIMIT 1
"""


@router.get("/snapshot-status", response_model=SnapshotStatusResponse)
def get_snapshot_status() -> SnapshotStatusResponse:
    """Freshness signal: the most recent ops.pipeline_runs row (already
    populated by the scheduler) -- replaces the retired
    serving.refreshes-backed cache metadata now that Phase 9 is
    strictly-live."""
    with db.connection() as conn:
        row = conn.execute(_LATEST_PIPELINE_RUN_SQL).fetchone()
    return SnapshotStatusResponse(pipeline_run=PipelineRunModel(**row) if row else None)


@router.get("/eval-accuracy")
def get_eval_accuracy() -> Dict[str, Any]:
    """Public per-task human-agreement overlay, suppressed below the
    review floor -- see review/service.py::get_public_accuracy."""
    return review_service.get_public_accuracy()
