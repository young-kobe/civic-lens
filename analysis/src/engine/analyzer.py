"""
Unified Text Analyzer for Civic Lens.

Fuses Sentiment and Favorability analysis into a single LLM inference.
"""

from typing import Any, Dict, List, Tuple
from analysis.src.common.logger import get_logger
from analysis.src.engine.constants import (
    POSITIVE_WORDS, NEGATIVE_WORDS, INTENSIFIERS, NEGATORS,
    GOP_ENTITIES, FAVORABLE_INDICATORS, UNFAVORABLE_INDICATORS, PROXIMITY_WINDOW
)
from analysis.src.engine.models.engine_models import SentimentResult, FavorabilityResult, EntityStance
from analysis.src.llm.context_seeds import (
    format_seeds_block, match_seeds, matched_slugs,
)
from analysis.src.llm.prompts import TEXT_ANALYSIS_SYSTEM_PROMPT, TEXT_ANALYSIS_USER_PROMPT_TEMPLATE
from analysis.src.llm.schemas import TEXT_ANALYSIS_SCHEMA

logger = get_logger(__name__)

# Minimum word count for a valid evidence span (matches prompt rule).
MIN_EVIDENCE_SPAN_WORDS = 4
# Confidence ceiling when the model returned evidence spans but none were verifiable.
UNVERIFIED_EVIDENCE_CONFIDENCE_CAP = 0.3


def _validate_evidence_spans(spans: List[str], source_text: str) -> Tuple[List[str], bool]:
    """Filter spans to those that are verbatim substrings of source_text with at least
    MIN_EVIDENCE_SPAN_WORDS words. Returns (valid_spans, had_invalid_spans).

    Fabricated or paraphrased evidence violates invariant B2 (traceability), so we
    drop it rather than store it. The caller uses had_invalid_spans to decide
    whether to cap confidence.
    """
    if not spans:
        return [], False
    source_lower = source_text.lower()
    valid: List[str] = []
    for span in spans:
        if not isinstance(span, str):
            continue
        cleaned = span.strip()
        if len(cleaned.split()) < MIN_EVIDENCE_SPAN_WORDS:
            continue
        if cleaned.lower() not in source_lower:
            continue
        valid.append(cleaned)
    return valid, len(valid) < len([s for s in spans if isinstance(s, str) and s.strip()])


