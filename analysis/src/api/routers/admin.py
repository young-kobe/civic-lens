"""
Admin-token-gated endpoints: pipeline triggers and recent-run listing.

All handlers here are behind ``require_admin_token``. Pipeline triggers are
additionally rate-limited per-endpoint via ``enforce_trigger_cooldown`` (shared
server-wide cooldown — prevents pile-up from a single misbehaving client) and
``limiter.limit`` (per-IP cap — prevents a distributed client from hitting the
shared cooldown 200 times in a minute). Both are intentionally layered.
"""

from __future__ import annotations

from fastapi import APIRouter, BackgroundTasks, Depends
from starlette.requests import Request

from analysis.src.api.dependencies import enforce_trigger_cooldown, require_admin_token
from analysis.src.api.models.ops import PipelineRunModel, PipelineRunsResponse
from analysis.src.api.rate_limits import limiter
from analysis.src.common import db
from analysis.src.scheduler.pipeline import run_pipeline

router = APIRouter(
    tags=["admin"],
    dependencies=[Depends(require_admin_token)],
)

# Recent-history view, not a full ops.pipeline_runs export.
PIPELINE_RUNS_LISTING_LIMIT = 20

_RECENT_PIPELINE_RUNS_SQL = """
    SELECT pipeline_run_id, status, started_at, completed_at, stage_summary
    FROM ops.pipeline_runs
    ORDER BY pipeline_run_id DESC
    LIMIT %(limit)s
"""


@router.get("/pipeline-runs", response_model=PipelineRunsResponse)
def list_pipeline_runs() -> PipelineRunsResponse:
    """Recent ops.pipeline_runs rows, newest first -- replaces the retired
    cache-status endpoint now that there is no serving.* cache to report on."""
    with db.connection() as conn:
        rows = conn.execute(_RECENT_PIPELINE_RUNS_SQL, {"limit": PIPELINE_RUNS_LISTING_LIMIT}).fetchall()
    return PipelineRunsResponse(pipeline_runs=[PipelineRunModel(**row) for row in rows])


@router.post("/run/etl")
@limiter.limit("1/hour")
def run_etl(request: Request):
    """Triggers the ETL stage only (raw content -> corpus.documents)."""
    enforce_trigger_cooldown("run/etl")
    return run_pipeline(tasks=["etl"])


@router.post("/run/full-pipeline")
@limiter.limit("1/hour")
def run_full_pipeline(request: Request, background_tasks: BackgroundTasks):
    """Runs the full analysis pipeline (every scheduler/pipeline.py stage,
    in STAGE_ORDER) in the background. For synchronous execution use
    ``./run.sh analyze-pg``."""
    enforce_trigger_cooldown("run/full-pipeline")
    background_tasks.add_task(run_pipeline)
    return {"status": "Full pipeline queued"}
