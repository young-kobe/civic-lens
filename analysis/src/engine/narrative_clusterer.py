"""
Narrative clusterer for Civic Lens.

Reads extracted claims from ``ai_outputs`` (task_type='claims') and groups
them into narratives using Jaccard similarity over stop-word-filtered tokens.
Each narrative is anchored by its first-seen claim — new claims are compared
to the anchor's token set, not a drifting centroid, so narrative identity
stays stable across runs.

Writers:
  - ``narratives``        — one row per distinct narrative.
  - ``narrative_docs``    — one row per (narrative, supporting doc) pair.
"""

import json
import re
import sqlite3
import time
from dataclasses import dataclass
from typing import Dict, List, Optional, Set, Tuple

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


# Conservative stop-word list — anything that adds noise to Jaccard on short claims.
_STOPWORDS: Set[str] = {
    "a", "an", "the", "and", "or", "but", "if", "of", "in", "on", "at", "by",
    "to", "for", "from", "with", "as", "is", "are", "was", "were", "be", "been",
    "being", "it", "its", "this", "that", "these", "those", "i", "you", "he",
    "she", "we", "they", "them", "his", "her", "their", "our", "my", "your",
    "not", "no", "do", "does", "did", "have", "has", "had", "will", "would",
    "could", "should", "can", "may", "might", "must", "just", "so", "than",
    "then", "there", "about", "into", "over", "after", "before", "more",
    "most", "some", "any", "all", "such", "up", "down", "out",
}

_TOKEN_RE = re.compile(r"[a-z0-9]+")

# Minimum Jaccard similarity for a new claim to join an existing narrative.
MATCH_THRESHOLD = 0.3

# How far back to look for claims when clustering. Narratives older than this
# window are still writable as targets, but we won't re-process their claims.
CLAIM_LOOKBACK_SECONDS = 30 * 24 * 60 * 60  # 30 days


def tokenize_claim(text: str) -> Set[str]:
    """Lowercase, split, drop stopwords — the only canonicalization we apply."""
    if not text:
        return set()
    return {t for t in _TOKEN_RE.findall(text.lower()) if t not in _STOPWORDS and len(t) > 1}


def jaccard(a: Set[str], b: Set[str]) -> float:
    if not a or not b:
        return 0.0
    inter = len(a & b)
    if inter == 0:
        return 0.0
    return inter / len(a | b)


@dataclass
class _NarrativeAnchor:
    narrative_id: int
    name: str
    anchor_tokens: Set[str]


@dataclass
class _PendingClaim:
    ai_output_id: int
    doc_id: int
    claim_text: str
    confidence: float
    tokens: Set[str]
    created_at: int


