"""
GOP Favorability Analyzer for Civic Lens.

Computes favorability toward Republican/GOP political entities using:
1. Deterministic layer: Entity extraction, stance keywords, source bias proxies
2. LLM layer: Nuanced stance classification per entity (when enabled)
"""

import re
from typing import Any, Dict, List, Optional, Tuple
from analysis.src.common.logger import get_logger
from analysis.src.engine.constants import (
    GOP_ENTITIES, FAVORABLE_INDICATORS, UNFAVORABLE_INDICATORS,
    PROXIMITY_WINDOW,
)
from analysis.src.engine.models import EntityStance, FavorabilityResult
from analysis.src.engine.prompts import FAVORABILITY_SYSTEM_PROMPT, FAVORABILITY_USER_PROMPT_TEMPLATE
from analysis.src.llm.schemas import FAVORABILITY_SCHEMA

logger = get_logger(__name__)



class FavorabilityAnalyzer:
    """
    Analyzes favorability toward GOP/Republican entities in text.
    """
    
    def __init__(self, llm_enabled: bool = False):
        self.llm_enabled = llm_enabled
        self._llm_client = None
        
        logger.info(f"Initialized FavorabilityAnalyzer (llm_enabled={llm_enabled})")
        
        if llm_enabled:
            self._init_llm_client()
    
    def _init_llm_client(self):
        """Initialize the LLM client if enabled."""
        try:
            from analysis.src.llm import get_llm_client
            self._llm_client = get_llm_client()
            if not self._llm_client.is_available:
                logger.warning("LLM client not available. Falling back to heuristics.")
                self.llm_enabled = False
        except Exception as e:
            logger.error(f"Failed to initialize LLM client: {e}")
            self.llm_enabled = False
    
    def _extract_gop_entities(self, text: str) -> List[str]:
        """Extract GOP-related entities mentioned in text."""
        text_lower = text.lower()
        found = []
        
        for entity in GOP_ENTITIES:
            if entity in text_lower:
                found.append(entity)
        
        # Deduplicate and return
        return list(set(found))
    
    def _find_entity_positions(self, text_lower: str, entities: List[str]) -> List[int]:
        """Find approximate word positions of all entity mentions in text."""
        positions = []
        for entity in entities:
            start = 0
            while True:
                pos = text_lower.find(entity, start)
                if pos == -1:
                    break
                word_pos = len(text_lower[:pos].split())
                positions.append(word_pos)
                start = pos + len(entity)
        return positions

    def _is_keyword_near_entity(
        self, keyword: str, text_lower: str, entity_positions: List[int], word_count: int
    ) -> bool:
        """Check if keyword appears within proximity window of any entity."""
        if not entity_positions:
            return False
        if word_count <= PROXIMITY_WINDOW:
            return True
        start = 0
        while True:
            pos = text_lower.find(keyword, start)
            if pos == -1:
                break
            kw_word_pos = len(text_lower[:pos].split())
            if any(abs(kw_word_pos - ep) <= PROXIMITY_WINDOW for ep in entity_positions):
                return True
            start = pos + len(keyword)
        return False

    def _compute_signals(self, text: str, gop_entities: List[str]) -> Dict[str, Any]:
        """
        Compute deterministic stance signals with proximity matching.

        Uses proximity-based matching: only counts stance keywords
        that appear within PROXIMITY_WINDOW words of a detected GOP entity.
        """
        empty_result = {
            "favorable_count": 0, "unfavorable_count": 0,
            "favorable_keywords": [], "unfavorable_keywords": [],
            "net_score": 0.0,
        }
        if not text or not gop_entities:
            return empty_result

        text_lower = text.lower()
        word_count = len(text_lower.split())
        entity_positions = self._find_entity_positions(text_lower, gop_entities)

        favorable_found = [
            ind for ind in FAVORABLE_INDICATORS
            if ind in text_lower and self._is_keyword_near_entity(ind, text_lower, entity_positions, word_count)
        ]
        unfavorable_found = [
            ind for ind in UNFAVORABLE_INDICATORS
            if ind in text_lower and self._is_keyword_near_entity(ind, text_lower, entity_positions, word_count)
        ]

        return {
            "favorable_count": len(favorable_found),
            "unfavorable_count": len(unfavorable_found),
            "favorable_keywords": favorable_found[:5],
            "unfavorable_keywords": unfavorable_found[:5],
            "net_score": len(favorable_found) - len(unfavorable_found),
        }
    
    def _heuristic_classify(
        self, 
        text: str,
        gop_entities: List[str], 
        signals: Dict[str, Any]
    ) -> FavorabilityResult:
        """
        Classify favorability based on deterministic signals only.
        """
        if not gop_entities:
            return FavorabilityResult(
                entity_stances=[],
                overall_gop_stance="neutral",
                overall_confidence=0.5,
                gop_entities_found=[],
                reasoning="No GOP entities detected in text",
                deterministic_signals=signals,
            )
        
        net = signals["net_score"]
        fav_count = signals["favorable_count"]
        unfav_count = signals["unfavorable_count"]
        total = fav_count + unfav_count
        
        # Determine overall stance
        # net >= 1 means more favorable than unfavorable indicators
        if total == 0:
            stance = "neutral"
            confidence = 0.5
        elif net >= 1:
            stance = "favorable"
            confidence = min(0.5 + (0.1 * net), 0.9)
        elif net <= -1:
            stance = "unfavorable"
            confidence = min(0.5 + (0.1 * abs(net)), 0.9)
        elif fav_count > 0 and unfav_count > 0:
            stance = "mixed"
            confidence = 0.6
        else:
            stance = "neutral"
            confidence = 0.5
        
        # Create per-entity stances (simplified: same stance for all)
        entity_stances = [
            EntityStance(
                entity=entity,
                stance=stance,
                confidence=confidence,
                evidence_spans=signals["favorable_keywords"][:2] + signals["unfavorable_keywords"][:2],
            )
            for entity in gop_entities[:5]  # Limit to top 5
        ]
        
        return FavorabilityResult(
            entity_stances=entity_stances,
            overall_gop_stance=stance,
            overall_confidence=round(confidence, 3),
            gop_entities_found=gop_entities,
            reasoning=f"Heuristic: {fav_count} favorable, {unfav_count} unfavorable indicators",
            deterministic_signals=signals,
        )
    
    def _llm_classify(
        self, 
        text: str, 
        gop_entities: List[str],
        signals: Dict[str, Any]
    ) -> FavorabilityResult:
        """
        Classify favorability using LLM with deterministic signals as context.
        """
        user_prompt = FAVORABILITY_USER_PROMPT_TEMPLATE.format(
            text=text[:2000],
            gop_mentions=", ".join(gop_entities[:10]),
            favorable_count=signals["favorable_count"],
            unfavorable_count=signals["unfavorable_count"],
            favorable_keywords=", ".join(signals["favorable_keywords"]),
            unfavorable_keywords=", ".join(signals["unfavorable_keywords"]),
        )
        
        try:
            response = self._llm_client.complete(
                system_prompt=FAVORABILITY_SYSTEM_PROMPT,
                user_prompt=user_prompt,
                response_schema=FAVORABILITY_SCHEMA,
            )
            
            # Parse entity stances
            entity_stances = []
            for es in response.get("entity_stances", []):
                if not isinstance(es, dict):
                    continue
                entity_stances.append(EntityStance(
                    entity=es.get("entity", "unknown"),
                    stance=es.get("stance", "neutral"),
                    confidence=float(es.get("confidence", 0.5)),
                    evidence_spans=es.get("evidence_spans", []),
                ))
            
            return FavorabilityResult(
                entity_stances=entity_stances,
                overall_gop_stance=response.get("overall_gop_stance", "neutral"),
                overall_confidence=float(response.get("overall_confidence", 0.5)),
                gop_entities_found=gop_entities,
                reasoning=response.get("reasoning"),
                deterministic_signals=signals,
            )
            
        except Exception as e:
            logger.error(f"LLM favorability classification failed: {e}. Falling back to heuristics.")
            return self._heuristic_classify(text, gop_entities, signals)
    
    def analyze(self, text: str) -> Tuple[str, float]:
        """
        Analyze favorability of text toward GOP.
        
        Returns:
            Tuple of (stance, confidence) for simple use cases.
            Use analyze_full() for complete results with per-entity breakdown.
        """
        result = self.analyze_full(text)
        return result.overall_gop_stance, result.overall_confidence
    
    def analyze_full(self, text: str) -> FavorabilityResult:
        """
        Analyze favorability with full results including per-entity breakdown.
        
        LLM is the primary classifier. Heuristic signals are computed first
        and fed as supplemental context to the LLM prompt. Heuristics are
        only used as a fallback when the LLM is unavailable.
        
        Returns:
            FavorabilityResult with entity stances, overall stance, and evidence.
        """
        if not text:
            return FavorabilityResult(
                entity_stances=[],
                overall_gop_stance="neutral",
                overall_confidence=0.0,
                gop_entities_found=[],
                reasoning="Empty text",
            )
        
        # 1. Extract GOP entities
        gop_entities = self._extract_gop_entities(text)
        
        if not gop_entities:
            return FavorabilityResult(
                entity_stances=[],
                overall_gop_stance="neutral",
                overall_confidence=0.5,
                gop_entities_found=[],
                reasoning="No GOP entities mentioned",
            )
        
        # 2. Compute deterministic signals (always, used as LLM context)
        signals = self._compute_signals(text, gop_entities)
        
        # 3. LLM is primary classifier
        if self.llm_enabled and self._llm_client and self._llm_client.is_available:
            return self._llm_classify(text, gop_entities, signals)
        
        # 4. Heuristic fallback only when LLM unavailable
        logger.warning("LLM unavailable, using heuristic fallback for favorability")
        return self._heuristic_classify(text, gop_entities, signals)
