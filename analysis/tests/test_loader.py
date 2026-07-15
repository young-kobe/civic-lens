import json
import os
import sqlite3
import sys
import tempfile
import time
import unittest

# Ensure project root is in path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.etl.loader import ContentLoader, SQLITE_BUSY_TIMEOUT_MS, is_index_page

MIGRATIONS_DIR = os.path.join(project_root, "data", "migrations")

# Real-world junk shapes from the 2026-07-11 prod incident: trafilatura output
# for the CBS and NPR homepages, which passed the political-keyword filter
# (menus name political topics) and were then bot-scored as "articles".
CBS_HOMEPAGE_JUNK = (
    "Latest U.S. Iran War World Cup Trump Administration Immigration Capitol "
    "Assault Crime Sports Essentials Local News Baltimore Bay Area Boston "
    "Chicago Colorado Detroit Los Angeles Miami Minnesota New York "
    "Philadelphia Pittsburgh Sacramento Texas Live CBS News 24/7 Baltimore "
    "Bay Area Boston Chicago Colorado Detroit Los Angeles Miami Minnesota "
    "New York Philadelphia Pittsburgh Sacramento Texas Shows 60 Minutes "
    "48 Hours CBS Evening News CBS Mornings Face the Nation Sunday Morning "
    "The Takeout with Major Garrett Brand Studio Live Local More Latest "
    "Video Photos Podcasts In Depth CBS News Team Executive Team Brand "
    "Studio Paramount Shop RSS Feeds Newsletters Download Our App Watch CBS News"
)

NPR_HOMEPAGE_JUNK = (
    "Accessibility links Skip to main content Keyboard shortcuts for audio "
    "player Open Navigation Menu NPR Shop Newsletters Sign In NPR Shop Home "
    "News Expand/collapse submenu for News National World Politics Business "
    "Health Science Climate Race Culture Expand/collapse submenu for Culture "
    "Books Movies Television Pop Culture Food Art & Design Performing Arts "
    "Music Expand/collapse submenu for Music All Songs Considered Tiny Desk "
    "New Music Friday Music Features Live Sessions Podcasts & Shows "
    "Expand/collapse submenu for Podcasts & Shows Daily Morning Edition "
    "Weekend Edition Saturday Weekend Edition Sunday All Things Considered "
    "Fresh Air Up First Featured Embedded The NPR Politics Podcast "
    "Throughline Trump's Terms More Podcasts & Shows Search Newsletters NPR Shop"
)

REAL_ARTICLE_TEXT = (
    "WASHINGTON — The Senate on Tuesday narrowly approved a sweeping budget "
    "package that would extend federal tax cuts and raise the debt ceiling, "
    "sending the measure back to the House for a final vote. The 51-49 tally "
    "followed a marathon overnight session in which lawmakers rejected dozens "
    "of amendments. Democrats argued the bill would balloon the deficit, "
    "citing a Congressional Budget Office estimate released last week. "
    "Republicans countered that the cuts would spur economic growth. "
    "President Trump praised the vote in a post on social media, calling it "
    "a win for American workers. The House is expected to take up the "
    "measure as soon as Thursday, though a handful of conservative holdouts "
    "have signaled they may withhold support over the spending levels."
)

REAL_BRIEF_TEXT = (
    "The governor signed the election reform bill into law on Friday. The "
    "measure expands early voting by six days and requires counties to "
    "publish ballot-count audits. Opponents said they plan to challenge the "
    "law in federal court next week."
)


def _apply_migrations(db_path: str) -> None:
    """Apply all SQL migrations from the project migrations directory."""
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    for m_file in sorted(os.listdir(MIGRATIONS_DIR)):
        if m_file.endswith(".sql"):
            with open(os.path.join(MIGRATIONS_DIR, m_file), "r") as f:
                cursor.executescript(f.read())
    conn.commit()
    conn.close()


