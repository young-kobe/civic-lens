"""
Hybrid Bot Detector for Civic Lens (walkthrough 040 rework).

Two-layer approach, reoriented to flag LLM-driven / astroturf accounts in
social-media political discourse — news is excluded upstream; elected and
affiliated accounts are hard-excluded in job_runner before this code runs.

1. Deterministic layer (stylometric + account + behavioral signals):
   - Stylometric: sentence-length variance, hedge-phrase rate, typographic
     purity (em-dashes / smart quotes).
   - Behavioral: repetition, URL density, hashtag stuffing.
   - Account metadata (X): follow-ratio anomaly, sustained tweet rate,
     unlisted-but-active, new-account flag, verified_type de-bias.
2. LLM layer: prompt reframed to ask "does this read as LLM-generated, and
   does the account look automated" given the computed signals.

Bot-flagged content is excluded from sentiment/favorability aggregations.
"""

from __future__ import annotations

import statistics
import time
import re
from typing import Any, Dict, List, Optional, Tuple

from analysis.src.common.logger import get_logger
from analysis.src.engine.constants import (
    SPAM_KEYWORDS,
    LLM_HEDGE_PHRASES,
    LLM_TYPOGRAPHIC_TELLS,
    COORDINATION_PATTERNS,  # noqa: F401 — exported for aggregator
)
from analysis.src.engine.models import BotResult
from analysis.src.llm.prompts import BOT_SYSTEM_PROMPT, BOT_USER_PROMPT_TEMPLATE
from analysis.src.llm.schemas import BOT_SCHEMA
from analysis.src.llm.base import normalize_confidence

logger = get_logger(__name__)


_SENTENCE_SPLIT_RE = re.compile(r"[.!?]+")


