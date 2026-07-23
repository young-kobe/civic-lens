"""
Tests for analysis/src/engine/propaganda.py -- Postgres redesign Phase 6.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_engine_text.py, test_result_store.py):

  1. Pure-core tests (no DB) -- analyze() against a fake LLMClient transport.
     Always run.
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

from analysis.src.engine import propaganda
from analysis.src.engine.constants import UNVERIFIED_EVIDENCE_CONFIDENCE_CAP
from analysis.src.llm.base import SchemaValidationError
from analysis.src.llm.client import LLMClient
from analysis.tests import pg_fixture


# =============================================================================
# Fakes shared by Tier 1 tests below.
# =============================================================================

class FakeTransport:
    """Stands in for a backend's complete_once()/is_available/
    get_token_usage() surface (same shape as test_engine_text.py's fixture).
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


# Loaded-language sample: "corrupt" and "radical" are both in NEGATIVE_WORDS,
# so this text clears the pre-filter and the LLM path is exercised.
_LOADED_TEXT = (
    "The radical mob attacked innocent citizens while corrupt politicians "
    "remained silent about the whole affair."
)

# No loaded-language token anywhere -- clears neither NEGATIVE_WORDS nor
# INTENSIFIERS, so the pre-filter should short-circuit before any LLM call.
_NEUTRAL_TEXT = (
    "The committee met on Tuesday to review the proposed infrastructure "
    "funding schedule for the upcoming fiscal year."
)


def _valid_response(**overrides) -> dict:
    base = {
        "techniques": [
            {
                "technique": "name_calling",
                "confidence": 0.8,
                "evidence_span": "corrupt politicians remained silent",
            }
        ],
        "overall_propaganda_score": 0.75,
        "reasoning": "test reasoning",
    }
    base.update(overrides)
    return base


def _doc(doc_id=1, text_value=_LOADED_TEXT, title=None) -> "propaganda.PropagandaDocInput":
    return propaganda.PropagandaDocInput(
        doc_id=doc_id, source_type="news", title=title, text=text_value,
    )


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TechniqueVocabularyMappingTests(unittest.TestCase):
    def test_out_of_enum_technique_is_dropped(self):
        client = LLMClient(FakeTransport([_valid_response(techniques=[
            {"technique": "flag_waving", "confidence": 0.9,
             "evidence_span": "waving the flag of patriotism proudly"},
            {"technique": "name_calling", "confidence": 0.8,
             "evidence_span": "corrupt politicians remained silent"},
        ])]))

        result = propaganda.analyze(_doc(), client)

        self.assertEqual(len(result.techniques), 1)
        self.assertEqual(result.techniques[0].technique, "name_calling")
        self.assertEqual(result.techniques_validated, 1)
        self.assertEqual(result.techniques_dropped, 1)

    def test_all_six_ddl_techniques_are_accepted(self):
        for technique in propaganda._DDL_PROPAGANDA_TECHNIQUES:
            client = LLMClient(FakeTransport([_valid_response(techniques=[
                {"technique": technique, "confidence": 0.6,
                 "evidence_span": "corrupt politicians remained silent"},
            ])]))
            result = propaganda.analyze(_doc(), client)
            self.assertEqual(result.techniques_validated, 1, msg=technique)
            self.assertEqual(result.techniques[0].technique, technique)


class EvidenceValidationAndCapTests(unittest.TestCase):
    def test_fabricated_evidence_is_dropped_and_density_capped(self):
        client = LLMClient(FakeTransport([_valid_response(techniques=[
            {"technique": "name_calling", "confidence": 0.9,
             "evidence_span": "a phrase never spoken in this text"},
        ], overall_propaganda_score=0.9)]))

        result = propaganda.analyze(_doc(), client)

        self.assertEqual(result.techniques, [])
        self.assertEqual(result.techniques_validated, 0)
        self.assertEqual(result.techniques_dropped, 1)
        self.assertEqual(result.density, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

    def test_valid_verbatim_evidence_is_kept_uncapped(self):
        client = LLMClient(FakeTransport([_valid_response(overall_propaganda_score=0.75)]))

        result = propaganda.analyze(_doc(), client)

        self.assertEqual(len(result.techniques), 1)
        self.assertEqual(result.techniques[0].evidence_span, "corrupt politicians remained silent")
        self.assertEqual(result.density, 0.75)

    def test_zero_techniques_forces_density_to_zero(self):
        client = LLMClient(FakeTransport([_valid_response(
            techniques=[], overall_propaganda_score=0.9,
        )]))

        result = propaganda.analyze(_doc(), client)

        self.assertEqual(result.techniques, [])
        self.assertEqual(result.techniques_validated, 0)
        self.assertEqual(result.techniques_dropped, 0)
        self.assertEqual(result.density, 0.0)


class PreFilterDeterministicPathTests(unittest.TestCase):
    def test_no_loaded_language_skips_the_llm_call(self):
        backend = FakeTransport([_valid_response()])
        client = LLMClient(backend)

        result = propaganda.analyze(_doc(text_value=_NEUTRAL_TEXT), client)

        self.assertEqual(backend.calls, 0)
        self.assertEqual(result.inference_method, "deterministic")
        self.assertEqual(result.density, 0.0)
        self.assertEqual(result.techniques, [])
        self.assertEqual(result.techniques_validated, 0)
        self.assertEqual(result.techniques_dropped, 0)
        self.assertIsNone(result.raw_response)
        self.assertEqual(result.summary, propaganda._PRE_FILTER_REASONING)

    def test_empty_text_also_skips_the_llm_call(self):
        backend = FakeTransport([_valid_response()])
        client = LLMClient(backend)

        result = propaganda.analyze(_doc(text_value=""), client)

        self.assertEqual(backend.calls, 0)
        self.assertEqual(result.inference_method, "deterministic")


class ValidatedDroppedCountingTests(unittest.TestCase):
    def test_counts_mixed_valid_and_invalid_techniques(self):
        client = LLMClient(FakeTransport([_valid_response(techniques=[
            {"technique": "flag_waving", "confidence": 0.9,
             "evidence_span": "waving the flag of patriotism proudly"},
            {"technique": "name_calling", "confidence": 0.7,
             "evidence_span": "a phrase never spoken in this text"},
            {"technique": "appeal_to_fear", "confidence": 0.85,
             "evidence_span": "corrupt politicians remained silent"},
        ])]))

        result = propaganda.analyze(_doc(), client)

        self.assertEqual(result.techniques_validated, 1)
        self.assertEqual(result.techniques_dropped, 2)
        self.assertEqual(len(result.techniques), 1)
        self.assertEqual(result.techniques[0].technique, "appeal_to_fear")


class AnalyzeSchemaRetryTests(unittest.TestCase):
    def test_schema_invalid_response_retries_then_succeeds(self):
        backend = FakeTransport([SchemaValidationError("bad enum"), _valid_response()])
        client = LLMClient(backend, max_retries=3)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            result = propaganda.analyze(_doc(), client)

        self.assertEqual(backend.calls, 2)
        self.assertEqual(result.inference_method, "llm")


class AnalyzeLlmFailureTests(unittest.TestCase):
    """No heuristic fallback -- a failed or unavailable LLM call must raise
    out of analyze(), not silently degrade to an empty verdict. (The empty
    verdict from the pre-filter is a distinct, deterministic code path --
    see PreFilterDeterministicPathTests -- not a failure substitute.)"""

    def test_unavailable_backend_raises(self):
        client = LLMClient(FakeTransport([_valid_response()], available=False))

        with self.assertRaises(RuntimeError):
            propaganda.analyze(_doc(), client)

    def test_exhausted_retries_raises(self):
        client = LLMClient(FakeTransport([RuntimeError("e1"), RuntimeError("e2")]), max_retries=2)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError):
                propaganda.analyze(_doc(), client)


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
                "TRUNCATE analysis.propaganda_techniques, analysis.propaganda_results, "
                "analysis.runs, analysis.prompt_versions, corpus.documents CASCADE"
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

    def test_valid_llm_response_persists_parent_and_child_rows(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        run_id = propaganda.process(_doc(self.doc_id), client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertTrue(run["is_current"])
        self.assertEqual(run["inference_method"], "llm")
        self.assertIsNotNone(run["prompt_version_id"])
        self.assertEqual(run["confidence"], 0.75)

        from analysis.src.results import store
        with store.db.connection() as conn:
            result_row = conn.execute(
                "SELECT * FROM analysis.propaganda_results WHERE run_id = %s", (run_id,)
            ).fetchone()
            technique_rows = conn.execute(
                "SELECT * FROM analysis.propaganda_techniques WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(result_row["density"], 0.75)
        self.assertEqual(result_row["techniques_validated"], 1)
        self.assertEqual(result_row["techniques_dropped"], 0)
        self.assertEqual(len(technique_rows), 1)
        self.assertEqual(technique_rows[0]["technique"], "name_calling")

    def test_pre_filter_process_is_deterministic_with_zero_techniques(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        neutral_doc = _doc(self.doc_id, text_value=_NEUTRAL_TEXT)
        run_id = propaganda.process(neutral_doc, client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertEqual(run["inference_method"], "deterministic")
        self.assertIsNone(run["prompt_version_id"])
        self.assertEqual(run["confidence"], 0.0)

        from analysis.src.results import store
        with store.db.connection() as conn:
            result_row = conn.execute(
                "SELECT * FROM analysis.propaganda_results WHERE run_id = %s", (run_id,)
            ).fetchone()
            technique_rows = conn.execute(
                "SELECT * FROM analysis.propaganda_techniques WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(result_row["density"], 0.0)
        self.assertEqual(result_row["techniques_validated"], 0)
        self.assertEqual(result_row["techniques_dropped"], 0)
        self.assertEqual(technique_rows, [])

    def test_mixed_validated_and_dropped_techniques_persist_both_counts(self):
        client = LLMClient(FakeTransport([_valid_response(techniques=[
            {"technique": "flag_waving", "confidence": 0.9,
             "evidence_span": "waving the flag of patriotism proudly"},
            {"technique": "name_calling", "confidence": 0.7,
             "evidence_span": "a phrase never spoken in this text"},
            {"technique": "appeal_to_fear", "confidence": 0.85,
             "evidence_span": "corrupt politicians remained silent"},
        ])]))
        run_id = propaganda.process(_doc(self.doc_id), client).run_id

        from analysis.src.results import store
        with store.db.connection() as conn:
            result_row = conn.execute(
                "SELECT * FROM analysis.propaganda_results WHERE run_id = %s", (run_id,)
            ).fetchone()
            technique_rows = conn.execute(
                "SELECT * FROM analysis.propaganda_techniques WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(result_row["techniques_validated"], 1)
        self.assertEqual(result_row["techniques_dropped"], 2)
        self.assertEqual(len(technique_rows), 1)
        self.assertEqual(technique_rows[0]["technique"], "appeal_to_fear")

    def test_reprocess_supersedes_prior_run(self):
        first_client = LLMClient(FakeTransport([_valid_response()]))
        first_run = propaganda.process(_doc(self.doc_id), first_client).run_id

        second_client = LLMClient(FakeTransport([_valid_response(overall_propaganda_score=0.5)]))
        second_run = propaganda.process(_doc(self.doc_id), second_client).run_id

        self.assertNotEqual(first_run, second_run)
        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = {
                r["run_id"]: r["is_current"]
                for r in conn.execute(
                    "SELECT run_id, is_current FROM analysis.runs "
                    "WHERE task = 'propaganda'::analysis.task AND doc_id = %s", (self.doc_id,)
                ).fetchall()
            }
        self.assertFalse(rows[first_run])
        self.assertTrue(rows[second_run])

    def test_failed_llm_call_records_failed_run_with_error(self):
        client = LLMClient(FakeTransport([_valid_response()], available=False))
        run_id = propaganda.process(_doc(self.doc_id), client).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "failed")
        self.assertFalse(run["is_current"])
        self.assertIsNotNone(run["error"])
        self.assertIsNone(run["raw_response"])

        from analysis.src.results import store
        with store.db.connection() as conn:
            result_row = conn.execute(
                "SELECT * FROM analysis.propaganda_results WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertIsNone(result_row)


if __name__ == "__main__":
    unittest.main()
