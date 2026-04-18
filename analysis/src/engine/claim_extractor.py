"""
Claim extractor for Civic Lens.

LLM-driven extraction of discrete claim statements from doc text. Each claim
is paraphrased into a canonical form (5-15 words) plus a verbatim
evidence_span that anchors it to the source — failing the verbatim check
drops the claim entirely, so a model that hallucinates can't silently
populate the narrative layer.
"""

from dataclasses import dataclass, asdict
from typing import List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.engine.prompts import (
    CLAIM_EXTRACTION_SYSTEM_PROMPT,
    CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import CLAIM_EXTRACTION_SCHEMA

logger = get_logger(__name__)

# Evidence-span word-count floor. Claims are shorter than sentiment excerpts,
# so we allow 3+ words here (versus 4+ for sentiment).
MIN_EVIDENCE_WORDS = 3
# Canonical claim bounds — rejecting fragments that look like headline scraps.
MIN_CLAIM_WORDS = 4
MAX_CLAIM_WORDS = 20


@dataclass
class ExtractedClaim:
    claim: str
    confidence: float
    evidence_span: str

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class ClaimExtractionResult:
    claims: List[ExtractedClaim]
    reasoning: Optional[str] = None

    def to_dict(self) -> dict:
        return {"claims": [c.to_dict() for c in self.claims]}


def _validate_claim(raw: dict, source_text: str) -> Optional[ExtractedClaim]:
    """Return a validated claim, or None if the raw record fails any rule."""
    if not isinstance(raw, dict):
        return None
    claim_text = (raw.get("claim") or "").strip()
    evidence = (raw.get("evidence_span") or "").strip()
    try:
        confidence = float(raw.get("confidence", 0.5))
    except (TypeError, ValueError):
        return None
    if not claim_text or not evidence:
        return None

    claim_words = claim_text.split()
    if not (MIN_CLAIM_WORDS <= len(claim_words) <= MAX_CLAIM_WORDS):
        return None
    if len(evidence.split()) < MIN_EVIDENCE_WORDS:
        return None
    if evidence.lower() not in source_text.lower():
        return None  # Fabricated evidence.

    confidence = max(0.0, min(1.0, confidence))
    return ExtractedClaim(claim=claim_text, confidence=confidence, evidence_span=evidence)


class ClaimExtractor:
    """Extract claim statements from doc text via LLM."""

    def __init__(self, llm_enabled: bool = False):
        self.llm_enabled = llm_enabled
        self._llm_client = None
        logger.info(f"Initialized ClaimExtractor (llm_enabled={llm_enabled})")
        if llm_enabled:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                raise RuntimeError("LLM client not available for ClaimExtractor")

    def extract(self, text: str) -> ClaimExtractionResult:
        if not text or not self.llm_enabled or self._llm_client is None:
            return ClaimExtractionResult(claims=[])
        if not self._llm_client.is_available:
            return ClaimExtractionResult(claims=[])

        try:
            response = self._llm_client.complete(
                system_prompt=CLAIM_EXTRACTION_SYSTEM_PROMPT,
                user_prompt=CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE.format(text=text[:2000]),
                response_schema=CLAIM_EXTRACTION_SCHEMA,
            )
        except Exception as e:
            logger.warning(f"Claim extraction LLM call failed: {e}")
            return ClaimExtractionResult(claims=[])

        raw_claims = response.get("claims", []) or []
        validated: List[ExtractedClaim] = []
        for raw in raw_claims[:3]:  # enforce schema's ≤3 cap defensively
            claim = _validate_claim(raw, text)
            if claim is not None:
                validated.append(claim)

        return ClaimExtractionResult(claims=validated)
