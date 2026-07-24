"""
FastAPI routers, Phase 9 strictly-live: every versioned router mounts under
``/api/v1``; the health router is deliberately unversioned (infrastructure
probes shouldn't have to track the API version).
"""

from analysis.src.api.routers.admin import router as admin_router
from analysis.src.api.routers.auth_bootstrap import router as auth_bootstrap_router
from analysis.src.api.routers.bots import router as bots_router
from analysis.src.api.routers.docs import router as docs_router
from analysis.src.api.routers.entities import router as entities_router
from analysis.src.api.routers.health import router as health_router
from analysis.src.api.routers.movers import router as movers_router
from analysis.src.api.routers.narratives import router as narratives_router
from analysis.src.api.routers.outlets import router as outlets_router
from analysis.src.api.routers.propaganda import router as propaganda_router
from analysis.src.api.routers.review import router as review_router
from analysis.src.api.routers.sentiment import router as sentiment_router
from analysis.src.api.routers.status import router as status_router

__all__ = [
    "admin_router",
    "auth_bootstrap_router",
    "bots_router",
    "docs_router",
    "entities_router",
    "health_router",
    "movers_router",
    "narratives_router",
    "outlets_router",
    "propaganda_router",
    "review_router",
    "sentiment_router",
    "status_router",
]
