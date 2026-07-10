"""
Data models for reporting aggregations.

Contains dataclasses for structured API responses.
"""

import datetime
from dataclasses import asdict, dataclass, field
from typing import Any, Dict, List, Optional


# =============================================================================
# Outlet Profiles
# =============================================================================

@dataclass
class OutletProfile:
    """Profile metrics for a news outlet or subreddit."""
    outlet: str
    avg_sentiment: float
    bot_rate: float
    volume: int
    total_scanned: int

    def to_dict(self) -> Dict[str, Any]:
        return {
            "outlet": self.outlet,
            "avg_sentiment": self.avg_sentiment,
            "bot_rate": self.bot_rate,
            "volume": self.volume,
            "total_scanned": self.total_scanned,
        }


# =============================================================================
# Public Sentiment
# =============================================================================

@dataclass
class SentimentOverview:
    """Sentiment overview metrics."""
    netScore: float
    volume: int
    coverage: str
    confidence: str


@dataclass
class SentimentDistribution:
    """Sentiment distribution counts."""
    strongPositive: int
    mildPositive: int
    neutral: int
    mildNegative: int
    strongNegative: int


@dataclass
class PlatformSentiment:
    """Platform-level sentiment breakdown."""
    platform: str
    positive: int
    negative: int
    neutral: int
    volume: int


@dataclass
class ClassificationSample:
    """Sample of an individual LLM classification with reasoning."""
    doc_id: int
    label: str
    confidence: float
    reasoning: str
    evidence_spans: List[str]
    sarcasm_detected: bool
    title: str
    source_type: str
    date: Optional[str] = None
    source_name: Optional[str] = None
    full_text: str = ""
    url: Optional[str] = None


@dataclass
class TopicSentiment:
    """Topic-level sentiment breakdown with classification samples.

    The ``newsNet`` / ``officialsNet`` / ``publicNet`` fields are the
    three-way net-sentiment split that powers the topic-divergence panel
    in the Overall Tone page redesign (Phase 5). They are optional because
    pre-Phase-3b callers and cached snapshots won't carry them; when None,
    the UI falls back to the single-tier net derived from positive/negative.
    """
    topic: str
    positive: int
    negative: int
    neutral: int
    volume: int
    sarcasm_rate: float = 0.0
    classification_samples: List[ClassificationSample] = field(default_factory=list)
    newsNet: Optional[float] = None
    officialsNet: Optional[float] = None
    publicNet: Optional[float] = None
    newsVolume: int = 0
    officialsVolume: int = 0
    publicVolume: int = 0


@dataclass
class TimeWindowSentiment:
    """Time window sentiment breakdown."""
    window: str
    positive: int
    negative: int
    neutral: int
    volume: int


@dataclass
class DayOfWeekSentiment:
    """Per-weekday sentiment breakdown. `day` is a short label (Mon..Sun).

    Aggregated from each doc's local calendar weekday so a reader can see
    whether weekend vs weekday coverage leans differently. Orthogonal to
    TimeWindowSentiment's age buckets: one answers "when in the week",
    the other answers "how recently".
    """
    day: str
    positive: int
    negative: int
    neutral: int
    volume: int


@dataclass
class EntitySentimentItem:
    """Per-entity sentiment breakdown for the three-way dashboard frame.

    One instance per registry-matched entity (news outlet, verified
    official, or major subreddit) plus catch-alls keyed by pseudo-entity
    names like ``other-outlets``, ``general-public``. ``entity_profile``
    carries the static editorial metadata rendered next to the metrics
    (display name, blurb, partisan lean, etc.) — populated from the
    entity registry, or a lightweight placeholder dict for catch-alls.
    """
    key: str                    # domain / handle / subreddit / sentinel like "other-outlets"
    kind: str                   # outlet | official | subreddit | catch_all
    positive: int
    negative: int
    neutral: int
    volume: int
    netScore: float
    entity_profile: Dict[str, Any] = field(default_factory=dict)
    classification_samples: List[ClassificationSample] = field(default_factory=list)
    # Received tone (officials only) — how sampled posts talk ABOUT this
    # entity, from target_sentiment fan-out. Shape:
    #   {net: float|None, volume: int, lowSample: bool,
    #    byTopic: [{topic, net|None, volume, lowSample}], samples: [...]}
    # net is None (suppressed) below the aggregator's min-sample threshold.
    # None field = no target_sentiment coverage; netScore above remains the
    # EXPRESSED tone (this entity's own posts) — the UI labels the two.
    received: Optional[Dict[str, Any]] = None
    # Speaker-target alignment cells (officials only): how this official's
    # own posts score toward same-party vs cross-party tracked targets.
    #   {samePartyNet: float|None, samePartyVolume: int,
    #    crossPartyNet: float|None, crossPartyVolume: int}
    expressed_alignment: Optional[Dict[str, Any]] = None


