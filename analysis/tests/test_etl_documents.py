"""
Tests for analysis/src/etl/documents.py — Postgres redesign Phase 4.

Two tiers, matching the repo's established convention for Phase 1-3
Postgres-redesign tests (analysis/tests/test_pg_db.py,
tools/test_migrate_sqlite_to_pg.py):

  1. Pure decision-function unit tests (no DB) — filter/recency/cap/config
     logic, always run.
  2. Integration tests gated on CIVIC_TEST_DATABASE_URL, against a real
     Postgres with data/pg-migrations/0001_north_star.sql applied. Skipped
     (never failed) when the env var is absent.
"""

from __future__ import annotations

import datetime
import os
import sys
import tempfile
import unittest
from pathlib import Path

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.etl import documents as docs

UTC = datetime.timezone.utc


def _dt(year, month, day, hour=0):
    return datetime.datetime(year, month, day, hour, tzinfo=UTC)


class IsUsPoliticalContentTests(unittest.TestCase):
    """Word-boundary matching (the Phase 4 tightening) — see documents.py's
    module docstring for the false-positive classes this closes."""

    def test_genuine_political_bill_still_matches(self):
        self.assertTrue(docs.is_us_political_content(
            "The Senate passed a sweeping spending bill on Tuesday."))

    def test_billion_does_not_false_positive_on_bill(self):
        # comparecards.com-shaped content: dollar amounts, no real political
        # keyword anywhere.
        self.assertFalse(docs.is_us_political_content(
            "Americans now owe $1.3 billion in credit card debt, according "
            "to a new report on consumer billing habits."))

    def test_billing_does_not_false_positive_on_bill(self):
        self.assertFalse(docs.is_us_political_content(
            "Check your monthly billing statement for hidden card fees."))

    def test_devoted_does_not_false_positive_on_vote(self):
        self.assertFalse(docs.is_us_political_content(
            "She devoted her career to volunteer animal rescue work."))

    def test_genuine_vote_usage_still_matches(self):
        self.assertTrue(docs.is_us_political_content(
            "He voted early in the midterm election."))

    def test_polluted_does_not_false_positive_on_poll(self):
        self.assertFalse(docs.is_us_political_content(
            "The polluted river has raised concerns among local anglers."))

    def test_taxi_does_not_false_positive_on_tax(self):
        self.assertFalse(docs.is_us_political_content(
            "The taxi driver navigated the syntax of a new app update."))

    def test_lawyer_does_not_false_positive_on_law(self):
        self.assertFalse(docs.is_us_political_content(
            "The flawed lawyer joke drew a laugh at the outlawed comedy show."))

    def test_genuine_law_usage_still_matches(self):
        self.assertTrue(docs.is_us_political_content(
            "Congress passed a new law regulating emissions."))

    def test_no_text_or_title_is_false(self):
        self.assertFalse(docs.is_us_political_content("", ""))

    def test_url_exclusion_pattern_rejects_even_with_keyword(self):
        self.assertFalse(docs.is_us_political_content(
            "The senator watched the game.", url="https://example.com/sports/football/recap"
        ))


class IsIndexPageTests(unittest.TestCase):
    def test_hub_url_with_chrome_terms_is_flagged(self):
        text = "Politics Sports Newsletters Download Our App Skip to main content"
        self.assertTrue(docs.is_index_page(text, url="https://example.com/"))

    def test_real_article_is_not_flagged(self):
        text = (
            "The Senate on Tuesday narrowly approved a sweeping budget package. "
            "Democrats argued the bill would balloon the deficit. Republicans "
            "countered that the cuts would spur growth. The vote was 51-49."
        )
        self.assertFalse(docs.is_index_page(text, url="https://example.com/2026/07/22/senate-vote"))

    def test_empty_text_is_not_flagged(self):
        self.assertFalse(docs.is_index_page("", url="https://example.com/"))


