"""
Response models for GET /api/v1/propaganda -- see
queries/propaganda.py for the live aggregation these shapes wrap.
"""

from __future__ import annotations

from typing import List, Literal

from pydantic import Field

from analysis.src.api.models.common import CamelModel, RangeMeta, SampleDocModel


class TechniqueCount(CamelModel):
    technique: str
    count: int
    pct_of_flagged_docs: float
    sample_evidence: List[str] = Field(default_factory=list)


class SourceSplit(CamelModel):
    source: Literal["news", "social"]
    total_docs: int
    flagged_docs: int
    flagged_rate_pct: float
    mean_score: float


class PartySplit(CamelModel):
    """`party` is corpus.entities.lean (democrat/republican/independent/
    mixed) or 'unknown' for authors with no resolved entity -- the schema
    has no separate party column, entities.lean is the single source of
    curated affiliation."""

    party: str
    total_docs: int
    flagged_docs: int
    flagged_rate_pct: float
    mean_score: float


class PropagandaEntityRow(CamelModel):
    """One registry entity's propaganda footprint over analysis.runs
    (is_current, done, task='propaganda') for [start, end], resolved via
    corpus.news_articles.outlet_entity_id / corpus.reddit_posts
    .subreddit_entity_id / corpus.authors -> corpus.author_profiles
    .entity_id, in that priority order. `group` is 'news'/'subreddit' for
    an outlet/subreddit-resolved entity, or the author's
    corpus.author_profiles.tier for an author-resolved entity."""

    entity_id: int
    entity_key: str
    display_name: str
    group: str
    doc_count: int
    mean_density: float
    flagged_share: float


class PropagandaTierSplit(CamelModel):
    """News/Officials/Public split (the ThreeWayGrid tiers, restored per
    docs/todos/ui-feature-restoration.md): 'news' is
    corpus.documents.source_type='news'; 'officials' is an author whose
    corpus.author_profiles.tier='elected_official'; every other doc
    (affiliated/general_public authors, and docs with no resolved author
    profile) buckets as 'public' -- mirrors by_party's 'unknown' convention
    of accounting for every eligible doc rather than dropping unmatched
    ones."""

    group: Literal["news", "officials", "public"]
    total_docs: int
    flagged_docs: int
    flagged_rate_pct: float
    mean_score: float


class PropagandaOverviewModel(CamelModel):
    """GET /api/v1/propaganda response. The denominator behind
    total_eligible_docs/flagged_docs/mean_score is analysis.runs (is_current,
    done, task='propaganda'), not propaganda_results rows -- a deterministic
    pre-filter-clean doc gets a real run row and belongs in the
    denominator, not just the flagged numerator."""

    range: RangeMeta
    total_eligible_docs: int
    flagged_docs: int
    flagged_rate_pct: float
    mean_score: float
    by_technique: List[TechniqueCount] = Field(default_factory=list)
    by_source: List[SourceSplit] = Field(default_factory=list)
    by_party: List[PartySplit] = Field(default_factory=list)
    by_tier: List[PropagandaTierSplit] = Field(default_factory=list)
    by_entity: List[PropagandaEntityRow] = Field(default_factory=list)
    examples: List[SampleDocModel] = Field(default_factory=list)
