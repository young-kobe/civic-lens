"""
Narratives panel router (Phase 9 strictly-live). Thin wrapper over
queries/narratives.py -- mounted by a separate workstream, so this module
only defines the APIRouter and does not register it on the app.
"""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from fastapi import APIRouter, HTTPException, Query

from analysis.src.api.models.narratives import NarrativesResponse
from analysis.src.api.queries import narratives as narrative_queries
from analysis.src.api.queries.base import resolve_time_range

router = APIRouter(tags=["narratives"])

# Applied only when the caller passes neither `window` nor `from`/`to`
# (owner decision 2026-07-24: presets are a convenience default, not a wall).
DEFAULT_WINDOW = "30d"


@router.get("/narratives", response_model=NarrativesResponse)
def get_narratives(
    window: Optional[str] = Query(default=None),
    date_from: Optional[datetime] = Query(default=None, alias="from"),
    date_to: Optional[datetime] = Query(default=None, alias="to"),
    limit: int = Query(
        default=narrative_queries.DEFAULT_NARRATIVE_LIMIT,
        ge=1,
        le=narrative_queries.MAX_NARRATIVE_LIMIT,
    ),
):
    """Top narratives by member-doc count over a preset window, 'all', or
    an explicit from/to range -- exactly one of the two modes. Historical
    narratives stay fully queryable; a range only scopes which narratives
    rank and which member docs count toward the in-window aggregates."""
    resolved_window = window
    if resolved_window is None and date_from is None and date_to is None:
        resolved_window = DEFAULT_WINDOW
    try:
        start, end = resolve_time_range(
            window=resolved_window, date_from=date_from, date_to=date_to,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    payload = narrative_queries.get_narratives(
        start=start, end=end, limit=limit, window_label=resolved_window,
    )
    return NarrativesResponse(**payload)
