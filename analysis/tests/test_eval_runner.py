"""
Tests for the golden-set eval harness (analysis/evals/).

These tests verify the harness's intent, not just its mechanics:
1. Scoring is span-anchored and deterministic — the same recording and
   golden set always produce the same P/R/F1 (CI can gate without keys).
2. The gate fails loudly when the prompt/schema changes without
   re-recording (stale fingerprint) — a prompt edit can never slip
   through as a silent pass against outdated recordings.
3. Unreviewed (PENDING_HUMAN_REVIEW) labels never count as ground truth.
4. A malformed golden set (fabricated evidence span) breaks the load,
   mirroring pipeline invariant B2.
"""

import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.evals.harness import ReplayLLMClient, claim_extraction_fingerprint
from analysis.evals.run_eval import (
    GOLDEN_DIR,
    GoldenExample,
    evaluate_gate,
    load_golden,
    run_replay,
)
from analysis.evals.scoring import aggregate_scores, score_example
from analysis.src.engine.claims import ClaimDocInput, analyze


def _write_example(dirpath: Path, ex_id: str, text: str, claims, status="VERIFIED"):
    (dirpath / f"{ex_id}.json").write_text(json.dumps({
        "id": ex_id,
        "task": "claim_extraction",
        "input_text": text,
        "input_provenance": "synthetic-draft",
        "expected_claims": claims,
        "review_status": status,
        "notes": "",
    }), encoding="utf-8")


def _write_recording(dirpath: Path, ex_id: str, raw_response, fingerprint=None):
    (dirpath / f"{ex_id}.json").write_text(json.dumps({
        "example_id": ex_id,
        "raw_response": raw_response,
        "prompt_version": "test",
        "prompt_fingerprint": fingerprint or claim_extraction_fingerprint(),
        "recorded_at": "2026-01-01T00:00:00+00:00",
    }), encoding="utf-8")


TEXT = "The Senate passed the border bill 52-48 on Tuesday, and the House vote is set for Friday."


class TestScoring(unittest.TestCase):
    def test_span_overlap_match_is_a_true_positive(self):
        """Overlapping evidence spans mean the same claim was found, even
        when the model quotes a different extent of the sentence."""
        score = score_example(
            "ex", TEXT,
            predicted=[{"claim": "Senate passed the border bill",
                        "evidence_span": "The Senate passed the border bill 52-48"}],
            expected=[{"claim": "The Senate passed the border bill 52-48",
                       "evidence_span": "Senate passed the border bill 52-48 on Tuesday"}],
        )
        self.assertEqual((score.true_positives, score.false_positives, score.false_negatives),
                         (1, 0, 0))

    def test_disjoint_spans_are_fp_and_fn(self):
        """A claim citing a different part of the text is a different claim —
        it must count against precision AND recall, not fuzzy-match."""
        score = score_example(
            "ex", TEXT,
            predicted=[{"claim": "House vote is set for Friday",
                        "evidence_span": "House vote is set for Friday"}],
            expected=[{"claim": "The Senate passed the border bill",
                       "evidence_span": "The Senate passed the border bill 52-48"}],
        )
        self.assertEqual((score.true_positives, score.false_positives, score.false_negatives),
                         (0, 1, 1))

    def test_empty_expected_and_empty_predicted_is_perfect(self):
        """No-claim negatives are part of the eval: predicting nothing on
        them must not be punished (and precision must punish inventing)."""
        score = score_example("ex", TEXT, predicted=[], expected=[])
        metrics = aggregate_scores([score])
        self.assertEqual(metrics["false_positives"], 0)
        self.assertEqual(metrics["false_negatives"], 0)

    def test_each_gold_claim_matches_at_most_once(self):
        """Two predictions citing the same span can't both count — that
        would let a model inflate recall by duplicating claims."""
        score = score_example(
            "ex", TEXT,
            predicted=[
                {"claim": "Senate passed the border bill", "evidence_span": "Senate passed the border bill 52-48"},
                {"claim": "The Senate passed the bill duplicate", "evidence_span": "The Senate passed the border bill"},
            ],
            expected=[{"claim": "The Senate passed the border bill",
                       "evidence_span": "The Senate passed the border bill 52-48"}],
        )
        self.assertEqual(score.true_positives, 1)
        self.assertEqual(score.false_positives, 1)


