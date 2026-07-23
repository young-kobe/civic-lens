"""
Tests for analysis/src/results/store.py — Postgres redesign Phase 5.

Two tiers, matching the repo's established convention for the Postgres-
redesign test modules (analysis/tests/test_pg_db.py,
analysis/tests/test_etl_documents.py):

  1. Pure validation tests (no DB) — the XOR/traceability checks that raise
     before store.py ever touches a connection. Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001_north_star.sql applied. Skipped
     (never failed) when the env var is absent.
"""

from __future__ import annotations

import os
import sys
import threading
import unittest
from pathlib import Path
from unittest import mock

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.results import store

REPO_ROOT = Path(project_root)
MIGRATION_SQL = REPO_ROOT / "data" / "pg-migrations" / "0001_north_star.sql"


# =============================================================================
# Tier 1 — no DB required. store.open_run()/finish() raise these before
# ever checking out a connection.
# =============================================================================

_FAKE_RUN_ID = 999


class _FakeCursor:
    """Stands in for a psycopg cursor: every execute() in store.py chains
    straight into .fetchone(), and store.py never inspects fetchall() results
    from these two call paths, so a single fixed run_id is enough."""

    def fetchone(self):
        return {"run_id": _FAKE_RUN_ID}


class _FakeConn:
    def execute(self, *args, **kwargs):
        return _FakeCursor()


class _FakeConnCtx:
    def __enter__(self):
        return _FakeConn()

    def __exit__(self, *exc):
        return False

class OpenRunValidationTests(unittest.TestCase):
    def test_rejects_neither_doc_nor_author(self):
        with self.assertRaises(ValueError):
            store.open_run("bot", model_id="m", inference_method="deterministic")

    def test_rejects_both_doc_and_author(self):
        with self.assertRaises(ValueError):
            store.open_run(
                "bot", doc_id=1, author_id=1, model_id="m", inference_method="deterministic"
            )

    def test_rejects_empty_model_id(self):
        with self.assertRaises(ValueError):
            store.open_run("bot", doc_id=1, model_id="", inference_method="deterministic")

    def test_accepts_doc_scoped(self):
        handle = store.open_run("bot", doc_id=1, model_id="m", inference_method="deterministic")
        self.assertEqual(handle.doc_id, 1)
        self.assertIsNone(handle.author_id)

    def test_accepts_author_scoped(self):
        handle = store.open_run(
            "account_tier", author_id=7, model_id="m", inference_method="deterministic"
        )
        self.assertEqual(handle.author_id, 7)
        self.assertIsNone(handle.doc_id)


class CitationRowValidationTests(unittest.TestCase):
    def test_rejects_neither_target(self):
        with self.assertRaises(ValueError):
            store.CitationRow(link_type="url_citation")

    def test_rejects_both_targets(self):
        with self.assertRaises(ValueError):
            store.CitationRow(link_type="url_citation", target_doc_id=1, target_url="http://x")

    def test_accepts_target_doc_id_only(self):
        row = store.CitationRow(link_type="url_citation", target_doc_id=1)
        self.assertEqual(row.target_doc_id, 1)

    def test_accepts_target_url_only(self):
        row = store.CitationRow(link_type="url_citation", target_url="http://x")
        self.assertEqual(row.target_url, "http://x")


class RunHandleScopeValidationTests(unittest.TestCase):
    """bot_signals/citations always carry doc_id, so calling them on an
    author-scoped handle is a caller bug the store must catch immediately."""

    def test_save_bot_signals_requires_doc_scope(self):
        handle = store.open_run(
            "account_tier", author_id=1, model_id="m", inference_method="deterministic"
        )
        with self.assertRaises(ValueError):
            handle.save_bot_signals(store.BotSignalsRow(label="human"))

    def test_save_citations_requires_doc_scope(self):
        handle = store.open_run(
            "account_tier", author_id=1, model_id="m", inference_method="deterministic"
        )
        with self.assertRaises(ValueError):
            handle.save_citations([store.CitationRow(link_type="url_citation", target_url="http://x")])