def _entity_item_to_dict(item: "EntitySentimentItem") -> Dict[str, Any]:
    """Serialize an EntitySentimentItem into the UI-facing wire shape.

    Split out so PublicSentimentResult.to_dict() keeps a flat table layout
    instead of nesting another five-level comprehension for every new
    entity tier. Also used by any future consumer that needs to serialize
    an EntitySentimentItem directly (e.g. the narrative aggregator's
    per-tier entity rollups).
    """
    result = {
        "key": item.key,
        "kind": item.kind,
        "positive": item.positive,
        "negative": item.negative,
        "neutral": item.neutral,
        "volume": item.volume,
        "netScore": item.netScore,
        "entityProfile": item.entity_profile,
        "classificationSamples": [
            {
                "doc_id": s.doc_id, "label": s.label,
                "confidence": s.confidence, "reasoning": s.reasoning,
                "evidence_spans": s.evidence_spans,
                "sarcasm_detected": s.sarcasm_detected,
                "title": s.title or "", "source_type": s.source_type,
                "source_name": s.source_name,
                "date": s.date,
                "full_text": s.full_text,
                "url": s.url,
            }
            for s in item.classification_samples
        ],
    }
    # Only stamped when target_sentiment coverage exists, so older cached
    # snapshots serialize byte-identical to their pre-target shape.
    if item.received is not None:
        result["received"] = item.received
    if item.expressed_alignment is not None:
        result["expressedAlignment"] = item.expressed_alignment
    return result


