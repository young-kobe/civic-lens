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
    deterministic_signals: Optional[Dict[str, Any]] = None  # Raw computed signals
    
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
    """
    is_bot: bool
    label: str  # human, bot, suspicious
    confidence: float  # 0.0 - 1.0
    indicators: List[str]  # Specific behavioral indicators
    reasoning: Optional[str] = None
    deterministic_signals: Optional[Dict[str, Any]] = None
    
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
    def to_dict(self) -> Dict[str, Any]:
        result = asdict(self)
        result["entity_stances"] = [asdict(es) for es in self.entity_stances]
        return result
