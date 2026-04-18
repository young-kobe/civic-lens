"""
Geographic sentiment aggregator.

Aggregates X posts by country_code with sentiment for the global heatmap.
Uses explicit country_code from X API metadata - no heuristics.
"""

import json
from typing import Dict, List, Any, Set
from collections import defaultdict

from analysis.src.common.logger import get_logger
from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
    get_bot_flagged_doc_ids,
)
from analysis.src.reporting.aggregators.constants import COUNTRY_NAMES

logger = get_logger(__name__)


# Sentiment label → numeric score, multiplied by per-sample confidence to produce
# a value in [-1, 1] that the UI maps to a color on the heatmap.
_LABEL_SCORE = {
    "POSITIVE": 1.0,
    "NEGATIVE": -1.0,
    "NEUTRAL": 0.0,
    "MIXED": 0.0,
}


class GeoAggregator:
    """Aggregates X posts by country for global heatmap visualization."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_country_sentiment(self, time_window: str = "7d") -> Dict[str, Any]:
        """Aggregate X posts by country_code with confidence-weighted sentiment.

        Uses the ``docs.place_country_code`` column (populated at ingest time
        from X's ``places`` expansion) rather than re-parsing metadata_json
        per row.
        """
        cutoff = get_time_cutoff(time_window)
        bot_docs = get_bot_flagged_doc_ids(self.db_path)

        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()

            query = """
                SELECT d.doc_id, d.place_country_code, a.output_json, a.confidence
                FROM docs d
                LEFT JOIN ai_outputs a ON d.doc_id = a.doc_id AND a.task_type = 'sentiment'
                WHERE d.source_type = 'x_post'
                  AND d.place_country_code IS NOT NULL
            """
            params: list = []

            if cutoff:
                query += " AND d.published_at >= ?"
                params.append(cutoff)

            cursor.execute(query, params)
            rows = cursor.fetchall()

            return self._process_geo_data(rows, bot_docs)

    def _process_geo_data(
        self,
        rows: List[tuple],
        bot_docs: Set[int],
    ) -> Dict[str, Any]:
        """Process rows into country-level aggregations."""
        by_country: Dict[str, Dict[str, Any]] = defaultdict(lambda: {
            "post_count": 0,
            "sentiment_sum": 0.0,
            "sentiment_count": 0,
        })

        total_posts = 0
        posts_with_geo = 0
        excluded_bots = 0

        for doc_id, country_code, sentiment_json, sentiment_conf in rows:
            total_posts += 1

            if doc_id in bot_docs:
                excluded_bots += 1
                continue

            if not country_code:
                continue

            posts_with_geo += 1
            code = country_code.upper()
            stats = by_country[code]
            stats["post_count"] += 1

            # Convert sentiment label to a confidence-weighted score in [-1, 1].
            # This matches the UI's expectation (color thresholds at +/-0.1, +/-0.3).
            if sentiment_json:
                try:
                    parsed = json.loads(sentiment_json)
                except json.JSONDecodeError:
                    continue
                label = parsed.get("label")
                if label in _LABEL_SCORE:
                    conf = sentiment_conf if sentiment_conf is not None else 1.0
                    stats["sentiment_sum"] += _LABEL_SCORE[label] * conf
                    stats["sentiment_count"] += 1

        countries = []
        for code, stats in by_country.items():
            avg_sentiment = (
                stats["sentiment_sum"] / stats["sentiment_count"]
                if stats["sentiment_count"] > 0 else 0.0
            )
            countries.append({
                "country_code": code,
                "country_name": COUNTRY_NAMES.get(code, code),
                "post_count": stats["post_count"],
                "avg_sentiment": round(avg_sentiment, 3),
            })

        countries.sort(key=lambda x: x["post_count"], reverse=True)

        return {
            "countries": countries,
            "total_posts": total_posts,
            "posts_with_geo": posts_with_geo,
            "geo_coverage_pct": round(posts_with_geo / total_posts * 100, 1) if total_posts > 0 else 0,
            "excluded_bots": excluded_bots,
            "country_count": len(countries),
        }
