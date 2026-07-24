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
from typing import Any, Dict, Iterable, Mapping, Optional, Tuple

from analysis.src.api.queries.constants import WINDOWS

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
