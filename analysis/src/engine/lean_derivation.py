"""
Deterministic political-lean derivation (Postgres redesign Phase 7B): pools
directional favorability_stances/target_mentions evidence per author and
per narrative into analysis.author_leans/narrative_leans (full rebuild every invocation).
"""

from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Dict, List, Optional, Set, Tuple

from analysis.src.common import db
from analysis.src.common.logger import get_logger
from analysis.src.engine.constants import (
    LEAN_CONFIDENCE_SATURATION_SAMPLES,
    LEAN_MIN_SAMPLE_COUNT,
    LEAN_SHARE_THRESHOLD,
)

logger = get_logger(__name__)

# Directional-only: neutral/mixed stance rows and non-partisan curated leans
# (independent/mixed/unknown) carry no signal and are excluded here in SQL,
# not in Python -- this is plain filtering, not the signal-direction logic
# itself (that stays in _signal_for so it's unit-testable without a
# database). Restricted to analysis.runs.is_current AND status='done' so a
# stale or failed run's stances never enter derivation. target_mentions
# carries its own doc_id (used directly); favorability_stances has none, so
# its doc_id comes from joining analysis.runs (that table is doc-scoped for
# the unified text engine that writes it).
_SELECT_DIRECTIONAL_EVIDENCE_SQL = """
    SELECT d.doc_id AS doc_id, d.author_id AS author_id,
           e.lean::text AS entity_lean, fs.stance::text AS stance
    FROM analysis.favorability_stances fs
    JOIN analysis.runs r ON r.run_id = fs.run_id
    JOIN corpus.entities e ON e.entity_id = fs.entity_id
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    WHERE r.is_current AND r.status = 'done'::analysis.run_status
      AND e.lean IN ('democrat'::corpus.political_lean, 'republican'::corpus.political_lean)
      AND fs.stance IN ('favorable'::analysis.favorability_label, 'unfavorable'::analysis.favorability_label)

    UNION ALL

    SELECT tm.doc_id AS doc_id, d.author_id AS author_id,
           e.lean::text AS entity_lean, tm.stance::text AS stance
    FROM analysis.target_mentions tm
    JOIN analysis.runs r ON r.run_id = tm.run_id
    JOIN corpus.entities e ON e.entity_id = tm.entity_id
    JOIN corpus.documents d ON d.doc_id = tm.doc_id
    WHERE r.is_current AND r.status = 'done'::analysis.run_status
      AND tm.entity_id IS NOT NULL
      AND e.lean IN ('democrat'::corpus.political_lean, 'republican'::corpus.political_lean)
      AND tm.stance IN ('positive'::analysis.sentiment_label, 'negative'::analysis.sentiment_label)
"""

_SELECT_NARRATIVE_DOCS_SQL = """
    SELECT narrative_id, doc_id FROM analysis.narrative_docs
"""

_DELETE_AUTHOR_LEANS_SQL = "DELETE FROM analysis.author_leans"

_INSERT_AUTHOR_LEAN_SQL = """
    INSERT INTO analysis.author_leans
        (author_id, lean, lean_share, lean_confidence, stance_sample_count, computed_at)
    VALUES (%(author_id)s, %(lean)s::corpus.political_lean, %(lean_share)s,
            %(lean_confidence)s, %(stance_sample_count)s, %(computed_at)s)
"""

_DELETE_NARRATIVE_LEANS_SQL = "DELETE FROM analysis.narrative_leans"

_INSERT_NARRATIVE_LEAN_SQL = """
    INSERT INTO analysis.narrative_leans
        (narrative_id, lean, lean_share, confidence, doc_count, computed_at)
    VALUES (%(narrative_id)s, %(lean)s::corpus.political_lean, %(lean_share)s,
            %(confidence)s, %(doc_count)s, %(computed_at)s)
"""

# Stance values from the two source enums that read as "favorable"/
# "unfavorable" toward the entity they're attached to -- favorability_label
# and sentiment_label use different words for the same two directions.
_FAVORABLE_STANCES = frozenset({"favorable", "positive"})
_UNFAVORABLE_STANCES = frozenset({"unfavorable", "negative"})


@dataclass(frozen=True)
class LeanDerivationResult:
    """Rows actually written this pass. A subject with zero directional
    samples gets no row in either table (not a zero-evidence placeholder)."""

    authors_written: int
    narratives_written: int


def _signal_for(entity_lean: str, stance: str) -> Optional[str]:
    """Explicit signal mapping (pinned design): favorable-or-positive toward
    a democrat entity, OR unfavorable-or-negative toward a republican entity,
    is one pro-democrat sample -- the mirror is one pro-republican sample.
    Returns the political_lean value the sample supports, or None for a
    non-directional stance (neutral/mixed) or a non-partisan curated lean
    (independent/mixed/unknown), so callers can filter with `is None`."""
    if entity_lean not in ("democrat", "republican"):
        return None
    if stance in _FAVORABLE_STANCES:
        return entity_lean
    if stance in _UNFAVORABLE_STANCES:
        return "republican" if entity_lean == "democrat" else "democrat"
    return None


def _index_narrative_docs(rows: List[Dict[str, Any]]) -> Dict[int, List[int]]:
    """Pure: doc_id -> every narrative_id it belongs to (a doc can carry
    claims clustered into more than one narrative)."""
    index: Dict[int, List[int]] = {}
    for row in rows:
        index.setdefault(row["doc_id"], []).append(row["narrative_id"])
    return index


