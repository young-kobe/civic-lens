"""
Unified evidence-span validator (Postgres redesign Phase 5, code design
principle 3). Consolidates the four drifted per-engine validators
(analyzer.py, claim_extractor.py, propaganda_detector.py, target_extractor.py)
into one set of pure functions; Phase 6 ports each engine onto this module.
Constant choices and disagreement-resolution rationale:
docs/audit-trail/analysis/2026-07-22-pg-analysis-plumbing.md.
"""

from __future__ import annotations

from typing import List, Tuple

from analysis.src.engine.constants import (
    MAX_CLAIM_WORDS,
    MIN_CLAIM_WORDS,
    MIN_EVIDENCE_WORDS,
    UNVERIFIED_EVIDENCE_CONFIDENCE_CAP,
)


def validate_evidence_span(span: str, source_text: str) -> bool:
    """True if span is a verbatim, case-insensitive substring of
    source_text with at least MIN_EVIDENCE_WORDS words. Non-string input,
    empty spans, and whitespace-only spans are invalid."""
    if not isinstance(span, str):
        return False
    cleaned = span.strip()
    if len(cleaned.split()) < MIN_EVIDENCE_WORDS:
        return False
    return cleaned.lower() in source_text.lower()


def validate_spans(spans: List[str], source_text: str) -> Tuple[List[str], bool]:
    """Filter spans to those passing validate_evidence_span.

    Returns (valid_spans, had_invalid). had_invalid is True only when spans
    was non-empty but zero entries survived — the exact signal the four
    originals used to decide whether to cap confidence via
    cap_confidence_if_unverified.
    """
    valid = [s.strip() for s in spans if validate_evidence_span(s, source_text)]
    had_invalid = bool(spans) and not valid
    return valid, had_invalid


def cap_confidence_if_unverified(confidence: float, verified: bool) -> float:
    """Clamp confidence to [0, 1]; if not verified, further cap it at
    UNVERIFIED_EVIDENCE_CONFIDENCE_CAP. Fabricated or unverifiable evidence
    (invariant B2) must not carry full confidence into public output."""
    clamped = max(0.0, min(1.0, confidence))
    if verified:
        return clamped
    return min(clamped, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)


def validate_claim_length(claim_text: str) -> bool:
    """True if claim_text's whitespace-split word count falls within
    [MIN_CLAIM_WORDS, MAX_CLAIM_WORDS] inclusive. claim_extractor-specific;
    the other three validators have no analogous bound."""
    word_count = len(claim_text.split())
    return MIN_CLAIM_WORDS <= word_count <= MAX_CLAIM_WORDS
