"""Shared pydantic v2 bases for the Phase 9 API contract: CamelModel,
LeanLabel (three-epistemic-kinds invariant), RangeMeta, SampleDocModel.
Contract details: docs/audit-trail/analysis/2026-07-24-phase9-prewave.md."""

from __future__ import annotations

from datetime import datetime
from typing import List, Literal, Optional

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class CamelModel(BaseModel):
    """Base for every Phase 9 API response model. Python fields stay
    snake_case; JSON keys are generated as camelCase (`to_camel`).
    `populate_by_name=True` so a model can also be constructed from its
    Python field names (tests, server-side assembly) without the alias."""

    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class LeanLabel(CamelModel):
    """
    The single place the three-epistemic-kinds lean invariant (owner
    decision 2026-07-22) is encoded: a lean is a stated FACT (official
    party registration), a CURATED editorial judgment (outlet/subreddit
    lean from the registry), or a statistically DERIVED estimate
    (analysis.author_leans / analysis.narrative_leans) -- and a derived
    lean must never render without the evidence (lean_share, confidence,
    sample_count) that backs it. Every UI surface showing a lean should
    consume this shape rather than a bare string, so the distinction is
    never lost in transit.
    """

    kind: Literal["fact", "curated", "derived"]
    value: str
    lean_share: Optional[float] = None
    confidence: Optional[float] = None
    sample_count: Optional[int] = None

    @model_validator(mode="after")
    def _evidence_matches_kind(self) -> "LeanLabel":
        evidence_present = (
            self.lean_share is not None
            or self.confidence is not None
            or self.sample_count is not None
        )
        evidence_complete = (
            self.lean_share is not None
            and self.confidence is not None
            and self.sample_count is not None
        )
        if self.kind == "derived" and not evidence_complete:
            raise ValueError(
                "kind='derived' requires lean_share, confidence, and sample_count "
                "(a derived lean must never surface without its evidence)"
            )
        if self.kind != "derived" and evidence_present:
            raise ValueError(
                f"kind={self.kind!r} must not carry derived-only evidence fields "
                "(lean_share/confidence/sample_count) -- those apply to kind='derived' only"
            )
        return self


class RangeMeta(CamelModel):
    """
    Honesty metadata every aggregate response carries (owner decision
    2026-07-24): the resolved time bounds (None = unbounded), the two
    admission bases counted separately, and the distinct model_ids of the
    runs behind the aggregate -- so a long historical range can be labeled
    as spanning methodology (model/prompt/source-mix) changes instead of
    being presented as one continuous comparable series.
    """

    window: Optional[str] = None
    start: Optional[datetime] = None
    end: Optional[datetime] = None
    sampled_doc_count: int
    official_record_doc_count: int
    model_ids: List[str]


class SampleDocModel(CamelModel):
    """One evidence sample backing a drill-down. `source_url` is
    required and non-empty (invariant C1: every surfaced sample carries a
    source link back to the original)."""

    doc_id: int
    source_url: str = Field(..., min_length=1)
    snippet: Optional[str] = None
    confidence: float
    admission_class: Literal["sampled", "official_record"]
    published_at: Optional[datetime] = None


class EntityProfileModel(CamelModel):
    """
    Editorial profile payload for one entity card, restoring the
    pre-Postgres UI's `EntityProfile` shape (see
    docs/audit-trail/api/2026-07-27-entity-profile-restoration.md). The PG
    `corpus.entities` columns feed this model's fields per `kind` (mapping
    decided in `queries/profiles.py::_map_entity_row`), NOT one-to-one by
    name -- in particular:

      * `lean` (this model) <- `entities.lean_source` (the curated
        pre-flattening classification string), for outlet/subreddit rows.
        NEVER `entities.lean` -- that column is the flattened join-axis
        enum and is never surfaced through this field.
      * `lean_source` (this model) <- `entities.source_citation` -- the
        CITATION for the lean above, not the lean value itself. Same PG
        column also feeds `bio_source` (see next point); the two model
        fields are populated from opposite `kind` branches, never both.
      * `bio_source` (this model) <- `entities.source_citation` too, but
        for official/collective rows (the citation for `party`, i.e. the
        bio_source of the old officials registry).
      * `party` (this model) <- `entities.lean_source` for official/
        collective rows (party membership was the "lean" for officials).
    """

    kind: str
    key: str
    display_name: str
    blurb: str = ""
    lean: Optional[str] = None
    lean_source: Optional[str] = None
    owner: Optional[str] = None
    founded: Optional[int] = None
    circulation_note: Optional[str] = None
    office: Optional[str] = None
    party: Optional[str] = None
    term_start: Optional[str] = None
    bio_source: Optional[str] = None
    subscriber_count_proxy: Optional[str] = None
    account_type: Optional[str] = None
    entity_id: Optional[int] = None


class ClassificationSampleModel(CamelModel):
    """
    One evidence sample for the Source-signals drill-down, restoring the
    pre-Postgres UI's `ClassificationSample` shape (see
    docs/audit-trail/api/2026-07-27-entity-profile-restoration.md).
    Assembled by `queries/base.py::build_classification_sample` from
    `fetch_rich_sample_fields()`'s per-doc dict.
    """

    doc_id: int
    label: str
    confidence: float
    reasoning: Optional[str] = None
    evidence_spans: List[str] = Field(default_factory=list)
    sarcasm_detected: Optional[bool] = None
    title: Optional[str] = None
    source_type: str
    source_name: Optional[str] = None
    date: Optional[str] = None
    full_text: Optional[str] = None
    url: Optional[str] = None
    topic: Optional[str] = None
    engagement: Optional["SampleEngagementModel"] = None
    author: Optional["SampleAuthorModel"] = None
    targets: Optional[List["SampleTargetModel"]] = None
    narrative: Optional[str] = None


class SampleEngagementModel(CamelModel):
    """Per-platform engagement counts backing a ClassificationSample --
    retweet/reply/like/quote for X, score/num_comments for Reddit. Fields
    outside the doc's own platform stay None (never fabricated as 0)."""

    retweets: Optional[int] = None
    replies: Optional[int] = None
    likes: Optional[int] = None
    quotes: Optional[int] = None
    score: Optional[int] = None
    num_comments: Optional[int] = None


class SampleAuthorModel(CamelModel):
    """X author metadata from corpus.authors backing a ClassificationSample.
    None/absent for non-X docs -- Reddit stores no author profile here, and
    we never fabricate one."""

    handle: Optional[str] = None
    display_name: Optional[str] = None
    avatar_url: Optional[str] = None
    verified_type: Optional[str] = None
    followers_count: Optional[int] = None
    account_created_at: Optional[datetime] = None
    bio: Optional[str] = None


class SampleTargetModel(CamelModel):
    """One "about X -- stance" chip on a ClassificationSample, sourced
    from analysis.target_mentions."""

    label: str
    stance: Literal["positive", "negative", "neutral", "mixed"]
