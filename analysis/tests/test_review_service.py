"""
Tests for analysis/src/review/service.py -- Phase 9 strictly-live rewrite
of reporting/review.py against Postgres.

Two tiers, matching the repo's established convention
(test_result_store.py, test_api_queries_narratives.py):

  1. Pure validation tests (no DB) -- submit()'s verdict/is_golden checks
     that raise before ever touching a connection. Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with 0001-0004 applied.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.review import service as review_service
from analysis.src.results import store
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure, no DB.
# =============================================================================

class SubmitValidationTests(unittest.TestCase):
    def test_rejects_unknown_verdict(self):
        with self.assertRaises(ValueError):
            review_service.submit(1, "maybe")

    def test_is_golden_without_expected_label_raises(self):
        with self.assertRaises(ValueError):
            review_service.submit(1, "correct", is_golden=True)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ReviewServiceIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_all()
        store.register_prompt_version("text_v1", "text", "system prompt")

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.golden_labels, analysis.evals, analysis.sentiment_results, "
                "analysis.runs, analysis.prompt_versions, corpus.documents, corpus.authors "
                "RESTART IDENTITY CASCADE"
            )

    def _seed_doc(self, natural_key: str, source_type: str = "news") -> int:
        with store.db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, body, source_url, raw_hash, etl_version) "
                "VALUES (%s::corpus.source_type, %s, now(), 'test body', "
                "        'http://example.com/' || %s, 'deadbeef', 'test') RETURNING doc_id",
                (source_type, natural_key, natural_key),
            ).fetchone()
            return row["doc_id"]

    def _seed_author(self) -> int:
        with store.db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id) "
                "VALUES ('x', 'reviewer-author') RETURNING author_id",
            ).fetchone()
            return row["author_id"]

    def _seed_text_run(self, doc_id: int, confidence: float) -> int:
        handle = store.open_run(
            "text", doc_id=doc_id, model_id="gemini-3.5-flash",
            prompt_version="text_v1", inference_method="llm",
        )
        handle.save_sentiment(store.SentimentRow(label="positive", score=0.5))
        return handle.finish("done", confidence=confidence).run_id

    def _seed_account_tier_run(self, author_id: int) -> int:
        handle = store.open_run(
            "account_tier", author_id=author_id, model_id="curated_list_v1",
            inference_method="deterministic",
        )
        return handle.finish("done", confidence=1.0).run_id

    # -- get_queue --------------------------------------------------------

    def test_queue_orders_lowest_confidence_first(self):
        doc_a = self._seed_doc("doc-a")
        doc_b = self._seed_doc("doc-b")
        doc_c = self._seed_doc("doc-c")
        run_hi = self._seed_text_run(doc_a, 0.9)
        run_lo = self._seed_text_run(doc_b, 0.2)
        run_mid = self._seed_text_run(doc_c, 0.5)
        items = review_service.get_queue("text", limit=10)
        self.assertEqual([i["run_id"] for i in items], [run_lo, run_mid, run_hi])

    def test_queue_excludes_reviewed_runs(self):
        doc = self._seed_doc("doc-reviewed")
        run_id = self._seed_text_run(doc, 0.3)
        review_service.submit(run_id, "correct")
        items = review_service.get_queue("text", limit=10)
        self.assertEqual(items, [])

    def test_queue_filters_by_source_type(self):
        news_doc = self._seed_doc("doc-news", source_type="news")
        reddit_doc = self._seed_doc("doc-reddit", source_type="reddit_post")
        self._seed_text_run(news_doc, 0.4)
        reddit_run = self._seed_text_run(reddit_doc, 0.4)
        items = review_service.get_queue("text", source_type="reddit_post", limit=10)
        self.assertEqual([i["run_id"] for i in items], [reddit_run])

    def test_queue_filters_by_confidence_max(self):
        doc_a = self._seed_doc("doc-a")
        doc_b = self._seed_doc("doc-b")
        low_run = self._seed_text_run(doc_a, 0.2)
        self._seed_text_run(doc_b, 0.8)
        items = review_service.get_queue("text", confidence_max=0.5, limit=10)
        self.assertEqual([i["run_id"] for i in items], [low_run])

    def test_queue_item_carries_doc_preview_and_traceability_fields(self):
        doc = self._seed_doc("doc-preview")
        run_id = self._seed_text_run(doc, 0.5)
        item = review_service.get_queue("text", limit=10)[0]
        self.assertEqual(item["run_id"], run_id)
        self.assertEqual(item["model_id"], "gemini-3.5-flash")
        self.assertEqual(item["prompt_version"], "text_v1")
        self.assertEqual(item["doc"]["source_type"], "news")
        self.assertEqual(item["doc"]["admission_class"], "sampled")
        self.assertTrue(item["doc"]["source_url"].startswith("http://"))

    def test_account_tier_task_queue_is_always_empty(self):
        """account_tier runs are author-scoped (no doc_id) -- the queue's
        JOIN to corpus.documents can never match them, so the task is a
        syntactically valid filter that always yields nothing."""
        author_id = self._seed_author()
        self._seed_account_tier_run(author_id)
        items = review_service.get_queue("account_tier", limit=10)
        self.assertEqual(items, [])

    # -- submit -------------------------------------------------------------

    def test_submit_unknown_run_id_raises(self):
        with self.assertRaises(ValueError):
            review_service.submit(999999, "correct")

    def test_submit_replaces_existing_verdict_for_same_run(self):
        doc = self._seed_doc("doc-replace")
        run_id = self._seed_text_run(doc, 0.5)
        review_service.submit(run_id, "incorrect", notes="first pass")
        review_service.submit(run_id, "correct", notes="second look")
        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT verdict::text AS verdict, notes FROM analysis.evals WHERE run_id = %(run_id)s",
                {"run_id": run_id},
            ).fetchall()
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["verdict"], "correct")
        self.assertEqual(rows[0]["notes"], "second look")

    def test_submit_golden_mints_golden_label(self):
        doc = self._seed_doc("doc-golden")
        run_id = self._seed_text_run(doc, 0.5)
        review_service.submit(run_id, "incorrect", is_golden=True, expected_label="negative")
        with store.db.connection() as conn:
            row = conn.execute(
                "SELECT expected_label, task::text AS task FROM analysis.golden_labels "
                "WHERE doc_id = %(doc_id)s",
                {"doc_id": doc},
            ).fetchone()
        self.assertEqual(row["expected_label"], "negative")
        self.assertEqual(row["task"], "text")

    def test_submit_golden_refreshes_existing_golden_label(self):
        doc = self._seed_doc("doc-golden-refresh")
        run_id = self._seed_text_run(doc, 0.5)
        review_service.submit(run_id, "incorrect", is_golden=True, expected_label="negative")
        review_service.submit(run_id, "correct", is_golden=True, expected_label="positive")
        with store.db.connection() as conn:
            rows = conn.execute(
                "SELECT expected_label FROM analysis.golden_labels WHERE doc_id = %(doc_id)s",
                {"doc_id": doc},
            ).fetchall()
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["expected_label"], "positive")

    def test_submit_golden_on_author_scoped_run_raises(self):
        author_id = self._seed_author()
        run_id = self._seed_account_tier_run(author_id)
        with self.assertRaises(ValueError):
            review_service.submit(run_id, "correct", is_golden=True, expected_label="affiliated")

    # -- get_stats / get_public_accuracy -------------------------------------

    def test_stats_reports_counts_and_accuracy(self):
        doc_a = self._seed_doc("doc-a")
        doc_b = self._seed_doc("doc-b")
        doc_c = self._seed_doc("doc-c")
        run_a = self._seed_text_run(doc_a, 0.9)
        run_b = self._seed_text_run(doc_b, 0.8)
        self._seed_text_run(doc_c, 0.7)  # left unreviewed
        review_service.submit(run_a, "correct")
        review_service.submit(run_b, "incorrect")
        stats = review_service.get_stats(task="text")
        row = stats["per_task"][0]
        self.assertEqual(row["task"], "text")
        self.assertEqual(row["total_runs"], 3)
        self.assertEqual(row["reviewed"], 2)
        self.assertEqual(row["correct"], 1)
        self.assertEqual(row["incorrect"], 1)
        self.assertAlmostEqual(row["accuracy_pct"], 50.0)

    def test_public_accuracy_withholds_percentage_below_floor(self):
        floor = review_service.MIN_PUBLIC_REVIEW_N
        for i in range(3):
            doc = self._seed_doc(f"doc-thin-{i}")
            run_id = self._seed_text_run(doc, 0.5)
            review_service.submit(run_id, "correct")
        result = review_service.get_public_accuracy()
        row = next(r for r in result["perTask"] if r["taskType"] == "text")
        self.assertEqual(row["scored"], 3)
        self.assertLess(row["scored"], floor)
        self.assertTrue(row["lowSample"])
        self.assertIsNone(row["accuracyPct"])
        self.assertEqual(result["minReviewN"], floor)

    def test_public_accuracy_publishes_percentage_at_or_above_floor(self):
        floor = review_service.MIN_PUBLIC_REVIEW_N
        for i in range(floor):
            doc = self._seed_doc(f"doc-thick-{i}")
            run_id = self._seed_text_run(doc, 0.5)
            review_service.submit(run_id, "correct")
        result = review_service.get_public_accuracy()
        row = next(r for r in result["perTask"] if r["taskType"] == "text")
        self.assertFalse(row["lowSample"])
        self.assertEqual(row["accuracyPct"], 100.0)


if __name__ == "__main__":
    unittest.main()
