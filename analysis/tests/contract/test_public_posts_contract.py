"""
Contract tests for the three public-column feeds -- GET /api/v1/
public-posts (tone), /propaganda-public-posts, and /bot-public-posts:
mounts the owning routers on a throwaway FastAPI app, seeds one
deterministic scenario (fixed timestamps, window='all', so the snapshots
are byte-stable), and snapshots each JSON response via the shared harness
(conftest.assert_snapshot_match).
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timezone

from fastapi import FastAPI
from fastapi.testclient import TestClient

from analysis.src.api.routers.bots import router as bots_router
from analysis.src.api.routers.propaganda import router as propaganda_router
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
        app.include_router(propaganda_router, prefix="/api/v1")
        app.include_router(bots_router, prefix="/api/v1")
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

    def _seed_propaganda_run(self, conn, doc_id: int, density: float, techniques=()) -> None:
        run = conn.execute(
            "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
            "VALUES ('propaganda'::analysis.task, %s, 'done'::analysis.run_status, 'gemini-3.5-flash', "
            "'llm'::analysis.inference_method, 0.9, true) RETURNING run_id",
            (doc_id,),
        ).fetchone()
        conn.execute(
            "INSERT INTO analysis.propaganda_results (run_id, density, techniques_validated) "
            "VALUES (%s, %s, %s)",
            (run["run_id"], density, len(techniques)),
        )
        for technique, span in techniques:
            conn.execute(
                "INSERT INTO analysis.propaganda_techniques (run_id, technique, evidence_span, confidence) "
                "VALUES (%s, %s::analysis.propaganda_technique, %s, 0.8)",
                (run["run_id"], technique, span),
            )

    def _seed_bot_run(self, conn, doc_id: int, label: str) -> None:
        run = conn.execute(
            "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current, raw_response) "
            "VALUES ('bot'::analysis.task, %s, 'done'::analysis.run_status, 'gemini-3.5-flash', "
            "'llm'::analysis.inference_method, 0.9, true, "
            "'{\"llm\": {\"indicators\": [\"posting cadence\"], \"reasoning\": \"test reasoning\"}}'::jsonb) "
            "RETURNING run_id",
            (doc_id,),
        ).fetchone()
        conn.execute(
            "INSERT INTO analysis.bot_signals (run_id, doc_id, label) "
            "VALUES (%s, %s, %s::analysis.bot_label)",
            (run["run_id"], doc_id, label),
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

            # Propaganda + bot lenses over the same three docs -- the
            # official's runs must not surface in either feed.
            self._seed_propaganda_run(conn, x_doc, 0.6, techniques=[("loaded_language", "a verbatim span")])
            self._seed_propaganda_run(conn, r_doc, 0.0)
            self._seed_propaganda_run(conn, o_doc, 0.9, techniques=[("name_calling", "span")])
            self._seed_bot_run(conn, x_doc, "bot")
            self._seed_bot_run(conn, r_doc, "human")
            self._seed_bot_run(conn, o_doc, "bot")

    def test_public_posts_shape_snapshot(self):
        response = self._client.get("/api/v1/public-posts", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        body = response.json()
        # The promoted official's official_record post is excluded; the two
        # public docs rank engagement-first (X post 10 vs Reddit 3).
        self.assertEqual(body["total"], 2)
        self.assertEqual([item["sourceType"] for item in body["items"]], ["x_post", "reddit_post"])
        conftest.assert_snapshot_match("public_posts_basic", body)

    def test_propaganda_public_posts_shape_snapshot(self):
        response = self._client.get("/api/v1/propaganda-public-posts", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        body = response.json()
        # Official excluded; flagged X post leads on engagement, clean
        # Reddit post follows with an empty technique list and density 0.
        self.assertEqual(body["total"], 2)
        self.assertEqual(body["items"][0]["techniques"][0]["technique"], "loaded_language")
        self.assertEqual(body["items"][1]["techniques"], [])
        conftest.assert_snapshot_match("propaganda_public_posts_basic", body)

    def test_bot_public_posts_shape_snapshot(self):
        response = self._client.get("/api/v1/bot-public-posts", params={"window": "all"})
        self.assertEqual(response.status_code, 200)
        body = response.json()
        # Official excluded; every verdict carries its label.
        self.assertEqual(body["total"], 2)
        self.assertEqual([item["label"] for item in body["items"]], ["bot", "human"])
        conftest.assert_snapshot_match("bot_public_posts_basic", body)


if __name__ == "__main__":
    unittest.main()
