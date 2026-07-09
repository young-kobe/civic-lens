"""
Google Gemini LLM Client for Civic Lens Analysis.

Provides a wrapper around the Google Generative AI SDK.
Supports structured output via response_schema.
"""

import time
from typing import Any, Dict, Optional
from analysis.src.llm.base import BaseLLMClient
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


# Gemini's default safety thresholds (BLOCK_MEDIUM_AND_ABOVE) refuse a large
# fraction of political-discourse posts as "harassment" or "hate speech",
# which is exactly the content we are tasked with ANALYZING. A blocked
# prompt forces a heuristic fallback (or an empty result for claim/
# propaganda tasks), which is the failure mode the audit trail flags as
# "ai_outputs.inference_method='heuristic'". Setting BLOCK_NONE on the
# four configurable categories lets the model read the input; the model
# still self-censors what it WRITES via the structured-output schema, and
# we never display its reasoning verbatim — only the labelled outputs that
# the prompt rubric requires. Documented at
# https://ai.google.dev/gemini-api/docs/safety-settings
_PERMISSIVE_SAFETY_SETTINGS = [
    {"category": "HARM_CATEGORY_HARASSMENT", "threshold": "BLOCK_NONE"},
    {"category": "HARM_CATEGORY_HATE_SPEECH", "threshold": "BLOCK_NONE"},
    {"category": "HARM_CATEGORY_SEXUALLY_EXPLICIT", "threshold": "BLOCK_NONE"},
    {"category": "HARM_CATEGORY_DANGEROUS_CONTENT", "threshold": "BLOCK_NONE"},
]


class GeminiClient(BaseLLMClient):
    """
    Client for Google Gemini API.
    
    Handles prompt construction, API calls, and response parsing
    using the Google Generative AI SDK.
    
    Supports structured output via response_schema in generation_config.
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
        self._genai = None
        
        if api_key:
            self._initialize_client()
    
    def _initialize_client(self):
        """Initialize the Gemini client."""
        try:
            import google.generativeai as genai
            genai.configure(api_key=self.api_key)
            self._genai = genai
            logger.info(f"Initialized Gemini client with model={self.model}")
        except ImportError:
            logger.warning("google-generativeai package not installed. LLM features disabled.")
            self._genai = None
        except Exception as e:
            logger.error(f"Failed to initialize Gemini client: {e}")
            self._genai = None
    
    @property
    def is_available(self) -> bool:
        """Check if the LLM client is properly initialized."""
        return self._genai is not None
    
    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """
        Send a completion request to Gemini.
        
        Args:
            system_prompt: System instructions for the model
            user_prompt: User message/query
            response_schema: JSON schema for structured output (optional)
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
        
        # Build generation config with optional schema
        generation_config = {
            "temperature": temperature if temperature is not None else self.temperature,
            "response_mime_type": "application/json",
        }
        if response_schema:
            generation_config["response_schema"] = response_schema
        
        # Create model instance with config + permissive safety settings
        # (see _PERMISSIVE_SAFETY_SETTINGS for the rationale).
        model_instance = self._genai.GenerativeModel(
            model_name=self.model,
            generation_config=generation_config,
            safety_settings=_PERMISSIVE_SAFETY_SETTINGS,
        )
        
        last_error = None
        for attempt in range(self.max_retries):
            try:
                response = model_instance.generate_content(full_prompt)
                
                if hasattr(response, 'usage_metadata'):
                    self.total_tokens_used += getattr(
                        response.usage_metadata, 'total_token_count', 0
                    )
                
                text = response.text.strip()
                return self.parse_json_response(text, schema=response_schema)
                
            except Exception as e:
                last_error = e
                # Don't sleep after the final attempt — there's no retry to
                # back off for (audit A-12), matching ollama.py's guard.
                if attempt < self.max_retries - 1:
                    wait_time = (2 ** attempt) + 0.5
                    logger.warning(
                        f"Gemini API call failed (attempt {attempt + 1}/{self.max_retries}): {e}. "
                        f"Retrying in {wait_time:.1f}s..."
                    )
                    time.sleep(wait_time)
                else:
                    logger.warning(
                        f"Gemini API call failed (attempt {attempt + 1}/{self.max_retries}): {e}."
                    )

        raise RuntimeError(f"Gemini API call failed after {self.max_retries} retries: {last_error}")
