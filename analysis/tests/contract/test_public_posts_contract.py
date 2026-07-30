"""
Contract test for GET /api/v1/public-posts: mounts sentiment.router on a
throwaway FastAPI app, seeds a deterministic scenario (fixed timestamps,
window='all', so the snapshot is byte-stable), and snapshots the JSON
response via the shared harness (conftest.assert_snapshot_match).
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
class PublicPostsContractTests(unittest.TestCase):
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
            conn.execute(
                "TRUNCATE corpus.entities, corpus.authors, corpus.documents, "
                "raw.x_posts, raw.reddit_posts RESTART IDENTITY CASCADE"
            )

    def _seed_doc(self, conn, source_type: str, natural_key: str,
                  published_at: datetime, author_id=None,
                  admission_class: str = "sampled") -> int:
        return conn.execute(
            "INSERT INTO corpus.documents "
            "(source_type, natural_key, published_at, author_id, title, body, "
            " source_url, raw_hash, etl_version, admission_class) "
            "VALUES (%s::corpus.source_type, %s, %s, %s, 'A post', 'body', "
            "'https://example.com/' || %s, 'h' || repeat('0', 63), 'test', "
            "%s::corpus.admission_class) RETURNING doc_id",
            (source_type, natural_key, published_at, author_id, natural_key, admission_class),
        ).fetchone()["doc_id"]

    def _seed_scored_run(self, conn, doc_id: int) -> None:
        run = conn.execute(
            "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
            "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, 'gemini-3.5-flash', "
            "'llm'::analysis.inference_method, 0.9, true) RETURNING run_id",
            (doc_id,),
        ).fetchone()
        conn.execute(
            "INSERT INTO analysis.sentiment_results (run_id, label) "
            "VALUES (%s, 'neutral'::analysis.sentiment_label)",
            (run["run_id"],),
        )

    def _seed(self) -> None:
        from analysis.src.common import db
        published = datetime(2026, 1, 15, 12, 0, tzinfo=timezone.utc)
        with db.connection() as conn:
            # A public X post with engagement (feed row 1).
            author = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle, display_name) "
                "VALUES ('x'::corpus.platform, 'someuser', 'someuser', 'Some User') RETURNING author_id"
            ).fetchone()["author_id"]
            x_doc = self._seed_doc(conn, "x_post", "tweet-1", published, author)
            conn.execute(
                "INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, "
                "like_count, raw_hash, extraction_version) "
                "VALUES ('tweet-1', 'raw-author', %s, %s, 'raw text', 10, 'h' || repeat('0', 63), 'test')",
                (published, published),
            )
            conn.execute(
                "INSERT INTO corpus.x_posts (doc_id, tweet_id, like_count) VALUES (%s, 'tweet-1', 10)",
                (x_doc,),
            )
            self._seed_scored_run(conn, x_doc)

            # A public Reddit post, no engagement (feed row 2).
            r_doc = self._seed_doc(conn, "reddit_post", "t3_abc", published)
            conn.execute(
                "INSERT INTO raw.reddit_posts (fullname, raw_hash, extraction_version) "
                "VALUES ('t3_abc', 'h' || repeat('0', 63), 'test')"
            )
            conn.execute(
                "INSERT INTO corpus.reddit_posts (doc_id, fullname, score, num_comments) "
                "VALUES (%s, 't3_abc', 2, 1)",
                (r_doc,),
            )
            self._seed_scored_run(conn, r_doc)

            # A promoted (editorial=false) official's post -- must NOT appear.
            official = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean, editorial) "
                "VALUES ('rep-example', 'official'::corpus.entity_kind, 'Rep. Example', "
                "'republican'::corpus.political_lean, false) RETURNING entity_id"
            ).fetchone()["entity_id"]
            official_author = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, 'repexample', 'repexample') RETURNING author_id"
            ).fetchone()["author_id"]
            conn.execute(
                "INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at) "
                "VALUES (%s, 'elected_official'::corpus.author_tier, "
                "'curated_list'::corpus.classification_method, %s, now())",
                (official_author, official),
            )
            o_doc = self._seed_doc(
                conn, "x_post", "tweet-official", published, official_author,
                admission_class="official_record",
            )
            conn.execute(
                "INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, "
                "raw_hash, extraction_version) "
                "VALUES ('tweet-official', 'raw-author-2', %s, %s, 'raw text', "
                "'h' || repeat('0', 63), 'test')",
                (published, published),
            )
            conn.execute(
                "INSERT INTO corpus.x_posts (doc_id, tweet_id) VALUES (%s, 'tweet-official')",
                (o_doc,),
            )
            self._seed_scored_run(conn, o_doc)

    def test_public_posts_shape_snapshot(self):
        response = self._client.get("/api/v1/public-posts", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        body = response.json()
        # The promoted official's official_record post is excluded; the two
        # public docs rank engagement-first (X post 10 vs Reddit 3).
        self.assertEqual(body["total"], 2)
        self.assertEqual([item["sourceType"] for item in body["items"]], ["x_post", "reddit_post"])
        conftest.assert_snapshot_match("public_posts_basic", body)


if __name__ == "__main__":
    unittest.main()
