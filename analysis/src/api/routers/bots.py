"""
FastAPI router for GET /bot-activity. Thin: query-param resolution and
HTTPException wrapping only -- all aggregation lives in queries/bots.py.
Not mounted here; a separate workstream wires this into server.py.
"""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from fastapi import APIRouter, HTTPException, Query

from analysis.src.api.models.bots import BotActivityResponse
from analysis.src.api.queries import base
from analysis.src.api.queries.bots import get_bot_activity

router = APIRouter(tags=["bots"])

# Historical data stays queryable forever (owner decision) -- this is a
# convenience default, not a wall, hence explicit from/to stay available.
_DEFAULT_WINDOW = "30d"


@router.get("/bot-activity", response_model=BotActivityResponse)
def bot_activity(
    window: Optional[str] = Query(None, description="24h|7d|30d|90d|all (default 30d)"),
    date_from: Optional[datetime] = Query(None, alias="from"),
    date_to: Optional[datetime] = Query(None, alias="to"),
) -> BotActivityResponse:
    """Bot Detector panel: automation rate, behavioral-signal breakdown,
    account-age buckets, per-entity bot rates, bot-pushed narratives, and
    flagged account/doc evidence. Pass a `window` preset OR explicit
    `from`/`to` -- not both."""
    window_label = window
    if window_label is None and date_from is None and date_to is None:
        window_label = _DEFAULT_WINDOW
    try:
        start, end = base.resolve_time_range(window=window_label, date_from=date_from, date_to=date_to)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    return get_bot_activity(start=start, end=end, window_label=window_label)
