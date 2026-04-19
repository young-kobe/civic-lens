"""
Tests for the human-review service that populates ai_output_evals.
"""

import json
import os
import sqlite3
import sys
import tempfile
import time
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting.review import ReviewService

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


class TestReviewService(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.NamedTemporaryFile(suffix=".db", delete=False)
        self.db_path = tmp.name
        tmp.close()
        _apply_migrations(self.db_path)

        now = int(time.time())
        conn = sqlite3.connect(self.db_path)
        conn.executemany(
            """
            INSERT INTO docs (doc_id, source_type, ident, domain_or_subreddit,
                              published_at, title, text, raw_hash, metadata_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (1, "news", "a", "a.com", now, "Title A", "Body of doc A about politics.", "h1", "{}"),
                (2, "news", "b", "b.com", now, "Title B", "Body of doc B.", "h2", "{}"),
                (3, "x_post", "c", "x.com", now, None, "x post text", "h3", "{}"),
            ],
        )
        # Three sentiment outputs at different confidences so we can verify
        # ordering (lowest first).
        conn.executemany(
            """
            INSERT INTO ai_outputs (output_id, doc_id, task_type, output_json, confidence,
                                    model_id, prompt_version, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                (10, 1, "sentiment", json.dumps({"label": "POSITIVE"}), 0.9, "m", "v1", now),
                (11, 2, "sentiment", json.dumps({"label": "NEGATIVE"}), 0.4, "m", "v1", now),
                (12, 3, "sentiment", json.dumps({"label": "NEUTRAL"}), 0.6, "m", "v1", now),
            ],
        )
        conn.commit()
        conn.close()
        self.svc = ReviewService(self.db_path)

    def tearDown(self):
        for suffix in ("", "-wal", "-shm"):
            try:
                os.remove(self.db_path + suffix)
            except (FileNotFoundError, PermissionError):
                pass

    def test_queue_orders_lowest_confidence_first(self):
        items = self.svc.get_queue("sentiment", limit=5)
        self.assertEqual([i["ai_output_id"] for i in items], [11, 12, 10])

    def test_queue_filters_by_source_type(self):
        items = self.svc.get_queue("sentiment", source_type="x_post")
        self.assertEqual([i["ai_output_id"] for i in items], [12])

    def test_queue_filters_by_confidence_max(self):
        items = self.svc.get_queue("sentiment", confidence_max=0.5)
        self.assertEqual([i["ai_output_id"] for i in items], [11])

    def test_queue_excludes_already_reviewed(self):
        self.svc.submit(
            ai_output_id=11, is_correct=1, human_label="NEGATIVE",
            human_confidence=0.95, is_golden=False, reviewer_id="kobe", notes=None,
        )
        items = self.svc.get_queue("sentiment", limit=5)
        self.assertEqual([i["ai_output_id"] for i in items], [12, 10])

    def test_submit_replaces_existing_review(self):
        self.svc.submit(11, 1, "NEGATIVE", 0.9, False, "kobe", None)
        # Same ai_output_id, this time mark incorrect with a different label.
        self.svc.submit(11, 0, "POSITIVE", 0.7, True, "kobe", "second look")
        conn = sqlite3.connect(self.db_path)
        rows = conn.execute(
            "SELECT is_correct, human_label, is_golden, notes FROM ai_output_evals WHERE ai_output_id = 11"
        ).fetchall()
        conn.close()
        self.assertEqual(len(rows), 1)
        is_correct, human_label, is_golden, notes = rows[0]
        self.assertEqual(is_correct, 0)
        self.assertEqual(human_label, "POSITIVE")
        self.assertEqual(is_golden, 1)
        self.assertEqual(notes, "second look")

    def test_stats_reports_accuracy_after_reviews(self):
        self.svc.submit(10, 1, "POSITIVE", 0.95, True, "kobe", None)  # correct, golden
        self.svc.submit(11, 0, "NEUTRAL", 0.8, True, "kobe", None)    # incorrect, golden
        self.svc.submit(12, 1, "NEUTRAL", 0.8, False, "kobe", None)   # correct
        stats = self.svc.get_stats(task_type="sentiment")
        per_task = stats["per_task"]
        self.assertEqual(len(per_task), 1)
        s = per_task[0]
        self.assertEqual(s["task_type"], "sentiment")
        self.assertEqual(s["total_outputs"], 3)
        self.assertEqual(s["reviewed"], 3)
        self.assertEqual(s["correct"], 2)
        self.assertEqual(s["incorrect"], 1)
        self.assertEqual(s["golden"], 2)
        self.assertAlmostEqual(s["accuracy_pct"], 66.7, places=1)

    def test_submit_unknown_output_raises(self):
        with self.assertRaises(ValueError):
            self.svc.submit(99999, 1, None, 0.9, False, "kobe", None)


if __name__ == "__main__":
    unittest.main()
