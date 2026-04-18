"""
Aggregators module.

Re-exports all domain aggregators for external use.
"""

from analysis.src.reporting.aggregators.outlet import OutletAggregator
from analysis.src.reporting.aggregators.sentiment import SentimentAggregator
from analysis.src.reporting.aggregators.bot import BotAggregator
from analysis.src.reporting.aggregators.geo import GeoAggregator

__all__ = [
    "OutletAggregator",
    "SentimentAggregator",
    "BotAggregator",
    "GeoAggregator",
]
