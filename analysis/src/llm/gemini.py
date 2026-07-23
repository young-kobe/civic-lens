"""
Google Gemini LLM Client for Civic Lens Analysis.

Wraps the ``google-genai`` SDK — the replacement for the deprecated
``google-generativeai`` package (Google archived it as
deprecated-generative-ai-python; no fixes, new models land only in the new
SDK). Supports structured output via response_schema.
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
    Client for the Google Gemini API (google-genai SDK).

    Handles prompt construction, API calls, and response parsing.
    Supports structured output via response_schema in the generation
    config. Unlike the old google-generativeai wrapper, the system prompt
    is sent as a real ``system_instruction`` role instead of being
    concatenated into the user turn — matching how ``prompt_versions``
    stores system and user templates separately (invariant B1).
    """

    def __init__(
        self,
        api_key: str,
        model: str = "gemini-3.5-flash",
        temperature: float = 0.0,
        max_retries: int = 3,
    ):
        super().__init__()
        self.api_key = api_key
        self.model = model
        self.temperature = temperature
        self.max_retries = max_retries
        self._client = None
        self._types = None

        if api_key:
            self._initialize_client()

    def _initialize_client(self):
        """Initialize the Gemini client."""
        try:
            from google import genai
            from google.genai import types
            self._client = genai.Client(api_key=self.api_key)
            self._types = types
            logger.info(f"Initialized Gemini client with model={self.model}")
        except ImportError:
            logger.warning("google-genai package not installed. LLM features disabled.")
            self._client = None
        except Exception as e:
            logger.error(f"Failed to initialize Gemini client: {e}")
            self._client = None

    @property
    def is_available(self) -> bool:
        """Check if the LLM client is properly initialized."""
        return self._client is not None

    def complete_once(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """Single-attempt request+parse — no retry, no sleep.

        Shared body extracted so complete()'s retry loop and the new
        llm/client.py transport path cannot drift apart. Raises on any
        failure; callers decide whether to retry.
        """
        if not self.is_available:
            raise RuntimeError("Gemini client not initialized. Check API key.")

        config_kwargs: Dict[str, Any] = {
            "system_instruction": system_prompt,
            "temperature": temperature if temperature is not None else self.temperature,
            "response_mime_type": "application/json",
            # Permissive thresholds — see _PERMISSIVE_SAFETY_SETTINGS.
            "safety_settings": [
                self._types.SafetySetting(**s) for s in _PERMISSIVE_SAFETY_SETTINGS
            ],
        }
        if response_schema:
            config_kwargs["response_schema"] = response_schema
        config = self._types.GenerateContentConfig(**config_kwargs)

        response = self._client.models.generate_content(
            model=self.model,
            contents=user_prompt,
            config=config,
        )

        usage = getattr(response, "usage_metadata", None)
        if usage is not None:
            self.total_tokens_used += getattr(usage, "total_token_count", 0) or 0

        text = (response.text or "").strip()
        return self.parse_json_response(text, schema=response_schema)

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
            RuntimeError: If client is not initialized or retries exhausted
            ValueError: If response cannot be parsed as JSON
        """
        if not self.is_available:
            raise RuntimeError("Gemini client not initialized. Check API key.")

        last_error = None
        for attempt in range(self.max_retries):
            try:
                return self.complete_once(system_prompt, user_prompt, response_schema, temperature)

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
