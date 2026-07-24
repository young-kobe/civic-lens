"""
Tests for analysis/src/api/queries/outlets.py -- Phase 9 strictly-live
Outlet Profiles panel.

Two tiers, matching the repo's established convention
(test_api_queries_narratives.py, test_api_queries_base.py):

  1. Pure-core tests (no DB) -- _time_filter.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with 0001-0004 applied, exercising get_outlet_profiles() end
     to end against seeded scenario rows.
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timezone

from analysis.src.api.queries import outlets
from analysis.src.api.queries.constants import MIN_TARGET_SAMPLE_N
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TimeFilterTests(unittest.TestCase):
    def test_no_bounds_yields_empty_clause_and_no_params(self):
        params = {}
        self.assertEqual(outlets._time_filter(None, None, params), "")
        self.assertEqual(params, {})

    def test_end_only_adds_inclusive_upper_bound(self):
        params = {}
        end = datetime(2026, 1, 1, tzinfo=timezone.utc)
        clause = outlets._time_filter(None, end, params)
        self.assertIn("<=", clause)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class GetOutletProfilesIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.bot_signals, analysis.sentiment_results, "
                "analysis.author_bot_scores, analysis.runs, corpus.documents, "
                "corpus.authors CASCADE"
            )

    # -- seeding helpers --------------------------------------------------

    def _author(self, handle):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, %s, %s) RETURNING author_id",
                (handle, handle),
            ).fetchone()
            return row["author_id"]

    def _doc(
        self, natural_key, *, source_type="news", domain_or_subreddit="example.com",
        author_id=None, published_at=None, admission_class="sampled", source_url=None,
    ):
        from analysis.src.common import db as dbmod
        published_at = published_at or datetime.now(timezone.utc)
        source_url = source_url or f"https://example.com/{natural_key}"
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, domain_or_subreddit, author_id, published_at, body, "
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, %s, 'body text', %s, "
                "        'deadbeef', 'test', %s::corpus.admission_class) "
                "RETURNING doc_id",
                (source_type, natural_key, domain_or_subreddit, author_id, published_at,
                 source_url, admission_class),
            ).fetchone()
            return row["doc_id"]

    def _run(self, task, doc_id, *, model_id="test-model", confidence=0.9):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES (%s::analysis.task, %s, 'done'::analysis.run_status, %s, "
                "        'hybrid'::analysis.inference_method, %s, true) "
                "RETURNING run_id",
                (task, doc_id, model_id, confidence),
            ).fetchone()
            return row["run_id"]

    def _sentiment(self, run_id, label):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.sentiment_results (run_id, label) VALUES (%s, %s::analysis.sentiment_label)",
                (run_id, label),
            )

    def _bot_signals(self, run_id, doc_id, label):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.bot_signals (run_id, doc_id, label) VALUES (%s, %s, %s::analysis.bot_label)",
                (run_id, doc_id, label),
            )

    def _author_bot_score(self, author_id, score):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.author_bot_scores (author_id, score, sample_count, updated_at) "
                "VALUES (%s, %s, 5, now())",
                (author_id, score),
            )

    def _seed_outlet_at_floor(self, domain, *, labels):
        """Seed exactly len(labels) sentiment-scored docs for `domain`."""
        for i, label in enumerate(labels):
            doc_id = self._doc(f"{domain}-{i}", domain_or_subreddit=domain)
            run_id = self._run("text", doc_id)
            self._sentiment(run_id, label)

    # -- tests --------------------------------------------------------------

    def test_outlet_below_volume_floor_is_omitted(self):
        self._seed_outlet_at_floor("thin.example", labels=["positive"] * (MIN_TARGET_SAMPLE_N - 1))
        result = outlets.get_outlet_profiles(start=None, end=None, window_label="all")
        self.assertNotIn("thin.example", [o.outlet_key for o in result.outlets])

    def test_outlet_at_volume_floor_reports_net_tone(self):
        self._seed_outlet_at_floor(
            "example.com",
            labels=["positive"] * (MIN_TARGET_SAMPLE_N - 1) + ["negative"],
        )
        result = outlets.get_outlet_profiles(start=None, end=None, window_label="all")
        profile = next(o for o in result.outlets if o.outlet_key == "example.com")
        self.assertEqual(profile.volume, MIN_TARGET_SAMPLE_N)
        expected_net = round(((MIN_TARGET_SAMPLE_N - 1) - 1) / MIN_TARGET_SAMPLE_N * 100, 1)
        self.assertEqual(profile.net_tone, expected_net)

    def test_bot_authored_content_included_in_net_tone_unlike_other_panels(self):
        # The preserved exception this module exists to encode: a
        # bot-scored author's sentiment-scored docs still count toward net
        # tone here, unlike movers/bot-activity which exclude them.
        bot_author = self._author("bot-handle")
        self._author_bot_score(bot_author, 0.95)
        for i in range(MIN_TARGET_SAMPLE_N):
            doc_id = self._doc(f"outlet-doc-{i}", domain_or_subreddit="botty.example", author_id=bot_author)
            run_id = self._run("text", doc_id)
            self._sentiment(run_id, "positive")

        result = outlets.get_outlet_profiles(start=None, end=None, window_label="all")
        profile = next(o for o in result.outlets if o.outlet_key == "botty.example")
        self.assertEqual(profile.volume, MIN_TARGET_SAMPLE_N)
        self.assertEqual(profile.net_tone, 100.0)

    def test_bot_rate_pct_computed_from_bot_scanned_docs(self):
        for i, label in enumerate(["positive"] * MIN_TARGET_SAMPLE_N):
            doc_id = self._doc(f"scan-doc-{i}", domain_or_subreddit="scanme.example")
            run_id = self._run("text", doc_id)
            self._sentiment(run_id, label)
        bot_doc = self._doc("bot-scan", domain_or_subreddit="scanme.example")
        human_doc = self._doc("human-scan", domain_or_subreddit="scanme.example")
        bot_run = self._run("bot", bot_doc)
        self._bot_signals(bot_run, bot_doc, "bot")
        human_run = self._run("bot", human_doc)
        self._bot_signals(human_run, human_doc, "human")

        result = outlets.get_outlet_profiles(start=None, end=None, window_label="all")
        profile = next(o for o in result.outlets if o.outlet_key == "scanme.example")
        self.assertEqual(profile.total_scanned, 2)
        self.assertEqual(profile.bot_rate_pct, 50.0)

    def test_samples_carry_confidence_source_url_admission_class(self):
        self._seed_outlet_at_floor("example.com", labels=["positive"] * MIN_TARGET_SAMPLE_N)
        result = outlets.get_outlet_profiles(start=None, end=None, window_label="all")
        profile = next(o for o in result.outlets if o.outlet_key == "example.com")
        self.assertGreater(len(profile.samples), 0)
        sample = profile.samples[0]
        self.assertIsNotNone(sample.confidence)
        self.assertTrue(sample.source_url)
        self.assertIn(sample.admission_class, ("sampled", "official_record"))

    def test_range_meta_model_ids_reflects_mixed_seeded_models(self):
        doc_a = self._doc("doc-a", domain_or_subreddit="example.com")
        run_a = self._run("text", doc_a, model_id="sentiment-v1")
        self._sentiment(run_a, "positive")
        doc_b = self._doc("doc-b", domain_or_subreddit="example.com")
        run_b = self._run("bot", doc_b, model_id="bot-detector-v2")
        self._bot_signals(run_b, doc_b, "human")

        result = outlets.get_outlet_profiles(start=None, end=None, window_label="all")
        self.assertEqual(result.range.model_ids, ["bot-detector-v2", "sentiment-v1"])

    def test_custom_date_range_scopes_result(self):
        in_range = self._doc(
            "in-range", domain_or_subreddit="example.com",
            published_at=datetime(2026, 3, 15, tzinfo=timezone.utc),
        )
        out_of_range = self._doc(
            "out-of-range", domain_or_subreddit="example.com",
            published_at=datetime(2020, 1, 1, tzinfo=timezone.utc),
        )
        for doc_id in (in_range, out_of_range):
            run_id = self._run("text", doc_id)
            self._sentiment(run_id, "positive")

        result = outlets.get_outlet_profiles(
            start=datetime(2026, 3, 1, tzinfo=timezone.utc),
            end=datetime(2026, 3, 31, tzinfo=timezone.utc),
            window_label=None,
        )
        self.assertEqual(result.range.sampled_doc_count, 1)
        self.assertIsNone(result.range.window)

    def test_disclaimer_present(self):
        result = outlets.get_outlet_profiles(start=None, end=None, window_label="all")
        self.assertIn("bot", result.disclaimer.lower())


if __name__ == "__main__":
    unittest.main()