class Analyzer:
    """
    Hybrid text analyzer evaluating BOTH sentiment and favorability 
    in a single LLM inference pass.
    """
    
    def __init__(self, llm_enabled: bool = False):
        self.llm_enabled = llm_enabled
        self._llm_client = None
        
        logger.info(f"Initialized Analyzer (llm_enabled={llm_enabled})")
        
        if llm_enabled:
            self._init_llm_client()
            
    def _init_llm_client(self):
        try:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                raise RuntimeError("LLM client not available")
        except (ImportError, RuntimeError) as e:
            raise RuntimeError(f"Failed to init LLM client: {e}") from e

    @staticmethod
    def _word_offsets(text_lower: str) -> List[int]:
        """Return (char_offset → word_index) as a list indexed by char.

        Splitting the whole text once lets downstream helpers map a char
        position to a word index in O(1) via `offsets[pos]`, instead of
        re-splitting `text_lower[:pos]` per call (the old code did this
        inside a loop over ~100 keywords on every inference).
        """
        offsets = [0] * (len(text_lower) + 1)
        word_idx = 0
        in_word = False
        for i, ch in enumerate(text_lower):
            if ch.isspace():
                if in_word:
                    word_idx += 1
                    in_word = False
            else:
                in_word = True
            offsets[i] = word_idx
        offsets[-1] = word_idx + (1 if in_word else 0)
        return offsets

    def _extract_gop_entities(self, text_lower: str) -> List[str]:
        return list(set([e for e in GOP_ENTITIES if e in text_lower]))

    def _find_entity_positions(
        self, text_lower: str, entities: List[str], offsets: List[int],
    ) -> List[int]:
        positions = []
        for entity in entities:
            start = 0
            while True:
                pos = text_lower.find(entity, start)
                if pos == -1:
                    break
                positions.append(offsets[pos])
                start = pos + len(entity)
        return positions

    def _is_keyword_near_entity(
        self,
        keyword: str,
        text_lower: str,
        entity_positions: List[int],
        word_count: int,
        offsets: List[int],
    ) -> bool:
        if not entity_positions:
            return False
        if word_count <= PROXIMITY_WINDOW:
            return True
        start = 0
        while True:
            pos = text_lower.find(keyword, start)
            if pos == -1:
                break
            kw_word_pos = offsets[pos]
            if any(abs(kw_word_pos - ep) <= PROXIMITY_WINDOW for ep in entity_positions):
                return True
            start = pos + len(keyword)
        return False

    def _compute_signals(self, text: str) -> Dict[str, Any]:
        if not text:
            return {}
            
        text_lower = text.lower()
        words = text_lower.split()
        word_count = len(words)

        # Sentiment deterministic
        pos_found = [w for w in words if w in POSITIVE_WORDS]
        neg_found = [w for w in words if w in NEGATIVE_WORDS]
        has_intensifiers = any(w in INTENSIFIERS for w in words)
        has_negators = any(w in NEGATORS for w in words)

        # Favorability deterministic — compute the char→word-index map once so
        # the proximity helper below doesn't re-split the text per keyword.
        gop_entities = self._extract_gop_entities(text_lower)
        offsets = self._word_offsets(text_lower) if gop_entities else []
        entity_positions = self._find_entity_positions(text_lower, gop_entities, offsets)

        fav_found = [
            ind for ind in FAVORABLE_INDICATORS
            if ind in text_lower
            and self._is_keyword_near_entity(ind, text_lower, entity_positions, word_count, offsets)
        ]
        unfav_found = [
            ind for ind in UNFAVORABLE_INDICATORS
            if ind in text_lower
            and self._is_keyword_near_entity(ind, text_lower, entity_positions, word_count, offsets)
        ]
        
        return {
            "gop_entities": gop_entities,
            "word_count": word_count,
            "positive_count": len(pos_found),
            "negative_count": len(neg_found),
            "positive_words": list(set(pos_found)),
            "negative_words": list(set(neg_found)),
            "has_intensifiers": has_intensifiers,
            "has_negators": has_negators,
            "favorable_count": len(fav_found),
            "unfavorable_count": len(unfav_found),
            "favorable_keywords": fav_found[:5],
            "unfavorable_keywords": unfav_found[:5],
            "net_favorability_score": len(fav_found) - len(unfav_found)
        }

    def _heuristic_classify(self, signals: Dict[str, Any]) -> Tuple[SentimentResult, FavorabilityResult]:
        """Fallback heuristics for both pipelines. Both results are marked
        ``inference_method='heuristic'`` so audit queries can tell this path
        apart from a validated LLM response (see walkthrough 038)."""
        # 1. Sentiment Fallback
        pos_count = signals.get("positive_count", 0)
        neg_count = signals.get("negative_count", 0)
        total_len = pos_count + neg_count
        conf_penalty = 0.15 if signals.get("has_negators") else 0.0

        if total_len == 0:
            sent_label, sent_conf, sent_ev = "NEUTRAL", 0.5, []
        elif pos_count > neg_count:
            sent_label = "POSITIVE"
            sent_conf = min(0.5 + (0.5 * (pos_count/total_len)) - conf_penalty, 1.0)
            sent_ev = signals.get("positive_words", [])[:3]
        elif neg_count > pos_count:
            sent_label = "NEGATIVE"
            sent_conf = min(0.5 + (0.5 * (neg_count/total_len)) - conf_penalty, 1.0)
            sent_ev = signals.get("negative_words", [])[:3]
        else:
            sent_label = "MIXED"
            sent_conf = 0.5
            sent_ev = signals.get("positive_words", [])[:2] + signals.get("negative_words", [])[:2]

        sentiment_res = SentimentResult(
            label=sent_label,
            confidence=round(sent_conf, 3),
            evidence_spans=sent_ev,
            reasoning=f"Heuristic sentiment based on {pos_count} positive and {neg_count} negative.",
            deterministic_signals=signals,
            inference_method="heuristic",
        )
        
        # 2. Favorability Fallback
        gop_entities = signals.get("gop_entities", [])
        net = signals.get("net_favorability_score", 0)
        fav_c = signals.get("favorable_count", 0)
        unfav_c = signals.get("unfavorable_count", 0)
        
        if not gop_entities:
            fav_stance, fav_conf = "neutral", 0.5
        elif net >= 1:
            fav_stance, fav_conf = "favorable", min(0.5 + (0.1 * net), 0.9)
        elif net <= -1:
            fav_stance, fav_conf = "unfavorable", min(0.5 + (0.1 * abs(net)), 0.9)
        elif fav_c > 0 and unfav_c > 0:
            fav_stance, fav_conf = "mixed", 0.6
        else:
            fav_stance, fav_conf = "neutral", 0.5
            
        entity_stances = [
            EntityStance(
                entity=e, 
                stance=fav_stance, 
                confidence=round(fav_conf, 3), 
                evidence_spans=signals.get("favorable_keywords", [])[:2] + signals.get("unfavorable_keywords", [])[:2]
            ) for e in gop_entities[:5]
        ]
        
        favorability_res = FavorabilityResult(
            entity_stances=entity_stances,
            overall_gop_stance=fav_stance,
            overall_confidence=round(fav_conf, 3),
            gop_entities_found=gop_entities,
            reasoning=f"Heuristic favorability: {fav_c} fav, {unfav_c} unfav",
            deterministic_signals=signals,
            inference_method="heuristic",
        )

        return sentiment_res, favorability_res

    def analyze_full(self, text: str) -> Tuple[SentimentResult, FavorabilityResult]:
        """
        Main entry point for analyzing a document.
        Returns both SentimentResult and FavorabilityResult.
        """
        if not text:
            return (
                SentimentResult(label="NEUTRAL", confidence=0.0, evidence_spans=[], reasoning="Empty"),
                FavorabilityResult(entity_stances=[], overall_gop_stance="neutral", overall_confidence=0.0, gop_entities_found=[], reasoning="Empty")
            )
            
        signals = self._compute_signals(text)

        if self.llm_enabled and self._llm_client and self._llm_client.is_available:
            gop_entities = signals.get("gop_entities", [])
            # The user-prompt template's only placeholder is {text}; the heuristic
            # signals live on `deterministic_signals` for auditability and are no
            # longer injected into the prompt (walkthrough 038 cleanup).
            #
            # Reference-context seeds (CDC / IPCC / BLS / etc.) are matched
            # against the doc text and prepended above the input. Unrelated
            # docs match nothing → empty block → no token cost.
            seeded = match_seeds(text)
            seeds_block = format_seeds_block(seeded)
            user_prompt = seeds_block + TEXT_ANALYSIS_USER_PROMPT_TEMPLATE.format(text=text[:2000])
            
            try:
                response = self._llm_client.complete(
                    system_prompt=TEXT_ANALYSIS_SYSTEM_PROMPT,
                    user_prompt=user_prompt,
                    response_schema=TEXT_ANALYSIS_SCHEMA
                )
                
                # Parse sentiment with evidence-span validation (invariant B2).
                sent_conf = float(response.get("sentiment_confidence", 0.5))
                sent_spans, sent_had_invalid = _validate_evidence_spans(
                    response.get("sentiment_evidence_spans", []) or [], text
                )
                if sent_had_invalid and not sent_spans:
                    sent_conf = min(sent_conf, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

                # Audit trail: stamp which reference seeds (if any) the LLM
                # saw alongside this doc's deterministic signals so reviewers
                # can correlate outputs with the seed registry version.
                signals_with_seeds = dict(signals)
                if seeded:
                    signals_with_seeds["reference_seeds"] = matched_slugs(seeded)

                sentiment_res = SentimentResult(
                    label=response.get("sentiment_label", "NEUTRAL"),
                    confidence=sent_conf,
                    evidence_spans=sent_spans,
                    sarcasm_detected=bool(response.get("sarcasm_detected", False)),
                    reasoning=response.get("sentiment_reasoning"),
                    deterministic_signals=signals_with_seeds,
                    inference_method="llm",
                )

                # Parse favorability
                entity_stances = []
                for es in response.get("entity_stances", []):
                    if not isinstance(es, dict): continue
                    es_conf = float(es.get("confidence", 0.5))
                    es_spans, es_had_invalid = _validate_evidence_spans(
                        es.get("evidence_spans", []) or [], text
                    )
                    if es_had_invalid and not es_spans:
                        es_conf = min(es_conf, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)
                    entity_stances.append(EntityStance(
                        entity=es.get("entity", "unknown"),
                        stance=es.get("stance", "neutral"),
                        confidence=es_conf,
                        evidence_spans=es_spans,
                    ))

                fav_conf = float(response.get("overall_favorability_confidence", 0.5))
                if entity_stances and all(not s.evidence_spans for s in entity_stances):
                    # Whole favorability call had no verifiable evidence — cap overall too.
                    fav_conf = min(fav_conf, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

                favorability_res = FavorabilityResult(
                    entity_stances=entity_stances,
                    overall_gop_stance=response.get("overall_gop_stance", "neutral"),
                    overall_confidence=fav_conf,
                    gop_entities_found=gop_entities,
                    reasoning=response.get("favorability_reasoning"),
                    deterministic_signals=signals_with_seeds,
                    inference_method="llm",
                )

                return sentiment_res, favorability_res

            except (ValueError, RuntimeError, KeyError, TypeError) as e:
                # ValueError: JSON parse failures, schema validation rejections,
                # or bad float conversions in the response.
                # RuntimeError: LLM client errors / transport failures.
                # KeyError/TypeError: unexpected response shape.
                logger.error(f"Unified LLM classification failed: {e}. Falling back to heuristics.")
                return self._heuristic_classify(signals)
                
        # LLM fallback
        return self._heuristic_classify(signals)
