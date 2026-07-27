"""
Live query module for the narratives panel (Phase 9 strictly-live API):
aggregates analysis.narratives/narrative_docs/narrative_leans/citations/runs
directly per request. Citation targets always resolve with no time bound
(owner decision 2026-07-24) -- only the member (source) doc side is scoped
to the requested range.
"""

from __future__ import annotations

from collections import defaultdict
from datetime import datetime
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

from analysis.src.api.queries.base import build_sample_doc, split_admission_counts
from analysis.src.api.queries.constants import (
    BOT_FLAGGED_SHARE_EXCLUSION,
    MAX_EVIDENCE_PER_SAMPLE,
    SNIPPET_MAX_CHARS,
)
from analysis.src.common import db
from analysis.src.common.settings import get_settings

# Default/ceiling for the `limit` query param -- narrative top-N is
# tolerance-based territory (recompute-driven ranking), not a hard cap
# tied to any stored rollup size.
DEFAULT_NARRATIVE_LIMIT = 20
MAX_NARRATIVE_LIMIT = 100

# corpus.source_type -> the news/reddit/x label the panel groups by.
SOURCE_TYPE_LABELS = {
    "news": "news",
    "reddit_post": "reddit",
    "x_post": "x",
}

# Analysis tasks whose runs feed a number actually rendered in a narrative
# summary (net_sentiment <- text, propaganda_flagged_fraction <- propaganda)
# -- the set RangeMeta.model_ids is drawn from.
_CONTRIBUTING_TASKS = ("text", "propaganda")


def _time_filter(
    start: Optional[datetime], end: Optional[datetime], params: Dict[str, Any],
    column: str = "d.published_at",
) -> str:
    """SQL fragment (leading ' AND ...' or '') applying an optional
    [start, end] bound on `column`, adding whichever bounds are present to
    `params`. Either bound may be None (unbounded) -- windows scope
    denominators, they never gate document existence."""
    clauses = []
    if start is not None:
        clauses.append(f"{column} >= %(_range_start)s")
        params["_range_start"] = start
    if end is not None:
        clauses.append(f"{column} <= %(_range_end)s")
        params["_range_end"] = end
    return (" AND " + " AND ".join(clauses)) if clauses else ""


def get_narratives(
    *,
    start: Optional[datetime],
    end: Optional[datetime],
    limit: int,
    window_label: Optional[str],
) -> Dict[str, Any]:
    """Top narratives by member-doc count in [start, end], with the
    coverage overlays assembled via batched follow-up queries. Returns
    {"range": <RangeMeta-shaped dict>, "narratives": [...]}."""
    with db.connection() as conn:
        ranked = _fetch_ranked(conn, start, end, limit)
        range_meta = _fetch_range_meta(conn, start, end, window_label)
        if not ranked:
            return {"range": range_meta, "narratives": []}

        ids = [row["narrative_id"] for row in ranked]
        min_conf = get_settings().aggregation_min_confidence
        source_breakdowns = _fetch_source_breakdown(conn, ids, start, end)
        timelines = _fetch_timeline(conn, ids, start, end)
        sentiment_rows = _fetch_sentiment_rows(conn, ids, start, end, min_conf)
        propaganda_rows = _fetch_propaganda_rows(conn, ids, start, end)
        bot_rows = _fetch_bot_rows(conn, ids, start, end)
        leans = _fetch_leans(conn, ids)
        citations = _fetch_citations(conn, ids, start, end)
        member_samples = _fetch_member_samples(conn, ids, start, end)
        mean_confidences = _fetch_mean_confidence(conn, ids, start, end)

    narratives = []
    for row in ranked:
        nid = row["narrative_id"]
        citation_count, cited_docs = citations.get(nid, (0, []))
        narratives.append({
            "narrative_id": nid,
            "anchor_claim_text": row["anchor_claim_text"],
            "doc_count": row["doc_count"],
            "source_breakdown": source_breakdowns.get(nid, []),
            "timeline": timelines.get(nid, []),
            "net_sentiment": _net_sentiment(sentiment_rows.get(nid, [])),
            "citation_count": citation_count,
            "cited_docs": cited_docs,
            "propaganda_flagged_fraction": _propaganda_fraction(propaganda_rows.get(nid, [])),
            "bot_pushed_fraction": _bot_pushed_fraction(bot_rows.get(nid, [])),
            "lean": _build_lean(leans.get(nid)),
            "member_doc_samples": member_samples.get(nid, []),
            "first_seen_at": row["first_seen_at"],
            "first_seen_doc_id": row["first_seen_doc_id"],
            "mean_confidence": mean_confidences.get(nid),
        })
    return {"range": range_meta, "narratives": narratives}


