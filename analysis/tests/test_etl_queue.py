"""
Tests for analysis/src/etl/queue.py — Postgres redesign Phase 4.
"""

from __future__ import annotations

import datetime
import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.etl import authors, documents, queue
from analysis.tests import pg_fixture

UTC = datetime.timezone.utc

# Expected task-applicability set per source_type — same (doc, task) set the
# old TASK_MATRIX dict encoded, kept here so the seeded-rows behavior is
# provably unchanged by the CROSS JOIN/unnest refactor in queue.py.
EXPECTED_TASKS_BY_SOURCE_TYPE = {
    "news": frozenset({"text", "targets", "propaganda", "claims", "citations"}),
    "reddit_post": frozenset({"bot", "text", "targets", "propaganda", "claims", "citations"}),
    "x_post": frozenset({"bot", "text", "targets", "propaganda", "claims", "citations"}),
}


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class QueueIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE ops.task_queue, corpus.x_posts, corpus.reddit_posts, "
                "corpus.news_articles, corpus.documents, corpus.authors, "
                "raw.x_posts, raw.x_users, raw.reddit_posts, raw.articles, raw.pages CASCADE"
            )

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _insert_doc(self, conn, source_type, natural_key):
        conn.execute(
            "INSERT INTO corpus.documents (source_type, natural_key, published_at, body, "
            "source_url, raw_hash, etl_version) VALUES (%s::corpus.source_type, %s, now(), "
            "'body', 'https://example.com', 'h'||repeat('0', 63), 'pg-1')",
            (source_type, natural_key),
        )

    def test_seeds_matrix_tasks_per_source_type(self):
        with queue.db.connection() as conn:
            self._insert_doc(conn, "news", "n1")
            self._insert_doc(conn, "reddit_post", "r1")
            self._insert_doc(conn, "x_post", "x1")
        inserted = queue.seed_pending_tasks()
        self.assertEqual(inserted, sum(len(tasks) for tasks in EXPECTED_TASKS_BY_SOURCE_TYPE.values()))
        with queue.db.connection() as conn:
            for source_type, natural_key in (("news", "n1"), ("reddit_post", "r1"), ("x_post", "x1")):
                tasks = {
                    row["task"] for row in conn.execute(
                        "SELECT task FROM ops.task_queue q JOIN corpus.documents d USING (doc_id) "
                        "WHERE d.natural_key = %s",
                        (natural_key,),
                    ).fetchall()
                }
                self.assertEqual(tasks, EXPECTED_TASKS_BY_SOURCE_TYPE[source_type])
            self.assertNotIn(
                "bot",
                {
                    row["task"] for row in conn.execute(
                        "SELECT task FROM ops.task_queue q JOIN corpus.documents d USING (doc_id) "
                        "WHERE d.natural_key = 'n1'"
                    ).fetchall()
                },
            )

    def test_reseed_is_idempotent_and_does_not_reset_status(self):
        with queue.db.connection() as conn:
            self._insert_doc(conn, "news", "n1")
        queue.seed_pending_tasks()
        with queue.db.connection() as conn:
            conn.execute(
                "UPDATE ops.task_queue SET status = 'done' "
                "WHERE task = 'text'::analysis.task"
            )
        second_pass_inserted = queue.seed_pending_tasks()
        self.assertEqual(second_pass_inserted, 0)
        with queue.db.connection() as conn:
            row = conn.execute(
                "SELECT status FROM ops.task_queue WHERE task = 'text'::analysis.task"
            ).fetchone()
            self.assertEqual(row["status"], "done")

    def test_reset_stale_in_progress_resets_old_but_not_recent(self):
        with queue.db.connection() as conn:
            self._insert_doc(conn, "news", "n1")
        queue.seed_pending_tasks()
        with queue.db.connection() as conn:
            conn.execute(
                "UPDATE ops.task_queue SET status = 'in_progress', attempts = 1, "
                "claimed_at = now() - interval '2 hours' WHERE task = 'text'::analysis.task"
            )
            conn.execute(
                "UPDATE ops.task_queue SET status = 'in_progress', attempts = 1, "
                "claimed_at = now() WHERE task = 'targets'::analysis.task"
            )
        reset_count = queue.reset_stale_in_progress(older_than_minutes=30)
        self.assertEqual(reset_count, 1)
        with queue.db.connection() as conn:
            stale_row = conn.execute(
                "SELECT status, attempts, claimed_at FROM ops.task_queue WHERE task = 'text'::analysis.task"
            ).fetchone()
            self.assertEqual(stale_row["status"], "pending")
            self.assertEqual(stale_row["attempts"], 1)
            self.assertIsNone(stale_row["claimed_at"])

            fresh_row = conn.execute(
                "SELECT status FROM ops.task_queue WHERE task = 'targets'::analysis.task"
            ).fetchone()
            self.assertEqual(fresh_row["status"], "in_progress")

    def test_full_pipeline_authors_documents_queue_fk_integrity(self):
        """End-to-end authors.py -> documents.py -> queue.py, per the
        ordering contract documented in each module: sync authors first,
        then load documents (author_id resolves), then seed the queue.
        Asserts FK integrity across doc/author/subtype/queue rows."""
        with queue.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.x_users (user_id, username, name, description, location, "
                "profile_image_url, verified, verified_type, followers_count, following_count, "
                "created_at, fetched_at, raw_hash) VALUES ('u1', 'senjones', 'Sen Jones', '', "
                "'', '', false, NULL, 100, 10, now(), now(), 'a'||repeat('0', 63))"
            )
            conn.execute(
                "INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, "
                "raw_hash, extraction_version) VALUES ('t1', 'u1', now(), now(), "
                "'Congress must pass this immigration bill now', "
                "'b'||repeat('0', 63), 'v1')"
            )

        authors.sync_x_authors()
        doc_result = documents.load_new_documents()
        self.assertEqual(doc_result.inserted, 1)
        queue_inserted = queue.seed_pending_tasks()
        self.assertEqual(queue_inserted, len(EXPECTED_TASKS_BY_SOURCE_TYPE["x_post"]))

        with queue.db.connection() as conn:
            doc = conn.execute(
                "SELECT d.doc_id, d.author_id, x.tweet_id "
                "FROM corpus.documents d JOIN corpus.x_posts x USING (doc_id) "
                "WHERE d.source_type = 'x_post'"
            ).fetchone()
            self.assertIsNotNone(doc["author_id"])

            author = conn.execute(
                "SELECT author_id FROM corpus.authors WHERE author_id = %s", (doc["author_id"],)
            ).fetchone()
            self.assertIsNotNone(author)

            queue_tasks = {
                row["task"] for row in conn.execute(
                    "SELECT task FROM ops.task_queue WHERE doc_id = %s", (doc["doc_id"],)
                ).fetchall()
            }
            self.assertEqual(queue_tasks, EXPECTED_TASKS_BY_SOURCE_TYPE["x_post"])


if __name__ == "__main__":
    unittest.main()
