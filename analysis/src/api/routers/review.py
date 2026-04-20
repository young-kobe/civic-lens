"""
Human review endpoints (walkthrough 034).

All review endpoints are gated behind CIVIC_ADMIN_TOKEN — /review/queue leaks
up to 1200 chars of raw scraped text per item, so the gate is required, not
optional.
"""

from typing import Literal, Optional

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel, Field
from starlette.requests import Request

from analysis.src.api.dependencies import require_admin_token
from analysis.src.api.rate_limits import limiter
from analysis.src.common.settings import get_settings
from analysis.src.reporting.review import ReviewService

settings = get_settings()
review_service = ReviewService(settings.db_path)

router = APIRouter(
    tags=["review"],
    dependencies=[Depends(require_admin_token)],
)

# Whitelisted task + source enums — mirror the values written by the pipeline
# and exposed by the Review UI. Keeping these as Literals means a typo in the
# query string returns 422 at FastAPI's validation layer, never reaches SQL.
ReviewTask = Literal["sentiment", "favorability", "bot_detection", "claims", "propaganda"]
ReviewSourceType = Literal["news", "reddit_post", "reddit_comment", "x_post"]


class ReviewSubmission(BaseModel):
    """Bounded fields only — unbounded strings / unconstrained floats would let a
    reviewer bloat the DB or corrupt accuracy stats (audit §1.2)."""

    ai_output_id: int
    is_correct: Optional[Literal[0, 1]] = None  # 1 correct, 0 incorrect, None unscored
    human_label: Optional[str] = Field(default=None, max_length=2000)
    human_confidence: Optional[float] = Field(default=None, ge=0.0, le=1.0)
    is_golden: bool = False
    reviewer_id: Optional[str] = Field(default=None, max_length=255)
    notes: Optional[str] = Field(default=None, max_length=2000)


@router.get("/review/queue")
def get_review_queue(
    task: ReviewTask,
    source_type: Optional[ReviewSourceType] = None,
    confidence_max: Optional[float] = Query(default=None, ge=0.0, le=1.0),
    limit: int = Query(default=20, ge=1, le=100),
    offset: int = Query(default=0, ge=0),
):
    """Return up to ``limit`` unreviewed AI outputs ordered lowest-confidence first."""
    return review_service.get_queue(
        task_type=task,
        source_type=source_type,
        confidence_max=confidence_max,
        limit=limit,
        offset=offset,
    )


@router.post("/review/submit")
@limiter.limit("30/hour")
def submit_review(request: Request, payload: ReviewSubmission):
    """Persist a human review for an AI output. Replaces any existing review."""
    try:
        return review_service.submit(
            ai_output_id=payload.ai_output_id,
            is_correct=payload.is_correct,
            human_label=payload.human_label,
            human_confidence=payload.human_confidence,
            is_golden=payload.is_golden,
            reviewer_id=payload.reviewer_id,
            notes=payload.notes,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


@router.get("/review/stats")
def get_review_stats(task: Optional[ReviewTask] = None):
    """Per-task counts of total outputs, reviewed, correct, incorrect, golden, accuracy %."""
    return review_service.get_stats(task_type=task)
