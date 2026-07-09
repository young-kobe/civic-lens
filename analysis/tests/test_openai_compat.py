"""
Tests for the OpenAI-compatible LLM client.

These verify the backend-swap contract: any server speaking the OpenAI /v1
surface (LiteLLM, vLLM, a custom token router) must be reachable behind
get_llm_client() with zero engine changes. That means:
1. Proper role-separated messages + strict json_schema response_format on
   the wire (the interface a replacement router must accept).
2. Failures surface as the same exception types the engines already catch
   for heuristic fallback (RuntimeError/ValueError), never new ones.
3. embed() degrades to None so narrative clustering falls back to Jaccard
   instead of crashing the pipeline.
"""

import unittest
import os
import sys
from unittest.mock import patch, MagicMock

# Ensure project root is in path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

import requests

from analysis.src.llm.openai_compat import OpenAICompatClient


TEST_SCHEMA = {
    "type": "object",
    "properties": {
        "sentiment": {"type": "string", "enum": ["positive", "negative", "neutral"]},
        "confidence": {"type": "number"},
    },
    "required": ["sentiment", "confidence"],
}


def _chat_response(content, total_tokens=42):
    """Build a minimal OpenAI-shaped /v1/chat/completions response."""
    mock = MagicMock()
    mock.status_code = 200
    mock.json.return_value = {
        "choices": [{"message": {"content": content}}],
        "usage": {"total_tokens": total_tokens},
    }
    mock.raise_for_status.return_value = None
    return mock


def _ok_get(*args, **kwargs):
    mock = MagicMock()
    mock.status_code = 200
    return mock


class TestOpenAICompatClient(unittest.TestCase):
    """Behavioral contract of OpenAICompatClient against a mocked server."""

    def _client(self, **kwargs):
        defaults = dict(
            base_url="http://router.local:4000",
            api_key="test-key",
            model="test-model",
            embedding_model="test-embed",
            timeout=5,
            max_retries=1,
        )
        defaults.update(kwargs)
        with patch(
            "analysis.src.llm.openai_compat.requests.get", side_effect=_ok_get
        ):
            return OpenAICompatClient(**defaults)

    def test_unavailable_without_base_url(self):
        """An unset CIVIC_LLM_BASE_URL must read as unavailable so engines
        fall back to heuristics instead of hammering a dead endpoint."""
        client = OpenAICompatClient(base_url="")
        self.assertFalse(client.is_available)
        with self.assertRaises(RuntimeError):
            client.complete("sys", "user")

    def test_complete_sends_role_messages_and_strict_schema(self):
        """The wire format IS the swap interface: a future custom router only
        needs to accept role-separated messages + strict json_schema."""
        client = self._client()
        with patch(
            "analysis.src.llm.openai_compat.requests.post",
            return_value=_chat_response('{"sentiment": "neutral", "confidence": 0.9}'),
        ) as mock_post:
            result = client.complete("sys prompt", "user prompt", response_schema=TEST_SCHEMA)

        self.assertEqual(result["sentiment"], "neutral")
        args, kwargs = mock_post.call_args
        self.assertEqual(args[0], "http://router.local:4000/v1/chat/completions")
        payload = kwargs["json"]
        self.assertEqual(
            payload["messages"],
            [
                {"role": "system", "content": "sys prompt"},
                {"role": "user", "content": "user prompt"},
            ],
        )
        self.assertEqual(payload["response_format"]["type"], "json_schema")
        self.assertTrue(payload["response_format"]["json_schema"]["strict"])
        self.assertEqual(
            payload["response_format"]["json_schema"]["schema"], TEST_SCHEMA
        )
        self.assertEqual(
            kwargs["headers"]["Authorization"], "Bearer test-key"
        )

    def test_complete_without_schema_uses_json_object_mode(self):
        """Engines that pass no schema still need JSON back, not prose."""
        client = self._client()
        with patch(
            "analysis.src.llm.openai_compat.requests.post",
            return_value=_chat_response('{"ok": true}'),
        ) as mock_post:
            client.complete("sys", "user")
        payload = mock_post.call_args.kwargs["json"]
        self.assertEqual(payload["response_format"], {"type": "json_object"})

    def test_complete_accumulates_token_usage(self):
        """Token accounting feeds the cost visibility that motivated the
        router work — it must survive the backend swap."""
        client = self._client()
        with patch(
            "analysis.src.llm.openai_compat.requests.post",
            return_value=_chat_response('{"ok": true}', total_tokens=100),
        ):
            client.complete("sys", "user")
            client.complete("sys", "user")
        self.assertEqual(client.get_token_usage(), 200)

    def test_schema_violation_raises_engine_catchable_error(self):
        """A schema-invalid response must surface as the RuntimeError the
        engines already catch to trigger heuristic fallback."""
        client = self._client()
        with patch(
            "analysis.src.llm.openai_compat.requests.post",
            return_value=_chat_response('{"sentiment": "invalid-enum", "confidence": 0.9}'),
        ):
            with self.assertRaises(RuntimeError):
                client.complete("sys", "user", response_schema=TEST_SCHEMA)

    def test_http_failure_raises_engine_catchable_error(self):
        client = self._client()
        with patch(
            "analysis.src.llm.openai_compat.requests.post",
            side_effect=requests.exceptions.ConnectionError("boom"),
        ):
            with self.assertRaises(RuntimeError):
                client.complete("sys", "user")

    def test_embed_returns_vector(self):
        client = self._client()
        mock = MagicMock()
        mock.raise_for_status.return_value = None
        mock.json.return_value = {"data": [{"embedding": [0.1, 0.2, 0.3]}]}
        with patch(
            "analysis.src.llm.openai_compat.requests.post", return_value=mock
        ) as mock_post:
            vec = client.embed("some claim text")
        self.assertEqual(vec, [0.1, 0.2, 0.3])
        args, kwargs = mock_post.call_args
        self.assertEqual(args[0], "http://router.local:4000/v1/embeddings")
        self.assertEqual(kwargs["json"]["model"], "test-embed")

    def test_embed_returns_none_on_failure(self):
        """None (not an exception) is the embed failure contract — the
        narrative clusterer uses it to fall back to Jaccard per claim."""
        client = self._client()
        with patch(
            "analysis.src.llm.openai_compat.requests.post",
            side_effect=requests.exceptions.ConnectionError("boom"),
        ):
            self.assertIsNone(client.embed("some claim text"))
        self.assertIsNone(client.embed(""))


class TestFactoryDispatch(unittest.TestCase):
    """CIVIC_LLM_BACKEND=openai_compat must route through the shared factory
    — the single seam every engine uses."""

    def test_openai_compat_backend_selected(self):
        import analysis.src.llm.factory as factory

        fake_settings = MagicMock()
        fake_settings.llm_backend = "openai_compat"
        fake_settings.llm_base_url = "http://router.local:4000"
        fake_settings.llm_api_key = ""
        fake_settings.llm_model = "test-model"
        fake_settings.llm_embedding_model = ""
        fake_settings.llm_timeout = 5

        original = factory._openai_compat_client_instance
        factory._openai_compat_client_instance = None
        try:
            with patch(
                "analysis.src.common.settings.get_settings",
                return_value=fake_settings,
            ), patch(
                "analysis.src.llm.openai_compat.requests.get", side_effect=_ok_get
            ):
                client = factory.get_llm_client()
                self.assertIsInstance(client, OpenAICompatClient)
                # Singleton: a second call returns the same instance.
                self.assertIs(factory.get_llm_client(), client)
        finally:
            factory._openai_compat_client_instance = original


if __name__ == "__main__":
    unittest.main()