class RecencyTests(unittest.TestCase):
    def test_none_published_at_is_recent(self):
        self.assertTrue(docs.is_recent(None, now=_dt(2026, 7, 22)))

    def test_within_window_is_recent(self):
        self.assertTrue(docs.is_recent(_dt(2026, 7, 1), now=_dt(2026, 7, 22)))

    def test_older_than_thirty_days_is_not_recent(self):
        self.assertFalse(docs.is_recent(_dt(2026, 5, 1), now=_dt(2026, 7, 22)))

    def test_before_2020_is_rejected(self):
        self.assertFalse(docs.is_recent(_dt(2019, 12, 31), now=_dt(2026, 7, 22)))

    def test_more_than_a_day_in_future_is_rejected(self):
        self.assertFalse(docs.is_recent(_dt(2026, 7, 25), now=_dt(2026, 7, 22)))

    def test_stamp_published_at_none_uses_now(self):
        now = _dt(2026, 7, 22)
        self.assertEqual(docs.stamp_published_at(None, now), now)

    def test_stamp_published_at_preserves_real_value(self):
        now = _dt(2026, 7, 22)
        published = _dt(2026, 7, 1)
        self.assertEqual(docs.stamp_published_at(published, now), published)


class DomainFilterConfigTests(unittest.TestCase):
    def test_missing_file_returns_defaults(self):
        cfg = docs.load_domain_filter_config(Path("/nonexistent/seeds.yaml"))
        self.assertEqual(cfg.deny, frozenset())
        self.assertEqual(cfg.allow, frozenset())
        self.assertEqual(cfg.max_docs_per_domain_per_window, 0)

    def test_missing_section_returns_defaults(self):
        with tempfile.NamedTemporaryFile(mode="w", suffix=".yaml", delete=False) as f:
            f.write("database:\n  path: data/x.db\n")
            path = Path(f.name)
        try:
            cfg = docs.load_domain_filter_config(path)
            self.assertEqual(cfg.deny, frozenset())
        finally:
            path.unlink()

    def test_full_section_parses_and_lowercases(self):
        with tempfile.NamedTemporaryFile(mode="w", suffix=".yaml", delete=False) as f:
            f.write(
                "domain_filter:\n"
                "  deny:\n    - WWW.Comparecards.com\n"
                "  allow:\n    - Example.com\n"
                "  max_docs_per_domain_per_window: 50\n"
            )
            path = Path(f.name)
        try:
            cfg = docs.load_domain_filter_config(path)
            self.assertEqual(cfg.deny, frozenset({"www.comparecards.com"}))
            self.assertEqual(cfg.allow, frozenset({"example.com"}))
            self.assertEqual(cfg.max_docs_per_domain_per_window, 50)
        finally:
            path.unlink()

    def test_real_seeds_yaml_denies_comparecards(self):
        """Guards against the deny-list entry silently regressing out of
        data/seeds.yaml."""
        cfg = docs.load_domain_filter_config()
        self.assertIn("www.comparecards.com", cfg.deny)


class SelectWithinDomainCapTests(unittest.TestCase):
    def test_zero_cap_is_uncapped(self):
        candidates = [{"domain_key": "a.com", "published_at": _dt(2026, 7, 1)}]
        admitted, skipped = docs.select_within_domain_cap(candidates, {}, cap=0)
        self.assertEqual(admitted, candidates)
        self.assertEqual(skipped, 0)

    def test_newest_first_when_over_cap(self):
        candidates = [
            {"domain_key": "a.com", "published_at": _dt(2026, 7, 1), "id": "oldest"},
            {"domain_key": "a.com", "published_at": _dt(2026, 7, 20), "id": "newest"},
            {"domain_key": "a.com", "published_at": _dt(2026, 7, 10), "id": "middle"},
        ]
        admitted, skipped = docs.select_within_domain_cap(candidates, {}, cap=2)
        self.assertEqual([c["id"] for c in admitted], ["newest", "middle"])
        self.assertEqual(skipped, 1)

    def test_existing_count_reduces_remaining_slots(self):
        candidates = [
            {"domain_key": "a.com", "published_at": _dt(2026, 7, 20), "id": "one"},
            {"domain_key": "a.com", "published_at": _dt(2026, 7, 19), "id": "two"},
        ]
        admitted, skipped = docs.select_within_domain_cap(candidates, {"a.com": 49}, cap=50)
        self.assertEqual([c["id"] for c in admitted], ["one"])
        self.assertEqual(skipped, 1)

    def test_existing_count_at_or_over_cap_rejects_all(self):
        candidates = [{"domain_key": "a.com", "published_at": _dt(2026, 7, 20)}]
        admitted, skipped = docs.select_within_domain_cap(candidates, {"a.com": 50}, cap=50)
        self.assertEqual(admitted, [])
        self.assertEqual(skipped, 1)

    def test_none_published_at_sorts_as_newest(self):
        candidates = [
            {"domain_key": "a.com", "published_at": _dt(2026, 7, 20), "id": "dated"},
            {"domain_key": "a.com", "published_at": None, "id": "undated"},
        ]
        admitted, skipped = docs.select_within_domain_cap(candidates, {}, cap=1)
        self.assertEqual(admitted[0]["id"], "undated")
        self.assertEqual(skipped, 1)

    def test_domains_are_independent(self):
        candidates = [
            {"domain_key": "a.com", "published_at": _dt(2026, 7, 20)},
            {"domain_key": "b.com", "published_at": _dt(2026, 7, 20)},
        ]
        admitted, skipped = docs.select_within_domain_cap(candidates, {}, cap=1)
        self.assertEqual(len(admitted), 2)
        self.assertEqual(skipped, 0)


