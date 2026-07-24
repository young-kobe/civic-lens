"""
Contract test for GET /api/v1/movers (Phase 9 strictly-live). Mounts ONLY
analysis/src/api/routers/movers.py on a throwaway FastAPI app -- this
workstream does not touch server.py/routers/__init__.py, so nothing else
is registered. Gated on CIVIC_TEST_DATABASE_URL like every other
integration test in this repo.
"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timedelta, timezone

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(os.path.dirname(current_dir)))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.api.queries.constants import MIN_TARGET_SAMPLE_N
from analysis.tests import pg_fixture
from analysis.tests.contract import conftest

# Fixed (not datetime.now()) so the recorded JSON snapshot stays stable
# across runs -- the current/previous range bounds feed straight into the
# response. A 7-day current range with the previous range immediately
# preceding it, matching how the router computes the previous period from
# an explicit from/to pair.
_CURRENT_START = datetime(2026, 1, 8, tzinfo=timezone.utc)
_CURRENT_END = datetime(2026, 1, 15, tzinfo=timezone.utc)
_PREVIOUS_START = datetime(2026, 1, 1, tzinfo=timezone.utc)


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class MoversContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

        from fastapi import FastAPI
        from fastapi.testclient import TestClient
        from analysis.src.api.routers.movers import router

        app = FastAPI()
        app.include_router(router)
        cls._client = TestClient(app)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.sentiment_results, analysis.runs, corpus.author_profiles, "
                "corpus.documents, corpus.authors, corpus.entities RESTART IDENTITY CASCADE"
            )

    def _seed_mover_scenario(self):
        """One official entity: unanimous positive in the fixed current
        range, unanimous negative in the immediately preceding range of
        the same duration."""
        from analysis.src.common import db as dbmod
        current_ts = _CURRENT_START + timedelta(days=1)
        previous_ts = _PREVIOUS_START + timedelta(days=1)
        with dbmod.connection() as conn:
            entity = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name) "
                "VALUES ('sen-contract', 'official'::corpus.entity_kind, 'Senator Contract') "
                "RETURNING entity_id",
            ).fetchone()["entity_id"]
            author = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, 'contract-handle', 'contract-handle') "
                "RETURNING author_id",
            ).fetchone()["author_id"]
            conn.execute(
                "INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at) "
                "VALUES (%s, 'elected_official'::corpus.author_tier, "
                "        'curated_list'::corpus.classification_method, %s, now())",
                (author, entity),
            )
            for label, ts in (("positive", current_ts), ("negative", previous_ts)):
                for i in range(MIN_TARGET_SAMPLE_N):
                    doc = conn.execute(
                        "INSERT INTO corpus.documents "
                        "(source_type, natural_key, author_id, published_at, body, source_url, "
                        " raw_hash, etl_version) "
                        "VALUES ('x_post'::corpus.source_type, %s, %s, %s, 'body', %s, "
                        "        'deadbeef', 'test') RETURNING doc_id",
                        (f"contract-{label}-{i}", author, ts, f"https://example.com/{label}-{i}"),
                    ).fetchone()["doc_id"]
                    run = conn.execute(
                        "INSERT INTO analysis.runs "
                        "(task, doc_id, status, model_id, inference_method, confidence, is_current) "
                        "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, "
                        "        'test-model', 'llm'::analysis.inference_method, 0.9, true) "
                        "RETURNING run_id",
                        (doc,),
                    ).fetchone()["run_id"]
                    conn.execute(
                        "INSERT INTO analysis.sentiment_results (run_id, label) "
                        "VALUES (%s, %s::analysis.sentiment_label)",
                        (run, label),
                    )

    def test_movers_response_shape(self):
        # Uses an explicit from/to range (not a window preset) so the
        # recorded snapshot's range bounds are deterministic across runs.
        self._seed_mover_scenario()
        response = self._client.get(
            "/movers",
            params={"from": _CURRENT_START.isoformat(), "to": _CURRENT_END.isoformat()},
        )
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIn("currentRange", payload)
        self.assertIn("previousRange", payload)
        self.assertEqual(len(payload["toneMovers"]), 1)
        mover = payload["toneMovers"][0]
        self.assertEqual(mover["entityKey"], "sen-contract")
        self.assertEqual(mover["currentNet"], 100.0)
        self.assertEqual(mover["prevNet"], -100.0)
        self.assertEqual(payload["previousRange"]["start"], "2026-01-01T00:00:00Z")
        conftest.assert_snapshot_match("movers_basic", payload)

    def test_all_window_is_rejected(self):
        # Owner-decision exception: unlike bot-activity/outlet-profiles,
        # movers cannot resolve to an unbounded range -- there is no
        # meaningful "preceding equal-length period" for 'all'.
        response = self._client.get("/movers", params={"window": "all"})
        self.assertEqual(response.status_code, 400)
        self.assertIn("bounded", response.json()["detail"])

    def test_unknown_window_returns_400(self):
        response = self._client.get("/movers", params={"window": "nonsense"})
        self.assertEqual(response.status_code, 400)

    def test_window_and_custom_range_together_returns_400(self):
        response = self._client.get(
            "/movers", params={"window": "7d", "from": "2026-01-01T00:00:00Z"},
        )
        self.assertEqual(response.status_code, 400)

    def test_custom_range_returns_200_with_computed_previous_period(self):
        self._seed_mover_scenario()
        now = datetime.now(timezone.utc)
        response = self._client.get(
            "/movers",
            params={
                "from": (now - timedelta(days=7)).isoformat(),
                "to": now.isoformat(),
            },
        )
        self.assertEqual(response.status_code, 200)
        payload = response.json()
        self.assertIsNone(payload["currentRange"]["window"])
        self.assertIsNotNone(payload["previousRange"]["start"])

    def test_no_params_defaults_to_30d_window(self):
        response = self._client.get("/movers")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json()["currentRange"]["window"], "30d")


if __name__ == "__main__":
    unittest.main()
