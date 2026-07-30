"""
Tests for analysis/src/api/queries/public_posts.py -- Phase 9 GET
/api/v1/public-posts, the sentiment page's public-column feed.

Integration tests gated on CIVIC_TEST_DATABASE_URL, per the established
convention: the canonical officials exclusion (kind='official', never
editorial -- the feed is the public column, so a doc the ETL would stamp
official_record must never appear in it), exact server-side topic
attribution, deterministic engagement ordering, and SQL pagination.
"""

from __future__ import annotations

import os
import unittest
from datetime import datetime, timedelta, timezone

from analysis.src.api.queries import public_posts
from analysis.tests import pg_fixture


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class PublicPostsIntegrationTests(unittest.TestCase):
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
                "raw.x_posts, raw.reddit_posts RESTART IDENTITY CASCADE"
            )

    # --- seeding helpers ---------------------------------------------------

    def _conn(self):
        from analysis.src.common import db
        return db.connection()

    def _next_key(self, prefix: str) -> str:
        self._n += 1
        return f"{prefix}-{self._n}"

    def _seed_entity(self, kind: str, display_name: str, editorial: bool = False) -> int:
        with self._conn() as conn:
            row = conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean, editorial) "
                "VALUES (%s, %s::corpus.entity_kind, %s, 'unknown'::corpus.political_lean, %s) "
                "RETURNING entity_id",
                (self._next_key("entity"), kind, display_name, editorial),
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
        admission_class: str = "sampled",
    ) -> int:
        with self._conn() as conn:
            key = self._next_key("doc")
            row = conn.execute(
                "INSERT INTO corpus.documents "
                "(source_type, natural_key, published_at, author_id, title, body, "
                " source_url, raw_hash, etl_version, admission_class) "
                "VALUES (%s::corpus.source_type, %s, %s, %s, 't', 'body', "
                "'https://example.com/' || %s, 'h' || repeat('0', 63), 'test', "
                "%s::corpus.admission_class) RETURNING doc_id",
                (source_type, key, published_at, author_id, key, admission_class),
            ).fetchone()
            return row["doc_id"]

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

    def _seed_reddit_post(self, doc_id: int, *, score: int = 0, num_comments: int = 0) -> None:
        fullname = self._next_key("t3_post")
        with self._conn() as conn:
            conn.execute(
                "INSERT INTO raw.reddit_posts (fullname, raw_hash, extraction_version) "
                "VALUES (%s, 'h' || repeat('0', 63), 'test')",
                (fullname,),
            )
            conn.execute(
                "INSERT INTO corpus.reddit_posts (doc_id, fullname, score, num_comments) "
                "VALUES (%s, %s, %s, %s)",
                (doc_id, fullname, score, num_comments),
            )

    def _seed_scored_text_run(self, doc_id: int, label: str = "neutral", confidence: float = 0.9) -> None:
        with self._conn() as conn:
            row = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('text'::analysis.task, %s, 'done'::analysis.run_status, 'test-model', "
                "'llm'::analysis.inference_method, %s, true) RETURNING run_id",
                (doc_id, confidence),
            ).fetchone()
            conn.execute(
                "INSERT INTO analysis.sentiment_results (run_id, label) VALUES (%s, %s::analysis.sentiment_label)",
                (row["run_id"], label),
            )

    def _seed_topic_mention(self, doc_id: int, topic, confidence: float = 0.9) -> None:
        with self._conn() as conn:
            run = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, is_current) "
                "VALUES ('targets'::analysis.task, %s, 'done'::analysis.run_status, 'test-model', "
                "'llm'::analysis.inference_method, true) RETURNING run_id",
                (doc_id,),
            ).fetchone()
            conn.execute(
                "INSERT INTO analysis.target_mentions (run_id, doc_id, raw_target, stance, topic, confidence) "
                "VALUES (%s, %s, 'someone', 'neutral'::analysis.sentiment_label, %s, %s)",
                (run["run_id"], doc_id, topic, confidence),
            )

    def _seed_propaganda_run(self, doc_id: int, density: float, techniques=()) -> None:
        with self._conn() as conn:
            run = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current) "
                "VALUES ('propaganda'::analysis.task, %s, 'done'::analysis.run_status, 'test-model', "
                "'llm'::analysis.inference_method, 0.9, true) RETURNING run_id",
                (doc_id,),
            ).fetchone()
            conn.execute(
                "INSERT INTO analysis.propaganda_results (run_id, density, techniques_validated) "
                "VALUES (%s, %s, %s)",
                (run["run_id"], density, len(techniques)),
            )
            for technique, span in techniques:
                conn.execute(
                    "INSERT INTO analysis.propaganda_techniques (run_id, technique, evidence_span, confidence) "
                    "VALUES (%s, %s::analysis.propaganda_technique, %s, 0.8)",
                    (run["run_id"], technique, span),
                )

    def _seed_bot_run(self, doc_id: int, label: str, confidence: float = 0.9) -> None:
        with self._conn() as conn:
            run = conn.execute(
                "INSERT INTO analysis.runs (task, doc_id, status, model_id, inference_method, confidence, is_current, raw_response) "
                "VALUES ('bot'::analysis.task, %s, 'done'::analysis.run_status, 'test-model', "
                "'llm'::analysis.inference_method, %s, true, "
                "'{\"llm\": {\"indicators\": [\"posting cadence\"], \"reasoning\": \"test reasoning\"}}'::jsonb) "
                "RETURNING run_id",
                (doc_id, confidence),
            ).fetchone()
            conn.execute(
                "INSERT INTO analysis.bot_signals (run_id, doc_id, label) "
                "VALUES (%s, %s, %s::analysis.bot_label)",
                (run["run_id"], doc_id, label),
            )

    def _seed_public_x_doc(self, published_at=None, **engagement) -> int:
        author = self._seed_author()
        doc = self._seed_document(
            "x_post", published_at or datetime.now(timezone.utc) - timedelta(days=1),
            author_id=author,
        )
        self._seed_x_post(doc, **engagement)
        self._seed_scored_text_run(doc)
        return doc

    # --- tests -------------------------------------------------------------

    def test_official_authors_excluded_regardless_of_editorial(self):
        # The feed IS the public column. The exclusion must be the canonical
        # kind='official' predicate -- the same definition the ETL uses to
        # stamp official_record -- so a promoted (editorial=false) official's
        # post can never render under "The Public" (the original bleed bug).
        official = self._seed_entity("official", "Promoted Official", editorial=False)
        official_author = self._seed_author()
        self._seed_author_profile(official_author, "elected_official", official)
        official_doc = self._seed_document(
            "x_post", datetime.now(timezone.utc) - timedelta(days=1),
            author_id=official_author, admission_class="official_record",
        )
        self._seed_x_post(official_doc)
        self._seed_scored_text_run(official_doc)

        public_author = self._seed_author()
        self._seed_author_profile(public_author, "general_public")
        public_doc = self._seed_document(
            "x_post", datetime.now(timezone.utc) - timedelta(days=1), author_id=public_author,
        )
        self._seed_x_post(public_doc)
        self._seed_scored_text_run(public_doc)

        unresolved_doc = self._seed_public_x_doc()

        reddit_doc = self._seed_document(
            "reddit_post", datetime.now(timezone.utc) - timedelta(days=1),
        )
        self._seed_reddit_post(reddit_doc, score=3)
        self._seed_scored_text_run(reddit_doc)

        resp = public_posts.get_public_posts(window="30d")
        got = {item.doc_id for item in resp.items}
        self.assertEqual(got, {public_doc, unresolved_doc, reddit_doc})
        self.assertEqual(resp.total, 3)

    def test_items_carry_label_and_confidence(self):
        # Labeling discipline: every card in the feed shows its
        # classification and confidence -- the sample contract, not an
        # optional nicety.
        self._seed_public_x_doc()
        resp = public_posts.get_public_posts(window="30d")
        self.assertEqual(len(resp.items), 1)
        self.assertEqual(resp.items[0].label, "neutral")
        self.assertEqual(resp.items[0].confidence, 0.9)

    def test_topic_filter_is_exact_server_side_attribution(self):
        # The topic tab must never keyword-guess: a doc matches a topic tab
        # only via its dominant resolved target_mentions topic, and
        # 'General' means "no resolved topic", not "everything else".
        economy_doc = self._seed_public_x_doc()
        self._seed_topic_mention(economy_doc, "Economy")
        general_doc = self._seed_public_x_doc()

        economy = public_posts.get_public_posts(window="30d", topic="Economy")
        self.assertEqual([i.doc_id for i in economy.items], [economy_doc])
        self.assertEqual(economy.items[0].topic, "Economy")

        general = public_posts.get_public_posts(window="30d", topic="General")
        self.assertEqual([i.doc_id for i in general.items], [general_doc])
        self.assertEqual(general.items[0].topic, "General")

        both = public_posts.get_public_posts(window="30d")
        self.assertEqual(both.total, 2)

    def test_engagement_ordering_with_deterministic_tiebreaks(self):
        # "Most relevant" is engagement-led and fully deterministic
        # (engagement desc, then published_at desc, then doc_id desc) so
        # the same corpus always renders the same feed -- reproducibility,
        # not an LLM ranking.
        base = datetime.now(timezone.utc) - timedelta(days=2)
        low = self._seed_public_x_doc(published_at=base, like_count=1)
        high = self._seed_public_x_doc(published_at=base, like_count=50)
        tie_older = self._seed_public_x_doc(published_at=base - timedelta(hours=1), like_count=5)
        tie_newer = self._seed_public_x_doc(published_at=base, like_count=5)

        resp = public_posts.get_public_posts(window="30d")
        self.assertEqual(
            [i.doc_id for i in resp.items],
            [high, tie_newer, tie_older, low],
        )

    def test_pagination_slices_in_sql_with_stable_total(self):
        # Corpus-wide feed pages in SQL (LIMIT/OFFSET): each page is a
        # disjoint slice and `total` stays the full predicate count on
        # every page, so the UI's "N of total" is honest.
        for i in range(public_posts.PUBLIC_POSTS_PAGE_SIZE + 5):
            self._seed_public_x_doc(like_count=i)

        page1 = public_posts.get_public_posts(window="30d", page=1)
        page2 = public_posts.get_public_posts(window="30d", page=2)
        self.assertEqual(len(page1.items), public_posts.PUBLIC_POSTS_PAGE_SIZE)
        self.assertEqual(len(page2.items), 5)
        self.assertEqual(page1.total, public_posts.PUBLIC_POSTS_PAGE_SIZE + 5)
        self.assertEqual(page2.total, page1.total)
        self.assertFalse(
            {i.doc_id for i in page1.items} & {i.doc_id for i in page2.items}
        )


    def test_propaganda_feed_is_lensed_and_excludes_officials(self):
        # The propaganda page's public column shows what the PAGE measures:
        # scored posts with their true technique flags/density (clean posts
        # included -- the baseline is part of the story), and officials
        # excluded by the same canonical kind predicate as the tone feed.
        flagged = self._seed_public_x_doc(like_count=50)
        self._seed_propaganda_run(flagged, 0.6, techniques=[("loaded_language", "verbatim span")])
        # The flagged post's target edge (same target_mentions source as
        # tone cards) must ride along -- who the propaganda is aimed at.
        self._seed_topic_mention(flagged, "Economy")
        clean = self._seed_public_x_doc(like_count=10)
        self._seed_propaganda_run(clean, 0.0)

        official = self._seed_entity("official", "Promoted Official", editorial=False)
        official_author = self._seed_author()
        self._seed_author_profile(official_author, "elected_official", official)
        official_doc = self._seed_document(
            "x_post", datetime.now(timezone.utc) - timedelta(days=1), author_id=official_author,
        )
        self._seed_x_post(official_doc)
        self._seed_propaganda_run(official_doc, 0.9, techniques=[("name_calling", "span")])

        resp = public_posts.get_propaganda_public_posts(window="30d")
        self.assertEqual([i.doc_id for i in resp.items], [flagged, clean])
        self.assertEqual(resp.total, 2)
        self.assertEqual(resp.items[0].overall_score, 0.6)
        self.assertEqual(resp.items[0].techniques[0].technique, "loaded_language")
        self.assertEqual(resp.items[0].targets[0].label, "someone")
        self.assertEqual(resp.items[0].targets[0].stance, "neutral")
        self.assertEqual(resp.items[1].techniques, [])
        self.assertIsNone(resp.items[1].targets)

    def test_bot_feed_carries_every_verdict_with_its_label(self):
        # The bot page's public column must show the verdict per card (the
        # feed mixes bot/suspicious/human -- an unlabeled card would imply
        # everything shown is flagged) and must NOT bot-exclude authors:
        # this page measures automation, excluding it would delete the
        # subject. Officials still route to their own column.
        bot_doc = self._seed_public_x_doc(like_count=50)
        self._seed_bot_run(bot_doc, "bot")
        human_doc = self._seed_public_x_doc(like_count=10)
        self._seed_bot_run(human_doc, "human")

        official = self._seed_entity("official", "Promoted Official", editorial=False)
        official_author = self._seed_author()
        self._seed_author_profile(official_author, "elected_official", official)
        official_doc = self._seed_document(
            "x_post", datetime.now(timezone.utc) - timedelta(days=1), author_id=official_author,
        )
        self._seed_x_post(official_doc)
        self._seed_bot_run(official_doc, "bot")

        resp = public_posts.get_bot_public_posts(window="30d")
        self.assertEqual([i.doc_id for i in resp.items], [bot_doc, human_doc])
        self.assertEqual(resp.total, 2)
        self.assertEqual([i.label for i in resp.items], ["bot", "human"])
        self.assertEqual(resp.items[0].indicators, ["posting cadence"])


if __name__ == "__main__":
    unittest.main()