class TestContentLoaderBatched(unittest.TestCase):
    def setUp(self):
        # Each test gets its own isolated temporary database file.
        self._tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = self._tmp.name
        self._tmp.close()  # Close the file handle so SQLite can open it.

        _apply_migrations(self.db_path)

        now = int(time.time())
        # Use political keywords so rows pass the is_us_political_content filter.
        political_title = "Senate vote on federal election reform bill"
        conn = sqlite3.connect(self.db_path)
        conn.executemany(
            "INSERT INTO articles_raw (url_canon, domain, fetched_at, published_at, title, raw_hash, extraction_version) VALUES (?, ?, ?, ?, ?, ?, ?)",
            [(f"http://news.com/{i}", "news.com", now, now, political_title, f"hash{i}", "v1") for i in range(50)],
        )
        conn.executemany(
            "INSERT INTO reddit_posts_raw (fullname, subreddit, created_utc, title, body, raw_hash, extraction_version) VALUES (?, ?, ?, ?, ?, ?, ?)",
            [(f"t3_{i}", "r/politics", now, political_title, "election body text", f"rhash{i}", "v1") for i in range(50)],
        )
        conn.commit()
        conn.close()

        self.loader = ContentLoader(self.db_path)

    def tearDown(self):
        # Attempt cleanup; tolerate Windows lock failures gracefully.
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except FileNotFoundError:
                pass
            except PermissionError:
                pass  # Windows may hold the handle briefly; temp dir cleanup handles it later.

    def test_batched_load_inserts_all_rows(self):
        """Verify batched load_new_raw_content inserts all qualifying rows into docs."""
        count = self.loader.load_new_raw_content()
        self.assertGreater(count, 0, "Expected at least some docs to be loaded")

        with sqlite3.connect(self.db_path) as conn:
            doc_count = conn.execute("SELECT COUNT(*) FROM docs").fetchone()[0]

        self.assertEqual(doc_count, count)

    def test_busy_timeout_pragma(self):
        """Verify the connection manager sets WAL mode and busy_timeout.

        The 15s timeout (raised from 5s) is what lets writes ride out
        Litestream checkpoint contention instead of throwing "database is
        locked" mid-batch (2026-07-15); assert the actual value so a silent
        regression to a too-short timeout is caught.
        """
        with self.loader._get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute("PRAGMA journal_mode;")
            self.assertEqual(cursor.fetchone()[0].lower(), "wal")
            cursor.execute("PRAGMA busy_timeout;")
            self.assertEqual(cursor.fetchone()[0], SQLITE_BUSY_TIMEOUT_MS)


class TestIndexPageDetector(unittest.TestCase):
    """is_index_page separates nav-menu scrapes from real articles.

    WHY: index pages passed the political-keyword filter (menus name
    political topics) and then polluted tone, narratives, and bot metrics
    while burning LLM budget. The detector must be aggressive on menu text
    but must NEVER drop a genuine article — a false positive here silently
    censors real coverage, which is the worse failure.
    """

    def test_cbs_homepage_junk_is_flagged(self):
        self.assertTrue(is_index_page(CBS_HOMEPAGE_JUNK, "https://www.cbsnews.com/"))

    def test_npr_homepage_junk_is_flagged(self):
        self.assertTrue(is_index_page(NPR_HOMEPAGE_JUNK, "https://www.npr.org/"))

    def test_junk_text_flagged_even_at_deep_url(self):
        # The crawler sometimes stores hub content under odd canonical URLs;
        # the text shape alone must be enough.
        self.assertTrue(is_index_page(CBS_HOMEPAGE_JUNK, "https://www.cbsnews.com/live-updates/today/"))
        self.assertTrue(is_index_page(NPR_HOMEPAGE_JUNK, ""))

    def test_section_front_url_with_menu_text_is_flagged(self):
        self.assertTrue(is_index_page(CBS_HOMEPAGE_JUNK, "https://www.cbsnews.com/politics/"))

    def test_real_article_is_never_flagged(self):
        self.assertFalse(is_index_page(
            REAL_ARTICLE_TEXT,
            "https://www.example.com/politics/senate-approves-budget-package-2026/",
        ))

    def test_short_real_brief_is_never_flagged(self):
        self.assertFalse(is_index_page(
            REAL_BRIEF_TEXT,
            "https://www.example.com/news/governor-signs-election-reform-bill/",
        ))

    def test_empty_text_is_not_the_detectors_call(self):
        # Empty extraction is skipped by the loader's existing guard, not
        # misreported as an index page.
        self.assertFalse(is_index_page("", "https://www.cbsnews.com/"))


