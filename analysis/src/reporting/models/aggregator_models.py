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
# Story Clusters
# =============================================================================

@dataclass
class MomentumData:
    """Momentum metrics for a story cluster."""
    delta24h: float
    delta7d: float = 0.0


@dataclass
class SourceMixItem:
    """Source type distribution item."""
    name: str
    value: int
    type: str


@dataclass
class TimelinePoint:
    """Timeline data point."""
    date: str
    value: int


@dataclass
class ArticlePreview:
    """Preview of an article in a cluster."""
    id: int
    title: str
    source: str
    snippet: str
    reason: str


@dataclass
class StoryCluster:
    """Rich story cluster data for frontend."""
    id: int
    title: str
    articleCount: int
    momentum: MomentumData
    primarySources: List[str]
    summary: List[str]
    keyClaims: List[str]
    entities: Dict[str, List[str]]
    sourceMix: List[SourceMixItem]
    timeline: List[TimelinePoint]
    articles: List[ArticlePreview]

    def to_dict(self) -> Dict[str, Any]:
        return {
            "id": self.id,
            "title": self.title,
            "articleCount": self.articleCount,
            "momentum": {"delta24h": self.momentum.delta24h, "delta7d": self.momentum.delta7d},
            "primarySources": self.primarySources,
            "summary": self.summary,
            "keyClaims": self.keyClaims,
            "entities": self.entities,
            "sourceMix": [{"name": s.name, "value": s.value, "type": s.type} for s in self.sourceMix],
            "timeline": [{"date": t.date, "value": t.value} for t in self.timeline],
            "articles": [{"id": a.id, "title": a.title, "source": a.source, "snippet": a.snippet, "reason": a.reason} for a in self.articles],
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
class PublicSentimentResult:
    """Complete public sentiment response."""
    overview: SentimentOverview
    distribution: SentimentDistribution
    byPlatform: List[PlatformSentiment]
    disclaimer: str
    excluded_bot_content: int

    def to_dict(self) -> Dict[str, Any]:
        return {
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
            "disclaimer": self.disclaimer,
            "excluded_bot_content": self.excluded_bot_content,
        }


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
    """Comparison between polling and social data."""
    polling: Dict[str, float]
    social: Dict[str, float]


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
                "polling": self.pollingVsSocial.polling,
                "social": self.pollingVsSocial.social,
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
