"""
Read-side helpers shared by every Phase 9 API query module: window cutoff
arithmetic, admission_class (sampled vs official_record) predicates, and
the evidence-sample row builder. Strictly-live (owner decision
2026-07-24): panels aggregate `corpus.*`/`analysis.*` directly at request
time, so there is deliberately no caching layer here yet -- see
docs/audit-trail/analysis/2026-07-24-phase9-prewave.md.
"""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Any, Dict, Iterable, List, Mapping, Optional, Tuple

from analysis.src.api.models.common import ClassificationSampleModel
from analysis.src.api.queries.constants import MAX_EVIDENCE_PER_SAMPLE, SNIPPET_MAX_CHARS, WINDOWS

SAMPLED = "sampled"
OFFICIAL_RECORD = "official_record"

ALL_TIME = "all"


def window_cutoff(window: str) -> datetime:
    """Inclusive lower bound for `window`, anchored to the current
    instant. `official_record` docs are admitted into a window by the same
    published-in-window cutoff as `sampled` docs -- admission_class only
    governs ETL-time capture (0003_admission_class.sql), not which window
    a doc's activity falls into at query time."""
    delta = WINDOWS.get(window)
    if delta is None:
        raise ValueError(f"unknown window: {window!r}")
    return datetime.now(timezone.utc) - delta


def resolve_time_range(
    window: Optional[str] = None,
    date_from: Optional[datetime] = None,
    date_to: Optional[datetime] = None,
) -> Tuple[Optional[datetime], Optional[datetime]]:
    """Resolve a panel's time scope to `(start, end)` bounds, either of
    which may be None (unbounded). Presets are conveniences over arbitrary
    ranges (owner decision 2026-07-24: historical data stays queryable
    forever -- windows scope aggregate denominators, never document
    existence). Exactly one mode: a preset/'all' window XOR an explicit
    date_from/date_to pair."""
    if window is not None and (date_from is not None or date_to is not None):
        raise ValueError("pass either window or date_from/date_to, not both")
    if window is not None:
        if window == ALL_TIME:
            return None, None
        return window_cutoff(window), None
    if date_from is None and date_to is None:
        raise ValueError("a window or an explicit date_from/date_to is required")
    if date_from is not None and date_to is not None and date_from > date_to:
        raise ValueError("date_from must not be after date_to")
    return date_from, date_to


def admission_label(admission_class: str) -> str:
    """Human-facing label for a `corpus.documents.admission_class` value --
    the one place that wording is decided, so query modules never hand-roll
    'sampled'/'official record' phrasing independently."""
    if admission_class == OFFICIAL_RECORD:
        return "official record"
    if admission_class == SAMPLED:
        return "sampled discourse"
    raise ValueError(f"unknown admission_class: {admission_class!r}")


def split_admission_counts(rows: Iterable[Mapping[str, Any]]) -> Tuple[int, int]:
    """Count `(sampled, official_record)` docs from rows each carrying an
    `admission_class` key -- so a panel query can report the two admission
    bases as distinct numbers instead of one silently blended total."""
    sampled = 0
    official_record = 0
    for row in rows:
        admission_class = row["admission_class"]
        if admission_class == OFFICIAL_RECORD:
            official_record += 1
        elif admission_class == SAMPLED:
            sampled += 1
        else:
            raise ValueError(f"unknown admission_class: {admission_class!r}")
    return sampled, official_record


def build_sample_doc(row: Mapping[str, Any], *, admission_class: str) -> Dict[str, Any]:
    """Assemble one evidence-sample dict from a corpus/analysis query row,
    enforcing the two things invariant C1 and the results-store
    traceability contract require: `source_url` present, `confidence`
    carried. Raises ValueError (never silently drops the field) if either
    is missing. The returned shape feeds directly into
    `api.models.common.SampleDocModel`."""
    source_url = row.get("source_url")
    if not source_url:
        raise ValueError(
            "sample doc source_url is required (invariant C1) -- refusing to "
            "build a sample with no source link"
        )
    confidence = row.get("confidence")
    if confidence is None:
        raise ValueError("sample doc confidence is required -- traceability contract")
    return {
        "doc_id": row["doc_id"],
        "source_url": source_url,
        "snippet": row.get("snippet"),
        "confidence": confidence,
        "admission_class": admission_class,
        "published_at": row.get("published_at"),
    }


