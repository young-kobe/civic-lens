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
