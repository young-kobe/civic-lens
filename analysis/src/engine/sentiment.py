"""
Hybrid Sentiment Analyzer for Civic Lens.

Uses a two-layer approach:
1. Deterministic layer: Compute raw sentiment signals
2. LLM layer: Taxonomy labeling with evidence spans (when enabled)
"""

from typing import Any, Dict, List, Optional, Tuple
from analysis.src.common.logger import get_logger
from analysis.src.engine.models import SentimentResult

logger = get_logger(__name__)


# Sentiment word lists for deterministic analysis
POSITIVE_WORDS = frozenset([
    "good", "great", "excellent", "amazing", "love", "support", "agree",
    "wonderful", "fantastic", "brilliant", "outstanding", "positive",
    "beneficial", "successful", "effective", "impressive", "promising",
    "strong", "robust", "healthy", "progress", "improve", "win", "victory",
    "approve", "praise", "commend", "celebrate", "achieve", "accomplish"
])

NEGATIVE_WORDS = frozenset([
    "bad", "terrible", "hate", "awful", "stupid", "wrong", "disagree",
    "fake", "failure", "disaster", "corrupt", "scandal", "crisis",
    "dangerous", "harmful", "threat", "attack", "destroy", "collapse",
    "reject", "oppose", "condemn", "criticize", "blame", "accuse",
    "incompetent", "unacceptable", "outrageous", "shocking", "disgrace"
])

INTENSIFIERS = frozenset([
    "very", "extremely", "incredibly", "absolutely", "completely",
    "totally", "utterly", "highly", "deeply", "strongly"
])

NEGATORS = frozenset([
    "not", "no", "never", "neither", "nobody", "nothing", "nowhere",
    "hardly", "barely", "scarcely", "without", "isn't", "aren't",
    "wasn't", "weren't", "won't", "wouldn't", "couldn't", "shouldn't"
])


class HybridSentimentAnalyzer:
    """
    Hybrid sentiment analyzer combining deterministic signals with LLM interpretation.
    """
    
    # LLM prompt templates
    SYSTEM_PROMPT = """You are a sentiment classifier for political news and social media content.
Your task is to classify the sentiment expressed toward the main subject of the text.

RULES:
1. Return ONLY valid JSON matching the schema below
2. Cite specific phrases from the text as evidence (use exact quotes)
3. If uncertain, set confidence < 0.7
4. Do not infer sentiment not explicitly present in the text
5. Consider context and nuance - sarcasm, irony, or mixed feelings affect classification

OUTPUT SCHEMA:
{
  "label": "POSITIVE" | "NEGATIVE" | "NEUTRAL" | "MIXED",
  "confidence": 0.0-1.0,
  "evidence_spans": ["quoted phrase 1", "quoted phrase 2"],
  "reasoning": "Brief explanation of classification"
}"""

    USER_PROMPT_TEMPLATE = """Classify the sentiment of the following text:

\"\"\"{text}\"\"\"

Pre-computed signals for context:
- Positive indicators found: {positive_count}
- Negative indicators found: {negative_count}
- Intensifiers present: {has_intensifiers}
- Negators present: {has_negators}"""

    def __init__(
        self,
        model_name: str = "distilbert-base-uncased-finetuned-sst-2-english",
        llm_enabled: bool = False,
    ):
        self.model_name = model_name
        self.llm_enabled = llm_enabled
        self._gemini_client = None
        
        logger.info(f"Initialized HybridSentimentAnalyzer (llm_enabled={llm_enabled})")
        
        if llm_enabled:
            self._init_llm_client()
    
    def _init_llm_client(self):
        """Initialize the LLM client if enabled."""
        try:
            from analysis.src.common.llm_client import get_gemini_client
            self._gemini_client = get_gemini_client()
            if not self._gemini_client.is_available:
                logger.warning("Gemini client not available. Falling back to heuristics.")
                self.llm_enabled = False
        except Exception as e:
            logger.error(f"Failed to initialize LLM client: {e}")
            self.llm_enabled = False
    
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
        user_prompt = self.USER_PROMPT_TEMPLATE.format(
            text=text[:2000],  # Truncate for token limits
            positive_count=signals["positive_count"],
            negative_count=signals["negative_count"],
            has_intensifiers=signals["has_intensifiers"],
            has_negators=signals["has_negators"],
        )
        
        try:
            response = self._gemini_client.complete(
                system_prompt=self.SYSTEM_PROMPT,
                user_prompt=user_prompt,
            )
            
            return SentimentResult(
                label=response.get("label", "NEUTRAL"),
                confidence=float(response.get("confidence", 0.5)),
                evidence_spans=response.get("evidence_spans", []),
                reasoning=response.get("reasoning"),
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
        
        # 1. Compute deterministic signals
        signals = self._compute_signals(text)
        
        # 2. LLM classification if enabled and client available
        if self.llm_enabled and self._gemini_client and self._gemini_client.is_available:
            return self._llm_classify(text, signals)
        
        # 3. Fallback to heuristic
        return self._heuristic_classify(signals)


# Backwards-compatible alias
SentimentAnalyzer = HybridSentimentAnalyzer
