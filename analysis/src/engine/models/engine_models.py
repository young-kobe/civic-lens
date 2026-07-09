"""
Data models for analysis engine modules.

Contains dataclasses for sentiment, bot detection, and favorability analysis results.
"""

from dataclasses import dataclass, asdict, field
from typing import Any, Dict, List, Optional


# =============================================================================
# Sentiment Analysis Models
# =============================================================================

@dataclass
class SentimentResult:
    """
    Sentiment analysis result with evidence.

    Satisfies invariant B2: AI outputs include confidence and evidence.
    """
    label: str  # POSITIVE, NEGATIVE, NEUTRAL, MIXED
    confidence: float  # 0.0 - 1.0
    evidence_spans: List[str]  # Specific phrases supporting classification
    reasoning: Optional[str] = None  # Explanation (LLM only)
    sarcasm_detected: bool = False  # Whether sarcasm/irony was detected
    deterministic_signals: Optional[Dict[str, Any]] = None  # Raw computed signals
    # 'llm' when the classification came from a validated LLM response;
    # 'heuristic' when the LLM was unavailable / errored / failed schema
    # validation and the deterministic fallback produced this row.
    inference_method: str = "llm"

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


# =============================================================================
# Bot Detection Models
# =============================================================================

@dataclass
class BotResult:
    """
    Bot detection result with evidence.

    Satisfies invariant B2: AI outputs include confidence and evidence.

    ``llm_text_likelihood`` (walkthrough 040) is a separate 0-1 score for
    "does this TEXT look LLM-generated" — independent of whether the ACCOUNT
    is flagged as a bot. A government press release can score high on
    llm_text_likelihood while is_bot remains False (de-biased).
    """
    is_bot: bool
    label: str  # human, bot, suspicious, unknown
    confidence: float  # 0.0 - 1.0
    indicators: List[str]
    reasoning: Optional[str] = None
    deterministic_signals: Optional[Dict[str, Any]] = None
    inference_method: str = "llm"
    llm_text_likelihood: float = 0.0
    # Populated on heuristic-fallback rows to record WHY the fallback happened
    # (e.g. 'SchemaValidationError: confidence above maximum', 'Timeout'). The
    # 'what' is inference_method=heuristic; this is the 'why'. Audit §9.
    fallback_reason: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return asdict(self)


# =============================================================================
# Favorability Analysis Models
# =============================================================================

@dataclass
class EntityStance:
    """Stance toward a single entity."""
    entity: str
    stance: str  # favorable, unfavorable, neutral, mixed
    confidence: float
    evidence_spans: List[str] = field(default_factory=list)


# =============================================================================
# Propaganda Detection Models (walkthrough 042)
# =============================================================================

@dataclass
class PropagandaTechnique:
    """One flagged propaganda technique with verbatim evidence."""
    technique: str   # one of PROPAGANDA_TECHNIQUE_ENUM
    confidence: float
    evidence_span: str


@dataclass
class PropagandaResult:
    """
    Propaganda-detection output for a single doc.

    ``techniques`` is 0..5 flagged entries, each validated to have a verbatim
    evidence span from the source text (invariant B2). An empty list means
    the text contained no detectable propaganda techniques.

    ``overall_propaganda_score`` is the summary 0-1 density. When the LLM
    returned techniques but none validated (fabricated evidence), the score
    is capped at ``UNVERIFIED_EVIDENCE_CAP`` — same pattern as sentiment.
    """
    techniques: List[PropagandaTechnique] = field(default_factory=list)
    overall_propaganda_score: float = 0.0
    reasoning: Optional[str] = None
    # No default 'llm' here: a bare PropagandaResult() is NOT a real LLM
    # verdict. The detector sets 'llm' or 'deterministic' explicitly on every
    # genuine result path; a None here means "not scored" (e.g. a failure
    # sentinel) and must never be persisted as though the model ran (audit A-3).
    inference_method: Optional[str] = None
    # True when the LLM call failed (transport error / unavailable client)
    # rather than legitimately finding no propaganda. job_runner MUST skip
    # persisting a failed result so the doc is re-queued next run.
    detection_failed: bool = False

    def to_dict(self) -> Dict[str, Any]:
        return {
            "techniques": [asdict(t) for t in self.techniques],
            "overall_propaganda_score": self.overall_propaganda_score,
            "reasoning": self.reasoning,
            "inference_method": self.inference_method,
        }


@dataclass
class FavorabilityResult:
    """
    Full favorability analysis result.

    Satisfies invariant B2: AI outputs include confidence and evidence.
    """
    entity_stances: List[EntityStance]
    overall_gop_stance: str  # favorable, unfavorable, neutral, mixed
    overall_confidence: float
    gop_entities_found: List[str]
    reasoning: Optional[str] = None
    deterministic_signals: Optional[Dict[str, Any]] = None
    # See SentimentResult.inference_method.
    inference_method: str = "llm"
    def to_dict(self) -> Dict[str, Any]:
        result = asdict(self)
        result["entity_stances"] = [asdict(es) for es in self.entity_stances]
        return result
