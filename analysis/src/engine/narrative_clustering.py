"""
Narrative clusterer (Postgres redesign Phase 6, Wave 3): groups current
`analysis.claims` into `analysis.narratives` (jaccard/embedding comparator,
deferred-materialization fragmentation fix) -- see docs/DATABASE_SCHEMA.md's "Narratives" section.
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from typing import Callable, Dict, List, Optional, Set, Tuple

from analysis.src.common import db
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine.constants import CLAIM_LOOKBACK_DAYS, MIN_NARRATIVE_SUPPORT, NARRATIVE_NAME_MAX_CHARS

logger = get_logger(__name__)

# =============================================================================
# MIN_NARRATIVE_SUPPORT/CLAIM_LOOKBACK_DAYS/NARRATIVE_NAME_MAX_CHARS
# consolidated into engine/constants.py (Postgres redesign Phase 6,
# constants-consolidation pass, 2026-07-23) -- imported above.
#
# MIN_NARRATIVE_SUPPORT is the fragmentation fix: a narrative persists only
# once >= this many DISTINCT docs support it. Below this, matched claims
# stay in memory for the run but are never written -- their docs remain
# absent from narrative_docs and are simply reconsidered (against a
# by-then-larger anchor pool) on the next run.
# =============================================================================

# A pre-embedded claim/anchor whose embed() call is injected by the caller
# returns this signature; None means "no vector" (never fabricated).
EmbedFn = Callable[[str], Optional[List[float]]]

# Ported verbatim from the old engine/narrative_clusterer.py -- the only
# canonicalization jaccard mode uses.
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


def tokenize_claim(text: str) -> Set[str]:
    """Lowercase, split, drop stopwords -- the only canonicalization jaccard
    mode uses."""
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


def cosine(a: Optional[List[float]], b: Optional[List[float]]) -> float:
    if not a or not b or len(a) != len(b):
        return 0.0
    dot = sum(x * y for x, y in zip(a, b))
    norm_a = math.sqrt(sum(x * x for x in a))
    norm_b = math.sqrt(sum(y * y for y in b))
    if norm_a == 0 or norm_b == 0:
        return 0.0
    return dot / (norm_a * norm_b)


# =============================================================================
# Pure data model -- inputs to plan_clustering() carry pre-computed
# tokens/embeddings; the planner itself does no I/O.
# =============================================================================

@dataclass
class PendingClaim:
    """One claim not yet linked to any narrative (its doc has no
    narrative_docs row). `embedding` is filled in by `_embed_pending_claims`
    when mode == "embedding"; stays None on failure (counted, not
    fabricated) or in jaccard mode."""

    claim_id: int
    doc_id: int
    claim_text: str
    confidence: Optional[float]
    published_at: datetime
    tokens: Set[str] = field(default_factory=set)
    embedding: Optional[List[float]] = None


@dataclass
class ExistingAnchor:
    """One narrative already in the DB, as seen by the matching loop.
    `embedding` is mutated in place by `_warm_anchor_embeddings` the first
    time a run needs it and the narrative doesn't have one yet."""

    narrative_id: int
    anchor_text: str
    tokens: Set[str]
    embedding: Optional[List[float]]
    first_seen_at: datetime
    first_seen_doc_id: int


@dataclass(frozen=True)
class ExistingAssignment:
    narrative_id: int
    claim: PendingClaim
    similarity: float


@dataclass(frozen=True)
class NewNarrativePlan:
    """A brand-new narrative that reached MIN_NARRATIVE_SUPPORT this run.
    `anchor_claim` is `members[0]` -- the founding claim, per the "anchor
    never drifts" identity rule ported from the old clusterer."""

    anchor_claim: PendingClaim
    embedding: Optional[List[float]]
    members: List[PendingClaim]


@dataclass(frozen=True)
class ClusterPlan:
    existing_assignments: List[ExistingAssignment]
    new_narratives: List[NewNarrativePlan]
    suppressed_count: int  # provisional groups that never reached MIN_NARRATIVE_SUPPORT
    suppressed_claims: int  # underlying claims left unclustered as a result


@dataclass
class _Provisional:
    members: List[PendingClaim]


