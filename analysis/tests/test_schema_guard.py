"""
Schema-guard tests.

Why this matters: the Python pipeline consumes a schema that only the Go
ingestor's `migrate` applies. When code newer than the DB runs, stages die
mid-pipeline on missing tables with unhelpful errors (2026-07-10 prod:
'no such table: ai_outputs_latest'). The guard converts that into an
immediate, actionable abort — and must never false-positive on a current
or NEWER database (deploys migrate before images switch, so DB-ahead is
the normal ordering).
"""

import os
import sqlite3
import sys
import tempfile
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.common.schema_guard import (
    check_schema_current,
    expected_schema_version,
)

MIGRATIONS_DIR = os.path.join(project_root, "data", "migrations")


def _migration_files():
    return sorted(f for f in os.listdir(MIGRATIONS_DIR) if f.endswith(".sql"))


class TestSchemaGuard(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = self._tmp.name
        self._tmp.close()

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def _apply(self, files):
        conn = sqlite3.connect(self.db_path)
        for name in files:
            with open(os.path.join(MIGRATIONS_DIR, name)) as f:
                conn.executescript(f.read())
        conn.commit()
        conn.close()

    def test_current_db_passes(self):
        self._apply(_migration_files())
        self.assertIsNone(check_schema_current(self.db_path, MIGRATIONS_DIR))

    def test_db_one_migration_behind_is_rejected_with_versions(self):
        files = _migration_files()
        self._apply(files[:-1])
        msg = check_schema_current(self.db_path, MIGRATIONS_DIR)
        self.assertIsNotNone(msg)
        expected = expected_schema_version(MIGRATIONS_DIR)
        self.assertIn(str(expected), msg)
        self.assertIn("migrate", msg)

    def test_never_migrated_db_is_rejected(self):
        # Fresh file: no schema_version table at all.
        msg = check_schema_current(self.db_path, MIGRATIONS_DIR)
        self.assertIsNotNone(msg)
        self.assertIn("version 0", msg)

    def test_db_newer_than_code_is_tolerated(self):
        self._apply(_migration_files())
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            "INSERT INTO schema_version (version, applied_at)"
            " VALUES (9999, strftime('%s','now'))"
        )
        conn.commit()
        conn.close()
        self.assertIsNone(check_schema_current(self.db_path, MIGRATIONS_DIR))

    def test_unreadable_migrations_dir_does_not_block(self):
        self.assertIsNone(
            check_schema_current(self.db_path, "/nonexistent/migrations")
        )


if __name__ == "__main__":
    unittest.main()
