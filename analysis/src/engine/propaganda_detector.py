"""
Propaganda-technique detector for Civic Lens (walkthrough 042).

LLM-driven classifier that flags one or more of six starter techniques
(loaded_language, name_calling, ad_hominem, appeal_to_fear, whataboutism,
doubt_casting) in a political-content document, each with a verbatim
evidence span.

Design choices:

- Evidence validation is strict (same as sentiment / claims): a flagged
  technique whose ``evidence_span`` is under 4 words or not a case-insensitive
  substring of the source text is dropped. If the LLM returned techniques but
  none validated, ``overall_propaganda_score`` is capped at 0.2 so a
  hallucinating model cannot drive headline propaganda numbers.
- Deterministic fallback is intentionally NOT provided. Propaganda-technique
  detection is a language-understanding task that heuristics cannot do
  honestly. When the LLM is unavailable the detector returns an empty
  result and job_runner skips the doc — better than a fabricated verdict.
- Scope: all political content (news + social) flows through this step.
  Honest reporting of propaganda patterns requires scoring both sides of the
  social/news split, which 043's aggregator will present separately.
"""

from __future__ import annotations

from typing import List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.engine.models import PropagandaResult, PropagandaTechnique
from analysis.src.engine.prompts import (
    PROPAGANDA_SYSTEM_PROMPT,
    PROPAGANDA_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import (
    PROPAGANDA_SCHEMA,
    PROPAGANDA_TECHNIQUE_ENUM,
)

logger = get_logger(__name__)

MIN_EVIDENCE_WORDS = 4
UNVERIFIED_EVIDENCE_CAP = 0.2


def _validate_technique(raw: dict, source_text: str) -> Optional[PropagandaTechnique]:
    """Return a validated technique, or None if the raw record fails any rule."""
    if not isinstance(raw, dict):
        return None
    technique = (raw.get("technique") or "").strip()
    if technique not in PROPAGANDA_TECHNIQUE_ENUM:
        return None
    evidence = (raw.get("evidence_span") or "").strip()
    if not evidence or len(evidence.split()) < MIN_EVIDENCE_WORDS:
        return None
    if evidence.lower() not in source_text.lower():
        return None  # fabricated evidence
    try:
        confidence = float(raw.get("confidence", 0.5))
    except (TypeError, ValueError):
        return None
    confidence = max(0.0, min(1.0, confidence))
    return PropagandaTechnique(
        technique=technique,
        confidence=confidence,
        evidence_span=evidence,
    )


class PropagandaDetector:
    """LLM-driven propaganda-technique classifier."""

    def __init__(self, llm_enabled: bool = False):
        self.llm_enabled = llm_enabled
        self._llm_client = None
        logger.info(f"Initialized PropagandaDetector (llm_enabled={llm_enabled})")
        if llm_enabled:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                raise RuntimeError("LLM client not available for PropagandaDetector")

    def detect(self, text: str) -> PropagandaResult:
        """Analyze one doc. Returns an empty result when LLM is disabled/fails."""
        if not text or not self.llm_enabled or self._llm_client is None:
            return PropagandaResult()
        if not self._llm_client.is_available:
            return PropagandaResult()

        try:
            response = self._llm_client.complete(
                system_prompt=PROPAGANDA_SYSTEM_PROMPT,
                user_prompt=PROPAGANDA_USER_PROMPT_TEMPLATE.format(text=text[:2500]),
                response_schema=PROPAGANDA_SCHEMA,
            )
        except Exception as e:
            logger.warning(f"Propaganda LLM call failed: {e}")
            return PropagandaResult()

        raw_techniques = response.get("techniques") or []
        # Enforce the schema's cap of 5 defensively.
        raw_techniques = raw_techniques[:5]
        validated: List[PropagandaTechnique] = []
        for raw in raw_techniques:
            t = _validate_technique(raw, text)
            if t is not None:
                validated.append(t)

        try:
            overall = float(response.get("overall_propaganda_score", 0.0))
        except (TypeError, ValueError):
            overall = 0.0
        overall = max(0.0, min(1.0, overall))

        # Handle the three outcomes:
        # - LLM returned techniques AND at least one validated: trust overall.
        # - LLM returned techniques but NONE validated: cap overall at 0.2.
        # - LLM returned zero techniques: force overall to 0.0.
        if not validated:
            if raw_techniques:
                overall = min(overall, UNVERIFIED_EVIDENCE_CAP)
            else:
                overall = 0.0

        return PropagandaResult(
            techniques=validated,
            overall_propaganda_score=round(overall, 3),
            reasoning=response.get("reasoning"),
            inference_method="llm",
        )
