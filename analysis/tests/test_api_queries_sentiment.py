"""
Tests for analysis/src/api/queries/sentiment.py -- Phase 9 GET
/api/v1/sentiment aggregation.

Two tiers, matching the repo's established Postgres-redesign convention
(test_engine_targets.py, test_api_queries_base.py):

  1. Pure-core tests (no DB) -- the row-level helper functions.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, seeding through a
     real Postgres with 0001-0004 applied, exercising the binding rules:
     denominators count analysis.runs (not result rows), trivial-content
     docs never render as fake-neutral, bot-authored docs are excluded from
     discourse aggregates, unresolved target_mentions bucket under a
     catch-all rather than vanishing, and RangeMeta reflects mixed models
     and custom date ranges.
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timedelta, timezone

from analysis.src.api.queries import sentiment
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TopicForRowTests(unittest.TestCase):
    def test_resolved_topic_is_used(self):
        topic = sentiment._topic_for_row(1, {1: "Healthcare"})
        self.assertEqual(topic, "Healthcare")

    def test_unresolved_topic_is_general_never_a_keyword_guess(self):
        # Topic classification is an LLM judgment call (analysis.target_mentions),
        # never a title heuristic -- a doc the LLM never resolved a topic for
        # must render as the honest "General" bucket, not a keyword-guessed one.
        self.assertEqual(sentiment._topic_for_row(1, {}), "General")


class TimeOfDayTests(unittest.TestCase):
    def test_morning_boundary(self):
        self.assertEqual(sentiment._time_of_day(datetime(2026, 1, 1, 5)), "Morning")

    def test_afternoon(self):
        self.assertEqual(sentiment._time_of_day(datetime(2026, 1, 1, 14)), "Afternoon")

    def test_evening(self):
        self.assertEqual(sentiment._time_of_day(datetime(2026, 1, 1, 19)), "Evening")

    def test_night_wraps_past_midnight(self):
        self.assertEqual(sentiment._time_of_day(datetime(2026, 1, 1, 2)), "Night")


class TierForRowTests(unittest.TestCase):
    def test_news_source_is_news_tier(self):
        self.assertEqual(sentiment._tier_for_row("news", None), "news")

    def test_elected_official_x_post_is_officials_tier(self):
        self.assertEqual(sentiment._tier_for_row("x_post", "elected_official"), "officials")

    def test_unaffiliated_x_post_is_general_public(self):
        self.assertEqual(sentiment._tier_for_row("x_post", None), "general_public")

    def test_reddit_post_is_general_public(self):
        self.assertEqual(sentiment._tier_for_row("reddit_post", None), "general_public")


class NetScoreSuppressionTests(unittest.TestCase):
    def test_below_floor_is_none(self):
        counts = {"positive": 2, "negative": 1, "neutral": 0, "mixed": 0}
        self.assertIsNone(sentiment._net_score(counts))

    def test_at_floor_computes(self):
        counts = {"positive": 4, "negative": 1, "neutral": 0, "mixed": 0}
        self.assertEqual(sentiment._net_score(counts), 60.0)


class LeanLabelKindTests(unittest.TestCase):
    def test_official_is_fact(self):
        label = sentiment._lean_label("official", "republican")
        self.assertEqual(label.kind, "fact")
        self.assertEqual(label.value, "republican")

    def test_outlet_is_curated(self):
        label = sentiment._lean_label("outlet", "center-left")
        self.assertEqual(label.kind, "curated")


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class SentimentPanelIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn)

    def setUp(self):
        prev_url = pg_fixture.begin_test(self._dsn)
        self.addCleanup(pg_fixture.end_test, prev_url)
        self._n = 0
        self._truncate_mutable()

    def _truncate_mutable(self) -> None:
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute("TRUNCATE corpus.entities, corpus.authors, corpus.documents RESTART IDENTITY CASCADE")

    # --- seeding helpers ---------------------------------------------------

    def _conn(self):
        from analysis.src.common import db
        return db.connection()

    def _next_key(self, prefix: str) -> str:
        self._n += 1
        return f"{prefix}-{self._n}"

    def _seed_entity(self, kind: str, display_name: str, lean: str = "unknown") -> int:
        with self._conn() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean) "
                "VALUES (%s, %s::corpus.entity_kind, %s, %s::corpus.political_lean) "
                "RETURNING entity_id",
                (self._next_key("entity"), kind, display_name, lean),
            ).fetchone()
            return row["entity_id"]

    def _seed_author(self) -> int:
        with self._conn() as conn:
            handle = self._next_key("author")
            row = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle) "
                "VALUES ('x'::corpus.platform, %s, %s) RETURNING author_id",
                (handle, handle),
            ).fetchone()
            return row["author_id"]

    def _seed_author_profile(self, author_id: int, tier: str, entity_id=None) -> None:
        with self._conn() as conn:
            conn.execute(
                "INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at) "
                "VALUES (%s, %s::corpus.author_tier, 'curated_list'::corpus.classification_method, %s, now())",
                (author_id, tier, entity_id),
            )

    def _seed_bot_score(self, author_id: int, flagged_share: float, *, sample_count: int = 10) -> None:
        """Seeds a bot_post_count so bot_post_count/sample_count ==
        flagged_share -- the label-driven share the exclusion predicate
        reads (replacing the retired additive `score` column)."""
        bot_post_count = round(flagged_share * sample_count)
        with self._conn() as conn:
            conn.execute(
                "INSERT INTO analysis.author_bot_scores "
                "(author_id, bot_post_count, suspicious_post_count, sample_count, updated_at) "
                "VALUES (%s, %s, 0, %s, now())",
                (author_id, bot_post_count, sample_count),
            )

    def _seed_document(
        self, source_type: str, published_at: datetime, author_id=None, title: str = "t",
    ) -> int:
        with self._conn() as conn:
            key = self._next_key("doc")
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, author_id, title, body, "
                " source_url, raw_hash, etl_version) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, %s, 'body', "
                "'https://example.com/' || %s, 'h' || repeat('0', 63), 'test') "
                "RETURNING doc_id",
                (source_type, key, published_at, author_id, title, key),
            ).fetchone()
            return row["doc_id"]

    def _seed_text_run(self, doc_id: int, label, confidence: float = 0.9, model_id: str = "gemini-3.5-flash") -> int:
        with self._conn() as conn:
            run = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, %s, "
                "'llm'::analysis.inference_method, %s, true) RETURNING run_id",
                (doc_id, model_id, confidence),
            ).fetchone()
            run_id = run["run_id"]
            if label is not None:
                conn.execute(
                    "INSERT INTO analysis.sentiment_results (run_id, label) VALUES (%s, %s::analysis.sentiment_label)",
                    (run_id, label),
                )
            return run_id

    def _seed_targets_run(self, doc_id: int, model_id: str = "gemini-3.5-flash") -> int:
        with self._conn() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, is_current) "
                "VALUES ('targets'::analysis.task, %s, 'done'::analysis.run_status, %s, "
                "'llm'::analysis.inference_method, true) RETURNING run_id",
                (doc_id, model_id),
            ).fetchone()
            return row["run_id"]

    def _seed_target_mention(
        self, run_id: int, doc_id: int, raw_target: str, entity_id, stance: str,
        confidence: float = 0.9, topic: str = None,
    ) -> None:
        with self._conn() as conn:
            conn.execute(
                "INSERT INTO analysis.target_mentions (run_id, doc_id, raw_target, entity_id, stance, confidence, topic) "
                "VALUES (%s, %s, %s, %s, %s::analysis.sentiment_label, %s, %s)",
                (run_id, doc_id, raw_target, entity_id, stance, confidence, topic),
            )

    # --- tests ---------------------------------------------------------

    def test_bot_authored_doc_excluded_from_net_tone(self):
        author = self._seed_author()
        self._seed_bot_score(author, 0.9)
        bot_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(bot_doc, "positive")

        clean_author = self._seed_author()
        clean_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=clean_author)
        self._seed_text_run(clean_doc, "negative")

        panel = sentiment.get_sentiment_panel(window="7d")
        self.assertEqual(panel.overview.volume, 1)
        self.assertEqual(panel.overview.excluded_bot_docs, 1)
        self.assertEqual(panel.distribution.strong_negative + panel.distribution.mild_negative, 1)

    def test_trivial_content_doc_absent_from_denominator_never_fake_neutral(self):
        author = self._seed_author()
        trivial_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(trivial_doc, None)  # run done, no sentiment_results row

        panel = sentiment.get_sentiment_panel(window="7d")
        self.assertEqual(panel.overview.total_analyzed, 1)
        self.assertEqual(panel.overview.volume, 0)
        self.assertEqual(panel.overview.trivial_content_docs, 1)
        total_distribution = (
            panel.distribution.strong_positive + panel.distribution.mild_positive
            + panel.distribution.neutral + panel.distribution.mild_negative
            + panel.distribution.strong_negative
        )
        self.assertEqual(total_distribution, 0)

    def test_low_confidence_run_excluded_entirely(self):
        author = self._seed_author()
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc, "positive", confidence=0.1)  # below aggregation_min_confidence

        panel = sentiment.get_sentiment_panel(window="7d")
        self.assertEqual(panel.overview.total_analyzed, 0)
        self.assertEqual(panel.overview.volume, 0)

    def test_doc_with_no_resolved_topic_is_general_not_keyword_guessed(self):
        # Title is deliberately topic-suggestive ("tariff" -> the old,
        # now-removed TOPIC_KEYWORDS fallback would have guessed "Economy").
        # With no analysis.target_mentions row for this doc, topic
        # classification never ran an LLM judgment on it -- the panel must
        # report the honest "General" bucket, not a title-keyword guess.
        author = self._seed_author()
        doc = self._seed_document(
            "x_post", datetime.now(timezone.utc), author_id=author, title="new tariff plan announced",
        )
        self._seed_text_run(doc, "positive")

        panel = sentiment.get_sentiment_panel(window="7d")
        topics = {t.topic: t for t in panel.by_topic}
        self.assertIn("General", topics)
        self.assertNotIn("Economy", topics)
        self.assertEqual(topics["General"].volume, 1)

    def test_official_post_routes_to_officials_tier(self):
        entity = self._seed_entity("official", "Sen. Example", lean="republican")
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=entity)
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc, "positive")

        panel = sentiment.get_sentiment_panel(window="7d")
        tiers = {t.tier: t for t in panel.by_tier}
        self.assertIn("officials", tiers)
        self.assertEqual(tiers["officials"].volume, 1)

    def test_unresolved_target_mention_buckets_under_catch_all(self):
        author = self._seed_author()
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc, "neutral")
        run_id = self._seed_targets_run(doc)
        self._seed_target_mention(run_id, doc, "Some Unregistered Group", None, "negative")

        panel = sentiment.get_sentiment_panel(window="7d")
        catch_all = [e for e in panel.entity_stances if e.catch_all_key == "unresolved"]
        self.assertEqual(len(catch_all), 1)
        self.assertIsNone(catch_all[0].entity_id)
        self.assertIsNone(catch_all[0].entity_key)
        self.assertEqual(catch_all[0].target_stance.negative, 1)

    def test_entity_stance_surfaces_from_target_mentions_alone(self):
        # The regression this whole change is for: analysis.favorability_stances
        # is retired (no writer as of 2026-07-25) -- an entity with ONLY
        # target_mentions evidence (no favorability row could ever exist) must
        # still surface in entity_stances, sourced from target_mentions alone.
        entity = self._seed_entity("official", "Sen. Example", lean="democrat")
        author = self._seed_author()
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc, "neutral")
        targets_run = self._seed_targets_run(doc)
        self._seed_target_mention(targets_run, doc, "Sen. Example", entity, "positive")

        panel = sentiment.get_sentiment_panel(window="7d")
        cell = next(e for e in panel.entity_stances if e.entity_id == entity)
        self.assertEqual(cell.target_stance.positive, 1)
        self.assertEqual(cell.lean.kind, "fact")
        self.assertEqual(cell.lean.value, "democrat")

    def test_custom_date_range_reaches_beyond_widest_preset_window(self):
        author = self._seed_author()
        old_published = datetime.now(timezone.utc) - timedelta(days=400)
        doc = self._seed_document("x_post", old_published, author_id=author)
        self._seed_text_run(doc, "positive")

        preset = sentiment.get_sentiment_panel(window="90d")
        self.assertEqual(preset.overview.volume, 0)

        custom = sentiment.get_sentiment_panel(
            date_from=old_published - timedelta(days=1),
            date_to=old_published + timedelta(days=1),
        )
        self.assertEqual(custom.overview.volume, 1)
        self.assertEqual(custom.range.window, None)
        self.assertIsNotNone(custom.range.start)
        self.assertIsNotNone(custom.range.end)

    def test_range_meta_model_ids_reflects_mixed_models(self):
        author = self._seed_author()
        doc_a = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc_a, "positive", model_id="gemini-3.5-flash")
        doc_b = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc_b, "negative", model_id="qwen2.5:3b")

        panel = sentiment.get_sentiment_panel(window="7d")
        self.assertEqual(panel.range.model_ids, ["gemini-3.5-flash", "qwen2.5:3b"])
        self.assertEqual(panel.range.sampled_doc_count, 2)
        self.assertEqual(panel.range.official_record_doc_count, 0)

    def test_daily_series_groups_by_calendar_day_not_by_window(self):
        # Several docs on the same calendar day must fall into one `daily`
        # bucket; docs on a different day get their own -- proves the
        # date_trunc('day', ...) grouping, not just a flat total. Each day
        # clears MIN_TARGET_SAMPLE_N so net_score isn't withheld as low-n.
        from analysis.src.api.queries.constants import MIN_TARGET_SAMPLE_N
        author = self._seed_author()
        day_one = datetime.now(timezone.utc).replace(hour=9, minute=0, second=0, microsecond=0)
        day_two = day_one - timedelta(days=1)
        for i in range(MIN_TARGET_SAMPLE_N):
            doc = self._seed_document("x_post", day_one.replace(hour=8 + i), author_id=author)
            self._seed_text_run(doc, "positive")
        for i in range(MIN_TARGET_SAMPLE_N):
            doc = self._seed_document("x_post", day_two.replace(hour=8 + i), author_id=author)
            self._seed_text_run(doc, "negative")

        panel = sentiment.get_sentiment_panel(window="7d")
        by_date = {d.date: d for d in panel.daily}
        self.assertEqual(by_date[day_one.date().isoformat()].volume, MIN_TARGET_SAMPLE_N)
        self.assertEqual(by_date[day_one.date().isoformat()].net_score, 100.0)
        self.assertEqual(by_date[day_two.date().isoformat()].volume, MIN_TARGET_SAMPLE_N)
        self.assertEqual(by_date[day_two.date().isoformat()].net_score, -100.0)

    def test_entity_stance_by_topic_breaks_down_target_mentions(self):
        # Regression for the per-entity-per-topic breakdown: GROUP BY
        # entity_id, topic must separate an entity's Economy mentions from
        # its unresolved-topic mentions rather than folding them together.
        entity = self._seed_entity("official", "Sen. Example", lean="republican")
        author = self._seed_author()
        doc_econ = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc_econ, "neutral")
        run_econ = self._seed_targets_run(doc_econ)
        self._seed_target_mention(run_econ, doc_econ, "Sen. Example", entity, "positive", topic="Economy")

        doc_notopic = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc_notopic, "neutral")
        run_notopic = self._seed_targets_run(doc_notopic)
        self._seed_target_mention(run_notopic, doc_notopic, "Sen. Example", entity, "negative", topic=None)

        panel = sentiment.get_sentiment_panel(window="7d")
        cell = next(e for e in panel.entity_stances if e.entity_id == entity)
        by_topic = {t.topic: t for t in cell.by_topic}
        self.assertEqual(by_topic["Economy"].positive, 1)
        self.assertEqual(by_topic["Economy"].volume, 1)
        self.assertEqual(by_topic["General"].negative, 1)

    def test_entity_stance_received_by_tier_separates_officials_from_public(self):
        # "Received tone by speaker tier": the tone an entity receives
        # from an elected-official X post must bucket separately from
        # tone received from an unaffiliated (general_public) X post.
        entity = self._seed_entity("official", "Sen. Example", lean="democrat")
        official_author = self._seed_author()
        self._seed_author_profile(official_author, "elected_official")
        public_author = self._seed_author()

        doc_official = self._seed_document("x_post", datetime.now(timezone.utc), author_id=official_author)
        self._seed_text_run(doc_official, "neutral")
        run_official = self._seed_targets_run(doc_official)
        self._seed_target_mention(run_official, doc_official, "Sen. Example", entity, "positive")

        doc_public = self._seed_document("x_post", datetime.now(timezone.utc), author_id=public_author)
        self._seed_text_run(doc_public, "neutral")
        run_public = self._seed_targets_run(doc_public)
        self._seed_target_mention(run_public, doc_public, "Sen. Example", entity, "negative")

        panel = sentiment.get_sentiment_panel(window="7d")
        cell = next(e for e in panel.entity_stances if e.entity_id == entity)
        by_tier = {t.tier: t for t in cell.received_by_tier}
        self.assertEqual(by_tier["officials"].positive, 1)
        self.assertEqual(by_tier["general_public"].negative, 1)

    def test_entity_stance_carries_entity_key_alongside_entity_id(self):
        # Dual-identifier restoration (owner decision 2026-07-26): the
        # per-entity aggregate must carry the stable slug too, not just
        # the numeric id, so cross-page joins don't need a name guess.
        entity = self._seed_entity("official", "Sen. Example", lean="republican")
        author = self._seed_author()
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc, "neutral")
        run_id = self._seed_targets_run(doc)
        self._seed_target_mention(run_id, doc, "Sen. Example", entity, "positive")

        panel = sentiment.get_sentiment_panel(window="7d")
        cell = next(e for e in panel.entity_stances if e.entity_id == entity)
        self.assertIsNotNone(cell.entity_key)


if __name__ == "__main__":
    unittest.main()
