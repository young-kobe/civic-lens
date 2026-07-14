"""
Outlet profile aggregator.

Cross-signal rollup per domain/subreddit: net tone x bot rate side by
side. Unlike every other public aggregate, bot-flagged content is
INCLUDED here on purpose — the point of the surface is transparency
about how much of an outlet's sampled discourse looks automated, so
excluding it would erase the signal being measured. The payload carries
that caveat as a disclaimer.

Each profile also carries a small sample of its scored posts, tagged with
the recurring narrative each belongs to, so the UI can open a domain and
show WHY its net tone reads the way it does — the articles/events driving
the number. Wired into the snapshot cache + /outlet-profiles in Phase 2e.
"""

import json
from typing import Any, Dict, List, Optional

from analysis.src.reporting.aggregators.base import (
    X_AUTHOR_JOIN_SQL,
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.sentiment.samples import (
    _build_sample_dict,
    _insert_capped,
    _sample_dict_to_model,
)
from analysis.src.reporting.models import OutletProfile
from analysis.src.reporting.models.aggregator_models import (
    _classification_sample_to_dict,
)

# Domains with fewer scored posts than this are folded out of the payload:
# a 2-post "outlet" row reads as a ranking entry while carrying no signal.
MIN_PROFILE_VOLUME = 5

# Sample posts kept per outlet for the drill-down modal (highest-confidence).
MAX_OUTLET_SAMPLES = 8

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
        """Per-domain net tone + bot rate for the window, each with a small
        narrative-tagged sample of its posts.

        Returns ``{window, disclaimer, outlets: [...]}`` — outlets sorted by
        bot rate descending.
        """
        cutoff = get_time_cutoff(time_window)
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            narrative_map = self._fetch_doc_narratives(cursor)
            rows = self._fetch_rows(cursor, cutoff)

        profiles = self._aggregate_outlet_data(rows)
        return {
            "window": time_window,
            "disclaimer": DISCLAIMER,
            "outlets": [
                p.to_dict()
                for p in self._format_outlet_profiles(profiles, narrative_map)
            ],
        }

    def _fetch_doc_narratives(self, cursor) -> Dict[int, str]:
        """doc_id -> recurring-narrative name (first cluster wins). Empty when
        the narrative tables haven't been created (fresh/test DBs)."""
        cursor.execute(
            "SELECT name FROM sqlite_master WHERE type='table' AND name='narrative_docs'"
        )
        if not cursor.fetchone():
            return {}
        cursor.execute(
            "SELECT nd.doc_id, n.name FROM narrative_docs nd "
            "JOIN narratives n ON n.narrative_id = nd.narrative_id"
        )
        out: Dict[int, str] = {}
        for doc_id, name in cursor.fetchall():
            if name:
                out.setdefault(doc_id, name)
        return out

    def _fetch_rows(self, cursor, cutoff) -> List[tuple]:
        sql = f"""
            SELECT d.domain_or_subreddit, d.source_type, a.task_type, a.output_json,
                   d.doc_id, d.title, d.text, d.published_at, d.ident,
                   u.username, a.confidence
            FROM ai_outputs_latest a
            JOIN docs d ON a.doc_id = d.doc_id
            {X_AUTHOR_JOIN_SQL}
            WHERE a.task_type IN ('sentiment', 'bot_detection')
        """
        params: tuple = ()
        if cutoff is not None:
            sql += " AND d.published_at >= ?"
            params = (cutoff,)
        cursor.execute(sql, params)
        return cursor.fetchall()

    def _aggregate_outlet_data(self, rows: List[tuple]) -> Dict[str, Dict[str, Any]]:
        """Aggregate raw outlet data + collect per-outlet sample posts."""
        profiles: Dict[str, Dict[str, Any]] = {}

        for (domain, source_type, task, output_json, doc_id, title, text,
             published_at, ident, username, confidence) in rows:
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
                    "samples": [],
                }

            try:
                data = json.loads(output_json)
            except (json.JSONDecodeError, TypeError):
                continue

            if task == "sentiment":
                label = data.get("label")
                if label == "POSITIVE":
                    profiles[domain]["positive"] += 1
                elif label == "NEGATIVE":
                    profiles[domain]["negative"] += 1
                profiles[domain]["sentiment_count"] += 1

                # A sample needs the model's reasoning to be worth showing.
                if data.get("reasoning"):
                    sample = _build_sample_dict(
                        doc_id, label or "NEUTRAL", float(confidence or 0.0),
                        data, title, source_type, published_at, domain, ident,
                        text, x_handle=username,
                    )
                    _insert_capped(
                        profiles[domain]["samples"], sample, MAX_OUTLET_SAMPLES,
                    )

            elif task == "bot_detection":
                label = data.get("label")
                if label in ("bot", "suspicious"):
                    profiles[domain]["bot_flags"] += 1
                profiles[domain]["total_scanned"] += 1

        return profiles

    def _format_outlet_profiles(
        self, profiles: Dict[str, Dict[str, Any]], narrative_map: Dict[int, str],
    ) -> List[OutletProfile]:
        """Format aggregated outlet data into response objects. Each sample is
        serialized to the shared ClassificationSample shape and tagged with the
        narrative its doc belongs to (null when unclustered)."""
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

            samples = []
            for s in stats["samples"]:
                serialized = _classification_sample_to_dict(_sample_dict_to_model(s))
                serialized["narrative"] = narrative_map.get(s.get("doc_id"))
                samples.append(serialized)

            results.append(OutletProfile(
                outlet=domain,
                source_type=stats["source_type"] or "unknown",
                net_tone=net_tone,
                bot_rate_pct=bot_rate_pct,
                volume=stats["sentiment_count"],
                total_scanned=stats["total_scanned"],
                samples=samples,
            ))

        return sorted(results, key=lambda x: x.bot_rate_pct, reverse=True)
