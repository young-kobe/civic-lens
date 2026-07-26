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

    def test_quota_exhaustion_error_is_not_retried(self):
        # Simulates google-genai's APIError shape: a `.code` attribute
        # (gemini.py's live path) carrying 429, with a billing-flavored
        # message -- the exact failure mode that burned through the
        # owner's Gemini prepayment credits by retrying 3x per doc. This
        # test exists to stop a future refactor from silently restoring
        # blanket retry on billing errors.
        class FakeQuotaError(Exception):
            code = 429

            def __init__(self):
                super().__init__(
                    "429 RESOURCE_EXHAUSTED. You exceeded your current "
                    "quota, please check your plan and billing details."
                )

        backend = FakeTransport([FakeQuotaError(), {"label": "unreachable"}])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep") as sleep:
            with self.assertRaises(RuntimeError) as ctx:
                client.complete("sys", "user")

        self.assertIn("quota", str(ctx.exception).lower())
        self.assertEqual(backend.calls, 1)
        sleep.assert_not_called()

    def test_auth_error_is_not_retried(self):
        # Simulates requests.exceptions.HTTPError's shape (Ollama/
        # OpenAICompat's response.raise_for_status()): a `.response`
        # object exposing `.status_code`. A bad/expired API key cannot be
        # fixed by retrying the same request.
        class FakeResponse:
            status_code = 401

        class FakeAuthError(Exception):
            def __init__(self):
                self.response = FakeResponse()
                super().__init__("401 Client Error: Unauthorized")

        backend = FakeTransport([FakeAuthError(), {"label": "unreachable"}])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep") as sleep:
            with self.assertRaises(RuntimeError) as ctx:
                client.complete("sys", "user")

        self.assertIn("authentication", str(ctx.exception).lower())
        self.assertEqual(backend.calls, 1)
        sleep.assert_not_called()

    def test_malformed_request_error_is_not_retried(self):
        class FakeMalformedRequestError(Exception):
            code = 400

        backend = FakeTransport([FakeMalformedRequestError(), {"label": "unreachable"}])
        client = LLMClient(backend, max_retries=3)

        with self.assertRaises(RuntimeError) as ctx:
            client.complete("sys", "user")

        self.assertIn("malformed", str(ctx.exception).lower())
        self.assertEqual(backend.calls, 1)

    def test_generic_rate_limit_429_without_quota_wording_still_retries(self):
        # A 429 alone is ambiguous (Gemini uses it for both a transient
        # rate limit and true quota exhaustion); absent quota/billing/
        # credit wording in the message, it must retry like any other
        # transient failure.
        class FakeRateLimitError(Exception):
            code = 429

            def __init__(self):
                super().__init__("429 RESOURCE_EXHAUSTED. Too many requests, slow down.")

        backend = FakeTransport([FakeRateLimitError(), {"label": "ok"}])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep") as sleep:
            result = client.complete("sys", "user")

        self.assertEqual(result["label"], "ok")
        self.assertEqual(backend.calls, 2)
        sleep.assert_called_once()

    def test_transient_error_without_status_code_still_retries_to_limit(self):
        # Plain network blips (e.g. a bare ConnectionError/timeout with no
        # status code attribute at all) carry no non-retryable signal and
        # must still exhaust the configured retry budget, same as before
        # this change.
        backend = FakeTransport([TimeoutError("timed out"), TimeoutError("timed out"), TimeoutError("timed out")])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError) as ctx:
                client.complete("sys", "user")

        self.assertIn("3 retries", str(ctx.exception))
        self.assertEqual(backend.calls, 3)

    def test_server_error_5xx_still_retries(self):
        class FakeServerError(Exception):
            code = 503

        backend = FakeTransport([FakeServerError(), {"label": "ok"}])
        client = LLMClient(backend, max_retries=3)

        with patch("analysis.src.llm.client.time.sleep"):
            result = client.complete("sys", "user")

        self.assertEqual(result["label"], "ok")
        self.assertEqual(backend.calls, 2)

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


class FakeEmbeddingTransport(FakeTransport):
    """A FakeTransport that overrides embed() with a real implementation --
    the "backend supports embeddings" side of the fixture pair."""

    def embed(self, text, model=None):
        return [len(text), 0.0] if text else None


class TestLLMClientEmbed(unittest.TestCase):
    """LLMClient.embed()/supports_embedding must tell a real embedder
    (overrides the no-op) apart from one that doesn't -- a plain hasattr
    check can't, since every backend "has" embed via BaseLLMClient's
    default. FakeTransport (no embed override) stands in for Gemini;
    FakeEmbeddingTransport stands in for Ollama/OpenAICompat."""

    def test_backend_without_embed_support_is_detected(self):
        client = LLMClient(FakeTransport([]))
        self.assertFalse(client.supports_embedding)

    def test_embed_raises_naming_the_backend_when_unsupported(self):
        client = LLMClient(FakeTransport([]))
        with self.assertRaises(RuntimeError) as ctx:
            client.embed("some text")
        self.assertIn("FakeTransport", str(ctx.exception))

    def test_backend_with_embed_support_is_detected(self):
        client = LLMClient(FakeEmbeddingTransport([]))
        self.assertTrue(client.supports_embedding)

    def test_embed_passes_through_to_a_supporting_backend(self):
        client = LLMClient(FakeEmbeddingTransport([]))
        result = client.embed("hello")
        self.assertEqual(result, [5, 0.0])

    def test_embed_passes_through_model_argument(self):
        calls = []

        class RecordingEmbedTransport(FakeTransport):
            def embed(self, text, model=None):
                calls.append((text, model))
                return [1.0]

        client = LLMClient(RecordingEmbedTransport([]))
        client.embed("text", model="a-model")
        self.assertEqual(calls, [("text", "a-model")])


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
