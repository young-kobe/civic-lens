"""
FastAPI router for GET /outlet-profiles. Thin: query-param resolution and
HTTPException wrapping only -- all aggregation lives in queries/outlets.py.
Not mounted here; a separate workstream wires this into server.py.
"""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from fastapi import APIRouter, HTTPException, Query

from analysis.src.api.models.outlets import OutletProfilesResponse
from analysis.src.api.queries import base
from analysis.src.api.queries.outlets import get_outlet_profiles

router = APIRouter(tags=["outlets"])

# Historical data stays queryable forever (owner decision) -- this is a
# convenience default, not a wall, hence explicit from/to stay available.
_DEFAULT_WINDOW = "30d"


@router.get("/outlet-profiles", response_model=OutletProfilesResponse)
def outlet_profiles(
    window: Optional[str] = Query(None, description="24h|7d|30d|90d|all (default 30d)"),
    date_from: Optional[datetime] = Query(None, alias="from"),
    date_to: Optional[datetime] = Query(None, alias="to"),
) -> OutletProfilesResponse:
    """Outlet Profiles panel: per-domain/subreddit net tone x bot rate.
    Unlike every other panel, bot-authored content is INCLUDED on purpose
    (see queries/outlets.py). Pass a `window` preset OR explicit
    `from`/`to` -- not both."""
    window_label = window
    if window_label is None and date_from is None and date_to is None:
        window_label = _DEFAULT_WINDOW
    try:
        start, end = base.resolve_time_range(window=window_label, date_from=date_from, date_to=date_to)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    return get_outlet_profiles(start=start, end=end, window_label=window_label)
