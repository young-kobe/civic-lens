"""
Geographic sentiment aggregator.

Aggregates X posts by country_code with sentiment/favorability for global heatmap.
Uses explicit country_code from X API metadata - no heuristics.
"""

import json
from typing import Dict, List, Any, Optional, Set
from collections import defaultdict

from analysis.src.common.logger import get_logger
from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
    get_bot_flagged_doc_ids,
)

logger = get_logger(__name__)


# ISO 3166-1 alpha-2 to country name mapping (common countries)
COUNTRY_NAMES = {
    "US": "United States",
    "GB": "United Kingdom",
    "CA": "Canada",
    "AU": "Australia",
    "DE": "Germany",
    "FR": "France",
    "IN": "India",
    "BR": "Brazil",
    "MX": "Mexico",
    "RU": "Russia",
    "CN": "China",
    "JP": "Japan",
    "KR": "South Korea",
    "IT": "Italy",
    "ES": "Spain",
    "NL": "Netherlands",
    "SE": "Sweden",
    "NO": "Norway",
    "PL": "Poland",
    "UA": "Ukraine",
    "IL": "Israel",
    "SA": "Saudi Arabia",
    "AE": "United Arab Emirates",
    "TR": "Turkey",
    "ZA": "South Africa",
    "NG": "Nigeria",
    "EG": "Egypt",
    "AR": "Argentina",
    "CO": "Colombia",
    "PH": "Philippines",
    "ID": "Indonesia",
    "MY": "Malaysia",
    "SG": "Singapore",
    "TH": "Thailand",
    "VN": "Vietnam",
    "PK": "Pakistan",
    "BD": "Bangladesh",
    "IR": "Iran",
    "IQ": "Iraq",
}


class GeoAggregator:
    """Aggregates X posts by country for global heatmap visualization."""
    
    def __init__(self, db_path: str):
        self.db_path = db_path
    
    def get_country_sentiment(self, time_window: str = "7d") -> Dict[str, Any]:
        """
        Aggregate X posts by country_code with sentiment scores.
        
        Args:
            time_window: Filter by time window (24h, 7d, 30d, 90d, all)
        
        Returns:
            Dict with countries list and metadata for heatmap rendering
        """
        cutoff = get_time_cutoff(time_window)
        bot_docs = get_bot_flagged_doc_ids(self.db_path)
        
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            
            # Build query for X posts with geo data
            query = """
                SELECT d.doc_id, d.metadata_json, a.output_json
                FROM docs d
                LEFT JOIN ai_outputs a ON d.doc_id = a.doc_id AND a.task_type = 'sentiment'
                WHERE d.source_type = 'x_post'
                AND d.metadata_json IS NOT NULL
            """
            params = []
            
            if cutoff:
                query += " AND d.published_at >= ?"
                params.append(cutoff)
            
            cursor.execute(query, params)
            rows = cursor.fetchall()
            
            return self._process_geo_data(rows, bot_docs)
    
    def _process_geo_data(
        self, 
        rows: List[tuple], 
        bot_docs: Set[int]
    ) -> Dict[str, Any]:
        """Process rows into country-level aggregations."""
        # Aggregate by country
        by_country: Dict[str, Dict[str, Any]] = defaultdict(lambda: {
            "post_count": 0,
            "sentiment_sum": 0.0,
            "sentiment_count": 0,
            "favorability_sum": 0.0,
            "favorability_count": 0,
        })
        
        total_posts = 0
        posts_with_geo = 0
        excluded_bots = 0
        
        for doc_id, metadata_json, sentiment_json in rows:
            total_posts += 1
            
            # Skip bot-flagged content
            if doc_id in bot_docs:
                excluded_bots += 1
                continue
            
            # Parse metadata for country_code
            try:
                metadata = json.loads(metadata_json) if metadata_json else {}
            except json.JSONDecodeError:
                continue
            
            country_code = metadata.get("place_country_code")
            if not country_code:
                continue
            
            posts_with_geo += 1
            country_code = country_code.upper()
            stats = by_country[country_code]
            stats["post_count"] += 1
            
            # Parse sentiment if available
            if sentiment_json:
                try:
                    sentiment = json.loads(sentiment_json)
                    if "sentiment_score" in sentiment:
                        stats["sentiment_sum"] += sentiment["sentiment_score"]
                        stats["sentiment_count"] += 1
                    if "gop_favorability" in sentiment:
                        stats["favorability_sum"] += sentiment["gop_favorability"]
                        stats["favorability_count"] += 1
                except json.JSONDecodeError:
                    pass
        
        # Format output
        countries = []
        for code, stats in by_country.items():
            avg_sentiment = (
                stats["sentiment_sum"] / stats["sentiment_count"]
                if stats["sentiment_count"] > 0 else 0.0
            )
            avg_favorability = (
                stats["favorability_sum"] / stats["favorability_count"]
                if stats["favorability_count"] > 0 else 0.0
            )
            
            countries.append({
                "country_code": code,
                "country_name": COUNTRY_NAMES.get(code, code),
                "post_count": stats["post_count"],
                "avg_sentiment": round(avg_sentiment, 3),
                "avg_favorability": round(avg_favorability, 3),
            })
        
        # Sort by post count descending
        countries.sort(key=lambda x: x["post_count"], reverse=True)
        
        return {
            "countries": countries,
            "total_posts": total_posts,
            "posts_with_geo": posts_with_geo,
            "geo_coverage_pct": round(posts_with_geo / total_posts * 100, 1) if total_posts > 0 else 0,
            "excluded_bots": excluded_bots,
            "country_count": len(countries),
        }
