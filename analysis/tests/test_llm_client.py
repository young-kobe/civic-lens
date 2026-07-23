"""
Tests for the unified LLM client (analysis/src/llm/client.py).

A fake transport backend stands in for Gemini/Ollama/OpenAICompatClient so
these tests pin LLMClient's own behavior — retry/backoff, schema-invalid
handling, token accounting, singleton lifecycle — independent of any real
backend's request/parse logic (those stay covered by
test_gemini_client.py / test_openai_compat.py).
"""

import os
import sys
import unittest
from unittest.mock import patch

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

import analysis.src.llm.client as client_module
from analysis.src.llm.base import SchemaValidationError
from analysis.src.llm.client import LLMClient


class FakeTransport:
    """Stands in for a backend's complete_once()/is_available/
    get_token_usage() surface. Each call pops the next scripted outcome —
    an exception to raise, or a dict to return (with a fixed token cost)."""

    def __init__(self, outcomes, available=True):
        self.is_available = available
        self._outcomes = list(outcomes)
        self.calls = 0
        self._tokens = 0

    def complete_once(self, system_prompt, user_prompt, response_schema=None, temperature=None):
        self.calls += 1
        outcome = self._outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        self._tokens += 10
        return outcome

    def get_token_usage(self) -> int:
        return self._tokens


class TestLLMClientRetry(unittest.TestCase):
    def test_retry_then_succeed(self):
        backend = FakeTransport([RuntimeError("transient"), {"label": "ok"}])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep") as sleep:
            result = client.complete("sys", "user")

        self.assertEqual(result["label"], "ok")
        self.assertEqual(backend.calls, 2)
        sleep.assert_called_once()

    def test_exhausted_retries_raises_with_attempt_count(self):
        backend = FakeTransport([RuntimeError("e1"), RuntimeError("e2")])
        client = LLMClient(backend, max_retries=2)

        with patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError) as ctx:
                client.complete("sys", "user")

        self.assertIn("2 retries", str(ctx.exception))
        self.assertEqual(backend.calls, 2)

    def test_schema_invalid_response_retries_like_transport_error(self):
        # SchemaValidationError is what complete_once()'s internal
        # parse_json_response raises for an out-of-schema response; the
        # client's retry loop must treat it the same as any other failure.
        backend = FakeTransport([SchemaValidationError("bad enum"), {"confidence": 0.9}])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep"):
            result = client.complete("sys", "user", response_schema={"type": "object"})

        self.assertEqual(backend.calls, 2)
        self.assertEqual(result["confidence"], 0.9)

    def test_no_sleep_after_final_attempt(self):
        backend = FakeTransport([RuntimeError("e1"), RuntimeError("e2"), RuntimeError("e3")])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep") as sleep:
            with self.assertRaises(RuntimeError):
                client.complete("sys", "user")

        # Attempts 0 and 1 back off; attempt 2 (the final one) does not.
        self.assertEqual(sleep.call_count, 2)

    def test_unavailable_backend_raises_without_calling_transport(self):
        backend = FakeTransport([{"ok": True}], available=False)
        client = LLMClient(backend)

        with self.assertRaises(RuntimeError):
            client.complete("sys", "user")
        self.assertEqual(backend.calls, 0)


class TestLLMClientConfidenceCoercion(unittest.TestCase):
    def test_coerces_confidence_fields_recursively_on_the_way_out(self):
        backend = FakeTransport([{
            "confidence": 87,
            "sentiment_confidence": 1.5,
            "entity_stances": [{"entity": "x", "confidence": 42}],
            "label": "NEUTRAL",
        }])
        client = LLMClient(backend)

        result = client.complete("sys", "user")

        self.assertEqual(result["confidence"], 0.87)
        self.assertEqual(result["sentiment_confidence"], 1.0)
        self.assertEqual(result["entity_stances"][0]["confidence"], 0.42)
        self.assertEqual(result["label"], "NEUTRAL")


class TestLLMClientTokenAccounting(unittest.TestCase):
    def test_token_accounting_normalized_across_calls(self):
        backend = FakeTransport([{"ok": True}, {"ok": True}])
        client = LLMClient(backend)

        client.complete("sys", "user")
        client.complete("sys", "user")

        self.assertEqual(client.total_tokens_used, 20)
        self.assertEqual(client.get_token_usage(), 20)


class TestGetClientSingleton(unittest.TestCase):
    def tearDown(self):
        client_module.reset_client()

    def test_get_client_returns_singleton_until_reset(self):
        fake_backend = FakeTransport([{"ok": True}])

        with patch("analysis.src.llm.factory.get_llm_client", return_value=fake_backend):
            client_module.reset_client()
            first = client_module.get_client()
            second = client_module.get_client()

        self.assertIs(first, second)

        client_module.reset_client()
        with patch("analysis.src.llm.factory.get_llm_client", return_value=FakeTransport([{"ok": True}])):
            third = client_module.get_client()

        self.assertIsNot(first, third)


if __name__ == "__main__":
    unittest.main()