# --------------------------------------------------------------------------- #
#  Rich sample fields: doc + sentiment + author + engagement + targets +      #
#  narrative, for the ClassificationSample drill-down restoration.            #
# --------------------------------------------------------------------------- #

_RICH_DOC_ROWS_SQL = """
    SELECT d.doc_id, d.title, d.body, d.source_type, d.source_url, d.published_at,
           d.domain_or_subreddit, d.author_id,
           a.handle AS author_handle, a.display_name AS author_display_name,
           a.profile_image_url AS author_profile_image_url,
           a.verified_type AS author_verified_type,
           a.followers_count AS author_followers_count,
           a.account_created_at AS author_account_created_at,
           a.description AS author_description
    FROM corpus.documents d
    LEFT JOIN corpus.authors a ON a.author_id = d.author_id
    WHERE d.doc_id = ANY(%(doc_ids)s)
"""

_RICH_SENTIMENT_ROWS_SQL = """
    SELECT r.doc_id, sr.label, sr.evidence_spans, sr.sarcasm_detected, r.confidence,
           r.raw_response ->> 'reasoning' AS reasoning
    FROM analysis.runs r
    JOIN analysis.sentiment_results sr ON sr.run_id = r.run_id
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done' AND r.doc_id = ANY(%(doc_ids)s)
"""

_RICH_X_ENGAGEMENT_ROWS_SQL = """
    SELECT doc_id, retweet_count, reply_count, like_count, quote_count
    FROM corpus.x_posts
    WHERE doc_id = ANY(%(doc_ids)s)
"""

_RICH_REDDIT_ENGAGEMENT_ROWS_SQL = """
    SELECT doc_id, score, num_comments
    FROM corpus.reddit_posts
    WHERE doc_id = ANY(%(doc_ids)s)
"""

# entities.display_name when the target_mentions row resolved to a
# registry entity, else the LLM's raw_target verbatim.
_RICH_TARGET_ROWS_SQL = """
    SELECT tm.doc_id, tm.stance, COALESCE(e.display_name, tm.raw_target) AS label
    FROM analysis.target_mentions tm
    JOIN analysis.runs r ON r.run_id = tm.run_id
    LEFT JOIN corpus.entities e ON e.entity_id = tm.entity_id
    WHERE r.is_current AND r.task = 'targets' AND tm.doc_id = ANY(%(doc_ids)s)
"""

# One narrative per doc, deterministic (lowest narrative_id wins) --
# DISTINCT ON does the "any one, picked consistently" selection in SQL
# rather than a per-doc loop.
_RICH_NARRATIVE_ROWS_SQL = """
    SELECT DISTINCT ON (nd.doc_id) nd.doc_id, n.name
    FROM analysis.narrative_docs nd
    JOIN analysis.narratives n ON n.narrative_id = nd.narrative_id
    WHERE nd.doc_id = ANY(%(doc_ids)s)
    ORDER BY nd.doc_id, nd.narrative_id
"""


def fetch_rich_sample_fields(conn, doc_ids: Iterable[int]) -> Dict[int, dict]:
    """Batch-fetch every field a ClassificationSample drill-down needs for
    each id in ``doc_ids`` -- a handful of `WHERE doc_id = ANY(...)` queries,
    never a per-doc-id loop. Missing ids (no matching document) are simply
    absent from the returned dict.

    Each value dict shape:
      title, body, source_type, source_url, published_at, domain_or_subreddit,
      sentiment: {label, evidence_spans, sarcasm_detected, confidence, reasoning} | None,
      author: {handle, display_name, profile_image_url, verified_type,
                followers_count, account_created_at, description} | None,
      engagement: {retweets, replies, likes, quotes} | {score, num_comments} | None,
      targets: [(label, stance), ...],
      narrative: str | None.
    """
    ids = list(doc_ids)
    if not ids:
        return {}
    params = {"doc_ids": ids}

    doc_rows = conn.execute(_RICH_DOC_ROWS_SQL, params).fetchall()
    sentiment_rows = conn.execute(_RICH_SENTIMENT_ROWS_SQL, params).fetchall()
    x_engagement_rows = conn.execute(_RICH_X_ENGAGEMENT_ROWS_SQL, params).fetchall()
    reddit_engagement_rows = conn.execute(_RICH_REDDIT_ENGAGEMENT_ROWS_SQL, params).fetchall()
    target_rows = conn.execute(_RICH_TARGET_ROWS_SQL, params).fetchall()
    narrative_rows = conn.execute(_RICH_NARRATIVE_ROWS_SQL, params).fetchall()

    sentiment_by_doc = {row["doc_id"]: _rich_sentiment_dict(row) for row in sentiment_rows}
    x_engagement_by_doc = {row["doc_id"]: _rich_x_engagement_dict(row) for row in x_engagement_rows}
    reddit_engagement_by_doc = {
        row["doc_id"]: _rich_reddit_engagement_dict(row) for row in reddit_engagement_rows
    }
    targets_by_doc: Dict[int, List[Dict[str, Any]]] = {}
    for row in target_rows:
        targets_by_doc.setdefault(row["doc_id"], []).append(
            {"label": row["label"], "stance": row["stance"]}
        )
    narrative_by_doc = {row["doc_id"]: row["name"] for row in narrative_rows}

    result: Dict[int, dict] = {}
    for row in doc_rows:
        doc_id = row["doc_id"]
        result[doc_id] = {
            "title": row["title"],
            "body": row["body"],
            "source_type": row["source_type"],
            "source_url": row["source_url"],
            "published_at": row["published_at"],
            "domain_or_subreddit": row["domain_or_subreddit"],
            "sentiment": sentiment_by_doc.get(doc_id),
            "author": _rich_author_dict(row),
            "engagement": x_engagement_by_doc.get(doc_id) or reddit_engagement_by_doc.get(doc_id),
            "targets": targets_by_doc.get(doc_id, []),
            "narrative": narrative_by_doc.get(doc_id),
        }
    return result


