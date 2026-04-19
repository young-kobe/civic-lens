"""
Data models for reporting aggregations.

Contains dataclasses for structured API responses.
"""

import datetime
from dataclasses import dataclass, field
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
    """Topic-level sentiment breakdown with classification samples."""
    topic: str
    positive: int
    negative: int
    neutral: int
    volume: int
    sarcasm_rate: float = 0.0
    classification_samples: List[ClassificationSample] = field(default_factory=list)


@dataclass
class TimeWindowSentiment:
    """Time window sentiment breakdown."""
    window: str
    positive: int
    negative: int
    neutral: int
    volume: int


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
    socialVsNews: Optional[Dict[str, Any]] = None  # Social vs News comparison
    # Merged GOP favorability data
    gopFavorability: Optional[Dict[str, Any]] = None  # Stance breakdown (favorable/unfavorable/neutral %)
    gopTrend: Optional[List[Dict[str, Any]]] = None  # Daily net favorability trend
    gopByPlatform: Optional[List[Dict[str, Any]]] = None  # Platform-level stance breakdown
    pollingVsSocial: Optional[Dict[str, Any]] = None  # Live polling comparison

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
            "byTimeWindow": [
                {"window": w.window, "positive": w.positive, "negative": w.negative, "neutral": w.neutral, "volume": w.volume}
                for w in self.byTimeWindow
            ],
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
# Bot Activity
# =============================================================================

@dataclass
class BotOverview:
    """Bot activity overview metrics."""
    suspectedAutomationRate: float
    coordinationIndex: float
    topClusters: List[str]
    totalFlaggedAccounts: int
    confidence: str


@dataclass
class NarrativeAmplification:
    """Narrative being amplified by suspected bots."""
    id: int
    narrative: str
    confidence: str
    examplePosts: List[str]
    topHashtags: List[str]
    topPhrases: List[str]
    targets: List[str]
    suspectedBotVolume: int
    whyFlagged: List[str]


@dataclass
class CoordinationStats:
    """Coordination behavior statistics."""
    burstTimingSimilarity: float
    accountReuse: float
    identicalTextPairs: int
    avgPostsPerSuspectedAccount: float


@dataclass
class BehavioralSignals:
    """Behavioral signal breakdowns."""
    accountAgeDistribution: List[Dict[str, Any]]
    postingCadence: List[Dict[str, Any]]
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
                "totalFlaggedAccounts": self.overview.totalFlaggedAccounts,
                "confidence": self.overview.confidence,
            },
            "narrativeAmplification": [
                {
                    "id": n.id,
                    "narrative": n.narrative,
                    "confidence": n.confidence,
                    "examplePosts": n.examplePosts,
                    "topHashtags": n.topHashtags,
                    "topPhrases": n.topPhrases,
                    "targets": n.targets,
                    "suspectedBotVolume": n.suspectedBotVolume,
                    "whyFlagged": n.whyFlagged,
                }
                for n in self.narrativeAmplification
            ],
            "coordinationStats": {
                "burstTimingSimilarity": self.coordinationStats.burstTimingSimilarity,
                "accountReuse": self.coordinationStats.accountReuse,
                "identicalTextPairs": self.coordinationStats.identicalTextPairs,
                "avgPostsPerSuspectedAccount": self.coordinationStats.avgPostsPerSuspectedAccount,
            },
            "behavioralSignals": {
                "accountAgeDistribution": self.behavioralSignals.accountAgeDistribution,
                "postingCadence": self.behavioralSignals.postingCadence,
                "copyPasteSimilarity": self.behavioralSignals.copyPasteSimilarity,
                "linkDomainConcentration": self.behavioralSignals.linkDomainConcentration,
            },
        }
