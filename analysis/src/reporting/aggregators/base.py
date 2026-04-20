"""
Base utilities for aggregators.

Shared database connection and utility functions used by all domain aggregators.
"""

import contextlib
import sqlite3
import json
import time
from typing import Optional, Set

from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


# Time window constants (in seconds)
TIME_WINDOWS = {
    "24h": 24 * 60 * 60,
    "7d": 7 * 24 * 60 * 60,
    "30d": 30 * 24 * 60 * 60,
    "90d": 90 * 24 * 60 * 60,
    "all": None,
}


@contextlib.contextmanager
def get_connection(db_path: str):
    """Context manager for database connections."""
    conn = sqlite3.connect(db_path)
    try:
        yield conn
    finally:
        conn.close()


def get_time_cutoff(window: str) -> Optional[int]:
    """Convert time window string to Unix timestamp cutoff."""
    seconds = TIME_WINDOWS.get(window)
    if seconds is None:
        return None
    return int(time.time()) - seconds


def fetch_task_rows(
    cursor,
    select_clause: str,
    task_type: str,
    cutoff: Optional[int],
    min_confidence: Optional[float] = None,
    extra_joins: str = "",
    extra_where: str = "",
    params_prefix: tuple = (),
) -> list:
    """Run a canonical ai_outputs+docs query and return rows.

    Consolidates the cutoff / min_confidence branching every aggregator was
    repeating inline. `select_clause` is the projection only (starts with
    ``SELECT ...``); the caller doesn't write the JOIN or WHERE chain.
    """
    sql = f"{select_clause} FROM ai_outputs a JOIN docs d ON a.doc_id = d.doc_id {extra_joins} WHERE a.task_type = ?"
    params: list = list(params_prefix) + [task_type]
    if min_confidence is not None:
        sql += " AND a.confidence >= ?"
        params.append(min_confidence)
    if cutoff is not None:
        sql += " AND d.published_at >= ?"
        params.append(cutoff)
    if extra_where:
        sql += f" AND ({extra_where})"
    cursor.execute(sql, tuple(params))
    return cursor.fetchall()


def get_bot_flagged_doc_ids(db_path: str, min_confidence: float = 0.5) -> Set[int]:
    """
    Get all doc_ids that have been flagged as 'bot' with confidence >= min_confidence.

    Only returns social media docs (reddit, x_post). News articles are
    assumed human-authored and are never excluded via bot filtering.

    These documents are excluded from sentiment, favorability, and cluster
    aggregations. Low-confidence bot flags do NOT cause exclusion — the
    audit's rationale is that a flaky bot flag shouldn't silently drop
    content from public-facing aggregates (walkthrough 039).
    """
    with get_connection(db_path) as conn:
        cursor = conn.cursor()

        # Check if ai_outputs table exists
        cursor.execute("""
            SELECT name FROM sqlite_master
            WHERE type='table' AND name='ai_outputs'
        """)
        if not cursor.fetchone():
            logger.warning("ai_outputs table does not exist - no bot filtering applied")
            return set()

        # Only flag social media docs as bots, never news articles.
        # Confidence filter avoids excluding content on a weak bot call.
        cursor.execute("""
            SELECT a.doc_id, a.output_json
            FROM ai_outputs a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type = 'bot_detection'
              AND a.confidence >= ?
              AND d.source_type IN ('reddit_post', 'reddit_comment', 'x_post')
        """, (min_confidence,))

        bot_docs = set()
        for doc_id, output_json in cursor.fetchall():
            try:
                data = json.loads(output_json)
                # Exclude if labeled as 'bot' (not 'suspicious' - those may be human)
                if data.get('label') == 'bot' or data.get('is_bot') is True:
                    bot_docs.add(doc_id)
            except json.JSONDecodeError:
                continue

        logger.info(
            f"Found {len(bot_docs)} bot-flagged social media documents "
            f"(confidence >= {min_confidence}) to exclude"
        )
        return bot_docs
