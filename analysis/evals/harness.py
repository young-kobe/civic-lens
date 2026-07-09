"""
Eval harness plumbing: prompt fingerprinting and the replay / recording
LLM clients.

The eval runner exercises the REAL pipeline code (ClaimExtractor.extract,
schema validation, evidence-span verification) in both modes; only the
network call is swapped out:

- ReplayLLMClient returns a previously recorded model response, round-
  tripped through ``BaseLLMClient.parse_json_response`` so schema
  validation runs exactly as it does in production. CI uses this mode —
  deterministic, no API keys.
- RecordingLLMClient wraps a real backend client and captures the parsed
  response so it can be committed under analysis/evals/recorded/ for
  later replay.

Every recording carries a prompt fingerprint (hash of system prompt +
user template + JSON schema + prompt version). When any of those change,
replayed responses no longer describe the current pipeline, so the
runner treats fingerprint-stale recordings as a gate failure until they
are re-recorded in live mode.
"""

import hashlib
import json
from typing import Any, Dict, Optional

from analysis.src.llm.base import BaseLLMClient
from analysis.src.llm.prompts import (
    CLAIM_EXTRACTION_PROMPT_VERSION,
    CLAIM_EXTRACTION_SYSTEM_PROMPT,
    CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE,
)
from analysis.src.llm.schemas import CLAIM_EXTRACTION_SCHEMA


def claim_extraction_fingerprint() -> str:
    """Stable hash of everything that shapes a claim-extraction inference.

    If this changes, recorded responses were produced by a different
    prompt/schema and can no longer stand in for the current pipeline.
    """
    material = json.dumps(
        {
            "system_prompt": CLAIM_EXTRACTION_SYSTEM_PROMPT,
            "user_prompt_template": CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE,
            "schema": CLAIM_EXTRACTION_SCHEMA,
            "prompt_version": CLAIM_EXTRACTION_PROMPT_VERSION,
        },
        sort_keys=True,
    )
    return "sha256:" + hashlib.sha256(material.encode("utf-8")).hexdigest()


class ReplayLLMClient(BaseLLMClient):
    """Returns one recorded response, validated against the live schema.

    The recorded response is serialized and re-parsed through
    ``parse_json_response`` so the same validation path that guards
    production inference also guards replay — a schema change that would
    reject the model's output in production rejects it here too.
    """

    def __init__(self, recorded_response: Dict[str, Any]):
        super().__init__()
        self._recorded_response = recorded_response

    @property
    def is_available(self) -> bool:
        return True

    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        return self.parse_json_response(
            json.dumps(self._recorded_response), schema=response_schema
        )


class RecordingLLMClient(BaseLLMClient):
    """Delegates to a real backend client and keeps the parsed response."""

    def __init__(self, inner: BaseLLMClient):
        super().__init__()
        self._inner = inner
        self.last_response: Optional[Dict[str, Any]] = None

    @property
    def is_available(self) -> bool:
        return self._inner.is_available

    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        response = self._inner.complete(
            system_prompt=system_prompt,
            user_prompt=user_prompt,
            response_schema=response_schema,
            temperature=temperature,
        )
        self.last_response = response
        return response
