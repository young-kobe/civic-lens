"""
Targets engine (Postgres redesign Phase 6): per-target stance extraction,
mirroring engine/text.py's pure analyze() + thin process() shape. Unlike
favorability_stances, target_mentions.entity_id is NULLABLE -- unresolved
targets are stored with entity_id NULL rather than dropped (see the
TargetAnalysis docstring below).
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional, Sequence

from analysis.src.common.entity_resolver import EntityResolver
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine import validation
from analysis.src.engine.constants import MAX_TARGETS, TARGET_TEXT_MAX_CHARS, TARGETS_TASK
from analysis.src.engine.text_prep import is_trivial_content, truncate_at_sentence
from analysis.src.llm.client import LLMClient
from analysis.src.llm.prompts import (
    TARGET_SENTIMENT_PROMPT_VERSION,
    TARGET_SENTIMENT_SYSTEM_PROMPT,
    TARGET_SENTIMENT_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import TARGET_SENTIMENT_SCHEMA, TARGET_TOPIC_ENUM
from analysis.src.results import store

logger = get_logger(__name__)

# TARGETS_TASK, MAX_TARGETS, TARGET_TEXT_MAX_CHARS consolidated into
# engine/constants.py (Postgres redesign Phase 6, constants-consolidation
# pass) -- imported above.

# analysis.target_mentions.stance is analysis.sentiment_label
# (positive/negative/neutral/mixed) with no "unknown" bucket. An LLM stance
# value outside this map is dropped, never guessed at.
_STANCE_MAP = {
    "positive": "positive",
    "negative": "negative",
    "neutral": "neutral",
    "mixed": "mixed",
}


@dataclass(frozen=True)
class TargetDocInput:
    """One doc's targets-engine input, assembled by the caller from
    corpus.documents. `tracked_targets` is the canonical-name list the
    caller builds from the entity registry (job_runner today) -- nudges the
    LLM to reuse an exact known name via the TRACKED TARGETS block, but
    resolution below never depends on the model having used it."""

    doc_id: int
    source_type: str
    text: str
    tracked_targets: Sequence[str] = ()


@dataclass(frozen=True)
class TargetMentionOutcome:
    """One validated stance toward one target. `raw_target` is always the
    LLM's original string, kept regardless of resolution; `entity_id` is
    None when EntityResolver could not resolve it."""

    raw_target: str
    entity_id: Optional[int]
    stance: str  # analysis.sentiment_label: positive|negative|neutral|mixed
    topic: Optional[str]
    confidence: float
    evidence_spans: List[str]


@dataclass(frozen=True)
class TargetAnalysis:
    """The validated outcome of one `analyze()` call.

    KEY ASYMMETRY vs the text engine: analysis.target_mentions.entity_id is
    NULLABLE, so an unresolved target is kept in `mentions` with
    entity_id=None instead of being dropped (unlike TextAnalysis.
    favorability_stances, which can only hold resolved entities because
    favorability_stances.entity_id is NOT NULL). `dropped_unmappable_stance`
    counts mentions dropped for a different reason entirely: a stance value
    that doesn't map onto the DDL's sentiment_label enum.
    """

    mentions: List[TargetMentionOutcome]
    dropped_unmappable_stance: int
    inference_method: str  # 'llm' | 'deterministic'
    raw_response: Optional[Dict]  # verbatim LLM payload; None for the trivial short-circuit


def _build_tracked_targets_block(tracked_targets: Sequence[str]) -> str:
    """Format the TRACKED TARGETS block prepended above the input text.
    Empty input -> empty string (no block, no token cost) -- same shape as
    the reference-context seeds block in llm/context_seeds.py."""
    if not tracked_targets:
        return ""
    lines = "\n".join(f"- {name}" for name in tracked_targets)
    return f"TRACKED TARGETS (use these exact names when the target matches):\n{lines}\n\n"


def analyze(doc: TargetDocInput, client: LLMClient, resolver: EntityResolver) -> TargetAnalysis:
    """Per-target stances for one doc. No DB access: `resolver` is an
    already-loaded in-memory lookup, not a live query. Raises whatever
    `client.complete()` raises (RuntimeError, after retries are exhausted)
    when the LLM call fails or the backend is unavailable -- `process()`
    below is what turns that into a recorded failed run."""
    if is_trivial_content(doc.text):
        return TargetAnalysis(
            mentions=[],
            dropped_unmappable_stance=0,
            inference_method="deterministic",
            raw_response=None,
        )

    targets_block = _build_tracked_targets_block(doc.tracked_targets)
    user_prompt = targets_block + TARGET_SENTIMENT_USER_PROMPT_TEMPLATE.format(
        text=truncate_at_sentence(doc.text, TARGET_TEXT_MAX_CHARS)
    )
    response = client.complete(
        system_prompt=TARGET_SENTIMENT_SYSTEM_PROMPT,
        user_prompt=user_prompt,
        response_schema=TARGET_SENTIMENT_SCHEMA,
    )

    mentions, dropped_unmappable_stance = _build_target_mentions(response, doc.text, resolver)
    if dropped_unmappable_stance:
        logger.info(
            f"targets engine doc={doc.doc_id}: dropped {dropped_unmappable_stance} "
            "target mention(s) with an unmappable stance"
        )
    unresolved_count = sum(1 for mention in mentions if mention.entity_id is None)
    if unresolved_count:
        logger.info(
            f"targets engine doc={doc.doc_id}: {unresolved_count} target mention(s) "
            "unresolved against the entity registry (kept with entity_id NULL)"
        )

    return TargetAnalysis(
        mentions=mentions,
        dropped_unmappable_stance=dropped_unmappable_stance,
        inference_method="llm",
        raw_response=response,
    )


def _build_target_mentions(
    response: dict, source_text: str, resolver: EntityResolver,
) -> tuple[List[TargetMentionOutcome], int]:
    mentions: List[TargetMentionOutcome] = []
    dropped_unmappable_stance = 0
    seen_names: set = set()

    for raw in (response.get("targets", []) or [])[:MAX_TARGETS]:
        if not isinstance(raw, dict):
            continue
        raw_target = (raw.get("target") or "").strip()
        if not raw_target:
            continue

        raw_stance = (raw.get("stance") or "").strip().lower()
        stance = _STANCE_MAP.get(raw_stance)
        if stance is None:
            dropped_unmappable_stance += 1
            continue

        # De-dup by normalized name, keeping the first valid mention -- the
        # schema caps at MAX_TARGETS, so a repeat is a model slip, not a
        # real second stance toward the same target.
        name_key = raw_target.lower()
        if name_key in seen_names:
            continue
        seen_names.add(name_key)

        topic = (raw.get("topic") or "").strip()
        if topic not in TARGET_TOPIC_ENUM:
            topic = "Other"

        spans, had_invalid = validation.validate_spans(
            raw.get("evidence_spans", []) or [], source_text
        )
        confidence = validation.cap_confidence_if_unverified(
            raw.get("confidence", 0.5), verified=not had_invalid
        )

        mentions.append(TargetMentionOutcome(
            raw_target=raw_target,
            entity_id=resolver.resolve(raw_target),
            stance=stance,
            topic=topic,
            confidence=confidence,
            evidence_spans=spans,
        ))

    return mentions, dropped_unmappable_stance


def _resolve_model_id() -> str:
    """Mirrors text.py's per-engine model_id resolution (settings.llm_backend
    selects gemini_model vs ollama_model). A per-engine stopgap until Phase 7
    generalizes model_id resolution into the scheduler."""
    settings = get_settings()
    if settings.llm_backend.lower() == "ollama":
        return settings.ollama_model
    return settings.gemini_model


def process(doc: TargetDocInput, client: LLMClient, resolver: EntityResolver) -> int:
    """Analyze `doc` and persist its target mentions under one
    analysis.runs('targets') row. Returns the new run_id."""
    store.register_prompt_version(
        TARGET_SENTIMENT_PROMPT_VERSION, TARGETS_TASK,
        TARGET_SENTIMENT_SYSTEM_PROMPT, TARGET_SENTIMENT_USER_PROMPT_TEMPLATE,
    )
    model_id = _resolve_model_id()

    try:
        result = analyze(doc, client, resolver)
    except Exception as exc:
        logger.warning(f"targets engine failed for doc={doc.doc_id}: {exc}")
        handle = store.open_run(
            TARGETS_TASK, doc_id=doc.doc_id, model_id=model_id,
            prompt_version=TARGET_SENTIMENT_PROMPT_VERSION, inference_method="llm",
        )
        return handle.finish("failed", error=str(exc))

    prompt_version = TARGET_SENTIMENT_PROMPT_VERSION if result.inference_method == "llm" else None
    handle = store.open_run(
        TARGETS_TASK, doc_id=doc.doc_id, model_id=model_id,
        prompt_version=prompt_version, inference_method=result.inference_method,
    )
    handle.save_target_mentions([
        store.TargetMentionRow(
            raw_target=mention.raw_target,
            stance=mention.stance,
            confidence=mention.confidence,
            entity_id=mention.entity_id,
            topic=mention.topic,
            evidence_spans=mention.evidence_spans,
        )
        for mention in result.mentions
    ])

    # Run-level confidence: mean of every mention's own confidence. Zero
    # mentions is a legitimate outcome (the doc took no stance toward any
    # target) with no per-mention confidence to average -- store None rather
    # than guess a value.
    confidences = [mention.confidence for mention in result.mentions]
    run_confidence = sum(confidences) / len(confidences) if confidences else None

    return handle.finish("done", confidence=run_confidence, raw_response=result.raw_response)
