"""
Reporting models package.

Provides dataclasses for structured API responses.
"""

from analysis.src.reporting.models.aggregator_models import (
    OutletProfile,
    SentimentOverview,
    SentimentDistribution,
    PlatformSentiment,
    TopicSentiment,
    ClassificationSample,
    TimeWindowSentiment,
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
    NarrativeSummary,
)

__all__ = [
    "OutletProfile",
    "SentimentOverview",
    "SentimentDistribution",
    "PlatformSentiment",
    "TopicSentiment",
    "ClassificationSample",
    "TimeWindowSentiment",
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
    "NarrativeSummary",
]
