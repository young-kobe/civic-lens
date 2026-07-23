"""
Tests for data/pg-migrations/0003_admission_class.sql -- Postgres redesign
Phase 8 prep. Gated on CIVIC_TEST_DATABASE_URL, skipped cleanly when unset
(matching analysis/tests/pg_fixture.py's convention). Applies 0001 + 0002
directly (not through pg_fixture.reset_schema, which already folds 0003
into its baseline) so "applies cleanly on top of 0001+0002" is actually
exercised here.
"""

from __future__ import annotations

import os
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
MIGRATION_0001 = REPO_ROOT / "data" / "pg-migrations" / "0001_north_star.sql"
MIGRATION_0002 = REPO_ROOT / "data" / "pg-migrations" / "0002_entity_registry_seed.sql"
MIGRATION_0003 = REPO_ROOT / "data" / "pg-migrations" / "0003_admission_class.sql"

_ALL_SCHEMAS = "raw, corpus, analysis, serving, ops, archive"


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class AdmissionClassMigrationTests(unittest.TestCase):
    def setUp(self):
        import psycopg
        from psycopg.rows import dict_row

        self._psycopg = psycopg
        self._dict_row = dict_row
        self._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        with psycopg.connect(self._dsn, autocommit=True, row_factory=dict_row) as conn:
            conn.execute(f"DROP SCHEMA IF EXISTS {_ALL_SCHEMAS} CASCADE")
            conn.execute(MIGRATION_0001.read_text())
            conn.execute(MIGRATION_0002.read_text())

    def tearDown(self):
        with self._psycopg.connect(self._dsn, autocommit=True, row_factory=self._dict_row) as conn:
            conn.execute(f"DROP SCHEMA IF EXISTS {_ALL_SCHEMAS} CASCADE")

    def test_applies_cleanly_on_top_of_0001_and_0002(self):
        with self._psycopg.connect(self._dsn, autocommit=True, row_factory=self._dict_row) as conn:
            conn.execute(MIGRATION_0003.read_text())
            row = conn.execute(
                "SELECT column_name, is_nullable, column_default FROM information_schema.columns "
                "WHERE table_schema='corpus' AND table_name='documents' AND column_name='admission_class'"
            ).fetchone()
        self.assertIsNotNone(row)
        self.assertEqual(row["is_nullable"], "NO")
        self.assertIn("sampled", row["column_default"])

    def test_existing_rows_default_to_sampled(self):
        """ADD COLUMN ... NOT NULL DEFAULT 'sampled' must backfill rows that
        existed before the migration ran, not just new inserts after it."""
        with self._psycopg.connect(self._dsn, autocommit=True, row_factory=self._dict_row) as conn:
            conn.execute(
                "INSERT INTO corpus.documents (source_type, natural_key, published_at, body, "
                "source_url, raw_hash, etl_version) VALUES ('news', 'pre-migration-doc', now(), "
                "'body text', 'https://example.com/a', 'h' || repeat('0', 63), 'pg-1')"
            )
            conn.execute(MIGRATION_0003.read_text())
            row = conn.execute(
                "SELECT admission_class FROM corpus.documents WHERE natural_key = 'pre-migration-doc'"
            ).fetchone()
        self.assertEqual(row["admission_class"], "sampled")

    def test_new_rows_default_to_sampled_without_specifying_the_column(self):
        with self._psycopg.connect(self._dsn, autocommit=True, row_factory=self._dict_row) as conn:
            conn.execute(MIGRATION_0003.read_text())
            conn.execute(
                "INSERT INTO corpus.documents (source_type, natural_key, published_at, body, "
                "source_url, raw_hash, etl_version) VALUES ('news', 'post-migration-doc', now(), "
                "'body text', 'https://example.com/b', 'h' || repeat('1', 63), 'pg-2')"
            )
            row = conn.execute(
                "SELECT admission_class FROM corpus.documents WHERE natural_key = 'post-migration-doc'"
            ).fetchone()
        self.assertEqual(row["admission_class"], "sampled")

    def test_not_self_idempotent_matching_directory_convention(self):
        """CREATE TYPE has no IF NOT EXISTS in Postgres and this file's ADD
        COLUMN isn't guarded either -- same one-shot-apply convention as
        0001/0002 (see 0002's header comment). A double-apply of the raw
        file fails loud; the Go migration runner's ops.schema_migrations
        version ledger (not this file) is what prevents that in practice."""
        with self._psycopg.connect(self._dsn, autocommit=True, row_factory=self._dict_row) as conn:
            conn.execute(MIGRATION_0003.read_text())
            with self.assertRaises(self._psycopg.Error):
                conn.execute(MIGRATION_0003.read_text())


if __name__ == "__main__":
    unittest.main()