class TestIndexPageFilterWiring(unittest.TestCase):
    """The ETL drops index pages before the political filter and counts them."""

    def setUp(self):
        self._tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = self._tmp.name
        self._tmp.close()
        _apply_migrations(self.db_path)

        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        conn.executemany(
            "INSERT INTO articles_raw (url_canon, domain, fetched_at, published_at, title, raw_hash, extraction_version) VALUES (?, ?, ?, ?, ?, ?, ?)",
            [
                ("https://www.cbsnews.com/", "cbsnews.com", now, now,
                 "CBS News | Breaking news", "junkhash", "v1"),
                ("https://www.example.com/politics/senate-approves-budget-2026/",
                 "example.com", now, now,
                 "Senate approves budget package", "articlehash", "v1"),
            ],
        )
        conn.commit()
        conn.close()

        self.loader = ContentLoader(self.db_path)
        # Bypass trafilatura/raw files: return the prod junk for the hub row
        # and real prose for the article row.
        self.loader._extract_text_from_raw = lambda raw_hash, raw_root: (
            CBS_HOMEPAGE_JUNK if raw_hash == "junkhash" else REAL_ARTICLE_TEXT
        )

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def test_index_page_skipped_article_loaded(self):
        count = self.loader.load_new_raw_content()
        self.assertEqual(count, 1)

        with sqlite3.connect(self.db_path) as conn:
            idents = [r[0] for r in conn.execute(
                "SELECT ident FROM docs WHERE source_type='news'"
            ).fetchall()]
        self.assertEqual(
            idents,
            ["https://www.example.com/politics/senate-approves-budget-2026/"],
            "The homepage scrape must be dropped and the real article kept",
        )


class TestDocTaskState(unittest.TestCase):
    """Work-queue semantics of doc_task_state (migration 022).

    The state table exists so that (a) a transient LLM failure re-queues the
    doc but leaves a visible trace instead of silently persisting nothing,
    and (b) a task can be reprocessed under a new prompt_version by deleting
    state rows while ai_outputs keeps every historical result row.
    """

    def setUp(self):
        self._tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = self._tmp.name
        self._tmp.close()
        _apply_migrations(self.db_path)

        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            "INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,"
            " published_at, text, raw_hash, metadata_json)"
            " VALUES (1, 'news', 'http://n.com/1', 'n.com', ?, 'body', 'h1', '{}')",
            (now,),
        )
        conn.commit()
        conn.close()
        self.loader = ContentLoader(self.db_path)

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def _state_row(self):
        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT status, prompt_version, attempts FROM doc_task_state"
            " WHERE doc_id = 1 AND task_type = 'claims'"
        ).fetchone()
        conn.close()
        return row

    def test_save_marks_done_and_dequeues(self):
        self.assertEqual(len(self.loader.get_unprocessed_docs("claims")), 1)
        self.loader.save_ai_output(1, "claims", {"claims": []}, 0.9,
                                   prompt_version="v1")
        self.assertEqual(len(self.loader.get_unprocessed_docs("claims")), 0)
        self.assertEqual(self._state_row(), ("done", "v1", 1))

    def test_failed_attempt_requeues_but_is_observable(self):
        # A transient outage must never freeze a doc as done — but the
        # retries must be visible, not the old persist-nothing pattern.
        self.loader.mark_task_failed(1, "claims", "v1")
        self.loader.mark_task_failed(1, "claims", "v1")
        self.assertEqual(self._state_row(), ("failed", "v1", 2))
        self.assertEqual(len(self.loader.get_unprocessed_docs("claims")), 1)
        # A later success flips the row to done and dequeues.
        self.loader.save_ai_output(1, "claims", {"claims": []}, 0.9,
                                   prompt_version="v1")
        self.assertEqual(self._state_row(), ("done", "v1", 3))
        self.assertEqual(len(self.loader.get_unprocessed_docs("claims")), 0)

    def test_reprocess_appends_and_latest_view_wins(self):
        # Reprocessing = delete state rows. ai_outputs must keep the v1 row
        # (append-only audit history) while every latest-per-doc reader sees
        # only the v2 row, so aggregates never double-count a doc.
        self.loader.save_ai_output(1, "claims", {"v": 1}, 0.9,
                                   prompt_version="v1")
        conn = sqlite3.connect(self.db_path)
        conn.execute("DELETE FROM doc_task_state WHERE task_type = 'claims'")
        conn.commit()
        conn.close()

        self.assertEqual(len(self.loader.get_unprocessed_docs("claims")), 1)
        self.loader.save_ai_output(1, "claims", {"v": 2}, 0.9,
                                   prompt_version="v2")

        conn = sqlite3.connect(self.db_path)
        all_rows = conn.execute(
            "SELECT COUNT(*) FROM ai_outputs WHERE doc_id = 1"
            " AND task_type = 'claims'"
        ).fetchone()[0]
        latest = conn.execute(
            "SELECT output_json, prompt_version FROM ai_outputs_latest"
            " WHERE doc_id = 1 AND task_type = 'claims'"
        ).fetchall()
        conn.close()
        self.assertEqual(all_rows, 2)
        self.assertEqual(len(latest), 1)
        self.assertEqual(latest[0], ('{"v": 2}', "v2"))


