"""
GeminiClient (google-genai SDK) tests.

The client wraps the google-genai SDK — the replacement for the deprecated
google-generativeai package. Contracts worth pinning:

  - The system prompt travels as a real ``system_instruction`` role
    (matching how prompt_versions stores system and user templates
    separately — invariant B1), and the permissive safety settings +
    response schema ride the generation config. A regression here silently
    changes what the model sees or lets default safety thresholds refuse
    the political content this system exists to analyze.
  - Transient API failures retry with backoff, then surface as
    RuntimeError so engines can fall back to heuristics.
  - A missing SDK degrades to is_available=False instead of crashing the
    pipeline at import time.

The SDK is mocked via sys.modules — these tests must run without
google-genai installed.
"""

import os
import sys
import unittest
from unittest.mock import MagicMock, patch

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.llm.gemini import GeminiClient, _PERMISSIVE_SAFETY_SETTINGS

_SCHEMA = {
    "type": "object",
    "properties": {"label": {"type": "string"}},
    "required": ["label"],
}


def _response(text='{"label": "NEUTRAL"}', tokens=42):
    resp = MagicMock()
    resp.text = text
    resp.usage_metadata.total_token_count = tokens
    return resp


def _fake_sdk(generate_side_effect):
    """Module mocks for `from google import genai` / `from google.genai
    import types`. Config/SafetySetting record their kwargs as plain dicts
    so tests can assert on exactly what would reach the API."""
    types_mod = MagicMock()
    types_mod.GenerateContentConfig = MagicMock(side_effect=lambda **kw: kw)
    types_mod.SafetySetting = MagicMock(side_effect=lambda **kw: kw)

    api_client = MagicMock()
    api_client.models.generate_content = MagicMock(side_effect=generate_side_effect)

    genai_mod = MagicMock()
    genai_mod.Client = MagicMock(return_value=api_client)
    genai_mod.types = types_mod

    google_mod = MagicMock()
    google_mod.genai = genai_mod
    return {
        "google": google_mod,
        "google.genai": genai_mod,
    }, api_client


class TestGeminiClient(unittest.TestCase):
    def test_system_role_schema_and_safety_reach_the_config(self):
        modules, api_client = _fake_sdk([_response()])
        with patch.dict(sys.modules, modules):
            client = GeminiClient(api_key="k", model="gemini-3.5-flash")
            result = client.complete("SYS PROMPT", "USER PROMPT", response_schema=_SCHEMA)

        self.assertEqual(result, {"label": "NEUTRAL"})
        call = api_client.models.generate_content.call_args
        self.assertEqual(call.kwargs["model"], "gemini-3.5-flash")
        # User prompt is the contents; system prompt is a REAL system role,
        # not concatenated into the user turn like the old SDK wrapper did.
        self.assertEqual(call.kwargs["contents"], "USER PROMPT")
        config = call.kwargs["config"]
        self.assertEqual(config["system_instruction"], "SYS PROMPT")
        self.assertEqual(config["response_mime_type"], "application/json")
        self.assertIs(config["response_schema"], _SCHEMA)
        # All four categories explicitly BLOCK_NONE — defaults refuse the
        # political content this pipeline exists to analyze.
        self.assertEqual(config["safety_settings"], _PERMISSIVE_SAFETY_SETTINGS)
        self.assertEqual(client.total_tokens_used, 42)

    def test_transient_failure_retries_then_succeeds(self):
        modules, api_client = _fake_sdk([RuntimeError("503"), _response(tokens=7)])
        with patch.dict(sys.modules, modules), patch("time.sleep") as sleep:
            client = GeminiClient(api_key="k")
            result = client.complete("s", "u", response_schema=_SCHEMA)
        self.assertEqual(result, {"label": "NEUTRAL"})
        self.assertEqual(api_client.models.generate_content.call_count, 2)
        sleep.assert_called_once()

    def test_exhausted_retries_raise_runtime_error(self):
        modules, api_client = _fake_sdk(RuntimeError("boom"))
        with patch.dict(sys.modules, modules), patch("time.sleep"):
            client = GeminiClient(api_key="k", max_retries=2)
            with self.assertRaises(RuntimeError):
                client.complete("s", "u")
        self.assertEqual(api_client.models.generate_content.call_count, 2)

    def test_missing_sdk_degrades_to_unavailable(self):
        # sys.modules[name] = None makes `import name` raise ImportError.
        with patch.dict(sys.modules, {"google": None, "google.genai": None}):
            client = GeminiClient(api_key="k")
        self.assertFalse(client.is_available)
        with self.assertRaises(RuntimeError):
            client.complete("s", "u")

    def test_no_api_key_is_unavailable_without_importing_sdk(self):
        client = GeminiClient(api_key="")
        self.assertFalse(client.is_available)


if __name__ == "__main__":
    unittest.main()
