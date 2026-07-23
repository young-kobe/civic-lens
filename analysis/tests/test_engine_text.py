"""
Tests for analysis/src/engine/text.py -- Postgres redesign Phase 6.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_result_store.py, test_engine_citations.py):

  1. Pure-core tests (no DB) -- analyze() against a fake LLMClient transport
     and an EntityResolver built from a fake connection. Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001_north_star.sql applied. Skipped
     (never failed) when the env var is absent.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.common.entity_resolver import EntityResolver
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


class _FakeResult:
    def __init__(self, rows):
        self._rows = rows

    def fetchall(self):
        return self._rows


class _FakeEntityConn:
    """Backs an EntityResolver with a single active entity, no aliases --
    enough for the resolution/drop-counting tests below."""

    def __init__(self, entity_rows):
        self._entity_rows = entity_rows

    def execute(self, query, params=None):
        if "entity_aliases" in query:
            return _FakeResult([])
        return _FakeResult(self._entity_rows)


def _resolver_with_entity(entity_id, entity_key, display_name) -> EntityResolver:
    conn = _FakeEntityConn([
        {"entity_id": entity_id, "entity_key": entity_key, "display_name": display_name, "active": True}
    ])
    return EntityResolver(conn)


def _valid_response(**overrides) -> dict:
    base = {
        "sentiment_label": "NEGATIVE",
        "sentiment_confidence": 0.8,
        "sentiment_evidence_spans": ["this is a bad policy decision"],
        "sarcasm_detected": False,
        "entity_stances": [
            {
                "entity": "Donald Trump",
                "stance": "favorable",
                "confidence": 0.75,
                "evidence_spans": ["praised the new policy strongly"],
            }
        ],
        "overall_gop_stance": "favorable",
        "overall_favorability_confidence": 0.7,
        "sentiment_reasoning": "test sentiment reasoning",
        "favorability_reasoning": "test favorability reasoning",
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
    def test_valid_response_maps_sentiment_and_resolved_favorability(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = text.analyze(_doc(), client, resolver)

        self.assertEqual(result.inference_method, "llm")
        self.assertEqual(result.sentiment.label, "negative")
        self.assertEqual(result.sentiment.confidence, 0.8)
        self.assertEqual(result.sentiment.evidence_spans, ["this is a bad policy decision"])
        self.assertFalse(result.sentiment.sarcasm_detected)
        self.assertEqual(len(result.favorability_stances), 1)
        stance = result.favorability_stances[0]
        self.assertEqual(stance.entity_id, 42)
        self.assertEqual(stance.stance, "favorable")
        self.assertEqual(stance.confidence, 0.75)
        self.assertEqual(result.dropped_unresolved, 0)
        self.assertIsNotNone(result.raw_response)

    def test_sentiment_label_mapping_covers_all_four_values(self):
        resolver = _resolver_with_entity(1, "x", "X")
        for raw_label, expected in [
            ("POSITIVE", "positive"), ("NEGATIVE", "negative"),
            ("NEUTRAL", "neutral"), ("MIXED", "mixed"),
        ]:
            client = LLMClient(FakeTransport([_valid_response(
                sentiment_label=raw_label, entity_stances=[],
            )]))
            result = text.analyze(_doc(), client, resolver)
            self.assertEqual(result.sentiment.label, expected)


class AnalyzeSchemaRetryTests(unittest.TestCase):
    def test_schema_invalid_response_retries_then_succeeds(self):
        backend = FakeTransport([SchemaValidationError("bad enum"), _valid_response()])
        client = LLMClient(backend, max_retries=3)
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            result = text.analyze(_doc(), client, resolver)

        self.assertEqual(backend.calls, 2)
        self.assertEqual(result.sentiment.label, "negative")


class AnalyzeTrivialContentTests(unittest.TestCase):
    """Owner decision 2026-07-23: unanalyzable is not neutral -- the
    trivial-content short-circuit yields no sentiment at all, not a
    placeholder neutral/low-confidence guess."""

    def test_trivial_content_skips_the_llm_call_and_yields_no_sentiment(self):
        backend = FakeTransport([_valid_response()])
        client = LLMClient(backend)
        resolver = _resolver_with_entity(1, "x", "X")
        trivial_doc = text.TextDocInput(
            doc_id=1, source_type="x_post", title=None,
            text="@someone #politics https://example.com",
        )

        result = text.analyze(trivial_doc, client, resolver)

        self.assertEqual(backend.calls, 0)
        self.assertEqual(result.inference_method, "deterministic")
        self.assertIsNone(result.sentiment)
        self.assertEqual(result.favorability_stances, [])
        self.assertEqual(result.dropped_unresolved, 0)
        self.assertIsNone(result.raw_response)


class AnalyzeEvidenceValidationTests(unittest.TestCase):
    def test_fabricated_sentiment_evidence_is_dropped_and_confidence_capped(self):
        client = LLMClient(FakeTransport([_valid_response(
            sentiment_evidence_spans=["this phrase is not in the source text"],
            entity_stances=[],
        )]))
        resolver = _resolver_with_entity(1, "x", "X")

        result = text.analyze(_doc(), client, resolver)

        self.assertEqual(result.sentiment.evidence_spans, [])
        self.assertEqual(result.sentiment.confidence, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

    def test_fabricated_favorability_evidence_is_dropped_and_confidence_capped(self):
        client = LLMClient(FakeTransport([_valid_response(
            entity_stances=[{
                "entity": "Donald Trump", "stance": "favorable", "confidence": 0.9,
                "evidence_spans": ["a phrase never spoken in this text"],
            }],
        )]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = text.analyze(_doc(), client, resolver)

        self.assertEqual(len(result.favorability_stances), 1)
        stance = result.favorability_stances[0]
        self.assertEqual(stance.evidence_spans, [])
        self.assertEqual(stance.confidence, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

    def test_valid_verbatim_evidence_is_kept_uncapped(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = text.analyze(_doc(), client, resolver)

        self.assertEqual(result.sentiment.evidence_spans, ["this is a bad policy decision"])
        self.assertEqual(result.sentiment.confidence, 0.8)


class AnalyzeUnresolvedEntityTests(unittest.TestCase):
    def test_unresolved_entity_dropped_and_counted(self):
        client = LLMClient(FakeTransport([_valid_response(entity_stances=[
            {"entity": "Someone Unregistered", "stance": "favorable", "confidence": 0.6,
             "evidence_spans": ["praised the new policy strongly"]},
        ])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = text.analyze(_doc(), client, resolver)

        self.assertEqual(result.favorability_stances, [])
        self.assertEqual(result.dropped_unresolved, 1)

    def test_mixed_resolved_and_unresolved_entities(self):
        client = LLMClient(FakeTransport([_valid_response(entity_stances=[
            {"entity": "Donald Trump", "stance": "favorable", "confidence": 0.75,
             "evidence_spans": ["praised the new policy strongly"]},
            {"entity": "Someone Unregistered", "stance": "unfavorable", "confidence": 0.6,
             "evidence_spans": ["also criticized someone unregistered here"]},
        ])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = text.analyze(_doc(), client, resolver)

        self.assertEqual(len(result.favorability_stances), 1)
        self.assertEqual(result.favorability_stances[0].entity_id, 42)
        self.assertEqual(result.dropped_unresolved, 1)


class AnalyzeLlmFailureTests(unittest.TestCase):
    """The deliberate behavior change: no heuristic fallback -- a failed or
    unavailable LLM call must raise out of analyze(), not silently degrade
    to a lower-quality deterministic guess."""

    def test_unavailable_backend_raises(self):
        client = LLMClient(FakeTransport([_valid_response()], available=False))
        resolver = _resolver_with_entity(1, "x", "X")

        with self.assertRaises(RuntimeError):
            text.analyze(_doc(), client, resolver)

    def test_exhausted_retries_raises(self):
        client = LLMClient(FakeTransport([RuntimeError("e1"), RuntimeError("e2")]), max_retries=2)
        resolver = _resolver_with_entity(1, "x", "X")

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError):
                text.analyze(_doc(), client, resolver)


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
        self.entity_id = self._seed_entity("trump", "Donald Trump")
        from analysis.src.common.entity_resolver import EntityResolver
        self.resolver = EntityResolver()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.favorability_stances, analysis.sentiment_results, "
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

    def _seed_entity(self, entity_key: str, display_name: str) -> int:
        from analysis.src.results import store
        with store.db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name) "
                "VALUES (%s, 'official', %s) RETURNING entity_id",
                (entity_key, display_name),
            ).fetchone()
            return row["entity_id"]

    def _run_row(self, run_id):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.runs WHERE run_id = %s", (run_id,)
            ).fetchone()

    def test_valid_llm_response_persists_run_and_result_rows(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        run_id = text.process(_doc(self.doc_id), client, self.resolver).run_id

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
            stances = conn.execute(
                "SELECT * FROM analysis.favorability_stances WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(sentiment["label"], "negative")
        self.assertEqual(len(stances), 1)
        self.assertEqual(stances[0]["entity_id"], self.entity_id)
        self.assertEqual(stances[0]["stance"], "favorable")

    def test_trivial_content_process_is_deterministic_with_no_sentiment_row(self):
        """Owner decision 2026-07-23: the trivial-content run lands 'done'
        with zero result rows -- no sentiment_results, no
        favorability_stances, no prompt_version (deterministic, no LLM call
        made), confidence None (nothing was measured)."""
        client = LLMClient(FakeTransport([_valid_response()]))
        trivial_doc = text.TextDocInput(
            doc_id=self.doc_id, source_type="x_post", title=None,
            text="@someone #politics https://example.com",
        )
        run_id = text.process(trivial_doc, client, self.resolver).run_id

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
            stances = conn.execute(
                "SELECT * FROM analysis.favorability_stances WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertIsNone(sentiment)
        self.assertEqual(stances, [])

    def test_reprocess_supersedes_prior_run(self):
        first_client = LLMClient(FakeTransport([_valid_response()]))
        first_run = text.process(_doc(self.doc_id), first_client, self.resolver).run_id

        second_client = LLMClient(FakeTransport([_valid_response(sentiment_label="POSITIVE")]))
        second_run = text.process(_doc(self.doc_id), second_client, self.resolver).run_id

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
        run_id = text.process(_doc(self.doc_id), client, self.resolver).run_id

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
