"""
analysis/src/etl/queue.py — seeds ops.task_queue and resets stale in_progress rows.

Run seed_pending_tasks() after documents.py's load_new_documents() in a
pipeline invocation — it seeds off corpus.documents rows that must already
exist. Task-applicability derivation: docs/audit-trail/analysis/2026-07-22-pg-etl-authors-documents-queue.md
"""

from __future__ import annotations

import datetime

from analysis.src.common import db
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)

_SEED_SQL = """
    INSERT INTO ops.task_queue (doc_id, task, status)
    SELECT d.doc_id, t.task, 'pending'
    FROM corpus.documents d
    CROSS JOIN unnest(enum_range(NULL::analysis.task)) AS t(task)
    WHERE t.task <> 'account_tier'  -- author-scoped, not doc-scoped
      AND NOT (t.task = 'bot' AND d.source_type = 'news')  -- bot detection is social-only
    ON CONFLICT (doc_id, task) DO NOTHING
"""


def seed_pending_tasks() -> int:
    """Insert one 'pending' ops.task_queue row per (doc, applicable task) for
    every corpus.documents row lacking one yet. Idempotent: re-seeding never
    resets a done/failed/in_progress row back to pending.

    Returns the number of rows actually inserted.
    """
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_SEED_SQL)
            inserted = cur.rowcount
    logger.info(f"queue: seeded {inserted} pending task_queue row(s)")
    return inserted


def reset_stale_in_progress(older_than_minutes: int = 30) -> int:
    """Reset ops.task_queue rows stuck 'in_progress' for longer than
    `older_than_minutes` back to 'pending', preserving `attempts`.

    Returns the number of rows reset.
    """
    cutoff = datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(minutes=older_than_minutes)
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(
                """
                UPDATE ops.task_queue
                SET status = 'pending', claimed_at = NULL, updated_at = now()
                WHERE status = 'in_progress' AND claimed_at < %s
                """,
                (cutoff,),
            )
            reset_count = cur.rowcount
    logger.info(f"queue: reset {reset_count} stale in_progress row(s) to pending")
    return reset_count