def _tally_author_evidence(rows: List[Dict[str, Any]]) -> Dict[int, Counter]:
    """Pure: groups directional samples by author_id. Rows with no
    author_id (e.g. a news doc with no linked byline) are skipped -- they
    still count toward any narrative they belong to, just not an author."""
    tallies: Dict[int, Counter] = {}
    for row in rows:
        author_id = row["author_id"]
        if author_id is None:
            continue
        signal = _signal_for(row["entity_lean"], row["stance"])
        if signal is None:
            continue
        tallies.setdefault(author_id, Counter())[signal] += 1
    return tallies


def _tally_narrative_evidence(
    rows: List[Dict[str, Any]], doc_to_narratives: Dict[int, List[int]]
) -> Tuple[Dict[int, Counter], Dict[int, Set[int]]]:
    """Pure: groups directional samples by narrative via narrative_docs
    membership, fanning a doc's evidence out to every narrative it belongs
    to. Returns the sample tally alongside the set of DISTINCT contributing
    docs per narrative -- doc_count is that set's size, kept separate from
    the raw sample tally since one doc can carry several directional
    samples."""
    tallies: Dict[int, Counter] = {}
    contributing_docs: Dict[int, Set[int]] = {}
    for row in rows:
        signal = _signal_for(row["entity_lean"], row["stance"])
        if signal is None:
            continue
        for narrative_id in doc_to_narratives.get(row["doc_id"], ()):
            tallies.setdefault(narrative_id, Counter())[signal] += 1
            contributing_docs.setdefault(narrative_id, set()).add(row["doc_id"])
    return tallies, contributing_docs


def _derive_lean(pro_democrat: int, pro_republican: int) -> Tuple[str, float, float]:
    """The three-way labeling gate (LEAN_* constants in engine/constants.py).
    Below LEAN_MIN_SAMPLE_COUNT total samples -> 'unknown' (insufficient
    evidence). At/above it with majority share under LEAN_SHARE_THRESHOLD ->
    'mixed' (balanced evidence is itself a finding). At/above both -> the
    majority side. `lean_share` is always the true majority-side share
    regardless of gate outcome, so the row stays auditable even when
    'unknown'/'mixed'. `confidence` scales share down for thin evidence,
    saturating (no further increase) once samples reach
    LEAN_CONFIDENCE_SATURATION_SAMPLES."""
    total = pro_democrat + pro_republican
    majority = max(pro_democrat, pro_republican)
    lean_share = majority / total
    if total < LEAN_MIN_SAMPLE_COUNT:
        lean = "unknown"
    elif lean_share >= LEAN_SHARE_THRESHOLD:
        lean = "democrat" if pro_democrat >= pro_republican else "republican"
    else:
        lean = "mixed"
    confidence = lean_share * min(1.0, total / LEAN_CONFIDENCE_SATURATION_SAMPLES)
    return lean, lean_share, confidence


def _author_lean_row(author_id: int, tally: Counter, computed_at: datetime) -> Dict[str, Any]:
    pro_democrat = tally.get("democrat", 0)
    pro_republican = tally.get("republican", 0)
    lean, lean_share, confidence = _derive_lean(pro_democrat, pro_republican)
    return {
        "author_id": author_id,
        "lean": lean,
        "lean_share": lean_share,
        "lean_confidence": confidence,
        "stance_sample_count": pro_democrat + pro_republican,
        "computed_at": computed_at,
    }


def _narrative_lean_row(
    narrative_id: int, tally: Counter, doc_count: int, computed_at: datetime
) -> Dict[str, Any]:
    pro_democrat = tally.get("democrat", 0)
    pro_republican = tally.get("republican", 0)
    lean, lean_share, confidence = _derive_lean(pro_democrat, pro_republican)
    return {
        "narrative_id": narrative_id,
        "lean": lean,
        "lean_share": lean_share,
        "confidence": confidence,
        "doc_count": doc_count,
        "computed_at": computed_at,
    }


def run(now: Optional[datetime] = None) -> LeanDerivationResult:
    """Full rebuild of analysis.author_leans/narrative_leans from current-run
    stance evidence. Reads once, then DELETEs + INSERTs each table inside
    its own transaction (via common/db.connection()) so readers never see a
    half-built table. `now` pins `computed_at` for every row this pass, for
    testability -- defaults to the real current time."""
    computed_at = now if now is not None else datetime.now(timezone.utc)

    with db.connection() as conn:
        evidence_rows = conn.execute(_SELECT_DIRECTIONAL_EVIDENCE_SQL).fetchall()
        narrative_doc_rows = conn.execute(_SELECT_NARRATIVE_DOCS_SQL).fetchall()

    doc_to_narratives = _index_narrative_docs(narrative_doc_rows)
    author_tallies = _tally_author_evidence(evidence_rows)
    narrative_tallies, narrative_doc_sets = _tally_narrative_evidence(evidence_rows, doc_to_narratives)

    author_rows = [
        _author_lean_row(author_id, tally, computed_at)
        for author_id, tally in author_tallies.items()
    ]
    narrative_rows = [
        _narrative_lean_row(narrative_id, tally, len(narrative_doc_sets[narrative_id]), computed_at)
        for narrative_id, tally in narrative_tallies.items()
    ]

    with db.connection() as conn:
        conn.execute(_DELETE_AUTHOR_LEANS_SQL)
        for row in author_rows:
            conn.execute(_INSERT_AUTHOR_LEAN_SQL, row)

    with db.connection() as conn:
        conn.execute(_DELETE_NARRATIVE_LEANS_SQL)
        for row in narrative_rows:
            conn.execute(_INSERT_NARRATIVE_LEAN_SQL, row)

    logger.info(
        f"lean_derivation: authors_written={len(author_rows)}, "
        f"narratives_written={len(narrative_rows)}"
    )
    return LeanDerivationResult(
        authors_written=len(author_rows), narratives_written=len(narrative_rows)
    )
