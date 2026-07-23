"""
Tests for analysis/src/etl/authors.py — Postgres redesign Phase 4.

sync_reddit_authors() is a pure documented no-op (see module docstring),
so it is tested unconditionally, no Postgres required. sync_x_authors()
needs a real database and is gated on CIVIC_TEST_DATABASE_URL, matching
the convention in analysis/tests/test_pg_db.py.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.etl import authors
from analysis.tests import pg_fixture


class SyncRedditAuthorsTests(unittest.TestCase):
    def test_is_a_documented_noop_requiring_no_database(self):
        """Must not attempt any DB call — CIVIC_DATABASE_URL is deliberately
        unset here, so a real attempt would raise."""
        prev = os.environ.pop("CIVIC_DATABASE_URL", None)
        try:
            self.assertEqual(authors.sync_reddit_authors(), 0)
        finally:
            if prev is not None:
                os.environ["CIVIC_DATABASE_URL"] = prev


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class SyncXAuthorsIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute("TRUNCATE corpus.authors, raw.x_users CASCADE")

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _insert_x_user(self, conn, user_id, username, followers_count, verified=False):
        conn.execute(
            "INSERT INTO raw.x_users (user_id, username, name, description, location, "
            "profile_image_url, verified, verified_type, followers_count, following_count, "
            "created_at, fetched_at, raw_hash) VALUES (%s, %s, %s, '', '', '', %s, NULL, %s, "
            "0, now(), now(), 'h'||repeat('0', 63))",
            (user_id, username, username, verified, followers_count),
        )

    def test_sync_inserts_author_row(self):
        with authors.db.connection() as conn:
            self._insert_x_user(conn, "u1", "senjones", 1000)
        count = authors.sync_x_authors()
        self.assertEqual(count, 1)
        with authors.db.connection() as conn:
            row = conn.execute(
                "SELECT platform, platform_author_id, handle, followers_count "
                "FROM corpus.authors WHERE platform_author_id = 'u1'"
            ).fetchone()
            self.assertEqual(row["platform"], "x")
            self.assertEqual(row["handle"], "senjones")
            self.assertEqual(row["followers_count"], 1000)

    def test_resync_refreshes_snapshot_columns(self):
        with authors.db.connection() as conn:
            self._insert_x_user(conn, "u1", "senjones", 1000)
        authors.sync_x_authors()
        with authors.db.connection() as conn:
            conn.execute("UPDATE raw.x_users SET followers_count = 2000 WHERE user_id = 'u1'")
        authors.sync_x_authors()
        with authors.db.connection() as conn:
            row = conn.execute(
                "SELECT followers_count FROM corpus.authors WHERE platform_author_id = 'u1'"
            ).fetchone()
            self.assertEqual(row["followers_count"], 2000)

    def test_idempotent_rerun_does_not_duplicate(self):
        with authors.db.connection() as conn:
            self._insert_x_user(conn, "u1", "senjones", 1000)
        authors.sync_x_authors()
        authors.sync_x_authors()
        with authors.db.connection() as conn:
            count = conn.execute(
                "SELECT COUNT(*) AS n FROM corpus.authors WHERE platform_author_id = 'u1'"
            ).fetchone()["n"]
            self.assertEqual(count, 1)


if __name__ == "__main__":
    unittest.main()