# =============================================================================
# Pure computation -- no DB, unit-testable directly.
# =============================================================================

def _net_sentiment(rows: Sequence[Tuple[Optional[str], Optional[float]]]) -> Optional[float]:
    """-100..+100 over (label, confidence) pairs already filtered to
    analysis.runs.confidence >= the aggregation floor in SQL. POSITIVE -> +conf,
    NEGATIVE -> -conf, NEUTRAL/MIXED -> 0; NEUTRAL/MIXED still count in the
    denominator. None (no data) when there are no rows, distinct from an
    actual net score of 0."""
    if not rows:
        return None
    total = 0.0
    for label, confidence in rows:
        weight = confidence if confidence is not None else 1.0
        if label == "positive":
            total += weight
        elif label == "negative":
            total -= weight
    return round((total / len(rows)) * 100, 1)


def _propaganda_fraction(techniques_validated_counts: Sequence[Optional[int]]) -> Optional[float]:
    """Fraction of scored member docs with >=1 validated propaganda
    technique. None when no member doc has been through propaganda
    detection yet -- distinct from a real 0.0 flagged fraction."""
    if not techniques_validated_counts:
        return None
    flagged = sum(1 for n in techniques_validated_counts if (n or 0) > 0)
    return round(flagged / len(techniques_validated_counts), 3)


def _bot_pushed_fraction(flagged_shares: Sequence[Optional[float]]) -> Optional[float]:
    """Fraction of member docs authored by a bot-scored author (flagged
    share >= BOT_FLAGGED_SHARE_EXCLUSION) -- this is the one metric the
    exclusion threshold measures rather than filters out. None when no
    member doc has an author_bot_scores row at all."""
    if not flagged_shares:
        return None
    flagged = sum(1 for share in flagged_shares if (share or 0.0) >= BOT_FLAGGED_SHARE_EXCLUSION)
    return round(flagged / len(flagged_shares), 3)


def _build_lean(row: Optional[Mapping[str, Any]]) -> Optional[Dict[str, Any]]:
    """LeanLabel-shaped dict from an analysis.narrative_leans row, or None
    if the narrative has no derived-lean row -- omitted entirely rather than
    rendered as a guess."""
    if row is None:
        return None
    return {
        "kind": "derived",
        "value": row["lean"],
        "lean_share": row["lean_share"],
        "confidence": row["confidence"],
        "sample_count": row["doc_count"],
    }


# =============================================================================
# SQL fetchers -- batched over the full ranked id set (O(1) query count).
# =============================================================================