# =============================================================================
# Pure planning core.
# =============================================================================

def plan_clustering(
    pending: List[PendingClaim],
    existing_anchors: List[ExistingAnchor],
    *,
    mode: str,
    jaccard_threshold: float,
    embedding_threshold: float,
    min_support: int = MIN_NARRATIVE_SUPPORT,
) -> ClusterPlan:
    """Decide, for each pending claim, whether it joins an existing
    narrative, joins/founds a provisional (in-run, not-yet-persisted) group,
    or ends up alone. No DB, no network -- `pending`/`existing_anchors`
    already carry whatever tokens/embeddings the comparator needs.

    Per-claim mode fallback matches the old clusterer exactly: a claim uses
    embedding comparison only if `mode == "embedding"` AND its own embedding
    succeeded; otherwise it compares by jaccard against every anchor
    (skipping none) -- comparing jaccard tokens against an anchor that only
    has an embedding, or vice versa, mixes thresholds and is never done.
    """
    existing_by_id: Dict[int, ExistingAnchor] = {a.narrative_id: a for a in existing_anchors}
    provisional: Dict[int, _Provisional] = {}
    next_temp_id = 0

    existing_assignments: List[ExistingAssignment] = []

    claims_by_doc: Dict[int, List[PendingClaim]] = {}
    for pc in pending:
        if pc.tokens:  # empty-token claims (stopwords-only) can never match
            claims_by_doc.setdefault(pc.doc_id, []).append(pc)

    for doc_claims in claims_by_doc.values():
        for pc in doc_claims:
            use_embedding = mode == "embedding" and pc.embedding is not None
            threshold = embedding_threshold if use_embedding else jaccard_threshold

            kind, key, similarity = _best_match(pc, use_embedding, existing_by_id, provisional)

            if key is not None and similarity >= threshold:
                if kind == "existing":
                    existing_assignments.append(
                        ExistingAssignment(narrative_id=key, claim=pc, similarity=similarity)
                    )
                else:
                    provisional[key].members.append(pc)
            else:
                next_temp_id -= 1
                provisional[next_temp_id] = _Provisional(members=[pc])

    new_narratives: List[NewNarrativePlan] = []
    suppressed_count = 0
    suppressed_claims = 0
    for prov in provisional.values():
        distinct_docs = {m.doc_id for m in prov.members}
        if len(distinct_docs) >= min_support:
            anchor_claim = prov.members[0]
            new_narratives.append(
                NewNarrativePlan(
                    anchor_claim=anchor_claim,
                    embedding=anchor_claim.embedding,
                    members=list(prov.members),
                )
            )
        else:
            suppressed_count += 1
            suppressed_claims += len(prov.members)

    return ClusterPlan(
        existing_assignments=existing_assignments,
        new_narratives=new_narratives,
        suppressed_count=suppressed_count,
        suppressed_claims=suppressed_claims,
    )


def _best_match(
    pc: PendingClaim,
    use_embedding: bool,
    existing_by_id: Dict[int, ExistingAnchor],
    provisional: Dict[int, _Provisional],
) -> Tuple[Optional[str], Optional[int], float]:
    best_kind: Optional[str] = None
    best_key: Optional[int] = None
    best_sim = 0.0

    for narrative_id, anchor in existing_by_id.items():
        if use_embedding:
            if anchor.embedding is None:
                continue
            sim = cosine(pc.embedding, anchor.embedding)
        else:
            sim = jaccard(pc.tokens, anchor.tokens)
        if sim > best_sim:
            best_sim, best_kind, best_key = sim, "existing", narrative_id

    for temp_id, prov in provisional.items():
        anchor_claim = prov.members[0]
        if use_embedding:
            if anchor_claim.embedding is None:
                continue
            sim = cosine(pc.embedding, anchor_claim.embedding)
        else:
            sim = jaccard(pc.tokens, anchor_claim.tokens)
        if sim > best_sim:
            best_sim, best_kind, best_key = sim, "provisional", temp_id

    return best_kind, best_key, best_sim


# =============================================================================
# Impure loaders / embedders / writers.
# =============================================================================

