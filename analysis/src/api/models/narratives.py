"""
Response models for GET /api/v1/narratives -- see
queries/narratives.py for the live aggregation these shapes wrap.
"""

from __future__ import annotations

from datetime import date, datetime
from typing import Dict, List, Literal, Optional

from pydantic import Field

from analysis.src.api.models.common import (
    CamelModel,
    ClassificationSampleModel,
    EntityProfileModel,
    LeanLabel,
    RangeMeta,
    SampleDocModel,
)


class SourceBreakdownItem(CamelModel):
    source: Literal["news", "reddit", "x"]
    doc_count: int


class TimelinePoint(CamelModel):
    day: date
    doc_count: int


class AccountProfileModel(CamelModel):
    """Old pre-Postgres `AccountProfile` subset carried by
    `first_seen_author` -- faction context for an X-origin narrative's
    first-seen doc. `branch`/`chamber`/`state_or_district` have no PG
    equivalent (that data lived in the retired SQLite `account_profiles`
    table); they are always None here, never guessed."""

    handle: Optional[str] = None
    full_name: Optional[str] = None
    party: Optional[str] = None
    branch: Optional[str] = None
    chamber: Optional[str] = None
    state_or_district: Optional[str] = None
    office_title: Optional[str] = None


class NarrativeSummaryModel(CamelModel):
    """One narrative's in-window coverage overlay. `lean` is None whenever
    analysis.narrative_leans has no row for this narrative -- never
    rendered as a guess. `cited_docs` resolve regardless of the requested
    range (owner decision 2026-07-24): a member doc may cite a doc far
    outside the range, and that doc still renders fully."""

    narrative_id: int
    name: str
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
    # First-seen-doc provenance (old contract restoration, owner decision
    # 2026-07-27) -- all None together whenever first_seen_doc_id is None.
    first_seen_source_type: Optional[str] = None
    first_seen_domain: Optional[str] = None
    # corpus.author_profiles.tier of the first-seen doc's author; None when
    # the doc has no author or the author has no profile row.
    first_seen_tier: Optional[str] = None
    first_seen_author: Optional[AccountProfileModel] = None
    first_seen_entity_profile: Optional[EntityProfileModel] = None
    # Coarse three-way frame: 'news' for a news first-seen doc, 'officials'
    # when first_seen_tier is elected_official/affiliated, else 'public'.
    first_seen_tier_group: Optional[Literal["news", "officials", "public"]] = None
    # True when the narrative's in-window member docs span more than one
    # of the three tier groups above.
    cross_tier: bool = False
    # analysis.citations grouped by link_type, targeting this narrative's
    # in-window member docs (the citing side is unbounded, same convention
    # as cited_docs above).
    inbound_by_link_type: Dict[str, int] = Field(default_factory=dict)
    # Up to MAX_EVIDENCE_PER_SAMPLE rich evidence samples (sentiment label,
    # reasoning, engagement, targets, narrative name) for the modal
    # drill-down -- restores the old ClassificationSample-backed table.
    top_supporting_docs: List[ClassificationSampleModel] = Field(default_factory=list)
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
