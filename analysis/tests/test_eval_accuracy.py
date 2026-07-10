"""
Public human-agreement overlay tests.

The business rule under test: human review results (ai_output_evals) must
feed back into the public UI as a per-task agreement figure — but only once
enough reviews exist to mean something. A task with 3 reviews must never
render "100% accurate"; it reports its honest counts with the accuracy
withheld, mirroring the received-tone small-n suppression.
"""

import sqlite3
import sys
import unittest
import uuid
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.reporting.review import ReviewService


class EvalAccuracyTests(unittest.TestCase):
    def setUp(self):
        self.db_path = f"test_eval_accuracy_{uuid.uuid4()}.db"
        conn = sqlite3.connect(self.db_path)
        cur = conn.cursor()
        cur.executescript("""
            CREATE TABLE ai_outputs (
                output_id INTEGER PRIMARY KEY,
                doc_id INTEGER,
                task_type TEXT,
                output_json TEXT,
                confidence REAL,
                label TEXT
            );
            CREATE VIEW ai_outputs_latest AS
            SELECT a.* FROM ai_outputs a
            WHERE a.output_id = (
                SELECT MAX(a2.output_id) FROM ai_outputs a2
                WHERE a2.doc_id = a.doc_id AND a2.task_type = a.task_type
            );

            CREATE TABLE ai_output_evals (
                eval_id INTEGER PRIMARY KEY AUTOINCREMENT,
                ai_output_id INTEGER NOT NULL UNIQUE,
                doc_id INTEGER NOT NULL,
                task_type TEXT NOT NULL,
                human_label TEXT,
                is_correct INTEGER,
                is_golden INTEGER NOT NULL DEFAULT 0,
                reviewed_at INTEGER NOT NULL
            );
        """)
        floor = ReviewService.MIN_PUBLIC_REVIEW_N
        # sentiment: floor+4 scored reviews (3/4 of them correct beyond an
        # even split — concretely: floor scored as alternating, plus 4
        # correct), enough to publish a percentage.
        rows = []
        eval_id = 0
        for i in range(floor + 4):
            eval_id += 1
            rows.append((eval_id, eval_id, "sentiment", 1 if (i % 2 == 0 or i >= floor) else 0))
        # propaganda: only 3 scored reviews, all correct — must be suppressed.
        for i in range(3):
            eval_id += 1
            rows.append((eval_id, eval_id, "propaganda", 1))
        cur.executemany(
            "INSERT INTO ai_outputs (output_id, doc_id, task_type, output_json, confidence) "
            "VALUES (?, ?, ?, '{}', 0.9)",
            [(r[0], r[1], r[2]) for r in rows],
        )
        cur.executemany(
            "INSERT INTO ai_output_evals "
            "(ai_output_id, doc_id, task_type, is_correct, is_golden, reviewed_at) "
            "VALUES (?, ?, ?, ?, 0, 1735000000)",
            [(r[0], r[1], r[2], r[3]) for r in rows],
        )
        conn.commit()
        conn.close()
        self.result = ReviewService(self.db_path).get_public_accuracy()
        self.by_task = {t["taskType"]: t for t in self.result["perTask"]}

    def tearDown(self):
        Path(self.db_path).unlink(missing_ok=True)

    def test_reviewed_task_publishes_accuracy(self):
        sent = self.by_task["sentiment"]
        floor = ReviewService.MIN_PUBLIC_REVIEW_N
        self.assertEqual(sent["scored"], floor + 4)
        self.assertFalse(sent["lowSample"])
        self.assertIsNotNone(sent["accuracyPct"])
        correct = floor // 2 + 4
        expected = round(correct / (floor + 4) * 100, 1)
        self.assertAlmostEqual(sent["accuracyPct"], expected, places=1)

    def test_thin_task_reports_counts_but_withholds_accuracy(self):
        """3 all-correct reviews must not render as 100% — the exact
        failure mode the floor exists to prevent."""
        prop = self.by_task["propaganda"]
        self.assertEqual(prop["scored"], 3)
        self.assertTrue(prop["lowSample"])
        self.assertIsNone(prop["accuracyPct"])

    def test_floor_is_published_for_ui_labeling(self):
        self.assertEqual(self.result["minReviewN"], ReviewService.MIN_PUBLIC_REVIEW_N)


if __name__ == "__main__":
    unittest.main()
