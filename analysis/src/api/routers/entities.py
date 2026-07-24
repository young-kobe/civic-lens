"""
GET /api/v1/entity-posts and GET /api/v1/entity-profile/{entity_id}. Not
mounted here -- registered by whichever workstream wires up server.py.
"""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from fastapi import APIRouter, HTTPException, Query

from analysis.src.api.models.entities import EntityPostsResponse, EntityProfileResponse
from analysis.src.api.queries.entities import get_entity_posts, get_entity_profile

router = APIRouter(tags=["entities"])


def _as_http_error(exc: ValueError) -> HTTPException:
    """Uniform mapping for both routes: unknown-entity -> 404, anything
    else (range validation from resolve_time_range) -> 400."""
    status_code = 404 if str(exc).startswith("unknown entity_id") else 400
    return HTTPException(status_code=status_code, detail=str(exc))


@router.get("/entity-posts", response_model=EntityPostsResponse)
def get_entity_posts_route(
    entity_id: int,
    window: Optional[str] = Query(default=None),
    date_from: Optional[datetime] = Query(default=None, alias="from"),
    date_to: Optional[datetime] = Query(default=None, alias="to"),
    page: int = Query(default=1, ge=1),
):
    """Paginated docs mentioning/authored-by entity_id. Accepts a preset
    ``window`` (or 'all') XOR an explicit ``from``/``to`` range; omitting
    all three defaults to 'all' (no time cutoff) -- an official's
    official_record history older than every preset window stays
    reachable without the caller having to know to ask for 'all'."""
    if window is None and date_from is None and date_to is None:
        window = "all"
    try:
        return get_entity_posts(entity_id, window, date_from, date_to, page)
    except ValueError as exc:
        raise _as_http_error(exc)


@router.get("/entity-profile/{entity_id}", response_model=EntityProfileResponse)
def get_entity_profile_route(entity_id: int):
    """ALL-TIME entity profile -- no window param, see get_entity_profile."""
    try:
        return get_entity_profile(entity_id)
    except ValueError as exc:
        raise _as_http_error(exc)
