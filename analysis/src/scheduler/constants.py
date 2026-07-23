"""
Constants for analysis/src/scheduler/stages.py and pipeline.py (Postgres
redesign Phase 7 — new-stack scheduler). See docs/todos/pg-redesign.md's
Phase 7 section for the design writeup.
"""

from __future__ import annotations

# ops.task_queue retry ceiling: a failed row is requeued to pending at its
# stage's start only while attempts < this; at the ceiling it stays
# 'failed' for a human to reset (ops.task_queue's documented contract —
# reprocess = UPDATE, never DELETE).
MAX_TASK_ATTEMPTS = 3

# ops.task_queue rows stuck 'in_progress' longer than this are reset to
# 'pending' once at pipeline start (crash-safety), matching
# etl/queue.py::reset_stale_in_progress's own default.
STALE_IN_PROGRESS_MINUTES = 30

# Stage names in pipeline order (pipeline.py's registry keys). Ordering
# constraints this order must preserve: narratives after claims; leans
# after text/targets AND after narratives; bot_rollup after bot. This
# vocabulary intentionally differs from the old job_runner.py's task names
# (account_tier replaces "accounts"; "snapshots" is dropped -- the new
# stack has no cache-snapshot stage yet; "leans" is new).
STAGE_ORDER = (
    "etl",
    "account_tier",
    "bot",
    "text",
    "targets",
    "propaganda",
    "citations",
    "claims",
    "bot_rollup",
    "narratives",
    "leans",
)
