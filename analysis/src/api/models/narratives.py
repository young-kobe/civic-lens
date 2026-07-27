"""
Response models for GET /api/v1/narratives -- see
queries/narratives.py for the live aggregation these shapes wrap.
"""

from __future__ import annotations

from datetime import date, datetime
from typing import List, Literal, Optional

from pydantic import Field

from analysis.src.api.models.common import CamelModel, LeanLabel, RangeMeta, SampleDocModel


class SourceBreakdownItem(CamelModel):
    source: Literal["news", "reddit", "x"]
    doc_count: int


class TimelinePoint(CamelModel):
    day: date
    doc_count: int


class NarrativeSummaryModel(CamelModel):
    """One narrative's in-window coverage overlay. `lean` is None whenever
    analysis.narrative_leans has no row for this narrative -- never
    rendered as a guess. `cited_docs` resolve regardless of the requested
    range (owner decision 2026-07-24): a member doc may cite a doc far
    outside the range, and that doc still renders fully."""

    narrative_id: int
    anchor_claim_text: Optional[str] = None
    doc_count: int
    source_breakdown: List[SourceBreakdownItem] = Field(default_factory=list)
    timeline: List[TimelinePoint] = Field(default_factory=list)
    net_sentiment: Optional[float] = None
    citation_count: int
    cited_docs: List[SampleDocModel] = Field(default_factory=list)
    propaganda_flagged_fraction: Optional[float] = None
    bot_pushed_fraction: Optional[float] = None
    lean: Optional[LeanLabel] = None
    member_doc_samples: List[SampleDocModel] = Field(default_factory=list)
    # First-ingested-by-us, not claim origin in the world (see CLAUDE.md
    # scope note / analysis.narratives.first_seen_doc_id column comment).
    # None whenever the narrative predates the first_seen columns existing.
    first_seen_at: Optional[datetime] = None
    first_seen_doc_id: Optional[int] = None
    # Mean analysis.narrative_docs.confidence (the claim-match confidence,
    # copied from analysis.claims.confidence at insert time) over ALL
    # in-window member docs -- not just the MAX_EVIDENCE_PER_SAMPLE ranked
    # subset member_doc_samples carries. None when no member doc in range
    # has a non-null confidence.
    mean_confidence: Optional[float] = None


class NarrativesResponse(CamelModel):
    """GET /api/v1/narratives response envelope: the resolved range
    (bounds, admission split, contributing model_ids) plus the ranked list."""

    range: RangeMeta
    narratives: List[NarrativeSummaryModel] = Field(default_factory=list)
