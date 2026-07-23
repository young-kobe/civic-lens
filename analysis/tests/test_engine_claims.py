"""
Tests for analysis/src/engine/claims.py -- Postgres redesign Phase 6, Wave 2.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_engine_text.py, test_engine_citations.py):

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

from analysis.src.engine import claims
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


_SAMPLE_TEXT = (
    "Trump dominated in Pennsylvania last night, flipping the state red for the "
    "first time in decades. Meanwhile allies say the DNC is scrambling for a "
    "response this week."
)


def _claim(claim_text: str, evidence_span: str, confidence: float = 0.85) -> dict:
    return {"claim": claim_text, "confidence": confidence, "evidence_span": evidence_span}


def _response(*raw_claims: dict) -> dict:
    return {"claims": list(raw_claims)}


def _words(n: int) -> str:
    """A claim-text stand-in with exactly n whitespace-separated tokens --
    only the word count matters for the length-bound tests."""
    return " ".join(f"word{i}" for i in range(n))


def _doc(doc_id=1, text_value=_SAMPLE_TEXT) -> "claims.ClaimDocInput":
    return claims.ClaimDocInput(doc_id=doc_id, text=text_value)


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class AnalyzeExtractionMappingTests(unittest.TestCase):
    def test_valid_claim_is_mapped_through(self):
        response = _response(_claim(
            "Trump won Pennsylvania decisively", "Trump dominated in Pennsylvania", 0.85,
        ))
        client = LLMClient(FakeTransport([response]))

        result = claims.analyze(_doc(), client)

        self.assertEqual(result.inference_method, "llm")
        self.assertEqual(result.extracted_count, 1)
        self.assertEqual(result.dropped_count, 0)
        self.assertEqual(len(result.claims), 1)
        claim = result.claims[0]
        self.assertEqual(claim.claim_text, "Trump won Pennsylvania decisively")
        self.assertEqual(claim.confidence, 0.85)
        self.assertEqual(claim.evidence_span, "Trump dominated in Pennsylvania")
        self.assertEqual(result.raw_response, response)


class AnalyzeClaimLengthBoundTests(unittest.TestCase):
    """MIN_CLAIM_WORDS=4, MAX_CLAIM_WORDS=20 (engine/constants.py) --
    boundary values 3/4/20/21, evidence held constant and valid throughout."""

    _EVIDENCE = "Trump dominated in Pennsylvania"

    def _analyze_with_claim_words(self, n: int):
        response = _response(_claim(_words(n), self._EVIDENCE))
        client = LLMClient(FakeTransport([response]))
        return claims.analyze(_doc(), client)

    def test_three_words_rejected(self):
        result = self._analyze_with_claim_words(3)
        self.assertEqual(result.claims, [])
        self.assertEqual(result.dropped_count, 1)

    def test_four_words_accepted(self):
        result = self._analyze_with_claim_words(4)
        self.assertEqual(len(result.claims), 1)
        self.assertEqual(result.dropped_count, 0)

    def test_twenty_words_accepted(self):
        result = self._analyze_with_claim_words(20)
        self.assertEqual(len(result.claims), 1)
        self.assertEqual(result.dropped_count, 0)

    def test_twenty_one_words_rejected(self):
        result = self._analyze_with_claim_words(21)
        self.assertEqual(result.claims, [])
        self.assertEqual(result.dropped_count, 1)


class AnalyzeEvidenceDropTests(unittest.TestCase):
    """The drop-vs-cap finding: a claim whose evidence fails verification is
    DROPPED ENTIRELY (not kept with confidence capped, unlike text.py's
    sentiment/favorability spans) -- ported unchanged from the old
    claim_extractor.py's `if evidence.lower() not in source_text.lower():
    return None`."""

    def test_fabricated_evidence_drops_the_claim(self):
        response = _response(_claim(
            "Trump won Pennsylvania decisively", "a phrase never spoken in this text",
        ))
        client = LLMClient(FakeTransport([response]))

        result = claims.analyze(_doc(), client)

        self.assertEqual(result.claims, [])
        self.assertEqual(result.extracted_count, 0)
        self.assertEqual(result.dropped_count, 1)

    def test_evidence_below_word_floor_drops_the_claim(self):
        """"Trump dominated in" is a verbatim substring of the source text
        but only 3 words -- below MIN_EVIDENCE_WORDS=4 (the 3->4 floor
        change this wave records) -- so it is dropped, not merely short."""
        response = _response(_claim("Trump won Pennsylvania decisively", "Trump dominated in"))
        client = LLMClient(FakeTransport([response]))

        result = claims.analyze(_doc(), client)

        self.assertEqual(result.claims, [])
        self.assertEqual(result.dropped_count, 1)

    def test_valid_verbatim_evidence_is_kept(self):
        response = _response(_claim(
            "Trump won Pennsylvania decisively", "Trump dominated in Pennsylvania",
        ))
        client = LLMClient(FakeTransport([response]))

        result = claims.analyze(_doc(), client)

        self.assertEqual(len(result.claims), 1)
        self.assertEqual(result.claims[0].evidence_span, "Trump dominated in Pennsylvania")


class AnalyzeDroppedCountingTests(unittest.TestCase):
    def test_mixed_valid_and_invalid_claims_counted_separately(self):
        response = _response(
            _claim("Trump won Pennsylvania decisively", "Trump dominated in Pennsylvania"),
            _claim("The DNC is scrambling for a response", "the DNC is scrambling for a response"),
            _claim("Fabricated assertion about the race", "nonexistent phrase not in source"),
        )
        client = LLMClient(FakeTransport([response]))

        result = claims.analyze(_doc(), client)

        self.assertEqual(result.extracted_count, 2)
        self.assertEqual(result.dropped_count, 1)
        self.assertEqual(len(result.claims), 2)

    def test_extra_claims_beyond_the_cap_are_ignored(self):
        """Defensive cap mirrors the old code's `raw_claims[:3]` -- a fourth
        claim is neither extracted nor counted as dropped."""
        response = _response(
            _claim("Trump won Pennsylvania decisively", "Trump dominated in Pennsylvania"),
            _claim("The DNC is scrambling for a response", "the DNC is scrambling for a response"),
            _claim("Allies say the state has flipped", "flipping the state red for the"),
            _claim("A fourth claim past the cap", "Trump dominated in Pennsylvania"),
        )
        client = LLMClient(FakeTransport([response]))

        result = claims.analyze(_doc(), client)

        self.assertEqual(result.extracted_count + result.dropped_count, 3)


class AnalyzeTrivialContentTests(unittest.TestCase):
    def test_trivial_content_skips_the_llm_call(self):
        backend = FakeTransport([_response(_claim("unused", "unused text here"))])
        client = LLMClient(backend)
        trivial_doc = claims.ClaimDocInput(doc_id=1, text="@someone #politics https://example.com")

        result = claims.analyze(trivial_doc, client)

        self.assertEqual(backend.calls, 0)
        self.assertEqual(result.inference_method, "deterministic")
        self.assertEqual(result.claims, [])
        self.assertEqual(result.extracted_count, 0)
        self.assertEqual(result.dropped_count, 0)
        self.assertIsNone(result.raw_response)


class AnalyzeLlmFailureTests(unittest.TestCase):
    def test_unavailable_backend_raises(self):
        client = LLMClient(FakeTransport([_response()], available=False))

        with self.assertRaises(RuntimeError):
            claims.analyze(_doc(), client)

    def test_exhausted_retries_raises(self):
        client = LLMClient(FakeTransport([RuntimeError("e1"), RuntimeError("e2")]), max_retries=2)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError):
                claims.analyze(_doc(), client)

    def test_schema_invalid_response_retries_then_succeeds(self):
        response = _response(_claim("Trump won Pennsylvania decisively", "Trump dominated in Pennsylvania"))
        backend = FakeTransport([SchemaValidationError("bad enum"), response])
        client = LLMClient(backend, max_retries=3)

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            result = claims.analyze(_doc(), client)

        self.assertEqual(backend.calls, 2)
        self.assertEqual(len(result.claims), 1)


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
                "TRUNCATE analysis.claims, analysis.runs, "
                "analysis.prompt_versions, corpus.documents CASCADE"
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

    def _claim_rows(self, run_id):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.claims WHERE run_id = %s", (run_id,)
            ).fetchall()

    def test_valid_llm_response_persists_run_and_claim_rows(self):
        response = _response(_claim("Trump won Pennsylvania decisively", "Trump dominated in Pennsylvania", 0.85))
        client = LLMClient(FakeTransport([response]))

        run_id = claims.process(_doc(self.doc_id), client)

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertTrue(run["is_current"])
        self.assertEqual(run["inference_method"], "llm")
        self.assertIsNotNone(run["prompt_version_id"])
        self.assertEqual(run["model_id"], claims._resolve_model_id())
        self.assertEqual(run["confidence"], 0.85)

        rows = self._claim_rows(run_id)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["claim_text"], "Trump won Pennsylvania decisively")
        self.assertEqual(rows[0]["doc_id"], self.doc_id)

    def test_trivial_content_process_is_deterministic_with_no_prompt_version(self):
        client = LLMClient(FakeTransport([_response()]))
        trivial_doc = claims.ClaimDocInput(doc_id=self.doc_id, text="@someone #politics https://example.com")

        run_id = claims.process(trivial_doc, client)

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertEqual(run["inference_method"], "deterministic")
        self.assertIsNone(run["prompt_version_id"])
        self.assertEqual(self._claim_rows(run_id), [])

    def test_zero_claims_from_llm_response_is_clean_done_run(self):
        """The LLM is called (not trivial content) but every proposed claim
        fails validation -- still a clean 'done' run, zero claims rows,
        confidence 0.0 (old job_runner convention), prompt_version_id set
        because the LLM genuinely ran."""
        response = _response(_claim("Fabricated assertion here", "phrase not present anywhere"))
        client = LLMClient(FakeTransport([response]))

        run_id = claims.process(_doc(self.doc_id), client)

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertTrue(run["is_current"])
        self.assertEqual(run["inference_method"], "llm")
        self.assertIsNotNone(run["prompt_version_id"])
        self.assertEqual(run["confidence"], 0.0)
        self.assertEqual(self._claim_rows(run_id), [])

    def test_reprocess_supersedes_prior_run(self):
        first_response = _response(_claim("Trump won Pennsylvania decisively", "Trump dominated in Pennsylvania"))
        first_client = LLMClient(FakeTransport([first_response]))
        first_run = claims.process(_doc(self.doc_id), first_client)

        second_response = _response(_claim("The DNC is scrambling now", "the DNC is scrambling for"))
        second_client = LLMClient(FakeTransport([second_response]))
        second_run = claims.process(_doc(self.doc_id), second_client)

        self.assertNotEqual(first_run, second_run)
        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = {
                r["run_id"]: r["is_current"]
                for r in conn.execute(
                    "SELECT run_id, is_current FROM analysis.runs "
                    "WHERE task = 'claims'::analysis.task AND doc_id = %s", (self.doc_id,)
                ).fetchall()
            }
        self.assertFalse(rows[first_run])
        self.assertTrue(rows[second_run])
        # Prior run's claim rows are not deleted (append-only per-run history).
        self.assertEqual(len(self._claim_rows(first_run)), 1)
        self.assertEqual(len(self._claim_rows(second_run)), 1)

    def test_failed_llm_call_records_failed_run_with_error(self):
        client = LLMClient(FakeTransport([_response()], available=False))
        run_id = claims.process(_doc(self.doc_id), client)

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "failed")
        self.assertFalse(run["is_current"])
        self.assertIsNotNone(run["error"])
        self.assertIsNone(run["raw_response"])
        self.assertEqual(self._claim_rows(run_id), [])


if __name__ == "__main__":
    unittest.main()
