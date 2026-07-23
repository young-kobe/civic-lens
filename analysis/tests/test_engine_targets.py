"""
Tests for analysis/src/engine/targets.py -- Postgres redesign Phase 6.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_result_store.py, test_engine_text.py):

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
from analysis.src.engine import targets
from analysis.src.engine.constants import UNVERIFIED_EVIDENCE_CONFIDENCE_CAP
from analysis.src.llm.base import SchemaValidationError
from analysis.src.llm.client import LLMClient
from analysis.tests import pg_fixture


# =============================================================================
# Fakes shared by Tier 1 tests below (same shape as test_engine_text.py's).
# =============================================================================

class FakeTransport:
    """Stands in for a backend's complete_once()/is_available/
    get_token_usage() surface. Each call pops the next scripted outcome --
    an exception to raise, or a dict to return."""

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
    enough for the resolution/unresolved-kept tests below."""

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
        "targets": [
            {
                "target": "Donald Trump",
                "topic": "Economy",
                "stance": "positive",
                "confidence": 0.75,
                "evidence_spans": ["praised the new policy strongly"],
            }
        ],
        "reasoning": "test reasoning",
    }
    base.update(overrides)
    return base


_SAMPLE_TEXT = (
    "Reporters said this is a bad policy decision overall. "
    "Meanwhile, allies noted that Trump praised the new policy strongly."
)


def _doc(doc_id=1, text_value=_SAMPLE_TEXT, tracked_targets=()) -> "targets.TargetDocInput":
    return targets.TargetDocInput(
        doc_id=doc_id, source_type="news", text=text_value, tracked_targets=tracked_targets,
    )


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class AnalyzeValidResponseTests(unittest.TestCase):
    def test_valid_response_maps_target_with_resolved_entity(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(result.inference_method, "llm")
        self.assertEqual(len(result.mentions), 1)
        mention = result.mentions[0]
        self.assertEqual(mention.raw_target, "Donald Trump")
        self.assertEqual(mention.entity_id, 42)
        self.assertEqual(mention.stance, "positive")
        self.assertEqual(mention.topic, "Economy")
        self.assertEqual(mention.confidence, 0.75)
        self.assertEqual(mention.evidence_spans, ["praised the new policy strongly"])
        self.assertEqual(result.dropped_unmappable_stance, 0)
        self.assertIsNotNone(result.raw_response)

    def test_unknown_topic_defaults_to_other(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[{
            "target": "Donald Trump", "topic": "NotARealTopic", "stance": "positive",
            "confidence": 0.6, "evidence_spans": ["praised the new policy strongly"],
        }])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(result.mentions[0].topic, "Other")

    def test_empty_targets_is_a_legitimate_empty_result(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[])]))
        resolver = _resolver_with_entity(1, "x", "X")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(result.mentions, [])
        self.assertEqual(result.dropped_unmappable_stance, 0)
        self.assertEqual(result.inference_method, "llm")


class AnalyzeSchemaRetryTests(unittest.TestCase):
    def test_schema_invalid_response_retries_then_succeeds(self):
        backend = FakeTransport([SchemaValidationError("bad enum"), _valid_response()])
        client = LLMClient(backend, max_retries=3)
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(backend.calls, 2)
        self.assertEqual(len(result.mentions), 1)


class AnalyzeTrivialContentTests(unittest.TestCase):
    def test_trivial_content_skips_the_llm_call(self):
        backend = FakeTransport([_valid_response()])
        client = LLMClient(backend)
        resolver = _resolver_with_entity(1, "x", "X")
        trivial_doc = targets.TargetDocInput(
            doc_id=1, source_type="x_post",
            text="@someone #politics https://example.com",
        )

        result = targets.analyze(trivial_doc, client, resolver)

        self.assertEqual(backend.calls, 0)
        self.assertEqual(result.inference_method, "deterministic")
        self.assertEqual(result.mentions, [])
        self.assertEqual(result.dropped_unmappable_stance, 0)
        self.assertIsNone(result.raw_response)


