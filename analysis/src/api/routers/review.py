"""
Human review endpoints (walkthrough 034).

All review endpoints are gated behind CIVIC_ADMIN_TOKEN — /review/queue leaks
up to 1200 chars of raw scraped text per item, so the gate is required, not
optional.
"""

from typing import Optional

from fastapi import APIRouter, Depends, HTTPException
from pydantic import BaseModel

from analysis.src.api.dependencies import require_admin_token
from analysis.src.common.settings import get_settings
from analysis.src.reporting.review import ReviewService

settings = get_settings()
review_service = ReviewService(settings.db_path)

router = APIRouter(
    tags=["review"],
    dependencies=[Depends(require_admin_token)],
)


class ReviewSubmission(BaseModel):
    ai_output_id: int
    is_correct: Optional[int] = None  # 1 correct, 0 incorrect, None unscored
    human_label: Optional[str] = None
    human_confidence: Optional[float] = None
    is_golden: bool = False
    reviewer_id: Optional[str] = None
    notes: Optional[str] = None


@router.get("/review/queue")
def get_review_queue(
    task: str,
    source_type: Optional[str] = None,
    confidence_max: Optional[float] = None,
    limit: int = 20,
    offset: int = 0,
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
def submit_review(payload: ReviewSubmission):
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
def get_review_stats(task: Optional[str] = None):
    """Per-task counts of total outputs, reviewed, correct, incorrect, golden, accuracy %."""
    return review_service.get_stats(task_type=task)
