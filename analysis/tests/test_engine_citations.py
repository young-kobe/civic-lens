"""
Tests for analysis/src/engine/citations.py -- Postgres redesign Phase 6.

Two tiers, matching the repo's established convention for Postgres-redesign
test modules (analysis/tests/test_result_store.py, test_etl_documents.py):

  1. Pure-core tests (no DB) -- extract() edge shapes and dedup,
     resolve_candidates() including self-citation exclusion. Always run.
     (canonicalize_url itself is tested in test_canonicalize.py, its home
     module.)
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001_north_star.sql applied. Skipped
     (never failed) when the env var is absent.
"""

from __future__ import annotations

import os
import sys
import unittest
from pathlib import Path

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine import citations
from analysis.src.results import store

REPO_ROOT = Path(project_root)
MIGRATION_SQL = REPO_ROOT / "data" / "pg-migrations" / "0001_north_star.sql"


# =============================================================================
# Tier 1 -- pure core, no DB.
# =============================================================================

class ExtractUrlCitationTests(unittest.TestCase):
    def test_no_text_yields_no_candidates(self):
        doc = citations.CitationDocInput(doc_id=1, source_type="news", text="")
        self.assertEqual(citations.extract(doc), [])

    def test_single_url_extracted(self):
        doc = citations.CitationDocInput(
            doc_id=1, source_type="news", text="See https://example.com/story for details."
        )
        result = citations.extract(doc)
        self.assertEqual(len(result), 1)
        self.assertEqual(result[0].link_type, "url_citation")
        self.assertEqual(result[0].target_url, "https://example.com/story")

    def test_duplicate_urls_deduped_by_canonical_form(self):
        text = (
            "First mention https://www.example.com/story/ and again "
            "https://example.com/story."
        )
        doc = citations.CitationDocInput(doc_id=1, source_type="news", text=text)
        result = citations.extract(doc)
        self.assertEqual(len(result), 1)
        self.assertEqual(result[0].target_url, "https://example.com/story")

    def test_distinct_urls_both_extracted(self):
        text = "See https://a.example.com/one and https://b.example.com/two"
        doc = citations.CitationDocInput(doc_id=1, source_type="news", text=text)
        result = citations.extract(doc)
        self.assertEqual({c.target_url for c in result}, {
            "https://a.example.com/one", "https://b.example.com/two",
        })


class ExtractXReferenceTests(unittest.TestCase):
    def test_reply_reference_extracted_for_x_post(self):
        doc = citations.CitationDocInput(
            doc_id=2, source_type="x_post", text="",
            referenced_tweet_id="tweet_100", referenced_tweet_type="replied_to",
        )
        result = citations.extract(doc)
        self.assertEqual(len(result), 1)
        self.assertEqual(result[0].link_type, "reply")
        self.assertEqual(result[0].target_tweet_id, "tweet_100")

    def test_quote_and_retweet_types_map_correctly(self):
        quote_doc = citations.CitationDocInput(
            doc_id=2, source_type="x_post", text="",
            referenced_tweet_id="t1", referenced_tweet_type="quoted",
        )
        retweet_doc = citations.CitationDocInput(
            doc_id=3, source_type="x_post", text="",
            referenced_tweet_id="t2", referenced_tweet_type="retweeted",
        )
        self.assertEqual(citations.extract(quote_doc)[0].link_type, "quote")
        self.assertEqual(citations.extract(retweet_doc)[0].link_type, "retweet")

    def test_unmapped_reference_type_dropped(self):
        doc = citations.CitationDocInput(
            doc_id=2, source_type="x_post", text="",
            referenced_tweet_id="tweet_100", referenced_tweet_type="liked",
        )
        self.assertEqual(citations.extract(doc), [])

    def test_reference_id_without_type_dropped(self):
        doc = citations.CitationDocInput(
            doc_id=2, source_type="x_post", text="",
            referenced_tweet_id="tweet_100", referenced_tweet_type=None,
        )
        self.assertEqual(citations.extract(doc), [])

    def test_non_x_post_ignores_reference_fields(self):
        """Reference fields only ever get populated for x_post docs by the
        caller, but extract() must not act on them for other source types
        even if they were somehow set."""
        doc = citations.CitationDocInput(
            doc_id=1, source_type="news", text="",
            referenced_tweet_id="tweet_100", referenced_tweet_type="replied_to",
        )
        self.assertEqual(citations.extract(doc), [])

    def test_x_post_yields_both_reference_and_url_candidates(self):
        doc = citations.CitationDocInput(
            doc_id=2, source_type="x_post",
            text="Discussing the story. https://news.example.com/story",
            referenced_tweet_id="tweet_100", referenced_tweet_type="replied_to",
        )
        result = citations.extract(doc)
        link_types = {c.link_type for c in result}
        self.assertEqual(link_types, {"reply", "url_citation"})


