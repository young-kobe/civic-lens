"""
Tests for analysis/src/common/db.py — the Postgres connection pool
singleton (Postgres redesign Phase 1). This module is new plumbing that
nothing else imports yet, so these tests exercise it directly rather than
through any pipeline stage.

The round-trip test only runs against a real Postgres server, gated by
CIVIC_TEST_DATABASE_URL — it is skipped (never failed) when that env var
is absent, since most dev/CI environments have no Postgres available yet.
"""

import os
import unittest

from analysis.src.common import db
from analysis.src.common.settings import Settings


class TestGetPoolFailsLoud(unittest.TestCase):
    """get_pool() must refuse to guess a DSN when CIVIC_DATABASE_URL is unset."""

    def setUp(self):
        db.close_pool()
        self._prev_url = os.environ.pop("CIVIC_DATABASE_URL", None)

    def tearDown(self):
        db.close_pool()
        if self._prev_url is not None:
            os.environ["CIVIC_DATABASE_URL"] = self._prev_url

    def test_empty_database_url_raises_runtime_error(self):
        with self.assertRaises(RuntimeError) as ctx:
            db.get_pool()
        self.assertIn("CIVIC_DATABASE_URL", str(ctx.exception))

    def test_failed_attempt_does_not_cache_a_broken_pool(self):
        """A failed get_pool() call must not leave a singleton behind — a
        later call with a valid URL should get a fresh, working pool."""
        with self.assertRaises(RuntimeError):
            db.get_pool()
        self.assertIsNone(db._pool)


class TestSettingsReadPgEnvVars(unittest.TestCase):
    """CIVIC_DATABASE_URL / CIVIC_PG_POOL_MAX must follow the same
    env-prefix convention as every other Settings field."""

    def test_defaults_when_unset(self):
        settings = Settings(_env_file=None)
        self.assertEqual(settings.database_url, "")
        self.assertEqual(settings.pg_pool_max, 5)

    def test_reads_database_url_from_env(self):
        prev = os.environ.get("CIVIC_DATABASE_URL")
        os.environ["CIVIC_DATABASE_URL"] = "postgresql://user:pass@localhost:5432/testdb"
        try:
            settings = Settings(_env_file=None)
            self.assertEqual(
                settings.database_url, "postgresql://user:pass@localhost:5432/testdb"
            )
        finally:
            if prev is None:
                del os.environ["CIVIC_DATABASE_URL"]
            else:
                os.environ["CIVIC_DATABASE_URL"] = prev

    def test_reads_pg_pool_max_from_env(self):
        prev = os.environ.get("CIVIC_PG_POOL_MAX")
        os.environ["CIVIC_PG_POOL_MAX"] = "12"
        try:
            settings = Settings(_env_file=None)
            self.assertEqual(settings.pg_pool_max, 12)
        finally:
            if prev is None:
                del os.environ["CIVIC_PG_POOL_MAX"]
            else:
                os.environ["CIVIC_PG_POOL_MAX"] = prev


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class TestPoolRoundTripAgainstRealPostgres(unittest.TestCase):
    """Only runs when a real server is reachable. Confirms the pool hands
    back dict_row rows and that TIMESTAMPTZ survives a round trip."""

    def setUp(self):
        db.close_pool()
        self._prev_url = os.environ.get("CIVIC_DATABASE_URL")
        os.environ["CIVIC_DATABASE_URL"] = os.environ["CIVIC_TEST_DATABASE_URL"]

    def tearDown(self):
        db.close_pool()
        if self._prev_url is None:
            os.environ.pop("CIVIC_DATABASE_URL", None)
        else:
            os.environ["CIVIC_DATABASE_URL"] = self._prev_url

    def test_dict_row_and_timestamptz_round_trip(self):
        import datetime

        sent = datetime.datetime(2026, 7, 22, 12, 30, 0, tzinfo=datetime.timezone.utc)
        with db.connection() as conn:
            row = conn.execute(
                "SELECT %(ts)s::timestamptz AS ts, 1 AS one", {"ts": sent}
            ).fetchone()
        self.assertIsInstance(row, dict)
        self.assertEqual(row["one"], 1)
        self.assertEqual(row["ts"], sent)


if __name__ == "__main__":
    unittest.main()
