"""
Public sentiment aggregator.

Aggregates sentiment metrics excluding bot-flagged content.
Provides separate breakdowns for Social Media vs News Outlets.
"""

import json
import re
from collections import Counter
from datetime import datetime, timedelta
from typing import Any, Dict, List, Set, Tuple

from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
    get_bot_flagged_doc_ids,
)
from analysis.src.reporting.models import (
    SentimentOverview,
    SentimentDistribution,
    PlatformSentiment,
    PublicSentimentResult,
    TopicSentiment,
    TimeWindowSentiment,
)

# Platform categorization: map source_type to display category
SOCIAL_PLATFORMS = frozenset(['reddit_post', 'reddit_comment', 'twitter', 'social'])
NEWS_PLATFORMS = frozenset(['news', 'rss'])

# Common political topics for keyword extraction
TOPIC_KEYWORDS = {
    'Immigration': ['immigration', 'border', 'migrant', 'asylum', 'deportation', 'illegal'],
    'Economy': ['economy', 'inflation', 'jobs', 'unemployment', 'tax', 'tariff', 'recession'],
    'Healthcare': ['healthcare', 'obamacare', 'medicare', 'insurance', 'hospital', 'medical'],
    'Climate': ['climate', 'energy', 'green', 'carbon', 'emissions', 'fossil', 'renewable'],
    'Foreign Policy': ['foreign', 'russia', 'china', 'ukraine', 'military', 'nato', 'war', 'troops'],
    'Gun Policy': ['gun', 'firearm', 'second amendment', 'nra', 'shooting'],
    'Abortion': ['abortion', 'roe', 'reproductive', 'pro-life', 'pro-choice'],
    'Education': ['education', 'school', 'student', 'college', 'university', 'teacher'],
    'Justice': ['justice', 'supreme court', 'judges', 'crime', 'police', 'prison'],
}


