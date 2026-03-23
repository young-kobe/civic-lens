"""
Hybrid Sentiment Analyzer for Civic Lens.

Uses a two-layer approach:
1. Deterministic layer: Compute raw sentiment signals
2. LLM layer: Taxonomy labeling with evidence spans (when enabled)
"""

from typing import Any, Dict, List, Optional, Tuple
from analysis.src.common.logger import get_logger
from analysis.src.engine.constants import (
    POSITIVE_WORDS, NEGATIVE_WORDS, INTENSIFIERS, NEGATORS,
    STRONG_CONFIDENCE_THRESHOLD,
)
from analysis.src.engine.models import SentimentResult
from analysis.src.engine.prompts import SENTIMENT_SYSTEM_PROMPT, SENTIMENT_USER_PROMPT_TEMPLATE
from analysis.src.llm.schemas import SENTIMENT_SCHEMA

logger = get_logger(__name__)


class HybridSentimentAnalyzer:
    """
    Hybrid sentiment analyzer combining deterministic signals with LLM interpretation.
    """
    
    def __init__(
        self,
        model_name: str = "distilbert-base-uncased-finetuned-sst-2-english",
        llm_enabled: bool = False,
    ):
        self.model_name = model_name
        self.llm_enabled = llm_enabled
        self._llm_client = None
        
        logger.info(f"Initialized HybridSentimentAnalyzer (llm_enabled={llm_enabled})")
        
        if llm_enabled:
            self._init_llm_client()
    
    def _init_llm_client(self):
        """Initialize the LLM client. Raises if enabled but unavailable."""
        try:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                raise RuntimeError(
                    "LLM client not available but llm_enabled=True. "
                    "Start the Ollama server or set CIVIC_LLM_ENABLED=false."
                )
        except RuntimeError:
            raise
        except Exception as e:
            raise RuntimeError(f"Failed to initialize LLM client: {e}") from e
    
    def _compute_signals(self, text: str) -> Dict[str, Any]:
        """
        Compute deterministic sentiment signals from text.
        
        Returns raw counts and detected phrases for transparency.
        """
        if not text:
            return {
                "positive_count": 0,
                "negative_count": 0,
                "positive_words": [],
                "negative_words": [],
                "has_intensifiers": False,
                "has_negators": False,
                "word_count": 0,
            }
        
        words = text.lower().split()
        
        # Find positive and negative words
        pos_found = [w for w in words if w in POSITIVE_WORDS]
        neg_found = [w for w in words if w in NEGATIVE_WORDS]
        
        # Check for intensifiers and negators
        has_intensifiers = any(w in INTENSIFIERS for w in words)
        has_negators = any(w in NEGATORS for w in words)
        
        return {
            "positive_count": len(pos_found),
            "negative_count": len(neg_found),
            "positive_words": list(set(pos_found)),
            "negative_words": list(set(neg_found)),
            "has_intensifiers": has_intensifiers,
            "has_negators": has_negators,
            "word_count": len(words),
        }
    
    def _heuristic_classify(self, signals: Dict[str, Any]) -> SentimentResult:
        """
        Classify sentiment based on deterministic signals only.
        
        Used as fallback when LLM is disabled or unavailable.
        """
        pos_count = signals["positive_count"]
        neg_count = signals["negative_count"]
        total = pos_count + neg_count
        
        if total == 0:
            return SentimentResult(
                label="NEUTRAL",
                confidence=0.5,
                evidence_spans=[],
                reasoning="No sentiment indicators detected",
                deterministic_signals=signals,
            )
        
        # Adjust for negators (basic negation handling)
        if signals["has_negators"]:
            # Negators might flip sentiment - reduce confidence
            confidence_penalty = 0.15
        else:
            confidence_penalty = 0.0
        
        if pos_count > neg_count:
            ratio = pos_count / total
            confidence = min(0.5 + (0.5 * ratio) - confidence_penalty, 1.0)
            label = "POSITIVE"
            evidence = signals["positive_words"][:3]
        elif neg_count > pos_count:
            ratio = neg_count / total
            confidence = min(0.5 + (0.5 * ratio) - confidence_penalty, 1.0)
            label = "NEGATIVE"
            evidence = signals["negative_words"][:3]
        else:
            label = "MIXED"
            confidence = 0.5
            evidence = signals["positive_words"][:2] + signals["negative_words"][:2]
        
        return SentimentResult(
            label=label,
            confidence=round(confidence, 3),
            evidence_spans=evidence,
            reasoning=f"Heuristic classification based on {pos_count} positive and {neg_count} negative indicators",
            deterministic_signals=signals,
        )
    
    def _llm_classify(self, text: str, signals: Dict[str, Any]) -> SentimentResult:
        """
        Classify sentiment using LLM with deterministic signals as context.
        """
        user_prompt = SENTIMENT_USER_PROMPT_TEMPLATE.format(
            text=text[:2000],  # Truncate for token limits
            positive_count=signals["positive_count"],
            negative_count=signals["negative_count"],
            has_intensifiers=signals["has_intensifiers"],
            has_negators=signals["has_negators"],
        )
        
        try:
            response = self._llm_client.complete(
                system_prompt=SENTIMENT_SYSTEM_PROMPT,
                user_prompt=user_prompt,
                response_schema=SENTIMENT_SCHEMA,
            )
            return SentimentResult(
                label=response.get("label", "NEUTRAL"),
                confidence=float(response.get("confidence", 0.5)),
                evidence_spans=response.get("evidence_spans", []),
                reasoning=response.get("reasoning"),
                sarcasm_detected=bool(response.get("sarcasm_detected", False)),
                deterministic_signals=signals,
            )
            
        except Exception as e:
            logger.error(f"LLM classification failed: {e}. Falling back to heuristics.")
            return self._heuristic_classify(signals)
    
    def analyze(self, text: str) -> Tuple[str, float]:
        """
        Analyze sentiment of text.
        
        Returns:
            Tuple of (label, confidence) for backwards compatibility.
            Use analyze_full() for complete results with evidence.
        """
        result = self.analyze_full(text)
        return result.label, result.confidence
    
    def analyze_full(self, text: str) -> SentimentResult:
        """
        Analyze sentiment with full results including evidence.
        
        LLM is the primary classifier. Heuristic signals are computed first
        and fed as supplemental context to the LLM prompt. Heuristics are
        only used as a fallback when the LLM is unavailable.
        
        Returns:
            SentimentResult with label, confidence, evidence spans, and reasoning.
        """
        if not text:
            return SentimentResult(
                label="NEUTRAL",
                confidence=0.0,
                evidence_spans=[],
                reasoning="Empty text",
            )
        
        # 1. Compute deterministic signals (always, used as LLM context)
        signals = self._compute_signals(text)
        
        # 2. LLM is primary classifier
        if self.llm_enabled and self._llm_client and self._llm_client.is_available:
            return self._llm_classify(text, signals)
        
        # 3. Heuristic fallback only when LLM unavailable
        logger.warning("LLM unavailable, using heuristic fallback for sentiment")
        return self._heuristic_classify(signals)