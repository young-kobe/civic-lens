"""Durable error recording into ops.error_log (migration 0009). Contract:
`record_error` never raises -- on any failure (pool unopened, DB down, rate
cap hit) it falls back to stdout logging and returns. Retention and the
storm guard are documented on the table itself."""

import threading
import time
import traceback as traceback_module
from typing import Any, Dict, Optional

from psycopg.types.json import Jsonb

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)

# Past this many durable writes per rolling hour the process falls back to
# stdout-only, after one final marker row -- an error storm must not bloat
# the small prod box's disk. Counter is process-wide on purpose: the cap
# bounds total write volume, not per-component fairness.
RATE_CAP_PER_HOUR = 200
_RATE_WINDOW_SECONDS = 3600.0

_rate_lock = threading.Lock()
_window_start = 0.0
_window_writes = 0
_cap_row_written = False

_INSERT_SQL = """
    INSERT INTO ops.error_log
        (source, component, message, traceback, doc_id, task, pipeline_run_id, context)
    VALUES
        (%(source)s, %(component)s, %(message)s, %(traceback)s, %(doc_id)s,
         %(task)s, %(pipeline_run_id)s, %(context)s)
"""

PRUNE_SQL = "DELETE FROM ops.error_log WHERE occurred_at < now() - interval '30 days'"


def _reserve_write() -> Optional[str]:
    """Take one slot in the rolling-hour write budget. Returns None when the
    write may proceed, 'cap' when this call should write the one marker row,
    and 'skip' when the cap row is already down and the write must not happen."""
    global _window_start, _window_writes, _cap_row_written
    with _rate_lock:
        now = time.monotonic()
        if now - _window_start >= _RATE_WINDOW_SECONDS:
            _window_start = now
            _window_writes = 0
            _cap_row_written = False
        if _window_writes < RATE_CAP_PER_HOUR:
            _window_writes += 1
            return None
        if not _cap_row_written:
            _cap_row_written = True
            return "cap"
        return "skip"


def record_error(
    exc: Optional[BaseException],
    *,
    component: str,
    source: str = "analysis",
    message: Optional[str] = None,
    doc_id: Optional[int] = None,
    task: Optional[str] = None,
    pipeline_run_id: Optional[int] = None,
    context: Optional[Dict[str, Any]] = None,
) -> None:
    """Write one ops.error_log row. Thread-safe (pooled connection per call,
    the same pool the scheduler's worker threads already share)."""
    text = message or (str(exc) if exc is not None else "")
    tb = "".join(traceback_module.format_exception(exc)) if exc is not None else None
    params = {
        "source": source,
        "component": component,
        "message": text,
        "traceback": tb,
        "doc_id": doc_id,
        "task": task,
        "pipeline_run_id": pipeline_run_id,
        "context": Jsonb(context) if context is not None else None,
    }
    try:
        verdict = _reserve_write()
        if verdict == "skip":
            logger.error(f"[{source}/{component}] {text} (error_log rate cap active, not persisted)")
            return
        if verdict == "cap":
            params = {
                "source": source, "component": "common.error_log",
                "message": f"error_log rate cap reached ({RATE_CAP_PER_HOUR}/hour); "
                           "further errors this window go to stdout only",
                "traceback": None, "doc_id": None, "task": None,
                "pipeline_run_id": None, "context": None,
            }
            logger.error(f"[{source}/{component}] {text} (error_log rate cap reached, not persisted)")
        # Imported here, not at module top: common.db pulls in settings and
        # raises without CIVIC_DATABASE_URL -- this module must stay
        # importable (and record_error callable) in that state.
        from analysis.src.common import db
        with db.connection() as conn:
            with conn.cursor() as cur:
                cur.execute(_INSERT_SQL, params)
    except Exception:
        # The fallback IS the contract: an error writer that raises turns
        # one failure into two. Message plus traceback still reach stdout.
        logger.error(f"[{source}/{component}] {text}" + (f"\n{tb}" if tb else ""))


def prune(conn: Any) -> int:
    """Delete rows older than the 30-day retention window; returns the count.
    Caller owns the connection and any failure handling (the pipeline wraps
    this in its own try/except so pruning can never block a run)."""
    with conn.cursor() as cur:
        cur.execute(PRUNE_SQL)
        return cur.rowcount