class FinishValidationTests(unittest.TestCase):
    def test_rejects_invalid_status(self):
        handle = store.open_run("bot", doc_id=1, model_id="m", inference_method="deterministic")
        with self.assertRaises(ValueError):
            handle.finish("running")

    def test_succeeded_llm_run_without_prompt_version_raises(self):
        handle = store.open_run("text", doc_id=1, model_id="gemini-3.5-flash", inference_method="llm")
        with self.assertRaises(ValueError) as ctx:
            handle.finish("done", confidence=0.9)
        self.assertIn("prompt_version", str(ctx.exception))

    def test_failed_llm_run_without_prompt_version_does_not_raise_traceability_error(self):
        """A run that never got as far as producing a prompted result must
        still be recordable as failed -- only a SUCCEEDED llm run enforces
        prompt_version. db.connection() is faked out so this stays DB-free;
        only the traceability check itself is under test."""
        handle = store.open_run("text", doc_id=1, model_id="gemini-3.5-flash", inference_method="llm")
        with mock.patch.object(store.db, "connection", return_value=_FakeConnCtx()):
            run_id = handle.finish("failed", error="timeout")
        self.assertEqual(run_id, _FAKE_RUN_ID)

    def test_deterministic_run_without_prompt_version_passes_traceability_check(self):
        """Deterministic runs never require a prompt_version; finish() must
        proceed past the traceability check straight to the (faked) DB call."""
        handle = store.open_run("citations", doc_id=1, model_id="citation_extractor_v1",
                                 inference_method="deterministic")
        with mock.patch.object(store.db, "connection", return_value=_FakeConnCtx()):
            run_id = handle.finish("done", confidence=1.0)
        self.assertEqual(run_id, _FAKE_RUN_ID)


