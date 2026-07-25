"""
Unversioned health endpoint. Infra probes (k8s, ELB, Cloudflare) shouldn't
have to know or bump an API version to ask "is this process alive".
"""

import psycopg
from fastapi import APIRouter

from analysis.src.common import db
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings

logger = get_logger("api.health")
router = APIRouter(tags=["health"])


def _check_db() -> bool:
    # RuntimeError: CIVIC_DATABASE_URL unset (db.get_pool()'s own fail-loud
    # guard). psycopg.Error: pool open but the server is unreachable. Both
    # mean "not ok" here, not a 500 -- the misconfiguration is reported via
    # the response body's degraded status, not by crashing the endpoint.
    try:
        with db.connection() as conn:
            conn.execute("SELECT 1").fetchone()
        return True
    except (RuntimeError, psycopg.Error) as e:
        logger.warning(f"Health DB check failed: {e}")
        return False


@router.get("/health")
def health():
    """Exercises the Postgres pool so a misconfigured deploy fails loudly
    (a degraded status in the response, not a raised exception)."""
    settings = get_settings()
    db_ok = _check_db()
    # Import here to avoid a circular with server.py at module load.
    from analysis.src.api.server import API_VERSION
    # No llm_enabled here: no engine in the Postgres pipeline reads that
    # setting (2026-07-25 audit), so reporting it would advertise a switch
    # this stack does not honour. Only the retired scheduler/job_runner.py
    # stack still consults it.
    return {
        "status": "ok" if db_ok else "degraded",
        "app_name": settings.app_name,
        "api_version": API_VERSION,
        "db_reachable": db_ok,
    }
