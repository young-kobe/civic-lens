"""Contract tests for AnalysisJobRunner._map_llm_concurrent.

WHY: the LLM stages parallelize network-bound calls but MUST write results in
the original doc order with per-item failures isolated (a raised call becomes an
``error`` tuple, never aborts the batch). These are the invariants the four
stage loops depend on; a regression here would silently drop or misalign
analysis outputs, so we assert them directly rather than through a full stage.
"""
import os
import sys
import threading
import time
import types
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

from analysis.src.scheduler.job_runner import AnalysisJobRunner


def _make_self(concurrency):
    """Minimal stand-in exposing only what the helper reads (self.settings)."""
    stub = types.SimpleNamespace()
    stub.settings = types.SimpleNamespace(llm_concurrency=concurrency)
    return stub


class TestMapLLMConcurrent(unittest.TestCase):
    def test_preserves_order_despite_out_of_order_completion(self):
        # Later items finish FIRST (sleep shrinks with index), so completion
        # order is the reverse of submission — results must still align to
        # input order, or downstream writes attach to the wrong doc.
        items = list(range(8))

        def call(x):
            time.sleep((len(items) - x) * 0.01)
            return x * 10

        out = AnalysisJobRunner._map_llm_concurrent(_make_self(4), items, call)
        self.assertEqual([item for item, _, _ in out], items)
        self.assertEqual([res for _, res, _ in out], [x * 10 for x in items])
        self.assertTrue(all(err is None for _, _, err in out))

    def test_captures_per_item_errors_without_aborting(self):
        items = [1, 2, 3, 4]

        def call(x):
            if x == 3:
                raise ValueError("boom")
            return x

        out = AnalysisJobRunner._map_llm_concurrent(_make_self(4), items, call)
        by_item = {item: (res, err) for item, res, err in out}
        self.assertEqual(by_item[2], (2, None))
        # The failing item yields result None + the captured exception; the
        # rest still complete.
        self.assertIsNone(by_item[3][0])
        self.assertIsInstance(by_item[3][1], ValueError)
        self.assertEqual(by_item[4], (4, None))

    def test_actually_runs_concurrently(self):
        # With concurrency=4, four 50ms calls should overlap and finish well
        # under the 200ms they'd take serially.
        items = list(range(4))
        max_seen = {"n": 0}
        active = {"n": 0}
        lock = threading.Lock()

        def call(x):
            with lock:
                active["n"] += 1
                max_seen["n"] = max(max_seen["n"], active["n"])
            time.sleep(0.05)
            with lock:
                active["n"] -= 1
            return x

        AnalysisJobRunner._map_llm_concurrent(_make_self(4), items, call)
        self.assertGreater(max_seen["n"], 1)

    def test_serial_path_when_concurrency_one(self):
        items = [1, 2, 3]
        max_seen = {"n": 0}
        active = {"n": 0}

        def call(x):
            active["n"] += 1
            max_seen["n"] = max(max_seen["n"], active["n"])
            time.sleep(0.01)
            active["n"] -= 1
            return x

        out = AnalysisJobRunner._map_llm_concurrent(_make_self(1), items, call)
        self.assertEqual([item for item, _, _ in out], items)
        self.assertEqual(max_seen["n"], 1)  # never more than one in flight


if __name__ == "__main__":
    unittest.main()
