"""
Tests for analysis/src/engine/narrative_clustering.py -- Postgres redesign
Phase 6, Wave 3.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_engine_claims.py, test_engine_citations.py):

  1. Pure-core tests (no DB) -- plan_clustering() against constructed
     PendingClaim/ExistingAnchor fixtures with pre-set embeddings (no
     embed_fn needed: embedding happens in run(), not the planner).
     Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations applied. Skipped (never failed) when
     the env var is absent.

Clustering is embedding-only (2026-07-26). Vectors here are explicit and
tiny on purpose: the planner's job is "cosine against a threshold", and
tests that spell out the vectors say what similarity is being asserted
instead of hiding it behind a similarity function of their own.
"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timedelta, timezone
from unittest.mock import MagicMock, patch

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine import narrative_clustering as nc
from analysis.tests import pg_fixture


# =============================================================================
# Fixtures shared by Tier 1 tests.
# =============================================================================

_T0 = datetime(2026, 1, 1, tzinfo=timezone.utc)


def _claim(claim_id, doc_id, text, published_at=None, confidence=0.7, embedding=None):
    return nc.PendingClaim(
        claim_id=claim_id,
        doc_id=doc_id,
        claim_text=text,
        confidence=confidence,
        published_at=published_at or _T0 + timedelta(days=doc_id),
        embedding=embedding,
    )


def _anchor(narrative_id, text, embedding=None, first_seen_at=None, first_seen_doc_id=1):
    return nc.ExistingAnchor(
        narrative_id=narrative_id,
        anchor_text=text,
        embedding=embedding,
        first_seen_at=first_seen_at or _T0,
        first_seen_doc_id=first_seen_doc_id,
    )


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class CosineTests(unittest.TestCase):
    def test_cosine_orthogonal_vectors_is_zero(self):
        self.assertEqual(nc.cosine([1.0, 0.0], [0.0, 1.0]), 0.0)

    def test_cosine_identical_vectors_is_one(self):
        self.assertAlmostEqual(nc.cosine([1.0, 2.0], [1.0, 2.0]), 1.0)

    def test_cosine_mismatched_length_is_zero(self):
        self.assertEqual(nc.cosine([1.0], [1.0, 2.0]), 0.0)

    def test_cosine_missing_vector_is_zero(self):
        # A None operand must score 0, never raise and never be treated as
        # a match -- the planner relies on this to skip unembedded rows.
        self.assertEqual(nc.cosine(None, [1.0, 0.0]), 0.0)
        self.assertEqual(nc.cosine([1.0, 0.0], None), 0.0)


class TwoSimilarClaimsClusterTests(unittest.TestCase):
    def test_two_similar_claims_on_different_docs_form_one_narrative(self):
        pending = [
            _claim(1, doc_id=1, text="Trump won Pennsylvania decisively", embedding=[1.0, 0.0]),
            _claim(2, doc_id=2, text="Trump carried Pennsylvania", embedding=[0.99, 0.01]),
        ]
        plan = nc.plan_clustering(pending, [], threshold=0.9)
        self.assertEqual(len(plan.new_narratives), 1)
        self.assertEqual(plan.suppressed_count, 0)
        self.assertEqual(plan.suppressed_claims, 0)
        member_docs = {m.doc_id for m in plan.new_narratives[0].members}
        self.assertEqual(member_docs, {1, 2})
        # Anchor is the founding (first-processed) claim, not a drifting centroid.
        self.assertEqual(plan.new_narratives[0].anchor_claim.claim_id, 1)

    def test_different_wording_same_meaning_still_clusters(self):
        # The whole reason clustering is embedding-only: these two share
        # almost no vocabulary. A lexical comparator would never group them.
        pending = [
            _claim(1, doc_id=1, text="The Senate greenlit the spending package",
                   embedding=[0.0, 1.0]),
            _claim(2, doc_id=2, text="Upper chamber approves budget resolution",
                   embedding=[0.02, 0.99]),
        ]
        plan = nc.plan_clustering(pending, [], threshold=0.9)
        self.assertEqual(len(plan.new_narratives), 1)


class DissimilarClaimsStayApartTests(unittest.TestCase):
    def test_two_unrelated_claims_produce_no_narrative(self):
        pending = [
            _claim(1, doc_id=1, text="Congress passed an infrastructure bill",
                   embedding=[1.0, 0.0]),
            _claim(2, doc_id=2, text="School board debates cafeteria menus",
                   embedding=[0.0, 1.0]),
        ]
        plan = nc.plan_clustering(pending, [], threshold=0.65)
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(plan.suppressed_count, 2)
        self.assertEqual(plan.suppressed_claims, 2)


class SingletonSuppressionTests(unittest.TestCase):
    def test_single_unmatched_claim_is_suppressed_not_materialized(self):
        pending = [_claim(1, doc_id=1, text="A unique claim about zoning", embedding=[1.0, 0.0])]
        plan = nc.plan_clustering(pending, [], threshold=0.65)
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(plan.existing_assignments, [])
        self.assertEqual(plan.suppressed_count, 1)
        self.assertEqual(plan.suppressed_claims, 1)

    def test_min_support_above_two_requires_more_docs(self):
        pending = [
            _claim(1, doc_id=1, text="Senate confirms the nominee today", embedding=[1.0, 0.0]),
            _claim(2, doc_id=2, text="Senate confirms the nominee this week", embedding=[0.99, 0.01]),
        ]
        plan = nc.plan_clustering(pending, [], threshold=0.9, min_support=3)
        # Two matching docs is still below a min_support=3 bar.
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(plan.suppressed_count, 1)
        self.assertEqual(plan.suppressed_claims, 2)


class IncrementalExtendTests(unittest.TestCase):
    def test_claim_matching_existing_anchor_extends_it_not_a_new_narrative(self):
        anchor = _anchor(narrative_id=42, text="Senate passes the budget", embedding=[1.0, 0.0])
        pending = [_claim(1, doc_id=5, text="Senate passes the budget again", embedding=[0.99, 0.01])]
        plan = nc.plan_clustering(pending, [anchor], threshold=0.9)
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(len(plan.existing_assignments), 1)
        self.assertEqual(plan.existing_assignments[0].narrative_id, 42)
        self.assertEqual(plan.existing_assignments[0].claim.doc_id, 5)


class UnembeddedRowsAreExcludedTests(unittest.TestCase):
    """The behavior that replaced the jaccard fallback. A row without a
    vector is not comparable, so it is left alone -- it is never measured
    by some other yardstick and slipped into the same narratives table."""

    def test_claim_without_embedding_is_left_unclustered(self):
        # Word-for-word near-identical to the anchor. Under the old lexical
        # fallback this joined narrative 7; now it must not, because the
        # claim carries no vector and no other comparator exists.
        anchor = _anchor(narrative_id=7, text="Border funding bill advances", embedding=[1.0, 0.0])
        pending = [
            _claim(1, doc_id=9, text="Border funding bill advances today", embedding=None),
        ]
        plan = nc.plan_clustering(pending, [anchor], threshold=0.9)
        self.assertEqual(plan.existing_assignments, [])
        self.assertEqual(plan.new_narratives, [])
        # Not even counted as a suppressed group -- it never entered planning.
        self.assertEqual(plan.suppressed_count, 0)

    def test_anchor_without_embedding_is_skipped_not_matched(self):
        anchor = _anchor(narrative_id=3, text="identical shared wording here", embedding=None)
        pending = [_claim(1, doc_id=2, text="identical shared wording here", embedding=[1.0, 0.0])]
        plan = nc.plan_clustering(pending, [anchor], threshold=0.9)
        self.assertEqual(plan.existing_assignments, [])
        # Falls through to founding its own (suppressed, alone) group.
        self.assertEqual(plan.suppressed_count, 1)


class _NoEmbedBackend:
    """Stand-in for LLMClient with a backend whose class never overrides
    BaseLLMClient.embed's no-op default."""

    supports_embedding = False


