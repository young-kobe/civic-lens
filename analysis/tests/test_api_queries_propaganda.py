"""
Tests for analysis/src/api/queries/propaganda.py -- Phase 9 strictly-live
propaganda panel.

Two tiers, matching the repo's established convention
(test_engine_lean_derivation.py, test_api_queries_base.py):

  1. Pure-core tests (no DB) -- _summarize_docs, _bucket_by_source,
     _bucket_by_party, _summarize_techniques, and the Wave 2 per-tier entity
     leaderboard/examples-by-entity helpers against synthetic row dicts.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with 0001-0004 applied, exercising get_propaganda_overview()
     end to end against seeded scenario rows.
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

from analysis.src.api.models.common import EntityProfileModel
from analysis.src.api.queries import propaganda
from analysis.src.api.queries.profiles import (
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
)
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

def _row(source_type="news", density=0.0, techniques_validated=0, party="unknown"):
    return {
        "source_type": source_type, "density": density,
        "techniques_validated": techniques_validated, "party": party,
    }


class SummarizeDocsTests(unittest.TestCase):
    def test_empty_rows_yields_zeroed_overview(self):
        overview = propaganda._summarize_docs([])
        self.assertEqual(overview["total_eligible_docs"], 0)
        self.assertEqual(overview["flagged_docs"], 0)
        self.assertEqual(overview["flagged_rate_pct"], 0.0)
        self.assertEqual(overview["mean_score"], 0.0)

    def test_deterministic_clean_rows_count_in_denominator_not_numerator(self):
        # The whole point of denominator=runs, not results: a deterministic
        # pre-filter-clean doc (density 0, no validated techniques) is a
        # real "not flagged" verdict and belongs in total_eligible_docs,
        # diluting the rate -- it must not be silently excluded.
        rows = [_row(density=0.0, techniques_validated=0)] * 3 + [
            _row(density=0.8, techniques_validated=2),
        ]
        overview = propaganda._summarize_docs(rows)
        self.assertEqual(overview["total_eligible_docs"], 4)
        self.assertEqual(overview["flagged_docs"], 1)
        self.assertEqual(overview["flagged_rate_pct"], 25.0)

    def test_mean_score_averages_density_across_all_eligible_docs(self):
        rows = [_row(density=0.0), _row(density=1.0)]
        overview = propaganda._summarize_docs(rows)
        self.assertEqual(overview["mean_score"], 0.5)


class BucketBySourceTests(unittest.TestCase):
    def test_news_and_social_split_correctly(self):
        rows = [
            _row(source_type="news", techniques_validated=1),
            _row(source_type="reddit_post", techniques_validated=0),
            _row(source_type="x_post", techniques_validated=1),
        ]
        buckets = {b["source"]: b for b in propaganda._bucket_by_source(rows)}
        self.assertEqual(buckets["news"]["total_docs"], 1)
        self.assertEqual(buckets["news"]["flagged_docs"], 1)
        self.assertEqual(buckets["social"]["total_docs"], 2)
        self.assertEqual(buckets["social"]["flagged_docs"], 1)

    def test_empty_bucket_reports_zero_rate_not_error(self):
        rows = [_row(source_type="news")]
        buckets = {b["source"]: b for b in propaganda._bucket_by_source(rows)}
        self.assertEqual(buckets["social"]["total_docs"], 0)
        self.assertEqual(buckets["social"]["flagged_rate_pct"], 0.0)


class BucketByPartyTests(unittest.TestCase):
    def test_unknown_party_bucketed_not_excluded(self):
        # Binding rule: entity-less authors bucket as 'unknown' rather than
        # being dropped from the by-party split.
        rows = [_row(party="unknown"), _row(party="democrat", techniques_validated=1)]
        buckets = {b["party"]: b for b in propaganda._bucket_by_party(rows)}
        self.assertIn("unknown", buckets)
        self.assertEqual(buckets["unknown"]["total_docs"], 1)
        self.assertEqual(buckets["democrat"]["flagged_docs"], 1)

    def test_buckets_sorted_alphabetically_by_party(self):
        rows = [_row(party="republican"), _row(party="democrat"), _row(party="unknown")]
        parties = [b["party"] for b in propaganda._bucket_by_party(rows)]
        self.assertEqual(parties, sorted(parties))


class BucketByTierTests(unittest.TestCase):
    def _row(self, source_type="x_post", tier=None, density=0.0, techniques_validated=0):
        return {
            "source_type": source_type, "tier": tier, "density": density,
            "techniques_validated": techniques_validated,
        }

    def test_news_source_type_always_buckets_as_news_regardless_of_tier(self):
        rows = [self._row(source_type="news", tier="elected_official")]
        buckets = {b["group"]: b for b in propaganda._bucket_by_tier(rows)}
        self.assertEqual(buckets["news"]["total_docs"], 1)
        self.assertEqual(buckets["officials"]["total_docs"], 0)

    def test_elected_official_tier_buckets_as_officials(self):
        rows = [self._row(tier="elected_official")]
        buckets = {b["group"]: b for b in propaganda._bucket_by_tier(rows)}
        self.assertEqual(buckets["officials"]["total_docs"], 1)

    def test_affiliated_and_no_tier_both_bucket_as_public(self):
        # Binding rule: the three-way split has no separate 'affiliated'
        # column -- affiliated authors and entity-less docs both collapse
        # into 'public' rather than being dropped.
        rows = [self._row(tier="affiliated"), self._row(tier=None)]
        buckets = {b["group"]: b for b in propaganda._bucket_by_tier(rows)}
        self.assertEqual(buckets["public"]["total_docs"], 2)

    def test_all_three_groups_present_even_at_zero_count(self):
        buckets = {b["group"] for b in propaganda._bucket_by_tier([])}
        self.assertEqual(buckets, {"news", "officials", "public"})


class SummarizeEntityRowsTests(unittest.TestCase):
    def _row(self, entity_id=1, entity_key="ent-1", display_name="Entity One",
             group="elected_official", density=0.5, techniques_validated=1):
        return {
            "entity_id": entity_id, "entity_key": entity_key, "display_name": display_name,
            "group": group, "density": density, "techniques_validated": techniques_validated,
        }

    def test_groups_by_entity_id_and_computes_share_and_mean(self):
        rows = [
            self._row(density=0.2, techniques_validated=0),
            self._row(density=0.8, techniques_validated=1),
        ]
        items = propaganda._summarize_entity_rows(rows)
        self.assertEqual(len(items), 1)
        item = items[0]
        self.assertEqual(item["doc_count"], 2)
        self.assertEqual(item["mean_density"], 0.5)
        self.assertEqual(item["flagged_share"], 0.5)

    def test_ranked_by_doc_count_descending(self):
        rows = (
            [self._row(entity_id=1, entity_key="e1")]
            + [self._row(entity_id=2, entity_key="e2")] * 3
        )
        items = propaganda._summarize_entity_rows(rows)
        self.assertEqual(items[0]["entity_id"], 2)

    def test_capped_at_max_leaderboard_rows(self):
        rows = [
            self._row(entity_id=i, entity_key=f"e{i}") for i in range(propaganda.MAX_ENTITY_LEADERBOARD_ROWS + 5)
        ]
        items = propaganda._summarize_entity_rows(rows)
        self.assertEqual(len(items), propaganda.MAX_ENTITY_LEADERBOARD_ROWS)


class ResolveTierAndKeyTests(unittest.TestCase):
    def _row(self, source_type, outlet_entity_id=None, subreddit_entity_id=None, author_entity_id=None):
        return {
            "source_type": source_type,
            "outlet_entity_id": outlet_entity_id,
            "subreddit_entity_id": subreddit_entity_id,
            "author_entity_id": author_entity_id,
        }

    def test_news_resolves_to_outlet_entity(self):
        profile = EntityProfileModel(kind="outlet", key="outlet-x", display_name="Outlet X")
        tier, key, resolved = propaganda._resolve_tier_and_key(
            self._row("news", outlet_entity_id=1), {1: profile},
        )
        self.assertEqual(tier, "news_outlet")
        self.assertEqual(key, "outlet-x")
        self.assertIs(resolved, profile)

    def test_news_with_no_outlet_falls_to_catch_all(self):
        tier, key, _resolved = propaganda._resolve_tier_and_key(self._row("news"), {})
        self.assertEqual(tier, "news_outlet")
        self.assertEqual(key, CATCH_ALL_OUTLETS)

    def test_editorial_official_x_post_lands_in_officials_tier(self):
        # kind == 'official' is the already-resolved ui-kind from
        # profiles.py's _entity_ui_kind -- 'official' means editorial
        # official or collective, per that module's mapping.
        profile = EntityProfileModel(
            kind="official", key="sen-x", display_name="Sen X", party="republican",
        )
        tier, _key, _resolved = propaganda._resolve_tier_and_key(
            self._row("x_post", author_entity_id=2), {2: profile},
        )
        self.assertEqual(tier, "official")

    def test_non_editorial_account_x_post_lands_in_general_public(self):
        # A resolved-but-non-editorial account still gets its own card, just
        # under general_public rather than official.
        profile = EntityProfileModel(kind="account", key="acct-x", display_name="Acct X")
        tier, key, _resolved = propaganda._resolve_tier_and_key(
            self._row("x_post", author_entity_id=3), {3: profile},
        )
        self.assertEqual(tier, "general_public")
        self.assertEqual(key, "acct-x")

    def test_reddit_post_always_general_public(self):
        profile = EntityProfileModel(kind="subreddit", key="sub-x", display_name="Sub X")
        tier, _key, _resolved = propaganda._resolve_tier_and_key(
            self._row("reddit_post", subreddit_entity_id=4), {4: profile},
        )
        self.assertEqual(tier, "general_public")

    def test_unresolved_reddit_post_falls_to_subreddit_catch_all(self):
        tier, key, _resolved = propaganda._resolve_tier_and_key(self._row("reddit_post"), {})
        self.assertEqual(tier, "general_public")
        self.assertEqual(key, CATCH_ALL_SUBREDDITS)

    def test_unresolved_x_post_falls_to_catch_all(self):
        _tier, key, _resolved = propaganda._resolve_tier_and_key(self._row("x_post"), {})
        self.assertEqual(key, CATCH_ALL_X_USERS)


class BucketByTierEntityTests(unittest.TestCase):
    def _news_row(self, outlet_entity_id, density, techniques_validated):
        return {
            "source_type": "news", "outlet_entity_id": outlet_entity_id,
            "subreddit_entity_id": None, "author_entity_id": None,
            "density": density, "techniques_validated": techniques_validated,
        }

    def test_ranked_by_flagged_rate_desc_within_tier(self):
        # binding rule: within a tier, entities rank by flagged_rate_pct, not
        # mean_score -- a lower-density but more-frequently-flagged outlet
        # must outrank a higher-density but rarely-flagged one.
        profiles = {
            1: EntityProfileModel(kind="outlet", key="a", display_name="A"),
            2: EntityProfileModel(kind="outlet", key="b", display_name="B"),
        }
        rows = [
            self._news_row(1, density=0.9, techniques_validated=1),
            self._news_row(2, density=0.1, techniques_validated=0),
        ]
        buckets = propaganda._bucket_by_tier_entity(rows, profiles)
        items = propaganda._finalize_tier_items(buckets["news_outlet"])
        self.assertEqual([item["key"] for item in items], ["a", "b"])
        self.assertEqual(items[0]["flagged_rate_pct"], 100.0)

    def test_catch_all_sorted_last_even_with_higher_rate(self):
        profiles = {1: EntityProfileModel(kind="outlet", key="a", display_name="A")}
        rows = [
            self._news_row(None, density=1.0, techniques_validated=1),  # catch-all, 100% flagged
            self._news_row(1, density=0.0, techniques_validated=0),     # named outlet, 0% flagged
        ]
        buckets = propaganda._bucket_by_tier_entity(rows, profiles)
        items = propaganda._finalize_tier_items(buckets["news_outlet"])
        self.assertEqual(items[-1]["kind"], "catch_all")

    def test_empty_slots_are_never_emitted(self):
        buckets = propaganda._bucket_by_tier_entity([], {})
        self.assertEqual(propaganda._finalize_tier_items(buckets["news_outlet"]), [])


class BuildExamplesByEntityTests(unittest.TestCase):
    def _row(self, doc_id, run_id, density, author_entity_id=None, body="body text"):
        return {
            "doc_id": doc_id, "run_id": run_id, "source_type": "x_post",
            "domain_or_subreddit": None, "title": None, "body": body,
            "source_url": "https://example.com/x", "density": density,
            "author_handle": "senx", "outlet_entity_id": None,
            "subreddit_entity_id": None, "author_entity_id": author_entity_id,
        }

    def test_examples_grouped_by_resolved_entity_key_carry_technique_spans(self):
        profile = EntityProfileModel(
            kind="official", key="sen-x", display_name="Sen X", party="republican",
        )
        rows = [self._row(1, 10, density=0.9, author_entity_id=2)]
        technique_rows = [
            {"run_id": 10, "technique": "loaded_language", "evidence_span": "vile", "confidence": 0.8},
        ]
        result = propaganda._build_examples_by_entity(rows, technique_rows, {2: profile})
        self.assertIn("sen-x", result)
        example = result["sen-x"][0]
        self.assertEqual(example["party"], "republican")
        self.assertEqual(example["techniques"][0]["technique"], "loaded_language")
        self.assertEqual(example["techniques"][0]["evidence_span"], "vile")

    def test_examples_capped_per_entity(self):
        profile = EntityProfileModel(kind="official", key="sen-x", display_name="Sen X")
        rows = [
            self._row(i, i, density=1.0 - i * 0.01, author_entity_id=2)
            for i in range(propaganda.EXAMPLES_PER_ENTITY + 3)
        ]
        result = propaganda._build_examples_by_entity(rows, [], {2: profile})
        self.assertEqual(len(result["sen-x"]), propaganda.EXAMPLES_PER_ENTITY)

    def test_party_none_for_non_editorial_account(self):
        # Binding rule: an example's own party field is populated only for
        # the editorial 'official' ui-kind, even though a non-editorial
        # account's EntityProfileModel may itself carry a party value.
        profile = EntityProfileModel(
            kind="account", key="acct-x", display_name="Acct X", party="democrat",
        )
        rows = [self._row(1, 10, density=0.5, author_entity_id=3)]
        result = propaganda._build_examples_by_entity(rows, [], {3: profile})
        self.assertIsNone(result["acct-x"][0]["party"])

    def test_text_preview_truncated_to_snippet_max(self):
        from analysis.src.api.queries.constants import SNIPPET_MAX_CHARS
        long_body = "x" * (SNIPPET_MAX_CHARS + 50)
        rows = [self._row(1, 10, density=0.5, author_entity_id=2, body=long_body)]
        profile = EntityProfileModel(kind="official", key="sen-x", display_name="Sen X")
        result = propaganda._build_examples_by_entity(rows, [], {2: profile})
        self.assertEqual(len(result["sen-x"][0]["text_preview"]), SNIPPET_MAX_CHARS)


class SummarizeTechniquesTests(unittest.TestCase):
    def test_every_enum_technique_present_even_at_zero_count(self):
        rows = [{"technique": "loaded_language", "evidence_span": "vile scheme"}]
        techniques = propaganda._summarize_techniques(rows, flagged_docs=1)
        names = {t["technique"] for t in techniques}
        self.assertEqual(names, set(propaganda.PROPAGANDA_TECHNIQUES))
        zero_count = next(t for t in techniques if t["technique"] == "name_calling")
        self.assertEqual(zero_count["count"], 0)
        self.assertEqual(zero_count["sample_evidence"], [])

    def test_pct_of_flagged_docs_uses_flagged_denominator(self):
        rows = [{"technique": "loaded_language", "evidence_span": "x"}] * 2
        techniques = propaganda._summarize_techniques(rows, flagged_docs=4)
        loaded = next(t for t in techniques if t["technique"] == "loaded_language")
        self.assertEqual(loaded["pct_of_flagged_docs"], 50.0)

    def test_zero_flagged_docs_yields_zero_pct_not_division_error(self):
        rows = [{"technique": "loaded_language", "evidence_span": "x"}]
        techniques = propaganda._summarize_techniques(rows, flagged_docs=0)
        loaded = next(t for t in techniques if t["technique"] == "loaded_language")
        self.assertEqual(loaded["pct_of_flagged_docs"], 0.0)

    def test_evidence_truncated_and_capped_per_technique(self):
        from analysis.src.api.queries.constants import SNIPPET_MAX_CHARS
        long_span = "x" * (SNIPPET_MAX_CHARS + 50)
        rows = [{"technique": "name_calling", "evidence_span": long_span}] * 5
        techniques = propaganda._summarize_techniques(rows, flagged_docs=5)
        name_calling = next(t for t in techniques if t["technique"] == "name_calling")
        self.assertEqual(len(name_calling["sample_evidence"]), propaganda.SAMPLE_EVIDENCE_PER_TECHNIQUE)
        self.assertEqual(len(name_calling["sample_evidence"][0]), SNIPPET_MAX_CHARS)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class GetPropagandaOverviewIntegrationTests(unittest.TestCase):
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
                "TRUNCATE analysis.propaganda_techniques, analysis.propaganda_results, "
                "analysis.author_bot_scores, analysis.runs, corpus.news_articles, "
                "corpus.reddit_posts, corpus.author_profiles, corpus.documents, "
                "corpus.authors, corpus.entities, raw.articles, raw.reddit_posts, "
                "raw.pages CASCADE"
            )

    # -- seeding helpers --------------------------------------------------

    def _entity(self, key, lean="unknown", kind="official", blurb=None, editorial=False):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean, blurb, editorial) "
                "VALUES (%s, %s::corpus.entity_kind, %s, %s::corpus.political_lean, %s, %s) "
                "RETURNING entity_id",
                (key, kind, key, lean, blurb, editorial),
            ).fetchone()
            return row["entity_id"]

    def _news_article(self, doc_id, outlet_entity_id):
        """corpus.news_articles.url_canon FKs to raw.articles, which FKs to
        raw.pages -- seed the raw-layer rows first so the FK is
        satisfiable (matches test_api_queries_docs.py's convention)."""
        from analysis.src.common import db as dbmod
        url_canon = f"https://example.com/canon/{doc_id}"
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO raw.pages (url_canon, url_raw, domain) VALUES (%s, %s, 'example.com')",
                (url_canon, url_canon),
            )
            conn.execute(
                "INSERT INTO raw.articles (url_canon, fetched_at, raw_hash, extraction_version) "
                "VALUES (%s, now(), 'deadbeef', 'test')",
                (url_canon,),
            )
            conn.execute(
                "INSERT INTO corpus.news_articles (doc_id, url_canon, outlet_entity_id) "
                "VALUES (%s, %s, %s)",
                (doc_id, url_canon, outlet_entity_id),
            )

    def _reddit_post(self, doc_id, subreddit_entity_id):
        """corpus.reddit_posts.fullname FKs to raw.reddit_posts -- seed the
        raw-layer row first so the FK is satisfiable."""
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
                "INSERT INTO corpus.author_profiles "
                "(author_id, tier, method, entity_id, classified_at) "
                "VALUES (%s, 'elected_official'::corpus.author_tier, "
                "        'curated_list'::corpus.classification_method, %s, now())",
                (author_id, entity_id),
            )

    def _doc(
        self, natural_key, *, source_type="news", author_id=None,
        published_at=None, admission_class="sampled",
    ):
        from analysis.src.common import db as dbmod
        published_at = published_at or datetime.now(timezone.utc)
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, author_id, published_at, body, "
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, 'body text', "
                "        %s, 'deadbeef', 'test', %s::corpus.admission_class) "
                "RETURNING doc_id",
                (source_type, natural_key, author_id, published_at,
                 f"https://example.com/{natural_key}", admission_class),
            ).fetchone()
            return row["doc_id"]

    def _run(self, task, doc_id, *, status="done", is_current=True,
              model_id="propaganda-v1", confidence=None):
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

    def _propaganda_result(self, run_id, *, density=0.0, techniques_validated=0, summary=None):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.propaganda_results "
                "(run_id, density, summary, techniques_validated) VALUES (%s, %s, %s, %s)",
                (run_id, density, summary, techniques_validated),
            )

    def _propaganda_technique(self, run_id, technique, evidence_span, confidence=0.9):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.propaganda_techniques "
                "(run_id, technique, evidence_span, confidence) "
                "VALUES (%s, %s::analysis.propaganda_technique, %s, %s)",
                (run_id, technique, evidence_span, confidence),
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

    def _flagged_doc(self, natural_key, *, density=0.5, techniques_validated=1,
                      confidence=0.8, model_id="propaganda-v1", **doc_kwargs):
        doc_id = self._doc(natural_key, **doc_kwargs)
        run_id = self._run("propaganda", doc_id, confidence=confidence, model_id=model_id)
        self._propaganda_result(run_id, density=density, techniques_validated=techniques_validated)
        return doc_id, run_id

    # -- tests --------------------------------------------------------------

    def test_deterministic_clean_run_counts_in_denominator(self):
        self._flagged_doc("clean-1", density=0.0, techniques_validated=0)
        self._flagged_doc("flagged-1", density=0.8, techniques_validated=1)
        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(overview["total_eligible_docs"], 2)
        self.assertEqual(overview["flagged_docs"], 1)
        self.assertEqual(overview["flagged_rate_pct"], 50.0)

    def test_bot_authored_doc_excluded_from_denominator(self):
        bot_author = self._author("bot-handle")
        self._bot_score(bot_author, 0.9)
        self._flagged_doc("bot-doc", author_id=bot_author, source_type="x_post")
        self._flagged_doc("human-doc", density=0.2, techniques_validated=0)

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(overview["total_eligible_docs"], 1)

    def test_technique_counts_come_from_techniques_table(self):
        _doc_id, run_id = self._flagged_doc("propaganda-doc", techniques_validated=2)
        self._propaganda_technique(run_id, "loaded_language", "vile scheme")
        self._propaganda_technique(run_id, "name_calling", "the enemy")

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        counts = {t["technique"]: t["count"] for t in overview["by_technique"]}
        self.assertEqual(counts["loaded_language"], 1)
        self.assertEqual(counts["name_calling"], 1)
        self.assertEqual(counts["ad_hominem"], 0)

    def test_by_party_buckets_unknown_authors(self):
        official_entity = self._entity("sen-x", "republican")
        official_author = self._author("sen-x-handle")
        self._author_profile(official_author, official_entity)
        self._flagged_doc(
            "official-doc", author_id=official_author, source_type="x_post",
            density=0.6, techniques_validated=1,
        )
        # No author_profiles row at all -- must land in 'unknown'.
        self._flagged_doc("no-profile-doc", density=0.1, techniques_validated=0)

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        parties = {p["party"]: p for p in overview["by_party"]}
        self.assertEqual(parties["republican"]["total_docs"], 1)
        self.assertEqual(parties["unknown"]["total_docs"], 1)

    def test_flagged_examples_via_build_sample_doc(self):
        doc_id, _run_id = self._flagged_doc(
            "example-doc", density=0.9, techniques_validated=1, confidence=0.85,
        )
        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(len(overview["examples"]), 1)
        example = overview["examples"][0]
        self.assertEqual(example["doc_id"], doc_id)
        self.assertEqual(example["confidence"], 0.85)
        self.assertIn("source_url", example)
        self.assertIn("admission_class", example)

    def test_custom_date_range_scopes_denominator(self):
        self._flagged_doc(
            "in-range", published_at=datetime(2026, 3, 15, tzinfo=timezone.utc),
            density=0.9, techniques_validated=1,
        )
        self._flagged_doc(
            "out-of-range", published_at=datetime(2020, 1, 1, tzinfo=timezone.utc),
            density=0.9, techniques_validated=1,
        )
        overview = propaganda.get_propaganda_overview(
            start=datetime(2026, 3, 1, tzinfo=timezone.utc),
            end=datetime(2026, 3, 31, tzinfo=timezone.utc),
            window_label=None,
        )
        self.assertEqual(overview["total_eligible_docs"], 1)

    def test_range_meta_model_ids_reflects_mixed_seeded_models(self):
        self._flagged_doc("model-a-doc", model_id="propaganda-v1")
        self._flagged_doc("model-b-doc", model_id="propaganda-v2")
        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(overview["range"]["model_ids"], ["propaganda-v1", "propaganda-v2"])

    def test_range_meta_admission_split(self):
        self._flagged_doc("sampled-doc", admission_class="sampled")
        self._flagged_doc("official-doc", admission_class="official_record")
        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(overview["range"]["sampled_doc_count"], 1)
        self.assertEqual(overview["range"]["official_record_doc_count"], 1)

    def test_by_tier_officials_bucket_reflects_author_profiles_tier(self):
        official_entity = self._entity("sen-x")
        official_author = self._author("sen-x-handle")
        self._author_profile(official_author, official_entity)
        self._flagged_doc(
            "official-doc", author_id=official_author, source_type="x_post",
            density=0.6, techniques_validated=1,
        )
        self._flagged_doc("news-doc", source_type="news", density=0.4, techniques_validated=0)

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        by_tier = {t["group"]: t for t in overview["by_tier"]}
        self.assertEqual(by_tier["officials"]["total_docs"], 1)
        self.assertEqual(by_tier["news"]["total_docs"], 1)
        self.assertEqual(by_tier["public"]["total_docs"], 0)

    def test_by_entity_resolves_via_news_outlet_entity_id(self):
        outlet_entity = self._entity("outlet-x", kind="outlet")
        doc_id, _run_id = self._flagged_doc(
            "outlet-doc", source_type="news", density=0.7, techniques_validated=1,
        )
        self._news_article(doc_id, outlet_entity)

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(len(overview["by_entity"]), 1)
        row = overview["by_entity"][0]
        self.assertEqual(row["entity_id"], outlet_entity)
        self.assertEqual(row["entity_key"], "outlet-x")
        self.assertEqual(row["group"], "news")
        self.assertEqual(row["doc_count"], 1)

    def test_by_entity_resolves_via_reddit_subreddit_entity_id(self):
        subreddit_entity = self._entity("subreddit-x", kind="subreddit")
        doc_id, _run_id = self._flagged_doc(
            "reddit-doc", source_type="reddit_post", density=0.5, techniques_validated=0,
        )
        self._reddit_post(doc_id, subreddit_entity)

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        row = next(r for r in overview["by_entity"] if r["entity_id"] == subreddit_entity)
        self.assertEqual(row["group"], "subreddit")
        self.assertEqual(row["entity_key"], "subreddit-x")

    def test_by_entity_resolves_via_author_profiles_entity_id_with_tier_group(self):
        official_entity = self._entity("sen-y")
        official_author = self._author("sen-y-handle")
        self._author_profile(official_author, official_entity)
        self._flagged_doc(
            "sen-y-doc", author_id=official_author, source_type="x_post",
            density=0.9, techniques_validated=1,
        )

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        row = next(r for r in overview["by_entity"] if r["entity_id"] == official_entity)
        self.assertEqual(row["group"], "elected_official")

    def test_by_entity_excludes_docs_with_no_resolvable_entity(self):
        self._flagged_doc("no-entity-doc", density=0.5, techniques_validated=1)
        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(overview["by_entity"], [])

    def test_no_eligible_docs_yields_zeroed_overview_not_error(self):
        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(overview["total_eligible_docs"], 0)
        self.assertEqual(overview["by_technique"][0]["count"], 0)
        self.assertEqual(overview["examples"], [])
        self.assertEqual(overview["by_entity"], [])
        self.assertEqual({t["group"] for t in overview["by_tier"]}, {"news", "officials", "public"})

    # -- Wave 2: per-tier entity leaderboards + examples_by_entity ----------

    def test_outlet_appears_in_by_news_outlet_with_profile_and_flagged_rate(self):
        outlet_entity = self._entity(
            "outlet-y", kind="outlet", blurb="A test news outlet.",
        )
        flagged_doc, _run = self._flagged_doc(
            "outlet-flagged", source_type="news", density=0.7, techniques_validated=1,
        )
        self._news_article(flagged_doc, outlet_entity)
        clean_doc, _run2 = self._flagged_doc(
            "outlet-clean", source_type="news", density=0.1, techniques_validated=0,
        )
        self._news_article(clean_doc, outlet_entity)

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        outlets = {item["key"]: item for item in overview["by_news_outlet"]}
        self.assertIn("outlet-y", outlets)
        item = outlets["outlet-y"]
        self.assertEqual(item["entity_profile"].blurb, "A test news outlet.")
        self.assertEqual(item["kind"], "outlet")
        self.assertEqual(item["total_docs"], 2)
        self.assertEqual(item["flagged_docs"], 1)
        self.assertEqual(item["flagged_rate_pct"], 50.0)

    def test_editorial_official_appears_in_by_official_not_general_public(self):
        official_entity = self._entity(
            "sen-z", kind="official", lean="republican", editorial=True,
        )
        official_author = self._author("sen-z-handle")
        self._author_profile(official_author, official_entity)
        self._flagged_doc(
            "sen-z-doc", author_id=official_author, source_type="x_post",
            density=0.6, techniques_validated=1,
        )

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        official_keys = {item["key"] for item in overview["by_official"]}
        public_keys = {item["key"] for item in overview["by_general_public"]}
        self.assertIn("sen-z", official_keys)
        self.assertNotIn("sen-z", public_keys)

    def test_examples_by_entity_carries_technique_evidence_spans(self):
        outlet_entity = self._entity("outlet-z", kind="outlet")
        doc_id, run_id = self._flagged_doc(
            "outlet-example-doc", source_type="news", density=0.9, techniques_validated=1,
        )
        self._news_article(doc_id, outlet_entity)
        self._propaganda_technique(run_id, "loaded_language", "vile scheme")

        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        examples = overview["examples_by_entity"]["outlet-z"]
        self.assertEqual(len(examples), 1)
        example = examples[0]
        self.assertEqual(example["doc_id"], doc_id)
        self.assertEqual(example["techniques"][0]["technique"], "loaded_language")
        self.assertEqual(example["techniques"][0]["evidence_span"], "vile scheme")


if __name__ == "__main__":
    unittest.main()
