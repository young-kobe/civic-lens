"""
Claim extractor for Civic Lens.

LLM-driven extraction of discrete claim statements from doc text. Each claim
is paraphrased into a canonical form (5-15 words) plus a verbatim
evidence_span that anchors it to the source — failing the verbatim check
drops the claim entirely, so a model that hallucinates can't silently
populate the narrative layer.
"""

from dataclasses import dataclass, asdict, field
from typing import List, Optional

from analysis.src.common.logger import get_logger
from analysis.src.llm.context_seeds import format_seeds_block, match_seeds, matched_slugs
from analysis.src.llm.prompts import (
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
    # Slugs of any reference seeds that matched the doc's text and were
    # injected into the LLM prompt. Persisted into ai_outputs.output_json
    # as audit metadata so reviewers can re-derive which references the
    # model saw for a given claim row.
    reference_seeds: List[str] = field(default_factory=list)

    def to_dict(self) -> dict:
        out: dict = {"claims": [c.to_dict() for c in self.claims]}
        if self.reference_seeds:
            out["reference_seeds"] = list(self.reference_seeds)
        return out


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

    def __init__(self, llm_enabled: bool = False, llm_client=None):
        # llm_client lets callers (eval harness, tests) supply a BaseLLMClient
        # directly instead of going through the factory singleton. Production
        # callers pass llm_enabled=True and use the configured backend.
        self.llm_enabled = llm_enabled or llm_client is not None
        self._llm_client = llm_client
        logger.info(f"Initialized ClaimExtractor (llm_enabled={self.llm_enabled})")
        if self.llm_enabled and self._llm_client is None:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                raise RuntimeError("LLM client not available for ClaimExtractor")

    def extract(self, text: str) -> ClaimExtractionResult:
        if not text or not self.llm_enabled or self._llm_client is None:
            return ClaimExtractionResult(claims=[])
        if not self._llm_client.is_available:
            return ClaimExtractionResult(claims=[])

        seeded = match_seeds(text)
        seeds_block = format_seeds_block(seeded)
        user_prompt = seeds_block + CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE.format(text=text[:2000])

        try:
            response = self._llm_client.complete(
                system_prompt=CLAIM_EXTRACTION_SYSTEM_PROMPT,
                user_prompt=user_prompt,
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

        return ClaimExtractionResult(
            claims=validated,
            reference_seeds=matched_slugs(seeded),
        )
