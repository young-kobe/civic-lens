"""
Response models for GET /api/v1/docs/{doc_id} -- see
queries/docs.py for the live lookup these shapes wrap.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Literal, Optional

from pydantic import Field

from analysis.src.api.models.common import CamelModel


class NewsExtrasModel(CamelModel):
    domain: Optional[str] = None
    extraction_version: Optional[str] = None
    outlet_entity_id: Optional[int] = None


class RedditExtrasModel(CamelModel):
    subreddit: Optional[str] = None
    score: Optional[int] = None
    num_comments: Optional[int] = None
    subreddit_entity_id: Optional[int] = None


class XPostExtrasModel(CamelModel):
    tweet_id: str
    conversation_id: Optional[str] = None
    lang: Optional[str] = None
    place_country_code: Optional[str] = None
    retweet_count: Optional[int] = None
    reply_count: Optional[int] = None
    like_count: Optional[int] = None
    quote_count: Optional[int] = None
    is_official_tier: Optional[bool] = None
    referenced_tweet_id: Optional[str] = None
    referenced_tweet_type: Optional[str] = None


class AnalysisResultModel(CamelModel):
    """One current (is_current, status='done') run against this doc.
    `fields` carries the task-specific typed result columns (see
    queries/docs.py's per-task fetchers) -- deliberately untyped here since
    the shape varies by task."""

    task: str
    run_id: int
    confidence: Optional[float] = None
    model_id: str
    prompt_version: Optional[str] = None
    fields: Dict[str, Any] = Field(default_factory=dict)


class CitationEdgeModel(CamelModel):
    """One citation edge. `doc_id`/`source_url`/`published_at`/
    `admission_class` resolve the OTHER side of the edge with NO time bound
    -- an outbound edge may instead be `target_url`-only when it points at a
    URL we never ingested (invariant: url-only citations are included, not
    dropped)."""

    link_type: str
    doc_id: Optional[int] = None
    source_url: Optional[str] = None
    published_at: Optional[datetime] = None
    admission_class: Optional[Literal["sampled", "official_record"]] = None
    target_url: Optional[str] = None


class DocumentDetailResponse(CamelModel):
    """GET /api/v1/docs/{doc_id} response: document core fields + admission
    class + source_url, subtype extras (exactly one of news/reddit/x_post
    populated), every current analysis result, and citations in/out."""

    doc_id: int
    source_type: Literal["news", "reddit_post", "x_post"]
    domain_or_subreddit: Optional[str] = None
    author_id: Optional[int] = None
    published_at: datetime
    title: Optional[str] = None
    body: str
    source_url: str
    admission_class: Literal["sampled", "official_record"]
    news_extras: Optional[NewsExtrasModel] = None
    reddit_extras: Optional[RedditExtrasModel] = None
    x_post_extras: Optional[XPostExtrasModel] = None
    analysis_results: List[AnalysisResultModel] = Field(default_factory=list)
    citations_out: List[CitationEdgeModel] = Field(default_factory=list)
    citations_in: List[CitationEdgeModel] = Field(default_factory=list)
