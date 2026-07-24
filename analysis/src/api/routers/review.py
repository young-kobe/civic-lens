"""
Human review endpoints (Phase 9 strictly-live rewrite of the SQLite-era
review router). All routes stay behind CIVIC_ADMIN_TOKEN --
``/review/queue`` leaks up to ``TEXT_PREVIEW_CHARS`` of raw scraped text per
item, so the gate is required, not optional.
"""

from __future__ import annotations

from typing import Literal, Optional

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel, Field
from starlette.requests import Request

from analysis.src.api.dependencies import require_admin_token
from analysis.src.api.rate_limits import limiter
from analysis.src.review import service as review_service

router = APIRouter(
    tags=["review"],
    dependencies=[Depends(require_admin_token)],
)

# analysis.task enum values (data/pg-migrations/0001_north_star.sql) -- a
# deliberate contract change from the old sentiment/favorability/
# bot_detection task names. account_tier is author-scoped, so its queue is
# always empty (see review/service.py::get_queue), but it is still a
# syntactically valid filter value.
ReviewTask = Literal["bot", "text", "targets", "propaganda", "claims", "citations", "account_tier"]
ReviewSourceType = Literal["news", "reddit_post", "x_post"]
ReviewVerdict = Literal["correct", "incorrect", "uncertain"]


class ReviewSubmission(BaseModel):
    """Bounded fields only -- unbounded strings would let a reviewer bloat
    the DB or corrupt accuracy stats (audit §1.2, carried over from the
    pre-redesign contract)."""

    run_id: int
    verdict: ReviewVerdict
    reviewer_id: Optional[str] = Field(default=None, max_length=255)
    notes: Optional[str] = Field(default=None, max_length=2000)
    is_golden: bool = False
    expected_label: Optional[str] = Field(default=None, max_length=2000)


@router.get("/review/queue")
def get_review_queue(
    task: ReviewTask,
    source_type: Optional[ReviewSourceType] = None,
    confidence_max: Optional[float] = Query(default=None, ge=0.0, le=1.0),
    limit: int = Query(default=20, ge=1, le=100),
    offset: int = Query(default=0, ge=0),
):
    """Return up to ``limit`` unreviewed current runs for ``task``, ordered
    lowest-confidence first."""
    return review_service.get_queue(
        task, source_type=source_type, confidence_max=confidence_max,
        limit=limit, offset=offset,
    )


@router.post("/review/submit")
@limiter.limit("30/hour")
def submit_review(request: Request, payload: ReviewSubmission):
    """Persist a human verdict for one run. Marking ``is_golden`` also
    mints/refreshes the run's (doc_id, task) analysis.golden_labels row in
    the same transaction."""
    try:
        return review_service.submit(
            payload.run_id, payload.verdict,
            reviewer_id=payload.reviewer_id, notes=payload.notes,
            is_golden=payload.is_golden, expected_label=payload.expected_label,
        )
    except ValueError as e:
        status_code = 404 if "not found" in str(e) else 400
        raise HTTPException(status_code=status_code, detail=str(e))


@router.get("/review/stats")
def get_review_stats(task: Optional[ReviewTask] = None):
    """Per-task counts of current-done runs, reviewed evals, and observed
    agreement."""
    return review_service.get_stats(task=task)
