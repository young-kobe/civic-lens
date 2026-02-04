"""
Aggregators module.

Re-exports all domain aggregators for external use.
"""

from analysis.src.reporting.aggregators.outlet import OutletAggregator
from analysis.src.reporting.aggregators.story import StoryAggregator
from analysis.src.reporting.aggregators.sentiment import SentimentAggregator
from analysis.src.reporting.aggregators.favorability import FavorabilityAggregator
from analysis.src.reporting.aggregators.bot import BotAggregator
from analysis.src.reporting.aggregators.geo import GeoAggregator

__all__ = [
    "OutletAggregator",
    "StoryAggregator", 
    "SentimentAggregator",
    "FavorabilityAggregator",
    "BotAggregator",
    "GeoAggregator",
]


# Legacy Aggregator class for backwards compatibility during transition
# TODO: Remove once all callers are updated to use domain-specific aggregators
class Aggregator:
    """
    Legacy aggregator that delegates to domain-specific aggregators.
    
    Deprecated: Use domain-specific aggregators directly.
    """
    
    def __init__(self, db_path: str):
        self.db_path = db_path
        self._outlet = OutletAggregator(db_path)
        self._story = StoryAggregator(db_path)
        self._sentiment = SentimentAggregator(db_path)
        self._favorability = FavorabilityAggregator(db_path)
        self._bot = BotAggregator(db_path)
    
    def get_outlet_profiles(self):
        return self._outlet.get_outlet_profiles()
    
    def get_stories(self, time_window: str = "24h"):
        return self._story.get_stories(time_window)
    
    def get_public_sentiment(self, time_window: str = "24h"):
        return self._sentiment.get_public_sentiment(time_window)
    
    def get_gop_favorability(self, time_window: str = "24h"):
        return self._favorability.get_gop_favorability(time_window)
    
    def get_bot_activity(self):
        return self._bot.get_bot_activity()