class AnalyzeStanceMappingTests(unittest.TestCase):
    def test_all_four_ddl_stance_values_map_through(self):
        resolver = _resolver_with_entity(1, "x", "X")
        for stance in ("positive", "negative", "neutral", "mixed"):
            client = LLMClient(FakeTransport([_valid_response(targets=[{
                "target": "Donald Trump", "topic": "Economy", "stance": stance,
                "confidence": 0.6, "evidence_spans": ["praised the new policy strongly"],
            }])]))
            result = targets.analyze(_doc(), client, resolver)
            self.assertEqual(result.mentions[0].stance, stance)

    def test_unmappable_stance_is_dropped_and_counted(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[{
            "target": "Donald Trump", "topic": "Economy", "stance": "strongly_negative",
            "confidence": 0.6, "evidence_spans": ["praised the new policy strongly"],
        }])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(result.mentions, [])
        self.assertEqual(result.dropped_unmappable_stance, 1)

    def test_mixed_mappable_and_unmappable_targets(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[
            {"target": "Donald Trump", "topic": "Economy", "stance": "positive",
             "confidence": 0.6, "evidence_spans": ["praised the new policy strongly"]},
            {"target": "Some Group", "topic": "Economy", "stance": "bogus_stance",
             "confidence": 0.6, "evidence_spans": ["this is a bad policy decision"]},
        ])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(len(result.mentions), 1)
        self.assertEqual(result.mentions[0].raw_target, "Donald Trump")
        self.assertEqual(result.dropped_unmappable_stance, 1)


class AnalyzeEvidenceValidationTests(unittest.TestCase):
    def test_fabricated_evidence_is_dropped_and_confidence_capped(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[{
            "target": "Donald Trump", "topic": "Economy", "stance": "positive",
            "confidence": 0.9, "evidence_spans": ["a phrase never spoken in this text"],
        }])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        mention = result.mentions[0]
        self.assertEqual(mention.evidence_spans, [])
        self.assertEqual(mention.confidence, UNVERIFIED_EVIDENCE_CONFIDENCE_CAP)

    def test_valid_verbatim_evidence_is_kept_uncapped(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        mention = result.mentions[0]
        self.assertEqual(mention.evidence_spans, ["praised the new policy strongly"])
        self.assertEqual(mention.confidence, 0.75)


class AnalyzeUnresolvedEntityTests(unittest.TestCase):
    """KEY ASYMMETRY vs the text engine: an unresolved target is KEPT with
    entity_id=None (target_mentions.entity_id is NULLABLE), never dropped."""

    def test_unresolved_target_kept_with_entity_id_none(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[{
            "target": "Someone Unregistered", "topic": "Economy", "stance": "positive",
            "confidence": 0.6, "evidence_spans": ["praised the new policy strongly"],
        }])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(len(result.mentions), 1)
        mention = result.mentions[0]
        self.assertEqual(mention.raw_target, "Someone Unregistered")
        self.assertIsNone(mention.entity_id)

    def test_mixed_resolved_and_unresolved_targets_both_kept(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[
            {"target": "Donald Trump", "topic": "Economy", "stance": "positive",
             "confidence": 0.75, "evidence_spans": ["praised the new policy strongly"]},
            {"target": "Someone Unregistered", "topic": "Economy", "stance": "negative",
             "confidence": 0.6, "evidence_spans": ["this is a bad policy decision"]},
        ])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(len(result.mentions), 2)
        by_name = {m.raw_target: m for m in result.mentions}
        self.assertEqual(by_name["Donald Trump"].entity_id, 42)
        self.assertIsNone(by_name["Someone Unregistered"].entity_id)


class AnalyzeDedupTests(unittest.TestCase):
    def test_repeated_target_name_keeps_only_first(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[
            {"target": "Donald Trump", "topic": "Economy", "stance": "positive",
             "confidence": 0.75, "evidence_spans": ["praised the new policy strongly"]},
            {"target": "donald trump", "topic": "Justice", "stance": "negative",
             "confidence": 0.5, "evidence_spans": ["this is a bad policy decision"]},
        ])]))
        resolver = _resolver_with_entity(42, "trump", "Donald Trump")

        result = targets.analyze(_doc(), client, resolver)

        self.assertEqual(len(result.mentions), 1)
        self.assertEqual(result.mentions[0].stance, "positive")


class AnalyzeLlmFailureTests(unittest.TestCase):
    """No heuristic fallback -- a failed or unavailable LLM call must raise
    out of analyze(), not silently degrade to a lower-quality guess."""

    def test_unavailable_backend_raises(self):
        client = LLMClient(FakeTransport([_valid_response()], available=False))
        resolver = _resolver_with_entity(1, "x", "X")

        with self.assertRaises(RuntimeError):
            targets.analyze(_doc(), client, resolver)

    def test_exhausted_retries_raises(self):
        client = LLMClient(FakeTransport([RuntimeError("e1"), RuntimeError("e2")]), max_retries=2)
        resolver = _resolver_with_entity(1, "x", "X")

        import unittest.mock as mock
        with mock.patch("analysis.src.llm.client.time.sleep"):
            with self.assertRaises(RuntimeError):
                targets.analyze(_doc(), client, resolver)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL
