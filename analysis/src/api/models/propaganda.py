"""
Response models for GET /api/v1/propaganda -- see
queries/propaganda.py for the live aggregation these shapes wrap.
"""

from __future__ import annotations

from typing import Dict, List, Literal, Optional

from pydantic import Field

from analysis.src.api.models.common import CamelModel, EntityProfileModel, RangeMeta, SampleDocModel


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


class PropagandaTechniqueSpan(CamelModel):
    """One evidence span backing a PropagandaExample, from
    analysis.propaganda_techniques for the doc's current propaganda run."""

    technique: str
    evidence_span: Optional[str] = None
    confidence: float


class PropagandaExample(CamelModel):
    """One flagged doc backing a per-entity drill-down (restored per
    docs/todos/ui-feature-restoration.md, Wave 2 propaganda). ``domain`` is
    corpus.documents.domain_or_subreddit; ``url`` is source_url;
    ``text_preview`` is body truncated to SNIPPET_MAX_CHARS. ``author_handle``
    is null for news/reddit docs. ``party`` is entities.lean_source via the
    author's author_profiles.entity_id, populated only when that entity
    resolves to the editorial 'official' bucket -- never for non-editorial
    accounts or unaffiliated authors."""

    doc_id: int
    source_type: str
    domain: Optional[str] = None
    title: Optional[str] = None
    overall_score: float
    text_preview: str
    url: Optional[str] = None
    techniques: List[PropagandaTechniqueSpan] = Field(default_factory=list)
    author_handle: Optional[str] = None
    party: Optional[str] = None


class PropagandaEntityItem(CamelModel):
    """One registry entity's (or catch-all bucket's) propaganda footprint
    within a single tier (news outlets / officials / general public).
    Ranked by flagged_rate_pct desc within its tier, catch-alls last."""

    key: str
    kind: str
    entity_profile: EntityProfileModel
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
    # Wave 2 (docs/todos/ui-feature-restoration.md): per-tier ranked entity
    # leaderboards with full editorial profiles, restoring the old
    # PropagandaOverview.byNewsOutlet/byOfficial/byGeneralPublic contract.
    by_news_outlet: List[PropagandaEntityItem] = Field(default_factory=list)
    by_official: List[PropagandaEntityItem] = Field(default_factory=list)
    by_general_public: List[PropagandaEntityItem] = Field(default_factory=list)
    # Per-entity flagged-example bucket, keyed by the same key used in
    # PropagandaEntityItem.key (entity_key or catch-all sentinel) -- the
    # drill-down modal reads from this, not the small global `examples` list.
    examples_by_entity: Dict[str, List[PropagandaExample]] = Field(default_factory=dict)


class PropagandaPublicPostsResponse(CamelModel):
    """GET /api/v1/propaganda-public-posts payload -- the Propaganda page's
    public-column feed. Items reuse the PropagandaExample drill-down shape
    (a clean scored post carries an empty ``techniques`` list and its true
    density); ``window`` is None for an explicit from/to range request."""

    window: Optional[str]
    page: int
    page_size: int
    total: int
    items: List[PropagandaExample]
