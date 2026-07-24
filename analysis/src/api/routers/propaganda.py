"""
Propaganda panel router (Phase 9 strictly-live). Thin wrapper over
queries/propaganda.py -- mounted by a separate workstream, so this module
only defines the APIRouter and does not register it on the app.
"""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from fastapi import APIRouter, HTTPException, Query

from analysis.src.api.models.propaganda import PropagandaOverviewModel
from analysis.src.api.queries import propaganda as propaganda_queries
from analysis.src.api.queries.base import resolve_time_range

router = APIRouter(tags=["propaganda"])

# Applied only when the caller passes neither `window` nor `from`/`to`
# (owner decision 2026-07-24: presets are a convenience default, not a wall).
DEFAULT_WINDOW = "30d"


@router.get("/propaganda", response_model=PropagandaOverviewModel)
def get_propaganda(
    window: Optional[str] = Query(default=None),
    date_from: Optional[datetime] = Query(default=None, alias="from"),
    date_to: Optional[datetime] = Query(default=None, alias="to"),
):
    """Propaganda-technique overview over a preset window, 'all', or an
    explicit from/to range -- exactly one of the two modes. Historical data
    stays fully queryable; a range only scopes the aggregate denominators."""
    resolved_window = window
    if resolved_window is None and date_from is None and date_to is None:
        resolved_window = DEFAULT_WINDOW
    try:
        start, end = resolve_time_range(
            window=resolved_window, date_from=date_from, date_to=date_to,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    payload = propaganda_queries.get_propaganda_overview(
        start=start, end=end, window_label=resolved_window,
    )
    return PropagandaOverviewModel(**payload)