# =============================================================================


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ProcessIntegrationTests(unittest.TestCase):
    """Live-run against a real Postgres with 0001+0002 applied. 0002 seeds the
    real curated entity registry (587 rows incl. entity_key='potus' for
    Donald Trump) -- resolution below is exercised against actual registry
    data rather than a hand-rolled fixture entity, so corpus.entities/
    entity_aliases are loaded once in setUpClass and never truncated between
    tests (only the mutable per-doc/per-run tables are)."""

    @classmethod
    def setUpClass(cls):
        import psycopg
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn, seed=True)
        with psycopg.connect(cls._dsn, autocommit=True) as conn:
            row = conn.execute(
                "SELECT entity_id FROM corpus.entities WHERE entity_key = 'potus'"
            ).fetchone()
            cls._trump_entity_id = row[0]

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_mutable()
        self.doc_id = self._seed_doc("doc-1")
        self.entity_id = self._trump_entity_id
        from analysis.src.common.entity_resolver import EntityResolver
        self.resolver = EntityResolver()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_mutable(self):
        # corpus.entities/entity_aliases (the 0002 seed) are deliberately NOT
        # truncated -- they are the fixed registry every test resolves against.
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.target_mentions, analysis.runs, "
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

    def test_valid_llm_response_persists_resolved_and_unresolved_mentions(self):
        client = LLMClient(FakeTransport([_valid_response(targets=[
            {"target": "Donald Trump", "topic": "Economy", "stance": "positive",
             "confidence": 0.75, "evidence_spans": ["praised the new policy strongly"]},
            {"target": "Some Unregistered Group", "topic": "Justice", "stance": "negative",
             "confidence": 0.6, "evidence_spans": ["this is a bad policy decision"]},
        ])]))
        run_id = targets.process(_doc(self.doc_id), client, self.resolver).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertTrue(run["is_current"])
        self.assertEqual(run["inference_method"], "llm")
        self.assertIsNotNone(run["prompt_version_id"])
        self.assertEqual(run["model_id"], targets._resolve_model_id())

        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT * FROM analysis.target_mentions WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(len(rows), 2)
        by_name = {r["raw_target"]: r for r in rows}
        self.assertEqual(by_name["Donald Trump"]["entity_id"], self.entity_id)
        self.assertEqual(by_name["Donald Trump"]["stance"], "positive")
        self.assertIsNone(by_name["Some Unregistered Group"]["entity_id"])
        self.assertEqual(by_name["Some Unregistered Group"]["stance"], "negative")

    def test_trivial_content_process_is_deterministic_with_no_prompt_version(self):
        client = LLMClient(FakeTransport([_valid_response()]))
        trivial_doc = targets.TargetDocInput(
            doc_id=self.doc_id, source_type="x_post",
            text="@someone #politics https://example.com",
        )
        run_id = targets.process(trivial_doc, client, self.resolver).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertEqual(run["inference_method"], "deterministic")
        self.assertIsNone(run["prompt_version_id"])

        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT * FROM analysis.target_mentions WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(rows, [])

    def test_reprocess_supersedes_prior_run(self):
        first_client = LLMClient(FakeTransport([_valid_response()]))
        first_run = targets.process(_doc(self.doc_id), first_client, self.resolver).run_id

        second_client = LLMClient(FakeTransport([_valid_response(targets=[{
            "target": "Donald Trump", "topic": "Economy", "stance": "negative",
            "confidence": 0.65, "evidence_spans": ["this is a bad policy decision"],
        }])]))
        second_run = targets.process(_doc(self.doc_id), second_client, self.resolver).run_id

        self.assertNotEqual(first_run, second_run)
        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = {
                r["run_id"]: r["is_current"]
                for r in conn.execute(
                    "SELECT run_id, is_current FROM analysis.runs "
                    "WHERE task = 'targets'::analysis.task AND doc_id = %s", (self.doc_id,)
                ).fetchall()
            }
        self.assertFalse(rows[first_run])
        self.assertTrue(rows[second_run])

    def test_failed_llm_call_records_failed_run_with_error(self):
        client = LLMClient(FakeTransport([_valid_response()], available=False))
        run_id = targets.process(_doc(self.doc_id), client, self.resolver).run_id

        run = self._run_row(run_id)
        self.assertEqual(run["status"], "failed")
        self.assertFalse(run["is_current"])
        self.assertIsNotNone(run["error"])
        self.assertIsNone(run["raw_response"])

        from analysis.src.results import store
        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT * FROM analysis.target_mentions WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(rows, [])


if __name__ == "__main__":
    unittest.main()
