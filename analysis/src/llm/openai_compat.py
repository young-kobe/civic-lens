"""
OpenAI-compatible LLM client for Civic Lens Analysis.

Speaks the OpenAI REST surface (/v1/chat/completions, /v1/embeddings,
/v1/models), which is the lingua franca of self-hosted inference: LiteLLM,
vLLM, llama.cpp server, hosted serverless GPU endpoints, and a future custom
token router all expose it. Selecting CIVIC_LLM_BACKEND=openai_compat plus
CIVIC_LLM_BASE_URL/_API_KEY/_MODEL is the entire cutover — no engine code
changes.
"""

import time
from typing import Any, Dict, Optional
import requests
from analysis.src.llm.base import BaseLLMClient
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class OpenAICompatClient(BaseLLMClient):
    """
    Client for any server exposing the OpenAI-compatible /v1 REST API.

    Unlike the Gemini/Ollama clients (which flatten system+user into one
    prompt string), this backend sends proper role-separated chat messages.
    Structured output uses the response_format json_schema mechanism; the
    inherited parse_json_response() still validates the result client-side,
    so schema enforcement stays backend-agnostic.
    """

    def __init__(
        self,
        base_url: str,
        api_key: str = "",
        model: str = "",
        embedding_model: str = "",
        timeout: int = 120,
        max_retries: int = 3,
        temperature: float = 0.0,
    ):
        super().__init__()
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.model = model
        self.embedding_model = embedding_model
        self.timeout = timeout
        self.max_retries = max_retries
        self.temperature = temperature
        self._available = self._check_availability()

    def _headers(self) -> Dict[str, str]:
        headers = {"Content-Type": "application/json"}
        if self.api_key:
            headers["Authorization"] = f"Bearer {self.api_key}"
        return headers

    def _check_availability(self) -> bool:
        """Check if the server is reachable via the /v1/models listing."""
        if not self.base_url:
            logger.warning(
                "openai_compat backend selected but CIVIC_LLM_BASE_URL is empty"
            )
            return False
        try:
            response = requests.get(
                f"{self.base_url}/v1/models", headers=self._headers(), timeout=5
            )
            if response.status_code == 200:
                logger.info(f"OpenAI-compatible server available at {self.base_url}")
                return True
            return False
        except requests.exceptions.RequestException as e:
            logger.warning(
                f"OpenAI-compatible server not reachable at {self.base_url}: {e}"
            )
            return False

    @property
    def is_available(self) -> bool:
        """Check if the server is reachable."""
        return self._available

    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """
        Send a chat completion request.

        Args:
            system_prompt: System instructions for the model
            user_prompt: User message/query
            response_schema: JSON schema for structured output (optional)
            temperature: Override default temperature (optional)

        Returns:
            Parsed JSON response as a dictionary

        Raises:
            RuntimeError: If server is not available
            ValueError: If response cannot be parsed as JSON
        """
        if not self.is_available:
            raise RuntimeError(
                f"OpenAI-compatible server not available at {self.base_url}"
            )

        payload: Dict[str, Any] = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "temperature": temperature if temperature is not None else self.temperature,
        }
        if response_schema is not None:
            payload["response_format"] = {
                "type": "json_schema",
                "json_schema": {
                    "name": "response",
                    "strict": True,
                    "schema": response_schema,
                },
            }
        else:
            payload["response_format"] = {"type": "json_object"}

        last_error = None
        for attempt in range(self.max_retries):
            try:
                response = requests.post(
                    f"{self.base_url}/v1/chat/completions",
                    json=payload,
                    headers=self._headers(),
                    timeout=self.timeout,
                )
                response.raise_for_status()

                result = response.json()

                usage = result.get("usage") or {}
                self.total_tokens_used += usage.get("total_tokens", 0)

                response_text = result["choices"][0]["message"]["content"]
                return self.parse_json_response(response_text, schema=response_schema)

            except requests.exceptions.Timeout:
                last_error = TimeoutError(
                    f"OpenAI-compatible request timed out after {self.timeout}s"
                )
                logger.warning(
                    f"openai_compat timeout (attempt {attempt + 1}/{self.max_retries})"
                )
            except requests.exceptions.RequestException as e:
                last_error = e
                logger.warning(
                    f"openai_compat API call failed (attempt {attempt + 1}/{self.max_retries}): {e}"
                )
            except Exception as e:
                last_error = e
                logger.warning(
                    f"openai_compat response parsing failed (attempt {attempt + 1}/{self.max_retries}): {e}"
                )

            if attempt < self.max_retries - 1:
                # Exponential backoff matches gemini.py/ollama.py — one shared
                # retry policy keeps behavior predictable across backends.
                time.sleep((2 ** attempt) + 0.5)

        raise RuntimeError(
            f"OpenAI-compatible API call failed after {self.max_retries} retries: {last_error}"
        )

    def embed(self, text: str, model: Optional[str] = None) -> Optional[list]:
        """Get an embedding vector via the /v1/embeddings endpoint.

        Returns None on any failure so the caller can fall back to a non-
        embedding similarity strategy without crashing the pipeline.
        """
        if not self.is_available or not text:
            return None
        embed_model = model or self.embedding_model or self.model
        try:
            response = requests.post(
                f"{self.base_url}/v1/embeddings",
                json={"model": embed_model, "input": text[:2000]},
                headers=self._headers(),
                timeout=self.timeout,
            )
            response.raise_for_status()
            payload = response.json()
            data = payload.get("data") or []
            if data and isinstance(data[0].get("embedding"), list) and data[0]["embedding"]:
                return data[0]["embedding"]
            return None
        except Exception as e:
            logger.warning(
                f"openai_compat embedding call failed for model={embed_model}: {e}"
            )
            return None
