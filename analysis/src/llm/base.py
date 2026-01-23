"""
Base LLM Client for Civic Lens Analysis.

Defines the abstract interface that all LLM backends must implement.
"""

import json
from abc import ABC, abstractmethod
from typing import Any, Dict, Optional
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class BaseLLMClient(ABC):
    """
    Abstract base class for LLM clients.
    
    All LLM backends (Gemini, Ollama, etc.) must inherit from this class
    and implement the required methods.
    """
    
    def __init__(self):
        self.total_tokens_used = 0
    
    @property
    @abstractmethod
    def is_available(self) -> bool:
        """Check if the LLM client is properly initialized and ready."""
        pass
    
    @abstractmethod
    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """
        Send a completion request and return parsed JSON.
        
        Args:
            system_prompt: System instructions for the model
            user_prompt: User message/query
            temperature: Override default temperature (optional)
            
        Returns:
            Parsed JSON response as a dictionary
            
        Raises:
            RuntimeError: If client is not available
            ValueError: If response cannot be parsed as JSON
        """
        pass
    
    def get_token_usage(self) -> int:
        """Get total tokens used across all calls."""
        return self.total_tokens_used
    
    @staticmethod
    def parse_json_response(response_text: str) -> Dict[str, Any]:
        """
        Parse and validate JSON response from LLM.
        
        Applies multi-layered repair strategies for common LLM JSON errors:
        1. Strip markdown code fences
        2. Extract JSON object/array using regex (handles surrounding text)
        3. Fix trailing commas
        4. Fix missing commas between fields
        
        Args:
            response_text: Raw text response from the model
            
        Returns:
            Parsed dictionary
            
        Raises:
            ValueError: If response is not valid JSON after repair attempts
        """
        import re
        
        text = response_text.strip()
        
        # Step 1: Strip markdown code fences
        if text.startswith("```json"):
            text = text[7:]
        elif text.startswith("```"):
            text = text[3:]
        if text.endswith("```"):
            text = text[:-3]
        text = text.strip()
        
        # Step 2: Try parsing as-is first (fast path)
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            pass  # Continue to repair strategies
        
        # Step 3: Extract JSON object/array from surrounding text
        # This handles cases where LLM adds explanation before/after JSON
        json_match = re.search(r'(\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}|\[[^\[\]]*(?:\[[^\[\]]*\][^\[\]]*)*\])', text, re.DOTALL)
        if json_match:
            extracted = json_match.group(1)
            try:
                return json.loads(extracted)
            except json.JSONDecodeError:
                text = extracted  # Use extracted for further repair
        
        # Step 4: Fix trailing commas (e.g., [1, 2, 3,] or {"a": 1,})
        repaired = re.sub(r',(\s*[}\]])', r'\1', text)
        if repaired != text:
            try:
                result = json.loads(repaired)
                logger.debug("JSON repair: fixed trailing comma(s)")
                return result
            except json.JSONDecodeError:
                text = repaired  # Keep the fix for next repair
        
        # Step 5: Fix missing commas between fields
        # Pattern: "value" "key" or "value" {"key" or number "key"
        repaired = re.sub(r'("|\d)(\s+)(")', r'\1,\2\3', text)
        # Also handle: } "key" or ] "key"
        repaired = re.sub(r'([}\]])(\s+)(")', r'\1,\2\3', repaired)
        if repaired != text:
            try:
                result = json.loads(repaired)
                logger.debug("JSON repair: fixed missing comma(s)")
                return result
            except json.JSONDecodeError:
                text = repaired
        
        # Step 6: Fix unescaped newlines in string values
        # This is a best-effort fix for strings containing literal newlines
        repaired = re.sub(r'(?<!\\)\n(?=[^"]*"[,}\]])', r'\\n', text)
        if repaired != text:
            try:
                result = json.loads(repaired)
                logger.debug("JSON repair: fixed unescaped newlines")
                return result
            except json.JSONDecodeError:
                pass
        
        # All repair attempts failed
        logger.error(f"Failed to parse LLM response as JSON after repair attempts")
        logger.debug(f"Raw response: {response_text[:500]}")
        raise ValueError(f"Invalid JSON response from LLM after repair attempts")