class BlankEmbeddingModelIsRefusedTests(unittest.TestCase):
    """clustering_runs.embedding_model is what says which model produced a
    run's vectors -- the question you need answered when a model swap
    splits the narrative table into before and after. A blank setting
    cannot answer it, so the stage refuses to start rather than record a
    run nobody can account for."""

    def test_blank_model_raises_before_touching_the_backend(self):
        fake_get_client = MagicMock()
        blank = MagicMock(narrative_embedding_model="", narrative_embedding_threshold=0.65)
        with patch("analysis.src.engine.narrative_clustering.get_settings", return_value=blank), \
                patch("analysis.src.llm.client.get_client", fake_get_client):
            with self.assertRaises(RuntimeError) as ctx:
                nc.run()
        self.assertIn("CIVIC_NARRATIVE_EMBEDDING_MODEL", str(ctx.exception))
        # Refused before any backend work, so nothing was spent finding out.
        fake_get_client.assert_not_called()


class MisconfiguredBackendTests(unittest.TestCase):
    """A backend that cannot embed must fail the stage, not produce
    narratives by another route. No DB is touched -- run() raises before
    reaching _open_clustering_run(), so this needs no
    CIVIC_TEST_DATABASE_URL and always runs."""

    def test_unsupported_backend_raises(self):
        # Model named explicitly so this exercises the backend check, not
        # the blank-model refusal that precedes it.
        with patch("analysis.src.llm.client.get_client", return_value=_NoEmbedBackend()):
            with self.assertRaises(RuntimeError) as ctx:
                nc.run(embedding_model="some-embed-model")
        self.assertIn("does not implement embed()", str(ctx.exception))

    def test_check_is_bypassed_when_caller_injects_embed_fn_directly(self):
        # Passing embed_fn explicitly (as tests/callers may) skips backend
        # resolution entirely -- _resolve_embed_fn, and thus the
        # misconfiguration check, must never be reached.
        fake_get_client = MagicMock()
        with patch("analysis.src.llm.client.get_client", fake_get_client):
            try:
                nc.run(embed_fn=lambda text: None)
            except Exception:
                # Anything past the check (e.g. no test database configured)
                # is irrelevant here -- only whether the resolver path was
                # reached is being asserted.
                pass
        fake_get_client.assert_not_called()


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class RunIntegrationTests(unittest.TestCase):
    """Live-run against a real Postgres with the migrations applied."""

    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn, seed=True)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_all()
        self._next_natural_key = 0

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.narrative_docs, analysis.narratives, "
                "analysis.clustering_runs, analysis.claims, analysis.runs, "
                "analysis.prompt_versions, corpus.documents CASCADE"
            )

    @staticmethod
    def _embedder(vectors):
        """An embed_fn over an explicit text -> vector table. Unknown text
        returns None, which is exactly what a failed embed() looks like."""
        return lambda text: vectors.get(text)

    def _seed_claim(self, claim_text, published_at=None, confidence=0.7):
        """Seed one doc + one done/current 'claims' run + one analysis.claims
        row directly via SQL -- bypasses engine/claims.py on purpose (that
        module's own behavior is out of this file's scope; here we only need
        analysis.claims rows shaped the way the store's traceability contract
        requires). Returns (doc_id, claim_id)."""
        from analysis.src.results import store
        self._next_natural_key += 1
        key = f"doc-{self._next_natural_key}"
        with store.db.connection() as conn:
            doc_row = conn.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, published_at, body, source_url,
                     raw_hash, etl_version)
                VALUES ('news', %s, %s, 'test body', 'http://example.com/' || %s,
                        'deadbeef', 'test')
                RETURNING doc_id
                """,
                (key, published_at or datetime.now(timezone.utc), key),
            ).fetchone()
            doc_id = doc_row["doc_id"]
            run_row = conn.execute(
                """
                INSERT INTO analysis.runs
                    (task, doc_id, status, model_id, inference_method, confidence,
                     is_current, started_at, completed_at)
                VALUES ('claims'::analysis.task, %s, 'done'::analysis.run_status,
                        'test-model', 'llm'::analysis.inference_method, %s, true, now(), now())
                RETURNING run_id
                """,
                (doc_id, confidence),
            ).fetchone()
            claim_row = conn.execute(
                """
                INSERT INTO analysis.claims (run_id, doc_id, claim_text, confidence)
                VALUES (%s, %s, %s, %s)
                RETURNING claim_id
                """,
                (run_row["run_id"], doc_id, claim_text, confidence),
            ).fetchone()
            return doc_id, claim_row["claim_id"]

    def _narratives(self):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute("SELECT * FROM analysis.narratives").fetchall()

    def _narrative_docs(self):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute("SELECT * FROM analysis.narrative_docs").fetchall()

    def _clustering_runs(self):
        from analysis.src.results import store
        with store.db.connection() as conn:
            return conn.execute("SELECT * FROM analysis.clustering_runs").fetchall()

    # -- full run: provenance + FKs + singleton suppression -----------------

    def test_full_run_lands_clustering_run_and_narratives_with_provenance(self):
        a, b, c = "Trump won Pennsylvania", "Trump carried Pennsylvania", "Municipal zoning changes"
        doc_a, claim_a = self._seed_claim(a)
        doc_b, _claim_b = self._seed_claim(b)
        self._seed_claim(c)
        embed = self._embedder({a: [1.0, 0.0], b: [0.99, 0.01], c: [0.0, 1.0]})

        result = nc.run(embed_fn=embed, embedding_threshold=0.9)

        self.assertEqual(result["narratives_created"], 1)
        self.assertEqual(result["docs_touched"], 2)
        self.assertEqual(result["suppressed_groups"], 1)
        self.assertEqual(result["suppressed_claims"], 1)
        self.assertEqual(result["embedding_failures"], 0)

        runs = self._clustering_runs()
        self.assertEqual(len(runs), 1)
        # Provenance says embedding because that is the only mode there is.
        self.assertEqual(runs[0]["mode"], "embedding")
        self.assertEqual(runs[0]["embedding_failures"], 0)
        self.assertIsNotNone(runs[0]["completed_at"])
        self.assertEqual(runs[0]["doc_count"], 2)

        narratives = self._narratives()
        self.assertEqual(len(narratives), 1)
        narrative = narratives[0]
        self.assertEqual(narrative["clustering_run_id"], runs[0]["clustering_run_id"])
        self.assertEqual(narrative["anchor_claim_id"], claim_a)
        self.assertIsNotNone(narrative["anchor_embedding"])

        docs = self._narrative_docs()
        # Singleton claim's doc got no narrative_docs row at all -- only the
        # two matching docs are present.
        self.assertEqual({d["doc_id"] for d in docs}, {doc_a, doc_b})
        # Both founding rows carry the founding run's id.
        self.assertTrue(all(d["added_by_run"] == runs[0]["clustering_run_id"] for d in docs))

        result2 = nc.run(embed_fn=embed, embedding_threshold=0.9)
        # The singleton doc is still pending next run (no narrative_docs row
        # excludes it) -- reprocessed, still alone, still suppressed.
        self.assertEqual(result2["narratives_created"], 0)
        self.assertEqual(result2["suppressed_groups"], 1)

    def test_second_run_extends_existing_narrative_not_a_duplicate(self):
        a, b, c = "Senate passes the budget", "Senate approves the budget", "Senate okays the budget"
        embed = self._embedder({a: [1.0, 0.0], b: [0.99, 0.01], c: [0.98, 0.02]})
        self._seed_claim(a)
        self._seed_claim(b)
        result1 = nc.run(embed_fn=embed, embedding_threshold=0.9)
        founding_run_id = result1["clustering_run_id"]
        narratives_before = self._narratives()
        self.assertEqual(len(narratives_before), 1)
        narrative_id = narratives_before[0]["narrative_id"]

        doc_c, _ = self._seed_claim(c)
        result2 = nc.run(embed_fn=embed, embedding_threshold=0.9)
        extending_run_id = result2["clustering_run_id"]

        self.assertEqual(result2["narratives_created"], 0)
        self.assertEqual(result2["docs_touched"], 1)

        narratives_after = self._narratives()
        self.assertEqual(len(narratives_after), 1)  # still one narrative, not two
        docs = self._narrative_docs()
        matching = [d for d in docs if d["narrative_id"] == narrative_id]
        self.assertEqual(len(matching), 3)
        self.assertIn(doc_c, {d["doc_id"] for d in matching})

        # added_by_run is FK-precise: the two founding docs carry the
        # founding run's id, the extension doc carries the extending run's
        # id -- not approximated via discovered_at against a run's window.
        by_doc = {d["doc_id"]: d["added_by_run"] for d in matching}
        self.assertEqual(by_doc[doc_c], extending_run_id)
        founding_docs = set(by_doc) - {doc_c}
        self.assertEqual(len(founding_docs), 2)
        for doc_id in founding_docs:
            self.assertEqual(by_doc[doc_id], founding_run_id)
        self.assertNotEqual(founding_run_id, extending_run_id)

    def test_idempotent_rerun_is_zero_delta_on_narratives(self):
        a, b = "Congress passed the infrastructure bill", "Congress cleared the infrastructure bill"
        embed = self._embedder({a: [1.0, 0.0], b: [0.99, 0.01]})
        self._seed_claim(a)
        self._seed_claim(b)
        nc.run(embed_fn=embed, embedding_threshold=0.9)

        narratives_before = self._narratives()
        docs_before = self._narrative_docs()

        result2 = nc.run(embed_fn=embed, embedding_threshold=0.9)  # no new claims seeded

        self.assertEqual(result2["claims_considered"], 0)
        self.assertEqual(result2["narratives_created"], 0)
        self.assertEqual(self._narratives(), narratives_before)
        self.assertEqual(self._narrative_docs(), docs_before)
        # clustering_runs provenance still grows -- that's expected, not a
        # violation of idempotency on the narrative tables.
        self.assertEqual(len(self._clustering_runs()), 2)

    def test_singleton_claim_produces_no_narrative_row(self):
        a = "A one-off claim nobody else made"
        self._seed_claim(a)

        result = nc.run(embed_fn=self._embedder({a: [1.0, 0.0]}), embedding_threshold=0.9)

        self.assertEqual(result["narratives_created"], 0)
        self.assertEqual(result["suppressed_groups"], 1)
        self.assertEqual(self._narratives(), [])
        self.assertEqual(self._narrative_docs(), [])
        runs = self._clustering_runs()
        self.assertEqual(runs[0]["doc_count"], 0)

    def test_first_seen_at_is_earliest_published_at_among_members(self):
        earlier = datetime(2026, 1, 1, tzinfo=timezone.utc)
        later = datetime(2026, 6, 1, tzinfo=timezone.utc)
        a, b = "Court blocks the voting law", "Court halts the voting law"
        self._seed_claim(a, published_at=later)
        self._seed_claim(b, published_at=earlier)

        nc.run(
            embed_fn=self._embedder({a: [1.0, 0.0], b: [0.99, 0.01]}),
            embedding_threshold=0.9,
        )

        narrative = self._narratives()[0]
        self.assertEqual(narrative["first_seen_at"], earlier)

    # -- embedding failures ------------------------------------------------

    def test_total_embedding_failure_raises_instead_of_writing_an_empty_result(self):
        """A dead backend or a model name it does not serve fails every
        call. That must not land as "no narratives found" -- an empty result
        is a claim about the corpus, and this run has nothing to say."""
        self._seed_claim("Wildfire smoke blankets the east coast")
        self._seed_claim("Wildfire smoke covers the eastern seaboard")

        with self.assertRaises(RuntimeError) as ctx:
            nc.run(embed_fn=lambda text: None)

        self.assertIn("embeddings failed", str(ctx.exception))
        self.assertEqual(self._narratives(), [])
        # The run row survives with the failure count and no completed_at --
        # provenance for an attempt that produced nothing.
        runs = self._clustering_runs()
        self.assertEqual(len(runs), 1)
        self.assertIsNone(runs[0]["completed_at"])
        self.assertGreaterEqual(runs[0]["embedding_failures"], 2)

    def test_partial_embedding_failure_is_recorded_and_claim_left_unclustered(self):
        """One bad embed does not sink the run, but the count is persisted:
        clustering_runs.mode alone would otherwise imply every row in the
        run was produced the same way."""
        a, b, c = "Senate passes the budget", "Senate approves the budget", "Unembeddable claim"
        self._seed_claim(a)
        self._seed_claim(b)
        self._seed_claim(c)

        result = nc.run(
            embed_fn=self._embedder({a: [1.0, 0.0], b: [0.99, 0.01]}),  # c -> None
            embedding_threshold=0.9,
        )

        self.assertEqual(result["narratives_created"], 1)
        self.assertEqual(result["embedding_failures"], 1)
        runs = self._clustering_runs()
        self.assertEqual(runs[0]["embedding_failures"], 1)
        self.assertIsNotNone(runs[0]["completed_at"])
        # The unembedded claim's doc is absent from narrative_docs entirely.
        self.assertEqual(len(self._narrative_docs()), 2)


if __name__ == "__main__":
    unittest.main()