class NarrativeClusterer:
    """Cluster recent claims into narratives."""

    def __init__(self, db_path: str, match_threshold: float = MATCH_THRESHOLD):
        self.db_path = db_path
        self.match_threshold = match_threshold

    def run(self) -> Dict[str, int]:
        """Process all claims not yet assigned to a narrative.

        Returns counts: ``{"claims_considered", "narratives_created", "assignments"}``.
        """
        cutoff = int(time.time()) - CLAIM_LOOKBACK_SECONDS

        conn = sqlite3.connect(self.db_path)
        conn.execute("PRAGMA busy_timeout = 5000")
        conn.execute("PRAGMA journal_mode = WAL")
        try:
            cursor = conn.cursor()
            pending = self._load_pending_claims(cursor, cutoff)
            anchors = self._load_existing_narratives(cursor)

            narratives_created = 0
            assignments = 0
            now = int(time.time())

            for pc in pending:
                if not pc.tokens:
                    continue
                best_id, best_sim = self._best_match(pc.tokens, anchors)

                if best_sim >= self.match_threshold and best_id is not None:
                    narrative_id = best_id
                else:
                    narrative_id = self._create_narrative(cursor, pc, now)
                    anchors[narrative_id] = _NarrativeAnchor(
                        narrative_id=narrative_id,
                        name=pc.claim_text,
                        anchor_tokens=pc.tokens,
                    )
                    narratives_created += 1

                if self._assign_doc(cursor, narrative_id, pc, now):
                    assignments += 1

                conn.commit()

            logger.info(
                f"Narrative clustering: considered {len(pending)} claims, "
                f"created {narratives_created} new narratives, wrote {assignments} assignments"
            )
            return {
                "claims_considered": len(pending),
                "narratives_created": narratives_created,
                "assignments": assignments,
            }
        finally:
            conn.close()

    def _load_pending_claims(self, cursor: sqlite3.Cursor, cutoff: int) -> List[_PendingClaim]:
        """Claims whose doc has no narrative_docs row yet, within the lookback window."""
        cursor.execute(
            """
            SELECT a.output_id, a.doc_id, a.output_json, a.confidence, a.created_at
            FROM ai_outputs a
            LEFT JOIN narrative_docs n ON n.doc_id = a.doc_id
            WHERE a.task_type = 'claims'
              AND a.created_at >= ?
              AND n.doc_id IS NULL
            ORDER BY a.created_at ASC
            """,
            (cutoff,),
        )
        rows = cursor.fetchall()

        pending: List[_PendingClaim] = []
        for output_id, doc_id, output_json, conf, created_at in rows:
            try:
                payload = json.loads(output_json) if output_json else {}
            except json.JSONDecodeError:
                continue
            claims = payload.get("claims") or []
            # One doc can contribute multiple claims; each is clustered independently
            # but the doc is only assigned once (to the strongest match's narrative).
            for raw in claims:
                if not isinstance(raw, dict):
                    continue
                claim_text = (raw.get("claim") or "").strip()
                if not claim_text:
                    continue
                try:
                    claim_conf = float(raw.get("confidence", conf or 0.5))
                except (TypeError, ValueError):
                    claim_conf = conf or 0.5
                pending.append(_PendingClaim(
                    ai_output_id=output_id,
                    doc_id=doc_id,
                    claim_text=claim_text,
                    confidence=claim_conf,
                    tokens=tokenize_claim(claim_text),
                    created_at=created_at or 0,
                ))
        return pending

    def _load_existing_narratives(self, cursor: sqlite3.Cursor) -> Dict[int, _NarrativeAnchor]:
        cursor.execute("SELECT narrative_id, name, description FROM narratives")
        anchors: Dict[int, _NarrativeAnchor] = {}
        for narrative_id, name, description in cursor.fetchall():
            anchor_text = description or name or ""
            anchors[narrative_id] = _NarrativeAnchor(
                narrative_id=narrative_id,
                name=name or "",
                anchor_tokens=tokenize_claim(anchor_text),
            )
        return anchors

    def _best_match(
        self, tokens: Set[str], anchors: Dict[int, _NarrativeAnchor],
    ) -> Tuple[Optional[int], float]:
        best_id: Optional[int] = None
        best_sim = 0.0
        for anchor in anchors.values():
            sim = jaccard(tokens, anchor.anchor_tokens)
            if sim > best_sim:
                best_sim = sim
                best_id = anchor.narrative_id
        return best_id, best_sim

    def _create_narrative(self, cursor: sqlite3.Cursor, pc: _PendingClaim, now: int) -> int:
        cursor.execute(
            """
            INSERT INTO narratives
                (name, description, first_seen_at, origin_doc_id, created_at, updated_at)
            VALUES (?, ?, ?, ?, ?, ?)
            """,
            (pc.claim_text[:120], pc.claim_text, pc.created_at or now, pc.doc_id, now, now),
        )
        return cursor.lastrowid

    def _assign_doc(
        self, cursor: sqlite3.Cursor, narrative_id: int, pc: _PendingClaim, now: int,
    ) -> bool:
        """Upsert a narrative_docs row. Returns True if a new row was written."""
        try:
            cursor.execute(
                """
                INSERT INTO narrative_docs
                    (narrative_id, doc_id, discovered_at, confidence)
                VALUES (?, ?, ?, ?)
                """,
                (narrative_id, pc.doc_id, now, pc.confidence),
            )
            return True
        except sqlite3.IntegrityError:
            # UNIQUE(narrative_id, doc_id) already exists — a second claim from
            # the same doc landed in the same narrative. Leave the first.
            return False