@dataclass
class PublicSentimentResult:
    """Complete public sentiment response with merged GOP favorability data."""
    overview: SentimentOverview
    distribution: SentimentDistribution
    byPlatform: List[PlatformSentiment]
    disclaimer: str
    excluded_bot_content: int
    byTopic: List[TopicSentiment] = field(default_factory=list)
    byTimeWindow: List[TimeWindowSentiment] = field(default_factory=list)
    byDayOfWeek: List[DayOfWeekSentiment] = field(default_factory=list)
    # Three-way entity breakdown (walkthrough 057 — Phase 3b).
    # Present when the aggregator matched docs against the entity registries;
    # older cached snapshots leave these empty so old API consumers are safe.
    byNewsOutlet: List[EntitySentimentItem] = field(default_factory=list)
    byOfficial: List[EntitySentimentItem] = field(default_factory=list)
    byGeneralPublic: List[EntitySentimentItem] = field(default_factory=list)
    # Per-intensity-bucket drill-down samples. Keys: strongPositive, mildPositive,
    # neutral, mildNegative, strongNegative. Each value is a list of up to
    # DISTRIBUTION_SAMPLES_PER_BUCKET docs, confidence-sorted descending, so the
    # UI can open a bucket and audit the actual classifications.
    distributionSamples: Dict[str, List[ClassificationSample]] = field(default_factory=dict)
    socialVsNews: Optional[Dict[str, Any]] = None  # Social vs News comparison
    # Merged GOP favorability data
    gopFavorability: Optional[Dict[str, Any]] = None  # Stance breakdown (favorable/unfavorable/neutral %)
    gopTrend: Optional[List[Dict[str, Any]]] = None  # Daily net favorability trend
    gopByPlatform: Optional[List[Dict[str, Any]]] = None  # Platform-level stance breakdown
    pollingVsSocial: Optional[Dict[str, Any]] = None  # Live polling comparison
    # Target-tone metadata from the target_sentiment fan-out: suppression
    # threshold, resolution coverage, collective-target rollups, and the
    # global same-/cross-party alignment baselines. None until the targets
    # stage has produced rows.
    #   {minSampleN, resolvedMentions, unresolvedMentions,
    #    collectives: {gop_collective: <received shape>, dem_collective: ...},
    #    baselines: {samePartyNet|None, samePartyVolume,
    #                crossPartyNet|None, crossPartyVolume}}
    targetTone: Optional[Dict[str, Any]] = None

    def to_dict(self) -> Dict[str, Any]:
        result = {
            "overview": {
                "netScore": self.overview.netScore,
                "volume": self.overview.volume,
                "coverage": self.overview.coverage,
                "confidence": self.overview.confidence,
            },
            "distribution": {
                "strongPositive": self.distribution.strongPositive,
                "mildPositive": self.distribution.mildPositive,
                "neutral": self.distribution.neutral,
                "mildNegative": self.distribution.mildNegative,
                "strongNegative": self.distribution.strongNegative,
            },
            "byPlatform": [
                {"platform": p.platform, "positive": p.positive, "negative": p.negative, "neutral": p.neutral, "volume": p.volume}
                for p in self.byPlatform
            ],
            "byTopic": [
                {
                    "topic": t.topic, "positive": t.positive, "negative": t.negative,
                    "neutral": t.neutral, "volume": t.volume,
                    "sarcasm_rate": t.sarcasm_rate,
                    "newsNet": t.newsNet,
                    "officialsNet": t.officialsNet,
                    "publicNet": t.publicNet,
                    "newsVolume": t.newsVolume,
                    "officialsVolume": t.officialsVolume,
                    "publicVolume": t.publicVolume,
                    "classificationSamples": [
                        {
                            "doc_id": s.doc_id, "label": s.label,
                            "confidence": s.confidence, "reasoning": s.reasoning,
                            "evidence_spans": s.evidence_spans,
                            "sarcasm_detected": s.sarcasm_detected,
                            "title": s.title or "", "source_type": s.source_type,
                            "source_name": s.source_name,
                            "date": s.date,
                            "full_text": s.full_text,
                            "url": s.url,
                        }
                        for s in t.classification_samples
                    ],
                }
                for t in self.byTopic
            ],
            "byNewsOutlet": [_entity_item_to_dict(e) for e in self.byNewsOutlet],
            "byOfficial": [_entity_item_to_dict(e) for e in self.byOfficial],
            "byGeneralPublic": [_entity_item_to_dict(e) for e in self.byGeneralPublic],
            "byTimeWindow": [
                {"window": w.window, "positive": w.positive, "negative": w.negative, "neutral": w.neutral, "volume": w.volume}
                for w in self.byTimeWindow
            ],
            "byDayOfWeek": [
                {"day": d.day, "positive": d.positive, "negative": d.negative, "neutral": d.neutral, "volume": d.volume}
                for d in self.byDayOfWeek
            ],
            "distributionSamples": {
                bucket: [
                    {
                        "doc_id": s.doc_id, "label": s.label,
                        "confidence": s.confidence, "reasoning": s.reasoning,
                        "evidence_spans": s.evidence_spans,
                        "sarcasm_detected": s.sarcasm_detected,
                        "title": s.title or "", "source_type": s.source_type,
                        "source_name": s.source_name,
                        "date": s.date,
                        "full_text": s.full_text,
                        "url": s.url,
                    }
                    for s in samples
                ]
                for bucket, samples in self.distributionSamples.items()
            },
            "disclaimer": self.disclaimer,
            "excluded_bot_content": self.excluded_bot_content,
        }
        if self.socialVsNews:
            result["socialVsNews"] = self.socialVsNews
        if self.gopFavorability:
            result["gopFavorability"] = self.gopFavorability
        if self.gopTrend is not None:
            result["gopTrend"] = self.gopTrend
        if self.gopByPlatform is not None:
            result["gopByPlatform"] = self.gopByPlatform
        if self.pollingVsSocial:
            result["pollingVsSocial"] = self.pollingVsSocial
        if self.targetTone is not None:
            result["targetTone"] = self.targetTone
        return result


# =============================================================================
# GOP Favorability
# =============================================================================