def _fetch_ranked(
    conn, start: Optional[datetime], end: Optional[datetime], limit: int,
) -> List[Mapping[str, Any]]:
    params: Dict[str, Any] = {"limit": limit}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT n.narrative_id AS narrative_id, c.claim_text AS anchor_claim_text,
               n.first_seen_at AS first_seen_at, n.first_seen_doc_id AS first_seen_doc_id,
               COUNT(DISTINCT nd.doc_id) AS doc_count
        FROM analysis.narratives n
        JOIN analysis.narrative_docs nd ON nd.narrative_id = n.narrative_id
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        LEFT JOIN analysis.claims c ON c.claim_id = n.anchor_claim_id
        WHERE true{time_clause}
        GROUP BY n.narrative_id, c.claim_text, n.first_seen_at, n.first_seen_doc_id
        ORDER BY doc_count DESC, n.narrative_id
        LIMIT %(limit)s
    """
    return conn.execute(sql, params).fetchall()


def _fetch_range_meta(
    conn, start: Optional[datetime], end: Optional[datetime], window_label: Optional[str],
) -> Dict[str, Any]:
    """Range honesty metadata: admission-class split over every in-range
    member doc (not just the returned top-N) and the distinct model_ids
    behind the sentiment/propaganda numbers rendered per narrative."""
    params: Dict[str, Any] = {}
    time_clause = _time_filter(start, end, params)
    doc_sql = f"""
        SELECT DISTINCT d.doc_id AS doc_id, d.admission_class::text AS admission_class
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        WHERE true{time_clause}
    """
    doc_rows = conn.execute(doc_sql, params).fetchall()
    sampled, official_record = split_admission_counts(doc_rows) if doc_rows else (0, 0)

    model_sql = f"""
        SELECT DISTINCT r.model_id AS model_id
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        JOIN analysis.runs r
             ON r.doc_id = d.doc_id
            AND r.task = ANY(%(tasks)s::analysis.task[])
            AND r.is_current AND r.status = 'done'::analysis.run_status
        WHERE true{time_clause}
    """
    model_params = dict(params, tasks=list(_CONTRIBUTING_TASKS))
    model_rows = conn.execute(model_sql, model_params).fetchall()

    return {
        "window": window_label,
        "start": start,
        "end": end,
        "sampled_doc_count": sampled,
        "official_record_doc_count": official_record,
        "model_ids": sorted({row["model_id"] for row in model_rows}),
    }


def _fetch_source_breakdown(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime],
) -> Dict[int, List[Dict[str, Any]]]:
    params: Dict[str, Any] = {"ids": list(ids)}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id AS narrative_id, d.source_type::text AS source_type,
               COUNT(DISTINCT nd.doc_id) AS doc_count
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        WHERE nd.narrative_id = ANY(%(ids)s){time_clause}
        GROUP BY nd.narrative_id, d.source_type
    """
    out: Dict[int, List[Dict[str, Any]]] = defaultdict(list)
    for row in conn.execute(sql, params).fetchall():
        out[row["narrative_id"]].append({
            "source": SOURCE_TYPE_LABELS[row["source_type"]],
            "doc_count": row["doc_count"],
        })
    return out


def _fetch_timeline(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime],
) -> Dict[int, List[Dict[str, Any]]]:
    params: Dict[str, Any] = {"ids": list(ids)}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id AS narrative_id,
               date_trunc('day', d.published_at)::date AS day,
               COUNT(DISTINCT nd.doc_id) AS doc_count
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        WHERE nd.narrative_id = ANY(%(ids)s){time_clause}
        GROUP BY nd.narrative_id, day
        ORDER BY day ASC
    """
    out: Dict[int, List[Dict[str, Any]]] = defaultdict(list)
    for row in conn.execute(sql, params).fetchall():
        out[row["narrative_id"]].append({"day": row["day"], "doc_count": row["doc_count"]})
    return out


def _fetch_sentiment_rows(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime], min_conf: float,
) -> Dict[int, List[Tuple[Optional[str], Optional[float]]]]:
    params: Dict[str, Any] = {"ids": list(ids), "min_conf": min_conf}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id AS narrative_id, sr.label::text AS label, r.confidence AS confidence
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        JOIN analysis.runs r
             ON r.doc_id = d.doc_id AND r.task = 'text'::analysis.task
            AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN analysis.sentiment_results sr ON sr.run_id = r.run_id
        WHERE nd.narrative_id = ANY(%(ids)s) AND r.confidence >= %(min_conf)s{time_clause}
    """
    out: Dict[int, List[Tuple[Optional[str], Optional[float]]]] = defaultdict(list)
    for row in conn.execute(sql, params).fetchall():
        out[row["narrative_id"]].append((row["label"], row["confidence"]))
    return out


