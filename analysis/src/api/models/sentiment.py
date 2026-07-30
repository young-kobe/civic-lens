"""
Response models for GET /api/v1/sentiment (Phase 9 strictly-live panel):
net tone, intensity distribution, platform/topic/time-of-day/day-of-week
splits, the news/officials/general-public tier split, and per-entity
stance aggregates. See the queries/sentiment/ package for the aggregation.
"""

from __future__ import annotations

from typing import Dict, List, Literal, Optional

from analysis.src.api.models.common import (
    CamelModel,
    ClassificationSampleModel,
    EntityProfileModel,
    LeanLabel,
    RangeMeta,
    SampleDocModel,
)


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


class EntityTopicCell(CamelModel):
    """One topic-scoped EXPRESSED cell for an entity's own posts (this
    entity's own sentiment label counts, grouped by the doc's dominant
    target_mentions topic) -- volume-sorted, net withheld below
    MIN_TARGET_SAMPLE_N."""

    topic: str
    net: Optional[float]
    volume: int
    low_sample: bool


class EntityDailyTonePoint(CamelModel):
    """One day of an entity's own-post net-tone series. Net withheld
    below MIN_TARGET_SAMPLE_N."""

    date: str
    net: Optional[float]
    volume: int
    low_sample: bool


class OutboundTargetCell(CamelModel):
    """One "who this bucket talks about" row. ``kind`` is the resolved
    target's corpus.entities.kind (official/collective/outlet/subreddit)
    when entity_id resolved, 'collective' for a raw_target matched against
    the GOP/DEM alias sets, 'raw' for a recurring unresolved free-text
    target, 'other' for the pooled one-off/overflow bucket."""

    label: str
    entity_key: Optional[str]
    kind: str
    net: Optional[float]
    volume: int
    low_sample: bool


class OutboundTargets(CamelModel):
    """News/public-tier "who they're talking about" rollup -- the inverse
    of received tone. Capped at MAX_OUTBOUND_TARGETS named rows plus an
    'Other targets' overflow row."""

    min_sample_n: int
    volume: int
    targets: List[OutboundTargetCell]


class ReceivedTopicCell(CamelModel):
    topic: str
    net: Optional[float]
    volume: int
    low_sample: bool


class ReceivedSpeakerTierCell(CamelModel):
    """WHO is talking about the entity: news / officials / affiliated /
    public, from the mentioning doc's own tier (source_type='news' ->
    news; author_profiles.tier elected_official/affiliated -> officials/
    affiliated; everything else -> public)."""

    tier: Literal["news", "officials", "affiliated", "public"]
    net: Optional[float]
    volume: int
    low_sample: bool


class ReceivedNarrativeCell(CamelModel):
    """Top narratives driving the mentions of an entity, by volume."""

    narrative_id: int
    name: str
    net: Optional[float]
    volume: int
    low_sample: bool


class ReceivedSourceCell(CamelModel):
    """One provenance cell answering WHO a slice of received tone comes
    from -- the inbound-edge counterpart to OutboundTargetCell.
    ``received_from_groups`` rolls cells up by (source_class, lean);
    ``received_from_top`` lists named individual sources. ``lean`` is the
    flat corpus.political_lean enum value of the source (never
    lean_source), None for the 'other' catch-all and for unresolved
    sampled X users (per-sampled-user derived lean is out of scope, see
    docs/todos/provenance-and-plain-language.md). ``share`` is this cell's
    volume over the received block's total volume -- all cells for one
    ReceivedTone sum to 1.0. ``net``/``low_sample`` share received tone's
    MIN_TARGET_SAMPLE_N suppression floor. Collective (gop/dem) provenance
    denominators are built the same way, but from the alias-matched
    unresolved raw_target rows that feed the collective rollup, not from
    an entity_id-resolved mention."""

    source_class: Literal["news_outlet", "official", "account", "subreddit", "x_user", "other"]
    label: str
    entity_key: Optional[str] = None
    lean: Optional[str] = None
    entity_profile: Optional[EntityProfileModel] = None
    share: float
    volume: int
    net: Optional[float] = None
    low_sample: bool = False


class ReceivedTone(CamelModel):
    """How sampled posts talk ABOUT an entity (the reputational signal),
    as opposed to an EntitySentimentItem's own positive/negative/neutral
    counts, which score the entity's OWN posts."""

    net: Optional[float]
    volume: int
    low_sample: bool
    engagement_weighted_net: Optional[float]
    by_topic: List[ReceivedTopicCell] = []
    by_speaker_tier: List[ReceivedSpeakerTierCell] = []
    by_narrative: List[ReceivedNarrativeCell] = []
    received_from_groups: List[ReceivedSourceCell] = []
    received_from_top: List[ReceivedSourceCell] = []
    samples: List[ClassificationSampleModel] = []


class ExpressedAlignment(CamelModel):
    """How an official's own posts score toward same-party vs cross-party
    tracked targets (self-mentions excluded). Nets withheld below
    MIN_TARGET_SAMPLE_N. Also reused for the panel-wide alignment
    baseline (TargetToneMeta.baselines)."""

    same_party_net: Optional[float]
    same_party_volume: int
    cross_party_net: Optional[float]
    cross_party_volume: int


class TargetToneMeta(CamelModel):
    """Received-tone meta: the suppression threshold, resolution
    coverage, bot-authored-mention exclusion count, party-collective
    rollups, and the global same/cross-party alignment baseline."""

    min_sample_n: int
    resolved_mentions: int
    unresolved_mentions: int
    bot_excluded_mentions: int
    collectives: Dict[str, ReceivedTone]
    baselines: ExpressedAlignment


class EntitySentimentItem(CamelModel):
    """One per-entity card on the three-way (news/officials/public)
    sentiment dashboard. positive/negative/neutral/volume/net_score are
    the entity's OWN posts (MIXED folded into neutral); ``received`` is
    the tone directed AT the entity (officials only; None elsewhere)."""

    key: str
    kind: str
    entity_profile: EntityProfileModel
    positive: int
    negative: int
    neutral: int
    volume: int
    net_score: Optional[float]
    classification_samples: List[ClassificationSampleModel] = []
    received: Optional[ReceivedTone] = None
    expressed_alignment: Optional[ExpressedAlignment] = None
    by_topic: List[EntityTopicCell] = []
    outbound: Optional[OutboundTargets] = None
    engagement_total: int = 0
    daily_tone: List[EntityDailyTonePoint] = []


class ToneTrendCell(CamelModel):
    net: Optional[float]
    volume: int


class ToneTrendPoint(CamelModel):
    """One day of the day-by-tier expressed net-tone series overlaying
    the news/officials/public columns on one chart."""

    date: str
    news: ToneTrendCell
    officials: ToneTrendCell
    public: ToneTrendCell


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
    by_news_outlet: List[EntitySentimentItem] = []
    by_official: List[EntitySentimentItem] = []
    by_general_public: List[EntitySentimentItem] = []
    target_tone: Optional[TargetToneMeta] = None
    tone_trend: List[ToneTrendPoint] = []
    distribution_samples: Dict[str, List[ClassificationSampleModel]] = {}
    day_samples: Dict[str, List[ClassificationSampleModel]] = {}


class PublicPostsResponse(CamelModel):
    """GET /api/v1/public-posts payload -- the sentiment page's public
    column feed. ``topic`` echoes the active filter (None = all topics);
    ``window`` is None for an explicit from/to range request."""

    window: Optional[str]
    topic: Optional[str]
    page: int
    page_size: int
    total: int
    items: List[ClassificationSampleModel]
