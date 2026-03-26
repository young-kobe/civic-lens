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

from analysis.src.etl.loader import ContentLoader

MIGRATIONS_DIR = os.path.join(project_root, "data", "migrations")


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
        """Verify the connection manager sets WAL mode and busy_timeout."""
        with self.loader._get_conn() as conn:
            cursor = conn.cursor()
            cursor.execute("PRAGMA journal_mode;")
            self.assertEqual(cursor.fetchone()[0].lower(), "wal")
            cursor.execute("PRAGMA busy_timeout;")
            self.assertEqual(cursor.fetchone()[0], 5000)


if __name__ == "__main__":
    unittest.main()