def _fetch_propaganda_rows(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime],
) -> Dict[int, List[Optional[int]]]:
    params: Dict[str, Any] = {"ids": list(ids), "bot_exclusion": BOT_FLAGGED_SHARE_EXCLUSION}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id AS narrative_id, pr.techniques_validated AS techniques_validated
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        JOIN analysis.runs r
             ON r.doc_id = d.doc_id AND r.task = 'propaganda'::analysis.task
            AND r.is_current AND r.status = 'done'::analysis.run_status
        JOIN analysis.propaganda_results pr ON pr.run_id = r.run_id
        WHERE nd.narrative_id = ANY(%(ids)s){time_clause}
          AND (d.author_id IS NULL OR NOT EXISTS (
                SELECT 1 FROM analysis.author_bot_scores ab
                WHERE ab.author_id = d.author_id
                  AND (ab.bot_post_count + ab.suspicious_post_count)::float
                      / NULLIF(ab.sample_count, 0) >= %(bot_exclusion)s
              ))
    """
    out: Dict[int, List[Optional[int]]] = defaultdict(list)
    for row in conn.execute(sql, params).fetchall():
        out[row["narrative_id"]].append(row["techniques_validated"])
    return out


def _fetch_bot_rows(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime],
) -> Dict[int, List[Optional[float]]]:
    """{narrative_id -> [author's flagged share, ...]} for in-range member
    docs whose author has an author_bot_scores row at all -- the
    denominator bot_pushed_fraction measures exactly."""
    params: Dict[str, Any] = {"ids": list(ids)}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id AS narrative_id,
               (ab.bot_post_count + ab.suspicious_post_count)::float
                   / NULLIF(ab.sample_count, 0) AS flagged_share
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        JOIN analysis.author_bot_scores ab ON ab.author_id = d.author_id
        WHERE nd.narrative_id = ANY(%(ids)s) AND d.author_id IS NOT NULL{time_clause}
    """
    out: Dict[int, List[Optional[float]]] = defaultdict(list)
    for row in conn.execute(sql, params).fetchall():
        out[row["narrative_id"]].append(row["flagged_share"])
    return out


def _fetch_mean_confidence(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime],
) -> Dict[int, float]:
    """Mean analysis.narrative_docs.confidence (the claim-match confidence
    copied from analysis.claims.confidence at insert time -- see that
    column's comment in 0001_north_star.sql) over ALL in-window member
    docs, not just the MAX_EVIDENCE_PER_SAMPLE ranked subset
    _fetch_member_samples returns. Narratives with no non-null confidence
    in range are simply absent from the returned mapping (None upstream)."""
    params: Dict[str, Any] = {"ids": list(ids)}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id AS narrative_id, AVG(nd.confidence) AS mean_confidence
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        WHERE nd.narrative_id = ANY(%(ids)s) AND nd.confidence IS NOT NULL{time_clause}
        GROUP BY nd.narrative_id
    """
    return {
        row["narrative_id"]: round(row["mean_confidence"], 3)
        for row in conn.execute(sql, params).fetchall()
    }


def _fetch_leans(conn, ids: Sequence[int]) -> Dict[int, Mapping[str, Any]]:
    sql = """
        SELECT narrative_id, lean::text AS lean, lean_share, confidence, doc_count
        FROM analysis.narrative_leans
        WHERE narrative_id = ANY(%(ids)s)
    """
    return {row["narrative_id"]: row for row in conn.execute(sql, {"ids": list(ids)}).fetchall()}


def _fetch_citations(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime],
) -> Dict[int, Tuple[int, List[Dict[str, Any]]]]:
    """{narrative_id -> (edge_count, cited_doc_samples)}. The member
    (source) doc is scoped to [start, end]; the cited (target) doc is
    resolved with NO time bound whatsoever -- an in-range doc may cite an
    official_record doc far older than any window (owner decision
    2026-07-24), and that edge must still render fully resolved."""
    params: Dict[str, Any] = {"ids": list(ids), "snippet_max": SNIPPET_MAX_CHARS}
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT nd.narrative_id AS narrative_id, tgt.doc_id AS doc_id,
               tgt.source_url AS source_url, tgt.published_at AS published_at,
               tgt.admission_class::text AS admission_class, r.confidence AS confidence,
               LEFT(COALESCE(tgt.title, tgt.body), %(snippet_max)s) AS snippet
        FROM analysis.narrative_docs nd
        JOIN corpus.documents d ON d.doc_id = nd.doc_id
        JOIN analysis.citations cit ON cit.source_doc_id = d.doc_id
        JOIN corpus.documents tgt ON tgt.doc_id = cit.target_doc_id
        LEFT JOIN analysis.runs r ON r.run_id = cit.run_id
        WHERE nd.narrative_id = ANY(%(ids)s) AND cit.target_doc_id IS NOT NULL{time_clause}
    """
    counts: Dict[int, int] = defaultdict(int)
    samples: Dict[int, List[Dict[str, Any]]] = defaultdict(list)
    seen_targets: Dict[int, set] = defaultdict(set)
    for row in conn.execute(sql, params).fetchall():
        nid = row["narrative_id"]
        counts[nid] += 1
        # A sample requires a resolvable confidence (invariant, enforced by
        # build_sample_doc) -- filtered here rather than fabricated. Dedup by
        # target doc_id so a heavily-cited doc doesn't crowd out the sample
        # list with repeats of itself.
        if row["confidence"] is None:
            continue
        if row["doc_id"] in seen_targets[nid] or len(samples[nid]) >= MAX_EVIDENCE_PER_SAMPLE:
            continue
        seen_targets[nid].add(row["doc_id"])
        samples[nid].append(build_sample_doc(row, admission_class=row["admission_class"]))
    return {nid: (counts[nid], samples.get(nid, [])) for nid in counts}


def _fetch_member_samples(
    conn, ids: Sequence[int], start: Optional[datetime], end: Optional[datetime],
) -> Dict[int, List[Dict[str, Any]]]:
    """Up to MAX_EVIDENCE_PER_SAMPLE member docs per narrative, ranked by
    the claim-match confidence recorded on narrative_docs.confidence at
    insert time, then recency."""
    params: Dict[str, Any] = {
        "ids": list(ids),
        "snippet_max": SNIPPET_MAX_CHARS,
        "max_samples": MAX_EVIDENCE_PER_SAMPLE,
    }
    time_clause = _time_filter(start, end, params)
    sql = f"""
        SELECT narrative_id, doc_id, source_url, published_at, admission_class, confidence, snippet
        FROM (
            SELECT nd.narrative_id AS narrative_id, d.doc_id AS doc_id,
                   d.source_url AS source_url, d.published_at AS published_at,
                   d.admission_class::text AS admission_class, nd.confidence AS confidence,
                   LEFT(COALESCE(d.title, d.body), %(snippet_max)s) AS snippet,
                   ROW_NUMBER() OVER (
                       PARTITION BY nd.narrative_id
                       ORDER BY nd.confidence DESC NULLS LAST, d.published_at DESC
                   ) AS rn
            FROM analysis.narrative_docs nd
            JOIN corpus.documents d ON d.doc_id = nd.doc_id
            WHERE nd.narrative_id = ANY(%(ids)s) AND nd.confidence IS NOT NULL{time_clause}
        ) ranked
        WHERE rn <= %(max_samples)s
        ORDER BY narrative_id, rn
    """
    out: Dict[int, List[Dict[str, Any]]] = defaultdict(list)
    for row in conn.execute(sql, params).fetchall():
        out[row["narrative_id"]].append(
            build_sample_doc(row, admission_class=row["admission_class"])
        )
    return out
