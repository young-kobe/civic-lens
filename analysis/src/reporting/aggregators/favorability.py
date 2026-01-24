"""
GOP favorability aggregator.

Aggregates GOP favorability metrics with live polling data integration.
"""

import datetime
import json
from typing import Any, Dict, List, Optional, Set

from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
    get_bot_flagged_doc_ids,
)
from analysis.src.reporting.models import (
    FavorabilityOverall,
    TrendPoint,
    PlatformFavorability,
    PollingSocialComparison,
    GOPFavorabilityResult,
)


class FavorabilityAggregator:
    """Aggregates GOP favorability metrics with polling comparison."""
    
    def __init__(self, db_path: str):
        self.db_path = db_path
        settings = get_settings()
        self.cache = SnapshotCache(settings.cache_dir)
    
    def get_gop_favorability(self, time_window: str = "24h") -> GOPFavorabilityResult:
        """
        Aggregate GOP favorability EXCLUDING bot-flagged content.
        Includes trend data and platform breakdown.
        
        Args:
            time_window: Filter by time window (24h, 7d, 30d, 90d, all)
        """
        bot_docs = get_bot_flagged_doc_ids(self.db_path)
        cutoff = get_time_cutoff(time_window)
        
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            
            if cutoff:
                cursor.execute("""
                    SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at
                    FROM ai_outputs a
                    JOIN docs d ON a.doc_id = d.doc_id
                    WHERE a.task_type = 'favorability' AND d.published_at >= ?
                """, (cutoff,))
            else:
                cursor.execute("""
                    SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at
                    FROM ai_outputs a
                    JOIN docs d ON a.doc_id = d.doc_id
                    WHERE a.task_type = 'favorability'
                """)
            
            rows = cursor.fetchall()
        
        return self._process_favorability_data(rows, bot_docs)

    def _process_favorability_data(self, rows: List[tuple], bot_docs: Set[int]) -> GOPFavorabilityResult:
        """Process favorability data into structured response."""
        distribution = {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0}
        by_platform: Dict[str, Dict[str, int]] = {}
        daily_net: Dict[str, Dict[str, Any]] = {}
        count = 0
        excluded_bot_count = 0
        
        for doc_id, output_json, confidence, source_type, pub_at in rows:
            if doc_id in bot_docs:
                excluded_bot_count += 1
                continue
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            stance = data.get('overall_gop_stance', 'neutral')
            if stance not in distribution:
                stance = 'neutral'
            distribution[stance] += 1
            
            # Platform breakdown (normalize reddit types)
            platform = self._normalize_platform(source_type)
            if platform not in by_platform:
                by_platform[platform] = {"favorable": 0, "unfavorable": 0, "neutral": 0}
            if stance in by_platform[platform]:
                by_platform[platform][stance] += 1
            
            # Track daily trend
            if pub_at:
                self._update_daily_trend(daily_net, pub_at, stance)
            
            count += 1
        
        # Compute metrics
        total = sum(distribution.values())
        net_favorability = ((distribution["favorable"] - distribution["unfavorable"]) / total * 100) if total > 0 else 0
        
        # Format results
        trend_data = self._format_trend_data(daily_net)
        platform_data = self._format_platform_favorability(by_platform)
        
        # Get live polling data from cache
        polling_data = self._get_cached_polling_data()
        
        return GOPFavorabilityResult(
            overall=FavorabilityOverall(
                favorable=round((distribution["favorable"] / total) * 100, 1) if total else 0,
                unfavorable=round((distribution["unfavorable"] / total) * 100, 1) if total else 0,
                neutral=round((distribution["neutral"] / total) * 100, 1) if total else 0,
                netFavorability=round(net_favorability, 1),
                sampleSize=count,
                sourceCount=len(by_platform),
                lastUpdated=datetime.datetime.now().isoformat(),
                dateRange="All time",
            ),
            trend=trend_data[-30:],
            trendAnnotations=[],
            byPlatform=platform_data,
            pollingVsSocial=PollingSocialComparison(
                onlineSentiment={
                    "favorable": round((distribution["favorable"] / total) * 100, 1) if total else 0,
                    "unfavorable": round((distribution["unfavorable"] / total) * 100, 1) if total else 0,
                    "neutral": round((distribution["neutral"] / total) * 100, 1) if total else 0,
                },
                pollingData=polling_data,
            ),
        )

    def _get_cached_polling_data(self) -> Optional[Dict[str, Any]]:
        """
        Get live polling data from cache.
        
        Returns None if polling data is not available.
        """
        # cache.load() already returns the 'data' portion of the cache file
        polling_data = self.cache.load("polling_gop")
        if polling_data:
            return polling_data
        return None

    def _normalize_platform(self, source_type: Optional[str]) -> str:
        """Normalize platform names."""
        platform = source_type or 'unknown'
        if platform in ('reddit_post', 'reddit_comment'):
            return 'reddit'
        return platform

    def _update_daily_trend(self, daily_net: Dict[str, Dict[str, Any]], pub_at: int, stance: str) -> None:
        """Update daily trend accumulator."""
        date_str = datetime.datetime.fromtimestamp(pub_at).strftime('%Y-%m-%d')
        if date_str not in daily_net:
            daily_net[date_str] = {"score": 0, "count": 0}
        
        val = 1 if stance == 'favorable' else (-1 if stance == 'unfavorable' else 0)
        daily_net[date_str]["score"] += val
        daily_net[date_str]["count"] += 1

    def _format_trend_data(self, daily_net: Dict[str, Dict[str, Any]]) -> List[TrendPoint]:
        """Format daily trend data."""
        return [
            TrendPoint(
                date=date,
                value=round((stats["score"] / stats["count"]) * 100, 1) if stats["count"] > 0 else 0,
            )
            for date, stats in sorted(daily_net.items())
        ]

    def _format_platform_favorability(self, by_platform: Dict[str, Dict[str, int]]) -> List[PlatformFavorability]:
        """Format platform favorability breakdown."""
        return [
            PlatformFavorability(
                group=platform.capitalize(),
                favorable=stats["favorable"],
                unfavorable=stats["unfavorable"],
                neutral=stats["neutral"],
            )
            for platform, stats in by_platform.items()
        ]
