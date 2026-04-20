"""
Per-IP rate limits (audit §1.6).

The 60s-per-endpoint trigger cooldown in ``dependencies.py`` guards the
shared-state overrun (one client can't queue 200 background pipeline runs).
This module adds the complementary IP-scoped layer: even with the cooldown,
a client that hits the API through 100 proxy IPs can still DoS the live-
compute endpoints. slowapi solves that.

Key lookup is Cloudflare-aware because every production request arrives via
the CF edge — ``request.client.host`` would otherwise always be 127.0.0.1
(Caddy on the same box) and the limiter would treat every client as the
same IP. The fallbacks exist so dev / direct-hit traffic still gets sensible
isolation.
"""

from slowapi import Limiter
from starlette.requests import Request


def client_ip_key(request: Request) -> str:
    """Best-guess client IP for limit bucketing.

    Prefer ``CF-Connecting-IP`` (only Cloudflare sets it; origin firewall
    pins port 80/443 to CF ranges so spoofing would require bypassing the
    firewall — at which point rate limits aren't the weak link anyway).
    Fall back to the first entry of ``X-Forwarded-For`` for non-CF deploys
    and finally the socket peer.
    """
    cf = request.headers.get("cf-connecting-ip")
    if cf:
        return cf
    xff = request.headers.get("x-forwarded-for")
    if xff:
        return xff.split(",", 1)[0].strip()
    return request.client.host if request.client else "unknown"


# Module-level singleton so decorators and middleware share one state.
limiter = Limiter(
    key_func=client_ip_key,
    default_limits=["120/minute"],
)
