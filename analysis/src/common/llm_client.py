"""
Gemini LLM Client for Civic Lens Analysis.

Provides a wrapper around the Google Generative AI SDK with:
- Retry logic with exponential backoff
- JSON response parsing and validation
- Token usage tracking
"""

import json
import time
from typing import Any, Dict, Optional
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class GeminiClient:
    """
    Client for Google Gemini API.
    
    Handles prompt construction, API calls, and response parsing.
    """
    
    def __init__(
        self,
        api_key: str,
        model: str = "gemini-2.0-flash",
        temperature: float = 0.0,
        max_retries: int = 3,
    ):
        self.api_key = api_key
        self.model = model
        self.temperature = temperature
        self.max_retries = max_retries
        self._client = None
        self._model_instance = None
        self.total_tokens_used = 0
        
        if api_key:
            self._initialize_client()
    
    def _initialize_client(self):
        """Initialize the Gemini client."""
        try:
            import google.generativeai as genai
            genai.configure(api_key=self.api_key)
            self._model_instance = genai.GenerativeModel(
                model_name=self.model,
                generation_config={
                    "temperature": self.temperature,
                    "response_mime_type": "application/json",
                }
            )
            logger.info(f"Initialized Gemini client with model={self.model}")
        except ImportError:
            logger.warning("google-generativeai package not installed. LLM features disabled.")
            self._model_instance = None
        except Exception as e:
            logger.error(f"Failed to initialize Gemini client: {e}")
            self._model_instance = None
    
    @property
    def is_available(self) -> bool:
        """Check if the LLM client is properly initialized."""
        return self._model_instance is not None
    
    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """
        Send a completion request to Gemini.
        
        Args:
            system_prompt: System instructions for the model
            user_prompt: User message/query
            temperature: Override default temperature (optional)
            
        Returns:
            Parsed JSON response as a dictionary
            
        Raises:
            RuntimeError: If client is not initialized
            ValueError: If response cannot be parsed as JSON
        """
        if not self.is_available:
            raise RuntimeError("Gemini client not initialized. Check API key.")
        
        # Combine prompts (Gemini uses a different format than OpenAI)
        full_prompt = f"{system_prompt}\n\n---\n\n{user_prompt}"
        
        last_error = None
        for attempt in range(self.max_retries):
            try:
                response = self._model_instance.generate_content(full_prompt)
                
                # Track token usage if available
                if hasattr(response, 'usage_metadata'):
                    self.total_tokens_used += getattr(
                        response.usage_metadata, 'total_token_count', 0
                    )
                
                # Parse response text
                text = response.text.strip()
                return self.parse_json_response(text)
                
            except Exception as e:
                last_error = e
                wait_time = (2 ** attempt) + 0.5  # Exponential backoff
                logger.warning(
                    f"Gemini API call failed (attempt {attempt + 1}/{self.max_retries}): {e}. "
                    f"Retrying in {wait_time:.1f}s..."
                )
                time.sleep(wait_time)
        
        raise RuntimeError(f"Gemini API call failed after {self.max_retries} retries: {last_error}")
    
    def parse_json_response(self, response_text: str) -> Dict[str, Any]:
        """
        Parse and validate JSON response from LLM.
        
        Args:
            response_text: Raw text response from the model
            
        Returns:
            Parsed dictionary
            
        Raises:
            ValueError: If response is not valid JSON
        """
        # Strip markdown code blocks if present
        text = response_text.strip()
        if text.startswith("```json"):
            text = text[7:]
        elif text.startswith("```"):
            text = text[3:]
        if text.endswith("```"):
            text = text[:-3]
        text = text.strip()
        
        try:
            return json.loads(text)
        except json.JSONDecodeError as e:
            logger.error(f"Failed to parse LLM response as JSON: {e}")
            logger.debug(f"Raw response: {response_text[:500]}")
            raise ValueError(f"Invalid JSON response from LLM: {e}")
    
    def get_token_usage(self) -> int:
        """Get total tokens used across all calls."""
        return self.total_tokens_used


# Singleton instance for shared use
_client_instance: Optional[GeminiClient] = None


def get_gemini_client(
    api_key: Optional[str] = None,
    model: Optional[str] = None,
) -> GeminiClient:
    """
    Get or create the shared Gemini client instance.
    
    Args:
        api_key: API key (uses settings if not provided)
        model: Model name (uses settings if not provided)
        
    Returns:
        GeminiClient instance
    """
    global _client_instance
    
    if _client_instance is None:
        from analysis.src.common.settings import get_settings
        settings = get_settings()
        
        _client_instance = GeminiClient(
            api_key=api_key or settings.gemini_api_key,
            model=model or settings.gemini_model,
            temperature=settings.gemini_temperature,
        )
    
    return _client_instance
