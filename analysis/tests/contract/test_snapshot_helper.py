"""
Tests for analysis/tests/contract/conftest.py's assert_snapshot_match --
the write-when-absent, diff-when-present JSON snapshot helper. Pure file
I/O, no DB; uses a throwaway name and always cleans up the file it writes.
"""

from __future__ import annotations

import unittest

from analysis.tests.contract import conftest


class AssertSnapshotMatchTests(unittest.TestCase):
    SNAPSHOT_NAME = "_test_roundtrip_scratch"

    def setUp(self):
        self._path = conftest.SNAPSHOT_DIR / f"{self.SNAPSHOT_NAME}.json"
        self._path.unlink(missing_ok=True)

    def tearDown(self):
        self._path.unlink(missing_ok=True)

    def test_absent_snapshot_is_written_and_passes(self):
        self.assertFalse(self._path.exists())
        conftest.assert_snapshot_match(self.SNAPSHOT_NAME, {"a": 1, "b": [1, 2, 3]})
        self.assertTrue(self._path.exists())

    def test_matching_payload_passes_on_second_call(self):
        payload = {"a": 1, "b": [1, 2, 3]}
        conftest.assert_snapshot_match(self.SNAPSHOT_NAME, payload)
        # Second call against the now-recorded snapshot must not raise.
        conftest.assert_snapshot_match(self.SNAPSHOT_NAME, payload)

    def test_mismatched_payload_raises_with_diff(self):
        conftest.assert_snapshot_match(self.SNAPSHOT_NAME, {"a": 1})
        with self.assertRaises(AssertionError) as ctx:
            conftest.assert_snapshot_match(self.SNAPSHOT_NAME, {"a": 2})
        message = str(ctx.exception)
        self.assertIn(self.SNAPSHOT_NAME, message)
        self.assertIn("-  \"a\": 1", message)
        self.assertIn("+  \"a\": 2", message)


if __name__ == "__main__":
    unittest.main()
