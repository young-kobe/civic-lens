"""
FastAPI router for GET /movers. Thin: query-param resolution, previous-
period arithmetic, and HTTPException wrapping only -- all aggregation
lives in queries/movers.py. Not mounted here; a separate workstream wires
this into server.py.
"""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Optional

from fastapi import APIRouter, HTTPException, Query

from analysis.src.api.models.movers import MoversResponse
from analysis.src.api.queries import base
from analysis.src.api.queries.movers import get_movers

router = APIRouter(tags=["movers"])

_DEFAULT_WINDOW = "30d"


@router.get("/movers", response_model=MoversResponse)
def movers(
    window: Optional[str] = Query(None, description="24h|7d|30d|90d (default 30d)"),
    date_from: Optional[datetime] = Query(None, alias="from"),
    date_to: Optional[datetime] = Query(None, alias="to"),
) -> MoversResponse:
    """Window-over-window tone + favorability movers. Unlike every other
    Phase 9 panel, `window='all'` is rejected -- an unbounded range has no
    meaningful preceding equal-length period to compare against. For an
    explicit `from`/`to` range, the previous period is the same duration
    immediately preceding `from`."""
    window_label = window
    if window_label is None and date_from is None and date_to is None:
        window_label = _DEFAULT_WINDOW
    if window_label == "all":
        raise HTTPException(
            status_code=400,
            detail="movers requires a bounded window -- 'all' has no previous period to compare against",
        )
    try:
        current_start, current_end = base.resolve_time_range(
            window=window_label, date_from=date_from, date_to=date_to,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    # current_start is always set here: 'all' (the only case resolve_time_range
    # would leave it None) was rejected above.
    reference_end = current_end or datetime.now(timezone.utc)
    duration = reference_end - current_start
    previous_start = current_start - duration
    previous_end = current_start

    return get_movers(
        current_start=current_start, current_end=current_end,
        previous_start=previous_start, previous_end=previous_end,
        current_window_label=window_label,
    )