class SentimentAggregator:
    """Aggregates public sentiment metrics with social vs news separation."""
    
    def __init__(self, db_path: str):
        self.db_path = db_path
    
    def _categorize_platform(self, source_type: str) -> str:
        """Categorize source_type into Social Media or News Outlets."""
        if source_type in SOCIAL_PLATFORMS:
            return "Social Media"
        elif source_type in NEWS_PLATFORMS:
            return "News Outlets"
        return "Other"
    
    def _extract_topic(self, title: str) -> str:
        """Extract topic from title using keyword matching."""
        if not title:
            return "General"
        title_lower = title.lower()
        for topic, keywords in TOPIC_KEYWORDS.items():
            if any(kw in title_lower for kw in keywords):
                return topic
        return "General"
    
    def _get_time_bucket(self, published_at: Any, now: datetime) -> str:
        """Get time bucket for a document based on published_at."""
        try:
            # Handle different formats
            if isinstance(published_at, (int, float)):
                # Unix timestamp
                pub_dt = datetime.fromtimestamp(published_at)
            elif isinstance(published_at, str):
                # ISO format string
                pub_dt = datetime.fromisoformat(published_at.replace('Z', '+00:00'))
                if pub_dt.tzinfo:
                    pub_dt = pub_dt.replace(tzinfo=None)
            else:
                return "Unknown"
            
            delta = now - pub_dt
            if delta.days < 1:
                return "24 hours"
            elif delta.days < 7:
                return "7 days"
            elif delta.days < 30:
                return "30 days"
            else:
                return "90+ days"
        except (ValueError, TypeError, OSError):
            return "Unknown"
    
    def get_public_sentiment(self, time_window: str = "24h") -> PublicSentimentResult:
        """
        Aggregate sentiment EXCLUDING bot-flagged content.
        Provides separate breakdowns for Social Media vs News Outlets.
        
        Args:
            time_window: Filter by time window (24h, 7d, 30d, 90d, all)
        """
        bot_docs = get_bot_flagged_doc_ids(self.db_path)
        cutoff = get_time_cutoff(time_window)
        
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            
            # Updated query to include title for topic extraction
            if cutoff:
                cursor.execute("""
                    SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at, d.title
                    FROM ai_outputs a
                    JOIN docs d ON a.doc_id = d.doc_id
                    WHERE a.task_type = 'sentiment' AND d.published_at >= ?
                """, (cutoff,))
            else:
                cursor.execute("""
                    SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at, d.title
                    FROM ai_outputs a
                    JOIN docs d ON a.doc_id = d.doc_id
                    WHERE a.task_type = 'sentiment'
                """)
            
            rows = cursor.fetchall()
        
        return self._process_sentiment_data(rows, bot_docs)

    def _process_sentiment_data(self, rows: List[tuple], bot_docs: Set[int]) -> PublicSentimentResult:
        """Process sentiment data into structured response with social vs news separation."""
        distribution = {"POSITIVE": 0, "NEGATIVE": 0, "NEUTRAL": 0, "MIXED": 0}
        
        # Separate tracking for social vs news
        social_counts = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
        news_counts = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
        
        # Track by display category for platform breakdown
        by_platform: Dict[str, Dict[str, int]] = {}
        
        # Track by topic
        by_topic: Dict[str, Dict[str, int]] = {}
        
        # Track by time window
        by_time_window: Dict[str, Dict[str, int]] = {}
        
        now = datetime.now()
        count = 0
        excluded_bot_count = 0
        
        for doc_id, output_json, confidence, source_type, published_at, title in rows:
            if doc_id in bot_docs:
                excluded_bot_count += 1
                continue
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            label = data.get('label', 'NEUTRAL')
            if label in distribution:
                distribution[label] += 1
            
            # Map label to lowercase key
            key_map = {"POSITIVE": "positive", "NEGATIVE": "negative", "NEUTRAL": "neutral", "MIXED": "mixed"}
            label_key = key_map.get(label, "neutral")
            
            # Categorize into social vs news
            platform = source_type or 'unknown'
            category = self._categorize_platform(platform)
            
            if category == "Social Media":
                social_counts[label_key] += 1
            elif category == "News Outlets":
                news_counts[label_key] += 1
            
            # Track by display category for platform breakdown
            if category not in by_platform:
                by_platform[category] = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
            by_platform[category][label_key] += 1
            
            # Track by topic
            topic = self._extract_topic(title)
            if topic not in by_topic:
                by_topic[topic] = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
            by_topic[topic][label_key] += 1
            
            # Track by time window
            time_bucket = self._get_time_bucket(published_at, now)
            if time_bucket not in by_time_window:
                by_time_window[time_bucket] = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
            by_time_window[time_bucket][label_key] += 1
            
            count += 1
        
        # Calculate overall net score
        total = sum(distribution.values())
        net_score = ((distribution["POSITIVE"] - distribution["NEGATIVE"]) / total * 100) if total > 0 else 0
        
        # Calculate separate net scores for social vs news
        social_total = sum(social_counts.values())
        news_total = sum(news_counts.values())
        
        social_net = ((social_counts["positive"] - social_counts["negative"]) / social_total * 100) if social_total > 0 else 0
        news_net = ((news_counts["positive"] - news_counts["negative"]) / news_total * 100) if news_total > 0 else 0
        
        # Format breakdowns
        platform_breakdown = self._format_platform_sentiment(by_platform)
        topic_breakdown = self._format_topic_sentiment(by_topic)
        time_window_breakdown = self._format_time_window_sentiment(by_time_window)
        
        # Build result with all breakdowns
        result = PublicSentimentResult(
            overview=SentimentOverview(
                netScore=round(net_score, 1),
                volume=count,
                coverage="medium",
                confidence="medium",
            ),
            distribution=SentimentDistribution(
                strongPositive=distribution["POSITIVE"],
                mildPositive=0,
                neutral=distribution["NEUTRAL"],
                mildNegative=0,
                strongNegative=distribution["NEGATIVE"],
            ),
            byPlatform=platform_breakdown,
            byTopic=topic_breakdown,
            byTimeWindow=time_window_breakdown,
            disclaimer="Represents sampled platform discourse, not verified population sentiment",
            excluded_bot_content=excluded_bot_count,
        )
        
        # Add socialVsNews as extra data
        result.socialVsNews = {
            "social": {
                "positive": social_counts["positive"],
                "negative": social_counts["negative"],
                "neutral": social_counts["neutral"],
                "netScore": round(social_net, 1),
                "volume": social_total,
            },
            "news": {
                "positive": news_counts["positive"],
                "negative": news_counts["negative"],
                "neutral": news_counts["neutral"],
                "netScore": round(news_net, 1),
                "volume": news_total,
            },
        }
        
        return result

    def _format_platform_sentiment(self, by_platform: Dict[str, Dict[str, int]]) -> List[PlatformSentiment]:
        """Format platform sentiment breakdown."""
        return [
            PlatformSentiment(
                platform=platform,
                positive=counts["positive"],
                negative=counts["negative"],
                neutral=counts["neutral"],
                volume=sum(counts.values()),
            )
            for platform, counts in by_platform.items()
        ]
    
    def _format_topic_sentiment(self, by_topic: Dict[str, Dict[str, int]]) -> List[TopicSentiment]:
        """Format topic sentiment breakdown, sorted by volume."""
        topics = [
            TopicSentiment(
                topic=topic,
                positive=counts["positive"],
                negative=counts["negative"],
                neutral=counts["neutral"],
                volume=sum(counts.values()),
            )
            for topic, counts in by_topic.items()
        ]
        # Sort by volume, descending
        return sorted(topics, key=lambda t: t.volume, reverse=True)
    
    def _format_time_window_sentiment(self, by_time_window: Dict[str, Dict[str, int]]) -> List[TimeWindowSentiment]:
        """Format time window sentiment breakdown in chronological order."""
        # Define order for time buckets
        order = {"24 hours": 0, "7 days": 1, "30 days": 2, "90+ days": 3, "Unknown": 4}
        
        windows = [
            TimeWindowSentiment(
                window=window,
                positive=counts["positive"],
                negative=counts["negative"],
                neutral=counts["neutral"],
                volume=sum(counts.values()),
            )
            for window, counts in by_time_window.items()
        ]
        return sorted(windows, key=lambda w: order.get(w.window, 999))
