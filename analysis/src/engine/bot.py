"""
Hybrid Bot Detector for Civic Lens.

Uses a two-layer approach:
1. Deterministic layer: Compute coordination/automation signals
2. LLM layer: Classify suspicious patterns with explanations (when enabled)

Bot-flagged content is excluded from sentiment/favorability aggregations.
"""

from typing import Any, Dict, List, Optional, Tuple
from dataclasses import dataclass, asdict
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


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


# Spam/bot keywords commonly found in automated content
SPAM_KEYWORDS = frozenset([
    "buy now", "click here", "subscribe", "limited time offer",
    "crypto", "bitcoin", "make money", "viagra", "free gift",
    "act now", "don't miss", "exclusive deal", "earn money",
    "work from home", "100% free", "amazing offer", "urgent",
])

# Patterns suggesting coordinated behavior
COORDINATION_PATTERNS = [
    # Pattern name, detection function description
    ("repetitive_text", "Text contains highly repetitive phrases"),
    ("url_density", "Unusually high URL/link density"),
    ("hashtag_spam", "Excessive hashtag usage"),
    ("timing_burst", "Posted in coordination burst window"),
]


class HybridBotDetector:
    """
    Hybrid bot detector combining deterministic signals with LLM interpretation.
    """
    
    SYSTEM_PROMPT = """You are an analyst detecting automated/bot behavior in social media content.
Analyze the text and behavioral signals to classify if this is likely automated content.

RULES:
1. Return ONLY valid JSON matching the schema below
2. Base classification on provided signals, not assumptions
3. If data is insufficient, set is_bot=false with low confidence
4. Cite specific behavioral indicators as evidence
5. Do not assume intent - classify only observable patterns

OUTPUT SCHEMA:
{
  "is_bot": true | false,
  "label": "human" | "bot" | "suspicious",
  "confidence": 0.0-1.0,
  "indicators": ["specific indicator 1", "specific indicator 2"],
  "reasoning": "Brief explanation"
}"""

    USER_PROMPT_TEMPLATE = """Analyze this content for automated behavior:

TEXT:
\"\"\"{text}\"\"\"

BEHAVIORAL SIGNALS:
- Spam keyword matches: {spam_keyword_hits}
- Text repetition score: {repetition_score:.2f} (0-1, higher = more repetitive)
- Unique word ratio: {unique_ratio:.2f} (lower = more repetitive)
- URL count: {url_count}
- Hashtag count: {hashtag_count}
- Account age: {account_age_days} days (if available)
- Posting frequency: {posting_frequency} posts/day (if available)"""

    def __init__(self, llm_enabled: bool = False):
        self.llm_enabled = llm_enabled
        self._gemini_client = None
        
        logger.info(f"Initialized HybridBotDetector (llm_enabled={llm_enabled})")
        
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
    
    def _compute_signals(self, text: str, metadata: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
        """
        Compute deterministic bot/coordination signals.
        
        Returns measurable indicators with raw values for transparency.
        """
        if not text:
            return {
                "spam_keyword_hits": 0,
                "spam_keywords_found": [],
                "repetition_score": 0.0,
                "unique_ratio": 1.0,
                "url_count": 0,
                "hashtag_count": 0,
                "word_count": 0,
                "account_age_days": None,
                "posting_frequency": None,
                "aggregated_score": 0.0,
            }
        
        metadata = metadata or {}
        text_lower = text.lower()
        words = text_lower.split()
        word_count = len(words)
        
        # 1. Spam keyword detection
        spam_found = [kw for kw in SPAM_KEYWORDS if kw in text_lower]
        spam_hits = len(spam_found)
        
        # 2. Text repetition analysis
        unique_words = set(words)
        unique_ratio = len(unique_words) / word_count if word_count > 0 else 1.0
        # Low unique ratio with enough words suggests bot-like repetition
        repetition_score = max(0, 1 - unique_ratio) if word_count > 5 else 0.0
        
        # 3. URL detection (simplified pattern)
        url_count = text_lower.count("http://") + text_lower.count("https://")
        
        # 4. Hashtag detection
        hashtag_count = text.count("#")
        
        # 5. Metadata signals
        account_age = metadata.get("account_age_days")
        posting_freq = metadata.get("posts_per_day")
        
        # Compute aggregated score
        score = 0.0
        
        # Spam keywords are strong indicators
        if spam_hits > 0:
            score += 0.3 + (0.1 * min(spam_hits, 3))
        
        # High repetition is suspicious
        if repetition_score > 0.5:
            score += 0.25
        
        # Unusual URL density
        if word_count > 0 and url_count / max(word_count, 1) > 0.1:
            score += 0.15
        
        # Excessive hashtags
        if hashtag_count > 5:
            score += 0.1
        
        # Account metadata signals
        if account_age is not None and account_age < 7:
            score += 0.15  # Very new account
        if posting_freq is not None and posting_freq > 50:
            score += 0.2  # Unusually high posting rate
        
        return {
            "spam_keyword_hits": spam_hits,
            "spam_keywords_found": spam_found,
            "repetition_score": repetition_score,
            "unique_ratio": unique_ratio,
            "url_count": url_count,
            "hashtag_count": hashtag_count,
            "word_count": word_count,
            "account_age_days": account_age,
            "posting_frequency": posting_freq,
            "aggregated_score": min(score, 1.0),
        }
    
    def _heuristic_classify(self, signals: Dict[str, Any]) -> BotResult:
        """
        Classify bot likelihood based on deterministic signals only.
        """
        score = signals["aggregated_score"]
        indicators = []
        
        if signals["spam_keyword_hits"] > 0:
            indicators.append(f"Spam keywords: {', '.join(signals['spam_keywords_found'][:3])}")
        
        if signals["repetition_score"] > 0.5:
            indicators.append(f"High text repetition ({signals['repetition_score']:.0%})")
        
        if signals["url_count"] > 2:
            indicators.append(f"Multiple URLs ({signals['url_count']})")
        
        if signals["hashtag_count"] > 5:
            indicators.append(f"Excessive hashtags ({signals['hashtag_count']})")
        
        if signals.get("account_age_days") is not None and signals["account_age_days"] < 7:
            indicators.append(f"New account ({signals['account_age_days']} days)")
        
        if signals.get("posting_frequency") is not None and signals["posting_frequency"] > 50:
            indicators.append(f"High posting rate ({signals['posting_frequency']}/day)")
        
        # Determine label
        if score >= 0.7:
            label = "bot"
            is_bot = True
        elif score >= 0.4:
            label = "suspicious"
            is_bot = False  # Not definitive
        else:
            label = "human"
            is_bot = False
        
        # Confidence based on signal strength and indicator count
        if score >= 0.7 and len(indicators) >= 2:
            confidence = min(0.6 + (0.1 * len(indicators)), 0.95)
        elif score >= 0.4:
            confidence = 0.5 + (score - 0.4)
        else:
            confidence = 0.7 - score  # Higher confidence of being human when score is low
        
        return BotResult(
            is_bot=is_bot,
            label=label,
            confidence=round(confidence, 3),
            indicators=indicators,
            reasoning=f"Heuristic classification with aggregate score {score:.2f}",
            deterministic_signals=signals,
        )
    
    def _llm_classify(self, text: str, signals: Dict[str, Any]) -> BotResult:
        """
        Classify bot likelihood using LLM with deterministic signals as context.
        """
        user_prompt = self.USER_PROMPT_TEMPLATE.format(
            text=text[:1500],  # Truncate for token limits
            spam_keyword_hits=signals["spam_keyword_hits"],
            repetition_score=signals["repetition_score"],
            unique_ratio=signals["unique_ratio"],
            url_count=signals["url_count"],
            hashtag_count=signals["hashtag_count"],
            account_age_days=signals.get("account_age_days", "N/A"),
            posting_frequency=signals.get("posting_frequency", "N/A"),
        )
        
        try:
            response = self._gemini_client.complete(
                system_prompt=self.SYSTEM_PROMPT,
                user_prompt=user_prompt,
            )
            
            return BotResult(
                is_bot=response.get("is_bot", False),
                label=response.get("label", "human"),
                confidence=float(response.get("confidence", 0.5)),
                indicators=response.get("indicators", []),
                reasoning=response.get("reasoning"),
                deterministic_signals=signals,
            )
            
        except Exception as e:
            logger.error(f"LLM bot detection failed: {e}. Falling back to heuristics.")
            return self._heuristic_classify(signals)
    
    def analyze(self, text: str, metadata: Optional[Dict[str, Any]] = None) -> Tuple[float, str]:
        """
        Analyze content for bot behavior.
        
        Returns:
            Tuple of (score, label) for backwards compatibility.
            Score is 0.0-1.0 where higher = more likely to be a bot.
            Use analyze_full() for complete results with indicators.
        """
        result = self.analyze_full(text, metadata)
        # Return aggregated_score for backwards compatibility
        score = result.deterministic_signals.get("aggregated_score", 0.0) if result.deterministic_signals else 0.0
        return score, result.label
    
    def analyze_full(self, text: str, metadata: Optional[Dict[str, Any]] = None) -> BotResult:
        """
        Analyze content for bot behavior with full results.
        
        Returns:
            BotResult with is_bot, label, confidence, indicators, and reasoning.
        """
        if not text:
            return BotResult(
                is_bot=False,
                label="unknown",
                confidence=0.0,
                indicators=[],
                reasoning="Empty text",
            )
        
        # 1. Compute deterministic signals
        signals = self._compute_signals(text, metadata)
        
        # 2. Only use LLM for edge cases (score > 0.3) to save costs
        if self.llm_enabled and self._gemini_client and self._gemini_client.is_available:
            if signals["aggregated_score"] > 0.3:
                return self._llm_classify(text, signals)
        
        # 3. Fallback to heuristic
        return self._heuristic_classify(signals)


# Backwards-compatible alias
BotDetector = HybridBotDetector