@dataclass
class FavorabilityOverall:
    """Overall favorability metrics."""
    favorable: float
    unfavorable: float
    neutral: float
    netFavorability: float
    sampleSize: int
    sourceCount: int
    lastUpdated: str
    dateRange: str


@dataclass
class TrendPoint:
    """Trend data point."""
    date: str
    value: float


@dataclass
class PlatformFavorability:
    """Platform-level favorability breakdown."""
    group: str
    favorable: int
    unfavorable: int
    neutral: int


@dataclass
class PollingSocialComparison:
    """
    Comparison between polling and online sentiment data.
    
    Designed for side-by-side horizontal bar graph display in UI.
    Each dataset is independent - no precomputed comparison metric.
    """
    # Online sentiment from our analysis
    onlineSentiment: Dict[str, float]
    # External polling reference data
    pollingData: Dict[str, Any]  # Includes source, date, favorable/unfavorable/neutral


@dataclass
class GOPFavorabilityResult:
    """Complete GOP favorability response."""
    overall: FavorabilityOverall
    trend: List[TrendPoint]
    trendAnnotations: List[Dict[str, str]]
    byPlatform: List[PlatformFavorability]
    pollingVsSocial: PollingSocialComparison

    def to_dict(self) -> Dict[str, Any]:
        return {
            "overall": {
                "favorable": self.overall.favorable,
                "unfavorable": self.overall.unfavorable,
                "neutral": self.overall.neutral,
                "netFavorability": self.overall.netFavorability,
                "sampleSize": self.overall.sampleSize,
                "sourceCount": self.overall.sourceCount,
                "lastUpdated": self.overall.lastUpdated,
                "dateRange": self.overall.dateRange,
            },
            "trend": [{"date": t.date, "value": t.value} for t in self.trend],
            "trendAnnotations": self.trendAnnotations,
            "byPlatform": [
                {"group": p.group, "favorable": p.favorable, "unfavorable": p.unfavorable, "neutral": p.neutral}
                for p in self.byPlatform
            ],
            "pollingVsSocial": {
                "onlineSentiment": self.pollingVsSocial.onlineSentiment,
                "pollingData": self.pollingVsSocial.pollingData,
            },
        }


# =============================================================================
# Movers (window-over-window deltas)
# =============================================================================

@dataclass
class EntityToneMover:
    """One entity whose political-tone net score shifted meaningfully between
    the previous equivalent window and the current one.

    ``delta_pts`` is signed (current_net - prev_net). Positive = more
    positive tone this window. ``min_volume`` enforced upstream so a
    low-volume swing from 1 → 2 docs doesn't rank."""
    key: str
    kind: str                    # outlet | official | subreddit
    displayName: str
    current_net: float
    prev_net: float
    delta_pts: float             # signed: positive = climbing, negative = falling
    current_volume: int
    prev_volume: int
    entity_profile: Dict[str, Any] = field(default_factory=dict)


@dataclass
class FavorabilityMover:
    """Window-over-window shift in overall GOP favorability. Shipped as a
    single row alongside the entity-tone movers so the ticker can foreground
    the party-stance trend (not just per-entity moves)."""
    label: str                   # "GOP party stance"
    current_net: float
    prev_net: float
    delta_pts: float
    current_volume: int
    prev_volume: int


@dataclass
class MoversResult:
    """Top N biggest movers (up and down) for the political-tone +
    GOP-favorability ticker. Window-over-window shape — previous window is
    the same duration ending at the current window start."""
    window: str                       # '24h' | '7d' | '30d' | '90d'
    entity_movers: List[EntityToneMover] = field(default_factory=list)
    favorability_mover: Optional[FavorabilityMover] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "window": self.window,
            "entity_movers": [asdict(m) for m in self.entity_movers],
            "favorability_mover": asdict(self.favorability_mover) if self.favorability_mover else None,
        }


# =============================================================================
# Bot Activity
# =============================================================================

@dataclass
class BotEntityItem:
    """Per-entity bot-amplification rollup for the three-way Bot Detector
    grid. Sort the UI list by ``bot_rate_pct`` desc; tie-break by
    ``bot_docs`` desc; catch-alls sorted last."""
    key: str                    # registry key or catch-all sentinel
    kind: str                   # outlet | official | subreddit | catch_all
    total_docs: int             # bot_detection-scored docs attributed to this entity
    bot_docs: int               # subset flagged as label='bot'
    bot_rate_pct: float         # bot_docs / total_docs * 100
    entity_profile: Dict[str, Any] = field(default_factory=dict)


