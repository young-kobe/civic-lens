"""
Constants for analysis/src/results/store.py. See
docs/DATABASE_SCHEMA.md's "Result-store write semantics" section for the
run-lifecycle rules these values encode.
"""

from __future__ import annotations

# analysis.runs.status values finish() accepts. 'pending'/'running' are not
# finish() states — the handle IS the pending/running state in memory until
# finish() commits it.
TERMINAL_STATUSES = ("done", "failed")

# inference_method values that require a prompt_version on a succeeded run.
PROMPT_REQUIRED_METHODS = ("llm", "hybrid")
