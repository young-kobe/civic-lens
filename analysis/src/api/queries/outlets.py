"""
Live query module for GET /outlet-profiles: per-domain/subreddit net tone
x bot rate. Preserved exception (unlike every other panel): bot-authored
content is INCLUDED here on purpose -- the bot rate IS the signal being
measured, so excluding it would erase the point of the surface. The
response carries this as a disclaimer field.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Optional

from analysis.src.api.models.common import RangeMeta, SampleDocModel
from analysis.src.api.models.outlets import OutletProfile, OutletProfilesResponse
from analysis.src.api.queries import base
from analysis.src.api.queries.constants import MAX_SAMPLES_PER_ENTITY, MIN_TARGET_SAMPLE_N, SNIPPET_MAX_CHARS
from analysis.src.common import db
from analysis.src.common.settings import get_settings

DISCLAIMER = (
    "Includes bot-flagged content on purpose -- the bot rate IS the signal. "
    "Net tone therefore differs from panels that exclude bot-scored authors. "
    "Sampled discourse, not a media-bias rating."
)


def _time_filter(
    start: Optional[datetime], end: Optional[datetime], params: Dict[str, Any],
    column: str = "d.published_at",
) -> str:
    """SQL fragment (leading ' AND ...' or '') applying an optional
    [start, end] bound on `column`, adding whichever bounds are present to
    `params`. Either bound may be None (unbounded). Matches
    queries/narratives.py's `_time_filter` convention."""
    clauses = []
    if start is not None:
        clauses.append(f"{column} >= %(_range_start)s")
        params["_range_start"] = start
    if end is not None:
        clauses.append(f"{column} <= %(_range_end)s")
        params["_range_end"] = end
    return (" AND " + " AND ".join(clauses)) if clauses else ""


def _fetch_sentiment_rows(conn, start, end, min_conf: float) -> List[Dict[str, Any]]:
    """One row per sentiment-scored doc, deliberately with NO bot-score
    exclusion -- see module docstring."""
    params: Dict[str, Any] = {"min_conf": min_conf, "snip": SNIPPET_MAX_CHARS}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT d.doc_id, d.domain_or_subreddit AS outlet_key, d.source_type::text AS source_type,
               sr.label::text AS label, r.confidence, r.model_id,
               d.published_at, d.source_url, d.admission_class::text AS admission_class,
               LEFT(d.body, %(snip)s) AS snippet
        FROM analysis.sentiment_results sr
        JOIN analysis.runs r ON r.run_id = sr.run_id AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN corpus.documents d ON d.doc_id = r.doc_id
        WHERE r.task = 'text'::analysis.task AND r.confidence >= %(min_conf)s
          AND d.domain_or_subreddit IS NOT NULL{time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _fetch_bot_scan_rows(conn, start, end, min_conf: float) -> List[Dict[str, Any]]:
    """One row per bot-detection-scanned doc, keyed the same way, for the
    bot_rate_pct half of the profile."""
    params: Dict[str, Any] = {"min_conf": min_conf}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT d.doc_id, d.domain_or_subreddit AS outlet_key, bs.label::text AS bot_label, r.model_id
        FROM analysis.bot_signals bs
        JOIN analysis.runs r ON r.run_id = bs.run_id AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN corpus.documents d ON d.doc_id = bs.doc_id
        WHERE r.confidence >= %(min_conf)s
          AND d.domain_or_subreddit IS NOT NULL{time_clause}
    """
    return conn.execute(sql, params).fetchall()


def _aggregate_outlets(
    sentiment_rows: List[Dict[str, Any]], bot_rows: List[Dict[str, Any]],
) -> List[OutletProfile]:
    profiles: Dict[str, Dict[str, Any]] = {}

    for row in sentiment_rows:
        key = row["outlet_key"]
        slot = profiles.setdefault(key, {
            "source_type": row["source_type"], "positive": 0, "negative": 0,
            "volume": 0, "bot_flags": 0, "total_scanned": 0, "rows": [],
        })
        slot["volume"] += 1
        if row["label"] == "positive":
            slot["positive"] += 1
        elif row["label"] == "negative":
            slot["negative"] += 1
        slot["rows"].append(row)

    for row in bot_rows:
        key = row["outlet_key"]
        slot = profiles.setdefault(key, {
            "source_type": None, "positive": 0, "negative": 0,
            "volume": 0, "bot_flags": 0, "total_scanned": 0, "rows": [],
        })
        slot["total_scanned"] += 1
        if row["bot_label"] == "bot":
            slot["bot_flags"] += 1

    results: List[OutletProfile] = []
    for outlet_key, slot in profiles.items():
        if slot["volume"] < MIN_TARGET_SAMPLE_N:
            continue
        net_tone = round((slot["positive"] - slot["negative"]) / slot["volume"] * 100, 1)
        bot_rate_pct = (
            round(slot["bot_flags"] / slot["total_scanned"] * 100, 1) if slot["total_scanned"] else 0.0
        )
        sample_rows = sorted(slot["rows"], key=lambda r: -(r["confidence"] or 0.0))
        samples = [
            SampleDocModel(**base.build_sample_doc(row, admission_class=row["admission_class"]))
            for row in sample_rows[:MAX_SAMPLES_PER_ENTITY]
        ]
        results.append(OutletProfile(
            outlet_key=outlet_key, source_type=slot["source_type"] or "unknown",
            net_tone=net_tone, bot_rate_pct=bot_rate_pct,
            volume=slot["volume"], total_scanned=slot["total_scanned"], samples=samples,
        ))

    results.sort(key=lambda p: p.bot_rate_pct, reverse=True)
    return results


def get_outlet_profiles(
    *, start: Optional[datetime], end: Optional[datetime], window_label: Optional[str],
) -> OutletProfilesResponse:
    """Aggregates the Outlet Profiles panel live over `[start, end]`
    (either bound may be None for an unbounded/'all' range)."""
    min_conf = get_settings().aggregation_min_confidence

    with db.connection() as conn:
        sentiment_rows = _fetch_sentiment_rows(conn, start, end, min_conf)
        bot_rows = _fetch_bot_scan_rows(conn, start, end, min_conf)

    model_ids = sorted({
        row["model_id"] for row in (*sentiment_rows, *bot_rows) if row.get("model_id")
    })
    sampled_count, official_count = base.split_admission_counts(sentiment_rows)
    range_meta = RangeMeta(
        window=window_label, start=start, end=end,
        sampled_doc_count=sampled_count, official_record_doc_count=official_count,
        model_ids=model_ids,
    )

    return OutletProfilesResponse(
        range=range_meta,
        disclaimer=DISCLAIMER,
        outlets=_aggregate_outlets(sentiment_rows, bot_rows),
    )
