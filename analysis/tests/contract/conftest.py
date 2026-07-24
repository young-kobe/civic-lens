"""
Shared harness for Phase 9 API contract tests: re-exports
analysis/tests/pg_fixture.py for schema setup (same gated-on-
CIVIC_TEST_DATABASE_URL convention as every other integration TestCase in
this repo) plus assert_snapshot_match, the write-when-absent /
diff-when-present JSON snapshot helper. Named conftest.py per the Phase 9
task; the repo's test runner is plain unittest (no pytest dependency), so
this is an ordinary importable module -- import its names directly rather
than relying on pytest auto-discovery.
"""

from __future__ import annotations

import json
from difflib import unified_diff
from pathlib import Path
from typing import Any

from analysis.tests import pg_fixture  # noqa: F401 -- re-exported for contract tests

SNAPSHOT_DIR = Path(__file__).resolve().parent / "snapshots"


def _serialize(payload: Any) -> str:
    return json.dumps(payload, indent=2, sort_keys=True, default=str) + "\n"


def assert_snapshot_match(name: str, payload: Any) -> None:
    """
    Round-trip a JSON-serializable payload (e.g. a FastAPI response's
    `.json()`) against `snapshots/<name>.json`.

    Absent snapshot: writes the recorded baseline and returns (first run
    records, it does not fail). Present snapshot: asserts the payload
    serializes byte-identical to what's recorded; on mismatch raises
    AssertionError with a unified diff, so a contract change is visible at
    a glance instead of an opaque "not equal".
    """
    SNAPSHOT_DIR.mkdir(parents=True, exist_ok=True)
    path = SNAPSHOT_DIR / f"{name}.json"
    serialized = _serialize(payload)
    if not path.exists():
        path.write_text(serialized)
        return
    recorded = path.read_text()
    if serialized != recorded:
        diff = "\n".join(
            unified_diff(
                recorded.splitlines(),
                serialized.splitlines(),
                fromfile=f"snapshots/{name}.json (recorded)",
                tofile=f"snapshots/{name}.json (actual)",
                lineterm="",
            )
        )
        raise AssertionError(f"Snapshot mismatch for {name!r}:\n{diff}")