# =============================================================================
# Tier 1 -- resolution (pure, given a fake resolved dict; no DB).
# =============================================================================

class ResolveCandidatesTests(unittest.TestCase):
    def test_resolved_url_becomes_target_doc_id(self):
        candidates = [citations.CitationCandidate(link_type="url_citation", target_url="https://x.com/a")]
        rows = citations.resolve_candidates(1, candidates, {"https://x.com/a": 42})
        self.assertEqual(rows, [store.CitationRow(link_type="url_citation", target_doc_id=42)])

    def test_unresolved_url_falls_back_to_target_url(self):
        candidates = [citations.CitationCandidate(link_type="url_citation", target_url="https://x.com/a")]
        rows = citations.resolve_candidates(1, candidates, {})
        self.assertEqual(rows, [store.CitationRow(link_type="url_citation", target_url="https://x.com/a")])

    def test_resolved_x_reference_becomes_target_doc_id(self):
        candidates = [citations.CitationCandidate(link_type="reply", target_tweet_id="tweet_100")]
        rows = citations.resolve_candidates(2, candidates, {"tweet_100": 7})
        self.assertEqual(rows, [store.CitationRow(link_type="reply", target_doc_id=7)])

    def test_unresolved_x_reference_is_dropped(self):
        """We don't know an un-ingested tweet's URL without a re-fetch --
        ported unchanged from the old extractor: skip rather than write a
        half-useful edge."""
        candidates = [citations.CitationCandidate(link_type="reply", target_tweet_id="tweet_999")]
        rows = citations.resolve_candidates(2, candidates, {})
        self.assertEqual(rows, [])

    def test_self_citation_dropped(self):
        candidates = [citations.CitationCandidate(link_type="url_citation", target_url="https://x.com/a")]
        rows = citations.resolve_candidates(42, candidates, {"https://x.com/a": 42})
        self.assertEqual(rows, [])

    def test_mixed_candidates_resolved_independently(self):
        candidates = [
            citations.CitationCandidate(link_type="url_citation", target_url="https://x.com/known"),
            citations.CitationCandidate(link_type="url_citation", target_url="https://x.com/unknown"),
            citations.CitationCandidate(link_type="reply", target_tweet_id="tweet_1"),
        ]
        rows = citations.resolve_candidates(
            1, candidates, {"https://x.com/known": 5, "tweet_1": 6}
        )
        self.assertEqual(len(rows), 3)
        self.assertIn(store.CitationRow(link_type="url_citation", target_doc_id=5), rows)
        self.assertIn(store.CitationRow(link_type="url_citation", target_url="https://x.com/unknown"), rows)
        self.assertIn(store.CitationRow(link_type="reply", target_doc_id=6), rows)


# =============================================================================
# Tier 2 -- integration, gated on CIVIC_TEST_DATABASE_URL
# =============================================================================

