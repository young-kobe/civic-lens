"""
Tests for analysis/src/api/queries/sentiment/ -- Phase 9 GET
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
from analysis.src.api.queries.constants import MAX_SAMPLED_AUTHOR_CARDS, MIN_SAMPLED_AUTHOR_FOLLOWERS, MIN_SAMPLED_AUTHOR_POSTS
from analysis.src.api.queries.profiles import CATCH_ALL_OUTLETS, CATCH_ALL_SUBREDDITS, CATCH_ALL_X_USERS
from analysis.src.api.queries.sentiment import outbound, routing
from analysis.src.api.queries.sentiment import panel as sentiment_panel
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TopicForRowTests(unittest.TestCase):
    def test_resolved_topic_is_used(self):
        topic = routing._topic_for_row(1, {1: "Healthcare"})
        self.assertEqual(topic, "Healthcare")

    def test_unresolved_topic_is_general_never_a_keyword_guess(self):
        # Topic classification is an LLM judgment call (analysis.target_mentions),
        # never a title heuristic -- a doc the LLM never resolved a topic for
        # must render as the honest "General" bucket, not a keyword-guessed one.
        self.assertEqual(routing._topic_for_row(1, {}), "General")


class TimeOfDayTests(unittest.TestCase):
    def test_morning_boundary(self):
        self.assertEqual(routing._time_of_day(datetime(2026, 1, 1, 5)), "Morning")

    def test_afternoon(self):
        self.assertEqual(routing._time_of_day(datetime(2026, 1, 1, 14)), "Afternoon")

    def test_evening(self):
        self.assertEqual(routing._time_of_day(datetime(2026, 1, 1, 19)), "Evening")

    def test_night_wraps_past_midnight(self):
        self.assertEqual(routing._time_of_day(datetime(2026, 1, 1, 2)), "Night")


class TierForRowTests(unittest.TestCase):
    def test_news_source_is_news_tier(self):
        self.assertEqual(routing._tier_for_row("news", None), "news")

    def test_elected_official_x_post_is_officials_tier(self):
        self.assertEqual(routing._tier_for_row("x_post", "elected_official"), "officials")

    def test_unaffiliated_x_post_is_general_public(self):
        self.assertEqual(routing._tier_for_row("x_post", None), "general_public")

    def test_reddit_post_is_general_public(self):
        self.assertEqual(routing._tier_for_row("reddit_post", None), "general_public")


class NetScoreSuppressionTests(unittest.TestCase):
    def test_below_floor_is_none(self):
        counts = {"positive": 2, "negative": 1, "neutral": 0, "mixed": 0}
        self.assertIsNone(routing._net_score(counts))

    def test_at_floor_computes(self):
        counts = {"positive": 4, "negative": 1, "neutral": 0, "mixed": 0}
        self.assertEqual(routing._net_score(counts), 60.0)


class LeanLabelKindTests(unittest.TestCase):
    def test_official_is_fact(self):
        label = routing._lean_label("official", "republican")
        self.assertEqual(label.kind, "fact")
        self.assertEqual(label.value, "republican")

    def test_outlet_is_curated(self):
        label = routing._lean_label("outlet", "center-left")
        self.assertEqual(label.kind, "curated")


class RouteOwnPostTests(unittest.TestCase):
    """_route_own_post is the sole (tier, bucket_key) decision point for
    by_news_outlet/by_official/by_general_public -- every bucketing rule
    (Wave 2 sentiment) traces back to this function."""

    def _row(self, **overrides):
        base = {
            "source_type": "x_post", "outlet_entity_id": None, "subreddit_entity_id": None,
            "author_entity_id": None, "author_entity_kind": None, "author_entity_editorial": None,
            "x_handle": None,
        }
        base.update(overrides)
        return base

    def test_news_with_resolved_outlet_routes_to_entity_bucket(self):
        tier, key = routing._route_own_post(self._row(source_type="news", outlet_entity_id=7))
        self.assertEqual(tier, "news")
        self.assertEqual(key, ("entity", 7))

    def test_news_with_no_outlet_match_routes_to_catch_all(self):
        tier, key = routing._route_own_post(self._row(source_type="news"))
        self.assertEqual(tier, "news")
        self.assertEqual(key, ("catchall", CATCH_ALL_OUTLETS))

    def test_reddit_with_no_subreddit_match_routes_to_catch_all(self):
        tier, key = routing._route_own_post(self._row(source_type="reddit_post"))
        self.assertEqual(tier, "public")
        self.assertEqual(key, ("catchall", CATCH_ALL_SUBREDDITS))

    def test_editorial_official_author_routes_to_officials_tier(self):
        tier, key = routing._route_own_post(self._row(
            author_entity_id=3, author_entity_kind="official", author_entity_editorial=True,
        ))
        self.assertEqual(tier, "officials")
        self.assertEqual(key, ("entity", 3))

    def test_non_editorial_official_author_routes_to_public_as_curated_account(self):
        # kind='official' AND NOT editorial -- the curated-account case, not
        # the editorial-officials column.
        tier, key = routing._route_own_post(self._row(
            author_entity_id=3, author_entity_kind="official", author_entity_editorial=False,
        ))
        self.assertEqual(tier, "public")
        self.assertEqual(key, ("entity", 3))

    def test_unmatched_x_author_with_handle_gets_provisional_handle_bucket(self):
        tier, key = routing._route_own_post(self._row(x_handle="SomeHandle"))
        self.assertEqual(tier, "public")
        self.assertEqual(key, ("handle", "somehandle"))

    def test_unmatched_x_author_with_no_handle_falls_to_catch_all(self):
        tier, key = routing._route_own_post(self._row())
        self.assertEqual(tier, "public")
        self.assertEqual(key, ("catchall", CATCH_ALL_X_USERS))

    def test_unknown_source_type_is_unrouted(self):
        self.assertIsNone(routing._route_own_post(self._row(source_type="unknown")))


class ConsolidateSampledXAuthorsTests(unittest.TestCase):
    """The provisional per-handle bucket either survives as a named card
    (both floors cleared, within the card cap) or folds into the shared
    'other-x-users' catch-all -- pooled counts, never dropped."""

    def _handle_bucket(self, volume, followers):
        bucket = sentiment_panel._empty_entity_bucket("public")
        bucket["positive"] = volume
        bucket["followers"] = followers
        return bucket

    def test_qualifying_author_keeps_a_named_card(self):
        buckets = {("handle", "bigvoice"): self._handle_bucket(
            MIN_SAMPLED_AUTHOR_POSTS, MIN_SAMPLED_AUTHOR_FOLLOWERS,
        )}
        sentiment_panel._consolidate_sampled_x_authors(buckets)
        self.assertIn(("handle", "bigvoice"), buckets)
        self.assertNotIn(("catchall", CATCH_ALL_X_USERS), buckets)

    def test_sub_floor_author_folds_into_catch_all_counts_preserved(self):
        buckets = {("handle", "quiet"): self._handle_bucket(1, 10)}
        sentiment_panel._consolidate_sampled_x_authors(buckets)
        self.assertNotIn(("handle", "quiet"), buckets)
        catch_all = buckets[("catchall", CATCH_ALL_X_USERS)]
        self.assertEqual(catch_all["positive"], 1)

    def test_card_cap_demotes_overflow_by_volume(self):
        buckets = {}
        for i in range(MAX_SAMPLED_AUTHOR_CARDS + 1):
            buckets[("handle", f"h{i}")] = self._handle_bucket(
                MIN_SAMPLED_AUTHOR_POSTS + i, MIN_SAMPLED_AUTHOR_FOLLOWERS,
            )
        sentiment_panel._consolidate_sampled_x_authors(buckets)
        named = [k for k in buckets if k[0] == "handle"]
        self.assertEqual(len(named), MAX_SAMPLED_AUTHOR_CARDS)
        # The lowest-volume handle (h0) is the one demoted into the catch-all.
        self.assertNotIn(("handle", "h0"), buckets)
        self.assertIn(("catchall", CATCH_ALL_X_USERS), buckets)


class RouteReceivedSourceTests(unittest.TestCase):
    """_route_received_source is the sole (source_class, ident, lean)
    decision point for received_from_groups/_top -- the inbound mirror of
    RouteOwnPostTests. Every branch must land somewhere so shares sum to
    1.0, including a single explicit 'other' catch-all for unroutable
    rows."""

    def _row(self, **overrides):
        base = {
            "source_type": "x_post",
            "outlet_entity_id": None, "outlet_lean": None,
            "subreddit_entity_id": None, "subreddit_lean": None,
            "author_entity_id": None, "author_entity_kind": None,
            "author_entity_editorial": None, "author_entity_lean": None,
            "x_handle": None,
        }
        base.update(overrides)
        return base

    def test_news_with_resolved_outlet_carries_its_lean(self):
        source_class, ident, lean = routing._route_received_source(
            self._row(source_type="news", outlet_entity_id=7, outlet_lean="republican"),
        )
        self.assertEqual(source_class, "news_outlet")
        self.assertEqual(ident, 7)
        self.assertEqual(lean, "republican")

    def test_news_with_no_outlet_match_is_other(self):
        source_class, ident, lean = routing._route_received_source(self._row(source_type="news"))
        self.assertEqual(source_class, "other")
        self.assertIsNone(ident)
        self.assertIsNone(lean)

    def test_reddit_with_resolved_subreddit_carries_its_lean(self):
        source_class, ident, lean = routing._route_received_source(
            self._row(source_type="reddit_post", subreddit_entity_id=4, subreddit_lean="democrat"),
        )
        self.assertEqual(source_class, "subreddit")
        self.assertEqual(ident, 4)
        self.assertEqual(lean, "democrat")

    def test_reddit_with_no_subreddit_match_is_other(self):
        source_class, ident, lean = routing._route_received_source(self._row(source_type="reddit_post"))
        self.assertEqual(source_class, "other")

    def test_editorial_official_author_is_official_class(self):
        source_class, ident, lean = routing._route_received_source(self._row(
            author_entity_id=3, author_entity_kind="official", author_entity_editorial=True,
            author_entity_lean="independent",
        ))
        self.assertEqual(source_class, "official")
        self.assertEqual(ident, 3)
        self.assertEqual(lean, "independent")

    def test_non_editorial_official_author_is_account_class(self):
        source_class, ident, lean = routing._route_received_source(self._row(
            author_entity_id=3, author_entity_kind="official", author_entity_editorial=False,
        ))
        self.assertEqual(source_class, "account")
        self.assertEqual(ident, 3)

    def test_unmatched_x_author_with_handle_is_x_user(self):
        source_class, ident, lean = routing._route_received_source(self._row(x_handle="SomeHandle"))
        self.assertEqual(source_class, "x_user")
        self.assertEqual(ident, "somehandle")
        self.assertIsNone(lean)

    def test_unmatched_x_author_with_no_handle_is_other(self):
        source_class, ident, lean = routing._route_received_source(self._row())
        self.assertEqual(source_class, "other")
        self.assertIsNone(ident)

    def test_unknown_source_type_is_other(self):
        source_class, ident, lean = routing._route_received_source(self._row(source_type="unknown"))
        self.assertEqual(source_class, "other")


class NormalizePartyTests(unittest.TestCase):
    def test_r_normalizes_to_r(self):
        self.assertEqual(routing._normalize_party("R"), "R")

    def test_d_normalizes_to_d(self):
        self.assertEqual(routing._normalize_party("D"), "D")

    def test_independent_dem_caucuses_with_democrats(self):
        self.assertEqual(routing._normalize_party("independent-dem"), "D")

    def test_unrecognized_value_is_outside_the_alignment_frame(self):
        self.assertIsNone(routing._normalize_party("other"))

    def test_none_is_none(self):
        self.assertIsNone(routing._normalize_party(None))


class EntityNetScoreTests(unittest.TestCase):
    def test_zero_volume_is_zero_not_none(self):
        # Unlike received/topic/daily nets, an EntitySentimentItem's own
        # expressed net_score is never suppressed -- matches the
        # pre-redesign contract's non-optional netScore.
        self.assertEqual(sentiment_panel._entity_net_score({"positive": 0, "negative": 0, "neutral": 0}), 0.0)

    def test_computes_from_pos_neg_volume(self):
        counts = {"positive": 3, "negative": 1, "neutral": 1}
        self.assertEqual(sentiment_panel._entity_net_score(counts), 40.0)


class OutboundGroupingTests(unittest.TestCase):
    def _row(self, **overrides):
        base = {"entity_id": None, "display_name": None, "kind": None, "entity_key": None, "raw_target": None}
        base.update(overrides)
        return base

    def test_resolved_entity_groups_by_entity_id(self):
        group_key, seed = outbound._outbound_group(
            self._row(entity_id=9, display_name="Sen. Example", kind="official", entity_key="sen-example"),
        )
        self.assertEqual(group_key, ("entity", 9))
        self.assertEqual(seed["label"], "Sen. Example")

    def test_gop_alias_routes_to_gop_collective(self):
        group_key, seed = outbound._outbound_group(self._row(raw_target="Republicans"))
        self.assertEqual(group_key, ("collective", "gop"))
        self.assertEqual(seed["kind"], "collective")

    def test_dem_alias_routes_to_dem_collective(self):
        group_key, seed = outbound._outbound_group(self._row(raw_target="Democrats"))
        self.assertEqual(group_key, ("collective", "dem"))

    def test_unmatched_raw_target_groups_by_lowercased_text(self):
        group_key, seed = outbound._outbound_group(self._row(raw_target="Some Group"))
        self.assertEqual(group_key, ("raw", "some group"))
        self.assertEqual(seed["kind"], "raw")

    def test_blank_raw_target_has_nothing_to_group_under(self):
        group_key, seed = outbound._outbound_group(self._row(raw_target="  "))
        self.assertIsNone(group_key)
        self.assertIsNone(seed)


class FormatOutboundTests(unittest.TestCase):
    def _cell(self, kind, positive, negative=0):
        counts = routing._empty_counts()
        counts["positive"] = positive
        counts["negative"] = negative
        return {"label": "x", "kind": kind, "entity_key": None, "counts": counts}

    def test_raw_target_below_recurrence_floor_pools_into_other(self):
        # A one-off unresolved raw_target must never render as if it were
        # an identified entity -- it needs to recur first.
        groups = {("raw", "onceonly"): self._cell("raw", 1)}
        result = outbound._format_outbound(groups)
        self.assertEqual(len(result.targets), 1)
        self.assertEqual(result.targets[0].label, outbound.OUTBOUND_OTHER_LABEL)

    def test_recurring_raw_target_earns_its_own_row(self):
        groups = {("raw", "recurs"): self._cell("raw", outbound.MIN_OUTBOUND_RAW_RECURRENCE)}
        result = outbound._format_outbound(groups)
        self.assertEqual(len(result.targets), 1)
        self.assertNotEqual(result.targets[0].label, outbound.OUTBOUND_OTHER_LABEL)

    def test_overflow_beyond_cap_pools_into_other(self):
        groups = {
            ("entity", i): self._cell("official", outbound.MAX_OUTBOUND_TARGETS + 1 - i)
            for i in range(outbound.MAX_OUTBOUND_TARGETS + 1)
        }
        result = outbound._format_outbound(groups)
        named = [t for t in result.targets if t.label != outbound.OUTBOUND_OTHER_LABEL]
        other = [t for t in result.targets if t.label == outbound.OUTBOUND_OTHER_LABEL]
        self.assertEqual(len(named), outbound.MAX_OUTBOUND_TARGETS)
        self.assertEqual(len(other), 1)


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
            conn.execute(
                "TRUNCATE corpus.entities, corpus.authors, corpus.documents, "
                "raw.x_posts, raw.articles, raw.pages RESTART IDENTITY CASCADE"
            )

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

    def _seed_entity_full(
        self, kind: str, display_name: str, *, lean: str = "unknown",
        editorial: bool = False, blurb: str = "", lean_source: str = None,
    ) -> int:
        with self._conn() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities "
                "(entity_key, kind, display_name, lean, editorial, blurb, lean_source) "
                "VALUES (%s, %s::corpus.entity_kind, %s, %s::corpus.political_lean, %s, %s, %s) "
                "RETURNING entity_id",
                (self._next_key("entity"), kind, display_name, lean, editorial, blurb, lean_source),
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

    def _seed_author_with_followers(self, followers_count: int) -> int:
        with self._conn() as conn:
            handle = self._next_key("author")
            row = conn.execute(
                "INSERT INTO corpus.authors (platform, platform_author_id, handle, followers_count) "
                "VALUES ('x'::corpus.platform, %s, %s, %s) RETURNING author_id",
                (handle, handle, followers_count),
            ).fetchone()
            return row["author_id"]

    def _seed_news_article(self, doc_id: int, domain: str, outlet_entity_id=None) -> None:
        """corpus.news_articles.url_canon FKs raw.articles.url_canon, which
        FKs raw.pages.url_canon -- seed the whole capture-layer chain so the
        corpus row's FK resolves."""
        url_canon = f"https://{domain}/{doc_id}"
        with self._conn() as conn:
            conn.execute(
                "INSERT INTO raw.pages (url_canon, url_raw, domain, state) "
                "VALUES (%s, %s, %s, 'done'::raw.page_state)",
                (url_canon, url_canon, domain),
            )
            conn.execute(
                "INSERT INTO raw.articles (url_canon, domain, fetched_at, raw_hash, extraction_version) "
                "VALUES (%s, %s, now(), 'h' || repeat('0', 63), 'test')",
                (url_canon, domain),
            )
            conn.execute(
                "INSERT INTO corpus.news_articles (doc_id, url_canon, domain, extraction_version, outlet_entity_id) "
                "VALUES (%s, %s, %s, 'test', %s)",
                (doc_id, url_canon, domain, outlet_entity_id),
            )

    def _seed_x_post(
        self, doc_id: int, *, retweet_count: int = 0, reply_count: int = 0,
        like_count: int = 0, quote_count: int = 0,
    ) -> None:
        tweet_id = self._next_key("tweet")
        with self._conn() as conn:
            conn.execute(
                "INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, "
                "retweet_count, reply_count, like_count, quote_count, raw_hash, extraction_version) "
                "VALUES (%s, 'raw-author', now(), now(), 'raw text', %s, %s, %s, %s, "
                "'h' || repeat('0', 63), 'test')",
                (tweet_id, retweet_count, reply_count, like_count, quote_count),
            )
            conn.execute(
                "INSERT INTO corpus.x_posts (doc_id, tweet_id, retweet_count, reply_count, like_count, quote_count) "
                "VALUES (%s, %s, %s, %s, %s, %s)",
                (doc_id, tweet_id, retweet_count, reply_count, like_count, quote_count),
            )

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

    # --- Wave 2 sentiment: tier bucketing, outbound, collectives, engagement --

    def test_editorial_official_appears_in_by_official_with_profile_blurb(self):
        entity = self._seed_entity_full(
            "official", "Sen. Example", lean="republican", editorial=True, blurb="A US Senator.",
        )
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=entity)
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc, "positive")

        panel = sentiment.get_sentiment_panel(window="7d")
        item = next(i for i in panel.by_official if i.entity_profile.entity_id == entity)
        self.assertEqual(item.entity_profile.blurb, "A US Senator.")
        self.assertEqual(item.volume, 1)

    def test_sampled_author_threshold_and_other_x_users_fold(self):
        # Clears both floors -> named card. Below the post floor -> folds
        # into the shared "other-x-users" catch-all, counts preserved.
        qualifying_author = self._seed_author_with_followers(MIN_SAMPLED_AUTHOR_FOLLOWERS)
        for _ in range(MIN_SAMPLED_AUTHOR_POSTS):
            doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=qualifying_author)
            self._seed_text_run(doc, "positive")

        quiet_author = self._seed_author_with_followers(MIN_SAMPLED_AUTHOR_FOLLOWERS)
        quiet_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=quiet_author)
        self._seed_text_run(quiet_doc, "negative")

        panel = sentiment.get_sentiment_panel(window="7d")
        public_kinds = {item.kind for item in panel.by_general_public}
        self.assertIn("account", public_kinds)
        catch_all = next(i for i in panel.by_general_public if i.entity_profile.kind == "catch_all")
        self.assertEqual(catch_all.negative, 1)

    def test_outbound_raw_target_pooling(self):
        # A one-off unresolved raw_target on a news outlet's own posts must
        # never render as if it were an identified entity.
        outlet = self._seed_entity_full("outlet", "Example Times", lean="unknown")
        doc = self._seed_document("news", datetime.now(timezone.utc))
        self._seed_news_article(doc, "example.com", outlet_entity_id=outlet)
        self._seed_text_run(doc, "neutral")
        run_id = self._seed_targets_run(doc)
        self._seed_target_mention(run_id, doc, "Some One-Off Group", None, "negative")

        panel = sentiment.get_sentiment_panel(window="7d")
        item = next(i for i in panel.by_news_outlet if i.entity_profile.entity_id == outlet)
        self.assertIsNotNone(item.outbound)
        labels = {t.label for t in item.outbound.targets}
        self.assertIn(outbound.OUTBOUND_OTHER_LABEL, labels)
        self.assertNotIn("Some One-Off Group", labels)

    def test_collective_alias_match_builds_gop_received_tone(self):
        entity = self._seed_entity_full("official", "Sen. Example", lean="republican", editorial=True)
        author = self._seed_author()
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc, "neutral")
        run_id = self._seed_targets_run(doc)
        self._seed_target_mention(run_id, doc, "Republicans", None, "negative")

        panel = sentiment.get_sentiment_panel(window="7d")
        gop = panel.target_tone.collectives["gop_collective"]
        self.assertEqual(gop.volume, 1)
        _ = entity  # only needed so an official entity exists in the registry

    def test_engagement_weighted_net_withheld_below_sample_floor(self):
        # engagement_weighted_net shares received tone's MIN_TARGET_SAMPLE_N
        # suppression floor -- a single high-engagement mention must not
        # render a confident weighted headline.
        entity = self._seed_entity_full("official", "Sen. Example", lean="democrat", editorial=True)
        author = self._seed_author()
        doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_x_post(doc, like_count=10000, retweet_count=5000)
        self._seed_text_run(doc, "neutral")
        run_id = self._seed_targets_run(doc)
        self._seed_target_mention(run_id, doc, "Sen. Example", entity, "positive")

        panel = sentiment.get_sentiment_panel(window="7d")
        item = next(i for i in panel.by_official if i.entity_profile.entity_id == entity)
        self.assertIsNotNone(item.received)
        self.assertTrue(item.received.low_sample)
        self.assertIsNone(item.received.engagement_weighted_net)

    def test_received_provenance_groups_and_top_sources(self):
        # Full received-tone provenance scenario: one editorial official
        # receives tone from a news outlet, another editorial official's
        # x_post, an unresolved sampled x author, and one unattributed
        # ("other") mention -- shares must sum to 1.0, registry sources
        # must carry entity_key/profile, the sampled author must carry a
        # handle label with no profile-fetch crash, and low-n nets must
        # be withheld.
        target = self._seed_entity_full(
            "official", "Sen. Target", lean="democrat", editorial=True, blurb="The receiving official.",
        )

        outlet = self._seed_entity_full("outlet", "Example Times", lean="republican")
        news_doc = self._seed_document("news", datetime.now(timezone.utc))
        self._seed_news_article(news_doc, "example.com", outlet_entity_id=outlet)
        self._seed_text_run(news_doc, "neutral")
        news_targets_run = self._seed_targets_run(news_doc)
        self._seed_target_mention(news_targets_run, news_doc, "Sen. Target", target, "positive")

        speaker = self._seed_entity_full("official", "Sen. Speaker", lean="republican", editorial=True)
        speaker_author = self._seed_author()
        self._seed_author_profile(speaker_author, "elected_official", entity_id=speaker)
        speaker_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=speaker_author)
        self._seed_text_run(speaker_doc, "neutral")
        speaker_targets_run = self._seed_targets_run(speaker_doc)
        self._seed_target_mention(speaker_targets_run, speaker_doc, "Sen. Target", target, "negative")

        sampled_author = self._seed_author()
        sampled_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=sampled_author)
        self._seed_text_run(sampled_doc, "neutral")
        sampled_targets_run = self._seed_targets_run(sampled_doc)
        self._seed_target_mention(sampled_targets_run, sampled_doc, "Sen. Target", target, "neutral")

        other_doc = self._seed_document("news", datetime.now(timezone.utc))
        self._seed_news_article(other_doc, "unregistered.example", outlet_entity_id=None)
        self._seed_text_run(other_doc, "neutral")
        other_targets_run = self._seed_targets_run(other_doc)
        self._seed_target_mention(other_targets_run, other_doc, "Sen. Target", target, "mixed")

        panel = sentiment.get_sentiment_panel(window="7d")
        item = next(i for i in panel.by_official if i.entity_profile.entity_id == target)
        received = item.received
        self.assertIsNotNone(received)
        self.assertEqual(received.volume, 4)

        total_group_volume = sum(c.volume for c in received.received_from_groups)
        self.assertEqual(total_group_volume, received.volume)
        total_share = sum(c.share for c in received.received_from_groups)
        self.assertAlmostEqual(total_share, 1.0, places=3)
        for cell in received.received_from_groups:
            self.assertTrue(cell.low_sample)
            self.assertIsNone(cell.net)

        other_groups = [c for c in received.received_from_groups if c.source_class == "other"]
        self.assertEqual(len(other_groups), 1)
        self.assertEqual(other_groups[0].volume, 1)
        self.assertIsNone(other_groups[0].lean)

        top_classes = {c.source_class for c in received.received_from_top}
        self.assertNotIn("other", top_classes)

        outlet_cell = next(c for c in received.received_from_top if c.source_class == "news_outlet")
        self.assertIsNotNone(outlet_cell.entity_key)
        self.assertIsNotNone(outlet_cell.entity_profile)
        self.assertEqual(outlet_cell.lean, "republican")

        official_cell = next(c for c in received.received_from_top if c.source_class == "official")
        self.assertIsNotNone(official_cell.entity_key)
        self.assertIsNotNone(official_cell.entity_profile)

        x_user_cell = next(c for c in received.received_from_top if c.source_class == "x_user")
        self.assertIsNone(x_user_cell.entity_key)
        self.assertIsNotNone(x_user_cell.label)


if __name__ == "__main__":
    unittest.main()
