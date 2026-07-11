"""
Admin-token-gated endpoints: cache metadata and pipeline triggers.

All handlers here are behind ``require_admin_token``. Pipeline triggers are
additionally rate-limited per-endpoint via ``enforce_trigger_cooldown`` (shared
server-wide cooldown — prevents pile-up from a single misbehaving client) and
``limiter.limit`` (per-IP cap — prevents a distributed client from hitting the
shared cooldown 200 times in a minute). Both are intentionally layered.
"""

from fastapi import APIRouter, BackgroundTasks, Depends
from starlette.requests import Request

from analysis.src.api.dependencies import enforce_trigger_cooldown, require_admin_token
from analysis.src.api.rate_limits import limiter
from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.etl.loader import ContentLoader

settings = get_settings()
cache = SnapshotCache(settings.cache_dir)
loader = ContentLoader(settings.db_path)

router = APIRouter(
    tags=["admin"],
    dependencies=[Depends(require_admin_token)],
)


@router.get("/cache-status")
def get_cache_status():
    """Returns metadata for all cached snapshots. The absolute ``cache_dir``
    path is deliberately omitted — it's a minor info leak that combines
    poorly with any future SSRF or path-traversal finding (audit §1.8)."""
    return {"snapshots": cache.get_all_metadata()}


@router.post("/run/etl")
@limiter.limit("1/hour")
def run_etl(request: Request):
    """Triggers raw content loading into docs table."""
    enforce_trigger_cooldown("run/etl")
    count = loader.load_new_raw_content()
    return {"new_docs": count}


@router.post("/run/full-pipeline")
@limiter.limit("1/hour")
def run_full_pipeline(request: Request, background_tasks: BackgroundTasks):
    """Run the full analysis pipeline in background.

    For synchronous execution use ``./run.sh analyze``.
    """
    enforce_trigger_cooldown("run/full-pipeline")
    from analysis.src.scheduler.job_runner import AnalysisJobRunner

    def run_pipeline():
        AnalysisJobRunner().run_full_pipeline()

    background_tasks.add_task(run_pipeline)
    return {"status": "Full pipeline queued"}
