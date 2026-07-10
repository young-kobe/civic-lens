"""
Outlet profile aggregator.

Aggregates sentiment and bot scores by domain/subreddit.
"""

import json
from typing import Any, Dict, List

from analysis.src.reporting.aggregators.base import get_connection
from analysis.src.reporting.models import OutletProfile


class OutletAggregator:
    """Aggregates outlet (domain/subreddit) profile metrics."""
    
    def __init__(self, db_path: str):
        self.db_path = db_path
    
    def get_outlet_profiles(self) -> List[OutletProfile]:
        """
        Aggregates sentiment and bot scores by domain/subreddit.
        Includes ALL content (bots included) for transparency in outlet analysis.
        """
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            
            cursor.execute("""
                SELECT d.domain_or_subreddit, a.task_type, a.output_json
                FROM ai_outputs_latest a
                JOIN docs d ON a.doc_id = d.doc_id
                WHERE a.task_type IN ('sentiment', 'bot_detection')
            """)
            
            rows = cursor.fetchall()
            profiles = self._aggregate_outlet_data(rows)
            return self._format_outlet_profiles(profiles)

    def _aggregate_outlet_data(self, rows: List[tuple]) -> Dict[str, Dict[str, Any]]:
        """Aggregate raw outlet data from DB rows."""
        profiles: Dict[str, Dict[str, Any]] = {}
        
        for domain, task, output_json in rows:
            if domain not in profiles:
                profiles[domain] = {
                    "sentiment_score": 0, 
                    "sentiment_count": 0, 
                    "bot_flags": 0, 
                    "total_scanned": 0
                }
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            if task == 'sentiment':
                label = data.get('label')
                val = 1 if label == 'POSITIVE' else (-1 if label == 'NEGATIVE' else 0)
                profiles[domain]["sentiment_score"] += val
                profiles[domain]["sentiment_count"] += 1
                
            elif task == 'bot_detection':
                label = data.get('label')
                if label in ['bot', 'suspicious']:
                    profiles[domain]["bot_flags"] += 1
                profiles[domain]["total_scanned"] += 1
        
        return profiles

    def _format_outlet_profiles(self, profiles: Dict[str, Dict[str, Any]]) -> List[OutletProfile]:
        """Format aggregated outlet data into response objects."""
        results = []
        for domain, stats in profiles.items():
            avg_sentiment = stats["sentiment_score"] / stats["sentiment_count"] if stats["sentiment_count"] > 0 else 0
            bot_rate = stats["bot_flags"] / stats["total_scanned"] if stats["total_scanned"] > 0 else 0
            
            profile = OutletProfile(
                outlet=domain,
                avg_sentiment=round(avg_sentiment, 3),
                bot_rate=round(bot_rate, 3),
                volume=stats["sentiment_count"],
                total_scanned=stats["total_scanned"],
            )
            results.append(profile)
            
        return sorted(results, key=lambda x: x.bot_rate, reverse=True)
