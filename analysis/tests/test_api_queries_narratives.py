"""
Tests for analysis/src/api/queries/narratives.py -- Phase 9 strictly-live
narratives panel.

Two tiers, matching the repo's established convention
(test_engine_lean_derivation.py, test_api_queries_base.py):

  1. Pure-core tests (no DB) -- _time_filter, _net_sentiment,
     _propaganda_fraction, _bot_pushed_fraction, _build_lean. Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with 0001-0004 applied, exercising get_narratives() end to
     end against seeded scenario rows.
"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timedelta, timezone

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.api.queries import narratives
from analysis.src.api.queries.constants import BOT_FLAGGED_SHARE_EXCLUSION
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class TimeFilterTests(unittest.TestCase):
    def test_no_bounds_yields_empty_clause_and_no_params(self):
        params = {}
        self.assertEqual(narratives._time_filter(None, None, params), "")
        self.assertEqual(params, {})

    def test_start_only_adds_lower_bound(self):
        params = {}
        start = datetime(2026, 1, 1, tzinfo=timezone.utc)
        clause = narratives._time_filter(start, None, params)
        self.assertIn(">=", clause)
        self.assertNotIn("<=", clause)
        self.assertEqual(params["_range_start"], start)

    def test_end_only_adds_upper_bound(self):
        params = {}
        end = datetime(2026, 1, 1, tzinfo=timezone.utc)
        clause = narratives._time_filter(None, end, params)
        self.assertIn("<=", clause)
        self.assertEqual(params["_range_end"], end)

    def test_both_bounds_present(self):
        params = {}
        start = datetime(2026, 1, 1, tzinfo=timezone.utc)
        end = datetime(2026, 2, 1, tzinfo=timezone.utc)
        clause = narratives._time_filter(start, end, params)
        self.assertIn(">=", clause)
        self.assertIn("<=", clause)


class NetSentimentTests(unittest.TestCase):
    def test_no_rows_is_none_not_zero(self):
        self.assertIsNone(narratives._net_sentiment([]))

    def test_unanimous_positive_scales_to_positive_hundred(self):
        rows = [("positive", 1.0), ("positive", 1.0)]
        self.assertEqual(narratives._net_sentiment(rows), 100.0)

    def test_unanimous_negative_scales_to_negative_hundred(self):
        self.assertEqual(narratives._net_sentiment([("negative", 1.0)]), -100.0)

    def test_neutral_rows_dilute_average_but_still_count_in_denominator(self):
        # A single unanimous-positive doc buried in a mostly-neutral
        # narrative must read close to zero, not +100 -- neutral/mixed
        # rows occupy a denominator slot without contributing signal.
        rows = [("positive", 1.0)] + [("neutral", 1.0)] * 49
        self.assertAlmostEqual(narratives._net_sentiment(rows), 2.0)

    def test_missing_confidence_defaults_to_full_weight(self):
        self.assertEqual(narratives._net_sentiment([("positive", None)]), 100.0)


class PropagandaFractionTests(unittest.TestCase):
    def test_no_rows_is_none_not_zero(self):
        # A narrative with zero propaganda-scored member docs must not read
        # as "0% flagged" -- that would misrepresent absent data as a clean
        # verdict.
        self.assertIsNone(narratives._propaganda_fraction([]))

    def test_fraction_is_share_of_docs_with_validated_technique(self):
        self.assertEqual(narratives._propaganda_fraction([0, 0, 1, 2]), 0.5)

    def test_none_validated_count_treated_as_zero_not_excluded(self):
        self.assertEqual(narratives._propaganda_fraction([None, 1]), 0.5)


class BotPushedFractionTests(unittest.TestCase):
    def test_no_scored_authors_is_none_not_zero(self):
        self.assertIsNone(narratives._bot_pushed_fraction([]))

    def test_fraction_at_or_above_exclusion_threshold_counts_as_bot_pushed(self):
        shares = [BOT_FLAGGED_SHARE_EXCLUSION, BOT_FLAGGED_SHARE_EXCLUSION - 0.1]
        self.assertEqual(narratives._bot_pushed_fraction(shares), 0.5)


class TierGroupTests(unittest.TestCase):
    def test_news_source_type_is_news_regardless_of_tier(self):
        self.assertEqual(narratives._tier_group("news", None), "news")
        self.assertEqual(narratives._tier_group("news", "elected_official"), "news")

    def test_elected_official_tier_is_officials(self):
        self.assertEqual(narratives._tier_group("x_post", "elected_official"), "officials")

    def test_affiliated_tier_is_officials(self):
        self.assertEqual(narratives._tier_group("x_post", "affiliated"), "officials")

    def test_general_public_tier_is_public(self):
        self.assertEqual(narratives._tier_group("x_post", "general_public"), "public")

    def test_no_tier_falls_back_to_public(self):
        # Unmatched X author (no author_profiles row) and every reddit doc
        # both land here -- coarse "not news, not an official" bucket.
        self.assertEqual(narratives._tier_group("x_post", None), "public")
        self.assertEqual(narratives._tier_group("reddit_post", None), "public")


class IsCrossTierTests(unittest.TestCase):
    def test_single_tier_group_is_not_cross_tier(self):
        rows = [("news", None), ("news", None)]
        self.assertFalse(narratives._is_cross_tier(rows))

    def test_news_and_officials_is_cross_tier(self):
        rows = [("news", None), ("x_post", "elected_official")]
        self.assertTrue(narratives._is_cross_tier(rows))

    def test_officials_and_public_is_cross_tier(self):
        rows = [("x_post", "elected_official"), ("reddit_post", None)]
        self.assertTrue(narratives._is_cross_tier(rows))

    def test_empty_rows_is_not_cross_tier(self):
        self.assertFalse(narratives._is_cross_tier([]))


class BuildLeanTests(unittest.TestCase):
    def test_no_row_omits_lean_entirely(self):
        # The binding rule this exists to enforce: a narrative with no
        # analysis.narrative_leans row must render with lean absent, never
        # a guessed/default value.
        self.assertIsNone(narratives._build_lean(None))

    def test_row_present_yields_derived_kind_with_full_evidence(self):
        row = {"lean": "democrat", "lean_share": 0.75, "confidence": 0.6, "doc_count": 12}
        lean = narratives._build_lean(row)
        self.assertEqual(lean, {
            "kind": "derived", "value": "democrat",
            "lean_share": 0.75, "confidence": 0.6, "sample_count": 12,
        })


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class GetNarrativesIntegrationTests(unittest.TestCase):
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
                "TRUNCATE analysis.narrative_leans, analysis.citations, "
                "analysis.narrative_docs, analysis.narratives, analysis.clustering_runs, "
                "analysis.claims, analysis.propaganda_techniques, analysis.propaganda_results, "
                "analysis.sentiment_results, analysis.author_bot_scores, analysis.runs, "
                "corpus.author_profiles, corpus.news_articles, corpus.documents, "
                "corpus.authors, corpus.entities, raw.articles, raw.pages CASCADE"
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
        self, natural_key, *, source_type="news", author_id=None,
        published_at=None, admission_class="sampled", title=None,
        body="body text", source_url=None,
    ):
        from analysis.src.common import db as dbmod
        published_at = published_at or datetime.now(timezone.utc)
        source_url = source_url or f"https://example.com/{natural_key}"
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, author_id, published_at, title, body, "
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, %s, %s, %s, "
                "        'deadbeef', 'test', %s::corpus.admission_class) "
                "RETURNING doc_id",
                (source_type, natural_key, author_id, published_at, title, body,
                 source_url, admission_class),
            ).fetchone()
            return row["doc_id"]

    def _run(self, task, doc_id, *, status="done", is_current=True,
              model_id="test-model", confidence=None):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES (%s::analysis.task, %s, %s::analysis.run_status, %s, "
                "        'llm'::analysis.inference_method, %s, %s) "
                "RETURNING run_id",
                (task, doc_id, status, model_id, confidence, is_current),
            ).fetchone()
            return row["run_id"]

    def _claim(self, run_id, doc_id, claim_text):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.claims (run_id, doc_id, claim_text) "
                "VALUES (%s, %s, %s) RETURNING claim_id",
                (run_id, doc_id, claim_text),
            ).fetchone()
            return row["claim_id"]

    def _sentiment(self, run_id, label):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.sentiment_results (run_id, label) "
                "VALUES (%s, %s::analysis.sentiment_label)",
                (run_id, label),
            )

    def _propaganda_result(self, run_id, *, density=0.0, techniques_validated=0):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.propaganda_results "
                "(run_id, density, techniques_validated) VALUES (%s, %s, %s)",
                (run_id, density, techniques_validated),
            )

    def _bot_score(self, author_id, flagged_share, *, sample_count=10):
        """Seeds a bot_post_count so bot_post_count/sample_count ==
        flagged_share -- the label-driven share the exclusion predicate
        reads (replacing the retired additive `score` column)."""
        from analysis.src.common import db as dbmod
        bot_post_count = round(flagged_share * sample_count)
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.author_bot_scores "
                "(author_id, bot_post_count, suspicious_post_count, sample_count, updated_at) "
                "VALUES (%s, %s, 0, %s, now())",
                (author_id, bot_post_count, sample_count),
            )

    def _clustering_run(self):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.clustering_runs (mode, threshold, started_at) "
                "VALUES ('jaccard', 0.5, now()) RETURNING clustering_run_id",
            ).fetchone()
            return row["clustering_run_id"]

    def _narrative(
        self, clustering_run_id, name="test narrative", anchor_claim_id=None,
        first_seen_at=None, first_seen_doc_id=None,
    ):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.narratives "
                "(clustering_run_id, name, anchor_claim_id, first_seen_at, first_seen_doc_id) "
                "VALUES (%s, %s, %s, %s, %s) RETURNING narrative_id",
                (clustering_run_id, name, anchor_claim_id, first_seen_at, first_seen_doc_id),
            ).fetchone()
            return row["narrative_id"]

    def _narrative_doc(self, narrative_id, doc_id, confidence=0.9):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.narrative_docs (narrative_id, doc_id, confidence) "
                "VALUES (%s, %s, %s)",
                (narrative_id, doc_id, confidence),
            )

    def _narrative_lean(self, narrative_id, lean, lean_share, confidence, doc_count):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.narrative_leans "
                "(narrative_id, lean, lean_share, confidence, doc_count, computed_at) "
                "VALUES (%s, %s::corpus.political_lean, %s, %s, %s, now())",
                (narrative_id, lean, lean_share, confidence, doc_count),
            )

    def _citation(self, source_doc_id, target_doc_id, run_id=None):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.citations "
                "(run_id, source_doc_id, target_doc_id, link_type) "
                "VALUES (%s, %s, %s, 'url_citation'::analysis.link_type)",
                (run_id, source_doc_id, target_doc_id),
            )

    def _basic_narrative_with_one_doc(self, *, published_at=None, claim_text="claim text"):
        clustering_run_id = self._clustering_run()
        doc_id = self._doc("doc-1", published_at=published_at)
        run_id = self._run("claims", doc_id)
        claim_id = self._claim(run_id, doc_id, claim_text)
        narrative_id = self._narrative(clustering_run_id, anchor_claim_id=claim_id)
        self._narrative_doc(narrative_id, doc_id)
        return narrative_id, doc_id

    def _outlet_entity(self, key, blurb):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, blurb) "
                "VALUES (%s, 'outlet'::corpus.entity_kind, %s, %s) RETURNING entity_id",
                (key, key, blurb),
            ).fetchone()
            return row["entity_id"]

    def _news_doc_with_outlet(self, natural_key, outlet_entity_id, *, published_at=None):
        """A news doc whose corpus.news_articles row resolves to
        ``outlet_entity_id`` -- exercises the outlet_entity_id ->
        EntityProfileModel path of first_seen_entity_profile."""
        from analysis.src.common import db as dbmod
        doc_id = self._doc(natural_key, source_type="news", published_at=published_at)
        url_canon = f"https://example.com/{natural_key}"
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO raw.pages (url_canon, url_raw, domain) VALUES (%s, %s, 'example.com')",
                (url_canon, url_canon),
            )
            conn.execute(
                "INSERT INTO raw.articles (url_canon, fetched_at, raw_hash, extraction_version) "
                "VALUES (%s, now(), 'h'||repeat('0', 63), 'v1')",
                (url_canon,),
            )
            conn.execute(
                "INSERT INTO corpus.news_articles (doc_id, url_canon, domain, outlet_entity_id) "
                "VALUES (%s, %s, 'example.com', %s)",
                (doc_id, url_canon, outlet_entity_id),
            )
        return doc_id

    # -- tests --------------------------------------------------------------

    def test_doc_count_and_anchor_claim_text(self):
        narrative_id, _doc_id = self._basic_narrative_with_one_doc(claim_text="the sky is blue")
        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(item["doc_count"], 1)
        self.assertEqual(item["anchor_claim_text"], "the sky is blue")

    def test_source_breakdown_labels_news_reddit_x(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        news_doc = self._doc("news-1", source_type="news")
        reddit_doc = self._doc("reddit-1", source_type="reddit_post")
        x_doc = self._doc("x-1", source_type="x_post")
        for doc_id in (news_doc, reddit_doc, x_doc):
            self._narrative_doc(narrative_id, doc_id)

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        sources = {row["source"]: row["doc_count"] for row in item["source_breakdown"]}
        self.assertEqual(sources, {"news": 1, "reddit": 1, "x": 1})

    def test_lean_omitted_without_narrative_leans_row(self):
        narrative_id, _doc_id = self._basic_narrative_with_one_doc()
        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertIsNone(item["lean"])

    def test_lean_present_carries_derived_evidence(self):
        narrative_id, _doc_id = self._basic_narrative_with_one_doc()
        self._narrative_lean(narrative_id, "democrat", 0.8, 0.6, 5)
        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(item["lean"], {
            "kind": "derived", "value": "democrat",
            "lean_share": 0.8, "confidence": 0.6, "sample_count": 5,
        })

    def test_citation_to_older_official_record_doc_resolves_with_no_window(self):
        # Owner decision 2026-07-24: a member doc inside the requested range
        # may cite a doc far outside it (here, an official_record doc six
        # months old); that citation must still render fully resolved, not
        # dropped as a dangling reference.
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        recent_doc = self._doc("recent", published_at=datetime.now(timezone.utc))
        self._narrative_doc(narrative_id, recent_doc)

        old_official_doc = self._doc(
            "old-official",
            published_at=datetime.now(timezone.utc) - timedelta(days=200),
            admission_class="official_record",
            source_url="https://example.com/old-official",
        )
        citation_run = self._run("citations", recent_doc, confidence=1.0)
        self._citation(recent_doc, old_official_doc, run_id=citation_run)

        # A tight 7-day window would exclude old_official_doc entirely if
        # the target were window-filtered.
        result = narratives.get_narratives(
            start=datetime.now(timezone.utc) - timedelta(days=7),
            end=None, limit=20, window_label="7d",
        )
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(item["citation_count"], 1)
        self.assertEqual(len(item["cited_docs"]), 1)
        cited = item["cited_docs"][0]
        self.assertEqual(cited["doc_id"], old_official_doc)
        self.assertEqual(cited["source_url"], "https://example.com/old-official")
        self.assertEqual(cited["admission_class"], "official_record")
        self.assertIsNotNone(cited["published_at"])

    def test_custom_date_range_scopes_ranking(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        in_range_doc = self._doc(
            "in-range", published_at=datetime(2026, 3, 15, tzinfo=timezone.utc),
        )
        out_of_range_doc = self._doc(
            "out-of-range", published_at=datetime(2020, 1, 1, tzinfo=timezone.utc),
        )
        self._narrative_doc(narrative_id, in_range_doc)
        self._narrative_doc(narrative_id, out_of_range_doc)

        result = narratives.get_narratives(
            start=datetime(2026, 3, 1, tzinfo=timezone.utc),
            end=datetime(2026, 3, 31, tzinfo=timezone.utc),
            limit=20, window_label=None,
        )
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(item["doc_count"], 1)

    def test_range_meta_model_ids_reflects_mixed_seeded_models(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        doc_a = self._doc("model-a-doc")
        doc_b = self._doc("model-b-doc")
        self._narrative_doc(narrative_id, doc_a)
        self._narrative_doc(narrative_id, doc_b)
        run_a = self._run("text", doc_a, model_id="sentiment-v1", confidence=0.9)
        self._sentiment(run_a, "positive")
        run_b = self._run("propaganda", doc_b, model_id="propaganda-v2", confidence=0.9)
        self._propaganda_result(run_b, density=0.1, techniques_validated=0)

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        self.assertEqual(result["range"]["model_ids"], ["propaganda-v2", "sentiment-v1"])

    def test_range_meta_admission_split(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        sampled_doc = self._doc("sampled-doc", admission_class="sampled")
        official_doc = self._doc("official-doc", admission_class="official_record")
        self._narrative_doc(narrative_id, sampled_doc)
        self._narrative_doc(narrative_id, official_doc)

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        self.assertEqual(result["range"]["sampled_doc_count"], 1)
        self.assertEqual(result["range"]["official_record_doc_count"], 1)

    def test_bot_pushed_fraction_measures_bot_authored_docs_not_excludes_them(self):
        # Binding rule exception: unlike every other rate in this panel,
        # bot_pushed_fraction is the metric that measures bot-authored docs
        # rather than filtering them out.
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        bot_author = self._author("bot-handle")
        human_author = self._author("human-handle")
        self._bot_score(bot_author, 0.9)
        self._bot_score(human_author, 0.1)
        bot_doc = self._doc("bot-doc", source_type="x_post", author_id=bot_author)
        human_doc = self._doc("human-doc", source_type="x_post", author_id=human_author)
        self._narrative_doc(narrative_id, bot_doc)
        self._narrative_doc(narrative_id, human_doc)

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(item["bot_pushed_fraction"], 0.5)

    def test_member_doc_samples_built_from_narrative_docs_confidence(self):
        narrative_id, doc_id = self._basic_narrative_with_one_doc()
        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(len(item["member_doc_samples"]), 1)
        sample = item["member_doc_samples"][0]
        self.assertEqual(sample["doc_id"], doc_id)
        self.assertEqual(sample["confidence"], 0.9)  # default in _narrative_doc

    def test_first_seen_columns_pass_through_from_narratives_row(self):
        clustering_run_id = self._clustering_run()
        doc_id = self._doc("first-seen-doc")
        first_seen_at = datetime(2026, 1, 1, tzinfo=timezone.utc)
        narrative_id = self._narrative(
            clustering_run_id, first_seen_at=first_seen_at, first_seen_doc_id=doc_id,
        )
        self._narrative_doc(narrative_id, doc_id)

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(item["first_seen_at"], first_seen_at)
        self.assertEqual(item["first_seen_doc_id"], doc_id)

    def test_first_seen_columns_none_when_unset(self):
        narrative_id, _doc_id = self._basic_narrative_with_one_doc()
        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertIsNone(item["first_seen_at"])
        self.assertIsNone(item["first_seen_doc_id"])

    def test_first_seen_entity_profile_carries_blurb_for_outlet_first_seen_doc(self):
        # Restoration contract: a narrative whose first-seen doc is an
        # outlet-authored news article must surface that outlet's editorial
        # profile (blurb included), not just the source_type/domain pair.
        clustering_run_id = self._clustering_run()
        outlet_entity_id = self._outlet_entity("example-outlet", "A test news outlet.")
        doc_id = self._news_doc_with_outlet("outlet-doc-1", outlet_entity_id)
        narrative_id = self._narrative(clustering_run_id, first_seen_doc_id=doc_id)
        self._narrative_doc(narrative_id, doc_id)

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertEqual(item["first_seen_source_type"], "news")
        self.assertEqual(item["first_seen_tier_group"], "news")
        self.assertIsNone(item["first_seen_author"])
        profile = item["first_seen_entity_profile"]
        self.assertIsNotNone(profile)
        self.assertEqual(profile.blurb, "A test news outlet.")
        self.assertFalse(item["cross_tier"])

    def test_mean_confidence_averages_all_member_docs_not_just_top_n(self):
        # Binding rule this exists to encode: mean_confidence must reflect
        # the WHOLE cluster, not the MAX_EVIDENCE_PER_SAMPLE-capped
        # member_doc_samples list -- seed more member docs than the sample
        # cap and confirm the mean still spans all of them.
        from analysis.src.api.queries.constants import MAX_EVIDENCE_PER_SAMPLE

        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        confidences = [0.9] * MAX_EVIDENCE_PER_SAMPLE + [0.1]
        for i, confidence in enumerate(confidences):
            doc_id = self._doc(f"conf-doc-{i}")
            self._narrative_doc(narrative_id, doc_id, confidence=confidence)

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        expected = round(sum(confidences) / len(confidences), 3)
        self.assertAlmostEqual(item["mean_confidence"], expected, places=3)

    def test_mean_confidence_none_without_any_confidence_rows(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        doc_id = self._doc("no-conf-doc")
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.narrative_docs (narrative_id, doc_id, confidence) "
                "VALUES (%s, %s, NULL)",
                (narrative_id, doc_id),
            )

        result = narratives.get_narratives(start=None, end=None, limit=20, window_label="all")
        item = next(n for n in result["narratives"] if n["narrative_id"] == narrative_id)
        self.assertIsNone(item["mean_confidence"])

    def test_no_narratives_in_range_returns_empty_list(self):
        result = narratives.get_narratives(
            start=datetime(1999, 1, 1, tzinfo=timezone.utc),
            end=datetime(1999, 1, 2, tzinfo=timezone.utc),
            limit=20, window_label=None,
        )
        self.assertEqual(result["narratives"], [])
        self.assertEqual(result["range"]["sampled_doc_count"], 0)


if __name__ == "__main__":
    unittest.main()
