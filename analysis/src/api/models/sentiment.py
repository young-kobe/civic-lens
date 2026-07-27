"""
Response models for GET /api/v1/sentiment (Phase 9 strictly-live panel):
net tone, intensity distribution, platform/topic/time-of-day/day-of-week
splits, the news/officials/general-public tier split, and per-entity
stance aggregates. See queries/sentiment.py for the aggregation.
"""

from __future__ import annotations

from typing import List, Literal, Optional

from analysis.src.api.models.common import CamelModel, LeanLabel, RangeMeta, SampleDocModel


class SentimentOverview(CamelModel):
    """Headline counts for the window. ``volume`` is docs that actually
    carry a sentiment label; ``trivial_content_docs`` is analyzed runs with
    no sentiment_results row (never folded into a fake neutral count)."""

    net_score: Optional[float]
    volume: int
    total_analyzed: int
    trivial_content_docs: int
    excluded_bot_docs: int
    mean_confidence: Optional[float]


class SentimentDistribution(CamelModel):
    """Intensity buckets. MIXED folds into ``neutral``, matching the
    pre-redesign UI convention for Tone Intensity."""

    strong_positive: int
    mild_positive: int
    neutral: int
    mild_negative: int
    strong_negative: int


class BucketSentiment(CamelModel):
    """One row of a positive/negative/neutral/volume/net breakdown, keyed
    by whatever dimension the caller names (platform, topic, bucket, day)."""

    positive: int
    negative: int
    neutral: int
    volume: int
    net_score: Optional[float]


class PlatformSentiment(BucketSentiment):
    platform: str


class TopicSentiment(BucketSentiment):
    topic: str


class TimeOfDaySentiment(BucketSentiment):
    bucket: Literal["Morning", "Afternoon", "Evening", "Night"]


class DayOfWeekSentiment(BucketSentiment):
    day: str


class DailySentiment(BucketSentiment):
    """One calendar day's net tone/volume within the request window
    (`date_trunc('day', published_at)`) -- restores the tone-trend chart
    line (docs/todos/ui-feature-restoration.md). ``date`` is an ISO
    calendar date (YYYY-MM-DD), matching the ``month`` string convention
    on entities.MonthlyActivity."""

    date: str


class TierSplit(BucketSentiment):
    tier: Literal["news", "officials", "general_public"]


class StanceCounts(CamelModel):
    """One stance-count cell. ``net_score`` is withheld (None) below
    MIN_TARGET_SAMPLE_N -- a low-n cell reports its volume, never a
    +/-100 headline (``low_sample`` flags why)."""

    positive: int
    negative: int
    neutral: int
    mixed: int
    volume: int
    net_score: Optional[float]
    low_sample: bool


class TopicStanceCounts(StanceCounts):
    """One entity's stance breakdown for a single topic
    (analysis.target_mentions GROUP BY entity_id, topic) -- feeds the
    three-way grid's per-entity topic tabs. ``topic`` is 'General' when
    target_mentions.topic is NULL, matching the panel-level by_topic
    convention (never keyword-guessed)."""

    topic: str


class TierStanceCounts(StanceCounts):
    """One entity's stance breakdown by the speaker tier (news/officials/
    general_public) of the mentioning doc's author -- the "received tone
    by speaker tier" divergence view, computed via the same
    ``_tier_for_row`` convention as the panel-level by_tier."""

    tier: Literal["news", "officials", "general_public"]


class EntityStanceAggregate(CamelModel):
    """Per-entity stance aggregate from analysis.target_mentions
    (``target_stance``) -- the sole per-entity stance source as of
    2026-07-25 (analysis.favorability_stances is retired, no longer
    written). ``entity_id`` is None only for the unresolved-mentions
    catch-all (``catch_all_key``) -- unresolved target_mentions are never
    dropped. ``entity_key`` is the stable slug counterpart to ``entity_id``
    (owner decision 2026-07-26: emit both so cross-page joins are exact,
    not a (kind, displayName) guess); also None for the catch-all."""

    entity_id: Optional[int]
    entity_key: Optional[str] = None
    catch_all_key: Optional[str]
    display_name: str
    kind: Optional[str]
    lean: Optional[LeanLabel]
    target_stance: StanceCounts
    by_topic: List[TopicStanceCounts] = []
    received_by_tier: List[TierStanceCounts] = []
    samples: List[SampleDocModel]


class SentimentPanelResponse(CamelModel):
    """Full GET /api/v1/sentiment payload. ``range`` is the honesty block:
    resolved bounds (a preset, 'all', or a custom from/to pair all land
    here), the two admission-basis doc counts, and the distinct model_ids
    behind the aggregate -- so a long range spanning a model/prompt change
    can be labeled as such instead of presented as one comparable series."""

    range: RangeMeta
    overview: SentimentOverview
    distribution: SentimentDistribution
    by_platform: List[PlatformSentiment]
    by_topic: List[TopicSentiment]
    by_time_of_day: List[TimeOfDaySentiment]
    by_day_of_week: List[DayOfWeekSentiment]
    by_tier: List[TierSplit]
    daily: List[DailySentiment] = []
    entity_stances: List[EntityStanceAggregate]
    samples: List[SampleDocModel]
    disclaimer: str
