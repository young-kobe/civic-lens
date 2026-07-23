"""
Claim extraction engine (Postgres redesign Phase 6, Wave 2). `analyze()` is
pure (no DB access); `process()` composes it with `results/store.py` to
write one `analysis.runs` ('claims') row plus its `claims` rows.

Ported behavior from the old `engine/claim_extractor.py`: a claim whose
evidence_span fails the verbatim-substring check is DROPPED ENTIRELY, not
capped in confidence -- unlike the text engine's sentiment/favorability
spans (which survive with confidence capped). The old code's rationale
(module docstring: "failing the verbatim check drops the claim entirely,
so a model that hallucinates can't silently populate the narrative layer")
still applies now that `claims` anchors narratives via
`analysis.narratives.anchor_claim_id`, and `analysis.claims` has no
evidence_span column to persist a capped-but-kept row against.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.engine import validation
from analysis.src.engine.constants import (
    CLAIM_TEXT_MAX_CHARS,
    CLAIMS_TASK,
    MAX_CLAIMS_PER_DOC,
    ZERO_CLAIMS_CONFIDENCE,
)
from analysis.src.engine.text_prep import is_trivial_content, truncate_at_sentence
from analysis.src.llm.client import LLMClient
from analysis.src.llm.context_seeds import format_seeds_block, match_seeds
from analysis.src.llm.prompts import (
    CLAIM_EXTRACTION_PROMPT_VERSION,
    CLAIM_EXTRACTION_SYSTEM_PROMPT,
    CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import CLAIM_EXTRACTION_SCHEMA
from analysis.src.results import store

logger = get_logger(__name__)

# CLAIMS_TASK, CLAIM_TEXT_MAX_CHARS, MAX_CLAIMS_PER_DOC,
# ZERO_CLAIMS_CONFIDENCE consolidated into engine/constants.py (Postgres
# redesign Phase 6, constants-consolidation pass) -- imported above.


@dataclass(frozen=True)
class ClaimDocInput:
    """One doc's claims-engine input, assembled by the caller from
    corpus.documents."""

    doc_id: int
    text: str


@dataclass(frozen=True)
class ClaimOutcome:
    claim_text: str
    confidence: float
    evidence_span: str  # verbatim, kept for traceability; not persisted --
    # analysis.claims has no evidence_span column (see module docstring).


@dataclass(frozen=True)
class ClaimsAnalysis:
    """The validated outcome of one `analyze()` call. `dropped_count` covers
    every raw claim the LLM proposed that failed a validation rule (bad
    shape, length bounds, or unverifiable evidence) -- `extracted_count`
    (== len(claims)) is what survived."""

    claims: List[ClaimOutcome]
    extracted_count: int
    dropped_count: int
    inference_method: str  # 'llm' | 'deterministic'
    raw_response: Optional[Dict]  # verbatim LLM payload; None for the trivial short-circuit


def analyze(doc: ClaimDocInput, client: LLMClient) -> ClaimsAnalysis:
    """Extract claims from one doc. No DB access. Raises whatever
    `client.complete()` raises (RuntimeError, after retries are exhausted)
    when the LLM call fails or the backend is unavailable -- `process()`
    below is what turns that into a recorded failed run."""
    if is_trivial_content(doc.text):
        return ClaimsAnalysis(
            claims=[], extracted_count=0, dropped_count=0,
            inference_method="deterministic", raw_response=None,
        )

    seeds_block = format_seeds_block(match_seeds(doc.text))
    user_prompt = seeds_block + CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE.format(
        text=truncate_at_sentence(doc.text, CLAIM_TEXT_MAX_CHARS)
    )
    response = client.complete(
        system_prompt=CLAIM_EXTRACTION_SYSTEM_PROMPT,
        user_prompt=user_prompt,
        response_schema=CLAIM_EXTRACTION_SCHEMA,
    )

    claims, dropped_count = _build_claims(response, doc.text)
    if dropped_count:
        logger.info(f"claims engine doc={doc.doc_id}: dropped {dropped_count} claim(s)")

    return ClaimsAnalysis(
        claims=claims, extracted_count=len(claims), dropped_count=dropped_count,
        inference_method="llm", raw_response=response,
    )


def _build_claims(response: dict, source_text: str) -> tuple[List[ClaimOutcome], int]:
    claims: List[ClaimOutcome] = []
    dropped_count = 0
    raw_claims = response.get("claims", []) or []
    for raw in raw_claims[:MAX_CLAIMS_PER_DOC]:
        outcome = _validate_claim(raw, source_text)
        if outcome is None:
            dropped_count += 1
        else:
            claims.append(outcome)
    return claims, dropped_count


def _validate_claim(raw: dict, source_text: str) -> Optional[ClaimOutcome]:
    """Return a validated claim, or None if the raw record fails any rule.
    Evidence failure DROPS the claim (see module docstring) -- there is no
    cap-confidence path here, unlike text.py's sentiment/favorability spans."""
    if not isinstance(raw, dict):
        return None
    claim_text = (raw.get("claim") or "").strip()
    evidence = (raw.get("evidence_span") or "").strip()
    if not claim_text or not evidence:
        return None
    if not validation.validate_claim_length(claim_text):
        return None
    if not validation.validate_evidence_span(evidence, source_text):
        return None

    confidence = validation.cap_confidence_if_unverified(
        raw.get("confidence", 0.5), verified=True,  # verified=True: just clamps to [0, 1]
    )
    return ClaimOutcome(claim_text=claim_text, confidence=confidence, evidence_span=evidence)


