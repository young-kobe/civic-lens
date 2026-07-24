"""
Tests for analysis/src/api/queries/propaganda.py -- Phase 9 strictly-live
propaganda panel.

Two tiers, matching the repo's established convention
(test_engine_lean_derivation.py, test_api_queries_base.py):

  1. Pure-core tests (no DB) -- _summarize_docs, _bucket_by_source,
     _bucket_by_party, _summarize_techniques against synthetic row dicts.
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

from analysis.src.api.queries import propaganda
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
                "analysis.author_bot_scores, analysis.runs, corpus.author_profiles, "
                "corpus.documents, corpus.authors, corpus.entities CASCADE"
            )

    # -- seeding helpers --------------------------------------------------

    def _entity(self, key, lean, kind="official"):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean) "
                "VALUES (%s, %s::corpus.entity_kind, %s, %s::corpus.political_lean) "
                "RETURNING entity_id",
                (key, kind, key, lean),
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

    def _bot_score(self, author_id, score):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.author_bot_scores "
                "(author_id, score, sample_count, updated_at) VALUES (%s, %s, 1, now())",
                (author_id, score),
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

    def test_no_eligible_docs_yields_zeroed_overview_not_error(self):
        overview = propaganda.get_propaganda_overview(start=None, end=None, window_label="all")
        self.assertEqual(overview["total_eligible_docs"], 0)
        self.assertEqual(overview["by_technique"][0]["count"], 0)
        self.assertEqual(overview["examples"], [])


if __name__ == "__main__":
    unittest.main()
