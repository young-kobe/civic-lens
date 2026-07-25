"""
Tests for analysis/src/engine/text.py -- Postgres redesign Phase 6.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_result_store.py, test_engine_citations.py):

  1. Pure-core tests (no DB) -- analyze() against a fake LLMClient transport.
     Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001_north_star.sql applied. Skipped
     (never failed) when the env var is absent.

Sentiment-only as of 2026-07-25: the favorability half (entity_stances,
overall_gop_stance, analysis.favorability_stances) is gone -- see
docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md. `analyze()`/
`process()` no longer take an `EntityResolver` (nothing in this module
resolves an entity anymore).
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine import text
from analysis.src.engine.constants import UNVERIFIED_EVIDENCE_CONFIDENCE_CAP
from analysis.src.llm.base import SchemaValidationError
from analysis.src.llm.client import LLMClient
from analysis.tests import pg_fixture


# =============================================================================
# Fakes shared by Tier 1 tests below.
# =============================================================================

class FakeTransport:
    """Stands in for a backend's complete_once()/is_available/
    get_token_usage() surface (same shape as test_llm_client.py's fixture).
    Each call pops the next scripted outcome -- an exception to raise, or a
    dict to return."""

    def __init__(self, outcomes, available=True):
        self.is_available = available
        self._outcomes = list(outcomes)
        self.calls = 0

    def complete_once(self, system_prompt, user_prompt, response_schema=None, temperature=None):
        self.calls += 1
        outcome = self._outcomes.pop(0)
        if isinstance(outcome, Exception):
            raise outcome
        return outcome

    def get_token_usage(self) -> int:
        return 0


def _valid_response(**overrides) -> dict:
    base = {
        "sentiment_label": "NEGATIVE",
        "sentiment_confidence": 0.8,
        "sentiment_evidence_spans": ["this is a bad policy decision"],
        "sarcasm_detected": False,
        "sentiment_reasoning": "test sentiment reasoning",
    }
    base.update(overrides)
    return base


_SAMPLE_TEXT = (
    "Reporters said this is a bad policy decision overall. "
    "Meanwhile, allies noted that Trump praised the new policy strongly."
)


def _doc(doc_id=1, text_value=_SAMPLE_TEXT) -> "text.TextDocInput":
    return text.TextDocInput(doc_id=doc_id, source_type="news", title="A headline", text=text_value)


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class AnalyzeValidResponseTests(unittest.TestCase):
    def test_valid_response_maps_sentiment(self):
        client = LLMClient(FakeTransport([_valid_response()]))

        result = text.analyze(_doc(), client)

        self.assertEqual(result.inference_method, "llm")
        self.assertEqual(result.sentiment.label, "negative")
        self.assertEqual(result.sentiment.confidence, 0.8)
        self.assertEqual(result.sentiment.evidence_spans, ["this is a bad policy decision"])
        self.assertFalse(result.sentiment.sarcasm_detected)
        self.assertIsNotNone(result.raw_response)

    def test_sentiment_label_mapping_covers_all_four_values(self):
        for raw_label, expected in [
            ("POSITIVE", "positive"), ("NEGATIVE", "negative"),
            ("NEUTRAL", "neutral"), ("MIXED", "mixed"),
        ]:
            client = LLMClient(FakeTransport([_valid_response(sentiment_label=raw_label)]))
            result = text.analyze(_doc(), client)
            self.assertEqual(result.sentiment.label, expected)


class AnalyzeSchemaRetryTests(unittest.TestCase):
    def test_schema_invalid_response_retries_then_succeeds(self):
        backend = FakeTransport([SchemaValidationError("bad enum"), _valid_response()])
        client = LLMClient(backend, max_retries=3)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            result = text.analyze(_doc(), client)

        self.assertEqual(backend.calls, 2)
        self.assertEqual(result.sentiment.label, "negative")


class AnalyzeTrivialContentTests(unittest.TestCase):
    """Owner decision 2026-07-23: unanalyzable is not neutral -- the
    trivial-content short-circuit yields no sentiment at all, not a
    placeholder neutral/low-confidence guess."""

    def test_trivial_content_skips_the_llm_call_and_yields_no_sentiment(self):
        backend = FakeTransport([_valid_response()])
        client = LLMClient(backend)
        trivial_doc = text.TextDocInput(
            doc_id=1, source_type="x_post", title=None,
            text="@someone #politics https://example.com",
        )

        result = text.analyze(trivial_doc, client)

        self.assertEqual(backend.calls, 0)
        self.assertEqual(result.inference_method, "deterministic")
        self.assertIsNone(result.sentiment)
        self.assertIsNone(result.raw_response)


class AnalyzeEvidenceValidationTests(unittest.TestCase):
    def test_fabricated_sentiment_evidence_is_dropped_and_confidence_capped(self):
        client = LLMClient(FakeTransport([_valid_response(
            sentiment_evidence_spans=["this phrase is not in the source text"],
        )]))

        result = text.analyze(_doc(), client)

        self.assertEqual(result.sentiment.evidence_spans, [])
        self.assertEqual(result.sentiment.confidence, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

    def test_valid_verbatim_evidence_is_kept_uncapped(self):
        client = LLMClient(FakeTransport([_valid_response()]))

        result = text.analyze(_doc(), client)

        self.assertEqual(result.sentiment.evidence_spans, ["this is a bad policy decision"])
        self.assertEqual(result.sentiment.confidence, 0.8)


class AnalyzeLlmFailureTests(unittest.TestCase):
    """The deliberate behavior change: no heuristic fallback -- a failed or
    unavailable LLM call must raise out of analyze(), not silently degrade
    to a lower-quality deterministic guess."""

    def test_unavailable_backend_raises(self):
        client = LLMClient(FakeTransport([_valid_response()], available=False))

        with self.assertRaises(RuntimeError):
            text.analyze(_doc(), client)

    def test_exhausted_retries_raises(self):
        client = LLMClient(FakeTransport([RuntimeError("e1"), RuntimeError("e2")]), max_retries=2)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError):
                text.analyze(_doc(), client)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ProcessIntegrationTests(unittest.TestCase):
    """Live-run against a real Postgres with 0001 applied."""

    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_all()
        self.doc_id = self._seed_doc("doc-1")

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.sentiment_results, "
                "analysis.runs, analysis.prompt_versions, corpus.documents, "
                "corpus.entity_aliases, corpus.entities CASCADE"
            )

    def _seed_doc(self, natural_key: str) -> int:
        from analysis.src.results import store
        with store.db.connection() as conn:
            row = conn.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, published_at, body, source_url,
                     raw_hash, etl_version)
                VALUES ('news', %s, now(), 'test body', 'http://example.com/' || %s,
                        'deadbeef', 'test')
                RETURNING doc_id
                """,
                (natural_key, natural_key),
            ).fetchone()
            return row["doc_id"]

    def _run_row(self, run_id):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.runs WHERE run_id = %s", (run_id,)
            ).fetchone()

    def test_valid_llm_response_persists_run_and_result_rows(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        run_id = text.process(_doc(self.doc_id), client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertTrue(run["is_current"])
        self.assertEqual(run["inference_method"], "llm")
        self.assertIsNotNone(run["prompt_version_id"])
        self.assertEqual(run["model_id"], text._resolve_model_id())

        from analysis.src.results import store
        with store.db.connection() as conn:
            sentiment = conn.execute(
                "SELECT * FROM analysis.sentiment_results WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertEqual(sentiment["label"], "negative")

    def test_trivial_content_process_is_deterministic_with_no_sentiment_row(self):
        """Owner decision 2026-07-23: the trivial-content run lands 'done'
        with zero result rows -- no sentiment_results, no prompt_version
        (deterministic, no LLM call made), confidence None (nothing was
        measured)."""
        client = LLMClient(FakeTransport([_valid_response()]))
        trivial_doc = text.TextDocInput(
            doc_id=self.doc_id, source_type="x_post", title=None,
            text="@someone #politics https://example.com",
        )
        run_id = text.process(trivial_doc, client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertTrue(run["is_current"])
        self.assertEqual(run["inference_method"], "deterministic")
        self.assertIsNone(run["prompt_version_id"])
        self.assertIsNone(run["confidence"])
        self.assertIsNone(run["raw_response"])

        from analysis.src.results import store
        with store.db.connection() as conn:
            sentiment = conn.execute(
                "SELECT * FROM analysis.sentiment_results WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertIsNone(sentiment)

    def test_reprocess_supersedes_prior_run(self):
        first_client = LLMClient(FakeTransport([_valid_response()]))
        first_run = text.process(_doc(self.doc_id), first_client).run_id

        second_client = LLMClient(FakeTransport([_valid_response(sentiment_label="POSITIVE")]))
        second_run = text.process(_doc(self.doc_id), second_client).run_id

        self.assertNotEqual(first_run, second_run)
        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = {
                r["run_id"]: r["is_current"]
                for r in conn.execute(
                    "SELECT run_id, is_current FROM analysis.runs "
                    "WHERE task = 'text'::analysis.task AND doc_id = %s", (self.doc_id,)
                ).fetchall()
            }
        self.assertFalse(rows[first_run])
        self.assertTrue(rows[second_run])

    def test_failed_llm_call_records_failed_run_with_error(self):
        client = LLMClient(FakeTransport([_valid_response()], available=False))
        run_id = text.process(_doc(self.doc_id), client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "failed")
        self.assertFalse(run["is_current"])
        self.assertIsNotNone(run["error"])
        self.assertIsNone(run["raw_response"])

        from analysis.src.results import store
        with store.db.connection() as conn:
            sentiment = conn.execute(
                "SELECT * FROM analysis.sentiment_results WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertIsNone(sentiment)


if __name__ == "__main__":
    unittest.main()