class SourceUrlTests(unittest.TestCase):
    def test_reddit_url_strips_fullname_prefix(self):
        url = docs._build_reddit_source_url("t3_abc123", "politics")
        self.assertEqual(url, "https://reddit.com/r/politics/comments/abc123")

    def test_reddit_url_falls_back_to_all(self):
        url = docs._build_reddit_source_url("t3_abc123", None)
        self.assertEqual(url, "https://reddit.com/r/all/comments/abc123")

    def test_x_url_is_handle_independent(self):
        url = docs._build_x_source_url("999888777")
        self.assertEqual(url, "https://x.com/i/web/status/999888777")


# ---------------------------------------------------------------------------
# Integration tests — gated on CIVIC_TEST_DATABASE_URL
# ---------------------------------------------------------------------------

REPO_ROOT = Path(project_root)
MIGRATION_SQL = REPO_ROOT / "data" / "pg-migrations" / "0001_north_star.sql"


@unittest.skipUnless(
    os.environ.get("CIVIC_TEST_DATABASE_URL"),
    "CIVIC_TEST_DATABASE_URL not set — no Postgres server available to test against",
)
class LoadNewDocumentsIntegrationTests(unittest.TestCase):
    """Live-run against a real Postgres with 0001 applied. Each test seeds
    its own raw.* fixture rows and truncates corpus/ops tables in setUp so
    tests are independent without needing a fresh container per test."""

    @classmethod
    def setUpClass(cls):
        import psycopg
        cls._dsn = os.environ["CIVIC_TEST_DATABASE_URL"]
        with psycopg.connect(cls._dsn, autocommit=True) as conn:
            # Idempotent so this class can share a running container with
            # other Phase 4 test modules in the same `python -m unittest
            # discover` process.
            conn.execute("DROP SCHEMA IF EXISTS raw, corpus, analysis, serving, ops, archive CASCADE")
            conn.execute(MIGRATION_SQL.read_text())

    def setUp(self):
        from analysis.src.common import db as dbmod
        dbmod.close_pool()
        self._prev_url = os.environ.get("CIVIC_DATABASE_URL")
        os.environ["CIVIC_DATABASE_URL"] = self._dsn
        self._truncate_all()

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
                "TRUNCATE ops.task_queue, corpus.x_posts, corpus.reddit_posts, "
                "corpus.news_articles, corpus.documents, corpus.authors, "
                "raw.x_posts, raw.x_users, raw.reddit_posts, raw.articles, raw.pages CASCADE"
            )

    def _insert_page_and_article(self, conn, url_canon, domain, raw_hash, title, published_at):
        conn.execute(
            "INSERT INTO raw.pages (url_canon, url_raw, domain, state) VALUES (%s, %s, %s, 'done')",
            (url_canon, url_canon, domain),
        )
        conn.execute(
            "INSERT INTO raw.articles (url_canon, domain, fetched_at, published_at, title, "
            "raw_hash, extraction_version) VALUES (%s, %s, now(), %s, %s, %s, 'v1')",
            (url_canon, domain, published_at, title, raw_hash),
        )

    def _write_raw_html(self, raw_root: Path, raw_hash: str, text: str):
        html_dir = raw_root / raw_hash[:2]
        html_dir.mkdir(parents=True, exist_ok=True)
        (html_dir / f"{raw_hash}.html").write_text(f"<html><body><p>{text}</p></body></html>")

    def _seed_outlet_entity(self, domain: str) -> None:
        """Register `domain` as a kind='outlet' corpus.entities row --
        curation is DB-native (data/pg-migrations/0002_entity_registry_seed.sql),
        so a direct insert stands in for what a curator would do by hand."""
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean, editorial) "
                "VALUES (%s, 'outlet'::corpus.entity_kind, 'Test Outlet', "
                "'independent'::corpus.political_lean, true)",
                (domain,),
            )

    def _seed_subreddit_entity(self, subreddit: str) -> None:
        """Same as `_seed_outlet_entity` for a kind='subreddit' entity."""
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO corpus.entities (entity_key, kind, display_name, lean, editorial) "
                "VALUES (%s, 'subreddit'::corpus.entity_kind, 'r/Test', "
                "'mixed'::corpus.political_lean, true)",
                (subreddit,),
            )

    def test_news_doc_inserted_with_subtype_row_and_null_author(self):
        raw_hash = "aa" + "1" * 62
        with tempfile.TemporaryDirectory() as tmp:
            raw_root = Path(tmp)
            self._write_raw_html(
                raw_root, raw_hash,
                "The Senate passed a sweeping spending bill after a long debate "
                "in Congress on Tuesday evening in Washington.",
            )
            with docs.db.connection() as conn:
                self._insert_page_and_article(
                    conn, "https://example.com/a1", "example.com", raw_hash,
                    "Senate passes bill", datetime.datetime.now(UTC),
                )
            result = docs.load_new_documents(raw_root=raw_root)
            self.assertEqual(result.inserted, 1)

            with docs.db.connection() as conn:
                row = conn.execute(
                    "SELECT doc_id, author_id, source_url FROM corpus.documents WHERE source_type='news'"
                ).fetchone()
                self.assertIsNotNone(row)
                self.assertIsNone(row["author_id"])
                self.assertEqual(row["source_url"], "https://example.com/a1")
                sub = conn.execute(
                    "SELECT doc_id FROM corpus.news_articles WHERE doc_id = %s", (row["doc_id"],)
                ).fetchone()
                self.assertIsNotNone(sub)

    def test_reddit_doc_inserted_with_subtype_row_and_synthesized_url(self):
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.reddit_posts (fullname, subreddit, created_utc, fetched_at, "
                "title, body, score, num_comments, raw_hash, extraction_version) VALUES "
                "('t3_abc123', 'politics', now(), now(), 'Congress votes on bill', "
                "'The house passed the immigration bill today.', 42, 7, "
                "'r'||repeat('0', 63), 'v1')"
            )
        result = docs.load_new_documents()
        self.assertEqual(result.inserted, 1)
        with docs.db.connection() as conn:
            row = conn.execute(
                "SELECT doc_id, author_id, source_url FROM corpus.documents WHERE source_type='reddit_post'"
            ).fetchone()
            self.assertIsNotNone(row)
            self.assertIsNone(row["author_id"])
            self.assertEqual(row["source_url"], "https://reddit.com/r/politics/comments/abc123")
            sub = conn.execute(
                "SELECT subreddit, score, num_comments FROM corpus.reddit_posts WHERE doc_id = %s",
                (row["doc_id"],),
            ).fetchone()
            self.assertEqual(sub["subreddit"], "politics")
            self.assertEqual(sub["score"], 42)
            self.assertEqual(sub["num_comments"], 7)

    def test_reddit_nonpolitical_post_is_skipped(self):
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.reddit_posts (fullname, subreddit, created_utc, fetched_at, "
                "title, body, raw_hash, extraction_version) VALUES "
                "('t3_def456', 'cooking', now(), now(), 'Best pasta recipe', "
                "'Add salt to the boiling water and taste as you go.', "
                "'s'||repeat('0', 63), 'v1')"
            )
        result = docs.load_new_documents()
        self.assertEqual(result.inserted, 0)
        self.assertEqual(result.skipped_nonpolitical, 1)

    def test_deny_list_rejects_seeded_domain(self):
        raw_hash = "bb" + "2" * 62
        with tempfile.TemporaryDirectory() as tmp:
            raw_root = Path(tmp)
            self._write_raw_html(
                raw_root, raw_hash,
                "Congress debated a new tax bill affecting billions in credit card debt.",
            )
            with docs.db.connection() as conn:
                self._insert_page_and_article(
                    conn, "https://www.comparecards.com/a1", "www.comparecards.com", raw_hash,
                    "Credit card bill", datetime.datetime.now(UTC),
                )
            with tempfile.NamedTemporaryFile(mode="w", suffix=".yaml", delete=False) as f:
                f.write("domain_filter:\n  deny:\n    - www.comparecards.com\n")
                seeds_path = Path(f.name)
            try:
                result = docs.load_new_documents(seeds_path=seeds_path, raw_root=raw_root)
                self.assertEqual(result.inserted, 0)
                self.assertEqual(result.skipped_denied, 1)
            finally:
                seeds_path.unlink()

    def test_idempotent_rerun_is_zero_delta(self):
        raw_hash = "cc" + "3" * 62
        with tempfile.TemporaryDirectory() as tmp:
            raw_root = Path(tmp)
            self._write_raw_html(
                raw_root, raw_hash,
                "The president signed an executive order on immigration policy Monday.",
            )
            with docs.db.connection() as conn:
                self._insert_page_and_article(
                    conn, "https://example.com/a2", "example.com", raw_hash,
                    "President signs order", datetime.datetime.now(UTC),
                )
            first = docs.load_new_documents(raw_root=raw_root)
            second = docs.load_new_documents(raw_root=raw_root)
            self.assertEqual(first.inserted, 1)
            self.assertEqual(second.inserted, 0)
            with docs.db.connection() as conn:
                count = conn.execute("SELECT COUNT(*) AS n FROM corpus.documents").fetchone()["n"]
                self.assertEqual(count, 1)

    def test_cap_enforcement_admits_newest_first(self):
        with tempfile.TemporaryDirectory() as tmp:
            raw_root = Path(tmp)
            base_time = datetime.datetime.now(UTC)
            for i in range(3):
                raw_hash = f"d{i}" + str(i) * 61
                self._write_raw_html(
                    raw_root, raw_hash,
                    f"Congress passed bill number {i} regulating federal tax policy today.",
                )
                with docs.db.connection() as conn:
                    self._insert_page_and_article(
                        conn, f"https://example.com/cap{i}", "example.com", raw_hash,
                        f"Bill {i}", base_time - datetime.timedelta(hours=i),
                    )
            with tempfile.NamedTemporaryFile(mode="w", suffix=".yaml", delete=False) as f:
                f.write("domain_filter:\n  max_docs_per_domain_per_window: 2\n")
                seeds_path = Path(f.name)
            try:
                result = docs.load_new_documents(seeds_path=seeds_path, raw_root=raw_root)
                self.assertEqual(result.inserted, 2)
                self.assertEqual(result.skipped_capped, 1)
                with docs.db.connection() as conn:
                    urls = {
                        row["source_url"] for row in
                        conn.execute("SELECT source_url FROM corpus.documents").fetchall()
                    }
                    # Newest two of cap0 (t=base), cap1 (t=base-1h), cap2 (t=base-2h)
                    # are cap0 and cap1; cap2 is the oldest and should be capped out.
                    self.assertIn("https://example.com/cap0", urls)
                    self.assertIn("https://example.com/cap1", urls)
                    self.assertNotIn("https://example.com/cap2", urls)
            finally:
                seeds_path.unlink()

    def test_x_post_resolves_author_id_when_synced(self):
        from analysis.src.etl import authors as authors_mod
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.x_users (user_id, username, name, description, location, "
                "profile_image_url, verified, verified_type, followers_count, following_count, "
                "created_at, fetched_at, raw_hash) VALUES ('u1', 'senjones', 'Sen Jones', '', '', "
                "'', false, NULL, 100, 10, now(), now(), 'x'||repeat('0', 63))"
            )
            conn.execute(
                "INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, "
                "raw_hash, extraction_version) VALUES ('t1', 'u1', now(), now(), "
                "'Congress must pass this bill on immigration policy now', "
                "'y'||repeat('0', 63), 'v1')"
            )
        authors_mod.sync_x_authors()
        result = docs.load_new_documents()
        self.assertEqual(result.inserted, 1)
        with docs.db.connection() as conn:
            row = conn.execute(
                "SELECT d.author_id FROM corpus.documents d WHERE d.source_type='x_post'"
            ).fetchone()
            self.assertIsNotNone(row["author_id"])

    def test_news_doc_resolves_outlet_entity_id_when_registered(self):
        """Matched outlet -> corpus.news_articles.outlet_entity_id populated
        (this closure's entity-FK restoration)."""
        self._seed_outlet_entity("registered-outlet.example")
        raw_hash = "e0" + "0" * 62
        with tempfile.TemporaryDirectory() as tmp:
            raw_root = Path(tmp)
            self._write_raw_html(
                raw_root, raw_hash,
                "Congress passed a new immigration bill after weeks of debate today.",
            )
            with docs.db.connection() as conn:
                self._insert_page_and_article(
                    conn, "https://registered-outlet.example/a1", "registered-outlet.example",
                    raw_hash, "Bill passes", datetime.datetime.now(UTC),
                )
            result = docs.load_new_documents(raw_root=raw_root)
            self.assertEqual(result.inserted, 1)
            with docs.db.connection() as conn:
                row = conn.execute(
                    "SELECT na.outlet_entity_id, e.entity_key FROM corpus.news_articles na "
                    "JOIN corpus.entities e ON e.entity_id = na.outlet_entity_id "
                    "JOIN corpus.documents d ON d.doc_id = na.doc_id "
                    "WHERE d.source_type = 'news'"
                ).fetchone()
                self.assertIsNotNone(row)
                self.assertEqual(row["entity_key"], "registered-outlet.example")

    def test_news_doc_outlet_entity_id_null_when_unregistered(self):
        """Unmatched outlet -> outlet_entity_id stays NULL, doc still loads
        (never fails a doc over a registry miss)."""
        raw_hash = "e1" + "1" * 62
        with tempfile.TemporaryDirectory() as tmp:
            raw_root = Path(tmp)
            self._write_raw_html(
                raw_root, raw_hash,
                "Congress passed a new immigration bill after weeks of debate today.",
            )
            with docs.db.connection() as conn:
                self._insert_page_and_article(
                    conn, "https://unregistered-outlet.example/a1", "unregistered-outlet.example",
                    raw_hash, "Bill passes", datetime.datetime.now(UTC),
                )
            result = docs.load_new_documents(raw_root=raw_root)
            self.assertEqual(result.inserted, 1)
            with docs.db.connection() as conn:
                row = conn.execute(
                    "SELECT na.outlet_entity_id FROM corpus.news_articles na "
                    "JOIN corpus.documents d ON d.doc_id = na.doc_id WHERE d.source_type = 'news'"
                ).fetchone()
                self.assertIsNotNone(row)
                self.assertIsNone(row["outlet_entity_id"])

    def test_outlet_entity_id_backfills_after_later_curation(self):
        """A doc loaded before its outlet is registered gets outlet_entity_id
        = NULL; once the outlet is curated into corpus.entities, the NEXT
        documents.py run backfills it without re-inserting the doc
        (idempotency contract)."""
        raw_hash = "e2" + "2" * 62
        with tempfile.TemporaryDirectory() as tmp:
            raw_root = Path(tmp)
            self._write_raw_html(
                raw_root, raw_hash,
                "Congress passed a new immigration bill after weeks of debate today.",
            )
            with docs.db.connection() as conn:
                self._insert_page_and_article(
                    conn, "https://later-registered.example/a1", "later-registered.example",
                    raw_hash, "Bill passes", datetime.datetime.now(UTC),
                )
            first = docs.load_new_documents(raw_root=raw_root)
            self.assertEqual(first.inserted, 1)
            with docs.db.connection() as conn:
                before = conn.execute(
                    "SELECT na.doc_id, na.outlet_entity_id FROM corpus.news_articles na "
                    "JOIN corpus.documents d ON d.doc_id = na.doc_id WHERE d.source_type = 'news'"
                ).fetchone()
                self.assertIsNone(before["outlet_entity_id"])

            self._seed_outlet_entity("later-registered.example")
            second = docs.load_new_documents(raw_root=raw_root)
            self.assertEqual(second.inserted, 0)  # doc already exists, not re-inserted
            with docs.db.connection() as conn:
                after = conn.execute(
                    "SELECT outlet_entity_id FROM corpus.news_articles WHERE doc_id = %s",
                    (before["doc_id"],),
                ).fetchone()
                self.assertIsNotNone(after["outlet_entity_id"])
                count = conn.execute("SELECT COUNT(*) AS n FROM corpus.documents").fetchone()["n"]
                self.assertEqual(count, 1)  # backfill, not a re-insert

    def test_reddit_doc_resolves_subreddit_entity_id_when_registered(self):
        self._seed_subreddit_entity("registeredsub")
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.reddit_posts (fullname, subreddit, created_utc, fetched_at, "
                "title, body, score, num_comments, raw_hash, extraction_version) VALUES "
                "('t3_regsub1', 'registeredsub', now(), now(), 'Congress votes on bill', "
                "'The house passed the immigration bill today.', 10, 2, "
                "'f'||repeat('0', 63), 'v1')"
            )
        result = docs.load_new_documents()
        self.assertEqual(result.inserted, 1)
        with docs.db.connection() as conn:
            row = conn.execute(
                "SELECT rp.subreddit_entity_id, e.entity_key FROM corpus.reddit_posts rp "
                "JOIN corpus.entities e ON e.entity_id = rp.subreddit_entity_id "
                "JOIN corpus.documents d ON d.doc_id = rp.doc_id WHERE d.source_type = 'reddit_post'"
            ).fetchone()
            self.assertIsNotNone(row)
            self.assertEqual(row["entity_key"], "registeredsub")

    def test_reddit_doc_subreddit_entity_id_null_when_unregistered(self):
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.reddit_posts (fullname, subreddit, created_utc, fetched_at, "
                "title, body, score, num_comments, raw_hash, extraction_version) VALUES "
                "('t3_unregsub1', 'unregisteredsub', now(), now(), 'Congress votes on bill', "
                "'The house passed the immigration bill today.', 10, 2, "
                "'g'||repeat('0', 63), 'v1')"
            )
        result = docs.load_new_documents()
        self.assertEqual(result.inserted, 1)
        with docs.db.connection() as conn:
            row = conn.execute(
                "SELECT rp.subreddit_entity_id FROM corpus.reddit_posts rp "
                "JOIN corpus.documents d ON d.doc_id = rp.doc_id WHERE d.source_type = 'reddit_post'"
            ).fetchone()
            self.assertIsNotNone(row)
            self.assertIsNone(row["subreddit_entity_id"])

    def test_x_post_author_id_null_when_unsynced(self):
        with docs.db.connection() as conn:
            conn.execute(
                "INSERT INTO raw.x_posts (tweet_id, author_id, created_at, fetched_at, text, "
                "raw_hash, extraction_version) VALUES ('t2', 'u_unsynced', now(), now(), "
                "'The senate passed a new immigration bill today', "
                "'z'||repeat('0', 63), 'v1')"
            )
        result = docs.load_new_documents()
        self.assertEqual(result.inserted, 1)
        with docs.db.connection() as conn:
            row = conn.execute(
                "SELECT d.author_id FROM corpus.documents d WHERE d.source_type='x_post'"
            ).fetchone()
            self.assertIsNone(row["author_id"])


if __name__ == "__main__":
    unittest.main()
