"""
Tests for analysis/src/api/queries/bots.py -- Phase 9 strictly-live
Bot Detector panel.

Two tiers, matching the repo's established convention
(test_api_queries_narratives.py, test_api_queries_base.py):

  1. Pure-core tests (no DB) -- _time_filter, _age_bucket_label.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with 0001-0004 applied, exercising get_bot_activity() end
     to end against seeded scenario rows.
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timedelta, timezone

from analysis.src.api.queries import bots
from analysis.src.api.queries.constants import BOT_FLAGGED_SHARE_EXCLUSION
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TimeFilterTests(unittest.TestCase):
    def test_no_bounds_yields_empty_clause_and_no_params(self):
        params = {}
        self.assertEqual(bots._time_filter(None, None, params), "")
        self.assertEqual(params, {})

    def test_start_only_adds_lower_bound(self):
        params = {}
        start = datetime(2026, 1, 1, tzinfo=timezone.utc)
        clause = bots._time_filter(start, None, params)
        self.assertIn(">=", clause)
        self.assertEqual(params["_range_start"], start)

    def test_end_only_adds_inclusive_upper_bound(self):
        params = {}
        end = datetime(2026, 1, 1, tzinfo=timezone.utc)
        clause = bots._time_filter(None, end, params)
        self.assertIn("<=", clause)
        self.assertEqual(params["_range_end"], end)


class PostingCadenceTests(unittest.TestCase):
    def _row(self, hour, label="bot"):
        return {"bot_label": label, "published_at": datetime(2026, 1, 1, hour, tzinfo=timezone.utc)}

    def test_all_24_hours_present_even_at_zero_count(self):
        cadence = bots._build_posting_cadence([self._row(3)])
        self.assertEqual(len(cadence), 24)
        hours = {bucket.hour for bucket in cadence}
        self.assertEqual(hours, set(range(24)))

    def test_only_bot_labeled_docs_counted(self):
        rows = [self._row(5, label="bot"), self._row(5, label="human")]
        cadence = bots._build_posting_cadence(rows)
        bucket = next(b for b in cadence if b.hour == 5)
        self.assertEqual(bucket.doc_count, 1)

    def test_none_published_at_does_not_raise(self):
        rows = [{"bot_label": "bot", "published_at": None}]
        cadence = bots._build_posting_cadence(rows)
        self.assertEqual(sum(b.doc_count for b in cadence), 0)


class HumanizeIndicatorTests(unittest.TestCase):
    def test_snake_case_slug_becomes_prose(self):
        self.assertEqual(
            bots._humanize_indicator("zero_followers_following_listed"),
            "Zero followers following listed",
        )

    def test_already_prose_heuristic_text_passes_through(self):
        self.assertEqual(bots._humanize_indicator("New account (12 days)"), "New account (12 days)")

    def test_blank_indicator_yields_empty_string(self):
        self.assertEqual(bots._humanize_indicator(""), "")


class BuildSourceLabelTests(unittest.TestCase):
    def test_news_uses_domain(self):
        self.assertEqual(bots._build_source_label("news", "nyt.com", None), "News · nyt.com")

    def test_x_post_uses_handle(self):
        self.assertEqual(bots._build_source_label("x_post", None, "someuser"), "X · @someuser")

    def test_reddit_post_uses_subreddit(self):
        self.assertEqual(bots._build_source_label("reddit_post", "politics", None), "Reddit · r/politics")

    def test_x_post_without_handle_degrades_to_bare_platform(self):
        self.assertEqual(bots._build_source_label("x_post", None, None), "X")


class BuildFlaggedExampleTests(unittest.TestCase):
    def _row(self, **overrides):
        row = {
            "doc_id": 1, "source_url": "https://example.com/1", "source_type": "x_post",
            "domain_or_subreddit": None, "author_handle": "handle", "confidence": 0.87,
            "flagged_text": "some flagged body text", "reasoning": "looks templated",
            "indicators_json": ["zero_followers_following_listed", "New account (2 days)"],
        }
        row.update(overrides)
        return row

    def test_empty_body_yields_none(self):
        self.assertIsNone(bots._build_flagged_example(self._row(flagged_text="   ")))

    def test_indicators_are_humanized_and_capped(self):
        example = bots._build_flagged_example(self._row())
        self.assertIn("Zero followers following listed", example.indicators)
        self.assertIn("New account (2 days)", example.indicators)

    def test_url_is_source_url_verbatim(self):
        example = bots._build_flagged_example(self._row())
        self.assertEqual(example.url, "https://example.com/1")

    def test_long_reasoning_is_truncated(self):
        row = self._row(reasoning="x" * 500)
        example = bots._build_flagged_example(row)
        self.assertLessEqual(len(example.reasoning), bots._EXAMPLE_REASONING_CHARS)
        self.assertTrue(example.reasoning.endswith("..."))


class RouteEntityBucketTests(unittest.TestCase):
    def _row(self, source_type, doc_id=1, author_id=None):
        return {"source_type": source_type, "doc_id": doc_id, "author_id": author_id}

    def test_news_docs_are_excluded_entirely(self):
        result = bots._route_entity_bucket(self._row("news", author_id=1), {}, {})
        self.assertIsNone(result)

    def test_editorial_official_routes_to_officials(self):
        author_entities = {1: {"entity_id": 42, "kind": "official", "editorial": True}}
        bucket, slot_key, kind, entity_id = bots._route_entity_bucket(
            self._row("x_post", author_id=1), author_entities, {},
        )
        self.assertEqual((bucket, slot_key, kind, entity_id), ("officials", 42, "official", 42))

    def test_non_editorial_account_routes_to_general_public(self):
        author_entities = {1: {"entity_id": 43, "kind": "official", "editorial": False}}
        bucket, slot_key, kind, entity_id = bots._route_entity_bucket(
            self._row("x_post", author_id=1), author_entities, {},
        )
        self.assertEqual((bucket, slot_key, kind, entity_id), ("general_public", 43, "account", 43))

    def test_subreddit_match_routes_to_general_public(self):
        subreddit_entities = {7: {"entity_id": 99, "entity_key": "r-politics"}}
        bucket, slot_key, kind, entity_id = bots._route_entity_bucket(
            self._row("reddit_post", doc_id=7), {}, subreddit_entities,
        )
        self.assertEqual((bucket, slot_key, kind, entity_id), ("general_public", 99, "subreddit", 99))

    def test_unmatched_x_post_folds_into_catch_all(self):
        from analysis.src.api.queries import profiles
        bucket, slot_key, kind, entity_id = bots._route_entity_bucket(
            self._row("x_post", author_id=None), {}, {},
        )
        self.assertEqual(bucket, "general_public")
        self.assertEqual(slot_key, profiles.CATCH_ALL_X_USERS)
        self.assertEqual(kind, "catch_all")
        self.assertIsNone(entity_id)

    def test_unmatched_reddit_post_folds_into_catch_all(self):
        from analysis.src.api.queries import profiles
        bucket, slot_key, kind, entity_id = bots._route_entity_bucket(
            self._row("reddit_post", doc_id=7), {}, {},
        )
        self.assertEqual(slot_key, profiles.CATCH_ALL_SUBREDDITS)


class CoordinationStatsTests(unittest.TestCase):
    def test_no_bot_flagged_docs_yields_zeros(self):
        stats = bots._build_coordination_stats([{"bot_label": "human", "author_id": 1}])
        self.assertEqual(stats.account_reuse, 0.0)
        self.assertEqual(stats.avg_posts_per_suspected_account, 0.0)
        self.assertIsNone(stats.identical_text_pairs)

    def test_reuse_and_average_over_in_range_flagged_docs_only(self):
        # author 1 posts 2 flagged docs (reused), author 2 posts 1 (not reused).
        rows = [
            {"bot_label": "bot", "author_id": 1},
            {"bot_label": "bot", "author_id": 1},
            {"bot_label": "bot", "author_id": 2},
            {"bot_label": "human", "author_id": 2},  # must not count -- not label='bot'
        ]
        stats = bots._build_coordination_stats(rows)
        self.assertAlmostEqual(stats.account_reuse, 1 / 2)
        self.assertAlmostEqual(stats.avg_posts_per_suspected_account, 3 / 2)
        self.assertIsNone(stats.identical_text_pairs)


class PgDayOfWeekTests(unittest.TestCase):
    def test_sunday_maps_to_zero(self):
        sunday = datetime(2026, 1, 4, tzinfo=timezone.utc)  # a Sunday
        self.assertEqual(sunday.weekday(), 6)
        self.assertEqual(bots._pg_day_of_week(sunday), 0)

    def test_monday_maps_to_one(self):
        monday = datetime(2026, 1, 5, tzinfo=timezone.utc)
        self.assertEqual(bots._pg_day_of_week(monday), 1)


class PostingCadenceGridTests(unittest.TestCase):
    def test_all_168_cells_present(self):
        grid = bots._build_posting_cadence_grid([])
        self.assertEqual(len(grid), 168)

    def test_only_bot_labeled_docs_counted(self):
        rows = [
            {"bot_label": "bot", "published_at": datetime(2026, 1, 5, 14, tzinfo=timezone.utc)},
            {"bot_label": "human", "published_at": datetime(2026, 1, 5, 14, tzinfo=timezone.utc)},
        ]
        grid = bots._build_posting_cadence_grid(rows)
        cell = next(c for c in grid if c.day_of_week == 1 and c.hour == 14)
        self.assertEqual(cell.doc_count, 1)


class CoordinationIndexTests(unittest.TestCase):
    def test_empty_histogram_is_zero(self):
        from analysis.src.api.models.bots import PostingCadenceBucket
        cadence = [PostingCadenceBucket(hour=h, doc_count=0) for h in range(24)]
        self.assertEqual(bots._compute_coordination_index(cadence), 0.0)

    def test_all_posts_in_one_hour_is_maximal_coordination(self):
        from analysis.src.api.models.bots import PostingCadenceBucket
        cadence = [
            PostingCadenceBucket(hour=h, doc_count=10 if h == 4 else 0) for h in range(24)
        ]
        self.assertEqual(bots._compute_coordination_index(cadence), 1.0)

    def test_evenly_spread_posts_yield_low_coordination(self):
        from analysis.src.api.models.bots import PostingCadenceBucket
        cadence = [PostingCadenceBucket(hour=h, doc_count=1) for h in range(24)]
        self.assertAlmostEqual(bots._compute_coordination_index(cadence), 1 / 24, places=3)


class AgeBucketLabelTests(unittest.TestCase):
    def test_none_created_at_is_unknown(self):
        self.assertEqual(bots._age_bucket_label(datetime.now(timezone.utc), None), "unknown")

    def test_just_under_seven_days_buckets_as_under_seven(self):
        now = datetime(2026, 1, 10, tzinfo=timezone.utc)
        created_at = now - timedelta(days=6)
        self.assertEqual(bots._age_bucket_label(now, created_at), "< 7 days")

    def test_four_years_buckets_as_three_plus(self):
        now = datetime(2026, 1, 10, tzinfo=timezone.utc)
        created_at = now - timedelta(days=365 * 4)
        self.assertEqual(bots._age_bucket_label(now, created_at), "3+ years")


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class GetBotActivityIntegrationTests(unittest.TestCase):
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
                "TRUNCATE analysis.author_leans, analysis.narrative_docs, analysis.narratives, "
                "analysis.clustering_runs, analysis.claims, analysis.bot_signals, "
                "analysis.author_bot_scores, analysis.runs, corpus.reddit_posts, "
                "corpus.author_profiles, corpus.documents, corpus.authors, corpus.entities, "
                "raw.reddit_posts CASCADE"
            )

    # -- seeding helpers --------------------------------------------------

    def _entity(self, key, kind="official", display_name=None, editorial=False):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, editorial) "
                "VALUES (%s, %s::corpus.entity_kind, %s, %s) RETURNING entity_id",
                (key, kind, display_name or key, editorial),
            ).fetchone()
            return row["entity_id"]

    def _reddit_post(self, doc_id, subreddit_entity_id):
        """corpus.reddit_posts.fullname FKs to raw.reddit_posts -- seed the
        raw-layer row first so the FK is satisfiable (mirrors
        test_api_queries_propaganda.py's _reddit_post helper)."""
        from analysis.src.common import db as dbmod
        fullname = f"t3_{doc_id}"
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO raw.reddit_posts (fullname, raw_hash, extraction_version) "
                "VALUES (%s, 'deadbeef', 'test')",
                (fullname,),
            )
            conn.execute(
                "INSERT INTO corpus.reddit_posts (doc_id, fullname, subreddit_entity_id) "
                "VALUES (%s, %s, %s)",
                (doc_id, fullname, subreddit_entity_id),
            )

    def _author(self, handle, *, followers_count=None, account_created_at=None):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.authors "
                "(platform, platform_author_id, handle, followers_count, account_created_at) "
                "VALUES ('x'::corpus.platform, %s, %s, %s, %s) RETURNING author_id",
                (handle, handle, followers_count, account_created_at),
            ).fetchone()
            return row["author_id"]

    def _author_profile(self, author_id, entity_id, *, tier="elected_official"):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO corpus.author_profiles (author_id, tier, method, entity_id, classified_at) "
                "VALUES (%s, %s::corpus.author_tier, 'curated_list'::corpus.classification_method, %s, now())",
                (author_id, tier, entity_id),
            )

    def _doc(
        self, natural_key, *, source_type="x_post", author_id=None,
        published_at=None, admission_class="sampled", source_url=None,
        domain_or_subreddit=None, body="body text",
    ):
        from analysis.src.common import db as dbmod
        published_at = published_at or datetime.now(timezone.utc)
        source_url = source_url or f"https://example.com/{natural_key}"
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, author_id, published_at, body, "
                " source_url, raw_hash, etl_version, admission_class, domain_or_subreddit) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, %s, %s, "
                "        'deadbeef', 'test', %s::corpus.admission_class, %s) "
                "RETURNING doc_id",
                (source_type, natural_key, author_id, published_at, body, source_url,
                 admission_class, domain_or_subreddit),
            ).fetchone()
            return row["doc_id"]

    def _run(self, task, doc_id, *, status="done", is_current=True,
              model_id="test-model", confidence=0.9, raw_response=None):
        from analysis.src.common import db as dbmod
        from psycopg.types.json import Jsonb
        raw_response_param = Jsonb(raw_response) if raw_response is not None else None
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, confidence, is_current, raw_response) "
                "VALUES (%s::analysis.task, %s, %s::analysis.run_status, %s, "
                "        'hybrid'::analysis.inference_method, %s, %s, %s) "
                "RETURNING run_id",
                (task, doc_id, status, model_id, confidence, is_current, raw_response_param),
            ).fetchone()
            return row["run_id"]

    def _bot_signals(self, run_id, doc_id, label, **stylometrics):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.bot_signals "
                "(run_id, doc_id, label, llm_text_likelihood, burstiness, type_token_ratio, template_score) "
                "VALUES (%s, %s, %s::analysis.bot_label, %s, %s, %s, %s)",
                (run_id, doc_id, label,
                 stylometrics.get("llm_text_likelihood"), stylometrics.get("burstiness"),
                 stylometrics.get("type_token_ratio"), stylometrics.get("template_score")),
            )

    def _author_bot_score(self, author_id, flagged_share, *, sample_count=5, followers_count=None):
        """Seeds analysis.author_bot_scores directly with a bot_post_count
        chosen so (bot_post_count / sample_count) == flagged_share -- the
        label-driven flagged-share gate the exclusion predicate now reads,
        replacing the retired additive `score` column."""
        from analysis.src.common import db as dbmod
        if followers_count is not None:
            with dbmod.connection() as conn:
                conn.execute(
                    "UPDATE corpus.authors SET followers_count = %s WHERE author_id = %s",
                    (followers_count, author_id),
                )
        bot_post_count = round(flagged_share * sample_count)
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.author_bot_scores "
                "(author_id, bot_post_count, suspicious_post_count, sample_count, updated_at) "
                "VALUES (%s, %s, 0, %s, now())",
                (author_id, bot_post_count, sample_count),
            )

    def _author_lean(self, author_id, lean, lean_share=0.8, confidence=0.6, sample_count=10):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.author_leans "
                "(author_id, lean, lean_share, lean_confidence, stance_sample_count, computed_at) "
                "VALUES (%s, %s::corpus.political_lean, %s, %s, %s, now())",
                (author_id, lean, lean_share, confidence, sample_count),
            )

    def _clustering_run(self):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.clustering_runs (mode, threshold, started_at) "
                "VALUES ('jaccard', 0.5, now()) RETURNING clustering_run_id",
            ).fetchone()
            return row["clustering_run_id"]

    def _narrative(self, clustering_run_id, name="test narrative"):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.narratives (clustering_run_id, name) VALUES (%s, %s) "
                "RETURNING narrative_id",
                (clustering_run_id, name),
            ).fetchone()
            return row["narrative_id"]

    def _narrative_doc(self, narrative_id, doc_id, confidence=0.9):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.narrative_docs (narrative_id, doc_id, confidence) VALUES (%s, %s, %s)",
                (narrative_id, doc_id, confidence),
            )

    # -- tests --------------------------------------------------------------

    def test_automation_rate_keys_off_author_flagged_share_not_doc_label(self):
        # The binding rule this exists to encode: automation rate is
        # "bot-scored authors' share of in-window analyzed docs" -- an
        # author whose flagged share (bot_post_count + suspicious_post_count
        # over sample_count) is at/above the exclusion threshold counts even
        # if THIS PARTICULAR doc was labeled human.
        bot_author = self._author("bot-handle")
        human_author = self._author("human-handle")
        self._author_bot_score(bot_author, BOT_FLAGGED_SHARE_EXCLUSION + 0.1)
        self._author_bot_score(human_author, BOT_FLAGGED_SHARE_EXCLUSION - 0.1)
        doc_a = self._doc("doc-a", author_id=bot_author)
        doc_b = self._doc("doc-b", author_id=human_author)
        run_a = self._run("bot", doc_a)
        self._bot_signals(run_a, doc_a, "human")  # per-doc label says human
        run_b = self._run("bot", doc_b)
        self._bot_signals(run_b, doc_b, "human")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(result.analyzed_doc_count, 2)
        self.assertEqual(result.bot_scored_doc_count, 1)
        self.assertEqual(result.automation_rate_pct, 50.0)

    def test_behavioral_signal_breakdown_averages_by_label(self):
        author = self._author("h1")
        doc_a = self._doc("doc-a", author_id=author)
        doc_b = self._doc("doc-b", author_id=author)
        run_a = self._run("bot", doc_a)
        self._bot_signals(run_a, doc_a, "bot", burstiness=0.2)
        run_b = self._run("bot", doc_b)
        self._bot_signals(run_b, doc_b, "bot", burstiness=0.6)

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        bucket = next(b for b in result.behavioral_signals if b.label == "bot")
        self.assertEqual(bucket.doc_count, 2)
        self.assertAlmostEqual(bucket.avg_burstiness, 0.4)

    def test_account_age_bucket_counts_distinct_authors_once(self):
        now = datetime.now(timezone.utc)
        author = self._author("h1", account_created_at=now - timedelta(days=3))
        self._author_bot_score(author, 0.9)
        doc_a = self._doc("doc-a", author_id=author)
        doc_b = self._doc("doc-b", author_id=author)
        for doc_id in (doc_a, doc_b):
            run_id = self._run("bot", doc_id)
            self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        bucket = next(b for b in result.account_age_buckets if b.age_range == "< 7 days")
        self.assertEqual(bucket.account_count, 1)

    def test_entity_bot_rate_only_covers_registry_resolved_authors(self):
        entity_id = self._entity("sen-test", display_name="Senator Test")
        linked_author = self._author("linked")
        self._author_profile(linked_author, entity_id)
        unlinked_author = self._author("unlinked")
        linked_doc = self._doc("linked-doc", author_id=linked_author)
        unlinked_doc = self._doc("unlinked-doc", author_id=unlinked_author)
        run_a = self._run("bot", linked_doc)
        self._bot_signals(run_a, linked_doc, "bot")
        run_b = self._run("bot", unlinked_doc)
        self._bot_signals(run_b, unlinked_doc, "human")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(len(result.by_entity), 1)
        self.assertEqual(result.by_entity[0].entity_key, "sen-test")
        self.assertEqual(result.by_entity[0].total_docs, 1)
        self.assertEqual(result.by_entity[0].bot_rate_pct, 100.0)

    def test_bot_pushed_narrative_suppressed_below_sample_floor(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        bot_author = self._author("bot-handle")
        self._author_bot_score(bot_author, 0.9)
        doc_a = self._doc("doc-a", author_id=bot_author)
        doc_b = self._doc("doc-b", author_id=bot_author)
        self._narrative_doc(narrative_id, doc_a)
        self._narrative_doc(narrative_id, doc_b)  # only 2 member docs -- below MIN_TARGET_SAMPLE_N=5

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(result.bot_pushed_narratives, [])

    def test_bot_pushed_narrative_reported_at_or_above_sample_floor(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        bot_author = self._author("bot-handle")
        human_author = self._author("human-handle")
        self._author_bot_score(bot_author, 0.9)
        self._author_bot_score(human_author, 0.1)
        for i in range(4):
            doc_id = self._doc(f"bot-doc-{i}", author_id=bot_author)
            self._narrative_doc(narrative_id, doc_id)
        human_doc = self._doc("human-doc", author_id=human_author)
        self._narrative_doc(narrative_id, human_doc)

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        item = next(n for n in result.bot_pushed_narratives if n.narrative_id == narrative_id)
        self.assertEqual(item.member_doc_count, 5)
        self.assertEqual(item.bot_authored_doc_count, 4)
        self.assertEqual(item.bot_fraction_pct, 80.0)

    def test_flagged_account_requires_footprint_floor(self):
        # MIN_SAMPLED_AUTHOR_POSTS/FOLLOWERS: a bot-scored author with too
        # thin a footprint never gets its own example card.
        thin_author = self._author("thin")
        self._author_bot_score(thin_author, 0.9, sample_count=1, followers_count=5)
        doc_id = self._doc("thin-doc", author_id=thin_author)
        run_id = self._run("bot", doc_id)
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(result.flagged_accounts, [])
        # But the doc itself still surfaces in the standalone flagged-docs list.
        self.assertEqual(len(result.flagged_docs), 1)

    def test_flagged_account_lean_omitted_without_author_leans_row(self):
        author = self._author("no-lean", followers_count=5000)
        self._author_bot_score(author, 0.9, sample_count=5)
        doc_id = self._doc("doc-1", author_id=author)
        run_id = self._run("bot", doc_id)
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        account = next(a for a in result.flagged_accounts if a.author_id == author)
        self.assertIsNone(account.lean)

    def test_flagged_account_lean_carries_derived_evidence_when_present(self):
        author = self._author("has-lean", followers_count=5000)
        self._author_bot_score(author, 0.9, sample_count=5)
        self._author_lean(author, "democrat", lean_share=0.7, confidence=0.5, sample_count=8)
        doc_id = self._doc("doc-1", author_id=author)
        run_id = self._run("bot", doc_id)
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        account = next(a for a in result.flagged_accounts if a.author_id == author)
        self.assertIsNotNone(account.lean)
        self.assertEqual(account.lean.kind, "derived")
        self.assertEqual(account.lean.value, "democrat")
        self.assertEqual(account.lean.sample_count, 8)

    def test_range_meta_model_ids_reflects_mixed_seeded_models(self):
        author = self._author("h1")
        doc_a = self._doc("doc-a", author_id=author)
        doc_b = self._doc("doc-b", author_id=author)
        run_a = self._run("bot", doc_a, model_id="bot-detector-v1")
        self._bot_signals(run_a, doc_a, "human")
        run_b = self._run("bot", doc_b, model_id="bot-detector-v2")
        self._bot_signals(run_b, doc_b, "human")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(result.range.model_ids, ["bot-detector-v1", "bot-detector-v2"])

    def test_custom_date_range_scopes_result(self):
        author = self._author("h1")
        in_range_doc = self._doc(
            "in-range", author_id=author, published_at=datetime(2026, 3, 15, tzinfo=timezone.utc),
        )
        out_of_range_doc = self._doc(
            "out-of-range", author_id=author, published_at=datetime(2020, 1, 1, tzinfo=timezone.utc),
        )
        for doc_id in (in_range_doc, out_of_range_doc):
            run_id = self._run("bot", doc_id)
            self._bot_signals(run_id, doc_id, "human")

        result = bots.get_bot_activity(
            start=datetime(2026, 3, 1, tzinfo=timezone.utc),
            end=datetime(2026, 3, 31, tzinfo=timezone.utc),
            window_label=None,
        )
        self.assertEqual(result.analyzed_doc_count, 1)
        self.assertIsNone(result.range.window)

    def test_low_confidence_run_excluded_from_aggregate(self):
        author = self._author("h1")
        doc_id = self._doc("doc-a", author_id=author)
        run_id = self._run("bot", doc_id, confidence=0.1)  # below aggregation_min_confidence default
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(result.analyzed_doc_count, 0)

    def test_posting_cadence_and_coordination_index_reflect_bot_flagged_docs_only(self):
        # Binding rule: the cadence histogram (and the coordination index
        # derived from it) counts only label='bot' docs -- a human-labeled
        # doc from the same author posted at the same hour must not
        # inflate either number.
        bot_author = self._author("cadence-bot")
        same_hour = datetime(2026, 4, 1, 14, tzinfo=timezone.utc)
        other_hour = datetime(2026, 4, 1, 20, tzinfo=timezone.utc)
        for i, published_at in enumerate([same_hour, same_hour, other_hour]):
            doc_id = self._doc(f"bot-cadence-{i}", author_id=bot_author, published_at=published_at)
            run_id = self._run("bot", doc_id)
            self._bot_signals(run_id, doc_id, "bot")
        human_doc = self._doc("human-cadence", author_id=bot_author, published_at=same_hour)
        human_run = self._run("bot", human_doc)
        self._bot_signals(human_run, human_doc, "human")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(len(result.posting_cadence), 24)
        bucket_14 = next(b for b in result.posting_cadence if b.hour == 14)
        bucket_20 = next(b for b in result.posting_cadence if b.hour == 20)
        self.assertEqual(bucket_14.doc_count, 2)
        self.assertEqual(bucket_20.doc_count, 1)
        self.assertAlmostEqual(result.coordination_index, 2 / 3, places=3)

    def test_refresh_author_bot_scores_feeds_the_flagged_share_exclusion(self):
        """End-to-end (not a hand-seeded author_bot_scores row): bot_detection
        .refresh_author_bot_scores() rolls HIGH-confidence bot-labelled docs
        into bot_post_count/sample_count, and the panel's flagged-account
        gate reads that rollup through BOT_FLAGGED_SHARE_EXCLUSION. A LOW-
        confidence 'bot' doc for the same author must not inflate the
        share -- the confidence floor (BOT_LABEL_MIN_CONFIDENCE) and the
        flagged-share gate earning their keep together, not as isolated
        units (docs/audit-trail/analysis/2026-07-25-bot-exclusion-gate.md)."""
        from analysis.src.engine import bot_detection

        author = self._author("rollup-author", followers_count=5000)
        for i in range(4):
            doc_id = self._doc(f"rollup-doc-{i}", author_id=author)
            run_id = self._run("bot", doc_id, confidence=0.9)
            self._bot_signals(run_id, doc_id, "bot")
        # A fifth doc labelled 'bot' but at LOW confidence -- must not count.
        low_conf_doc = self._doc("rollup-doc-lowconf", author_id=author)
        low_conf_run = self._run("bot", low_conf_doc, confidence=0.2)
        self._bot_signals(low_conf_run, low_conf_doc, "bot")

        written = bot_detection.refresh_author_bot_scores()
        self.assertEqual(written, 1)

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        account = next(a for a in result.flagged_accounts if a.author_id == author)
        # 4 of 5 sampled posts are HIGH-confidence 'bot' -- flagged_post_share
        # = 4/5 = 0.8, comfortably above BOT_FLAGGED_SHARE_EXCLUSION (0.5).
        # If the low-confidence doc had counted, the share would still be
        # 0.8 by coincidence of these numbers -- the assertion that matters
        # is the exact share, not merely ">= threshold".
        self.assertAlmostEqual(account.flagged_post_share, 0.8, places=6)

    def test_editorial_official_appears_in_by_official_with_samples_and_indicators(self):
        entity_id = self._entity("sen-official", display_name="Senator Official", editorial=True)
        author_id = self._author("sen-official-handle")
        self._author_profile(author_id, entity_id)
        doc_id = self._doc("official-doc", author_id=author_id, source_type="x_post")
        run_id = self._run(
            "bot", doc_id,
            raw_response={"llm": {
                "indicators": ["zero_followers_following_listed"], "reasoning": "templated phrasing",
            }},
        )
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        item = next(i for i in result.by_official if i.key == "sen-official")
        self.assertEqual(item.kind, "official")
        self.assertEqual(item.entity_profile.display_name, "Senator Official")
        self.assertEqual(item.total_docs, 1)
        self.assertEqual(item.bot_docs, 1)
        self.assertEqual(len(item.samples), 1)
        self.assertIn("Zero followers following listed", item.samples[0].indicators)
        self.assertEqual(item.samples[0].reasoning, "templated phrasing")

    def test_non_editorial_account_and_subreddit_land_in_general_public(self):
        account_entity = self._entity("known-account", editorial=False)
        account_author = self._author("known-account-handle")
        self._author_profile(account_author, account_entity)
        account_doc = self._doc("account-doc", author_id=account_author, source_type="x_post")
        run_a = self._run("bot", account_doc)
        self._bot_signals(run_a, account_doc, "bot")

        subreddit_entity = self._entity("r-testpolitics", kind="subreddit")
        reddit_doc = self._doc(
            "reddit-doc", source_type="reddit_post", domain_or_subreddit="testpolitics",
        )
        self._reddit_post(reddit_doc, subreddit_entity)
        run_b = self._run("bot", reddit_doc)
        self._bot_signals(run_b, reddit_doc, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        gp_keys = {item.key: item for item in result.by_general_public}
        self.assertIn("known-account", gp_keys)
        self.assertEqual(gp_keys["known-account"].kind, "account")
        self.assertIn("r-testpolitics", gp_keys)
        self.assertEqual(gp_keys["r-testpolitics"].kind, "subreddit")
        self.assertEqual([i.key for i in result.by_official], [])

    def test_unmatched_x_post_folds_into_general_public_catch_all(self):
        from analysis.src.api.queries import profiles

        author = self._author("no-registry-match")
        doc_id = self._doc("catch-all-doc", author_id=author, source_type="x_post")
        run_id = self._run("bot", doc_id)
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        item = next(i for i in result.by_general_public if i.key == profiles.CATCH_ALL_X_USERS)
        self.assertEqual(item.kind, "catch_all")
        self.assertEqual(item.total_docs, 1)

    def test_news_docs_never_appear_in_either_entity_rollup(self):
        doc_id = self._doc("news-doc", source_type="news", domain_or_subreddit="example.com")
        run_id = self._run("bot", doc_id)
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(result.by_official, [])
        self.assertEqual(result.by_general_public, [])

    def test_coordination_stats_identical_text_pairs_always_none(self):
        author = self._author("coord-author")
        doc_a = self._doc("coord-doc-a", author_id=author)
        doc_b = self._doc("coord-doc-b", author_id=author)
        for doc_id in (doc_a, doc_b):
            run_id = self._run("bot", doc_id)
            self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertIsNone(result.coordination_stats.identical_text_pairs)
        # Both flagged docs share the same author -- reused once out of one
        # unique flagged author, and 2 flagged posts over that 1 author.
        self.assertAlmostEqual(result.coordination_stats.account_reuse, 1.0)
        self.assertAlmostEqual(result.coordination_stats.avg_posts_per_suspected_account, 2.0)

    def test_account_age_bucket_percentage_reflects_share_of_flagged_authors(self):
        now = datetime.now(timezone.utc)
        young_author = self._author("young", account_created_at=now - timedelta(days=3))
        old_author = self._author("old", account_created_at=now - timedelta(days=400 * 3))
        for author in (young_author, old_author):
            self._author_bot_score(author, 0.9)
            doc_id = self._doc(f"doc-{author}", author_id=author)
            run_id = self._run("bot", doc_id)
            self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        young_bucket = next(b for b in result.account_age_buckets if b.age_range == "< 7 days")
        self.assertAlmostEqual(young_bucket.percentage, 50.0)

    def test_total_flagged_posts_counts_bot_and_suspicious(self):
        author = self._author("flagged-mix")
        bot_doc = self._doc("mix-bot", author_id=author)
        suspicious_doc = self._doc("mix-suspicious", author_id=author)
        human_doc = self._doc("mix-human", author_id=author)
        for doc_id, label in ((bot_doc, "bot"), (suspicious_doc, "suspicious"), (human_doc, "human")):
            run_id = self._run("bot", doc_id)
            self._bot_signals(run_id, doc_id, label)

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(result.total_flagged_posts, 2)

    def test_posting_cadence_grid_has_168_cells_and_counts_bot_flagged_docs(self):
        author = self._author("grid-author")
        published_at = datetime(2026, 1, 5, 14, tzinfo=timezone.utc)  # a Monday
        doc_id = self._doc("grid-doc", author_id=author, published_at=published_at)
        run_id = self._run("bot", doc_id)
        self._bot_signals(run_id, doc_id, "bot")

        result = bots.get_bot_activity(start=None, end=None, window_label="all")
        self.assertEqual(len(result.posting_cadence_grid), 168)
        cell = next(c for c in result.posting_cadence_grid if c.day_of_week == 1 and c.hour == 14)
        self.assertEqual(cell.doc_count, 1)


if __name__ == "__main__":
    unittest.main()