def _load_pending_claims(lookback_days: int = CLAIM_LOOKBACK_DAYS) -> List[PendingClaim]:
    """Current (is_current, status='done') claims whose doc has no
    narrative_docs row yet -- doc-level exclusion, ported unchanged from the
    old clusterer: once ANY claim of a doc clusters, the whole doc is
    considered done, even if the doc had other unclustered claims. A claim
    that stays unclustered (fragmentation fix) keeps its doc eligible."""
    cutoff = datetime.now(timezone.utc) - timedelta(days=lookback_days)
    with db.connection() as conn:
        rows = conn.execute(
            """
            SELECT c.claim_id, c.doc_id, c.claim_text, c.confidence, d.published_at
            FROM analysis.claims c
            JOIN analysis.runs r ON r.run_id = c.run_id
            JOIN corpus.documents d ON d.doc_id = c.doc_id
            LEFT JOIN analysis.narrative_docs nd ON nd.doc_id = c.doc_id
            WHERE r.is_current AND r.status = 'done'::analysis.run_status
              AND r.task = 'claims'::analysis.task
              AND c.created_at >= %s
              AND nd.doc_id IS NULL
            ORDER BY c.created_at ASC
            """,
            (cutoff,),
        ).fetchall()
    return [
        PendingClaim(
            claim_id=row["claim_id"],
            doc_id=row["doc_id"],
            claim_text=row["claim_text"],
            confidence=row["confidence"],
            published_at=row["published_at"],
            tokens=tokenize_claim(row["claim_text"]),
        )
        for row in rows
    ]


def _load_existing_anchors() -> List[ExistingAnchor]:
    with db.connection() as conn:
        rows = conn.execute(
            """
            SELECT narrative_id, description, anchor_embedding, first_seen_at, first_seen_doc_id
            FROM analysis.narratives
            """
        ).fetchall()
    return [
        ExistingAnchor(
            narrative_id=row["narrative_id"],
            anchor_text=row["description"] or "",
            tokens=tokenize_claim(row["description"] or ""),
            embedding=list(row["anchor_embedding"]) if row["anchor_embedding"] is not None else None,
            first_seen_at=row["first_seen_at"],
            first_seen_doc_id=row["first_seen_doc_id"],
        )
        for row in rows
    ]


def _warm_anchor_embeddings(anchors: List[ExistingAnchor], embed_fn: EmbedFn) -> int:
    """Materialize anchor_embedding for every existing narrative missing one,
    once per run (not per pending claim) -- matches the old clusterer's
    _warm_anchor_embeddings rationale. Returns the fallback count."""
    fallbacks = 0
    with db.connection() as conn:
        for anchor in anchors:
            if anchor.embedding is not None or not anchor.anchor_text:
                continue
            embedding = embed_fn(anchor.anchor_text)
            if embedding is None:
                fallbacks += 1
                continue
            anchor.embedding = embedding
            conn.execute(
                "UPDATE analysis.narratives SET anchor_embedding = %s WHERE narrative_id = %s",
                (embedding, anchor.narrative_id),
            )
    return fallbacks


def _embed_pending_claims(pending: List[PendingClaim], embed_fn: EmbedFn) -> int:
    """One batched up-front pass over every pending claim's text -- the
    matching loop then only does in-memory cosine, never a network call.
    A failed embed() call (backend already logs the warning) leaves
    `embedding` as None; that claim degrades to jaccard-only for this run
    (see plan_clustering's per-claim fallback) rather than fabricating a
    vector. Returns the fallback count."""
    fallbacks = 0
    for pc in pending:
        embedding = embed_fn(pc.claim_text)
        if embedding is None:
            fallbacks += 1
            continue
        pc.embedding = embedding
    return fallbacks


def _resolve_embed_fn(embedding_model: str) -> Optional[EmbedFn]:
    """The configured backend's embed(), via the shared LLMClient (llm/
    client.py) instead of reaching for the raw backend directly. `supports_
    embedding` is checked up front (Gemini never overrides the no-op
    default) and turned into the same "embedding mode unavailable this run,
    fall back to jaccard" signal run() already handles for a None return."""
    from analysis.src.llm.client import get_client

    client = get_client()
    if not client.supports_embedding:
        logger.warning(
            f"Narrative clustering: backend has no embed() support -- "
            "embedding mode is unavailable this run"
        )
        return None
    return lambda text: client.embed(text, model=embedding_model)


