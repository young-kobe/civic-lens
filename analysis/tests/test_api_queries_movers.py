"""
Tests for analysis/src/api/queries/movers.py -- Phase 9 strictly-live
window-over-window movers panel.

Two tiers, matching the repo's established convention
(test_api_queries_narratives.py, test_api_queries_base.py):

  1. Pure-core tests (no DB) -- _time_filter's exclusive upper bound.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with 0001-0004 applied, exercising get_movers() end to end
     against seeded scenario rows.

Coverage note: entity resolution is exercised via corpus.author_profiles
(kind='official') only -- the outlet (corpus.news_articles) and subreddit
(corpus.reddit_posts) resolution paths share the same COALESCE join and
are not separately seeded here (they each require seeding raw.articles/
raw.reddit_posts too, which is disproportionate to the incremental risk).
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timedelta, timezone

from analysis.src.api.queries import movers
from analysis.src.api.queries.constants import BOT_SCORE_AUTHOR_EXCLUSION, MIN_TARGET_SAMPLE_N
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TimeFilterTests(unittest.TestCase):
    def test_no_bounds_yields_empty_clause_and_no_params(self):
        params = {}
        self.assertEqual(movers._time_filter(None, None, params), "")
        self.assertEqual(params, {})

    def test_end_only_adds_exclusive_upper_bound(self):
        # Deliberate divergence from the sibling panels' inclusive
        # convention: movers compares adjacent periods sharing a boundary
        # instant, so the upper bound must be exclusive.
        params = {}
        end = datetime(2026, 1, 1, tzinfo=timezone.utc)
        clause = movers._time_filter(None, end, params)
        self.assertIn("<", clause)
        self.assertNotIn("<=", clause)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class GetMoversIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate()
        self._now = datetime.now(timezone.utc)
        self._current_start = self._now - timedelta(days=7)
        self._previous_start = self._now - timedelta(days=14)
        self._previous_end = self._current_start

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.favorability_stances, analysis.sentiment_results, "
                "analysis.author_bot_scores, analysis.runs, corpus.author_profiles, "
                "corpus.documents, corpus.authors, corpus.entities CASCADE"
            )

    def _get_movers(self, **overrides):
        kwargs = dict(
            current_start=self._current_start, current_end=None,
            previous_start=self._previous_start, previous_end=self._previous_end,
            current_window_label="7d",
        )
        kwargs.update(overrides)
        return movers.get_movers(**kwargs)

    # -- seeding helpers --------------------------------------------------

    def _entity(self, key, kind="official", display_name=None):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name) "
                "VALUES (%s, %s::corpus.entity_kind, %s) RETURNING entity_id",
                (key, kind, display_name or key),
            ).fetchone()
            return row["entity_id"]

    def _author(self, handle):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, %s, %s) RETURNING author_id",
                (handle, handle),
            ).fetchone()
            return row["author_id"]

    def _author_profile(self, author_id, entity_id):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at) "
                "VALUES (%s, 'elected_official'::corpus.author_tier, "
                "        'curated_list'::corpus.classification_method, %s, now())",
                (author_id, entity_id),
            )

    def _doc(self, natural_key, *, author_id=None, published_at, admission_class="sampled"):
        from analysis.src.common import db as dbmod
        source_url = f"https://example.com/{natural_key}"
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, author_id, published_at, body, "
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES ('x_post'::corpus.source_type, %s, %s, %s, 'body text', %s, "
                "        'deadbeef', 'test', %s::corpus.admission_class) "
                "RETURNING doc_id",
                (natural_key, author_id, published_at, source_url, admission_class),
            ).fetchone()
            return row["doc_id"]

    def _run(self, doc_id, *, model_id="test-model", confidence=0.9):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, %s, "
                "        'llm'::analysis.inference_method, %s, true) "
                "RETURNING run_id",
                (doc_id, model_id, confidence),
            ).fetchone()
            return row["run_id"]

    def _sentiment(self, run_id, label):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.sentiment_results (run_id, label) VALUES (%s, %s::analysis.sentiment_label)",
                (run_id, label),
            )

    def _favorability(self, run_id, entity_id, stance):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.favorability_stances (run_id, entity_id, stance) "
                "VALUES (%s, %s, %s::analysis.favorability_label)",
                (run_id, entity_id, stance),
            )

    def _author_bot_score(self, author_id, score):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.author_bot_scores (author_id, score, sample_count, updated_at) "
                "VALUES (%s, %s, 5, now())",
                (author_id, score),
            )

    def _seed_tone_docs(self, author_id, published_at, labels):
        for i, label in enumerate(labels):
            doc_id = self._doc(f"{author_id}-{published_at.isoformat()}-{i}", author_id=author_id, published_at=published_at)
            run_id = self._run(doc_id)
            self._sentiment(run_id, label)

    def _seed_favorability_docs(self, author_id, entity_id, published_at, stances):
        for i, stance in enumerate(stances):
            doc_id = self._doc(f"fav-{entity_id}-{published_at.isoformat()}-{i}", author_id=author_id, published_at=published_at)
            run_id = self._run(doc_id)
            self._favorability(run_id, entity_id, stance)

    # -- tests --------------------------------------------------------------

    def test_tone_mover_resolved_via_author_profiles(self):
        entity_id = self._entity("sen-test")
        author_id = self._author("sen-handle")
        self._author_profile(author_id, entity_id)
        # Current period: mostly positive. Previous period: mostly negative.
        self._seed_tone_docs(author_id, self._current_start + timedelta(days=1), ["positive"] * MIN_TARGET_SAMPLE_N)
        self._seed_tone_docs(author_id, self._previous_start + timedelta(days=1), ["negative"] * MIN_TARGET_SAMPLE_N)

        result = self._get_movers()
        mover = next(m for m in result.tone_movers if m.entity_key == "sen-test")
        self.assertEqual(mover.current_net, 100.0)
        self.assertEqual(mover.prev_net, -100.0)
        self.assertEqual(mover.delta_pts, 200.0)
        self.assertEqual(mover.current_volume, MIN_TARGET_SAMPLE_N)
        self.assertEqual(mover.prev_volume, MIN_TARGET_SAMPLE_N)

    def test_tone_mover_excludes_bot_scored_author_docs(self):
        entity_id = self._entity("sen-test")
        human_author = self._author("human-handle")
        bot_author = self._author("bot-handle")
        self._author_profile(human_author, entity_id)
        self._author_profile(bot_author, entity_id)
        self._author_bot_score(bot_author, BOT_SCORE_AUTHOR_EXCLUSION + 0.1)
        # Enough human-authored docs to clear the floor in both periods.
        self._seed_tone_docs(human_author, self._current_start + timedelta(days=1), ["positive"] * MIN_TARGET_SAMPLE_N)
        self._seed_tone_docs(human_author, self._previous_start + timedelta(days=1), ["positive"] * MIN_TARGET_SAMPLE_N)
        # Bot-authored docs that would otherwise change the net entirely.
        self._seed_tone_docs(bot_author, self._current_start + timedelta(days=1), ["negative"] * MIN_TARGET_SAMPLE_N)

        result = self._get_movers()
        mover = next((m for m in result.tone_movers if m.entity_key == "sen-test"), None)
        # Human volume alone (5) is unaffected by the bot-authored docs --
        # if bot docs were counted, current_volume would be 10 and the net
        # would no longer be a clean 100.0.
        self.assertIsNotNone(mover)
        self.assertEqual(mover.current_volume, MIN_TARGET_SAMPLE_N)
        self.assertEqual(mover.current_net, 100.0)

    def test_tone_mover_suppressed_below_sample_floor(self):
        entity_id = self._entity("sen-thin")
        author_id = self._author("thin-handle")
        self._author_profile(author_id, entity_id)
        self._seed_tone_docs(author_id, self._current_start + timedelta(days=1), ["positive"] * (MIN_TARGET_SAMPLE_N - 1))
        self._seed_tone_docs(author_id, self._previous_start + timedelta(days=1), ["positive"] * MIN_TARGET_SAMPLE_N)

        result = self._get_movers()
        self.assertNotIn("sen-thin", [m.entity_key for m in result.tone_movers])

    def test_top_favorability_mover_picks_largest_absolute_delta(self):
        small_entity = self._entity("small-delta")
        big_entity = self._entity("big-delta")
        author_id = self._author("handle-1")
        self._author_profile(author_id, small_entity)  # profile link irrelevant to favorability resolution

        self._seed_favorability_docs(author_id, small_entity, self._current_start + timedelta(days=1), ["favorable"] * 3 + ["unfavorable"] * 2)
        self._seed_favorability_docs(author_id, small_entity, self._previous_start + timedelta(days=1), ["favorable"] * 2 + ["unfavorable"] * 3)
        self._seed_favorability_docs(author_id, big_entity, self._current_start + timedelta(days=1), ["favorable"] * MIN_TARGET_SAMPLE_N)
        self._seed_favorability_docs(author_id, big_entity, self._previous_start + timedelta(days=1), ["unfavorable"] * MIN_TARGET_SAMPLE_N)

        result = self._get_movers()
        self.assertIsNotNone(result.top_favorability_mover)
        self.assertEqual(result.top_favorability_mover.entity_key, "big-delta")
        self.assertEqual(result.top_favorability_mover.delta_pts, 200.0)

    def test_range_meta_model_ids_for_current_and_previous(self):
        entity_id = self._entity("sen-test")
        author_id = self._author("sen-handle")
        self._author_profile(author_id, entity_id)
        doc_current = self._doc("doc-current", author_id=author_id, published_at=self._current_start + timedelta(days=1))
        run_current = self._run(doc_current, model_id="sentiment-current")
        self._sentiment(run_current, "positive")
        doc_previous = self._doc("doc-previous", author_id=author_id, published_at=self._previous_start + timedelta(days=1))
        run_previous = self._run(doc_previous, model_id="sentiment-previous")
        self._sentiment(run_previous, "negative")

        result = self._get_movers()
        self.assertEqual(result.current_range.model_ids, ["sentiment-current"])
        self.assertEqual(result.previous_range.model_ids, ["sentiment-previous"])
        self.assertEqual(result.current_range.window, "7d")
        self.assertIsNone(result.previous_range.window)

    def test_custom_range_previous_period_computed_from_explicit_bounds(self):
        entity_id = self._entity("sen-test")
        author_id = self._author("sen-handle")
        self._author_profile(author_id, entity_id)
        custom_start = datetime(2026, 3, 1, tzinfo=timezone.utc)
        custom_end = datetime(2026, 3, 11, tzinfo=timezone.utc)  # 10-day custom range
        expected_previous_start = datetime(2026, 2, 19, tzinfo=timezone.utc)

        self._seed_tone_docs(author_id, custom_start + timedelta(days=1), ["positive"] * MIN_TARGET_SAMPLE_N)
        self._seed_tone_docs(author_id, expected_previous_start + timedelta(days=1), ["negative"] * MIN_TARGET_SAMPLE_N)

        result = movers.get_movers(
            current_start=custom_start, current_end=custom_end,
            previous_start=expected_previous_start, previous_end=custom_start,
            current_window_label=None,
        )
        mover = next(m for m in result.tone_movers if m.entity_key == "sen-test")
        self.assertEqual(mover.current_net, 100.0)
        self.assertEqual(mover.prev_net, -100.0)
        self.assertIsNone(result.current_range.window)


if __name__ == "__main__":
    unittest.main()