@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class ProcessIntegrationTests(unittest.TestCase):
    """Live-run against a real Postgres with 0001 applied. Fixture mirrors
    the old test_narrative_pipeline.py::TestCitationExtractor scenario (a
    news doc, an x post citing its URL, an x post replying to another x
    post) plus a doc citing an un-ingested external URL, ported to the new
    corpus.documents/corpus.x_posts shape."""

    @classmethod
    def setUpClass(cls):
        import psycopg
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        with psycopg.connect(cls._dsn, autocommit=True) as conn:
            conn.execute("DROP SCHEMA IF EXISTS raw, corpus, analysis, serving, ops, archive CASCADE")
            conn.execute(MIGRATION_SQL.read_text())

    def setUp(self):
        from analysis.src.common import db as dbmod
        dbmod.close_pool()
        self._prev_url = os.environ.get("CIVIC_DATABASE_URL")
        os.environ["CIVIC_DATABASE_URL"] = self._dsn
        self._truncate_all()
        self._seed_fixture()

    def tearDown(self):
        from analysis.src.common import db as dbmod
        dbmod.close_pool()
        if self._prev_url is None:
            os.environ.pop("CIVIC_DATABASE_URL", None)
        else:
            os.environ["CIVIC_DATABASE_URL"] = self._prev_url

    def _truncate_all(self):
        import psycopg
        with psycopg.connect(self._dsn, autocommit=True) as conn:
            conn.execute(
                "TRUNCATE analysis.citations, analysis.runs, "
                "corpus.x_posts, corpus.news_articles, corpus.documents, "
                "raw.x_posts, raw.articles, raw.pages CASCADE"
            )

    def _seed_fixture(self):
        """news doc (doc_a) <- url-cited by tweet_100 (doc_b) and tweet_300
        (doc_d, www + trailing-slash variant) <- tweet_200 (doc_c) replies to
        tweet_100. tweet_400 (doc_e) cites an external URL never ingested."""
        with store.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.pages (url_canon, url_raw, domain, state) "
                "VALUES ('https://news.example.com/story', 'https://news.example.com/story', "
                "'news.example.com', 'done')"
            )
            conn.execute(
                "INSERT INTO raw.articles (url_canon, domain, fetched_at, raw_hash, extraction_version) "
                "VALUES ('https://news.example.com/story', 'news.example.com', now(), 'h1', 'v1')"
            )
            self.doc_a = conn.execute(
                """
                INSERT INTO corpus.documents
                    (source_type, natural_key, published_at, body, source_url, raw_hash, etl_version)
                VALUES ('news', 'https://news.example.com/story', now(),
                        'Article body discussing the election.',
                        'https://news.example.com/story', 'h1', 'test')
                RETURNING doc_id
                """
            ).fetchone()["doc_id"]
            conn.execute(
                "INSERT INTO corpus.news_articles (doc_id, url_canon) VALUES (%s, "
                "'https://news.example.com/story')",
                (self.doc_a,),
            )

            for tweet_id, ref_id, ref_type in [
                ("tweet_100", None, None),
                ("tweet_200", "tweet_100", "replied_to"),
                ("tweet_300", None, None),
                ("tweet_400", None, None),
            ]:
                conn.execute(
                    "INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, "
                    "referenced_tweet_id, referenced_tweet_type, raw_hash, extraction_version) "
                    "VALUES (%s, 'u1', now(), now(), 't', %s, %s, 'h', 'v1')",
                    (tweet_id, ref_id, ref_type),
                )

            self.doc_b = self._seed_x_doc(conn, "tweet_100",
                "Discussing the story. https://news.example.com/story")
            self.doc_c = self._seed_x_doc(conn, "tweet_200", "Reply thread",
                ref_id="tweet_100", ref_type="replied_to")
            self.doc_d = self._seed_x_doc(conn, "tweet_300",
                "Another tweet citing https://www.news.example.com/story/")
            self.doc_e = self._seed_x_doc(conn, "tweet_400",
                "Check this out https://external.example/never-ingested")

    def _seed_x_doc(self, conn, tweet_id, text, ref_id=None, ref_type=None):
        """Seeds corpus.x_posts with the reference columns directly -- these
        are ETL-load-time snapshots from raw.x_posts (see documents.py), not
        something the citations engine itself reads from raw.*."""
        doc_id = conn.execute(
            """
            INSERT INTO corpus.documents
                (source_type, natural_key, published_at, body, source_url, raw_hash, etl_version)
            VALUES ('x_post', %s, now(), %s, %s, 'h', 'test')
            RETURNING doc_id
            """,
            (tweet_id, text, f"https://x.com/i/web/status/{tweet_id}"),
        ).fetchone()["doc_id"]
        conn.execute(
            "INSERT INTO corpus.x_posts (doc_id, tweet_id, referenced_tweet_id, referenced_tweet_type) "
            "VALUES (%s, %s, %s, %s)",
            (doc_id, tweet_id, ref_id, ref_type),
        )
        return doc_id

    def _doc_input(self, doc_id, source_type, text):
        """Assembles CitationDocInput the way a real caller would: reference
        fields read off corpus.x_posts (never raw.x_posts) -- proves the
        module docstring's caller contract, not just a hardcoded pass-through."""
        ref_id, ref_type = None, None
        if source_type == "x_post":
            with store.db.connection() as conn:
                row = conn.execute(
                    "SELECT referenced_tweet_id, referenced_tweet_type "
                    "FROM corpus.x_posts WHERE doc_id = %s", (doc_id,),
                ).fetchone()
            ref_id, ref_type = row["referenced_tweet_id"], row["referenced_tweet_type"]
        return citations.CitationDocInput(
            doc_id=doc_id, source_type=source_type, text=text,
            referenced_tweet_id=ref_id, referenced_tweet_type=ref_type,
        )

    def _citation_rows(self, run_id):
        with store.db.connection() as conn:
            return conn.execute(
                "SELECT source_doc_id, target_doc_id, target_url, link_type "
                "FROM analysis.citations WHERE run_id = %s", (run_id,)
            ).fetchall()

    def _run_row(self, run_id):
        with store.db.connection() as conn:
            return conn.execute(
                "SELECT * FROM analysis.runs WHERE run_id = %s", (run_id,)
            ).fetchone()

    def test_url_citation_resolves_to_ingested_doc(self):
        run_id = citations.process(self._doc_input(
            self.doc_b, "x_post", "Discussing the story. https://news.example.com/story",
        ))
        rows = self._citation_rows(run_id)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["target_doc_id"], self.doc_a)
        self.assertIsNone(rows[0]["target_url"])
        self.assertEqual(rows[0]["link_type"], "url_citation")

    def test_url_citation_www_variant_resolves_to_same_doc(self):
        run_id = citations.process(self._doc_input(
            self.doc_d, "x_post", "Another tweet citing https://www.news.example.com/story/",
        ))
        rows = self._citation_rows(run_id)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["target_doc_id"], self.doc_a)

    def test_reply_edge_resolves_to_ingested_tweet(self):
        run_id = citations.process(self._doc_input(self.doc_c, "x_post", "Reply thread"))
        rows = self._citation_rows(run_id)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]["target_doc_id"], self.doc_b)
        self.assertIsNone(rows[0]["target_url"])
        self.assertEqual(rows[0]["link_type"], "reply")

    def test_url_citation_to_un_ingested_url_keeps_target_url(self):
        run_id = citations.process(self._doc_input(
            self.doc_e, "x_post", "Check this out https://external.example/never-ingested",
        ))
        rows = self._citation_rows(run_id)
        self.assertEqual(len(rows), 1)
        self.assertIsNone(rows[0]["target_doc_id"])
        self.assertEqual(rows[0]["target_url"], "https://external.example/never-ingested")

    def test_doc_with_no_citations_yields_clean_empty_done_run(self):
        """The empty-finish behavior finding: zero candidates must still
        produce a 'done', is_current run row with zero analysis.citations
        rows -- not an error, not a skipped run."""
        run_id = citations.process(self._doc_input(
            self.doc_a, "news", "Article body discussing the election.",
        ))
        run = self._run_row(run_id)
        self.assertEqual(run["status"], "done")
        self.assertTrue(run["is_current"])
        self.assertEqual(run["model_id"], citations.MODEL_ID)
        self.assertEqual(run["inference_method"], "deterministic")
        self.assertIsNone(run["prompt_version_id"])
        self.assertEqual(self._citation_rows(run_id), [])

    def test_reprocess_supersedes_prior_run(self):
        first_run = citations.process(self._doc_input(
            self.doc_b, "x_post", "Discussing the story. https://news.example.com/story",
        ))
        second_run = citations.process(self._doc_input(
            self.doc_b, "x_post", "Discussing the story. https://news.example.com/story",
        ))
        self.assertNotEqual(first_run, second_run)
        with store.db.connection() as conn:
            rows = {
                r["run_id"]: r["is_current"]
                for r in conn.execute(
                    "SELECT run_id, is_current FROM analysis.runs "
                    "WHERE task = 'citations'::analysis.task AND doc_id = %s", (self.doc_b,)
                ).fetchall()
            }
        self.assertFalse(rows[first_run])
        self.assertTrue(rows[second_run])
        # The prior run's citations rows are not deleted (append-only per-run
        # history); only is_current on analysis.runs flips.
        self.assertEqual(len(self._citation_rows(first_run)), 1)
        self.assertEqual(len(self._citation_rows(second_run)), 1)


if __name__ == "__main__":
    unittest.main()
