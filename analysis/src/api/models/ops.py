"""
Response models for the ops-machinery surfaces (Phase 9 strictly-live):
GET /snapshot-status (routers/status.py) and GET /pipeline-runs
(routers/admin.py) both wrap ops.pipeline_runs rows in this shape.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Optional

from pydantic import Field

from analysis.src.api.models.common import CamelModel


class PipelineRunModel(CamelModel):
    pipeline_run_id: int
    status: str
    started_at: datetime
    completed_at: Optional[datetime] = None
    stage_summary: Optional[Dict[str, Any]] = None


class SnapshotStatusResponse(CamelModel):
    """GET /api/v1/snapshot-status: the freshness signal now that
    serving.refreshes is gone. `pipeline_run` is None on a fresh database
    with no recorded runs yet -- never fabricated."""

    pipeline_run: Optional[PipelineRunModel] = None


class PipelineRunsResponse(CamelModel):
    """GET /api/v1/pipeline-runs (admin-gated): recent ops.pipeline_runs
    rows, newest first."""

    pipeline_runs: List[PipelineRunModel] = Field(default_factory=list)
