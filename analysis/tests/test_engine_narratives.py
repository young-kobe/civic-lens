"""
Tests for analysis/src/engine/narrative_clustering.py -- Postgres redesign
Phase 6, Wave 3.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_engine_claims.py, test_engine_citations.py):

  1. Pure-core tests (no DB) -- plan_clustering() against constructed
     PendingClaim/ExistingAnchor fixtures with pre-set tokens/embeddings
     (no embed_fn needed: embedding happens in run(), not the planner).
     Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001+0002 applied. Skipped (never
     failed) when the env var is absent.
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
        tokens=nc.tokenize_claim(text),
        embedding=embedding,
    )


def _anchor(narrative_id, text, embedding=None, first_seen_at=None, first_seen_doc_id=1):
    return nc.ExistingAnchor(
        narrative_id=narrative_id,
        anchor_text=text,
        tokens=nc.tokenize_claim(text),
        embedding=embedding,
        first_seen_at=first_seen_at or _T0,
        first_seen_doc_id=first_seen_doc_id,
    )


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TokenizeAndCompareTests(unittest.TestCase):
    def test_tokenize_drops_stopwords_and_short_tokens(self):
        tokens = nc.tokenize_claim("The president is a strong leader of it")
        self.assertNotIn("the", tokens)
        self.assertNotIn("is", tokens)
        self.assertNotIn("a", tokens)  # len(1) dropped too
        self.assertIn("president", tokens)
        self.assertIn("strong", tokens)
        self.assertIn("leader", tokens)

    def test_jaccard_empty_sets_is_zero(self):
        self.assertEqual(nc.jaccard(set(), {"a"}), 0.0)
        self.assertEqual(nc.jaccard(set(), set()), 0.0)

    def test_jaccard_identical_sets_is_one(self):
        self.assertEqual(nc.jaccard({"a", "b"}, {"a", "b"}), 1.0)

    def test_cosine_orthogonal_vectors_is_zero(self):
        self.assertEqual(nc.cosine([1.0, 0.0], [0.0, 1.0]), 0.0)

    def test_cosine_identical_vectors_is_one(self):
        self.assertAlmostEqual(nc.cosine([1.0, 2.0], [1.0, 2.0]), 1.0)

    def test_cosine_mismatched_length_is_zero(self):
        self.assertEqual(nc.cosine([1.0], [1.0, 2.0]), 0.0)


class TwoSimilarClaimsClusterTests(unittest.TestCase):
    def test_two_similar_claims_on_different_docs_form_one_narrative(self):
        pending = [
            _claim(1, doc_id=1, text="Trump won Pennsylvania decisively in the election"),
            _claim(2, doc_id=2, text="Trump won Pennsylvania decisively in the vote"),
        ]
        plan = nc.plan_clustering(
            pending, [], mode="jaccard", jaccard_threshold=0.3, embedding_threshold=0.65,
        )
        self.assertEqual(len(plan.new_narratives), 1)
        self.assertEqual(plan.suppressed_count, 0)
        self.assertEqual(plan.suppressed_claims, 0)
        member_docs = {m.doc_id for m in plan.new_narratives[0].members}
        self.assertEqual(member_docs, {1, 2})
        # Anchor is the founding (first-processed) claim, not a drifting centroid.
        self.assertEqual(plan.new_narratives[0].anchor_claim.claim_id, 1)


class DissimilarClaimsStayApartTests(unittest.TestCase):
    def test_two_unrelated_claims_produce_no_narrative(self):
        pending = [
            _claim(1, doc_id=1, text="Congress passed a new infrastructure funding bill"),
            _claim(2, doc_id=2, text="Local school board debates cafeteria menu changes"),
        ]
        plan = nc.plan_clustering(
            pending, [], mode="jaccard", jaccard_threshold=0.3, embedding_threshold=0.65,
        )
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(plan.suppressed_count, 2)
        self.assertEqual(plan.suppressed_claims, 2)


class SingletonSuppressionTests(unittest.TestCase):
    def test_single_unmatched_claim_is_suppressed_not_materialized(self):
        pending = [_claim(1, doc_id=1, text="A wholly unique claim about municipal zoning")]
        plan = nc.plan_clustering(
            pending, [], mode="jaccard", jaccard_threshold=0.3, embedding_threshold=0.65,
        )
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(plan.existing_assignments, [])
        self.assertEqual(plan.suppressed_count, 1)
        self.assertEqual(plan.suppressed_claims, 1)

    def test_min_support_above_two_requires_more_docs(self):
        pending = [
            _claim(1, doc_id=1, text="Senate confirms new cabinet nominee today"),
            _claim(2, doc_id=2, text="Senate confirms new cabinet nominee this week"),
        ]
        plan = nc.plan_clustering(
            pending, [], mode="jaccard", jaccard_threshold=0.3, embedding_threshold=0.65,
            min_support=3,
        )
        # Two matching docs is still below a min_support=3 bar.
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(plan.suppressed_count, 1)
        self.assertEqual(plan.suppressed_claims, 2)


class IncrementalExtendTests(unittest.TestCase):
    def test_claim_matching_existing_anchor_extends_it_not_a_new_narrative(self):
        anchor = _anchor(narrative_id=42, text="Senate passes the annual budget resolution")
        pending = [_claim(1, doc_id=5, text="Senate passes the annual budget resolution again")]
        plan = nc.plan_clustering(
            pending, [anchor], mode="jaccard", jaccard_threshold=0.3, embedding_threshold=0.65,
        )
        self.assertEqual(plan.new_narratives, [])
        self.assertEqual(len(plan.existing_assignments), 1)
        self.assertEqual(plan.existing_assignments[0].narrative_id, 42)
        self.assertEqual(plan.existing_assignments[0].claim.doc_id, 5)


class EmbeddingModeTests(unittest.TestCase):
    def test_claims_with_similar_embeddings_cluster_via_cosine(self):
        pending = [
            _claim(1, doc_id=1, text="completely different wording one", embedding=[1.0, 0.0]),
            _claim(2, doc_id=2, text="completely different wording two", embedding=[0.99, 0.01]),
        ]
        plan = nc.plan_clustering(
            pending, [], mode="embedding", jaccard_threshold=0.3, embedding_threshold=0.9,
        )
        self.assertEqual(len(plan.new_narratives), 1)
        self.assertEqual({m.doc_id for m in plan.new_narratives[0].members}, {1, 2})

    def test_claim_missing_embedding_falls_back_to_jaccard_against_all_anchors(self):
        # No embedding computed (e.g. embed() failed) -- mode is "embedding"
        # but this claim must still be comparable via jaccard, per-claim.
        anchor = _anchor(narrative_id=7, text="Border security funding bill advances in Congress")
        pending = [
            _claim(
                1, doc_id=9,
                text="Border security funding bill advances in Congress today",
                embedding=None,
            )
        ]
        plan = nc.plan_clustering(
            pending, [anchor], mode="embedding", jaccard_threshold=0.3, embedding_threshold=0.9,
        )
        self.assertEqual(len(plan.existing_assignments), 1)
        self.assertEqual(plan.existing_assignments[0].narrative_id, 7)

    def test_embedding_only_claim_never_compared_against_jaccard_only_anchor(self):
        # Anchor has no embedding (created under jaccard mode, say); a claim
        # with a real embedding must skip it entirely, not fall back to
        # jaccard for that one anchor (mixing thresholds is never done).
        anchor = _anchor(narrative_id=3, text="identical shared wording here", embedding=None)
        pending = [
            _claim(1, doc_id=2, text="identical shared wording here", embedding=[1.0, 0.0]),
        ]
        plan = nc.plan_clustering(
            pending, [anchor], mode="embedding", jaccard_threshold=0.3, embedding_threshold=0.9,
        )
        self.assertEqual(plan.existing_assignments, [])
        # Falls through to founding its own (suppressed, alone) group.
        self.assertEqual(plan.suppressed_count, 1)


class _NoEmbedBackend:
    """Stand-in for LLMClient with a backend whose class never overrides
    BaseLLMClient.embed's no-op default (e.g. GeminiClient before this
    change, or any future backend that regresses)."""

    supports_embedding = False


class MisconfiguredEmbeddingModeTests(unittest.TestCase):
    """Regression guard for the exact bug this workstream fixes: mode=
    'embedding' paired with a backend that cannot embed at all must fail
    loudly, not silently cluster on jaccard while settings/logs still say
    'embedding'. No DB is touched -- run() must raise before reaching
    _open_clustering_run(), so this test needs no CIVIC_TEST_DATABASE_URL
    and always runs."""

    def test_unsupported_backend_raises_instead_of_silently_falling_back(self):
        with patch(
            "analysis.src.llm.client.get_client", return_value=_NoEmbedBackend()
        ):
            with self.assertRaises(RuntimeError):
                nc.run(mode="embedding")

    def test_check_is_bypassed_when_caller_injects_embed_fn_directly(self):
        # Passing embed_fn explicitly (as tests/callers may) skips backend
        # resolution entirely -- _resolve_embed_fn, and thus the
        # misconfiguration check, must never be reached even against a
        # backend that can't embed.
        fake_get_client = MagicMock()
        with patch("analysis.src.llm.client.get_client", fake_get_client):
            try:
                nc.run(mode="embedding", embed_fn=lambda text: None)
            except Exception:
                # Anything past the check (e.g. no test database
                # configured) is irrelevant here -- only whether the
                # resolver path was reached is being asserted.
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
    """Live-run against a real Postgres with 0001+0002 applied."""

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
        doc_a, claim_a = self._seed_claim("Trump won Pennsylvania decisively in the election")
        doc_b, _claim_b = self._seed_claim("Trump won Pennsylvania decisively in the vote")
        self._seed_claim("A wholly unrelated claim about municipal zoning")

        result = nc.run(mode="jaccard")

        self.assertEqual(result["mode"], "jaccard")
        self.assertEqual(result["narratives_created"], 1)
        self.assertEqual(result["docs_touched"], 2)
        self.assertEqual(result["suppressed_groups"], 1)
        self.assertEqual(result["suppressed_claims"], 1)

        runs = self._clustering_runs()
        self.assertEqual(len(runs), 1)
        self.assertEqual(runs[0]["mode"], "jaccard")
        self.assertIsNotNone(runs[0]["completed_at"])
        self.assertEqual(runs[0]["doc_count"], 2)

        narratives = self._narratives()
        self.assertEqual(len(narratives), 1)
        narrative = narratives[0]
        self.assertEqual(narrative["clustering_run_id"], runs[0]["clustering_run_id"])
        self.assertEqual(narrative["anchor_claim_id"], claim_a)

        docs = self._narrative_docs()
        # Singleton claim's doc got no narrative_docs row at all -- only the
        # two matching docs are present.
        self.assertEqual({d["doc_id"] for d in docs}, {doc_a, doc_b})
        # Both founding rows carry the founding run's id.
        self.assertTrue(all(d["added_by_run"] == runs[0]["clustering_run_id"] for d in docs))

        result2 = nc.run(mode="jaccard")
        # The singleton doc is still pending next run (no narrative_docs row
        # excludes it) -- reprocessed, still alone, still suppressed.
        self.assertEqual(result2["narratives_created"], 0)
        self.assertEqual(result2["suppressed_groups"], 1)

    def test_second_run_extends_existing_narrative_not_a_duplicate(self):
        self._seed_claim("Senate passes the annual budget resolution today")
        self._seed_claim("Senate passes the annual budget resolution now")
        result1 = nc.run(mode="jaccard")
        founding_run_id = result1["clustering_run_id"]
        narratives_before = self._narratives()
        self.assertEqual(len(narratives_before), 1)
        narrative_id = narratives_before[0]["narrative_id"]

        doc_c, _ = self._seed_claim("Senate passes the annual budget resolution finally")
        result2 = nc.run(mode="jaccard")
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
        self._seed_claim("Congress passed a new infrastructure funding bill today")
        self._seed_claim("Congress passed a new infrastructure funding bill yesterday")
        nc.run(mode="jaccard")

        narratives_before = self._narratives()
        docs_before = self._narrative_docs()

        result2 = nc.run(mode="jaccard")  # no new claims seeded

        self.assertEqual(result2["claims_considered"], 0)
        self.assertEqual(result2["narratives_created"], 0)
        self.assertEqual(self._narratives(), narratives_before)
        self.assertEqual(self._narrative_docs(), docs_before)
        # clustering_runs provenance still grows -- that's expected, not a
        # violation of idempotency on the narrative tables.
        self.assertEqual(len(self._clustering_runs()), 2)

    def test_singleton_claim_produces_no_narrative_row(self):
        self._seed_claim("A completely one-off claim nobody else made")

        result = nc.run(mode="jaccard")

        self.assertEqual(result["narratives_created"], 0)
        self.assertEqual(result["suppressed_groups"], 1)
        self.assertEqual(self._narratives(), [])
        self.assertEqual(self._narrative_docs(), [])
        runs = self._clustering_runs()
        self.assertEqual(runs[0]["doc_count"], 0)

    def test_first_seen_at_is_earliest_published_at_among_members(self):
        earlier = datetime(2026, 1, 1, tzinfo=timezone.utc)
        later = datetime(2026, 6, 1, tzinfo=timezone.utc)
        self._seed_claim("Federal court blocks the new voting law", published_at=later)
        self._seed_claim("Federal court blocks the new voting law today", published_at=earlier)

        nc.run(mode="jaccard")

        narrative = self._narratives()[0]
        self.assertEqual(narrative["first_seen_at"], earlier)

    # -- embedding mode, injected fake embed_fn (no live Ollama needed) ------

    def test_embedding_mode_with_injected_embed_fn_clusters_and_stores_vector(self):
        doc_a, claim_a = self._seed_claim("alpha bravo charlie delta")
        doc_b, _ = self._seed_claim("echo foxtrot golf hotel")

        vectors = {
            "alpha bravo charlie delta": [1.0, 0.0],
            "echo foxtrot golf hotel": [0.98, 0.02],
        }
        result = nc.run(
            mode="embedding", embedding_threshold=0.9,
            embed_fn=lambda text: vectors.get(text),
        )

        self.assertEqual(result["mode"], "embedding")
        self.assertEqual(result["narratives_created"], 1)
        self.assertEqual(result["embedding_fallbacks"], 0)
        narrative = self._narratives()[0]
        self.assertEqual(narrative["anchor_claim_id"], claim_a)
        self.assertIsNotNone(narrative["anchor_embedding"])

    def test_embedding_failure_is_counted_and_degrades_to_jaccard_for_that_claim(self):
        self._seed_claim("Wildfire smoke blankets the entire east coast region")
        self._seed_claim("Wildfire smoke blankets the entire east coast area")

        # embed_fn returns None for everything -- simulates a down backend;
        # the claims still cluster via jaccard fallback, never a fabricated vector.
        result = nc.run(
            mode="embedding", jaccard_threshold=0.3,
            embed_fn=lambda text: None,
        )

        self.assertEqual(result["narratives_created"], 1)
        self.assertGreaterEqual(result["embedding_fallbacks"], 2)
        narrative = self._narratives()[0]
        self.assertIsNone(narrative["anchor_embedding"])


if __name__ == "__main__":
    unittest.main()
