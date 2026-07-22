#!/usr/bin/env python3
"""
Tests for tools/migrate_sqlite_to_pg.py.

Design choice: this is a standalone `unittest` file runnable directly with
the venv python (`analysis/.venv/bin/python -m unittest tools.test_migrate_sqlite_to_pg`
from the repo root, or `python tools/test_migrate_sqlite_to_pg.py`), NOT under
`analysis/tests/` — the module under test is deliberately standalone (imports
nothing from analysis.src or ingest, per its own docstring) and outlives both
of those trees, so its test belongs next to it in tools/, not folded into a
tree it must not depend on. unittest (not pytest) matches the repo's existing
convention (analysis/tests/ is unittest-based).

Two tiers:

  1. Pure mapping-function unit tests (TestMappingFunctions) — no I/O, run
     always, cover every documented edge case (0 epoch, NULL epoch, empty
     string, invalid JSON, int flags both values, page states 0-3).
  2. Full end-to-end integration tests (TestRawArchiveVerifyIntegration) —
     gated on CIVIC_TEST_POSTGRES_DSN (distinct from the runtime CIVIC_DATABASE_URL,
     same convention as ingest/internal/storage/db/db_postgres_test.go) so
     `python -m unittest` never accidentally writes into a configured
     Postgres. Requires a throwaway Postgres 17 the DSN points at; DDL is
     applied by executing data/pg-migrations/0001_north_star.sql directly
     (psycopg), not by shelling out to the Go binary, so this file has no
     Go/binary dependency. Builds a synthetic SQLite source DB by applying
     every data/migrations/*.sql file in order, then inserting fixture rows
     covering every mapped table and the edge cases called out in the task:
     0 epochs, NULL epochs, empty-string JSON, invalid JSON, int flags both
     values, all 4 page states, a raw_hash with a real file under a temp
     raw-files dir plus one deliberately missing.
"""

from __future__ import annotations

import os
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import migrate_sqlite_to_pg as mig  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[1]
PG_DSN_ENV = "CIVIC_TEST_POSTGRES_DSN"


class TestMappingFunctions(unittest.TestCase):
    """The shared mapping layer, exercised directly — no DB required."""

    def test_epoch_to_datetime_nullable(self) -> None:
        self.assertIsNone(mig.epoch_to_datetime(0))
        self.assertIsNone(mig.epoch_to_datetime(None))
        self.assertEqual(mig.epoch_to_datetime(1750000000).timestamp(), 1750000000)

    def test_epoch_to_datetime_not_null_substitutes_epoch_zero(self) -> None:
        self.assertEqual(mig.epoch_to_datetime_not_null(0), mig.EPOCH_ZERO)
        self.assertEqual(mig.epoch_to_datetime_not_null(None), mig.EPOCH_ZERO)
        self.assertEqual(mig.epoch_to_datetime_not_null(1750000000).timestamp(), 1750000000)

    def test_is_epoch_missing(self) -> None:
        self.assertTrue(mig.is_epoch_missing(0))
        self.assertTrue(mig.is_epoch_missing(None))
        self.assertFalse(mig.is_epoch_missing(1))
        self.assertFalse(mig.is_epoch_missing(-1))

    def test_page_state_label_all_four_states(self) -> None:
        self.assertEqual(mig.page_state_label(0), "queued")
        self.assertEqual(mig.page_state_label(1), "inflight")
        self.assertEqual(mig.page_state_label(2), "done")
        self.assertEqual(mig.page_state_label(3), "failed")
        with self.assertRaises(ValueError):
            mig.page_state_label(4)

    def test_int_flag_to_bool_both_values_and_null(self) -> None:
        self.assertTrue(mig.int_flag_to_bool(1))
        self.assertFalse(mig.int_flag_to_bool(0))
        self.assertFalse(mig.int_flag_to_bool(None))

    def test_nullable_int_flag_to_bool_preserves_null(self) -> None:
        self.assertIsNone(mig.nullable_int_flag_to_bool(None))
        self.assertTrue(mig.nullable_int_flag_to_bool(1))
        self.assertFalse(mig.nullable_int_flag_to_bool(0))

    def test_raw_json_text_empty_and_none_map_to_null(self) -> None:
        self.assertIsNone(mig.raw_json_text(""))
        self.assertIsNone(mig.raw_json_text(None))
        self.assertEqual(mig.raw_json_text('{"a":1}'), '{"a":1}')

    def test_best_effort_json_valid_invalid_and_empty(self) -> None:
        self.assertEqual(mig.best_effort_json('{"a":1}'), ({"a": 1}, False))
        self.assertEqual(mig.best_effort_json(""), (None, False))
        self.assertEqual(mig.best_effort_json(None), (None, False))
        value, invalid = mig.best_effort_json("{not valid json")
        self.assertIsNone(value)
        self.assertTrue(invalid)


