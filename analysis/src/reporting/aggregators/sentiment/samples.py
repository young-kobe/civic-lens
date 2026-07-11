"""
Classification-sample construction and target-chip helpers.

Leaf module of the sentiment package: the sample dict/model builders, the
capped-insert de-duper, the per-collector wrappers, and the doc-target chip
attachment. Everything here depends only on the shared aggregator utilities
(``evidence`` / ``rows`` / ``models`` / ``entity_registry``), never on the
other sentiment submodules — so both ``target_tone`` and ``entities`` (and the
orchestrating ``aggregator``) can import from it without a cycle.

Relocated here to keep this a leaf (and break the samples<->target_tone /
samples<->aggregator cycles the split would otherwise create):
  * ``_LABEL_MAP`` / ``_increment_bucket`` — used by ``_collect_entity_sample``
    below and by the aggregator's row loop; the aggregator imports them back.
  * ``_STANCE_KEYS`` / ``_COLLECTIVE_LABELS`` / ``MAX_TARGETS_PER_SAMPLE`` —
    used by ``_build_doc_targets`` / ``_target_display_label`` here and by
    ``target_tone``; ``target_tone`` imports them back.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, Iterable, List, Optional, Tuple

from analysis.src.reporting.aggregators.evidence import (
    build_doc_url,
    sanitize_evidence,
)
from analysis.src.reporting.entity_registry import (
    DEM_COLLECTIVE, GOP_COLLECTIVE, get_registry,
)
from analysis.src.reporting.models import (
    ClassificationSample,
    PublicSentimentResult,
)


# --------------------------------------------------------------------------- #
#  Sample caps + shared count maps                                            #
# --------------------------------------------------------------------------- #

# Keys for per-intensity drill-down sampling — must match SentimentDistribution
# field names so the UI can look them up directly.
STRENGTH_BUCKETS = (
    "strongPositive", "mildPositive", "neutral", "mildNegative", "strongNegative",
)
MAX_DISTRIBUTION_SAMPLES_PER_BUCKET = 15
MAX_SAMPLES_PER_TOPIC = 5
MAX_SAMPLES_PER_ENTITY = 10
MAX_SAMPLES_PER_TARGET = 5

_LABEL_MAP = {
    "POSITIVE": "positive",
    "NEGATIVE": "negative",
    "NEUTRAL": "neutral",
    "MIXED": "neutral",
}


def _increment_bucket(bucket: Dict[str, Dict[str, int]], key: str, label_key: str) -> None:
    bucket.setdefault(key, {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0})
    bucket[key][label_key] += 1


# --------------------------------------------------------------------------- #
#  Sample collection                                                          #
# --------------------------------------------------------------------------- #

def _build_sample_dict(
    doc_id: int,
    label: str,
    confidence: float,
    data: Dict[str, Any],
    title: Optional[str],
    source_type: Optional[str],
    published_at: Optional[float],
    domain_or_subreddit: Optional[str],
    ident: Optional[str],
    text: Optional[str],
    x_handle: Optional[str] = None,
    topic: Optional[str] = None,
    engagement: Optional[Dict[str, int]] = None,
    author: Optional[Dict[str, Any]] = None,
) -> Dict[str, Any]:
    """Build the dict representation of a classification sample used by
    every collector. Centralizing this avoids the 3-place duplication
    that pre-split sentiment.py was carrying.

    ``topic`` is the doc's topic attribution (LLM mention topic with
    keyword fallback — see _fetch_doc_topics) so the UI can filter samples
    exactly instead of re-guessing with client-side keyword lists.

    ``engagement`` / ``author`` are the Phase 2b/2c enrichments
    (base.build_sample_engagement / build_sample_author); None when the
    source has none stored — the wire shape omits nothing, the UI treats
    null as "not available"."""
    date_str = (
        datetime.fromtimestamp(published_at).strftime("%b %d, %Y")
        if published_at else None
    )
    # Synthesize the permalink so every sample is auditable (invariant C1 /
    # audit A-7) via the shared builder — one implementation across aggregators.
    url = build_doc_url(source_type, domain_or_subreddit, ident, x_handle)
    return {
        "doc_id": doc_id,
        "label": label,
        "confidence": confidence,
        "reasoning": data.get("reasoning", ""),
        "evidence_spans": sanitize_evidence(data.get("evidence_spans", [])),
        "sarcasm_detected": bool(data.get("sarcasm_detected", False)),
        "title": title or "",
        "source_type": source_type or "unknown",
        # X rows carry the author handle as the display name (the UI renders
        # "X · @<source_name>"); domain_or_subreddit is literally "x.com" and
        # would render "X · @x.com". Handle-less X rows (author missing from
        # x_users_raw — the join is a LEFT JOIN) get None so the UI degrades
        # to a bare "X", matching narrative.py's _build_source_label.
        # News/Reddit keep domain/subreddit.
        "source_name": (
            (x_handle or None) if source_type == "x_post"
            else domain_or_subreddit
        ),
        "date": date_str,
        "full_text": text or "",
        "url": url,
        "topic": topic,
        "engagement": engagement,
        "author": author,
        # Stamped after aggregation (_attach_sample_targets) or by
        # entity_posts' per-page fetch; None = no target coverage.
        "targets": None,
    }


def _sample_dict_to_model(s: Dict[str, Any]) -> ClassificationSample:
    """Convert a sample dict into the ClassificationSample dataclass.
    Used in every list-level formatter to avoid repeated field hoisting."""
    return ClassificationSample(
        doc_id=s["doc_id"],
        label=s["label"],
        confidence=s["confidence"],
        reasoning=s["reasoning"],
        evidence_spans=s["evidence_spans"],
        sarcasm_detected=s["sarcasm_detected"],
        title=s.get("title"),
        source_type=s.get("source_type"),
        source_name=s.get("source_name"),
        date=s.get("date"),
        full_text=s.get("full_text", ""),
        url=s.get("url"),
        topic=s.get("topic"),
        engagement=s.get("engagement"),
        author=s.get("author"),
        targets=s.get("targets"),
    )


def _insert_capped(samples: List[Dict[str, Any]], sample: Dict[str, Any], cap: int) -> None:
    """Keep the list sorted by confidence desc and capped at ``cap``.
    De-dupes by doc_id so the same row can't show up twice from the
    ai_outputs table (which may have multiple rows per doc across prompt
    versions / reruns)."""
    if any(s["doc_id"] == sample["doc_id"] for s in samples):
        return
    if len(samples) < cap:
        samples.append(sample)
        samples.sort(key=lambda s: s["confidence"], reverse=True)
    elif sample["confidence"] > samples[-1]["confidence"]:
        samples[-1] = sample
        samples.sort(key=lambda s: s["confidence"], reverse=True)


def _collect_topic_sample(
    topic_samples: Dict[str, List[Dict[str, Any]]],
    topic: str,
    doc_id: int, label: str, confidence: float,
    data: Dict[str, Any],
    title: Optional[str], source_type: Optional[str], published_at: Optional[float],
    domain_or_subreddit: Optional[str], ident: Optional[str], text: Optional[str],
    x_handle: Optional[str] = None,
    engagement: Optional[Dict[str, int]] = None,
    author: Optional[Dict[str, Any]] = None,
) -> None:
    if not data.get("reasoning"):
        return
    samples = topic_samples.setdefault(topic, [])
    sample = _build_sample_dict(
        doc_id, label, confidence, data, title, source_type,
        published_at, domain_or_subreddit, ident, text, x_handle,
        topic=topic, engagement=engagement, author=author,
    )
    _insert_capped(samples, sample, MAX_SAMPLES_PER_TOPIC)


def _collect_strength_sample(
    strength_samples: Dict[str, List[Dict[str, Any]]],
    bucket: str,
    doc_id: int, label: str, confidence: float,
    data: Dict[str, Any],
    title: Optional[str], source_type: Optional[str], published_at: Optional[float],
    domain_or_subreddit: Optional[str], ident: Optional[str], text: Optional[str],
    x_handle: Optional[str] = None,
    topic: Optional[str] = None,
    engagement: Optional[Dict[str, int]] = None,
    author: Optional[Dict[str, Any]] = None,
) -> None:
    """Append a sample to one of the five STRENGTH_BUCKETS. Silently
    drops rows for unknown buckets so callers can pass the key through
    without branching."""
    samples = strength_samples.get(bucket)
    if samples is None or not data.get("reasoning"):
        return
    sample = _build_sample_dict(
        doc_id, label, confidence, data, title, source_type,
        published_at, domain_or_subreddit, ident, text, x_handle,
        topic=topic, engagement=engagement, author=author,
    )
    _insert_capped(samples, sample, MAX_DISTRIBUTION_SAMPLES_PER_BUCKET)


def _collect_entity_sample(
    entity_accum: Dict[str, Any],
    doc_id: int, label: str, confidence: float,
    data: Dict[str, Any],
    title: Optional[str], source_type: Optional[str], published_at: Optional[float],
    domain_or_subreddit: Optional[str], ident: Optional[str], text: Optional[str],
    x_handle: Optional[str] = None,
    topic: Optional[str] = None,
    engagement: Optional[Dict[str, int]] = None,
    author: Optional[Dict[str, Any]] = None,
) -> None:
    """Bump an entity bucket's counters + append a sample when there's room."""
    label_key = _LABEL_MAP.get(label, "neutral")
    entity_accum[label_key] += 1
    entity_accum["volume"] += 1
    # Sum engagement across every post (not just the capped samples) so the
    # officials column can rank by engagement-weighted volume.
    if engagement:
        entity_accum["engagement_total"] = (
            entity_accum.get("engagement_total", 0) + sum(engagement.values())
        )
    if topic is not None:
        _increment_bucket(entity_accum["by_topic"], topic, label_key)
    if not data.get("reasoning"):
        return
    sample = _build_sample_dict(
        doc_id, label, confidence, data, title, source_type,
        published_at, domain_or_subreddit, ident, text, x_handle,
        topic=topic, engagement=engagement, author=author,
    )
    _insert_capped(entity_accum["samples"], sample, MAX_SAMPLES_PER_ENTITY)


# --------------------------------------------------------------------------- #
#  Target chips on classification samples                                     #
# --------------------------------------------------------------------------- #

_STANCE_KEYS = ("positive", "negative", "neutral", "mixed")

_COLLECTIVE_LABELS = {
    GOP_COLLECTIVE: "Republicans (party)",
    DEM_COLLECTIVE: "Democrats (party)",
}

# Chips per sample stay small — the card answers "who is this about",
# not the full fan-out.
MAX_TARGETS_PER_SAMPLE = 2


def _target_display_label(key: Optional[str], raw_target: Optional[str], registry) -> Optional[str]:
    """Display label for one target_mentions row: registry name for
    resolved officials, fixed label for party collectives, the verbatim
    raw string otherwise (shown as free text, never dressed up as an
    identified entity)."""
    if key is not None:
        if key in _COLLECTIVE_LABELS:
            return _COLLECTIVE_LABELS[key]
        official = registry.officials.get(key)
        if official is not None:
            return official.profile_dict().get("displayName") or key
        return key
    raw = (raw_target or "").strip()
    return raw or None


def _build_doc_targets(
    mention_rows: Iterable[Tuple[int, Optional[str], str, Optional[str]]],
) -> Dict[int, List[Dict[str, str]]]:
    """doc_id → [{label, stance}] chips, resolved targets first, capped at
    MAX_TARGETS_PER_SAMPLE. Rows are (doc_id, entity_key, stance,
    raw_target) slices of the frozen target_mentions rows — the same
    source the tone merges read."""
    registry = get_registry()
    resolved: Dict[int, List[Dict[str, str]]] = {}
    raw: Dict[int, List[Dict[str, str]]] = {}
    for doc_id, key, stance, raw_target in mention_rows:
        if stance not in _STANCE_KEYS:
            continue
        label = _target_display_label(key, raw_target, registry)
        if label is None:
            continue
        chip = {"label": label, "stance": stance}
        bucket = resolved if key is not None else raw
        chips = bucket.setdefault(doc_id, [])
        if not any(c["label"] == label for c in chips):
            chips.append(chip)
    out: Dict[int, List[Dict[str, str]]] = {}
    for doc_id in resolved.keys() | raw.keys():
        chips = resolved.get(doc_id, []) + raw.get(doc_id, [])
        out[doc_id] = chips[:MAX_TARGETS_PER_SAMPLE]
    return out


def _attach_sample_targets(
    result: PublicSentimentResult,
    doc_targets: Dict[int, List[Dict[str, str]]],
) -> None:
    """Stamp target chips onto every classification-sample surface.

    A display-only post-pass (targets never affect bucketing, unlike
    doc_topics), so it runs once here instead of threading another
    parameter through every collector. Received-tone samples are skipped —
    on the target's own card the chip would restate the card."""
    if not doc_targets:
        return

    def visit(samples: List[ClassificationSample]) -> None:
        for s in samples:
            chips = doc_targets.get(s.doc_id)
            if chips:
                s.targets = chips

    for topic_row in result.byTopic:
        visit(topic_row.classification_samples)
    for bucket_samples in result.distributionSamples.values():
        visit(bucket_samples)
    for items in (result.byNewsOutlet, result.byOfficial, result.byGeneralPublic):
        for item in items:
            visit(item.classification_samples)
