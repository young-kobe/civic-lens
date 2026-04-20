"""
Unversioned health endpoint. Infra probes (k8s, ELB, Cloudflare) shouldn't
have to know or bump an API version to ask "is this process alive".
"""

import os
import sqlite3

from fastapi import APIRouter

from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings

logger = get_logger("api.health")
router = APIRouter(tags=["health"])


def _check_db(db_path: str) -> bool:
    try:
        conn = sqlite3.connect(db_path, timeout=1.0)
        try:
            conn.execute("SELECT 1").fetchone()
        finally:
            conn.close()
        return True
    except sqlite3.Error as e:
        logger.warning(f"Health DB check failed: {e}")
        return False


@router.get("/health")
def health():
    """Exercises DB and cache directory so a misconfigured deploy fails loudly."""
    settings = get_settings()
    db_ok = _check_db(settings.db_path)
    cache_ok = os.path.isdir(settings.cache_dir)
    ok = db_ok and cache_ok
    # Import here to avoid a circular with server.py at module load.
    from analysis.src.api.server import API_VERSION
    return {
        "status": "ok" if ok else "degraded",
        "app_name": settings.app_name,
        "api_version": API_VERSION,
        "llm_enabled": settings.llm_enabled,
        "db_reachable": db_ok,
        "cache_dir_exists": cache_ok,
    }
