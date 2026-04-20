"""
Narrative clusterer for Civic Lens.

Reads claims written by the claim-extraction step (``ai_outputs`` rows with
``task_type='claims'``) and groups them into narratives. Two similarity
modes are supported, controlled by ``CIVIC_NARRATIVE_SIMILARITY_MODE``:

  - ``"jaccard"`` (default): lexical Jaccard over stop-word-filtered tokens.
    Fast, deterministic, no external dependency. Splits on synonymy.
  - ``"embedding"``: cosine similarity over Ollama-generated embeddings.
    Catches synonymy ("Trump won PA" ≈ "Trump victory in Pennsylvania").
    Falls back to jaccard automatically if the embedding client returns
    None for the input claim.

Both modes anchor each narrative on its first-seen claim — new claims are
compared to the anchor (token set or stored embedding), not to a drifting
centroid, so narrative identity is stable across runs.
"""

import json
import math
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

# Default thresholds; overridable via constructor.
JACCARD_THRESHOLD = 0.3
EMBEDDING_THRESHOLD = 0.65

# How far back to look for claims when clustering.
CLAIM_LOOKBACK_SECONDS = 30 * 24 * 60 * 60  # 30 days


def tokenize_claim(text: str) -> Set[str]:
    """Lowercase, split, drop stopwords — the only canonicalization Jaccard uses."""
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


def cosine(a: List[float], b: List[float]) -> float:
    if not a or not b or len(a) != len(b):
        return 0.0
    dot = sum(x * y for x, y in zip(a, b))
    norm_a = math.sqrt(sum(x * x for x in a))
    norm_b = math.sqrt(sum(y * y for y in b))
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return dot / (norm_a * norm_b)


@dataclass
class _NarrativeAnchor:
    narrative_id: int
    name: str
    anchor_text: str
    anchor_tokens: Set[str]
    # Lazy-loaded; None means we haven't fetched/computed it yet.
    anchor_embedding: Optional[List[float]] = None


@dataclass
class _PendingClaim:
    ai_output_id: int
    doc_id: int
    claim_text: str
    confidence: float
    tokens: Set[str]
    created_at: int


