"""
FastAPI shared dependencies: admin-token gate and pipeline-trigger cooldown.

Isolated from the router/handler modules so each router can import one cheap
module instead of pulling in `server.py` (which owns the FastAPI app and
aggregator wiring).
"""

import secrets
import time as _time
from typing import Dict, Optional

from fastapi import Header, HTTPException

from analysis.src.common.settings import get_settings


def require_admin_token(x_admin_token: Optional[str] = Header(default=None)) -> None:
    """Reject requests missing or with an incorrect X-Admin-Token header.

    Returns 503 (not 401) when the server has no admin token configured, so
    a misconfigured deploy fails loudly instead of silently allowing everyone
    in.
    """
    settings = get_settings()
    expected = settings.admin_token
    if not expected:
        raise HTTPException(
            status_code=503,
            detail="Admin endpoints disabled: CIVIC_ADMIN_TOKEN is not set on the server.",
        )
    if not x_admin_token or not secrets.compare_digest(x_admin_token, expected):
        raise HTTPException(status_code=401, detail="Invalid or missing admin token.")


# Pipeline-trigger cooldown state: in-memory, per-endpoint, single-process.
# Good enough for a single-worker uvicorn — the concrete risk is a UI bug or
# misbehaving client spamming the trigger, not a coordinated attacker.
_TRIGGER_LAST_FIRED: Dict[str, float] = {}


def enforce_trigger_cooldown(endpoint: str) -> None:
    """Raise 429 when the named trigger fired less than the cooldown ago."""
    cooldown = get_settings().pipeline_trigger_cooldown_seconds
    if cooldown <= 0:
        return
    now = _time.time()
    last = _TRIGGER_LAST_FIRED.get(endpoint, 0.0)
    if now - last < cooldown:
        retry_in = int(cooldown - (now - last))
        raise HTTPException(
            status_code=429,
            detail=f"Trigger '{endpoint}' fired recently; retry in {retry_in}s",
            headers={"Retry-After": str(retry_in)},
        )
    _TRIGGER_LAST_FIRED[endpoint] = now
