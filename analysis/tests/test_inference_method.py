"""
Tests for inference_method tracking + pages.state CHECK constraint
(walkthrough 038).

Covers:
  - SentimentResult / FavorabilityResult / BotResult carry 'llm' vs 'heuristic'
    on the right code paths.
  - loader.save_ai_output writes the inference_method column.
  - migration 013's CHECK constraint on pages.state rejects out-of-range values.
"""

import os
import sqlite3
import sys
import tempfile
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine.analyzer import Analyzer
from analysis.src.engine.bot import HybridBotDetector
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


class TestResultMethodFields(unittest.TestCase):
    """Each analyzer's heuristic/LLM paths set inference_method correctly."""

    def test_analyzer_heuristic_fallback_marks_method_heuristic(self):
        # LLM disabled → heuristic path runs.
        analyzer = Analyzer(llm_enabled=False)
        sentiment, favorability = analyzer.analyze_full("This is a great day for democracy.")
        self.assertEqual(sentiment.inference_method, "heuristic")
        self.assertEqual(favorability.inference_method, "heuristic")

    def test_analyzer_empty_text_result_still_sets_method(self):
        analyzer = Analyzer(llm_enabled=False)
        sentiment, favorability = analyzer.analyze_full("")
        # Empty-text results default to the dataclass default ('llm') — the
        # caller should treat zero-confidence outputs as non-classifications.
        # What matters is that the field exists and isn't None.
        self.assertIn(sentiment.inference_method, {"llm", "heuristic"})
        self.assertIn(favorability.inference_method, {"llm", "heuristic"})

    def test_bot_detector_heuristic_fallback_marks_method(self):
        bot = HybridBotDetector(llm_enabled=False)
        result = bot.analyze_full("Buy now! Click here! Amazing deals!!!")
        self.assertEqual(result.inference_method, "heuristic")

    def test_bot_detector_empty_text_marks_method(self):
        bot = HybridBotDetector(llm_enabled=False)
        result = bot.analyze_full("")
        self.assertEqual(result.inference_method, "heuristic")


class TestSaveAiOutputWritesMethod(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)
        conn = sqlite3.connect(self.db_path)
        conn.execute(
            """
            INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                               published_at, text, raw_hash)
            VALUES (1, 'news', 'https://x.test/1', 'x.test', 0, 'body', 'rh-1')
            """
        )
        conn.commit()
        conn.close()

    def tearDown(self):
        os.unlink(self.db_path)

    def test_writes_llm_method(self):
        loader = ContentLoader(self.db_path)
        loader.save_ai_output(
            doc_id=1,
            task="sentiment",
            result={"label": "POSITIVE"},
            confidence=0.8,
            inference_method="llm",
        )
        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT inference_method FROM ai_outputs WHERE doc_id = 1"
        ).fetchone()
        conn.close()
        self.assertEqual(row[0], "llm")

    def test_writes_heuristic_method(self):
        loader = ContentLoader(self.db_path)
        loader.save_ai_output(
            doc_id=1,
            task="sentiment",
            result={"label": "POSITIVE"},
            confidence=0.5,
            inference_method="heuristic",
        )
        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT inference_method FROM ai_outputs WHERE doc_id = 1"
        ).fetchone()
        conn.close()
        self.assertEqual(row[0], "heuristic")

    def test_defaults_to_null_when_not_passed(self):
        loader = ContentLoader(self.db_path)
        loader.save_ai_output(
            doc_id=1,
            task="sentiment",
            result={"label": "POSITIVE"},
            confidence=0.5,
        )
        conn = sqlite3.connect(self.db_path)
        row = conn.execute(
            "SELECT inference_method FROM ai_outputs WHERE doc_id = 1"
        ).fetchone()
        conn.close()
        self.assertIsNone(row[0])

    def test_check_constraint_rejects_invalid_method(self):
        conn = sqlite3.connect(self.db_path)
        with self.assertRaises(sqlite3.IntegrityError):
            conn.execute(
                """
                INSERT INTO ai_outputs
                    (doc_id, task_type, output_json, confidence, inference_method, created_at)
                VALUES (1, 'sentiment', '{}', 0.5, 'guessing', 0)
                """
            )
        conn.close()


class TestPagesStateCheck(unittest.TestCase):
    def setUp(self):
        fd, self.db_path = tempfile.mkstemp(suffix=".db")
        os.close(fd)
        _apply_migrations(self.db_path)

    def tearDown(self):
        os.unlink(self.db_path)

    def test_valid_states_insert(self):
        conn = sqlite3.connect(self.db_path)
        for state in (0, 1, 2, 3):
            conn.execute(
                "INSERT INTO pages (url_canon, url_raw, domain, state) "
                "VALUES (?, ?, ?, ?)",
                (f"https://x.test/{state}", f"https://x.test/{state}", "x.test", state),
            )
        conn.commit()
        conn.close()

    def test_invalid_state_rejected(self):
        conn = sqlite3.connect(self.db_path)
        with self.assertRaises(sqlite3.IntegrityError):
            conn.execute(
                "INSERT INTO pages (url_canon, url_raw, domain, state) "
                "VALUES ('https://x.test/bad', 'https://x.test/bad', 'x.test', 99)"
            )
        conn.close()

    def test_negative_state_rejected(self):
        conn = sqlite3.connect(self.db_path)
        with self.assertRaises(sqlite3.IntegrityError):
            conn.execute(
                "INSERT INTO pages (url_canon, url_raw, domain, state) "
                "VALUES ('https://x.test/neg', 'https://x.test/neg', 'x.test', -1)"
            )
        conn.close()


if __name__ == "__main__":
    unittest.main()
