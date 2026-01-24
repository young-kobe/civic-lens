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


def get_bot_flagged_doc_ids(db_path: str) -> Set[int]:
    """
    Get all doc_ids that have been flagged as 'bot'.
    
    These documents are excluded from sentiment, favorability, and cluster aggregations.
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
        
        cursor.execute("""
            SELECT doc_id, output_json
            FROM ai_outputs
            WHERE task_type = 'bot_detection'
        """)
        
        bot_docs = set()
        for doc_id, output_json in cursor.fetchall():
            try:
                data = json.loads(output_json)
                # Exclude if labeled as 'bot' (not 'suspicious' - those may be human)
                if data.get('label') == 'bot' or data.get('is_bot') is True:
                    bot_docs.add(doc_id)
            except json.JSONDecodeError:
                continue
        
        logger.info(f"Found {len(bot_docs)} bot-flagged documents to exclude")
        return bot_docs
