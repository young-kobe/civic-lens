"""
Base utilities for aggregators.

Shared database connection and utility functions used by all domain aggregators.
"""

import contextlib
import sqlite3
import time
from typing import Any, Dict, Optional, Set

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


# Canonical SQL fragment joining docs → x_posts_raw → x_users_raw so an
# aggregator row can pick up the X author (handle, name, verification,
# follower counts, etc.) via `u.*` / `x.*`. Ten aggregator query sites
# duplicated these two joins verbatim; centralizing one fragment keeps
# them drift-free and means a future schema change (e.g., a new x_authors
# view) touches exactly one constant.
#
# The joins assume the caller aliases `docs AS d`. Both joins are LEFT
# so non-x_post rows pass through with NULLs in the x.* / u.* columns —
# callers filter by `d.source_type = 'x_post'` when they only want X docs.
X_AUTHOR_JOIN_SQL = (
    "LEFT JOIN x_posts_raw x "
    "ON d.source_type = 'x_post' AND x.tweet_id = d.ident "
    "LEFT JOIN x_users_raw u ON u.user_id = x.author_id"
)

# Companion fragment layering the curated account classification onto the X
# author join (requires X_AUTHOR_JOIN_SQL's `x` alias). LEFT so unclassified
# authors pass through with NULL ap.* — absence from account_profiles means
# "general_public" by contract (migration 010).
ACCOUNT_PROFILE_JOIN_SQL = (
    "LEFT JOIN account_profiles ap "
    "ON ap.platform = 'x' AND ap.author_id = x.author_id"
)

# Reddit engagement join — picks up score / num_comments for reddit POSTS so
# samples can carry engagement counts (Phase 2b of the UI depth overhaul).
# LEFT join keyed on source_type so news/X rows pass through with NULLs.
# reddit_comment docs get no engagement: reddit_comments_raw was dropped in
# migration 005 (comment ingestion stores no raw rows), so there is nothing
# stored to surface — absent beats fabricated.
REDDIT_ENGAGEMENT_JOIN_SQL = (
    "LEFT JOIN reddit_posts_raw rp "
    "ON d.source_type = 'reddit_post' AND rp.fullname = d.ident"
)

# Projection fragment for the per-sample enrichment columns (Phase 2b/2c).
# Callers append this to their SELECT clause after the joins above; the
# resulting 11 row fields feed build_sample_engagement / build_sample_author.
SAMPLE_ENRICHMENT_SELECT = (
    "x.retweet_count, x.reply_count, x.like_count, x.quote_count, "
    "u.name, u.profile_image_url, u.verified_type, u.followers_count, "
    "u.created_at, "
    "rp.score, rp.num_comments"
)


def build_sample_engagement(
    source_type: Optional[str],
    retweets: Any, replies: Any, likes: Any, quotes: Any,
    reddit_score: Any, reddit_comments: Any,
) -> Optional[Dict[str, int]]:
    """Engagement counts for one sampled doc, or None when the source has
    none stored (news, or an X/Reddit row whose raw record is missing).

    Counts are as-at-collection-time — a reach proxy, not verified reach;
    the UI labels them accordingly. News docs always return None: we do
    not fabricate an engagement shape for sources that have no counts.
    """
    if source_type == "x_post":
        counts = (retweets, replies, likes, quotes)
        if all(c is None for c in counts):
            return None
        return {
            "retweets": int(retweets or 0),
            "replies": int(replies or 0),
            "likes": int(likes or 0),
            "quotes": int(quotes or 0),
        }
    if source_type == "reddit_post":
        if reddit_score is None and reddit_comments is None:
            return None
        out: Dict[str, int] = {"score": int(reddit_score or 0)}
        if reddit_comments is not None:
            out["num_comments"] = int(reddit_comments)
        return out
    # reddit_comment: no raw engagement stored (migration 005) — None.
    return None


def build_sample_author(
    source_type: Optional[str],
    handle: Optional[str],
    display_name: Optional[str],
    avatar_url: Optional[str],
    verified_type: Optional[str],
    followers_count: Any,
    created_at: Any,
) -> Optional[Dict[str, Any]]:
    """Author enrichment for an X sample, from x_users_raw. None for
    non-X docs (Reddit stores no author at ingest — never fabricate one)
    and for X rows whose author record is missing."""
    if source_type != "x_post" or not handle:
        return None
    return {
        "handle": handle,
        "display_name": display_name,
        "avatar_url": avatar_url,
        "verified_type": verified_type,
        "followers_count": int(followers_count) if followers_count is not None else None,
        "account_created_at": int(created_at) if created_at is not None else None,
    }

@contextlib.contextmanager
def get_connection(db_path: str):
    """Context manager for database connections."""
    conn = sqlite3.connect(db_path)
    conn.execute("PRAGMA foreign_keys = ON")  # match Go ingestor (audit D-5)
    try:
        yield conn
    finally:
        conn.close()


def get_aggregation_min_confidence() -> float:
    """Single source for the confidence floor every public-facing aggregate
    applies. Sentiment, narrative net-sentiment, and now movers all read the
    same setting so 'what counts as confident' can't drift between the
    Overall-Tone chart and the biggest-movers ticker on the same page
    (audit A-5; backend-aggregator-audit item 3)."""
    from analysis.src.common.settings import get_settings
    return get_settings().aggregation_min_confidence


def get_time_cutoff(window: str) -> Optional[int]:
    """Convert time window string to Unix timestamp cutoff."""
    seconds = TIME_WINDOWS.get(window)
    if seconds is None:
        return None
    return int(time.time()) - seconds


def get_previous_window_range(window: str) -> Optional[tuple]:
    """Return (start, end) unix timestamps for the window *before* the given
    one — same duration, ending at the current window's start. Returns None
    for ``all`` (no preceding window) or unknown keys.

    7d → (now - 14*86400, now - 7*86400)
    """
    seconds = TIME_WINDOWS.get(window)
    if seconds is None:
        return None
    now = int(time.time())
    return (now - 2 * seconds, now - seconds)


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
    sql = f"{select_clause} FROM ai_outputs_latest a JOIN docs d ON a.doc_id = d.doc_id {extra_joins} WHERE a.task_type = ?"
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


def get_high_bot_score_author_ids(cursor, min_score: float = 0.5) -> Set[str]:
    """X author_ids whose account-level bot rollup (`author_bot_scores.score`,
    mean of post-level scores) meets ``min_score``. Complements the per-doc
    bot exclusion: a doc can individually pass bot detection while its author's
    posting pattern as a whole scores bot-like. Returns empty when the rollup
    table hasn't been created (fresh/test DBs)."""
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='author_bot_scores'"
    )
    if not cursor.fetchone():
        return set()
    cursor.execute(
        "SELECT author_id FROM author_bot_scores WHERE platform = 'x' AND score >= ?",
        (min_score,),
    )
    return {row[0] for row in cursor.fetchall() if row[0]}


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
        # label='bot' only — 'suspicious' may be human. The canonical label
        # column (migration 023) replaced the old two-key JSON check
        # (label=='bot' OR is_bot==1); old rows were backfilled.
        cursor.execute("""
            SELECT a.doc_id
            FROM ai_outputs_latest a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type = 'bot_detection'
              AND a.label = 'bot'
              AND a.confidence >= ?
              AND d.source_type IN ('reddit_post', 'reddit_comment', 'x_post')
        """, (min_confidence,))

        bot_docs = {row[0] for row in cursor.fetchall()}

        logger.info(
            f"Found {len(bot_docs)} bot-flagged social media documents "
            f"(confidence >= {min_confidence}) to exclude"
        )
        return bot_docs
