"""
Propaganda-technique engine (Postgres redesign Phase 6): pure `analyze()` +
thin `process()`, same shape as engine/text.py. Ports propaganda_detector.py's
loaded-language pre-filter and evidence validation onto engine/validation.py.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Dict, List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine import validation
from analysis.src.engine.constants import (
    MAX_PROPAGANDA_TECHNIQUES,
    PROPAGANDA_LOADED_LEXICON,
    PROPAGANDA_PRE_FILTER_SCAN_CHARS,
    PROPAGANDA_TASK,
    PROPAGANDA_TEXT_MAX_CHARS,
)
from analysis.src.engine.text_prep import truncate_at_sentence
from analysis.src.llm.client import LLMClient
from analysis.src.llm.prompts import (
    PROPAGANDA_PROMPT_VERSION,
    PROPAGANDA_SYSTEM_PROMPT,
    PROPAGANDA_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import PROPAGANDA_SCHEMA
from analysis.src.results import store

logger = get_logger(__name__)

# PROPAGANDA_TASK, PROPAGANDA_TEXT_MAX_CHARS, MAX_PROPAGANDA_TECHNIQUES,
# PROPAGANDA_LOADED_LEXICON, PROPAGANDA_PRE_FILTER_SCAN_CHARS consolidated
# into engine/constants.py (Postgres redesign Phase 6, constants-
# consolidation pass) -- imported above. _DDL_PROPAGANDA_TECHNIQUES below
# stays local: a private enum-gate frozenset read by exactly one function
# here, not a genuinely shared value.

# The DDL's analysis.propaganda_technique enum (data/pg-migrations/
# 0001_north_star.sql), duplicated here as the INSERT-time gate on purpose:
# kept separate from llm/schemas.py's PROPAGANDA_TECHNIQUE_ENUM (the
# LLM-facing contract) so a future prompt/schema addition can't punch
# straight through to a failed enum cast. Verified identical to the
# schema/prompt vocabulary as of 2026-07-23 -- any future technique the LLM
# returns outside this set is dropped, never guess-mapped; extending it for
# real requires an `ALTER TYPE ... ADD VALUE` migration first.
_DDL_PROPAGANDA_TECHNIQUES = frozenset({
    "loaded_language", "name_calling", "ad_hominem",
    "appeal_to_fear", "whataboutism", "doubt_casting",
})

# Compiled pattern for the pre-filter word scan -- implementation detail of
# _has_loaded_language() below, not a shared config value; stays local.
_WORD_RE = re.compile(r"[a-zA-Z']+")

_PRE_FILTER_REASONING = (
    "No loaded language detected in the pre-filter window (or empty "
    "document); propaganda-technique LLM call skipped."
)


def _has_loaded_language(text: str) -> bool:
    """True if the first `PROPAGANDA_PRE_FILTER_SCAN_CHARS` of `text` contain
    any token from `PROPAGANDA_LOADED_LEXICON`."""
    if not text:
        return False
    window = text[:PROPAGANDA_PRE_FILTER_SCAN_CHARS].lower()
    return any(m.group() in PROPAGANDA_LOADED_LEXICON for m in _WORD_RE.finditer(window))


@dataclass(frozen=True)
class PropagandaDocInput:
    """One doc's propaganda-engine input. `title` is prepended to `text`
    before scoring -- propaganda techniques often live in headline wording."""

    doc_id: int
    source_type: str
    title: Optional[str]
    text: str


@dataclass(frozen=True)
class PropagandaTechniqueOutcome:
    technique: str  # analysis.propaganda_technique
    confidence: float
    evidence_span: str


@dataclass(frozen=True)
class PropagandaAnalysis:
    """The validated outcome of one `analyze()` call. `techniques` holds
    only DDL-enum-valid, evidence-verified flags; `techniques_validated`/
    `techniques_dropped` make the drop rate explicit rather than implicit
    in `len(techniques)` vs the raw LLM count."""

    density: float  # analysis.propaganda_results.density
    summary: Optional[str]  # analysis.propaganda_results.summary
    techniques: List[PropagandaTechniqueOutcome]
    techniques_validated: int
    techniques_dropped: int
    inference_method: str  # 'llm' | 'deterministic'
    raw_response: Optional[Dict]


def analyze(doc: PropagandaDocInput, client: LLMClient) -> PropagandaAnalysis:
    """Propaganda-technique detection for one doc. No DB access. Raises
    whatever `client.complete()` raises (RuntimeError, after retries are
    exhausted) when the LLM call fails or the backend is unavailable --
    `process()` below is what turns that into a recorded failed run."""
    combined = f"{doc.title}\n\n{doc.text}" if doc.title else doc.text
    clamped = truncate_at_sentence(combined, PROPAGANDA_TEXT_MAX_CHARS)

    if not _has_loaded_language(clamped):
        return PropagandaAnalysis(
            density=0.0,
            summary=_PRE_FILTER_REASONING,
            techniques=[],
            techniques_validated=0,
            techniques_dropped=0,
            inference_method="deterministic",
            raw_response=None,
        )

    response = client.complete(
        system_prompt=PROPAGANDA_SYSTEM_PROMPT,
        user_prompt=PROPAGANDA_USER_PROMPT_TEMPLATE.format(text=clamped),
        response_schema=PROPAGANDA_SCHEMA,
    )

    raw_techniques = (response.get("techniques") or [])[:MAX_PROPAGANDA_TECHNIQUES]
    validated: List[PropagandaTechniqueOutcome] = []
    dropped = 0
    for raw in raw_techniques:
        outcome = _validate_technique(raw, clamped)
        if outcome is None:
            dropped += 1
        else:
            validated.append(outcome)

    try:
        overall = float(response.get("overall_propaganda_score", 0.0))
    except (TypeError, ValueError):
        overall = 0.0
    overall = max(0.0, min(1.0, overall))

    # Three outcomes (ported from the old detector, cap value updated
    # 0.2 -> 0.3 per the unified UNVERIFIED_EVIDENCE_CONFIDENCE_CAP):
    # - >=1 technique validated: trust the reported density.
    # - LLM flagged techniques but none validated: cap density.
    # - LLM flagged zero techniques: force density to 0.0.
    if not validated:
        overall = validation.cap_confidence_if_unverified(overall, verified=False) if raw_techniques else 0.0

    if dropped:
        logger.info(
            f"propaganda engine doc={doc.doc_id}: validated {len(validated)}, "
            f"dropped {dropped} technique(s)"
        )

    return PropagandaAnalysis(
        density=round(overall, 3),
        summary=response.get("reasoning"),
        techniques=validated,
        techniques_validated=len(validated),
        techniques_dropped=dropped,
        inference_method="llm",
        raw_response=response,
    )


def _validate_technique(raw: dict, source_text: str) -> Optional[PropagandaTechniqueOutcome]:
    """One flagged technique, or None if it fails the DDL-vocabulary gate,
    the evidence-span check, or has an unparseable confidence."""
    if not isinstance(raw, dict):
        return None
    technique = (raw.get("technique") or "").strip()
    if technique not in _DDL_PROPAGANDA_TECHNIQUES:
        logger.warning(
            f"propaganda engine: LLM returned technique {technique!r}, outside "
            "analysis.propaganda_technique -- dropping. If this is a genuine "
            "new technique, extend the enum (ALTER TYPE ... ADD VALUE) first."
        )
        return None
    evidence = (raw.get("evidence_span") or "").strip()
    if not validation.validate_evidence_span(evidence, source_text):
        return None
    try:
        confidence = float(raw.get("confidence", 0.5))
    except (TypeError, ValueError):
        return None
    confidence = max(0.0, min(1.0, confidence))
    return PropagandaTechniqueOutcome(technique=technique, confidence=confidence, evidence_span=evidence)


def _resolve_model_id() -> str:
    """Mirrors text.py's per-engine stopgap -- generalizes in Phase 7."""
    settings = get_settings()
    if settings.llm_backend.lower() == "ollama":
        return settings.ollama_model
    return settings.gemini_model