class NarrativeClusterer:
    """Cluster recent claims into narratives.

    The ``embedding_client`` parameter is optional: when None (or when the
    client returns None for a given input), the clusterer transparently
    falls back to Jaccard mode for that claim. Storage schema is identical
    in both modes — only the comparator differs.
    """

    def __init__(
        self,
        db_path: str,
        mode: str = "jaccard",
        embedding_client=None,
        embedding_model: Optional[str] = None,
        jaccard_threshold: float = JACCARD_THRESHOLD,
        embedding_threshold: float = EMBEDDING_THRESHOLD,
    ):
        self.db_path = db_path
        self.mode = mode if mode in ("jaccard", "embedding") else "jaccard"
        self.embedding_client = embedding_client if self.mode == "embedding" else None
        self.embedding_model = embedding_model
        self.jaccard_threshold = jaccard_threshold
        self.embedding_threshold = embedding_threshold

    def run(self) -> Dict[str, int]:
        """Process all claims not yet assigned to a narrative.

        Returns ``{"claims_considered", "narratives_created", "assignments", "mode"}``.
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
            embedding_fallbacks = 0
            now = int(time.time())

            # In embedding mode, materialize missing anchor embeddings once up
            # front so `_best_match` doesn't re-embed the same anchors per
            # pending claim (audit 2026-04-19 §8).
            if self.mode == "embedding" and self.embedding_client is not None:
                self._warm_anchor_embeddings(anchors, cursor)

            for pc in pending:
                if not pc.tokens:
                    continue

                claim_embedding = self._embed_if_needed(pc.claim_text)
                if self.mode == "embedding" and claim_embedding is None:
                    embedding_fallbacks += 1

                best_id, best_sim = self._best_match(pc, claim_embedding, anchors, cursor)

                threshold = (
                    self.embedding_threshold
                    if (self.mode == "embedding" and claim_embedding is not None)
                    else self.jaccard_threshold
                )

                if best_sim >= threshold and best_id is not None:
                    narrative_id = best_id
                else:
                    narrative_id = self._create_narrative(cursor, pc, claim_embedding, now)
                    anchors[narrative_id] = _NarrativeAnchor(
                        narrative_id=narrative_id,
                        name=pc.claim_text,
                        anchor_text=pc.claim_text,
                        anchor_tokens=pc.tokens,
                        anchor_embedding=claim_embedding,
                    )
                    narratives_created += 1

                if self._assign_doc(cursor, narrative_id, pc, now):
                    assignments += 1

                conn.commit()

            logger.info(
                f"Narrative clustering [{self.mode}]: considered {len(pending)} claims, "
                f"created {narratives_created} new narratives, wrote {assignments} assignments"
                + (f" (embedding fallbacks: {embedding_fallbacks})" if embedding_fallbacks else "")
            )
            return {
                "claims_considered": len(pending),
                "narratives_created": narratives_created,
                "assignments": assignments,
                "mode": self.mode,
                "embedding_fallbacks": embedding_fallbacks,
            }
        finally:
            conn.close()

    def _embed_if_needed(self, text: str) -> Optional[List[float]]:
        if self.mode != "embedding" or self.embedding_client is None:
            return None
        return self.embedding_client.embed(text, model=self.embedding_model)

    def _best_match(
        self,
        pc: _PendingClaim,
        claim_embedding: Optional[List[float]],
        anchors: Dict[int, _NarrativeAnchor],
        cursor: sqlite3.Cursor,
    ) -> Tuple[Optional[int], float]:
        best_id: Optional[int] = None
        best_sim = 0.0

        use_embedding = self.mode == "embedding" and claim_embedding is not None

        for anchor in anchors.values():
            if use_embedding:
                anchor_emb = self._anchor_embedding(anchor, cursor)
                if anchor_emb is None:
                    # Anchor has no embedding yet (created under jaccard or first-time).
                    # Skip — comparing across modes mixes thresholds badly.
                    continue
                sim = cosine(claim_embedding, anchor_emb)
            else:
                sim = jaccard(pc.tokens, anchor.anchor_tokens)

            if sim > best_sim:
                best_sim = sim
                best_id = anchor.narrative_id
        return best_id, best_sim

    def _warm_anchor_embeddings(
        self, anchors: Dict[int, "_NarrativeAnchor"], cursor: sqlite3.Cursor,
    ) -> None:
        """Populate anchor_embedding for every anchor missing one, once per run.

        The old flow embedded each anchor lazily inside the per-claim inner
        loop; with N pending claims and M anchors missing embeddings this
        was up to N lookups each. Materializing once makes the inner loop
        O(N·M) cosine only, not O(N·M) embedding calls.
        """
        for anchor in anchors.values():
            if anchor.anchor_embedding is not None:
                continue
            embedding = self.embedding_client.embed(anchor.anchor_text, model=self.embedding_model)
            if embedding is None:
                continue
            anchor.anchor_embedding = embedding
            cursor.execute(
                "UPDATE narratives SET anchor_embedding_json = ? WHERE narrative_id = ?",
                (json.dumps(embedding), anchor.narrative_id),
            )

    def _anchor_embedding(
        self, anchor: _NarrativeAnchor, cursor: sqlite3.Cursor,
    ) -> Optional[List[float]]:
        if anchor.anchor_embedding is not None:
            return anchor.anchor_embedding
        # Try to embed-and-store now so future runs hit the cache.
        if self.embedding_client is None:
            return None
        embedding = self.embedding_client.embed(anchor.anchor_text, model=self.embedding_model)
        if embedding is None:
            return None
        anchor.anchor_embedding = embedding
        cursor.execute(
            "UPDATE narratives SET anchor_embedding_json = ? WHERE narrative_id = ?",
            (json.dumps(embedding), anchor.narrative_id),
        )
        return embedding

    def _load_pending_claims(self, cursor: sqlite3.Cursor, cutoff: int) -> List[_PendingClaim]:
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
            for raw in payload.get("claims") or []:
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
        cursor.execute(
            "SELECT narrative_id, name, description, anchor_embedding_json FROM narratives"
        )
        anchors: Dict[int, _NarrativeAnchor] = {}
        for narrative_id, name, description, embed_json in cursor.fetchall():
            anchor_text = description or name or ""
            embedding: Optional[List[float]] = None
            if embed_json:
                try:
                    parsed = json.loads(embed_json)
                    if isinstance(parsed, list):
                        embedding = parsed
                except json.JSONDecodeError:
                    pass
            anchors[narrative_id] = _NarrativeAnchor(
                narrative_id=narrative_id,
                name=name or "",
                anchor_text=anchor_text,
                anchor_tokens=tokenize_claim(anchor_text),
                anchor_embedding=embedding,
            )
        return anchors

    def _create_narrative(
        self,
        cursor: sqlite3.Cursor,
        pc: _PendingClaim,
        embedding: Optional[List[float]],
        now: int,
    ) -> int:
        # Record the clustering mode + threshold + embedding model used to
        # create this anchor so a later threshold change doesn't silently
        # rewrite the semantics of old narratives (audit 2026-04-19 §8).
        effective_mode = "embedding" if (self.mode == "embedding" and embedding is not None) else "jaccard"
        threshold = (
            self.embedding_threshold if effective_mode == "embedding"
            else self.jaccard_threshold
        )
        cursor.execute(
            """
            INSERT INTO narratives
                (name, description, first_seen_at, first_seen_doc_id, created_at, updated_at,
                 anchor_embedding_json, clustering_mode, clustering_threshold, embedding_model)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                pc.claim_text[:120],
                pc.claim_text,
                pc.created_at or now,
                pc.doc_id,
                now,
                now,
                json.dumps(embedding) if embedding is not None else None,
                effective_mode,
                threshold,
                self.embedding_model if effective_mode == "embedding" else None,
            ),
        )
        return cursor.lastrowid

    def _assign_doc(
        self, cursor: sqlite3.Cursor, narrative_id: int, pc: _PendingClaim, now: int,
    ) -> bool:
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
            return False
