"""
Base LLM Client for Civic Lens Analysis.

Defines the abstract interface that all LLM backends must implement.
"""

import json
from abc import ABC, abstractmethod
from typing import Any, Dict, Optional
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class SchemaValidationError(ValueError):
    """Raised when an LLM response does not satisfy the provided JSON schema."""


# Values at or above this (up to 100) are read as a 0-100 percentage and
# divided by 100. Values strictly between 1 and this are treated as small
# overshoots of the 0-1 scale and clamped down to 1.0 instead of divided —
# a model that reports 1.5 meant "very confident", not "0.015".
_PERCENTAGE_SCALE_MIN = 2.0


def normalize_confidence(value: Any, field_name: str = "confidence") -> float:
    """Coerce an LLM-reported confidence onto the [0, 1] scale.

    Two regressions are handled here, in order:

    1. **0-100 percentage scale.** Qwen-class local models occasionally emit
       confidence as a percentage despite the prompt's 0-1 rule. A value in
       ``[_PERCENTAGE_SCALE_MIN, 100]`` is divided by 100 with a warning so the
       regression stays visible. This mirrors ``_coerce_numeric_scales`` but
       fires at the engine read sites, because the Gemini-facing schemas omit
       ``minimum``/``maximum`` (Gemini rejects those keywords) and so the
       schema-level coercer never runs.
    2. **Range overshoot.** Everything is then clamped to ``[0.0, 1.0]`` — the
       same guard claims/propaganda already apply inline. A value just above 1
       (e.g. 1.5) clamps to 1.0 rather than being read as a tiny percentage.

    Non-numeric input falls back to 0.5 (the neutral default the read sites
    already used).
    """
    try:
        v = float(value)
    except (TypeError, ValueError):
        return 0.5
    if _PERCENTAGE_SCALE_MIN <= v <= 100.0:
        logger.warning(
            f"Coerced {field_name} from 0-100 scale ({v}) to 0-1 ({v / 100})"
        )
        v = v / 100.0
    return max(0.0, min(1.0, v))


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
    
    def embed(self, text: str, model: Optional[str] = None) -> Optional[list]:
        """Return an embedding vector for ``text``, or None if not supported.

        The default returns None so backends that do not implement embeddings
        (or where the embedding model isn't installed) fall back gracefully.
        """
        return None

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
    def parse_json_response(
        response_text: str,
        schema: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """
        Parse and validate a JSON response from the LLM.

        If a schema is supplied, the parsed dict is validated against it —
        missing required fields, out-of-range numbers, invalid enums, or
        wrong types raise SchemaValidationError. Callers should treat that
        the same as any other LLM failure (trigger heuristic fallback).
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
        except json.JSONDecodeError as e:
            logger.error(f"JSON parse error: {e}")
            logger.debug(f"Raw response: {response_text[:500]}")
            raise ValueError(f"Invalid JSON response: {e}")

        if isinstance(parsed, list) and len(parsed) > 0 and isinstance(parsed[0], dict):
            # Some models return array of objects - take first
            parsed = parsed[0]
        if not isinstance(parsed, dict):
            raise ValueError(f"Expected dict, got {type(parsed).__name__}")

        if schema is not None:
            BaseLLMClient._coerce_numeric_scales(parsed, schema, path="")
            BaseLLMClient._validate_against_schema(parsed, schema, path="")

        return parsed

    @staticmethod
    def _coerce_numeric_scales(data: Any, schema: Dict[str, Any], path: str) -> None:
        """Coerce LLM-reported 0-100 values back to the 0-1 scale the schema expects.

        Qwen-class local models occasionally emit confidence as a percentage
        despite the prompt's explicit 0-1 rule. Rather than reject those rows
        outright (which silently drops real classifications onto heuristic
        fallback), divide by 100 and log a warning so the regression is
        visible. Only applies to numeric fields with maximum=1 and minimum>=0;
        any value in (1, 100] is in scope. Values >100 or <0 are left for
        the validator to reject.
        """
        expected_type = schema.get("type")
        if expected_type == "object" and isinstance(data, dict):
            for key, subschema in schema.get("properties", {}).items():
                if key not in data:
                    continue
                sub_type = subschema.get("type")
                if sub_type in ("number", "integer"):
                    v = data[key]
                    if (
                        isinstance(v, (int, float))
                        and not isinstance(v, bool)
                        and subschema.get("maximum") == 1
                        and subschema.get("minimum", 0) >= 0
                        and 1.0 < v <= 100.0
                    ):
                        logger.warning(
                            f"Coerced {path + '.' + key if path else key} "
                            f"from 0-100 scale ({v}) to 0-1 ({v / 100})"
                        )
                        data[key] = v / 100.0
                elif sub_type in ("object", "array"):
                    BaseLLMClient._coerce_numeric_scales(
                        data[key], subschema,
                        f"{path}.{key}" if path else key,
                    )
        elif expected_type == "array" and isinstance(data, list):
            item_schema = schema.get("items")
            if item_schema:
                for i, item in enumerate(data):
                    BaseLLMClient._coerce_numeric_scales(item, item_schema, f"{path}[{i}]")

    @staticmethod
    def _validate_against_schema(data: Any, schema: Dict[str, Any], path: str) -> None:
        """Minimal JSON Schema validator covering the constraints we rely on.

        Supports: required fields, type (object/array/string/number/integer/
        boolean), enum, minimum/maximum, items, properties. Anything outside
        this subset is ignored — the structured-output modes on Gemini/Ollama
        already constrain the model, this guards against the cases they miss.
        """
        expected_type = schema.get("type")
        if expected_type == "object":
            if not isinstance(data, dict):
                raise SchemaValidationError(f"{path or '<root>'}: expected object, got {type(data).__name__}")
            for req in schema.get("required", []):
                if req not in data:
                    raise SchemaValidationError(f"{path or '<root>'}: missing required field '{req}'")
            for key, subschema in schema.get("properties", {}).items():
                if key in data:
                    BaseLLMClient._validate_against_schema(data[key], subschema, f"{path}.{key}" if path else key)
        elif expected_type == "array":
            if not isinstance(data, list):
                raise SchemaValidationError(f"{path}: expected array, got {type(data).__name__}")
            item_schema = schema.get("items")
            if item_schema:
                for i, item in enumerate(data):
                    BaseLLMClient._validate_against_schema(item, item_schema, f"{path}[{i}]")
        elif expected_type == "string":
            if not isinstance(data, str):
                raise SchemaValidationError(f"{path}: expected string, got {type(data).__name__}")
            if "enum" in schema and data not in schema["enum"]:
                raise SchemaValidationError(f"{path}: value '{data}' not in enum {schema['enum']}")
        elif expected_type in ("number", "integer"):
            if not isinstance(data, (int, float)) or isinstance(data, bool):
                raise SchemaValidationError(f"{path}: expected number, got {type(data).__name__}")
            if "minimum" in schema and data < schema["minimum"]:
                raise SchemaValidationError(f"{path}: value {data} below minimum {schema['minimum']}")
            if "maximum" in schema and data > schema["maximum"]:
                raise SchemaValidationError(f"{path}: value {data} above maximum {schema['maximum']}")
        elif expected_type == "boolean":
            if not isinstance(data, bool):
                raise SchemaValidationError(f"{path}: expected boolean, got {type(data).__name__}")
