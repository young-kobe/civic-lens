"""
Reporting models package.

Provides dataclasses for structured API responses.
"""

from analysis.src.reporting.models.aggregator_models import (
    OutletProfile,
    MomentumData,
    SourceMixItem,
    TimelinePoint,
    ArticlePreview,
    StoryCluster,
    SentimentOverview,
    SentimentDistribution,
    PlatformSentiment,
    PublicSentimentResult,
    FavorabilityOverall,
    TrendPoint,
    PlatformFavorability,
    PollingSocialComparison,
    GOPFavorabilityResult,
    BotOverview,
    NarrativeAmplification,
    CoordinationStats,
    BehavioralSignals,
    BotActivityData,
)

__all__ = [
    "OutletProfile",
    "MomentumData",
    "SourceMixItem",
    "TimelinePoint",
    "ArticlePreview",
    "StoryCluster",
    "SentimentOverview",
    "SentimentDistribution",
    "PlatformSentiment",
    "PublicSentimentResult",
    "FavorabilityOverall",
    "TrendPoint",
    "PlatformFavorability",
    "PollingSocialComparison",
    "GOPFavorabilityResult",
    "BotOverview",
    "NarrativeAmplification",
    "CoordinationStats",
    "BehavioralSignals",
    "BotActivityData",
]
