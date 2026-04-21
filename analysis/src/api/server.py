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
from slowapi import _rate_limit_exceeded_handler
from slowapi.errors import RateLimitExceeded
from slowapi.middleware import SlowAPIMiddleware
from starlette.middleware.base import BaseHTTPMiddleware
from starlette.requests import Request
from starlette.responses import Response

from analysis.src.api.rate_limits import limiter
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

# Rate limits: default 120/min per client IP via middleware, plus tighter
# per-route decorators in the routers (/run/* = 1/hour, /review/submit =
# 30/hour, /geo-sentiment + /narratives live path = 10/min). See
# rate_limits.py for the CF-aware key_func (audit §1.6).
app.state.limiter = limiter
app.add_exception_handler(RateLimitExceeded, _rate_limit_exceeded_handler)
app.add_middleware(SlowAPIMiddleware)


class SecurityHeadersMiddleware(BaseHTTPMiddleware):
    """Attaches the baseline security headers to every response.

    HSTS is deliberately omitted — Cloudflare sets it at the edge and the
    origin is not directly HTTPS-reachable in prod (CF tunnels TLS via
    Authenticated Origin Pulls). Duplicating HSTS from the origin risks
    shipping a stricter policy than the edge during misconfig windows.
    CSP is permissive enough to allow the Google Fonts pair the UI uses
    (``fonts.googleapis.com`` stylesheet + ``fonts.gstatic.com`` fonts)
    and nothing else (audit §1.7).
    """

    async def dispatch(self, request: Request, call_next) -> Response:
        response = await call_next(request)
        response.headers.setdefault(
            "Content-Security-Policy",
            "default-src 'self'; "
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; "
            "font-src 'self' https://fonts.gstatic.com; "
            "img-src 'self' data:; "
            "script-src 'self'; "
            "connect-src 'self'; "
            "frame-ancestors 'none'; "
            "base-uri 'self'; "
            "form-action 'self'",
        )
        response.headers.setdefault("X-Content-Type-Options", "nosniff")
        response.headers.setdefault("X-Frame-Options", "DENY")
        response.headers.setdefault(
            "Referrer-Policy", "strict-origin-when-cross-origin"
        )
        response.headers.setdefault(
            "Permissions-Policy",
            "camera=(), microphone=(), geolocation=(), interest-cohort=()",
        )
        return response


app.add_middleware(SecurityHeadersMiddleware)

# Unversioned infra endpoint.
app.include_router(health_router)

# v1 surface. When v2 lands, include a v2_router here alongside — keep both
# mounted during the migration window, retire v1 when clients have moved.
app.include_router(admin_router, prefix=V1_PREFIX)
app.include_router(data_router, prefix=V1_PREFIX)
app.include_router(review_router, prefix=V1_PREFIX)