@dataclass
class BotOverview:
    """Bot activity overview metrics."""
    suspectedAutomationRate: float
    coordinationIndex: float
    topClusters: List[str]
    totalFlaggedPosts: int
    confidence: str
    # Three-way entity rollups — which outlets / officials / subreddits have
    # the highest bot-classification rate on their contributed posts. Empty
    # on older snapshots.
    by_news_outlet: List[BotEntityItem] = field(default_factory=list)
    by_official: List[BotEntityItem] = field(default_factory=list)
    by_general_public: List[BotEntityItem] = field(default_factory=list)


@dataclass
class FlaggedExample:
    """One bot-flagged post displayed as evidence on the Bot Detector.
    Every instance must carry a URL back to the original when one can be
    synthesized — invariant C1 (source links on evidence)."""
    doc_id: int
    text: str
    source_label: str          # "News · foo.com" / "X · @handle" / "Reddit · r/politics"
    url: Optional[str]         # null when ingest metadata wasn't enough to synthesize


@dataclass
class NarrativeAmplification:
    """Narrative being amplified by suspected bots."""
    id: int
    narrative: str
    confidence: str
    examplePosts: List[FlaggedExample]
    topHashtags: List[str]
    topPhrases: List[str]
    targets: List[str]
    suspectedBotVolume: int
    whyFlagged: List[str]


@dataclass
class CoordinationStats:
    """Coordination behavior statistics."""
    accountReuse: float
    identicalTextPairs: int
    avgPostsPerSuspectedAccount: float


@dataclass
class BehavioralSignals:
    """Behavioral signal breakdowns.

    The former ``postingCadence`` heatmap field was dropped: the aggregator
    only ever emitted ``day: 0`` for every row and bucketed by server-local
    hour, so the heatmap it fed was fabricated. The UI viz was removed first;
    this drops the dead payload (audit HEATMAP / ui-rework Phase B).
    """
    accountAgeDistribution: List[Dict[str, Any]]
    copyPasteSimilarity: Dict[str, float]
    linkDomainConcentration: List[Dict[str, Any]]


