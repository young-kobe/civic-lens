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
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """
        Send a completion request and return parsed JSON.
        
        Args:
            system_prompt: System instructions for the model
            user_prompt: User message/query
            response_schema: JSON schema to enforce structured output (optional)
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
        Parse JSON response from LLM.
        
        When using structured output (JSON schema mode), the LLM is 
        constrained to return valid JSON matching the schema. This 
        method simply parses the response with minimal cleanup.
        
        Args:
            response_text: Raw text response from the model
            
        Returns:
            Parsed dictionary
            
        Raises:
            ValueError: If response is not valid JSON
        """
        text = response_text.strip()
        
        # Strip markdown code fences if present (some models add these)
        if text.startswith("```json"):
            text = text[7:]
        elif text.startswith("```"):
            text = text[3:]
        if text.endswith("```"):
            text = text[:-3]
        text = text.strip()
        
        try:
            parsed = json.loads(text)
            if isinstance(parsed, dict):
                return parsed
            elif isinstance(parsed, list) and len(parsed) > 0 and isinstance(parsed[0], dict):
                # Some models return array of objects - take first
                return parsed[0]
            else:
                raise ValueError(f"Expected dict, got {type(parsed).__name__}")
        except json.JSONDecodeError as e:
            logger.error(f"JSON parse error: {e}")
            logger.debug(f"Raw response: {response_text[:500]}")
            raise ValueError(f"Invalid JSON response: {e}")
