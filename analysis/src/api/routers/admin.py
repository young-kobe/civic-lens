"""
Admin-token-gated endpoints: cache metadata and pipeline triggers.

All handlers here are behind ``require_admin_token``. Pipeline triggers are
additionally rate-limited per-endpoint via ``enforce_trigger_cooldown`` so a
misbehaving client can't pile up background tasks.
"""

from fastapi import APIRouter, BackgroundTasks, Depends

from analysis.src.api.dependencies import enforce_trigger_cooldown, require_admin_token
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
    """Returns metadata for all cached snapshots."""
    return {
        "snapshots": cache.get_all_metadata(),
        "cache_dir": settings.cache_dir,
    }


@router.post("/run/etl")
def run_etl():
    """Triggers raw content loading into docs table."""
    enforce_trigger_cooldown("run/etl")
    count = loader.load_new_raw_content()
    return {"new_docs": count}


@router.post("/run/full-pipeline")
def run_full_pipeline(background_tasks: BackgroundTasks):
    """Run the full analysis pipeline in background.

    For synchronous execution use ``.\\run.ps1 analyze``.
    """
    enforce_trigger_cooldown("run/full-pipeline")
    from analysis.src.scheduler.job_runner import AnalysisJobRunner

    def run_pipeline():
        AnalysisJobRunner().run_full_pipeline()

    background_tasks.add_task(run_pipeline)
    return {"status": "Full pipeline queued"}
