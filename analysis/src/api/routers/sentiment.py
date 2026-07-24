"""
GET /api/v1/sentiment: net tone, distribution, platform/topic/time-of-day/
day-of-week splits, tier split, and per-entity stance aggregates. Not
mounted here -- registered by whichever workstream wires up server.py.
"""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from fastapi import APIRouter, HTTPException, Query

from analysis.src.api.models.sentiment import SentimentPanelResponse
from analysis.src.api.queries.sentiment import get_sentiment_panel

router = APIRouter(tags=["sentiment"])

_DEFAULT_WINDOW = "30d"


@router.get("/sentiment", response_model=SentimentPanelResponse)
def get_sentiment(
    window: Optional[str] = Query(default=None),
    date_from: Optional[datetime] = Query(default=None, alias="from"),
    date_to: Optional[datetime] = Query(default=None, alias="to"),
):
    """Aggregates corpus/analysis tables live for a preset ``window`` (or
    'all') XOR an explicit ``from``/``to`` range. Historical data stays
    queryable forever (owner decision 2026-07-24) -- presets are
    conveniences over arbitrary ranges, not walls. Defaults to '30d' when
    no window or range is given."""
    if window is None and date_from is None and date_to is None:
        window = _DEFAULT_WINDOW
    try:
        return get_sentiment_panel(window=window, date_from=date_from, date_to=date_to)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc))
