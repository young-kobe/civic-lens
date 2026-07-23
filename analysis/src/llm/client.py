"""
Unified LLM client (Postgres redesign, Phase 5 — analysis plumbing). Single
retry/backoff policy + single confidence-coercion pass on top of a
transport-only backend (backend.complete_once()), replacing each engine's
own copy-pasted retry loop. Pre-Phase-6 engines are unaffected: they keep
calling backend.complete() via get_llm_client() until ported.

`embed()` (added Phase 6 Wave 3) is a thin passthrough to the backend's own
embed() when it supports one -- no retry/backoff wrapping, since callers
(narrative_clustering.py) already treat a None/failed embed as a per-text
fallback signal, not a retryable transport error.
"""

import time
from typing import Any, Dict, Optional, Protocol

from analysis.src.common.logger import get_logger
from analysis.src.llm.base import BaseLLMClient, normalize_confidence
from analysis.src.llm.constants import BACKOFF_BASE_SECONDS, DEFAULT_MAX_RETRIES

logger = get_logger(__name__)


class TransportBackend(Protocol):
    """Structural type for a backend usable by LLMClient.

    Satisfied by GeminiClient/OllamaClient/OpenAICompatClient (via their
    additive complete_once()) and by fake backends in tests.
    """

    @property
    def is_available(self) -> bool: ...

    def complete_once(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]: ...

    def get_token_usage(self) -> int: ...


def _coerce_confidence_fields(data: Any) -> None:
    """Recursively coerce every dict key named or suffixed '_confidence'
    onto [0, 1] via normalize_confidence, mutating in place.

    Applied once here, on the way out of LLMClient.complete(), instead of
    at each engine read site (the duplication base.py's normalize_confidence
    docstring flags: engines today call it per-field because the schema-
    driven _coerce_numeric_scales never fires — the schemas omit minimum/
    maximum for Gemini compatibility).
    """
    if isinstance(data, dict):
        for key, value in data.items():
            if key == "confidence" or key.endswith("_confidence"):
                if isinstance(value, (int, float)) and not isinstance(value, bool):
                    data[key] = normalize_confidence(value, key)
            else:
                _coerce_confidence_fields(value)
    elif isinstance(data, list):
        for item in data:
            _coerce_confidence_fields(item)


class LLMClient:
    """Wraps a transport backend with one retry/backoff policy and one
    confidence-coercion pass. Engines receive an instance via constructor
    injection instead of importing a backend directly."""

    def __init__(self, backend: TransportBackend, max_retries: int = DEFAULT_MAX_RETRIES):
        self._backend = backend
        self.max_retries = max_retries

    @property
    def is_available(self) -> bool:
        """Check if the wrapped backend is ready."""
        return self._backend.is_available

    @property
    def total_tokens_used(self) -> int:
        """Token usage in one shape regardless of backend."""
        return self._backend.get_token_usage()

    def get_token_usage(self) -> int:
        """Get total tokens used across all calls."""
        return self.total_tokens_used

    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """
        Send a completion request through the retry-wrapped transport.

        A schema-invalid response (SchemaValidationError, raised inside
        complete_once()'s parse_json_response call) is retried exactly like
        a transport error — same loop, same attempt count.

        Args:
            system_prompt: System instructions for the model
            user_prompt: User message/query
            response_schema: JSON schema to enforce structured output (optional)
            temperature: Override default temperature (optional)

        Returns:
            Parsed JSON response as a dictionary, with confidence fields
            coerced onto [0, 1].

        Raises:
            RuntimeError: If the backend is unavailable or retries are exhausted
        """
        if not self.is_available:
            raise RuntimeError("LLM backend not available")

        last_error = None
        for attempt in range(self.max_retries):
            try:
                result = self._backend.complete_once(
                    system_prompt, user_prompt, response_schema, temperature
                )
                _coerce_confidence_fields(result)
                return result

            except Exception as e:
                last_error = e
                # Don't sleep after the final attempt — matches the retry
                # semantics ported from gemini.py/ollama.py/openai_compat.py.
                if attempt < self.max_retries - 1:
                    wait_time = (2 ** attempt) + BACKOFF_BASE_SECONDS
                    logger.warning(
                        f"LLM call failed (attempt {attempt + 1}/{self.max_retries}): {e}. "
                        f"Retrying in {wait_time:.1f}s..."
                    )
                    time.sleep(wait_time)
                else:
                    logger.warning(
                        f"LLM call failed (attempt {attempt + 1}/{self.max_retries}): {e}."
                    )

        raise RuntimeError(f"LLM call failed after {self.max_retries} retries: {last_error}")

    @property
    def supports_embedding(self) -> bool:
        """True when the wrapped backend's class overrides
        `BaseLLMClient.embed`'s no-op default. Every backend "has" the
        `embed` attribute via that shared base class, so a plain `hasattr`
        check can't tell real support (Ollama/OpenAICompat) from the
        default no-op (Gemini) -- checked by identity against the
        unbound base method instead."""
        backend_embed = getattr(type(self._backend), "embed", None)
        return backend_embed is not None and backend_embed is not BaseLLMClient.embed

    def embed(self, text: str, model: Optional[str] = None) -> Optional[list]:
        """Passthrough to the backend's embed() when it actually supports
        embeddings (see `supports_embedding`). Raises RuntimeError naming
        the backend when it doesn't, so a caller can distinguish "this
        backend was never going to embed anything" (check `supports_
        embedding` first to avoid the exception path) from "embed() ran and
        returned None this call" (a transient per-text failure the backend
        already handles by returning None, unaffected by this check)."""
        if not self.supports_embedding:
            raise RuntimeError(f"{type(self._backend).__name__} does not support embed()")
        return self._backend.embed(text, model=model)


_client_instance: Optional[LLMClient] = None


def get_client() -> LLMClient:
    """Get or create the shared LLMClient singleton.

    Mirrors factory.py's lazy-singleton pattern, but there is exactly one
    of these (not one per backend) — the backend is resolved via the
    existing factory/settings pattern (CIVIC_LLM_BACKEND).
    """
    global _client_instance

    if _client_instance is None:
        from analysis.src.llm.factory import get_llm_client
        _client_instance = LLMClient(get_llm_client())

    return _client_instance


def reset_client() -> None:
    """Clear the singleton. Test hook only — production code never needs to
    reset the configured client mid-process."""
    global _client_instance
    _client_instance = None
