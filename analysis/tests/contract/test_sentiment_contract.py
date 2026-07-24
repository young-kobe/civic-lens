"""
Contract test for GET /api/v1/sentiment: mounts sentiment.router on a
throwaway FastAPI app, seeds a deterministic scenario, and snapshots the
JSON response via the shared harness (conftest.assert_snapshot_match).
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timezone

from fastapi import FastAPI
from fastapi.testclient import TestClient

from analysis.src.api.routers.sentiment import router as sentiment_router
from analysis.tests.contract import conftest


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class SentimentContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        conftest.pg_fixture.reset_schema(cls._dsn)
        app = FastAPI()
        app.include_router(sentiment_router, prefix="/api/v1")
        cls._client = TestClient(app)

    def setUp(self):
        prev_url = conftest.pg_fixture.begin_test(self._dsn)
        self.addCleanup(conftest.pg_fixture.end_test, prev_url)
        self._truncate_mutable()
        self._seed()

    def _truncate_mutable(self) -> None:
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute("TRUNCATE corpus.entities, corpus.authors, corpus.documents RESTART IDENTITY CASCADE")

    def _seed(self) -> None:
        from analysis.src.common import db
        published_at = datetime(2026, 6, 1, 14, 0, tzinfo=timezone.utc)  # fixed -- snapshot determinism
        with db.connection() as conn:
            entity = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean) "
                "VALUES ('sen-example', 'official'::corpus.entity_kind, 'Sen. Example', "
                "'republican'::corpus.political_lean) RETURNING entity_id"
            ).fetchone()["entity_id"]
            author = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, 'senexample', 'senexample') RETURNING author_id"
            ).fetchone()["author_id"]
            conn.execute(
                "INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at) "
                "VALUES (%s, 'elected_official'::corpus.author_tier, "
                "'curated_list'::corpus.classification_method, %s, now())",
                (author, entity),
            )
            doc = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, author_id, title, body, "
                " source_url, raw_hash, etl_version) "
                "VALUES ('x_post'::corpus.source_type, 'tweet-1', %s, %s, 'A tariff plan', 'body', "
                "'https://example.com/tweet-1', 'h' || repeat('0', 63), 'test') RETURNING doc_id",
                (published_at, author),
            ).fetchone()["doc_id"]
            run = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, 'gemini-3.5-flash', "
                "'llm'::analysis.inference_method, 0.9, true) RETURNING run_id",
                (doc,),
            ).fetchone()["run_id"]
            conn.execute(
                "INSERT INTO analysis.sentiment_results (run_id, label) VALUES (%s, 'positive'::analysis.sentiment_label)",
                (run,),
            )
            conn.execute(
                "INSERT INTO analysis.favorability_stances (run_id, entity_id, stance) "
                "VALUES (%s, %s, 'favorable'::analysis.favorability_label)",
                (run, entity),
            )

    def test_sentiment_panel_shape_snapshot(self):
        response = self._client.get("/api/v1/sentiment", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        body = response.json()
        self.assertIn("overview", body)
        self.assertIn("range", body)
        self.assertEqual(body["range"]["modelIds"], ["gemini-3.5-flash"])
        conftest.assert_snapshot_match("sentiment_panel_basic", body)


if __name__ == "__main__":
    unittest.main()