def _rich_sentiment_dict(row: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "label": row["label"],
        "evidence_spans": row["evidence_spans"] or [],
        "sarcasm_detected": row["sarcasm_detected"],
        "confidence": row["confidence"],
        "reasoning": row["reasoning"],
    }


def _rich_x_engagement_dict(row: Mapping[str, Any]) -> Dict[str, Any]:
    return {
        "retweets": row["retweet_count"],
        "replies": row["reply_count"],
        "likes": row["like_count"],
        "quotes": row["quote_count"],
    }


def _rich_reddit_engagement_dict(row: Mapping[str, Any]) -> Dict[str, Any]:
    return {"score": row["score"], "num_comments": row["num_comments"]}


def _rich_author_dict(row: Mapping[str, Any]) -> Optional[Dict[str, Any]]:
    """None when the doc has no author_id (news, reddit -- Reddit stores
    no author profile at ingest, and we never fabricate one)."""
    if row["author_id"] is None:
        return None
    return {
        "handle": row["author_handle"],
        "display_name": row["author_display_name"],
        "profile_image_url": row["author_profile_image_url"],
        "verified_type": row["author_verified_type"],
        "followers_count": row["author_followers_count"],
        "account_created_at": row["author_account_created_at"],
        "description": row["author_description"],
    }


def build_classification_sample(doc_id: int, fields: Mapping[str, Any]) -> ClassificationSampleModel:
    """Assemble one ClassificationSampleModel from `fetch_rich_sample_fields()`'s
    per-doc dict. Raises ValueError (fail loud, per invariant C1) if the doc
    has no current sentiment run -- a classification sample without a
    label/confidence is not a valid sample."""
    sentiment = fields.get("sentiment")
    if sentiment is None:
        raise ValueError(
            f"doc_id={doc_id} has no current done sentiment run -- cannot build a "
            "classification sample with no label/confidence"
        )
    source_type = fields["source_type"]
    author = fields.get("author")
    source_name = (
        author["handle"] if source_type == "x_post" and author else fields.get("domain_or_subreddit")
    )
    engagement = fields.get("engagement")
    return ClassificationSampleModel(
        doc_id=doc_id,
        label=sentiment["label"],
        confidence=sentiment["confidence"],
        reasoning=sentiment.get("reasoning"),
        evidence_spans=list(sentiment.get("evidence_spans") or [])[:MAX_EVIDENCE_PER_SAMPLE],
        sarcasm_detected=sentiment.get("sarcasm_detected"),
        title=_snippet(fields.get("title")),
        source_type=source_type,
        source_name=source_name,
        date=fields["published_at"].isoformat() if fields.get("published_at") else None,
        full_text=fields.get("body"),
        url=fields.get("source_url"),
        engagement=engagement,
        author=author,
        targets=fields.get("targets") or None,
        narrative=fields.get("narrative"),
    )


def _snippet(text: Optional[str], limit: int = SNIPPET_MAX_CHARS) -> Optional[str]:
    if not text:
        return None
    return text[:limit]