# =============================================================================
# Tier 2 — integration, gated on CIVIC_TEST_DATABASE_URL
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ResultStoreIntegrationTests(unittest.TestCase):
    """Live-run against a real Postgres with 0001 applied. Each test seeds
    its own corpus fixtures and truncates in setUp so tests are independent
    without needing a fresh container per test."""

    @classmethod
    def setUpClass(cls):
        import psycopg
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        with psycopg.connect(cls._dsn, autocommit=True) as conn:
            conn.execute("DROP SCHEMA IF EXISTS raw, corpus, analysis, serving, ops, archive CASCADE")
            conn.execute(MIGRATION_SQL.read_text())

    def setUp(self):
        from analysis.src.common import db as dbmod
        dbmod.close_pool()
        self._prev_url = os.environ.get("CIVIC_DATABASE_URL")
        os.environ["CIVIC_DATABASE_URL"] = self._dsn
        self._truncate_all()
        self.doc_id = self._seed_doc("doc-1")
        self.author_id = self._seed_author("author-1")
        self.entity_id = self._seed_entity("test-entity")

    def tearDown(self):
        from analysis.src.common import db as dbmod
        dbmod.close_pool()
        if self._prev_url is None:
            os.environ.pop("CIVIC_DATABASE_URL", None)
        else:
            os.environ["CIVIC_DATABASE_URL"] = self._prev_url

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.citations, analysis.bot_signals, analysis.claims, "
                "analysis.propaganda_techniques, analysis.propaganda_results, "
                "analysis.target_mentions, analysis.favorability_stances, "
                "analysis.sentiment_results, analysis.runs, analysis.prompt_versions, "
                "corpus.documents, corpus.authors, corpus.entities CASCADE"
            )

    def _seed_doc(self, natural_key: str) -> int:
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

    def _seed_author(self, platform_author_id: str) -> int:
        with store.db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id) "
                "VALUES ('x', %s) RETURNING author_id",
                (platform_author_id,),
            ).fetchone()
            return row["author_id"]

    def _seed_entity(self, entity_key: str) -> int:
        with store.db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name) "
                "VALUES (%s, 'official', 'Test Entity') RETURNING entity_id",
                (entity_key,),
            ).fetchone()
            return row["entity_id"]

    def _run_count(self, task: str, doc_id=None, author_id=None) -> int:
        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT count(*) AS n FROM analysis.runs WHERE task = %s::analysis.task "
                "AND doc_id IS NOT DISTINCT FROM %s AND author_id IS NOT DISTINCT FROM %s",
                (task, doc_id, author_id),
            ).fetchone()
            return row["n"]

    def _current_run_id(self, task: str, doc_id) -> int | None:
        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT run_id FROM analysis.runs WHERE task = %s::analysis.task "
                "AND doc_id = %s AND is_current",
                (task, doc_id),
            ).fetchone()
            return row["run_id"] if row else None

    # -- per-task writer round trips -----------------------------------

    def test_sentiment_and_favorability_round_trip(self):
        store.register_prompt_version("text_v1", "text", "system prompt")
        handle = store.open_run("text", doc_id=self.doc_id, model_id="gemini-3.5-flash",
                                 prompt_version="text_v1", inference_method="llm")
        handle.save_sentiment(store.SentimentRow(
            label="positive", score=0.8, sarcasm_detected=False, evidence_spans=["good news"]
        ))
        handle.save_favorability_stances([
            store.FavorabilityStanceRow(entity_id=self.entity_id, stance="favorable",
                                         score=0.7, evidence_spans=["praised the policy"])
        ])
        run_id = handle.finish("done", confidence=0.85)

        with store.db.connection() as conn:
            sentiment = conn.execute(
                "SELECT * FROM analysis.sentiment_results WHERE run_id = %s", (run_id,)
            ).fetchone()
            stances = conn.execute(
                "SELECT * FROM analysis.favorability_stances WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(sentiment["label"], "positive")
        self.assertEqual(sentiment["evidence_spans"], ["good news"])
        self.assertEqual(len(stances), 1)
        self.assertEqual(stances[0]["entity_id"], self.entity_id)
        self.assertEqual(stances[0]["stance"], "favorable")

    def test_target_mentions_round_trip(self):
        handle = store.open_run("targets", doc_id=self.doc_id, model_id="gemini-3.5-flash",
                                 prompt_version=None, inference_method="deterministic")
        handle.save_target_mentions([
            store.TargetMentionRow(raw_target="Senator Smith", stance="negative",
                                    confidence=0.6, entity_id=None, topic="economy",
                                    evidence_spans=["criticized the bill"]),
        ])
        run_id = handle.finish("done", confidence=0.6)

        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT * FROM analysis.target_mentions WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["raw_target"], "Senator Smith")
        self.assertIsNone(rows[0]["entity_id"])
        self.assertEqual(rows[0]["doc_id"], self.doc_id)

    def test_propaganda_round_trip(self):
        handle = store.open_run("propaganda", doc_id=self.doc_id, model_id="gemini-3.5-flash",
                                 prompt_version=None, inference_method="deterministic")
        handle.save_propaganda(
            store.PropagandaResultRow(density=0.4, summary="some loaded language",
                                       techniques_validated=1, techniques_dropped=2),
            [store.PropagandaTechniqueRow(technique="loaded_language",
                                           evidence_span="radical extremists", confidence=0.7)],
        )
        run_id = handle.finish("done", confidence=0.7)

        with store.db.connection() as conn:
            result = conn.execute(
                "SELECT * FROM analysis.propaganda_results WHERE run_id = %s", (run_id,)
            ).fetchone()
            techniques = conn.execute(
                "SELECT * FROM analysis.propaganda_techniques WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(result["density"], 0.4)
        self.assertEqual(result["techniques_validated"], 1)
        self.assertEqual(result["techniques_dropped"], 2)
        self.assertEqual(len(techniques), 1)
        self.assertEqual(techniques[0]["technique"], "loaded_language")

    def test_claims_round_trip(self):
        handle = store.open_run("claims", doc_id=self.doc_id, model_id="gemini-3.5-flash",
                                 prompt_version=None, inference_method="deterministic")
        handle.save_claims([store.ClaimRow(claim_text="X said Y happened", topic="policy",
                                            confidence=0.5)])
        run_id = handle.finish("done", confidence=0.5)

        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT * FROM analysis.claims WHERE run_id = %s", (run_id,)
            ).fetchall()
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["claim_text"], "X said Y happened")

    def test_bot_signals_round_trip(self):
        handle = store.open_run("bot", doc_id=self.doc_id, model_id="gemini-3.5-flash",
                                 prompt_version=None, inference_method="deterministic")
        handle.save_bot_signals(store.BotSignalsRow(
            label="suspicious", score=0.6, llm_text_likelihood=0.9,
            burstiness=0.2, type_token_ratio=0.5, template_score=0.3,
        ))
        run_id = handle.finish("done", confidence=0.6)

        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT * FROM analysis.bot_signals WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertEqual(row["label"], "suspicious")
        self.assertEqual(row["doc_id"], self.doc_id)

    def test_citations_round_trip(self):
        second_doc = self._seed_doc("doc-2")
        handle = store.open_run("citations", doc_id=self.doc_id, model_id="citation_extractor_v1",
                                 prompt_version=None, inference_method="deterministic")
        handle.save_citations([
            store.CitationRow(link_type="url_citation", target_doc_id=second_doc),
            store.CitationRow(link_type="quote", target_url="http://external.example/article"),
        ])
        run_id = handle.finish("done", confidence=1.0)

        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT * FROM analysis.citations WHERE run_id = %s ORDER BY citation_id", (run_id,)
            ).fetchall()
        self.assertEqual(len(rows), 2)
        self.assertEqual(rows[0]["source_doc_id"], self.doc_id)
        self.assertEqual(rows[0]["target_doc_id"], second_doc)
        self.assertIsNone(rows[0]["target_url"])
        self.assertEqual(rows[1]["target_url"], "http://external.example/article")
        self.assertIsNone(rows[1]["target_doc_id"])

    def test_author_scoped_run_round_trip(self):
        handle = store.open_run("account_tier", author_id=self.author_id, model_id="curated_list_v1",
                                 prompt_version=None, inference_method="deterministic")
        run_id = handle.finish("done", confidence=1.0)

        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT doc_id, author_id, is_current FROM analysis.runs WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertIsNone(row["doc_id"])
        self.assertEqual(row["author_id"], self.author_id)
        self.assertTrue(row["is_current"])

    # -- is_current flip semantics --------------------------------------

    def test_second_succeeded_run_flips_predecessor_current(self):
        first = store.open_run("bot", doc_id=self.doc_id, model_id="m1",
                                prompt_version=None, inference_method="deterministic")
        first_id = first.finish("done", confidence=0.5)

        second = store.open_run("bot", doc_id=self.doc_id, model_id="m2",
                                 prompt_version=None, inference_method="deterministic")
        second_id = second.finish("done", confidence=0.9)

        with store.db.connection() as conn:
            rows = {
                r["run_id"]: r["is_current"]
                for r in conn.execute(
                    "SELECT run_id, is_current FROM analysis.runs WHERE task = 'bot'::analysis.task "
                    "AND doc_id = %s", (self.doc_id,)
                ).fetchall()
            }
        self.assertFalse(rows[first_id])
        self.assertTrue(rows[second_id])
        self.assertEqual(self._current_run_id("bot", self.doc_id), second_id)

    def test_crash_mid_finish_leaves_predecessor_current_with_no_orphan_row(self):
        """Force _write_results to raise after the flip UPDATE and the runs
        INSERT have both executed inside the same transaction. Both must be
        rolled back together: the predecessor stays current and the new run
        row never persists."""
        first = store.open_run("bot", doc_id=self.doc_id, model_id="m1",
                                prompt_version=None, inference_method="deterministic")
        first_id = first.finish("done", confidence=0.5)

        second = store.open_run("bot", doc_id=self.doc_id, model_id="m2",
                                 prompt_version=None, inference_method="deterministic")
        second.save_bot_signals(store.BotSignalsRow(label="bot", score=0.9))
        with mock.patch.object(
            store.RunHandle, "_write_results", side_effect=RuntimeError("simulated crash")
        ):
            with self.assertRaises(RuntimeError):
                second.finish("done", confidence=0.9)

        self.assertEqual(self._run_count("bot", doc_id=self.doc_id), 1)
        self.assertEqual(self._current_run_id("bot", self.doc_id), first_id)
        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT is_current FROM analysis.runs WHERE run_id = %s", (first_id,)
            ).fetchone()
        self.assertTrue(row["is_current"])

    def test_missing_prompt_version_registration_raises_and_writes_nothing(self):
        handle = store.open_run("text", doc_id=self.doc_id, model_id="gemini-3.5-flash",
                                 prompt_version="never_registered_v1", inference_method="llm")
        with self.assertRaises(ValueError) as ctx:
            handle.finish("done", confidence=0.5)
        self.assertIn("never_registered_v1", str(ctx.exception))
        self.assertEqual(self._run_count("text", doc_id=self.doc_id), 0)

    def test_two_concurrent_finishes_never_violate_partial_unique_index(self):
        """Two RunHandles for the same (doc, task), finished from separate
        threads/connections at once. The advisory lock in finish() must
        serialize them: both succeed, exactly one ends up is_current."""
        results = {}
        errors = []

        def _finish(model_id, confidence):
            handle = store.open_run("bot", doc_id=self.doc_id, model_id=model_id,
                                     prompt_version=None, inference_method="deterministic")
            try:
                results[model_id] = handle.finish("done", confidence=confidence)
            except Exception as exc:  # noqa: BLE001 - captured for assertion below
                errors.append(exc)

        t1 = threading.Thread(target=_finish, args=("thread-a", 0.4))
        t2 = threading.Thread(target=_finish, args=("thread-b", 0.6))
        t1.start()
        t2.start()
        t1.join(timeout=10)
        t2.join(timeout=10)

        self.assertEqual(errors, [])
        self.assertEqual(len(results), 2)
        self.assertEqual(self._run_count("bot", doc_id=self.doc_id), 2)

        with store.db.connection() as conn:
            current_rows = conn.execute(
                "SELECT run_id FROM analysis.runs WHERE task = 'bot'::analysis.task "
                "AND doc_id = %s AND is_current", (self.doc_id,)
            ).fetchall()
        self.assertEqual(len(current_rows), 1)
        self.assertIn(current_rows[0]["run_id"], results.values())

    # -- failed-run is_current rule ---------------------------------------

    def test_failed_run_does_not_take_over_is_current_from_succeeded_predecessor(self):
        first = store.open_run("bot", doc_id=self.doc_id, model_id="m1",
                                prompt_version=None, inference_method="deterministic")
        first_id = first.finish("done", confidence=0.5)

        second = store.open_run("bot", doc_id=self.doc_id, model_id="m2",
                                 prompt_version=None, inference_method="deterministic")
        second_id = second.finish("failed", error="LLM timeout")

        self.assertEqual(self._current_run_id("bot", self.doc_id), first_id)
        with store.db.connection() as conn:
            failed_row = conn.execute(
                "SELECT is_current, status, raw_response, error FROM analysis.runs WHERE run_id = %s",
                (second_id,),
            ).fetchone()
        self.assertFalse(failed_row["is_current"])
        self.assertEqual(failed_row["status"], "failed")
        self.assertEqual(failed_row["error"], "LLM timeout")
        self.assertIsNone(failed_row["raw_response"])

    def test_failed_run_as_first_ever_run_leaves_no_current_row(self):
        handle = store.open_run("bot", doc_id=self.doc_id, model_id="m1",
                                 prompt_version=None, inference_method="deterministic")
        run_id = handle.finish("failed", error="boom")
        self.assertIsNone(self._current_run_id("bot", self.doc_id))
        self.assertEqual(self._run_count("bot", doc_id=self.doc_id), 1)
        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT error, raw_response FROM analysis.runs WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertEqual(row["error"], "boom")
        self.assertIsNone(row["raw_response"])

    def test_error_and_raw_response_do_not_mix(self):
        """error and raw_response are independent columns -- passing both
        must not fold one into the other (the no-mixing rule)."""
        handle = store.open_run("bot", doc_id=self.doc_id, model_id="m1",
                                 prompt_version=None, inference_method="deterministic")
        run_id = handle.finish("failed", raw_response={"partial": "output"}, error="boom")

        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT error, raw_response FROM analysis.runs WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertEqual(row["error"], "boom")
        self.assertEqual(row["raw_response"], {"partial": "output"})
        self.assertNotIn("error", row["raw_response"])

    def test_failed_run_discards_accumulated_results(self):
        handle = store.open_run("bot", doc_id=self.doc_id, model_id="m1",
                                 prompt_version=None, inference_method="deterministic")
        handle.save_bot_signals(store.BotSignalsRow(label="bot", score=0.9))
        run_id = handle.finish("failed", error="boom")

        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT * FROM analysis.bot_signals WHERE run_id = %s", (run_id,)
            ).fetchone()
        self.assertIsNone(row)

    # -- prompt_version registration ---------------------------------------

    def test_register_prompt_version_idempotent_upsert(self):
        store.register_prompt_version("bot_v1", "bot", "first system prompt", "template A")
        store.register_prompt_version("bot_v1", "bot", "second system prompt", None)

        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT system_prompt, user_prompt_template, task FROM analysis.prompt_versions "
                "WHERE prompt_version = 'bot_v1'"
            ).fetchone()
        # system_prompt is overwritten by the later call; user_prompt_template
        # survives because the later call passed None (COALESCE keeps the old value).
        self.assertEqual(row["system_prompt"], "second system prompt")
        self.assertEqual(row["user_prompt_template"], "template A")
        self.assertEqual(row["task"], "bot")

        with store.db.connection() as conn:
            count = conn.execute(
                "SELECT count(*) AS n FROM analysis.prompt_versions WHERE prompt_version = 'bot_v1'"
            ).fetchone()
        self.assertEqual(count["n"], 1)


if __name__ == "__main__":
    unittest.main()
