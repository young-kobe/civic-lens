"""
Movers aggregator — window-over-window deltas for the political-tone ticker.

Computes top N entities (outlets / officials / subreddits) whose net political
tone moved the most between the previous equivalent window and the current
one, plus a single GOP-party-stance delta. Both surfaces the "biggest movers"
ticker on the Overall Tone + Political Narratives pages.

Keeps logic narrow so it can be cache-rebuilt cheaply on each pipeline run:
two SQL passes (current window + prev window), one per-entity aggregation
per pass, a diff. No LLM calls. No classification work.
"""

from __future__ import annotations

import json
from typing import Any, Dict, List, Optional, Tuple

from analysis.src.reporting.aggregators.base import (
    X_AUTHOR_JOIN_SQL,
    get_connection,
    get_previous_window_range,
    get_time_cutoff,
)
from analysis.src.reporting.entity_registry import get_registry, resolve_entity
from analysis.src.reporting.models import (
    EntityToneMover,
    FavorabilityMover,
    MoversResult,
)


# Minimum doc count per window per entity to be eligible as a mover — stops
# a swing from 1 → 2 docs from topping the list.
_MIN_VOLUME = 10

# Top N movers to return (up + down combined, sorted by |delta_pts| desc).
_TOP_N = 10


class MoversAggregator:
    """Build ``MoversResult`` payloads for the UI ticker."""

    def __init__(self, db_path: str):
        self.db_path = db_path

    def get_movers(self, time_window: str = "7d") -> MoversResult:
        # 'all' has no preceding window — no movers to compute.
        if time_window == "all":
            return MoversResult(window=time_window)

        prev_range = get_previous_window_range(time_window)
        current_cutoff = get_time_cutoff(time_window)
        if prev_range is None or current_cutoff is None:
            return MoversResult(window=time_window)

        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            current_stats = self._entity_stats(cursor, current_cutoff, None)
            prev_stats = self._entity_stats(cursor, prev_range[0], prev_range[1])
            current_fav = self._gop_favorability(cursor, current_cutoff, None)
            prev_fav = self._gop_favorability(cursor, prev_range[0], prev_range[1])

        entity_movers = self._diff_entity_stats(current_stats, prev_stats)
        favorability = self._diff_favorability(current_fav, prev_fav)

        return MoversResult(
            window=time_window,
            entity_movers=entity_movers,
            favorability_mover=favorability,
        )

    # ---------- Entity tone per window ----------

    def _entity_stats(
        self, cursor, start_ts: int, end_ts: Optional[int],
    ) -> Dict[Tuple[str, str], Dict[str, Any]]:
        """Per-entity {net, volume, profile} over [start_ts, end_ts).

        ``end_ts=None`` means "up to now" (current window). Otherwise bounded
        so the previous window doesn't bleed into the current one.
        """
        sql = f"""
            SELECT a.output_json, a.confidence,
                   d.source_type, d.domain_or_subreddit,
                   u.username
            FROM ai_outputs a
            JOIN docs d ON d.doc_id = a.doc_id
            {X_AUTHOR_JOIN_SQL}
            WHERE a.task_type = 'sentiment'
              AND COALESCE(a.inference_method, '') != 'deterministic'
              AND d.published_at >= ?
        """
        params: List[Any] = [start_ts]
        if end_ts is not None:
            sql += " AND d.published_at < ?"
            params.append(end_ts)
        cursor.execute(sql, params)

        registry = get_registry()
        accum: Dict[Tuple[str, str], Dict[str, Any]] = {}
        for (output_json, conf, source_type, domain, handle) in cursor.fetchall():
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            label = payload.get("label")
            c = conf if conf is not None else 1.0

            tier, entity = resolve_entity(registry, source_type, domain, handle)
            # Only registry-matched entities are eligible as movers — a
            # "catch-all shifted 5 points" row is noise, not signal.
            if entity is None:
                continue
            profile = entity.profile_dict()
            key = (tier or "unknown", profile["key"])
            slot = accum.setdefault(key, {
                "kind": profile["kind"],
                "displayName": profile["displayName"],
                "profile": profile,
                "sum": 0.0,
                "count": 0,
            })
            if label == "POSITIVE":
                slot["sum"] += c
                slot["count"] += 1
            elif label == "NEGATIVE":
                slot["sum"] -= c
                slot["count"] += 1
            else:
                # NEUTRAL / MIXED don't shift the net; still add to volume
                # so the volume floor reflects actual coverage.
                slot["count"] += 1
        return accum

    def _diff_entity_stats(
        self,
        current: Dict[Tuple[str, str], Dict[str, Any]],
        prev: Dict[Tuple[str, str], Dict[str, Any]],
    ) -> List[EntityToneMover]:
        movers: List[EntityToneMover] = []
        for key, cur in current.items():
            if cur["count"] < _MIN_VOLUME:
                continue
            prev_slot = prev.get(key)
            if prev_slot is None or prev_slot["count"] < _MIN_VOLUME:
                continue
            cur_net = (cur["sum"] / cur["count"]) * 100
            prev_net = (prev_slot["sum"] / prev_slot["count"]) * 100
            delta = cur_net - prev_net
            movers.append(EntityToneMover(
                key=key[1],
                kind=cur["kind"],
                displayName=cur["displayName"],
                current_net=round(cur_net, 1),
                prev_net=round(prev_net, 1),
                delta_pts=round(delta, 1),
                current_volume=cur["count"],
                prev_volume=prev_slot["count"],
                entity_profile=cur["profile"],
            ))
        movers.sort(key=lambda m: abs(m.delta_pts), reverse=True)
        return movers[:_TOP_N]

    # ---------- GOP favorability per window ----------

    def _gop_favorability(
        self, cursor, start_ts: int, end_ts: Optional[int],
    ) -> Dict[str, Any]:
        """Overall GOP net favorability + volume in [start_ts, end_ts).

        Pulled from ai_outputs.task='favorability' rows with target='GOP'.
        Mirrors the per-window computation SentimentAggregator does for the
        GOP card, simplified to the two numbers the mover needs.
        """
        sql = """
            SELECT a.output_json, a.confidence
            FROM ai_outputs a
            JOIN docs d ON d.doc_id = a.doc_id
            WHERE a.task_type = 'favorability'
              AND COALESCE(a.inference_method, '') != 'deterministic'
              AND d.published_at >= ?
        """
        params: List[Any] = [start_ts]
        if end_ts is not None:
            sql += " AND d.published_at < ?"
            params.append(end_ts)
        cursor.execute(sql, params)

        fav = 0
        unfav = 0
        volume = 0
        for (output_json, _conf) in cursor.fetchall():
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            # Many favorability outputs are dicts with a 'gop' or 'republicans'
            # target — keep this flexible, count any target that reads as GOP.
            target = str(payload.get("target", "")).lower()
            if "gop" not in target and "republican" not in target:
                continue
            volume += 1
            label = str(payload.get("label", "")).lower()
            if label == "favorable":
                fav += 1
            elif label == "unfavorable":
                unfav += 1
        net = ((fav - unfav) / volume * 100) if volume else 0.0
        return {"net": round(net, 1), "volume": volume}

    def _diff_favorability(
        self, current: Dict[str, Any], prev: Dict[str, Any],
    ) -> Optional[FavorabilityMover]:
        if current["volume"] < _MIN_VOLUME or prev["volume"] < _MIN_VOLUME:
            return None
        delta = current["net"] - prev["net"]
        return FavorabilityMover(
            label="GOP party stance",
            current_net=current["net"],
            prev_net=prev["net"],
            delta_pts=round(delta, 1),
            current_volume=current["volume"],
            prev_volume=prev["volume"],
        )
