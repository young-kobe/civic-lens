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
