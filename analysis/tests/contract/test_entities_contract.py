"""
Contract tests for GET /api/v1/entity-posts and GET
/api/v1/entity-profile/{entity_id}: mounts entities.router on a throwaway
FastAPI app, seeds a deterministic scenario, and snapshots the JSON
response via the shared harness (conftest.assert_snapshot_match).
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timezone

from fastapi import FastAPI
from fastapi.testclient import TestClient

from analysis.src.api.routers.entities import router as entities_router
from analysis.tests.contract import conftest


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class EntitiesContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        conftest.pg_fixture.reset_schema(cls._dsn)
        app = FastAPI()
        app.include_router(entities_router, prefix="/api/v1")
        cls._client = TestClient(app)

    def setUp(self):
        prev_url = conftest.pg_fixture.begin_test(self._dsn)
        self.addCleanup(conftest.pg_fixture.end_test, prev_url)
        self._truncate_mutable()
        self._entity_id = self._seed()

    def _truncate_mutable(self) -> None:
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute("TRUNCATE corpus.entities, corpus.authors, corpus.documents RESTART IDENTITY CASCADE")

    def _seed(self) -> int:
        from analysis.src.common import db
        # An official_record post older than every preset window -- it must
        # still surface on the un-windowed drill-down and the all-time
        # profile (owner decision 2026-07-24: historical data stays
        # queryable forever).
        old_published = datetime(2024, 1, 1, 12, 0, tzinfo=timezone.utc)
        with db.connection() as conn:
            entity = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean) "
                "VALUES ('sen-example', 'official'::corpus.entity_kind, 'Sen. Example', "
                "'democrat'::corpus.political_lean) RETURNING entity_id"
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
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES ('x_post'::corpus.source_type, 'tweet-old', %s, %s, 'An old post', 'body', "
                "'https://example.com/tweet-old', 'h' || repeat('0', 63), 'test', "
                "'official_record'::corpus.admission_class) RETURNING doc_id",
                (old_published, author),
            ).fetchone()["doc_id"]
            conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, 'gemini-3.5-flash', "
                "'llm'::analysis.inference_method, 0.9, true)",
                (doc,),
            )
            return entity

    def test_entity_posts_unwindowed_shape_snapshot(self):
        response = self._client.get(
            "/api/v1/entity-posts", params={"entity_id": self._entity_id, "window": "all"},
        )
        self.assertEqual(response.status_code, 200)
        body = response.json()
        self.assertEqual(body["total"], 1)
        self.assertEqual(body["items"][0]["admissionClass"], "official_record")
        conftest.assert_snapshot_match("entity_posts_basic", body)

    def test_entity_posts_90d_window_excludes_the_old_post(self):
        response = self._client.get(
            "/api/v1/entity-posts", params={"entity_id": self._entity_id, "window": "90d"},
        )
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["total"], 0)

    def test_entity_profile_all_time_shape_snapshot(self):
        response = self._client.get(f"/api/v1/entity-profile/{self._entity_id}")
        self.assertEqual(response.status_code, 200)
        body = response.json()
        self.assertIsNone(body["range"]["start"])
        self.assertIsNone(body["range"]["end"])
        self.assertEqual(body["analyzedDocCounts"], {"sampled": 0, "officialRecord": 1})
        conftest.assert_snapshot_match("entity_profile_basic", body)

    def test_unknown_entity_id_is_404(self):
        response = self._client.get("/api/v1/entity-profile/999999")
        self.assertEqual(response.status_code, 404)


if __name__ == "__main__":
    unittest.main()