def _resolve_model_id() -> str:
    """Mirrors text.py's model_id resolution (settings.llm_backend selects
    gemini_model vs ollama_model). A per-engine stopgap until Phase 7
    generalizes model_id resolution into the scheduler."""
    settings = get_settings()
    if settings.llm_backend.lower() == "ollama":
        return settings.ollama_model
    return settings.gemini_model


def process(doc: ClaimDocInput, client: LLMClient) -> store.RunOutcome:
    """Analyze `doc` and persist its claims under one analysis.runs
    ('claims') row. Zero surviving claims is a legitimate outcome: a
    'done' run with no analysis.claims rows. Returns the RunOutcome from
    store.RunHandle.finish()."""
    store.register_prompt_version(
        CLAIM_EXTRACTION_PROMPT_VERSION, CLAIMS_TASK,
        CLAIM_EXTRACTION_SYSTEM_PROMPT, CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE,
    )
    model_id = _resolve_model_id()

    try:
        result = analyze(doc, client)
    except Exception as exc:
        logger.warning(f"claims engine failed for doc={doc.doc_id}: {exc}")
        handle = store.open_run(
            CLAIMS_TASK, doc_id=doc.doc_id, model_id=model_id,
            prompt_version=CLAIM_EXTRACTION_PROMPT_VERSION, inference_method="llm",
        )
        return handle.finish("failed", error=str(exc))

    prompt_version = CLAIM_EXTRACTION_PROMPT_VERSION if result.inference_method == "llm" else None
    handle = store.open_run(
        CLAIMS_TASK, doc_id=doc.doc_id, model_id=model_id,
        prompt_version=prompt_version, inference_method=result.inference_method,
    )
    handle.save_claims([
        store.ClaimRow(claim_text=c.claim_text, confidence=c.confidence)
        for c in result.claims
    ])

    # Run-level confidence: mean of every claim's own confidence, matching
    # the old job_runner's row_confidence convention -- 0.0 (not skipped)
    # when zero claims survived.
    confidences = [c.confidence for c in result.claims]
    run_confidence = sum(confidences) / len(confidences) if confidences else ZERO_CLAIMS_CONFIDENCE

    return handle.finish("done", confidence=run_confidence, raw_response=result.raw_response)
