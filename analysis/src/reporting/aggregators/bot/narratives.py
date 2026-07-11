"""
Narrative-amplification assembly + the shared flagged-example shaper.

Turns bot-flagged docs into evidence excerpts (``_flagged_example``) and
groups them by the real clustered narratives they support
(``_fetch_narrative_amplification``). ``_humanize_indicator`` normalizes
LLM-echoed snake_case signal slugs into display prose; it is used both by
the amplification pass and by ``_flagged_example`` (imported by the
entity-rollup pass in ``repository``).
"""

from __future__ import annotations

import re
from collections import Counter
from typing import Any, Dict, List, Optional

from analysis.src.reporting.aggregators.evidence import (
    build_doc_url,
    build_source_label,
)
from analysis.src.reporting.entity_registry import get_registry
from analysis.src.reporting.models import (
    FlaggedExample,
    NarrativeAmplification,
)


# How many real posts to surface per amplified narrative. Each appears as
# an evidence excerpt with an outbound source link.
_EXAMPLES_PER_NARRATIVE = 3
# Character budget for the excerpt shown in the modal. Full text is
# one click away via the permalink; the preview is just for recognition.
_EXAMPLE_TEXT_CHARS = 220
# Caps for the derived chips in the amplification modal.
_MAX_HASHTAGS = 5
_MAX_TARGETS = 5
_MAX_WHY_FLAGGED = 3
# Per-example evidence caps (Phase 2d) — indicator chips + reasoning shown
# on each flagged post card.
_MAX_INDICATORS_PER_EXAMPLE = 4
_EXAMPLE_REASONING_CHARS = 240

_HASHTAG_RE = re.compile(r"#(\w{2,})")
_SLUG_RE = re.compile(r"^[a-z0-9_]+$")


def _humanize_indicator(indicator: str) -> str:
    """Display form of a bot-detection indicator. Heuristic indicators are
    already prose ("New account (12 days)"); LLM responses sometimes echo
    snake_case signal names ("zero_followers_following_listed") — those are
    real signals but must not render as raw slugs."""
    text = (indicator or "").strip()
    if _SLUG_RE.match(text):
        return text.replace("_", " ").capitalize()
    return text


def _flagged_example(
    doc_id: int,
    text: Any,
    source_type: Any,
    domain: Any,
    x_handle: Any,
    ident: Any,
    data: Optional[Dict[str, Any]] = None,
    confidence: Optional[float] = None,
) -> FlaggedExample | None:
    """Shape one bot-flagged doc into evidence-excerpt form, or None when
    there is no text to excerpt. Permalink via the shared build_doc_url
    helper — C1 invariant: every evidence surface links back to the
    original.

    ``data`` (the parsed bot_detection output_json) and ``confidence``
    carry the per-doc WHY onto the example (Phase 2d): humanized
    indicators + truncated reasoning + flag confidence. Indicators pass
    through _humanize_indicator so LLM-echoed snake_case slugs never
    render raw; noise entries from pre-sanitization rows are still
    filtered display-side until those age out of the window."""
    excerpt = (text or "").strip()
    if not excerpt:
        return None
    if len(excerpt) > _EXAMPLE_TEXT_CHARS:
        excerpt = excerpt[:_EXAMPLE_TEXT_CHARS].rstrip() + "…"

    indicators: List[str] = []
    reasoning: Optional[str] = None
    if data:
        indicators = [
            _humanize_indicator(i)
            for i in data.get("indicators", [])[:_MAX_INDICATORS_PER_EXAMPLE]
            if isinstance(i, str) and i.strip()
        ]
        reasoning = data.get("reasoning") or None
        if reasoning and len(reasoning) > _EXAMPLE_REASONING_CHARS:
            reasoning = reasoning[:_EXAMPLE_REASONING_CHARS - 3].rstrip() + "..."

    return FlaggedExample(
        doc_id=doc_id,
        text=excerpt,
        source_label=build_source_label(source_type, domain, x_handle),
        url=build_doc_url(source_type, domain, ident, x_handle=x_handle),
        confidence=round(float(confidence), 3) if confidence is not None else None,
        indicators=indicators,
        reasoning=reasoning,
    )


