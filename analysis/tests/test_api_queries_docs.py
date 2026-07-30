"""
Tests for analysis/src/api/queries/docs.py -- Phase 9 strictly-live
universal document drill-down. Integration-only (the module is pure SQL
assembly, no computation worth unit-testing in isolation), gated on
CIVIC_TEST_DATABASE_URL like every other integration TestCase in this repo.
"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timedelta, timezone

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.api.queries import docs as docs_queries
from analysis.src.results import store
from analysis.tests import pg_fixture


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class GetDocumentIntegrationTests(unittest.TestCase):
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
                "TRUNCATE analysis.citations, analysis.bot_signals, analysis.claims, "
                "analysis.propaganda_techniques, analysis.propaganda_results, "
                "analysis.target_mentions, "
                "analysis.sentiment_results, analysis.runs, analysis.prompt_versions, "
                "corpus.x_posts, corpus.reddit_posts, corpus.news_articles, "
                "corpus.documents, corpus.authors, corpus.entities, "
                "raw.articles, raw.pages RESTART IDENTITY CASCADE"
            )

    def _seed_doc(self, natural_key: str, *, source_type: str = "news", published_at=None) -> int:
        with store.db.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, title, body, source_url, "
                " raw_hash, etl_version) "
                "VALUES (%s::corpus.source_type, %s, %s, 'a title', 'test body', "
                "        'http://example.com/' || %s, 'deadbeef', 'test') RETURNING doc_id",
                (source_type, natural_key, published_at or datetime.now(timezone.utc), natural_key),
            ).fetchone()
            return row["doc_id"]

    def test_unknown_doc_id_returns_none(self):
        self.assertIsNone(docs_queries.get_document(999999))

    def test_core_fields_and_admission_class(self):
        doc_id = self._seed_doc("doc-core")
        payload = docs_queries.get_document(doc_id)
        self.assertEqual(payload["doc_id"], doc_id)
        self.assertEqual(payload["source_type"], "news")
        self.assertEqual(payload["admission_class"], "sampled")
        self.assertTrue(payload["source_url"].startswith("http://"))
        self.assertEqual(payload["analysis_results"], [])
        self.assertEqual(payload["citations_out"], [])
        self.assertEqual(payload["citations_in"], [])

    def test_resolves_doc_older_than_any_window(self):
        """The whole point of this endpoint: no time predicate at all. A
        doc published five years ago -- far past even a 90d window, let
        alone any preset -- must still resolve in full."""
        ancient = datetime.now(timezone.utc) - timedelta(days=5 * 365)
        doc_id = self._seed_doc("doc-ancient", published_at=ancient)
        payload = docs_queries.get_document(doc_id)
        self.assertEqual(payload["doc_id"], doc_id)
        self.assertEqual(payload["published_at"], ancient)

    def test_news_subtype_extras(self):
        doc_id = self._seed_doc("doc-news-extras", source_type="news")
        url_canon = "http://example.com/doc-news-extras"
        with store.db.connection() as conn:
            # corpus.news_articles.url_canon FKs to raw.articles -- seed the
            # raw-layer rows first so the FK is satisfiable.
            conn.execute(
                "INSERT INTO raw.pages (url_canon, url_raw, domain) VALUES (%s, %s, 'example.com')",
                (url_canon, url_canon),
            )
            conn.execute(
                "INSERT INTO raw.articles (url_canon, fetched_at, raw_hash, extraction_version) "
                "VALUES (%s, now(), 'deadbeef', 'test')",
                (url_canon,),
            )
            conn.execute(
                "INSERT INTO corpus.news_articles (doc_id, url_canon, domain) "
                "VALUES (%s, %s, 'example.com')",
                (doc_id, url_canon),
            )
        payload = docs_queries.get_document(doc_id)
        self.assertEqual(payload["news_extras"]["domain"], "example.com")
        self.assertIsNone(payload["reddit_extras"])
        self.assertIsNone(payload["x_post_extras"])

    def test_analysis_result_per_task_carries_traceability_and_fields(self):
        doc_id = self._seed_doc("doc-text-run")
        handle = store.open_run(
            "text", doc_id=doc_id, model_id="gemini-3.5-flash",
            prompt_version="text_v1", inference_method="llm",
        )
        handle.save_sentiment(store.SentimentRow(label="negative", score=0.6))
        handle.finish("done", confidence=0.75)

        payload = docs_queries.get_document(doc_id)
        self.assertEqual(len(payload["analysis_results"]), 1)
        result = payload["analysis_results"][0]
        self.assertEqual(result["task"], "text")
        self.assertEqual(result["model_id"], "gemini-3.5-flash")
        self.assertEqual(result["prompt_version"], "text_v1")
        self.assertEqual(result["confidence"], 0.75)
        self.assertEqual(result["fields"]["sentiment"]["label"], "negative")

    def test_non_current_run_is_excluded(self):
        doc_id = self._seed_doc("doc-superseded")
        first = store.open_run(
            "bot", doc_id=doc_id, model_id="bot_v1", inference_method="deterministic",
        )
        first_id = first.finish("done", confidence=0.5).run_id
        second = store.open_run(
            "bot", doc_id=doc_id, model_id="bot_v1", inference_method="deterministic",
        )
        second.finish("done", confidence=0.9)

        payload = docs_queries.get_document(doc_id)
        bot_results = [r for r in payload["analysis_results"] if r["task"] == "bot"]
        self.assertEqual(len(bot_results), 1)
        self.assertNotEqual(bot_results[0]["run_id"], first_id)

    def test_citations_out_includes_url_only_target(self):
        source_doc = self._seed_doc("doc-citer")
        target_doc = self._seed_doc("doc-cited")
        with store.db.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.citations (source_doc_id, target_doc_id, link_type) "
                "VALUES (%s, %s, 'quote'::analysis.link_type)",
                (source_doc, target_doc),
            )
            conn.execute(
                "INSERT INTO analysis.citations (source_doc_id, target_url, link_type) "
                "VALUES (%s, 'https://example.invalid/never-ingested', 'url_citation'::analysis.link_type)",
                (source_doc,),
            )
        payload = docs_queries.get_document(source_doc)
        out = payload["citations_out"]
        self.assertEqual(len(out), 2)
        resolved = next(c for c in out if c["doc_id"] == target_doc)
        self.assertEqual(resolved["link_type"], "quote")
        self.assertIsNotNone(resolved["source_url"])
        url_only = next(c for c in out if c["target_url"] is not None)
        self.assertIsNone(url_only["doc_id"])
        self.assertEqual(url_only["target_url"], "https://example.invalid/never-ingested")

    def test_citations_in_resolves_citing_doc(self):
        source_doc = self._seed_doc("doc-citer-2")
        target_doc = self._seed_doc("doc-cited-2")
        with store.db.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.citations (source_doc_id, target_doc_id, link_type) "
                "VALUES (%s, %s, 'reply'::analysis.link_type)",
                (source_doc, target_doc),
            )
        payload = docs_queries.get_document(target_doc)
        self.assertEqual(len(payload["citations_in"]), 1)
        self.assertEqual(payload["citations_in"][0]["doc_id"], source_doc)
        self.assertEqual(payload["citations_in"][0]["link_type"], "reply")


if __name__ == "__main__":
    unittest.main()
