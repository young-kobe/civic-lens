"""
Outlet profile aggregator.

Cross-signal rollup per domain/subreddit: net tone x bot rate side by
side. Unlike every other public aggregate, bot-flagged content is
INCLUDED here on purpose — the point of the surface is transparency
about how much of an outlet's sampled discourse looks automated, so
excluding it would erase the signal being measured. The payload carries
that caveat as a disclaimer.

Wired into the snapshot cache + /outlet-profiles in Phase 2e of the UI
depth overhaul; previously fully implemented but reachable from nothing.
"""

import json
from typing import Any, Dict, List, Optional

from analysis.src.reporting.aggregators.base import get_connection, get_time_cutoff
from analysis.src.reporting.models import OutletProfile

# Domains with fewer scored posts than this are folded out of the payload:
# a 2-post "outlet" row reads as a ranking entry while carrying no signal.
MIN_PROFILE_VOLUME = 5

DISCLAIMER = (
    "Includes bot-flagged content on purpose — the bot rate IS the signal. "
    "Net tone therefore differs from the Overall Tone page, which excludes "
    "flagged posts. Sampled discourse, not a media-bias rating."
)


class OutletAggregator:
    """Aggregates outlet (domain/subreddit) profile metrics."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_outlet_profiles(self, time_window: str = "7d") -> Dict[str, Any]:
        """Per-domain net tone + bot rate for the window.

        Returns the wire shape ``{window, disclaimer, outlets: [...]}`` —
        outlets sorted by bot rate descending.
        """
        cutoff = get_time_cutoff(time_window)
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            sql = """
                SELECT d.domain_or_subreddit, d.source_type, a.task_type, a.output_json
                FROM ai_outputs_latest a
                JOIN docs d ON a.doc_id = d.doc_id
                WHERE a.task_type IN ('sentiment', 'bot_detection')
            """
            params: tuple = ()
            if cutoff is not None:
                sql += " AND d.published_at >= ?"
                params = (cutoff,)
            cursor.execute(sql, params)
            rows = cursor.fetchall()

        profiles = self._aggregate_outlet_data(rows)
        return {
            "window": time_window,
            "disclaimer": DISCLAIMER,
            "outlets": [p.to_dict() for p in self._format_outlet_profiles(profiles)],
        }

    def _aggregate_outlet_data(self, rows: List[tuple]) -> Dict[str, Dict[str, Any]]:
        """Aggregate raw outlet data from DB rows."""
        profiles: Dict[str, Dict[str, Any]] = {}

        for domain, source_type, task, output_json in rows:
            if not domain:
                continue
            if domain not in profiles:
                profiles[domain] = {
                    "source_type": source_type,
                    "positive": 0,
                    "negative": 0,
                    "sentiment_count": 0,
                    "bot_flags": 0,
                    "total_scanned": 0,
                }

            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue

            if task == "sentiment":
                label = data.get("label")
                if label == "POSITIVE":
                    profiles[domain]["positive"] += 1
                elif label == "NEGATIVE":
                    profiles[domain]["negative"] += 1
                profiles[domain]["sentiment_count"] += 1

            elif task == "bot_detection":
                label = data.get("label")
                if label in ["bot", "suspicious"]:
                    profiles[domain]["bot_flags"] += 1
                profiles[domain]["total_scanned"] += 1

        return profiles

    def _format_outlet_profiles(
        self, profiles: Dict[str, Dict[str, Any]],
    ) -> List[OutletProfile]:
        """Format aggregated outlet data into response objects.

        ``net_tone`` is on the same -100..+100 points scale every other
        tone number uses (was a -1..1 mean before the Phase 2e wiring —
        nothing consumed it, so the unit change breaks no one).
        """
        results = []
        for domain, stats in profiles.items():
            if stats["sentiment_count"] < MIN_PROFILE_VOLUME:
                continue
            net_tone: Optional[float] = round(
                (stats["positive"] - stats["negative"])
                / stats["sentiment_count"] * 100, 1,
            )
            bot_rate_pct = round(
                stats["bot_flags"] / stats["total_scanned"] * 100, 1,
            ) if stats["total_scanned"] > 0 else 0.0

            results.append(OutletProfile(
                outlet=domain,
                source_type=stats["source_type"] or "unknown",
                net_tone=net_tone,
                bot_rate_pct=bot_rate_pct,
                volume=stats["sentiment_count"],
                total_scanned=stats["total_scanned"],
            ))

        return sorted(results, key=lambda x: x.bot_rate_pct, reverse=True)