def _fetch_narrative_amplification(
    cursor,
    bot_docs_data: List[Dict[str, Any]],
) -> List[NarrativeAmplification]:
    """Top narratives (real clustered claims from the narratives tables)
    whose supporting docs are bot-flagged.

    This replaced the pre-2026-07-10 version that presented bot INDICATOR
    strings as "narratives" — an internal signal name is not a talking
    point, and LLM-echoed slugs leaked straight into headline copy. Now:
      - narrative   = the narrative's actual name (narrative_docs join)
      - examplePosts = bot-flagged docs supporting that narrative
      - topHashtags = hashtags extracted from those docs' texts
      - targets     = who those docs take stances toward (target_mentions)
      - whyFlagged  = the humanized top indicators across those docs
    Empty when no bot-flagged doc belongs to a narrative — the UI states
    that honestly instead of inventing a cluster.
    """
    if not bot_docs_data:
        return []
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='narrative_docs'"
    )
    if not cursor.fetchone():
        return []

    by_doc = {row["doc_id"]: row for row in bot_docs_data}
    placeholders = ",".join("?" * len(by_doc))
    cursor.execute(
        f"""
        SELECT nd.doc_id, n.narrative_id, n.name
        FROM narrative_docs nd
        JOIN narratives n ON n.narrative_id = nd.narrative_id
        WHERE nd.doc_id IN ({placeholders})
        """,
        list(by_doc),
    )
    groups: Dict[int, Dict[str, Any]] = {}
    for doc_id, narrative_id, name in cursor.fetchall():
        group = groups.setdefault(
            narrative_id,
            {"narrative_id": narrative_id, "name": name, "doc_ids": []},
        )
        group["doc_ids"].append(doc_id)

    top = sorted(groups.values(), key=lambda g: -len(g["doc_ids"]))[:3]
    out: List[NarrativeAmplification] = []
    for group in top:
        rows = [by_doc[d] for d in group["doc_ids"]]
        examples = []
        for row in rows:
            ex = _flagged_example(
                row["doc_id"], row.get("text"), row.get("source_type"),
                row.get("domain"), row.get("x_handle"), row.get("ident"),
                data=row.get("data"), confidence=row.get("confidence"),
            )
            if ex is not None:
                examples.append(ex)
            if len(examples) >= _EXAMPLES_PER_NARRATIVE:
                break

        indicator_counts: Counter = Counter()
        hashtag_counts: Counter = Counter()
        for row in rows:
            for indicator in row["data"].get("indicators", []):
                indicator_counts[_humanize_indicator(indicator)] += 1
            for tag in _HASHTAG_RE.findall(row.get("text") or ""):
                hashtag_counts[f"#{tag}"] += 1

        out.append(NarrativeAmplification(
            # The REAL narrative_id — the UI deep-links
            # "#narratives?open=<id>" from the amplification card, which
            # only resolves when this matches the Narratives payload.
            # (Was a synthetic 1..3 index before 2026-07-10.)
            id=group["narrative_id"],
            narrative=group["name"] or "",
            confidence="medium" if len(rows) > 5 else "low",
            examplePosts=examples,
            topHashtags=[t for t, _ in hashtag_counts.most_common(_MAX_HASHTAGS)],
            topPhrases=[],
            targets=_targets_for_docs(cursor, group["doc_ids"]),
            suspectedBotVolume=len(rows),
            whyFlagged=[i for i, _ in indicator_counts.most_common(_MAX_WHY_FLAGGED)],
        ))
    return out


def _targets_for_docs(cursor, doc_ids: List[int]) -> List[str]:
    """Display names of the entities the given docs take stances toward,
    from write-time-resolved target_mentions (migration 025). Empty when
    the table is absent (older test DBs) or nothing resolved."""
    if not doc_ids:
        return []
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='target_mentions'"
    )
    if not cursor.fetchone():
        return []
    placeholders = ",".join("?" * len(doc_ids))
    cursor.execute(
        f"""
        SELECT m.entity_key, m.raw_target, COUNT(*) AS n
        FROM target_mentions m
        WHERE m.doc_id IN ({placeholders}) AND m.entity_key IS NOT NULL
        GROUP BY m.entity_key
        ORDER BY n DESC
        LIMIT {_MAX_TARGETS}
        """,
        doc_ids,
    )
    registry = get_registry()
    targets: List[str] = []
    for entity_key, raw_target, _n in cursor.fetchall():
        official = registry.officials.get(entity_key)
        targets.append(official.display_name if official else raw_target)
    return targets