def process(doc: PropagandaDocInput, client: LLMClient) -> int:
    """Analyze `doc` and persist one `analysis.runs` ('propaganda') row plus
    its `propaganda_results` + `propaganda_techniques` rows. Returns the new
    run_id."""
    store.register_prompt_version(
        PROPAGANDA_PROMPT_VERSION, PROPAGANDA_TASK,
        PROPAGANDA_SYSTEM_PROMPT, PROPAGANDA_USER_PROMPT_TEMPLATE,
    )
    model_id = _resolve_model_id()

    try:
        result = analyze(doc, client)
    except Exception as exc:
        logger.warning(f"propaganda engine failed for doc={doc.doc_id}: {exc}")
        handle = store.open_run(
            PROPAGANDA_TASK, doc_id=doc.doc_id, model_id=model_id,
            prompt_version=PROPAGANDA_PROMPT_VERSION, inference_method="llm",
        )
        return handle.finish("failed", error=str(exc))

    prompt_version = PROPAGANDA_PROMPT_VERSION if result.inference_method == "llm" else None
    handle = store.open_run(
        PROPAGANDA_TASK, doc_id=doc.doc_id, model_id=model_id,
        prompt_version=prompt_version, inference_method=result.inference_method,
    )
    handle.save_propaganda(
        store.PropagandaResultRow(
            density=result.density, summary=result.summary,
            techniques_validated=result.techniques_validated,
            techniques_dropped=result.techniques_dropped,
        ),
        [
            store.PropagandaTechniqueRow(
                technique=t.technique, evidence_span=t.evidence_span, confidence=t.confidence,
            )
            for t in result.techniques
        ],
    )

    # Run confidence: the old job_runner passed result.overall_propaganda_score
    # straight through as ai_outputs.confidence (run_propaganda_detection ->
    # save_ai_output(..., result.overall_propaganda_score, ...)) -- the
    # self-reported overall score, NOT a mean over per-technique confidences
    # (contrast text.py's unified run, which averages sentiment+favorability
    # because those are two independently-scored facets of one run). This
    # run mirrors that: density IS the run confidence.
    return handle.finish("done", confidence=result.density, raw_response=result.raw_response)
