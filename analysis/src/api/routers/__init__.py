"""
FastAPI routers. The versioned data/admin/review routers are mounted under
``/api/v1``; the health router is deliberately unversioned (infrastructure
probes shouldn't have to track the API version).
"""

from analysis.src.api.routers.admin import router as admin_router
from analysis.src.api.routers.auth_bootstrap import router as auth_bootstrap_router
from analysis.src.api.routers.data import router as data_router
from analysis.src.api.routers.health import router as health_router
from analysis.src.api.routers.review import router as review_router

__all__ = [
    "admin_router",
    "auth_bootstrap_router",
    "data_router",
    "health_router",
    "review_router",
]
