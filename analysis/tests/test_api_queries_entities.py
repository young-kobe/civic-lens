"""
Tests for analysis/src/api/queries/entities.py -- Phase 9 GET
/api/v1/entity-posts and GET /api/v1/entity-profile/{entity_id}.

Two tiers, matching the repo's established Postgres-redesign convention:

  1. Pure-core tests (no DB) -- the row-level helper functions.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL: an official's
     official_record post older than every preset window stays reachable
     (entity-posts un-windowed, entity-profile is always all-time), the
     mentions/authored_by/both relation tagging, stance-received vs
     stance-expressed, propaganda rate, and RangeMeta mixed-model tracking.
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timedelta, timezone

from analysis.src.api.queries import entities
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class PropagandaRateTests(unittest.TestCase):
    def test_no_analyzed_docs_is_none(self):
        rate, n = entities._propaganda_rate([{"techniques_validated": None}])
        self.assertIsNone(rate)
        self.assertEqual(n, 0)

    def test_rate_computed_over_analyzed_docs_only(self):
        rows = [
            {"techniques_validated": 2}, {"techniques_validated": 0},
            {"techniques_validated": None},
        ]
        rate, n = entities._propaganda_rate(rows)
        self.assertEqual(n, 2)
        self.assertEqual(rate, 0.5)


class MonthlyActivityTests(unittest.TestCase):
    def test_groups_by_month_and_admission_class(self):
        rows = [
            {"published_at": datetime(2026, 1, 5), "admission_class": "sampled"},
            {"published_at": datetime(2026, 1, 20), "admission_class": "official_record"},
            {"published_at": datetime(2026, 2, 1), "admission_class": "sampled"},
        ]
        months = entities._monthly_activity(rows)
        self.assertEqual([m.month for m in months], ["2026-01", "2026-02"])
        self.assertEqual(months[0].doc_count, 2)
        self.assertEqual(months[0].sampled_count, 1)
        self.assertEqual(months[0].official_record_count, 1)


class ActivityBoundsTests(unittest.TestCase):
    def test_empty_is_none_none(self):
        self.assertEqual(entities._activity_bounds([]), (None, None))

    def test_min_max_over_timestamps(self):
        rows = [{"published_at": datetime(2026, 1, 1)}, {"published_at": datetime(2026, 3, 1)}]
        first, last = entities._activity_bounds(rows)
        self.assertEqual(first, datetime(2026, 1, 1))
        self.assertEqual(last, datetime(2026, 3, 1))


class StanceDistributionTests(unittest.TestCase):
    def test_counts_by_sentiment_label(self):
        rows = [{"stance": "positive", "n": 4}, {"stance": "negative", "n": 1}]
        dist = entities._stance_distribution(rows)
        self.assertEqual(dist.positive, 4)
        self.assertEqual(dist.negative, 1)
        self.assertEqual(dist.net_score, 60.0)

    def test_below_floor_net_score_is_none(self):
        rows = [{"stance": "positive", "n": 2}]
        dist = entities._stance_distribution(rows)
        self.assertIsNone(dist.net_score)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class EntityQueriesIntegrationTests(unittest.TestCase):
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

    def _seed_document(
        self, source_type: str, published_at: datetime, author_id=None,
        admission_class: str = "sampled", title: str = "t",
    ) -> int:
        with self._conn() as conn:
            key = self._next_key("doc")
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, author_id, title, body, "
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, %s, 'body', "
                "'https://example.com/' || %s, 'h' || repeat('0', 63), 'test', "
                "%s::corpus.admission_class) RETURNING doc_id",
                (source_type, key, published_at, author_id, title, key, admission_class),
            ).fetchone()
            return row["doc_id"]

    def _seed_text_run(self, doc_id: int, confidence: float = 0.9, model_id: str = "gemini-3.5-flash") -> int:
        with self._conn() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, %s, "
                "'llm'::analysis.inference_method, %s, true) RETURNING run_id",
                (doc_id, model_id, confidence),
            ).fetchone()
            return row["run_id"]

    def _seed_targets_run(self, doc_id: int, model_id: str = "gemini-3.5-flash") -> int:
        with self._conn() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, is_current) "
                "VALUES ('targets'::analysis.task, %s, 'done'::analysis.run_status, %s, "
                "'llm'::analysis.inference_method, true) RETURNING run_id",
                (doc_id, model_id),
            ).fetchone()
            return row["run_id"]

    def _seed_target_mention(self, run_id, doc_id, raw_target, entity_id, stance, confidence=0.9):
        with self._conn() as conn:
            conn.execute(
                "INSERT INTO analysis.target_mentions (run_id, doc_id, raw_target, entity_id, stance, confidence) "
                "VALUES (%s, %s, %s, %s, %s::analysis.sentiment_label, %s)",
                (run_id, doc_id, raw_target, entity_id, stance, confidence),
            )

    def _seed_propaganda_run(self, doc_id, techniques_validated, model_id="gemini-3.5-flash"):
        with self._conn() as conn:
            run = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, is_current) "
                "VALUES ('propaganda'::analysis.task, %s, 'done'::analysis.run_status, %s, "
                "'llm'::analysis.inference_method, true) RETURNING run_id",
                (doc_id, model_id),
            ).fetchone()
            conn.execute(
                "INSERT INTO analysis.propaganda_results (run_id, techniques_validated) VALUES (%s, %s)",
                (run["run_id"], techniques_validated),
            )

    # --- tests -----------------------------------------------------------

    def test_unknown_entity_raises(self):
        with self.assertRaises(ValueError):
            entities.get_entity_posts(999999, window="all")
        with self.assertRaises(ValueError):
            entities.get_entity_profile(999999)

    def test_official_record_post_older_than_90d_absent_from_90d_window(self):
        official = self._seed_entity("official", "Sen. Old Guard", lean="republican")
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=official)
        old_published = datetime.now(timezone.utc) - timedelta(days=400)
        old_doc = self._seed_document(
            "x_post", old_published, author_id=author, admission_class="official_record",
        )
        self._seed_text_run(old_doc)

        windowed = entities.get_entity_posts(official, window="90d")
        self.assertEqual(windowed.total, 0)

        unwindowed = entities.get_entity_posts(official, window="all")
        self.assertEqual(unwindowed.total, 1)
        self.assertEqual(unwindowed.items[0].doc_id, old_doc)
        self.assertEqual(unwindowed.items[0].admission_class, "official_record")

    def test_custom_date_range_reaches_the_old_post_too(self):
        official = self._seed_entity("official", "Sen. Old Guard", lean="republican")
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=official)
        old_published = datetime.now(timezone.utc) - timedelta(days=400)
        old_doc = self._seed_document(
            "x_post", old_published, author_id=author, admission_class="official_record",
        )
        self._seed_text_run(old_doc)

        custom = entities.get_entity_posts(
            official, date_from=old_published - timedelta(days=1), date_to=old_published + timedelta(days=1),
        )
        self.assertEqual(custom.total, 1)
        self.assertIsNone(custom.window)

    def test_relation_tags_mentions_authored_by_and_both(self):
        official = self._seed_entity("official", "Sen. Example", lean="democrat")
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=official)

        own_post = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(own_post)

        other_author = self._seed_author()
        mentioning_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=other_author)
        self._seed_text_run(mentioning_doc)
        run_id = self._seed_targets_run(mentioning_doc)
        self._seed_target_mention(run_id, mentioning_doc, "Sen. Example", official, "positive")

        self_mention_run = self._seed_targets_run(own_post)
        self._seed_target_mention(self_mention_run, own_post, "Sen. Example", official, "neutral")

        result = entities.get_entity_posts(official, window="all")
        by_doc = {item.doc_id: item.relation for item in result.items}
        self.assertEqual(by_doc[mentioning_doc], "mentions")
        self.assertEqual(by_doc[own_post], "both")

    def test_analyzed_doc_counts_split_by_admission_class(self):
        official = self._seed_entity("official", "Sen. Example", lean="republican")
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=official)
        sampled_doc = self._seed_document(
            "x_post", datetime.now(timezone.utc), author_id=author, admission_class="sampled",
        )
        official_doc = self._seed_document(
            "x_post", datetime.now(timezone.utc), author_id=author, admission_class="official_record",
        )

        profile = entities.get_entity_profile(official)
        self.assertEqual(profile.analyzed_doc_counts.sampled, 1)
        self.assertEqual(profile.analyzed_doc_counts.official_record, 1)
        self.assertIsNotNone(profile.first_activity)
        self.assertIsNotNone(profile.last_activity)

    def test_stance_received_vs_expressed_are_distinct(self):
        official = self._seed_entity("official", "Sen. Example", lean="democrat")
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=official)

        # stance_expressed: a target_mentions row on a doc THIS official
        # authored, naming some other entity -- the official's own post
        # expressing a stance toward Rep. Other.
        own_post = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(own_post)
        other_entity = self._seed_entity("official", "Rep. Other", lean="republican")
        own_targets_run = self._seed_targets_run(own_post)
        self._seed_target_mention(own_targets_run, own_post, "Rep. Other", other_entity, "negative")

        # stance_received: a target_mentions row on someone ELSE's doc,
        # naming this official as the target.
        other_author = self._seed_author()
        about_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=other_author)
        self._seed_text_run(about_doc)
        run_id = self._seed_targets_run(about_doc)
        self._seed_target_mention(run_id, about_doc, "Sen. Example", official, "positive")

        profile = entities.get_entity_profile(official)
        self.assertEqual(profile.stance_received.positive, 1)
        self.assertEqual(profile.stance_expressed.negative, 1)

    def test_propaganda_rate_over_analyzed_docs(self):
        outlet = self._seed_entity("collective", "Party Collective")
        author = self._seed_author()
        self._seed_author_profile(author, "affiliated", entity_id=outlet)
        flagged_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_propaganda_run(flagged_doc, techniques_validated=2)
        clean_doc = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_propaganda_run(clean_doc, techniques_validated=0)

        profile = entities.get_entity_profile(outlet)
        self.assertEqual(profile.propaganda_analyzed_docs, 2)
        self.assertEqual(profile.propaganda_rate, 0.5)

    def test_entity_profile_range_meta_is_all_time_with_mixed_models(self):
        official = self._seed_entity("official", "Sen. Example", lean="republican")
        author = self._seed_author()
        self._seed_author_profile(author, "elected_official", entity_id=official)
        doc_a = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_text_run(doc_a, model_id="gemini-3.5-flash")
        doc_b = self._seed_document("x_post", datetime.now(timezone.utc), author_id=author)
        self._seed_targets_run(doc_b, model_id="qwen2.5:3b")

        profile = entities.get_entity_profile(official)
        self.assertIsNone(profile.range.start)
        self.assertIsNone(profile.range.end)
        self.assertEqual(profile.range.model_ids, ["gemini-3.5-flash", "qwen2.5:3b"])


if __name__ == "__main__":
    unittest.main()