@dataclass
class NarrativeSummary:
    """Summary of a single narrative for the Narratives list view.

    ``first_seen_*`` fields reference the earliest doc WE ingested that
    carried this claim — not where the claim originated in the world.
    ``first_seen_tier`` is only populated for x_post source types and uses
    ``account_profiles`` (curated list + LLM classifier). Defaults to
    'general_public' when the X author isn't classified; None for news and
    reddit source types.

    ``first_seen_author`` is a faction-context sub-object for x_post
    first-seen docs: handle, full_name, party, branch, chamber,
    state_or_district, office_title, account_type. Lets the UI render
    "Rep Adams (D, NC-12)"-style labels. None for news/reddit.

    ``propaganda_score`` (walkthrough 043) is the mean propaganda density
    across supporting docs that have been through propaganda detection. None
    when no supporting doc has a propaganda row yet.

    ``bot_pushed_fraction`` (walkthrough 043) is the fraction of the
    narrative's unique X supporting authors whose ``author_bot_scores.score
    >= 0.5`` or who have any bot-labeled posts. None when no supporting doc
    is an X post with a resolved author. Together with ``propaganda_score``
    it lets the UI show "this narrative is heavily bot-pushed AND heavy on
    propaganda techniques."
    """
    narrative_id: int
    name: str
    first_seen_at: int
    first_seen_doc_id: Optional[int]
    first_seen_source_type: Optional[str]
    first_seen_domain: Optional[str]
    first_seen_tier: Optional[str]  # 'elected_official' | 'affiliated' | 'general_public' | None
    first_seen_author: Optional[Dict[str, Any]]
    supporting_doc_count: int
    source_breakdown: List[Dict[str, Any]]
    timeline: List[Dict[str, Any]]
    net_sentiment: float
    inbound_citation_count: int
    propaganda_score: Optional[float] = None
    bot_pushed_fraction: Optional[float] = None
    # Walkthrough 058 — three-way entity framing:
    #   first_seen_entity_profile: profile_dict()-shaped dict for the
    #     registry entity the first-seen doc maps to (outlet / official /
    #     subreddit / catch_all). None when first_seen_doc_id is null.
    #   first_seen_tier_group: coarse tier bucket ('news' | 'officials' |
    #     'public') driven by entity_registry.resolve_entity, independent
    #     of the existing first_seen_tier which comes from account_profiles.
    #   cross_tier: True when supporting docs span >1 tier group.
    first_seen_entity_profile: Optional[Dict[str, Any]] = None
    first_seen_tier_group: Optional[str] = None
    cross_tier: bool = False
    # Top N supporting docs for the modal drill-down table — matches the
    # SupportingDoc TS interface in ui/src/types.ts.
    top_supporting_docs: List[Dict[str, Any]] = field(default_factory=list)
    # Mean sentiment-row confidence across ALL supporting docs in the window
    # (not just top_supporting_docs, which would misrepresent the cluster) —
    # lets the UI show a narrative-level confidence chip (audit R-3). None when
    # no supporting doc has a sentiment row.
    mean_confidence: Optional[float] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "narrative_id": self.narrative_id,
            "name": self.name,
            "first_seen_at": self.first_seen_at,
            "first_seen_doc_id": self.first_seen_doc_id,
            "first_seen_source_type": self.first_seen_source_type,
            "first_seen_domain": self.first_seen_domain,
            "first_seen_tier": self.first_seen_tier,
            "first_seen_author": self.first_seen_author,
            "supporting_doc_count": self.supporting_doc_count,
            "source_breakdown": self.source_breakdown,
            "timeline": self.timeline,
            "net_sentiment": self.net_sentiment,
            "inbound_citation_count": self.inbound_citation_count,
            "propaganda_score": self.propaganda_score,
            "bot_pushed_fraction": self.bot_pushed_fraction,
            "first_seen_entity_profile": self.first_seen_entity_profile,
            "first_seen_tier_group": self.first_seen_tier_group,
            "cross_tier": self.cross_tier,
            "top_supporting_docs": self.top_supporting_docs,
            "mean_confidence": self.mean_confidence,
        }


@dataclass
class BotActivityData:
    """Complete bot activity response."""
    overview: BotOverview
    narrativeAmplification: List[NarrativeAmplification]
    coordinationStats: CoordinationStats
    behavioralSignals: BehavioralSignals

    def to_dict(self) -> Dict[str, Any]:
        return {
            "overview": {
                "suspectedAutomationRate": self.overview.suspectedAutomationRate,
                "coordinationIndex": self.overview.coordinationIndex,
                "topClusters": self.overview.topClusters,
                "totalFlaggedPosts": self.overview.totalFlaggedPosts,
                "confidence": self.overview.confidence,
                "by_news_outlet": [asdict(e) for e in self.overview.by_news_outlet],
                "by_official": [asdict(e) for e in self.overview.by_official],
                "by_general_public": [asdict(e) for e in self.overview.by_general_public],
            },
            "narrativeAmplification": [
                {
                    "id": n.id,
                    "narrative": n.narrative,
                    "confidence": n.confidence,
                    "examplePosts": [asdict(ex) for ex in n.examplePosts],
                    "topHashtags": n.topHashtags,
                    "topPhrases": n.topPhrases,
                    "targets": n.targets,
                    "suspectedBotVolume": n.suspectedBotVolume,
                    "whyFlagged": n.whyFlagged,
                }
                for n in self.narrativeAmplification
            ],
            "coordinationStats": {
                "accountReuse": self.coordinationStats.accountReuse,
                "identicalTextPairs": self.coordinationStats.identicalTextPairs,
                "avgPostsPerSuspectedAccount": self.coordinationStats.avgPostsPerSuspectedAccount,
            },
            "behavioralSignals": {
                "accountAgeDistribution": self.behavioralSignals.accountAgeDistribution,
                "copyPasteSimilarity": self.behavioralSignals.copyPasteSimilarity,
                "linkDomainConcentration": self.behavioralSignals.linkDomainConcentration,
            },
        }