def _distinct_doc_confidences(members: List[PendingClaim]) -> Dict[int, Optional[float]]:
    """One row per distinct doc for narrative_docs -- if two claims from the
    same doc join the same narrative, keep the higher confidence."""
    result: Dict[int, Optional[float]] = {}
    for pc in members:
        if pc.doc_id not in result:
            result[pc.doc_id] = pc.confidence
        elif pc.confidence is not None and (result[pc.doc_id] is None or pc.confidence > result[pc.doc_id]):
            result[pc.doc_id] = pc.confidence
    return result


def _earliest_doc(members: List[PendingClaim]) -> Tuple[int, datetime]:
    best = members[0]
    for pc in members[1:]:
        if pc.published_at < best.published_at or (
            pc.published_at == best.published_at and pc.doc_id < best.doc_id
        ):
            best = pc
    return best.doc_id, best.published_at


def _open_clustering_run(mode: str, threshold: float, embedding_model: Optional[str]) -> int:
    with db.connection() as conn:
        row = conn.execute(
            """
            INSERT INTO analysis.clustering_runs (mode, threshold, embedding_model, started_at)
            VALUES (%s, %s, %s, now())
            RETURNING clustering_run_id
            """,
            (mode, threshold, embedding_model),
        ).fetchone()
        return row["clustering_run_id"]


def _finish_clustering_run(clustering_run_id: int, doc_count: int) -> None:
    with db.connection() as conn:
        conn.execute(
            "UPDATE analysis.clustering_runs SET completed_at = now(), doc_count = %s "
            "WHERE clustering_run_id = %s",
            (doc_count, clustering_run_id),
        )


def _apply_plan(plan: ClusterPlan, clustering_run_id: int) -> Tuple[int, int]:
    """Write narratives/narrative_docs for one plan in a single transaction
    -- documented exception to results/store.py's sole-writer rule (see
    module docstring). Crash mid-apply rolls the whole run's writes back;
    the next run reprocesses every doc still absent from narrative_docs, so
    nothing is lost, just re-planned. Returns (docs_touched, narratives_created)."""
    docs_touched: Set[int] = set()
    narratives_created = 0

    with db.connection() as conn:
        for assignment in plan.existing_assignments:
            cur = conn.execute(
                """
                INSERT INTO analysis.narrative_docs (narrative_id, doc_id, confidence, added_by_run)
                VALUES (%s, %s, %s, %s)
                ON CONFLICT (narrative_id, doc_id) DO NOTHING
                """,
                (assignment.narrative_id, assignment.claim.doc_id, assignment.claim.confidence,
                 clustering_run_id),
            )
            if cur.rowcount:
                docs_touched.add(assignment.claim.doc_id)
                conn.execute(
                    """
                    UPDATE analysis.narratives
                    SET updated_at = now(),
                        first_seen_at = LEAST(first_seen_at, %(pub)s),
                        first_seen_doc_id = CASE WHEN %(pub)s < first_seen_at
                            THEN %(doc)s ELSE first_seen_doc_id END
                    WHERE narrative_id = %(nid)s
                    """,
                    {
                        "pub": assignment.claim.published_at,
                        "doc": assignment.claim.doc_id,
                        "nid": assignment.narrative_id,
                    },
                )

        for new_narrative in plan.new_narratives:
            doc_confidences = _distinct_doc_confidences(new_narrative.members)
            first_seen_doc_id, first_seen_at = _earliest_doc(new_narrative.members)
            row = conn.execute(
                """
                INSERT INTO analysis.narratives
                    (clustering_run_id, name, description, anchor_claim_id,
                     anchor_embedding, first_seen_at, first_seen_doc_id, updated_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, now())
                RETURNING narrative_id
                """,
                (
                    clustering_run_id,
                    new_narrative.anchor_claim.claim_text[:NARRATIVE_NAME_MAX_CHARS],
                    new_narrative.anchor_claim.claim_text,
                    new_narrative.anchor_claim.claim_id,
                    new_narrative.embedding,
                    first_seen_at,
                    first_seen_doc_id,
                ),
            ).fetchone()
            narrative_id = row["narrative_id"]
            narratives_created += 1
            for doc_id, confidence in doc_confidences.items():
                conn.execute(
                    """
                    INSERT INTO analysis.narrative_docs (narrative_id, doc_id, confidence, added_by_run)
                    VALUES (%s, %s, %s, %s)
                    """,
                    (narrative_id, doc_id, confidence, clustering_run_id),
                )
                docs_touched.add(doc_id)

    return len(docs_touched), narratives_created


