"""
Tests for walkthrough 041:
  - save_ai_output upserts user_prompt_template into prompt_versions.
  - Upsert preserves a previously-stored user_prompt_template when a later
    call omits it (COALESCE behavior).
"""

import json
import os
import sqlite3
import sys
import tempfile
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.etl.loader import ContentLoader

MIGRATIONS_DIR = os.path.join(project_root, "data", "migrations")


def _apply_migrations(db_path: str) -> None:
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    for name in sorted(os.listdir(MIGRATIONS_DIR)):
        if name.endswith(".sql"):
            with open(os.path.join(MIGRATIONS_DIR, name)) as f:
                cursor.executescript(f.read())
    conn.commit()
    conn.close()


def _seed_doc(conn, doc_id: int) -> None:
    conn.execute(
        """
        INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                           published_at, text, raw_hash)
        VALUES (?, 'news', ?, 'x.test', 0, 'body', ?)
        """,
        (doc_id, f"https://x.test/{doc_id}", f"rh-{doc_id}"),
    )


class TestPromptVersionsUserTemplate(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        conn = sqlite3.connect(self.db_path)
        _seed_doc(conn, 1)
        _seed_doc(conn, 2)
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_user_prompt_template_persisted_alongside_system(self):
        loader = ContentLoader(self.db_path)
        loader.save_ai_output(
            doc_id=1,
            task="sentiment",
            result={"label": "POSITIVE"},
            confidence=0.8,
            prompt_version="sentiment-041-test",
            system_prompt="<system-prompt>",
            user_prompt_template="Analyze this: {text}",
            inference_method="llm",
        )
        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT system_prompt, user_prompt_template "
            "FROM prompt_versions WHERE prompt_version = 'sentiment-041-test'"
        ).fetchone()
        conn.close()
        self.assertEqual(row[0], "<system-prompt>")
        self.assertEqual(row[1], "Analyze this: {text}")

    def test_user_template_preserved_when_later_call_omits_it(self):
        loader = ContentLoader(self.db_path)
        # First call supplies both system + user template.
        loader.save_ai_output(
            doc_id=1,
            task="sentiment",
            result={"label": "POSITIVE"},
            confidence=0.8,
            prompt_version="sentiment-v-coalesce",
            system_prompt="<sys>",
            user_prompt_template="first-template",
            inference_method="llm",
        )
        # Second call to the same prompt_version omits the user template.
        # COALESCE keeps the previously-stored value.
        loader.save_ai_output(
            doc_id=2,
            task="sentiment",
            result={"label": "POSITIVE"},
            confidence=0.8,
            prompt_version="sentiment-v-coalesce",
            system_prompt="<sys>",
            inference_method="llm",
        )
        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT user_prompt_template FROM prompt_versions "
            "WHERE prompt_version = 'sentiment-v-coalesce'"
        ).fetchone()
        conn.close()
        self.assertEqual(row[0], "first-template")

    def test_no_prompt_version_means_no_row(self):
        loader = ContentLoader(self.db_path)
        loader.save_ai_output(
            doc_id=1,
            task="sentiment",
            result={"label": "POSITIVE"},
            confidence=0.8,
            # No prompt_version, no system_prompt — nothing goes to prompt_versions.
        )
        conn = sqlite3.connect(self.db_path)
        (count,) = conn.execute(
            "SELECT COUNT(*) FROM prompt_versions"
        ).fetchone()
        conn.close()
        self.assertEqual(count, 0)


if __name__ == "__main__":
    unittest.main()