class HybridBotDetector:
    """Stylometric + behavioral + account-metadata bot detector."""

    def __init__(self, llm_enabled: bool = False):
        self.llm_enabled = llm_enabled
        self._llm_client = None

        logger.info(f"Initialized HybridBotDetector (llm_enabled={llm_enabled})")

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

    # ---------- Signal computation ----------

    def _compute_signals(
        self, text: str, metadata: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """
        Compute deterministic signals: text-level, stylometric, and account.
        Returns one flat dict for auditability — every value fed into the
        scorer or the LLM prompt is here.
        """
        if not text:
            return _empty_signals()

        metadata = metadata or {}
        text_lower = text.lower()
        words = text_lower.split()
        word_count = len(words)

        # ----- Text / spam signals (legacy) -----
        spam_found = [kw for kw in SPAM_KEYWORDS if kw in text_lower]
        spam_hits = len(spam_found)

        unique_ratio = len(set(words)) / word_count if word_count > 0 else 1.0
        repetition_score = max(0, 1 - unique_ratio) if word_count > 5 else 0.0

        url_count = text_lower.count("http://") + text_lower.count("https://")
        hashtag_count = text.count("#")

        # ----- Stylometric signals (new) -----
        sentence_length_variance = _sentence_length_variance(text)
        hedge_hits, hedge_rate = _hedge_stats(text_lower, word_count)
        typographic_purity = _typographic_purity(text)

        # ----- Account metadata -----
        account_age = metadata.get("account_age_days")
        posting_freq = metadata.get("posts_per_day")
        verified = bool(metadata.get("verified"))
        verified_type = metadata.get("verified_type") or ""
        followers = int(metadata.get("user_followers") or 0)
        following = int(metadata.get("user_following") or 0)
        listed_count = int(metadata.get("user_listed") or 0)
        tweet_count = int(metadata.get("user_tweet_count") or 0)

        # ----- X-specific flags -----
        x_new_account_flag = False
        x_low_followers_flag = False
        x_foreign_origin_flag = False
        x_origin_confidence: Optional[str] = None
        follow_ratio_anomaly = False
        sustained_tweet_rate_flag = False
        unlisted_active_flag = False

        if metadata.get("platform") == "x":
            user_created = metadata.get("user_created_at")
            if user_created:
                age_days = (time.time() - user_created) / 86400
                account_age = age_days
                x_new_account_flag = age_days < 90

            x_low_followers_flag = followers < 50

            country_code = metadata.get("place_country_code")
            if country_code:
                x_foreign_origin_flag = country_code.upper() != "US"
                x_origin_confidence = "high"

            # Follow-ratio anomaly: follows >> is followed (classic bot pattern).
            if following > 1000 and followers < 0.1 * following:
                follow_ratio_anomaly = True

            # Sustained tweet rate over the account's lifetime.
            if tweet_count > 0 and account_age and account_age > 0:
                rate = tweet_count / account_age
                if rate > 50:
                    sustained_tweet_rate_flag = True

            # Active but no one ever listed them — humans tend to get listed.
            if tweet_count > 500 and listed_count == 0:
                unlisted_active_flag = True

        return {
            # Legacy text signals
            "spam_keyword_hits": spam_hits,
            "spam_keywords_found": spam_found,
            "repetition_score": round(repetition_score, 3),
            "unique_ratio": round(unique_ratio, 3),
            "url_count": url_count,
            "hashtag_count": hashtag_count,
            "word_count": word_count,
            # Stylometric signals (new)
            "sentence_length_variance": round(sentence_length_variance, 3),
            "hedge_phrase_hits": hedge_hits,
            "hedge_phrase_rate": round(hedge_rate, 3),
            "typographic_purity_score": round(typographic_purity, 3),
            # Account metadata
            "account_age_days": account_age,
            "posting_frequency": posting_freq,
            "verified": verified,
            "verified_type": verified_type,
            "followers": followers,
            "following": following,
            "listed_count": listed_count,
            "tweet_count": tweet_count,
            # X-specific
            "x_new_account_flag": x_new_account_flag,
            "x_low_followers_flag": x_low_followers_flag,
            "x_foreign_origin_flag": x_foreign_origin_flag,
            "x_origin_confidence": x_origin_confidence,
            "follow_ratio_anomaly": follow_ratio_anomaly,
            "sustained_tweet_rate_flag": sustained_tweet_rate_flag,
            "unlisted_active_flag": unlisted_active_flag,
            "aggregated_score": _aggregate_score({
                "spam_hits": spam_hits,
                "repetition_score": repetition_score,
                "url_count": url_count,
                "word_count": word_count,
                "hashtag_count": hashtag_count,
                "account_age": account_age,
                "posting_freq": posting_freq,
                "x_new_account_flag": x_new_account_flag,
                "x_low_followers_flag": x_low_followers_flag,
                "x_foreign_origin_flag": x_foreign_origin_flag,
                "x_origin_confidence": x_origin_confidence,
                "sentence_length_variance": sentence_length_variance,
                "hedge_phrase_rate": hedge_rate,
                "typographic_purity_score": typographic_purity,
                "follow_ratio_anomaly": follow_ratio_anomaly,
                "sustained_tweet_rate_flag": sustained_tweet_rate_flag,
                "unlisted_active_flag": unlisted_active_flag,
                "verified_type": verified_type,
            }),
        }

    # ---------- Heuristic classification ----------

    def _heuristic_classify(self, signals: Dict[str, Any]) -> BotResult:
        score = signals["aggregated_score"]
        indicators: List[str] = []

        # Text-signal indicators
        if signals["spam_keyword_hits"] > 0:
            indicators.append(
                f"Spam keywords: {', '.join(signals['spam_keywords_found'][:3])}"
            )
        if signals["repetition_score"] > 0.5:
            indicators.append(
                f"High text repetition ({signals['repetition_score']:.0%})"
            )
        if signals["url_count"] > 2:
            indicators.append(f"Multiple URLs ({signals['url_count']})")
        if signals["hashtag_count"] > 5:
            indicators.append(f"Excessive hashtags ({signals['hashtag_count']})")

        # Stylometric indicators
        if (
            signals["word_count"] > 30
            and signals["sentence_length_variance"] < 4
        ):
            indicators.append(
                f"Uniform sentence length (variance={signals['sentence_length_variance']})"
            )
        if signals["hedge_phrase_rate"] > 1.0:
            indicators.append(
                f"High hedge-phrase rate ({signals['hedge_phrase_hits']} hits, "
                f"{signals['hedge_phrase_rate']:.1f}/100w)"
            )
        if signals["typographic_purity_score"] >= 0.5:
            indicators.append(
                f"Typographic purity ({signals['typographic_purity_score']:.0%}) "
                "— smart quotes / em-dashes unusual for casual social writing"
            )

        # Account / behavioral indicators
        age = signals.get("account_age_days")
        if age is not None and age < 7:
            indicators.append(f"New account ({age:.0f} days)")
        if signals.get("posting_frequency") and signals["posting_frequency"] > 50:
            indicators.append(
                f"High posting rate ({signals['posting_frequency']}/day)"
            )
        if signals.get("x_new_account_flag"):
            indicators.append("X account < 90 days old")
        if signals.get("x_low_followers_flag"):
            indicators.append("X account has < 50 followers")
        if signals.get("x_foreign_origin_flag"):
            indicators.append("Non-US geo-tagged origin")
        if signals.get("follow_ratio_anomaly"):
            indicators.append(
                f"Follow-ratio anomaly (following={signals['following']} "
                f"followers={signals['followers']})"
            )
        if signals.get("sustained_tweet_rate_flag"):
            indicators.append(
                f"Sustained high tweet rate over account lifetime "
                f"({signals['tweet_count']} tweets / "
                f"{age or 0:.0f} days)"
            )
        if signals.get("unlisted_active_flag"):
            indicators.append(
                "Active account with zero list memberships "
                f"(tweet_count={signals['tweet_count']})"
            )

        # De-bias: government-verified accounts cannot be bots in our model.
        vtype = signals.get("verified_type", "")
        if vtype == "government":
            indicators.append("verified_type=government (de-biased)")
            return BotResult(
                is_bot=False,
                label="human",
                confidence=0.95,
                indicators=indicators,
                reasoning="X government-verified account; bot score suppressed.",
                deterministic_signals=signals,
                inference_method="heuristic",
                llm_text_likelihood=0.0,
            )

        # Label from score
        if score >= 0.7:
            label, is_bot = "bot", True
        elif score >= 0.4:
            label, is_bot = "suspicious", False
        else:
            label, is_bot = "human", False

        # Confidence based on signal strength and indicator count
        if score >= 0.7 and len(indicators) >= 2:
            confidence = min(0.6 + (0.1 * len(indicators)), 0.95)
        elif score >= 0.4:
            confidence = 0.5 + (score - 0.4)
        else:
            confidence = 0.7 - score

        return BotResult(
            is_bot=is_bot,
            label=label,
            confidence=round(confidence, 3),
            # Pass through the sanitizer so any `=None`-style string slipped
            # in by an upstream helper is dropped. The heuristic branch
            # doesn't generate those today, but running the same filter on
            # both code paths keeps the output contract uniform — one
            # canonical shape regardless of which classifier ran.
            indicators=_sanitize_llm_indicators(indicators),
            reasoning=f"Heuristic classification, aggregated_score={score:.2f}",
            deterministic_signals=signals,
            inference_method="heuristic",
            llm_text_likelihood=_heuristic_llm_text_likelihood(signals),
        )

    # ---------- LLM classification ----------

    def _llm_classify(self, text: str, signals: Dict[str, Any]) -> BotResult:
        # Previously `signals.get(key, "N/A")` returned None when the key
        # EXISTED with value None — so the prompt rendered lines like
        # "Account age: None days" and the LLM echoed those literals back
        # in its indicators list ("account_age=None days"). Now we
        # substitute any None / empty value with "unknown" so the model
        # never sees ambiguous placeholders in the first place.
        user_prompt = BOT_USER_PROMPT_TEMPLATE.format(
            text=text[:1500],
            # Text / behavioral context
            spam_keyword_hits=signals["spam_keyword_hits"],
            repetition_score=signals["repetition_score"],
            unique_ratio=signals["unique_ratio"],
            url_count=signals["url_count"],
            hashtag_count=signals["hashtag_count"],
            # Stylometric context
            sentence_length_variance=signals["sentence_length_variance"],
            hedge_phrase_hits=signals["hedge_phrase_hits"],
            hedge_phrase_rate=signals["hedge_phrase_rate"],
            typographic_purity_score=signals["typographic_purity_score"],
            # Account metadata — route through _safe_prompt_value so None
            # renders as "unknown" rather than the literal string "None".
            account_age_days=_safe_prompt_value(signals.get("account_age_days")),
            posting_frequency=_safe_prompt_value(signals.get("posting_frequency")),
            followers=_safe_prompt_value(signals.get("followers")),
            following=_safe_prompt_value(signals.get("following")),
            listed_count=_safe_prompt_value(signals.get("listed_count")),
            verified_type=_safe_prompt_value(signals.get("verified_type")),
        )

        try:
            response = self._llm_client.complete(
                system_prompt=BOT_SYSTEM_PROMPT,
                user_prompt=user_prompt,
                response_schema=BOT_SCHEMA,
            )
            llm_text_lik = normalize_confidence(
                response.get("llm_text_likelihood", 0.0), "llm_text_likelihood"
            )
            # even with the prompt cleanup above, older
            # stored outputs + the occasional LLM-generated placeholder string
            # can leak noise entries. Filter them at the boundary so the UI
            # never has to work around it. Canonical fix for todos/
            # bot-propaganda-entity-signals.md §"Sanitize whyFlagged at source".
            raw_indicators = response.get("indicators", [])
            return BotResult(
                is_bot=response.get("is_bot", False),
                label=response.get("label", "human"),
                confidence=normalize_confidence(
                    response.get("confidence", 0.5), "bot_confidence"
                ),
                indicators=_sanitize_llm_indicators(raw_indicators),
                reasoning=response.get("reasoning"),
                deterministic_signals=signals,
                inference_method="llm",
                llm_text_likelihood=llm_text_lik,
            )
        except (ValueError, RuntimeError, KeyError, TypeError) as e:
            # See analyzer.py for exception-class rationale.
            logger.error(f"LLM bot detection failed: {e}. Falling back to heuristics.")
            fallback = self._heuristic_classify(signals)
            # Preserve the reason so downstream ai_outputs has visibility into
            # why this row went through the heuristic path (audit §9).
            fallback.fallback_reason = f"{type(e).__name__}: {e}"
            return fallback

    # ---------- Public entry points ----------

    def analyze(self, text: str, metadata: Optional[Dict[str, Any]] = None) -> Tuple[float, str]:
        """Backwards-compatible thin wrapper returning (score, label)."""
        result = self.analyze_full(text, metadata)
        score = (
            result.deterministic_signals.get("aggregated_score", 0.0)
            if result.deterministic_signals else 0.0
        )
        return score, result.label

    def analyze_full(
        self, text: str, metadata: Optional[Dict[str, Any]] = None,
    ) -> BotResult:
        """Full result for a single post."""
        if not text:
            return BotResult(
                is_bot=False,
                label="unknown",
                confidence=0.0,
                indicators=[],
                reasoning="Empty text",
                inference_method="heuristic",
                llm_text_likelihood=0.0,
            )

        signals = self._compute_signals(text, metadata)

        if self.llm_enabled and self._llm_client and self._llm_client.is_available:
            return self._llm_classify(text, signals)

        logger.warning("LLM unavailable, using heuristic fallback for bot detection")
        return self._heuristic_classify(signals)


# =============================================================================
# Module-level helpers (pure functions, easy to unit-test)
# =============================================================================


def _safe_prompt_value(value: Any) -> str:
    """Return a human-readable string for an LLM prompt field that never
    surfaces the literal Python ``None`` / empty-string as a visible value.

    Why: BOT_USER_PROMPT_TEMPLATE lines like "Account age: {account_age_days} days"
    render "Account age: None days" when the signal is None. The LLM sometimes
    echoes those literals back in its `indicators` list, producing entries like
    "account_age=None days" that reach the UI as noise. Feeding "unknown"
    removes the ambiguous placeholder from the model's input entirely.
    """
    if value is None:
        return "unknown"
    text = str(value).strip()
    if not text or text.lower() in {"none", "null", "undefined", "n/a"}:
        return "unknown"
    return text


# Noise patterns for post-hoc indicator sanitization. Mirrors (and supersedes)
# the `sanitizeWhyFlagged` filter the UI used to apply in BotActivityProfiler.
# Matched entries are dropped from the indicators list before persistence,
# so downstream consumers (UI, review queue, narrative-amplification rollups)
# only ever see signal-bearing strings.
_NOISE_INDICATOR_PATTERNS: Tuple[re.Pattern, ...] = (
    re.compile(r"=\s*None\b", re.IGNORECASE),
    re.compile(r"=\s*null\b", re.IGNORECASE),
    re.compile(r"=\s*undefined\b", re.IGNORECASE),
    re.compile(r"=\s*0(\s|$)"),
    re.compile(r"=\s*$"),
)


def _sanitize_llm_indicators(raw: Any) -> List[str]:
    """Drop noise entries from the LLM's indicator list.

    The LLM's structured output says ``indicators`` is a list of strings, but
    we defensively coerce non-string entries to str so a malformed response
    doesn't raise. Empty strings and entries matching a noise pattern are
    skipped; everything else passes through unchanged.
    """
    if not isinstance(raw, list):
        return []
    out: List[str] = []
    for item in raw:
        text = str(item).strip() if item is not None else ""
        if not text:
            continue
        if any(p.search(text) for p in _NOISE_INDICATOR_PATTERNS):
            continue
        out.append(text)
    return out


def _empty_signals() -> Dict[str, Any]:
    return {
        "spam_keyword_hits": 0,
        "spam_keywords_found": [],
        "repetition_score": 0.0,
        "unique_ratio": 1.0,
        "url_count": 0,
        "hashtag_count": 0,
        "word_count": 0,
        "sentence_length_variance": 0.0,
        "hedge_phrase_hits": 0,
        "hedge_phrase_rate": 0.0,
        "typographic_purity_score": 0.0,
        "account_age_days": None,
        "posting_frequency": None,
        "verified": False,
        "verified_type": "",
        "followers": 0,
        "following": 0,
        "listed_count": 0,
        "tweet_count": 0,
        "x_new_account_flag": False,
        "x_low_followers_flag": False,
        "x_foreign_origin_flag": False,
        "x_origin_confidence": None,
        "follow_ratio_anomaly": False,
        "sustained_tweet_rate_flag": False,
        "unlisted_active_flag": False,
        "aggregated_score": 0.0,
    }


def _sentence_length_variance(text: str) -> float:
    """Population variance of per-sentence word counts. 0 when <2 sentences."""
    parts = [s.strip() for s in _SENTENCE_SPLIT_RE.split(text) if s.strip()]
    lengths = [len(s.split()) for s in parts]
    if len(lengths) < 2:
        return 0.0
    return float(statistics.pvariance(lengths))


def _hedge_stats(text_lower: str, word_count: int) -> Tuple[int, float]:
    """Return (hits, rate-per-100-words) for LLM hedge phrases."""
    hits = sum(1 for phrase in LLM_HEDGE_PHRASES if phrase in text_lower)
    rate = (hits * 100 / word_count) if word_count > 0 else 0.0
    return hits, rate


def _typographic_purity(text: str) -> float:
    """Fraction of the ``LLM_TYPOGRAPHIC_TELLS`` tuple present in the text.

    High values mean the text uses multiple LLM-styled glyphs (em-dash,
    smart quotes, ellipsis character) — unusual for casual social writing.
    """
    if not text:
        return 0.0
    hits = sum(1 for ch in LLM_TYPOGRAPHIC_TELLS if ch in text)
    return hits / len(LLM_TYPOGRAPHIC_TELLS)


def _heuristic_llm_text_likelihood(signals: Dict[str, Any]) -> float:
    """Proxy for 'how LLM-generated does this text look' from signals alone.

    Keep conservative — 0.0 baseline, bump for each stylometric tell that
    fires. Full calibration comes after walkthrough 043 / golden set.
    """
    score = 0.0
    if signals.get("word_count", 0) > 30 and signals.get("sentence_length_variance", 99) < 4:
        score += 0.3
    if signals.get("hedge_phrase_rate", 0) > 1.0:
        score += 0.3
    if signals.get("typographic_purity_score", 0) >= 0.5:
        score += 0.3
    return round(min(1.0, score), 3)


def _aggregate_score(ctx: Dict[str, Any]) -> float:
    """Compute the deterministic aggregated bot score from named inputs.

    Breakdown kept transparent (one contribution per signal) so every fired
    add-on shows up in indicators later. Thresholds are placeholders until
    calibrated against a golden set (walkthrough 043/044).
    """
    score = 0.0

    # Legacy text signals (kept — still triggers on classic spam runs).
    spam_hits = ctx.get("spam_hits", 0)
    if spam_hits > 0:
        score += 0.3 + (0.1 * min(spam_hits, 3))
    if ctx.get("repetition_score", 0) > 0.5:
        score += 0.2
    if ctx.get("word_count", 0) > 0:
        if ctx.get("url_count", 0) / max(ctx["word_count"], 1) > 0.1:
            score += 0.1
    if ctx.get("hashtag_count", 0) > 5:
        score += 0.08

    # Stylometric signals (new)
    if ctx.get("sentence_length_variance", 99) < 4 and ctx.get("word_count", 0) > 30:
        score += 0.15
    if ctx.get("hedge_phrase_rate", 0) > 1.0:
        score += 0.18
    if ctx.get("typographic_purity_score", 0) >= 0.5:
        score += 0.12

    # Account metadata
    age = ctx.get("account_age")
    if age is not None and age < 7:
        score += 0.12
    if ctx.get("posting_freq") and ctx["posting_freq"] > 50:
        score += 0.15

    # X-specific
    if ctx.get("x_new_account_flag"):
        score += 0.08
    if ctx.get("x_low_followers_flag"):
        score += 0.05
    if ctx.get("x_foreign_origin_flag") and ctx.get("x_origin_confidence") in ("high", "medium"):
        score += 0.1
    if ctx.get("follow_ratio_anomaly"):
        score += 0.2
    if ctx.get("sustained_tweet_rate_flag"):
        score += 0.15
    if ctx.get("unlisted_active_flag"):
        score += 0.08

    # De-bias by verification type (the big one: government is never a bot).
    vtype = ctx.get("verified_type", "")
    if vtype == "government":
        return 0.0
    if vtype == "business":
        score = min(score, 0.3)
    elif vtype == "blue":
        pass  # paid verification; no effect

    return round(min(score, 1.0), 3)
