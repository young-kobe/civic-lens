"""
Tests for analysis/src/engine/lean_derivation.py -- Postgres redesign
Phase 7B.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (test_engine_account_tier.py, test_engine_targets.py):

  1. Pure-core tests (no DB) -- the signal-direction mapping (_signal_for),
     the three-way labeling gate (_derive_lean), and the evidence-tallying
     helpers, all against synthetic row dicts. Always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001_north_star.sql applied -- this
     module's whole job is the SQL evidence query plus the full-rebuild
     write, both of which need a real database to exercise.
"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timezone

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine import lean_derivation
from analysis.src.engine.constants import (
    LEAN_CONFIDENCE_SATURATION_SAMPLES,
    LEAN_MIN_SAMPLE_COUNT,
    LEAN_SHARE_THRESHOLD,
)
from analysis.tests import pg_fixture


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class SignalForTests(unittest.TestCase):
    """The four directional signal combinations (target_mentions'
    sentiment_label is the only vocabulary since analysis.favorability_stances
    retired 2026-07-25), plus the non-directional and non-partisan
    exclusions."""

    def test_positive_toward_democrat_is_pro_democrat(self):
        self.assertEqual(lean_derivation._signal_for("democrat", "positive"), "democrat")

    def test_positive_toward_republican_is_pro_republican(self):
        self.assertEqual(lean_derivation._signal_for("republican", "positive"), "republican")

    def test_negative_toward_republican_is_pro_democrat(self):
        # The debunker-style mismatch this module must get right: criticizing
        # a Republican is pro-democrat evidence, not pro-republican -- the
        # entity's OWN lean is the anchor, stance direction flips it.
        self.assertEqual(lean_derivation._signal_for("republican", "negative"), "democrat")

    def test_negative_toward_democrat_is_pro_republican(self):
        self.assertEqual(lean_derivation._signal_for("democrat", "negative"), "republican")

    def test_neutral_stance_toward_democrat_is_excluded(self):
        self.assertIsNone(lean_derivation._signal_for("democrat", "neutral"))

    def test_mixed_stance_toward_republican_is_excluded(self):
        self.assertIsNone(lean_derivation._signal_for("republican", "mixed"))

    def test_independent_entity_lean_is_excluded(self):
        self.assertIsNone(lean_derivation._signal_for("independent", "positive"))

    def test_curated_mixed_entity_lean_is_excluded(self):
        self.assertIsNone(lean_derivation._signal_for("mixed", "positive"))

    def test_unknown_entity_lean_is_excluded(self):
        self.assertIsNone(lean_derivation._signal_for("unknown", "positive"))


class DeriveLeanGateTests(unittest.TestCase):
    """The three labeling outcomes -- 'unknown' below sample count, 'mixed'
    at/above it but under share threshold, a decided lean at/above both."""

    def test_below_min_sample_count_is_unknown_even_when_unanimous(self):
        pro_democrat = LEAN_MIN_SAMPLE_COUNT - 1
        lean, lean_share, _confidence = lean_derivation._derive_lean(pro_democrat, 0)
        self.assertEqual(lean, "unknown")
        self.assertEqual(lean_share, 1.0)  # real share recorded despite the gate

    def test_at_min_sample_count_below_share_threshold_is_mixed(self):
        # LEAN_MIN_SAMPLE_COUNT total samples, 60/40 split (< 0.7 threshold).
        pro_democrat = 3
        pro_republican = 2
        self.assertEqual(pro_democrat + pro_republican, LEAN_MIN_SAMPLE_COUNT)
        lean, lean_share, _confidence = lean_derivation._derive_lean(pro_democrat, pro_republican)
        self.assertEqual(lean, "mixed")
        self.assertAlmostEqual(lean_share, 0.6)

    def test_share_exactly_at_threshold_is_decided(self):
        # 10 samples, 7/3 split -> share == LEAN_SHARE_THRESHOLD exactly; the
        # gate uses >=, so this must be decided, not mixed.
        lean, lean_share, _confidence = lean_derivation._derive_lean(7, 3)
        self.assertEqual(lean, "democrat")
        self.assertAlmostEqual(lean_share, LEAN_SHARE_THRESHOLD)

    def test_share_just_below_threshold_is_mixed(self):
        lean, lean_share, _confidence = lean_derivation._derive_lean(6, 4)
        self.assertEqual(lean, "mixed")
        self.assertAlmostEqual(lean_share, 0.6)

    def test_republican_majority_is_labeled_republican(self):
        lean, _lean_share, _confidence = lean_derivation._derive_lean(1, 9)
        self.assertEqual(lean, "republican")

    def test_democrat_majority_is_labeled_democrat(self):
        lean, _lean_share, _confidence = lean_derivation._derive_lean(9, 1)
        self.assertEqual(lean, "democrat")

    def test_confidence_saturates_at_saturation_samples(self):
        total = LEAN_CONFIDENCE_SATURATION_SAMPLES
        pro_democrat = total  # unanimous, so lean_share == 1.0
        _lean, lean_share, confidence = lean_derivation._derive_lean(pro_democrat, 0)
        self.assertEqual(confidence, lean_share)  # min(1.0, total/saturation) == 1.0

    def test_confidence_scales_down_below_saturation(self):
        total = LEAN_CONFIDENCE_SATURATION_SAMPLES // 2
        pro_democrat = total
        _lean, lean_share, confidence = lean_derivation._derive_lean(pro_democrat, 0)
        expected = lean_share * (total / LEAN_CONFIDENCE_SATURATION_SAMPLES)
        self.assertAlmostEqual(confidence, expected)
        self.assertLess(confidence, lean_share)


class TallyEvidenceTests(unittest.TestCase):
    """Pure evidence-grouping helpers, exercised without a database."""

    def test_author_evidence_groups_by_author_id(self):
        rows = [
            {"doc_id": 1, "author_id": 10, "entity_lean": "democrat", "stance": "positive"},
            {"doc_id": 2, "author_id": 10, "entity_lean": "republican", "stance": "negative"},
            {"doc_id": 3, "author_id": 20, "entity_lean": "republican", "stance": "positive"},
        ]
        tallies = lean_derivation._tally_author_evidence(rows)
        self.assertEqual(tallies[10]["democrat"], 2)  # both rows favor democrat
        self.assertEqual(tallies[20]["republican"], 1)

    def test_author_evidence_skips_rows_with_no_author(self):
        rows = [{"doc_id": 1, "author_id": None, "entity_lean": "democrat", "stance": "positive"}]
        tallies = lean_derivation._tally_author_evidence(rows)
        self.assertEqual(tallies, {})

    def test_author_evidence_skips_non_directional_rows(self):
        rows = [{"doc_id": 1, "author_id": 10, "entity_lean": "democrat", "stance": "neutral"}]
        tallies = lean_derivation._tally_author_evidence(rows)
        self.assertEqual(tallies, {})

    def test_narrative_evidence_fans_a_doc_out_to_every_member_narrative(self):
        rows = [{"doc_id": 5, "author_id": 1, "entity_lean": "democrat", "stance": "positive"}]
        doc_to_narratives = {5: [100, 200]}
        tallies, contributing_docs = lean_derivation._tally_narrative_evidence(rows, doc_to_narratives)
        self.assertEqual(tallies[100]["democrat"], 1)
        self.assertEqual(tallies[200]["democrat"], 1)
        self.assertEqual(contributing_docs[100], {5})
        self.assertEqual(contributing_docs[200], {5})

    def test_narrative_doc_count_counts_distinct_docs_not_samples(self):
        # Two directional samples from the SAME doc must count as doc_count=1.
        rows = [
            {"doc_id": 5, "author_id": 1, "entity_lean": "democrat", "stance": "positive"},
            {"doc_id": 5, "author_id": 1, "entity_lean": "republican", "stance": "negative"},
        ]
        doc_to_narratives = {5: [100]}
        tallies, contributing_docs = lean_derivation._tally_narrative_evidence(rows, doc_to_narratives)
        self.assertEqual(tallies[100]["democrat"], 2)  # both rows favor democrat
        self.assertEqual(len(contributing_docs[100]), 1)

    def test_narrative_evidence_ignores_docs_with_no_narrative_membership(self):
        rows = [{"doc_id": 9, "author_id": 1, "entity_lean": "democrat", "stance": "positive"}]
        tallies, contributing_docs = lean_derivation._tally_narrative_evidence(rows, {})
        self.assertEqual(tallies, {})
        self.assertEqual(contributing_docs, {})

    def test_index_narrative_docs_groups_by_doc(self):
        rows = [{"narrative_id": 1, "doc_id": 7}, {"narrative_id": 2, "doc_id": 7}]
        index = lean_derivation._index_narrative_docs(rows)
        self.assertEqual(sorted(index[7]), [1, 2])


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL.
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class RunIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        pg_fixture.reset_schema(cls._dsn, seed=False)

    def setUp(self):
        self._prev_url = pg_fixture.begin_test(self._dsn)
        self._truncate_mutable_tables()

    def tearDown(self):
        pg_fixture.end_test(self._prev_url)

    def _truncate_mutable_tables(self):
        # run() rebuilds from ALL current evidence in the database, not a
        # per-test scope -- every test must start from an empty slate or
        # aggregate-count assertions (authors_written, doc_count, ...) would
        # pick up leftover rows from a previous test in this class.
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.author_leans, analysis.narrative_leans, "
                "analysis.narrative_docs, analysis.narratives, analysis.clustering_runs, "
                "analysis.target_mentions, analysis.runs, "
                "corpus.documents, corpus.authors, corpus.entities CASCADE"
            )

    # -- seeding helpers ------------------------------------------------

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

    def _doc(self, natural_key, author_id=None):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, author_id, published_at, body, source_url, "
                " raw_hash, etl_version) "
                "VALUES ('news'::corpus.source_type, %s, %s, now(), 'body', "
                "        'http://example.com/' || %s, 'deadbeef', 'test') "
                "RETURNING doc_id",
                (natural_key, author_id, natural_key),
            ).fetchone()
            return row["doc_id"]

    def _run(self, task, doc_id, status="done", is_current=True):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs "
                "(task, doc_id, status, model_id, inference_method, is_current) "
                "VALUES (%s::analysis.task, %s, %s::analysis.run_status, "
                "        'test-model', 'llm'::analysis.inference_method, %s) "
                "RETURNING run_id",
                (task, doc_id, status, is_current),
            ).fetchone()
            return row["run_id"]

    def _target_mention(self, run_id, doc_id, entity_id, stance):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.target_mentions "
                "(run_id, doc_id, raw_target, entity_id, stance, confidence) "
                "VALUES (%s, %s, 'raw target', %s, %s::analysis.sentiment_label, 0.8)",
                (run_id, doc_id, entity_id, stance),
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
                "INSERT INTO analysis.narratives (clustering_run_id, name) "
                "VALUES (%s, %s) RETURNING narrative_id",
                (clustering_run_id, name),
            ).fetchone()
            return row["narrative_id"]

    def _narrative_doc(self, narrative_id, doc_id):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            conn.execute(
                "INSERT INTO analysis.narrative_docs (narrative_id, doc_id) VALUES (%s, %s)",
                (narrative_id, doc_id),
            )

    def _author_lean_row(self, author_id):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.author_leans WHERE author_id = %s", (author_id,)
            ).fetchone()

    def _narrative_lean_row(self, narrative_id):
        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.narrative_leans WHERE narrative_id = %s", (narrative_id,)
            ).fetchone()

    def _seed_pro_democrat_author_samples(self, author_id, count):
        """count directional positive-toward-democrat target_mentions rows,
        each on its own doc, all attributed to author_id."""
        democrat_entity = self._entity(f"dem-{author_id}", "democrat")
        for i in range(count):
            doc_id = self._doc(f"doc-{author_id}-{i}", author_id=author_id)
            run_id = self._run("targets", doc_id)
            self._target_mention(run_id, doc_id, democrat_entity, "positive")

    # -- tests ------------------------------------------------------------

    def test_column_name_asymmetry_author_lean_confidence_vs_narrative_confidence(self):
        author_id = self._author("handle-a")
        self._seed_pro_democrat_author_samples(author_id, LEAN_MIN_SAMPLE_COUNT)

        result = lean_derivation.run()
        self.assertEqual(result.authors_written, 1)

        row = self._author_lean_row(author_id)
        self.assertIn("lean_confidence", row)
        self.assertNotIn("confidence", row)

        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        democrat_entity = self._entity("narr-dem", "democrat")
        for i in range(LEAN_MIN_SAMPLE_COUNT):
            doc_id = self._doc(f"narr-doc-{i}")
            self._narrative_doc(narrative_id, doc_id)
            run_id = self._run("targets", doc_id)
            self._target_mention(run_id, doc_id, democrat_entity, "positive")

        result = lean_derivation.run()
        self.assertEqual(result.narratives_written, 1)
        nrow = self._narrative_lean_row(narrative_id)
        self.assertIn("confidence", nrow)
        self.assertNotIn("lean_confidence", nrow)

    def test_negative_toward_republican_writes_democrat_lean(self):
        # End-to-end version of the debunker-mismatch pure test above.
        author_id = self._author("critic")
        republican_entity = self._entity("gop-figure", "republican")
        for i in range(LEAN_MIN_SAMPLE_COUNT):
            doc_id = self._doc(f"crit-doc-{i}", author_id=author_id)
            run_id = self._run("targets", doc_id)
            self._target_mention(run_id, doc_id, republican_entity, "negative")

        lean_derivation.run()

        row = self._author_lean_row(author_id)
        self.assertEqual(row["lean"], "democrat")
        self.assertEqual(row["stance_sample_count"], LEAN_MIN_SAMPLE_COUNT)

    def test_neutral_and_mixed_stances_excluded(self):
        author_id = self._author("neutral-author")
        democrat_entity = self._entity("dem-neutral", "democrat")
        for stance in ("neutral", "mixed"):
            doc_id = self._doc(f"neutral-doc-{stance}", author_id=author_id)
            run_id = self._run("targets", doc_id)
            self._target_mention(run_id, doc_id, democrat_entity, stance)

        result = lean_derivation.run()

        self.assertEqual(result.authors_written, 0)
        self.assertIsNone(self._author_lean_row(author_id))

    def test_unresolved_target_mention_excluded(self):
        author_id = self._author("unresolved-author")
        doc_id = self._doc("unresolved-doc", author_id=author_id)
        run_id = self._run("targets", doc_id)
        self._target_mention(run_id, doc_id, entity_id=None, stance="positive")

        result = lean_derivation.run()

        self.assertEqual(result.authors_written, 0)
        self.assertIsNone(self._author_lean_row(author_id))

    def test_independent_entity_lean_excluded(self):
        author_id = self._author("independent-author")
        independent_entity = self._entity("indy", "independent")
        doc_id = self._doc("indy-doc", author_id=author_id)
        run_id = self._run("targets", doc_id)
        self._target_mention(run_id, doc_id, independent_entity, "positive")

        result = lean_derivation.run()

        self.assertEqual(result.authors_written, 0)

    def test_stale_and_failed_runs_excluded(self):
        author_id = self._author("stale-author")
        democrat_entity = self._entity("dem-stale", "democrat")

        stale_doc = self._doc("stale-doc", author_id=author_id)
        stale_run = self._run("targets", stale_doc, status="done", is_current=False)
        self._target_mention(stale_run, stale_doc, democrat_entity, "positive")

        failed_doc = self._doc("failed-doc", author_id=author_id)
        failed_run = self._run("targets", failed_doc, status="failed", is_current=False)
        self._target_mention(failed_run, failed_doc, democrat_entity, "positive")

        result = lean_derivation.run()

        self.assertEqual(result.authors_written, 0)
        self.assertIsNone(self._author_lean_row(author_id))

    def test_three_gate_outcomes_end_to_end(self):
        # unknown: below LEAN_MIN_SAMPLE_COUNT, unanimous.
        unknown_author = self._author("unknown-author")
        self._seed_pro_democrat_author_samples(unknown_author, LEAN_MIN_SAMPLE_COUNT - 1)

        # mixed: at min sample count, share below threshold (3/5 = 0.6).
        mixed_author = self._author("mixed-author")
        dem_entity = self._entity("dem-mixed", "democrat")
        rep_entity = self._entity("rep-mixed", "republican")
        for i in range(3):
            doc_id = self._doc(f"mixed-dem-doc-{i}", author_id=mixed_author)
            run_id = self._run("targets", doc_id)
            self._target_mention(run_id, doc_id, dem_entity, "positive")
        for i in range(2):
            doc_id = self._doc(f"mixed-rep-doc-{i}", author_id=mixed_author)
            run_id = self._run("targets", doc_id)
            self._target_mention(run_id, doc_id, rep_entity, "positive")

        # decided: at min sample count, unanimous (share 1.0 >= threshold).
        decided_author = self._author("decided-author")
        self._seed_pro_democrat_author_samples(decided_author, LEAN_MIN_SAMPLE_COUNT)

        lean_derivation.run()

        unknown_row = self._author_lean_row(unknown_author)
        self.assertEqual(unknown_row["lean"], "unknown")
        self.assertEqual(unknown_row["stance_sample_count"], LEAN_MIN_SAMPLE_COUNT - 1)

        mixed_row = self._author_lean_row(mixed_author)
        self.assertEqual(mixed_row["lean"], "mixed")
        self.assertEqual(mixed_row["stance_sample_count"], 5)

        decided_row = self._author_lean_row(decided_author)
        self.assertEqual(decided_row["lean"], "democrat")

    def test_narrative_doc_count_is_distinct_docs_not_sample_count(self):
        clustering_run_id = self._clustering_run()
        narrative_id = self._narrative(clustering_run_id)
        dem_entity = self._entity("dem-doccount", "democrat")
        rep_entity = self._entity("rep-doccount", "republican")

        # One doc carrying TWO directional target_mentions rows (a target
        # task can extract multiple targets per doc) -- doc_count must still
        # be 1 for this doc, not 2.
        doc_id = self._doc("doccount-doc")
        self._narrative_doc(narrative_id, doc_id)
        targets_run = self._run("targets", doc_id)
        self._target_mention(targets_run, doc_id, dem_entity, "positive")
        self._target_mention(targets_run, doc_id, rep_entity, "negative")

        # Enough additional docs to clear LEAN_MIN_SAMPLE_COUNT.
        for i in range(LEAN_MIN_SAMPLE_COUNT - 2):
            extra_doc = self._doc(f"doccount-extra-{i}")
            self._narrative_doc(narrative_id, extra_doc)
            run_id = self._run("targets", extra_doc)
            self._target_mention(run_id, extra_doc, dem_entity, "positive")

        lean_derivation.run()

        row = self._narrative_lean_row(narrative_id)
        self.assertEqual(row["doc_count"], LEAN_MIN_SAMPLE_COUNT - 1)  # distinct docs, not samples
        self.assertEqual(row["lean"], "democrat")

    def test_full_rebuild_idempotency_replaces_not_duplicates(self):
        author_id = self._author("rerun-author")
        self._seed_pro_democrat_author_samples(author_id, LEAN_MIN_SAMPLE_COUNT)

        first = lean_derivation.run()
        self.assertEqual(first.authors_written, 1)

        from analysis.src.common import db as dbmod
        with dbmod.connection() as conn:
            count_after_first = conn.execute(
                "SELECT COUNT(*) AS n FROM analysis.author_leans"
            ).fetchone()["n"]

        second = lean_derivation.run()
        self.assertEqual(second.authors_written, 1)
        with dbmod.connection() as conn:
            count_after_second = conn.execute(
                "SELECT COUNT(*) AS n FROM analysis.author_leans"
            ).fetchone()["n"]
        self.assertEqual(count_after_first, count_after_second)

    def test_rerun_reflects_new_evidence_not_stale_row(self):
        author_id = self._author("shift-author")
        self._seed_pro_democrat_author_samples(author_id, LEAN_MIN_SAMPLE_COUNT)
        lean_derivation.run()
        self.assertEqual(self._author_lean_row(author_id)["lean"], "democrat")

        # Tip the balance heavily republican and rerun -- full rebuild means
        # the old democrat-leaning row must not survive.
        rep_entity = self._entity("rep-shift", "republican")
        for i in range(20):
            doc_id = self._doc(f"shift-doc-{i}", author_id=author_id)
            run_id = self._run("targets", doc_id)
            self._target_mention(run_id, doc_id, rep_entity, "positive")

        lean_derivation.run()
        self.assertEqual(self._author_lean_row(author_id)["lean"], "republican")

    def test_computed_at_uses_injected_now(self):
        author_id = self._author("timestamp-author")
        self._seed_pro_democrat_author_samples(author_id, LEAN_MIN_SAMPLE_COUNT)
        fixed_now = datetime(2026, 1, 1, tzinfo=timezone.utc)

        lean_derivation.run(now=fixed_now)

        row = self._author_lean_row(author_id)
        self.assertEqual(row["computed_at"], fixed_now)

    def test_zero_evidence_subjects_get_no_row(self):
        self._author("no-evidence-author")
        clustering_run_id = self._clustering_run()
        self._narrative(clustering_run_id)  # no narrative_docs, no evidence

        result = lean_derivation.run()

        self.assertEqual(result.authors_written, 0)
        self.assertEqual(result.narratives_written, 0)


if __name__ == "__main__":
    unittest.main()