class TestLabelColumn(unittest.TestCase):
    """Canonical label column (migration 023).

    The verdict of a row used to live only inside output_json — and for
    bot_detection under TWO keys (label / is_bot), so every reader carried a
    two-key defensive check. The column is the single encoding readers
    filter on; these tests pin the backfill of both legacy encodings and
    the write path.
    """

    def setUp(self):
        self._tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = self._tmp.name
        self._tmp.close()

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def _apply_up_to(self, conn, stop_before: str):
        for m_file in sorted(os.listdir(MIGRATIONS_DIR)):
            if m_file >= stop_before:
                break
            if m_file.endswith(".sql"):
                with open(os.path.join(MIGRATIONS_DIR, m_file)) as f:
                    conn.executescript(f.read())

    def test_backfill_normalizes_both_legacy_bot_encodings(self):
        conn = sqlite3.connect(self.db_path)
        self._apply_up_to(conn, "023")
        conn.execute(
            "INSERT INTO docs (doc_id, source_type, ident, raw_hash)"
            " VALUES (1, 'news', 'u1', 'h1')"
        )
        rows = [
            (1, "bot_detection", '{"is_bot": 1}'),          # boolean-only era
            (1, "bot_detection", '{"is_bot": 0}'),
            (1, "bot_detection", '{"label": "suspicious"}'),  # label era
            (1, "sentiment", '{"label": "POSITIVE"}'),
            (1, "favorability", '{"overall_gop_stance": "favorable"}'),
            (1, "claims", '{"claims": []}'),                  # no scalar verdict
        ]
        conn.executemany(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence)"
            " VALUES (?, ?, ?, 0.9)",
            rows,
        )
        # Apply 023 (and everything after) on the pre-column corpus.
        for m_file in sorted(os.listdir(MIGRATIONS_DIR)):
            if m_file >= "023" and m_file.endswith(".sql"):
                with open(os.path.join(MIGRATIONS_DIR, m_file)) as f:
                    conn.executescript(f.read())

        labels = conn.execute(
            "SELECT task_type, output_json, label FROM ai_outputs ORDER BY output_id"
        ).fetchall()
        by_json = {json_: label for _, json_, label in labels}
        self.assertEqual(by_json['{"is_bot": 1}'], "bot")
        self.assertEqual(by_json['{"is_bot": 0}'], "human")
        self.assertEqual(by_json['{"label": "suspicious"}'], "suspicious")
        self.assertEqual(by_json['{"label": "POSITIVE"}'], "POSITIVE")
        self.assertEqual(by_json['{"overall_gop_stance": "favorable"}'], "favorable")
        self.assertIsNone(by_json['{"claims": []}'])

        # The recreated latest view must expose the new column.
        view_label = conn.execute(
            "SELECT label FROM ai_outputs_latest"
            " WHERE task_type = 'sentiment'"
        ).fetchone()[0]
        self.assertEqual(view_label, "POSITIVE")
        conn.close()

    def test_save_ai_output_returns_output_id(self):
        _apply_migrations(self.db_path)
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            "INSERT INTO docs (doc_id, source_type, ident, raw_hash)"
            " VALUES (1, 'news', 'u1', 'h1')"
        )
        conn.commit()
        conn.close()

        loader = ContentLoader(self.db_path)
        first = loader.save_ai_output(1, "sentiment", {"label": "NEUTRAL"}, 0.5)
        second = loader.save_ai_output(1, "claims", {"claims": []}, 0.5)
        self.assertIsInstance(first, int)
        self.assertEqual(second, first + 1)

    def test_save_ai_output_writes_label(self):
        _apply_migrations(self.db_path)
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            "INSERT INTO docs (doc_id, source_type, ident, raw_hash)"
            " VALUES (1, 'news', 'u1', 'h1')"
        )
        conn.commit()
        conn.close()

        loader = ContentLoader(self.db_path)
        loader.save_ai_output(
            1, "bot_detection", {"label": "bot", "is_bot": True}, 0.9,
            prompt_version="v1", label="bot",
        )
        conn = sqlite3.connect(self.db_path)
        stored = conn.execute(
            "SELECT label FROM ai_outputs WHERE doc_id = 1"
        ).fetchone()[0]
        conn.close()
        self.assertEqual(stored, "bot")