# =============================================================================
# Entry point.
# =============================================================================

def run(
    *,
    embed_fn: Optional[EmbedFn] = None,
    mode: Optional[str] = None,
    jaccard_threshold: Optional[float] = None,
    embedding_threshold: Optional[float] = None,
    embedding_model: Optional[str] = None,
    min_support: int = MIN_NARRATIVE_SUPPORT,
) -> Dict[str, object]:
    """Run one clustering pass: load current unclustered claims, match them
    against existing narratives (and each other, within this run), persist
    the result, and record one `clustering_runs` provenance row.

    `embed_fn` lets tests/callers inject a fake embedder without a live
    Ollama/OpenAI-compatible server; production leaves it None and this
    resolves the configured backend's embed() (see `_resolve_embed_fn`).
    Settings-derived args default from `CIVIC_NARRATIVE_*` (get_settings())
    when not passed explicitly.
    """
    settings = get_settings()
    resolved_mode = mode if mode in ("jaccard", "embedding") else settings.narrative_similarity_mode
    if resolved_mode not in ("jaccard", "embedding"):
        resolved_mode = "jaccard"
    jaccard_threshold = (
        settings.narrative_jaccard_threshold if jaccard_threshold is None else jaccard_threshold
    )
    embedding_threshold = (
        settings.narrative_embedding_threshold if embedding_threshold is None else embedding_threshold
    )
    embedding_model = embedding_model or settings.narrative_embedding_model

    embedding_fallbacks = 0
    if resolved_mode == "embedding":
        if embed_fn is None:
            embed_fn = _resolve_embed_fn(embedding_model)
        if embed_fn is None:
            resolved_mode = "jaccard"

    threshold = embedding_threshold if resolved_mode == "embedding" else jaccard_threshold
    clustering_run_id = _open_clustering_run(
        resolved_mode, threshold, embedding_model if resolved_mode == "embedding" else None
    )

    existing_anchors = _load_existing_anchors()
    if resolved_mode == "embedding":
        embedding_fallbacks += _warm_anchor_embeddings(existing_anchors, embed_fn)

    pending = _load_pending_claims()
    if resolved_mode == "embedding":
        embedding_fallbacks += _embed_pending_claims(pending, embed_fn)

    docs_considered = len({pc.doc_id for pc in pending})

    plan = plan_clustering(
        pending,
        existing_anchors,
        mode=resolved_mode,
        jaccard_threshold=jaccard_threshold,
        embedding_threshold=embedding_threshold,
        min_support=min_support,
    )

    docs_touched, narratives_created = _apply_plan(plan, clustering_run_id)
    _finish_clustering_run(clustering_run_id, docs_touched)

    logger.info(
        f"Narrative clustering [{resolved_mode}] run={clustering_run_id}: "
        f"considered {len(pending)} claims across {docs_considered} docs, "
        f"created {narratives_created} narratives, touched {docs_touched} docs, "
        f"suppressed {plan.suppressed_count} sub-threshold group(s) "
        f"({plan.suppressed_claims} claim(s) left unclustered)"
        + (f", embedding fallbacks: {embedding_fallbacks}" if embedding_fallbacks else "")
    )

    return {
        "clustering_run_id": clustering_run_id,
        "mode": resolved_mode,
        "claims_considered": len(pending),
        "docs_considered": docs_considered,
        "narratives_created": narratives_created,
        "docs_touched": docs_touched,
        "suppressed_groups": plan.suppressed_count,
        "suppressed_claims": plan.suppressed_claims,
        "embedding_fallbacks": embedding_fallbacks,
    }