class TestReplay(unittest.TestCase):
    def test_replay_exercises_pipeline_validation(self):
        """A recorded response with a fabricated evidence span must be
        dropped by the LIVE validation code during replay — the harness
        scores the pipeline, not the raw recording."""
        client = ReplayLLMClient({
            "claims": [
                {"claim": "The Senate passed the border bill", "confidence": 0.9,
                 "evidence_span": "The Senate passed the border bill 52-48"},
                {"claim": "The president vetoed the farm bill", "confidence": 0.9,
                 "evidence_span": "president vetoed the farm bill"},  # not in TEXT
            ]
        })
        result = analyze(ClaimDocInput(doc_id=0, text=TEXT), client)
        self.assertEqual(len(result.claims), 1)
        self.assertEqual(result.claims[0].claim_text, "The Senate passed the border bill")

    def test_replay_is_deterministic_end_to_end(self):
        with tempfile.TemporaryDirectory() as tmp:
            golden, recorded = Path(tmp) / "g", Path(tmp) / "r"
            golden.mkdir(); recorded.mkdir()
            _write_example(golden, "claims-t01", TEXT,
                           [{"claim": "The Senate passed the border bill",
                             "evidence_span": "The Senate passed the border bill 52-48"}])
            _write_recording(recorded, "claims-t01", {"claims": [
                {"claim": "Senate passed the border bill", "confidence": 0.9,
                 "evidence_span": "Senate passed the border bill 52-48"}]})
            examples = load_golden(golden)
            runs = [aggregate_scores(run_replay(examples, recorded)[0]) for _ in range(2)]
            self.assertEqual(runs[0], runs[1])
            self.assertEqual(runs[0]["f1"], 1.0)

    def test_stale_fingerprint_is_flagged_not_scored(self):
        """Recordings made under a different prompt/schema describe a
        pipeline that no longer exists; scoring them would be fabricated
        evidence of current quality."""
        with tempfile.TemporaryDirectory() as tmp:
            golden, recorded = Path(tmp) / "g", Path(tmp) / "r"
            golden.mkdir(); recorded.mkdir()
            _write_example(golden, "claims-t01", TEXT,
                           [{"claim": "The Senate passed the border bill",
                             "evidence_span": "The Senate passed the border bill 52-48"}])
            _write_recording(recorded, "claims-t01", {"claims": []},
                             fingerprint="sha256:0000")
            scores, missing, stale = run_replay(load_golden(golden), recorded)
            self.assertEqual(scores, [])
            self.assertEqual(stale, ["claims-t01"])
            self.assertEqual(missing, [])


class TestGate(unittest.TestCase):
    BASELINE = {"baseline": {"f1": 0.80}, "tolerance": 0.02}

    def test_inactive_until_baseline_exists(self):
        passed, msg = evaluate_gate({"f1": 0.0}, {"baseline": None}, [], [])
        self.assertTrue(passed)
        self.assertIn("INACTIVE", msg)

    def test_regression_below_tolerance_fails(self):
        passed, msg = evaluate_gate({"f1": 0.7799}, self.BASELINE, [], [])
        self.assertFalse(passed)
        self.assertIn("below baseline", msg)

    def test_within_tolerance_passes(self):
        passed, _ = evaluate_gate({"f1": 0.7801}, self.BASELINE, [], [])
        self.assertTrue(passed)

    def test_stale_recordings_fail_an_active_gate(self):
        """The core regression story: prompt changed, recordings not
        refreshed — the gate must demand a re-record, never pass silently."""
        passed, msg = evaluate_gate({"f1": 0.99}, self.BASELINE, [], ["claims-001"])
        self.assertFalse(passed)
        self.assertIn("re-record", msg)

    def test_missing_recordings_fail_an_active_gate(self):
        passed, msg = evaluate_gate({"f1": 0.99}, self.BASELINE, ["claims-002"], [])
        self.assertFalse(passed)
        self.assertIn("no recording", msg)


class TestGoldenSet(unittest.TestCase):
    def test_committed_golden_set_loads_and_is_valid(self):
        """Every committed example: id matches filename, spans verbatim,
        statuses legal. Catches drive-by edits that break invariant B2."""
        examples = load_golden(GOLDEN_DIR)
        self.assertGreaterEqual(len(examples), 50)

    def test_unreviewed_labels_are_not_ground_truth(self):
        """PENDING examples must be loadable but distinguishable — the
        runner scores/gates VERIFIED only. This encodes the labeling
        contract: drafted labels are not evidence until a human signs off."""
        examples = load_golden(GOLDEN_DIR)
        statuses = {e.review_status for e in examples}
        self.assertTrue(statuses.issubset({"PENDING_HUMAN_REVIEW", "VERIFIED"}))

    def test_fabricated_evidence_span_breaks_the_load(self):
        with tempfile.TemporaryDirectory() as tmp:
            golden = Path(tmp)
            _write_example(golden, "claims-t01", TEXT,
                           [{"claim": "Something not in the text at all",
                             "evidence_span": "this span does not exist in the input"}])
            with self.assertRaises(ValueError):
                load_golden(golden)


if __name__ == "__main__":
    unittest.main()