def _build_sqlite_fixture(db_path: str, raw_files_dir: str) -> tuple[str, str]:
    """Apply every data/migrations/*.sql file in order, then insert fixture
    rows covering every mapped table and edge case. Returns
    (hash_with_file, hash_missing_file) — hash_with_file is used throughout
    the base fixture (a genuinely clean baseline is required for the
    all-green verify test); hash_missing_file is deliberately NOT inserted
    here and is reserved for test_06, which adds one extra row using it to
    prove --verify catches a single missing raw_hash file among otherwise-
    resolvable ones, then removes it again."""
    migrations_dir = REPO_ROOT / "data" / "migrations"
    for sql_file in sorted(migrations_dir.glob("*.sql")):
        subprocess.run(
            ["sqlite3", db_path], input=sql_file.read_text(), text=True, check=True,
        )
    hash_with_file = "aa" + "1" * 62
    hash_missing = "cc" + "2" * 62
    subdir = Path(raw_files_dir) / hash_with_file[:2]
    subdir.mkdir(parents=True, exist_ok=True)
    (subdir / f"{hash_with_file}.html").write_text("<html>fixture</html>")
    _insert_fixture_rows(db_path, hash_with_file, hash_missing)
    return hash_with_file, hash_missing


def _insert_fixture_rows(db_path: str, hash_ok: str, hash_missing: str) -> None:
    """Inserts fixture rows into every mapped table, split across the raw
    (ingestion-side) and corpus/derived-side tables so neither helper
    function grows past the repo's ~60-line guideline. See the two helpers'
    docstrings for exactly which edge case each row covers."""
    conn = sqlite3.connect(db_path)
    conn.execute("PRAGMA foreign_keys = OFF")
    _insert_raw_fixture_rows(conn, hash_ok)
    _insert_derived_fixture_rows(conn, hash_ok)
    conn.commit()
    conn.close()


def _insert_raw_fixture_rows(conn: sqlite3.Connection, hash_ok: str) -> None:
    """pages: all 4 states with sentinel-zero next_fetch_at/inflight_at.
    articles_raw/x_posts_raw/x_users_raw/x_api_budget: one strict NOT-NULL
    epoch problem each (fetched_at/created_at/fetched_at/last_updated = 0).
    reddit_posts_raw/x_users_raw: nullable epoch 0 and NULL.
    x_posts_raw.context_annotations_json: valid/empty-string/NULL. Both
    int-flag values in is_official_tier and verified/protected."""
    conn.executemany(
        "INSERT INTO pages (url_canon, url_raw, domain, state, priority, retries, "
        "next_fetch_at, inflight_at) VALUES (?,?,?,?,?,?,?,?)",
        [
            ("https://a.example/1", "https://a.example/1", "a.example", 0, 5, 0, 0, 0),
            ("https://a.example/2", "https://a.example/2", "a.example", 1, 0, 1, 1750000000, 1750000100),
            ("https://a.example/3", "https://a.example/3", "a.example", 2, 0, 0, 1750000200, 0),
            ("https://a.example/4", "https://a.example/4", "a.example", 3, 0, 3, 1750000300, 0),
        ],
    )
    conn.executemany(
        "INSERT INTO articles_raw (url_canon, domain, fetched_at, published_at, title, raw_hash, extraction_version) "
        "VALUES (?,?,?,?,?,?,?)",
        [
            ("https://a.example/3", "a.example", 1750000250, 1750000000, "Real Article", hash_ok, "v1"),
            ("https://a.example/1", "a.example", 0, None, "Zero Fetched", hash_ok, "v1"),
        ],
    )
    conn.executemany(
        "INSERT INTO reddit_posts_raw (fullname, subreddit, created_utc, fetched_at, title, body, score, num_comments, raw_hash, extraction_version) "
        "VALUES (?,?,?,?,?,?,?,?,?,?)",
        [
            ("t3_abc", "politics", 1750000000, 1750000010, "Post A", "body a", 10, 2, hash_ok, "v1"),
            ("t3_def", "politics", 0, None, "Post B", "body b", 0, 0, hash_ok, "v1"),
        ],
    )
    conn.executemany(
        "INSERT INTO x_posts_raw (tweet_id, author_id, created_at, fetched_at, text, "
        "context_annotations_json, raw_hash, extraction_version, is_official_tier) "
        "VALUES (?,?,?,?,?,?,?,?,?)",
        [
            ("1001", "u1", 1750000000, 1750000010, "hello world", '[{"domain":{"id":"1"}}]', hash_ok, "v1", 1),
            ("1002", "u2", 1750000020, 1750000030, "second post", "", hash_ok, "v1", 0),
            ("1003", "u3", 0, 1750000040, "zero created", None, hash_ok, "v1", 0),
        ],
    )
    conn.executemany(
        "INSERT INTO x_users_raw (user_id, username, created_at, verified, protected, fetched_at, raw_hash) "
        "VALUES (?,?,?,?,?,?,?)",
        [
            ("u1", "user_one", 1700000000, 1, 0, 1750000010, hash_ok),
            ("u2", "user_two", None, 0, 1, 1750000030, hash_ok),
            ("u3", "user_three", 0, 0, 0, 0, hash_ok),
        ],
    )
    conn.executemany(
        "INSERT INTO x_api_budget (month_key, post_count, last_updated) VALUES (?,?,?)",
        [("2026-07", 3, 1750000400), ("1970-01", 0, 0)],
    )


