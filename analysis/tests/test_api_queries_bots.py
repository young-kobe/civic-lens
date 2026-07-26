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
                "analysis.author_bot_scores, analysis.runs, corpus.author_profiles, "
                "corpus.documents, corpus.authors, corpus.entities CASCADE"
            )

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
    ):
        from analysis.src.common import db as dbmod
        published_at = published_at or datetime.now(timezone.utc)
        source_url = source_url or f"https://example.com/{natural_key}"
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, author_id, published_at, body, "
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, 'body text', %s, "
                "        'deadbeef', 'test', %s::corpus.admission_class) "
                "RETURNING doc_id",
                (source_type, natural_key, author_id, published_at, source_url, admission_class),
            ).fetchone()
            return row["doc_id"]

    def _run(self, task, doc_id, *, status="done", is_current=True,
              model_id="test-model", confidence=0.9):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES (%s::analysis.task, %s, %s::analysis.run_status, %s, "
                "        'hybrid'::analysis.inference_method, %s, %s) "
                "RETURNING run_id",
                (task, doc_id, status, model_id, confidence, is_current),
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


if __name__ == "__main__":
    unittest.main()
