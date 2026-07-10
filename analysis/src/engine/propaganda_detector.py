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

import re

from analysis.src.common.logger import get_logger
from analysis.src.engine.constants import INTENSIFIERS, NEGATIVE_WORDS
from analysis.src.engine.models import PropagandaResult, PropagandaTechnique
from analysis.src.engine.text_prep import truncate_at_sentence
from analysis.src.llm.prompts import (
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

# Character budget for the text the LLM actually sees. Propaganda techniques
# (loaded language, name-calling, appeal-to-fear, etc.) surface in the
# opening rhetoric of a piece — the hook paragraphs, not paragraph 12. Was
# 2500 chars; dropped to 800 to cut per-call token cost by ~70%. Roughly
# "headline + first 2-3 paragraphs" of news copy. Social posts are always
# under this cap so they're unaffected.
PROPAGANDA_TEXT_MAX_CHARS = 800

# Tokenizer used by the pre-filter. Matches the tokenization used by the
# sentiment engine so the two agree on what counts as a "word".
_WORD_RE = re.compile(r"[a-zA-Z']+")

# Loaded-language keyword set used by the pre-filter. Union of the
# engine's negative-word lexicon (what fuels name-calling, appeal-to-fear)
# and its intensifier list (what amplifies that language). If a doc's
# opening ~600 chars contain zero of these, propaganda techniques are
# overwhelmingly unlikely — we write a deterministic empty result instead
# of burning an LLM call. The lexicon is deliberately broad; the pre-filter
# should aim for high recall, not precision.
_LOADED_LEXICON = NEGATIVE_WORDS | INTENSIFIERS
_PRE_FILTER_SCAN_CHARS = 600


def _has_loaded_language(text: str) -> bool:
    """True if the first ``_PRE_FILTER_SCAN_CHARS`` contain any loaded
    token from the combined negative + intensifier lexicon."""
    if not text:
        return False
    window = text[:_PRE_FILTER_SCAN_CHARS].lower()
    for match in _WORD_RE.finditer(window):
        if match.group() in _LOADED_LEXICON:
            return True
    return False


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

    def detect(self, text: str, title: Optional[str] = None) -> PropagandaResult:
        """Analyze one doc. Returns an empty result when LLM is disabled/fails.

        When ``title`` is provided, it's prepended to the body so the LLM
        sees "HEADLINE\\n\\nBODY…" — propaganda techniques often live in the
        headline wording. The combined string is clamped to
        ``PROPAGANDA_TEXT_MAX_CHARS`` to keep per-call token cost bounded.
        """
        if not text:
            # Nothing to score — a clean empty deterministic result.
            return PropagandaResult(inference_method="deterministic")
        if not self.llm_enabled or self._llm_client is None or not self._llm_client.is_available:
            # Client unavailable — a failure, not a clean verdict. Flag it so
            # job_runner marks the doc 'failed' in doc_task_state and it
            # re-queues.
            return PropagandaResult(detection_failed=True)

        combined = f"{title}\n\n{text}" if title else text
        # Sentence-boundary clamp: a mid-sentence tail invites the model to
        # hallucinate a completion it can then "quote" — which the substring
        # validator below rejects, wasting the call.
        clamped = truncate_at_sentence(combined, PROPAGANDA_TEXT_MAX_CHARS)

        # Loaded-language pre-filter. Docs whose opening contains zero
        # negative-or-intensifier tokens almost never surface propaganda
        # techniques (the six starter techniques — loaded language,
        # name-calling, ad hominem, appeal to fear, whataboutism,
        # doubt-casting — all require loaded vocabulary to land). Short-
        # circuit with a deterministic empty result so the doc is marked
        # done and isn't re-queued on the next pipeline run. Cuts
        # LLM fan-out materially on straight-wire news coverage.
        if not _has_loaded_language(clamped):
            return PropagandaResult(inference_method="deterministic")

        try:
            response = self._llm_client.complete(
                system_prompt=PROPAGANDA_SYSTEM_PROMPT,
                user_prompt=PROPAGANDA_USER_PROMPT_TEMPLATE.format(text=clamped),
                response_schema=PROPAGANDA_SCHEMA,
            )
        except Exception as e:
            logger.warning(f"Propaganda LLM call failed: {e}")
            return PropagandaResult(detection_failed=True)

        raw_techniques = response.get("techniques") or []
        # Enforce the schema's cap of 5 defensively.
        raw_techniques = raw_techniques[:5]
        validated: List[PropagandaTechnique] = []
        for raw in raw_techniques:
            # Validate evidence-span substring match against the *clamped*
            # text — the LLM only saw those characters, so any verbatim span
            # it quotes must appear inside that window.
            t = _validate_technique(raw, clamped)
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