def _insert_derived_fixture_rows(conn: sqlite3.Connection, hash_ok: str) -> None:
    """docs.metadata_json: valid JSON, empty string, and invalid JSON (the
    archive best-effort path); docs.published_at: real/NULL/0."""
    conn.executemany(
        "INSERT INTO docs (source_type, ident, published_at, title, text, raw_hash, metadata_json, etl_version) "
        "VALUES (?,?,?,?,?,?,?,?)",
        [
            ("news", "https://a.example/3", 1750000000, "Real Article", "body", hash_ok, '{"k":"v"}', "v1"),
            ("news", "https://a.example/1", None, "No pubdate", "body2", hash_ok, "", "v1"),
            ("x_post", "1001", 0, "Zero pubdate", "tweet", hash_ok, "{not valid json", "v1"),
        ],
    )


@unittest.skipUnless(os.environ.get(PG_DSN_ENV), f"{PG_DSN_ENV} not set; skipping Postgres integration tests")
class TestRawArchiveVerifyIntegration(unittest.TestCase):
    """Full --raw / --archive / --verify battery against a real throwaway
    Postgres 17 (DSN from CIVIC_TEST_POSTGRES_DSN) and a synthetic SQLite
    source DB built fresh for this test run."""

    @classmethod
    def setUpClass(cls) -> None:
        cls.dsn = os.environ[PG_DSN_ENV]
        cls.tmpdir = tempfile.mkdtemp(prefix="civic_pg_migrate_test_")
        cls.db_path = os.path.join(cls.tmpdir, "fixture.db")
        cls.raw_files_dir = os.path.join(cls.tmpdir, "rawfiles")
        cls.hash_ok, cls.hash_missing = _build_sqlite_fixture(cls.db_path, cls.raw_files_dir)
        cls._reset_schema()

    @classmethod
    def tearDownClass(cls) -> None:
        shutil.rmtree(cls.tmpdir, ignore_errors=True)

    @classmethod
    def _reset_schema(cls) -> None:
        """Drop and recreate every north-star schema so repeated test runs
        against the same throwaway server start from a clean slate, then
        apply 0001_north_star.sql directly (no Go binary dependency here)."""
        import psycopg

        ddl_path = REPO_ROOT / "data" / "pg-migrations" / "0001_north_star.sql"
        with psycopg.connect(cls.dsn, autocommit=True) as conn:
            for schema in ("raw", "corpus", "analysis", "serving", "ops", "archive"):
                conn.execute(f"DROP SCHEMA IF EXISTS {schema} CASCADE")
            conn.execute(ddl_path.read_text())

    def test_01_raw_clean_run_and_idempotent_rerun(self) -> None:
        code = mig.run_raw(self.db_path, self.dsn, 1000, tolerate_not_null_zeroes=True)
        self.assertEqual(code, 0)
        import psycopg

        with psycopg.connect(self.dsn) as conn:
            count_before = conn.execute("SELECT COUNT(*) FROM raw.pages").fetchone()[0]
        self.assertEqual(count_before, 4)

        sqlite_conn = sqlite3.connect(self.db_path)
        sqlite_conn.execute(
            "UPDATE pages SET priority = 99, last_error = 'updated-on-rerun' WHERE url_canon = ?",
            ("https://a.example/2",),
        )
        sqlite_conn.commit()
        sqlite_conn.close()

        code = mig.run_raw(self.db_path, self.dsn, 1000, tolerate_not_null_zeroes=True)
        self.assertEqual(code, 0)
        with psycopg.connect(self.dsn) as conn:
            count_after = conn.execute("SELECT COUNT(*) FROM raw.pages").fetchone()[0]
            row = conn.execute(
                "SELECT priority, last_error FROM raw.pages WHERE url_canon = %s",
                ("https://a.example/2",),
            ).fetchone()
        self.assertEqual(count_after, count_before, "idempotent re-run must not change row count")
        self.assertEqual(row[0], 99, "updated source row must propagate on re-run")
        self.assertEqual(row[1], "updated-on-rerun")

    def test_02_raw_strict_epoch_fails_loud_without_tolerate_flag(self) -> None:
        code = mig.run_raw(self.db_path, self.dsn, 1000, tolerate_not_null_zeroes=False)
        self.assertEqual(code, 1, "strict NOT-NULL epoch zeroes must fail loud by default")

    def test_03_archive_import_and_empty_evals_skip(self) -> None:
        code = mig.run_archive(self.db_path, self.dsn, 1000)
        self.assertEqual(code, 0)
        import psycopg

        with psycopg.connect(self.dsn) as conn:
            docs_count = conn.execute("SELECT COUNT(*) FROM archive.docs").fetchone()[0]
            evals_count = conn.execute("SELECT COUNT(*) FROM archive.ai_output_evals").fetchone()[0]
        self.assertEqual(docs_count, 3)
        self.assertEqual(evals_count, 0, "ai_output_evals must stay empty when source is empty")

    def test_04_verify_all_green_on_migrated_pair(self) -> None:
        code = mig.verify_all(self.db_path, self.dsn, self.raw_files_dir, sample_size=500, seed=42)
        self.assertEqual(code, 0, "verify must pass cleanly on a correctly migrated pair")

    def test_05_verify_fails_on_deleted_target_row(self) -> None:
        import psycopg

        with psycopg.connect(self.dsn, autocommit=True) as conn:
            conn.execute("DELETE FROM raw.x_users WHERE user_id = 'u2'")
        try:
            code = mig.verify_all(self.db_path, self.dsn, self.raw_files_dir, sample_size=500, seed=42)
            self.assertEqual(code, 1, "verify must fail after a target row is deleted")
        finally:
            mig.run_raw(self.db_path, self.dsn, 1000, tolerate_not_null_zeroes=True)  # restore

    def test_06_verify_fails_on_one_missing_hash_among_resolvable_ones(self) -> None:
        """Adds one extra reddit_posts_raw row whose raw_hash has no file on
        disk (hash_missing), alongside all the existing rows whose hashes DO
        resolve (hash_ok) — proving --verify catches a single bad hash in an
        otherwise-clean table, not just a wholesale-missing directory."""
        sqlite_conn = sqlite3.connect(self.db_path)
        sqlite_conn.execute(
            "INSERT INTO reddit_posts_raw (fullname, subreddit, raw_hash, extraction_version) "
            "VALUES (?,?,?,?)",
            ("t3_missing_hash", "politics", self.hash_missing, "v1"),
        )
        sqlite_conn.commit()
        sqlite_conn.close()
        try:
            self.assertEqual(mig.run_raw(self.db_path, self.dsn, 1000, tolerate_not_null_zeroes=True), 0)
            code = mig.verify_all(self.db_path, self.dsn, self.raw_files_dir, sample_size=500, seed=42)
            self.assertEqual(code, 1, "verify must fail when one raw_hash among many resolves to no file")
        finally:
            sqlite_conn = sqlite3.connect(self.db_path)
            sqlite_conn.execute("DELETE FROM reddit_posts_raw WHERE fullname = 't3_missing_hash'")
            sqlite_conn.commit()
            sqlite_conn.close()
            import psycopg

            with psycopg.connect(self.dsn, autocommit=True) as conn:
                conn.execute("DELETE FROM raw.reddit_posts WHERE fullname = 't3_missing_hash'")

    def test_07_verify_fails_when_raw_files_dir_points_elsewhere(self) -> None:
        empty_dir = tempfile.mkdtemp(prefix="civic_pg_migrate_test_emptyraw_")
        try:
            code = mig.verify_all(self.db_path, self.dsn, empty_dir, sample_size=500, seed=42)
            self.assertEqual(code, 1, "verify must fail when raw_hash values resolve to no file")
        finally:
            shutil.rmtree(empty_dir, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
