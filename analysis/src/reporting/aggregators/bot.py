"""
Bot activity aggregator.

Aggregates bot detection metrics, coordination stats, and behavioral signals.
"""

import datetime
import json
from typing import Any, Dict, List

from analysis.src.reporting.aggregators.base import get_connection
from analysis.src.reporting.models import (
    BotOverview,
    NarrativeAmplification,
    CoordinationStats,
    BehavioralSignals,
    BotActivityData,
)


class BotAggregator:
    """Aggregates bot activity metrics and behavioral signals."""
    
    def __init__(self, db_path: str):
        self.db_path = db_path
    
    def get_bot_activity(self) -> BotActivityData:
        """
        Aggregate bot activity metrics.
        Provides overview, narrative amplification, coordination stats, and behavioral signals.
        """
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            bot_data = self._fetch_bot_detection_data(cursor)
        
        return self._format_bot_activity(bot_data)

    def _fetch_bot_detection_data(self, cursor) -> Dict[str, Any]:
        """Fetch bot detection data from database."""
        cursor.execute("""
            SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.domain_or_subreddit, d.published_at
            FROM ai_outputs a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type = 'bot_detection'
        """)
        
        total_scanned = 0
        bot_count = 0
        suspicious_count = 0
        by_cluster: Dict[str, int] = {}
        indicators_frequency: Dict[str, int] = {}
        hourly_distribution: Dict[int, int] = {}
        
        bot_docs_data = []
        
        for doc_id, output_json, confidence, source_type, domain, pub_at in cursor.fetchall():
            total_scanned += 1
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            label = data.get('label', 'human')
            
            if label == 'bot':
                bot_count += 1
                bot_docs_data.append({
                    "doc_id": doc_id,
                    "data": data,
                    "domain": domain,
                    "pub_at": pub_at,
                })
                
                # Track cluster/domain
                by_cluster[domain] = by_cluster.get(domain, 0) + 1
                
                # Track indicators
                for indicator in data.get('indicators', []):
                    indicators_frequency[indicator] = indicators_frequency.get(indicator, 0) + 1
                
                # Track hourly posting
                if pub_at:
                    hour = datetime.datetime.fromtimestamp(pub_at).hour
                    hourly_distribution[hour] = hourly_distribution.get(hour, 0) + 1
                    
            elif label == 'suspicious':
                suspicious_count += 1
        
        return {
            "total_scanned": total_scanned,
            "bot_count": bot_count,
            "suspicious_count": suspicious_count,
            "by_cluster": by_cluster,
            "indicators_frequency": indicators_frequency,
            "hourly_distribution": hourly_distribution,
            "bot_docs_data": bot_docs_data,
        }

    def _format_bot_activity(self, bot_data: Dict[str, Any]) -> BotActivityData:
        """Format bot detection data into BotActivityData response."""
        total_scanned = bot_data["total_scanned"]
        bot_count = bot_data["bot_count"]
        suspicious_count = bot_data["suspicious_count"]
        by_cluster = bot_data["by_cluster"]
        indicators_frequency = bot_data["indicators_frequency"]
        hourly_distribution = bot_data["hourly_distribution"]
        
        # Compute automation rate
        automation_rate = (bot_count / total_scanned * 100) if total_scanned > 0 else 0
        
        # Top clusters (domains/subreddits with most bot activity)
        top_clusters = sorted(by_cluster.items(), key=lambda x: x[1], reverse=True)[:5]
        
        # Compute coordination index (simplified: based on timing patterns)
        coordination_index = self._compute_coordination_index(hourly_distribution)
        
        # Top indicators as narrative amplification proxies
        narratives = self._compute_narrative_amplification(indicators_frequency, bot_data["bot_docs_data"])
        
        # Build behavioral signals
        posting_cadence = [
            {"day": 0, "hour": hour, "value": count}
            for hour, count in sorted(hourly_distribution.items())
        ]
        
        return BotActivityData(
            overview=BotOverview(
                suspectedAutomationRate=round(automation_rate, 1),
                coordinationIndex=round(coordination_index, 2),
                topClusters=[c[0] for c in top_clusters],
                totalFlaggedAccounts=bot_count + suspicious_count,
                confidence="medium" if total_scanned > 100 else "low",
            ),
            narrativeAmplification=narratives,
            coordinationStats=CoordinationStats(
                burstTimingSimilarity=round(coordination_index, 2),
                accountReuse=0.0,
                identicalTextPairs=0,
                avgPostsPerSuspectedAccount=1.0,
            ),
            behavioralSignals=BehavioralSignals(
                accountAgeDistribution=[],
                postingCadence=posting_cadence,
                copyPasteSimilarity={"high": 0, "medium": 0, "low": 0},
                linkDomainConcentration=[
                    {"domain": c[0], "percentage": round(c[1] / bot_count * 100, 1) if bot_count > 0 else 0}
                    for c in top_clusters[:5]
                ],
            ),
        )

    def _compute_coordination_index(self, hourly_distribution: Dict[int, int]) -> float:
        """Compute coordination index based on posting time concentration."""
        if not hourly_distribution:
            return 0.0
        
        total = sum(hourly_distribution.values())
        if total == 0:
            return 0.0
        
        # Higher concentration in fewer hours = higher coordination
        max_hour_count = max(hourly_distribution.values())
        return max_hour_count / total

    def _compute_narrative_amplification(
        self, 
        indicators_frequency: Dict[str, int], 
        bot_docs_data: List[Dict[str, Any]]
    ) -> List[NarrativeAmplification]:
        """Compute narrative amplification from bot indicators."""
        if not indicators_frequency:
            return []
        
        # Group by top indicators
        top_indicators = sorted(indicators_frequency.items(), key=lambda x: x[1], reverse=True)[:3]
        
        narratives = []
        for idx, (indicator, count) in enumerate(top_indicators):
            narratives.append(NarrativeAmplification(
                id=idx + 1,
                narrative=indicator,
                confidence="medium" if count > 5 else "low",
                examplePosts=[],  # Would need to fetch actual post content
                topHashtags=[],
                topPhrases=[indicator],
                targets=[],
                suspectedBotVolume=count,
                whyFlagged=[indicator],
            ))
        
        return narratives
