"""
Response models for GET /api/v1/entity-posts and GET
/api/v1/entity-profile/{entity_id} (Phase 9 strictly-live panels). See
queries/entities.py for the aggregation.
"""

from __future__ import annotations

from datetime import datetime
from typing import List, Literal, Optional

from analysis.src.api.models.common import CamelModel, LeanLabel, RangeMeta, SampleDocModel


class EntityPostRow(SampleDocModel):
    """One drill-down row: an analyzed doc mentioning and/or authored by
    the entity. ``relation`` is 'both' when a doc both mentions the entity
    and was authored/published by it."""

    relation: Literal["mentions", "authored_by", "both"]


class EntityPostsResponse(CamelModel):
    """GET /api/v1/entity-posts payload. ``window`` is None for an
    un-windowed (all-time) request -- required so officials' backfilled
    official_record history older than every preset window stays
    reachable. Carries both ``entity_id`` and ``entity_key`` (owner
    decision 2026-07-26: emit both so cross-page joins are exact, not a
    (kind, displayName) guess)."""

    entity_id: int
    entity_key: str
    window: Optional[str]
    page: int
    page_size: int
    total: int
    items: List[EntityPostRow]


class StanceDistribution(CamelModel):
    """Stance-count distribution on the analysis.target_mentions
    sentiment_label vocabulary (positive/negative/neutral/mixed) --
    the sole source for both stance_received and stance_expressed since
    analysis.favorability_stances retired 2026-07-25."""

    positive: int
    negative: int
    neutral: int
    mixed: int
    volume: int
    net_score: Optional[float]


class AdmissionSplit(CamelModel):
    sampled: int
    official_record: int


class MonthlyActivity(CamelModel):
    """One calendar month's doc count for the entity's own doc history,
    split by admission_class."""

    month: str
    doc_count: int
    sampled_count: int
    official_record_count: int


class EntityProfileResponse(CamelModel):
    """GET /api/v1/entity-profile/{entity_id} payload -- ALL-TIME, no
    window cutoff, so an inactive official's history never vanishes.
    ``range`` is the honesty block (owner decision 2026-07-24); its
    start/end are always null here since this endpoint is unconditionally
    all-time. Carries both ``entity_id`` and ``entity_key`` (owner
    decision 2026-07-26: emit both so cross-page joins are exact, not a
    (kind, displayName) guess)."""

    entity_id: int
    entity_key: str
    display_name: str
    kind: str
    lean: Optional[LeanLabel]
    range: RangeMeta
    analyzed_doc_counts: AdmissionSplit
    stance_received: StanceDistribution
    stance_expressed: StanceDistribution
    propaganda_rate: Optional[float]
    propaganda_analyzed_docs: int
    first_activity: Optional[datetime]
    last_activity: Optional[datetime]
    monthly_activity: List[MonthlyActivity]
