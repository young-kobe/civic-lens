"""
Live query module for GET /movers: window-over-window tone and
favorability shifts, comparing an arbitrary current [start, end) range
against the immediately preceding range of the same duration -- both
computed live, no precomputed rollup.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Optional, Set

from analysis.src.api.models.common import RangeMeta
from analysis.src.api.models.movers import FavorabilityMover, MoversResponse, ToneMover
from analysis.src.api.queries import base
from analysis.src.api.queries.constants import BOT_FLAGGED_SHARE_EXCLUSION, MIN_TARGET_SAMPLE_N
from analysis.src.common import db
from analysis.src.common.settings import get_settings

# Top N movers (up + down combined, ranked by |delta_pts|) returned in
# tone_movers. Display cap, not a statistical floor -- matches the
# pre-redesign ticker's cap (reporting/aggregators/movers.py::_TOP_N).
_TOP_N_MOVERS = 10


def _time_filter(
    start: Optional[datetime], end: Optional[datetime], params: Dict[str, Any],
    column: str = "d.published_at",
) -> str:
    """SQL fragment (leading ' AND ...' or '') applying an optional
    [start, end) bound on `column`. EXCLUSIVE upper bound (`<`) -- unlike
    the sibling single-range panels' inclusive `_time_filter` convention,
    movers compares two ADJACENT periods where previous_end ==
    current_start; a doc timestamped at that exact shared instant must
    land in exactly one period, not both."""
    clauses = []
    if start is not None:
        clauses.append(f"{column} >= %(_range_start)s")
        params["_range_start"] = start
    if end is not None:
        clauses.append(f"{column} < %(_range_end)s")
        params["_range_end"] = end
    return (" AND " + " AND ".join(clauses)) if clauses else ""


def _fetch_tone_rows(conn, start, end, min_conf: float) -> List[Dict[str, Any]]:
    """One row per sentiment-scored doc resolved to an entity (official/
    collective via author_profiles, outlet via news_articles, subreddit
    via reddit_posts). Bot-scored-author docs are excluded (binding rule:
    every panel but outlet-profiles excludes them from discourse
    denominators)."""
    params: Dict[str, Any] = {"min_conf": min_conf, "bot_threshold": BOT_FLAGGED_SHARE_EXCLUSION}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT d.doc_id, d.admission_class::text AS admission_class, r.model_id, sr.label::text AS label,
               COALESCE(ap.entity_id, na.outlet_entity_id, rp.subreddit_entity_id) AS entity_id
        FROM analysis.sentiment_results sr
        JOIN analysis.runs r ON r.run_id = sr.run_id AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
        LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
        LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
        LEFT JOIN analysis.author_bot_scores abs ON abs.author_id = d.author_id
        WHERE r.task = 'text'::analysis.task AND r.confidence >= %(min_conf)s
          AND (abs.author_id IS NULL OR (abs.bot_post_count + abs.suspicious_post_count)::float
              / NULLIF(abs.sample_count, 0) < %(bot_threshold)s){time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _fetch_favorability_rows(conn, start, end, min_conf: float) -> List[Dict[str, Any]]:
    """One row per (run, entity) target_mentions stance -- the FavorabilityMover
    feature's data source since 2026-07-25 (analysis.favorability_stances is
    retired, no longer written). entity_id is a direct FK on target_mentions;
    unresolved mentions (entity_id NULL) are excluded, since movers has
    nothing to aggregate them by. Same bot exclusion as tone."""
    params: Dict[str, Any] = {"min_conf": min_conf, "bot_threshold": BOT_FLAGGED_SHARE_EXCLUSION}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT d.doc_id, d.admission_class::text AS admission_class, r.model_id,
               tm.stance::text AS stance, tm.entity_id
        FROM analysis.target_mentions tm
        JOIN analysis.runs r ON r.run_id = tm.run_id AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN corpus.documents d ON d.doc_id = tm.doc_id
        LEFT JOIN analysis.author_bot_scores abs ON abs.author_id = d.author_id
        WHERE r.task = 'targets'::analysis.task AND tm.entity_id IS NOT NULL
          AND tm.confidence >= %(min_conf)s
          AND (abs.author_id IS NULL OR (abs.bot_post_count + abs.suspicious_post_count)::float
              / NULLIF(abs.sample_count, 0) < %(bot_threshold)s){time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _fetch_entities(conn, entity_ids: Set[int]) -> Dict[int, Dict[str, Any]]:
    if not entity_ids:
        return {}
    sql = """
        SELECT entity_id, entity_key, kind::text AS kind, display_name
        FROM corpus.entities WHERE entity_id = ANY(%(ids)s)
    """
    rows = conn.execute(sql, {"ids": list(entity_ids)}).fetchall()
    return {row["entity_id"]: row for row in rows}


def _tally_tone(rows: List[Dict[str, Any]]) -> Dict[int, Dict[str, Any]]:
    accum: Dict[int, Dict[str, Any]] = {}
    for row in rows:
        entity_id = row["entity_id"]
        if entity_id is None:
            continue
        slot = accum.setdefault(entity_id, {"pos": 0, "neg": 0, "count": 0})
        slot["count"] += 1
        if row["label"] == "positive":
            slot["pos"] += 1
        elif row["label"] == "negative":
            slot["neg"] += 1
    return accum


def _tally_favorability(rows: List[Dict[str, Any]]) -> Dict[int, Dict[str, Any]]:
    accum: Dict[int, Dict[str, Any]] = {}
    for row in rows:
        entity_id = row["entity_id"]
        slot = accum.setdefault(entity_id, {"pos": 0, "neg": 0, "count": 0})
        slot["count"] += 1
        if row["stance"] == "positive":
            slot["pos"] += 1
        elif row["stance"] == "negative":
            slot["neg"] += 1
    return accum


def _diff_tone(
    current: Dict[int, Dict[str, Any]], previous: Dict[int, Dict[str, Any]], entities: Dict[int, Dict[str, Any]],
) -> List[ToneMover]:
    movers: List[ToneMover] = []
    for entity_id, cur in current.items():
        if cur["count"] < MIN_TARGET_SAMPLE_N:
            continue
        prev = previous.get(entity_id)
        if prev is None or prev["count"] < MIN_TARGET_SAMPLE_N:
            continue
        entity = entities.get(entity_id)
        if entity is None:
            continue
        cur_net = (cur["pos"] - cur["neg"]) / cur["count"] * 100
        prev_net = (prev["pos"] - prev["neg"]) / prev["count"] * 100
        movers.append(ToneMover(
            entity_key=entity["entity_key"], kind=entity["kind"], display_name=entity["display_name"],
            current_net=round(cur_net, 1), prev_net=round(prev_net, 1),
            delta_pts=round(cur_net - prev_net, 1),
            current_volume=cur["count"], prev_volume=prev["count"],
        ))
    movers.sort(key=lambda m: -abs(m.delta_pts))
    return movers[:_TOP_N_MOVERS]


def _diff_favorability(
    current: Dict[int, Dict[str, Any]], previous: Dict[int, Dict[str, Any]], entities: Dict[int, Dict[str, Any]],
) -> Optional[FavorabilityMover]:
    candidates: List[FavorabilityMover] = []
    for entity_id, cur in current.items():
        if cur["count"] < MIN_TARGET_SAMPLE_N:
            continue
        prev = previous.get(entity_id)
        if prev is None or prev["count"] < MIN_TARGET_SAMPLE_N:
            continue
        entity = entities.get(entity_id)
        if entity is None:
            continue
        cur_net = (cur["pos"] - cur["neg"]) / cur["count"] * 100
        prev_net = (prev["pos"] - prev["neg"]) / prev["count"] * 100
        candidates.append(FavorabilityMover(
            entity_key=entity["entity_key"], kind=entity["kind"], display_name=entity["display_name"],
            current_net=round(cur_net, 1), prev_net=round(prev_net, 1),
            delta_pts=round(cur_net - prev_net, 1),
            current_volume=cur["count"], prev_volume=prev["count"],
        ))
    if not candidates:
        return None
    candidates.sort(key=lambda m: -abs(m.delta_pts))
    return candidates[0]


def get_movers(
    *,
    current_start: Optional[datetime],
    current_end: Optional[datetime],
    previous_start: datetime,
    previous_end: datetime,
    current_window_label: Optional[str],
) -> MoversResponse:
    """Aggregates window-over-window tone/favorability movers. The
    previous period is always fully bounded (the caller -- the router --
    computes it as the same duration immediately preceding the current
    period's start); the current period's own bounds may be open-ended
    (`current_end=None` means "up to now")."""
    min_conf = get_settings().aggregation_min_confidence

    with db.connection() as conn:
        current_tone_rows = _fetch_tone_rows(conn, current_start, current_end, min_conf)
        previous_tone_rows = _fetch_tone_rows(conn, previous_start, previous_end, min_conf)
        current_fav_rows = _fetch_favorability_rows(conn, current_start, current_end, min_conf)
        previous_fav_rows = _fetch_favorability_rows(conn, previous_start, previous_end, min_conf)

        entity_ids = {
            row["entity_id"]
            for row in (*current_tone_rows, *previous_tone_rows, *current_fav_rows, *previous_fav_rows)
            if row["entity_id"] is not None
        }
        entities = _fetch_entities(conn, entity_ids)

    tone_movers = _diff_tone(_tally_tone(current_tone_rows), _tally_tone(previous_tone_rows), entities)
    top_favorability_mover = _diff_favorability(
        _tally_favorability(current_fav_rows), _tally_favorability(previous_fav_rows), entities,
    )

    current_sampled, current_official = base.split_admission_counts(current_tone_rows)
    previous_sampled, previous_official = base.split_admission_counts(previous_tone_rows)
    current_model_ids = sorted({
        row["model_id"] for row in (*current_tone_rows, *current_fav_rows) if row.get("model_id")
    })
    previous_model_ids = sorted({
        row["model_id"] for row in (*previous_tone_rows, *previous_fav_rows) if row.get("model_id")
    })

    return MoversResponse(
        current_range=RangeMeta(
            window=current_window_label, start=current_start, end=current_end,
            sampled_doc_count=current_sampled, official_record_doc_count=current_official,
            model_ids=current_model_ids,
        ),
        previous_range=RangeMeta(
            window=None, start=previous_start, end=previous_end,
            sampled_doc_count=previous_sampled, official_record_doc_count=previous_official,
            model_ids=previous_model_ids,
        ),
        tone_movers=tone_movers,
        top_favorability_mover=top_favorability_mover,
    )
