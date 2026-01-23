"""
Google Gemini LLM Client for Civic Lens Analysis.

Provides a wrapper around the Google Generative AI SDK.
"""

import time
from typing import Any, Dict, Optional
from analysis.src.llm.base import BaseLLMClient
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class GeminiClient(BaseLLMClient):
    """
    Client for Google Gemini API.
    
    Handles prompt construction, API calls, and response parsing
    using the Google Generative AI SDK.
    """
    
    def __init__(
        self,
        api_key: str,
        model: str = "gemini-2.0-flash",
        temperature: float = 0.0,
        max_retries: int = 3,
    ):
        super().__init__()
        self.api_key = api_key
        self.model = model
        self.temperature = temperature
        self.max_retries = max_retries
        self._model_instance = None
        
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
        
        full_prompt = f"{system_prompt}\n\n---\n\n{user_prompt}"
        
        last_error = None
        for attempt in range(self.max_retries):
            try:
                response = self._model_instance.generate_content(full_prompt)
                
                if hasattr(response, 'usage_metadata'):
                    self.total_tokens_used += getattr(
                        response.usage_metadata, 'total_token_count', 0
                    )
                
                text = response.text.strip()
                return self.parse_json_response(text)
                
            except Exception as e:
                last_error = e
                wait_time = (2 ** attempt) + 0.5
                logger.warning(
                    f"Gemini API call failed (attempt {attempt + 1}/{self.max_retries}): {e}. "
                    f"Retrying in {wait_time:.1f}s..."
                )
                time.sleep(wait_time)
        
        raise RuntimeError(f"Gemini API call failed after {self.max_retries} retries: {last_error}")
