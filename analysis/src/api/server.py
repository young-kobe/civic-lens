"""
Civic Lens Analysis API Server.

Versioned routers (admin / data / review) are mounted under ``/api/v1``. The
health router is deliberately unversioned at the app root — infra probes
shouldn't have to track the API version.

When v2 arrives, keep this file as the one place that lists which versions
are live: add a `v2_router` import, mount it at ``/api/v2``, and retire v1
when the migration window ends. A single-line string constant didn't
document anything the include_router line doesn't already show, so we
inline the prefix here.
"""

from fastapi import FastAPI

from analysis.src.api.routers import (
    admin_router,
    data_router,
    health_router,
    review_router,
)

# The UI is served same-origin (Vite proxy in dev, Caddy/Cloudflare in prod),
# so CORS middleware is intentionally absent. If the UI ever lives on a
# different origin, add CORSMiddleware back with an explicit origin allowlist
# — never "*" combined with allow_credentials=True.

API_VERSION = "v1"
V1_PREFIX = f"/api/{API_VERSION}"

app = FastAPI(title="Civic Lens API", version=API_VERSION)

# Unversioned infra endpoint.
app.include_router(health_router)

# v1 surface. When v2 lands, include a v2_router here alongside — keep both
# mounted during the migration window, retire v1 when clients have moved.
app.include_router(admin_router, prefix=V1_PREFIX)
app.include_router(data_router, prefix=V1_PREFIX)
app.include_router(review_router, prefix=V1_PREFIX)