def _resolve_senx(raw):
    """Test resolver: only 'Senator X' is a known entity."""
    if raw == "Senator X":
        return "SenX", "official", "R"
    return None, None, None


class TestTargetMentions(unittest.TestCase):
    """Write-time identity for extracted targets (migration 025).

    Why: target identity used to be re-resolved from free text on every
    aggregation, so a registry edit silently remapped history and
    unresolved mentions vanished (only a counter survived). These tests
    pin: unresolved targets persist as NULL-entity rows, the backfill is
    idempotent, and a later, different resolver does NOT alter stored rows.
    """

    _TARGETS = [
        {"target": "Senator X", "topic": "Economy", "stance": "negative",
         "confidence": 0.9, "evidence_spans": ["a span"]},
        {"target": "Unknown Pundit", "topic": "Other", "stance": "positive",
         "confidence": 0.8},
    ]

    def setUp(self):
        self._tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = self._tmp.name
        self._tmp.close()
        _apply_migrations(self.db_path)
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            "INSERT INTO docs (doc_id, source_type, ident, raw_hash)"
            " VALUES (1, 'news', 'u1', 'h1')"
        )
        conn.commit()
        conn.close()
        self.loader = ContentLoader(self.db_path)

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def _rows(self):
        conn = sqlite3.connect(self.db_path)
        rows = conn.execute(
            "SELECT raw_target, entity_key, entity_kind, entity_party, stance,"
            " topic, confidence FROM target_mentions ORDER BY mention_id"
        ).fetchall()
        conn.close()
        return rows

    def test_unresolved_mentions_persist_and_identity_freezes(self):
        output_id = self.loader.save_ai_output(
            1, "target_sentiment",
            {"targets": self._TARGETS, "reasoning": "r"}, 0.85,
            prompt_version="v1",
        )
        self.loader.save_target_mentions(
            output_id, 1,
            self.loader.build_target_mentions(self._TARGETS, _resolve_senx),
        )
        self.assertEqual(self._rows(), [
            ("Senator X", "SenX", "official", "R", "negative", "Economy", 0.9),
            ("Unknown Pundit", None, None, None, "positive", "Other", 0.8),
        ])

        # Registry drift: a resolver that now matches everything must NOT
        # remap the stored rows — backfill only fills gaps, never rewrites.
        remapped = self.loader.backfill_target_mentions(
            lambda raw: ("SomeoneElse", "official", "D")
        )
        self.assertEqual(remapped, 0)
        self.assertEqual(self._rows()[0][1], "SenX")
        self.assertEqual(self._rows()[1][1], None)

    def test_backfill_materializes_json_only_outputs_idempotently(self):
        # A pre-migration-025 output row: JSON only, no mention rows.
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            "INSERT INTO ai_outputs (doc_id, task_type, output_json, confidence)"
            " VALUES (1, 'target_sentiment', ?, 0.85)",
            (json.dumps({"targets": self._TARGETS, "reasoning": "r"}),),
        )
        conn.commit()
        conn.close()

        self.assertEqual(
            self.loader.backfill_target_mentions(_resolve_senx), 1)
        self.assertEqual(len(self._rows()), 2)
        # Second run: nothing left to materialize, nothing duplicated.
        self.assertEqual(
            self.loader.backfill_target_mentions(_resolve_senx), 0)
        self.assertEqual(len(self._rows()), 2)


if __name__ == "__main__":
    unittest.main()
