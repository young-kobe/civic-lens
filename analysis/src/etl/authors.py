"""
analysis/src/etl/authors.py — upserts corpus.authors from raw capture tables.

Ordering contract: run sync_x_authors() (and sync_reddit_authors(), once
enabled) BEFORE documents.py's load_new_documents() — documents.py resolves
author_id via a read-only lookup and leaves it NULL when unmatched rather
than blocking; nothing re-links a doc retroactively once its author appears.

News outlets are NOT synthesized as authors here: they are corpus.entities
rows (kind='outlet') already, joined by domain in serving rollups, so news
documents get author_id = NULL by design. See
docs/audit-trail/analysis/2026-07-22-pg-etl-authors-documents-queue.md.
"""

from __future__ import annotations

from analysis.src.common import db
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)

# ON CONFLICT DO UPDATE refreshes every snapshot column — corpus.authors is
# a latest-snapshot table, not a history table, so a re-run overwrites stale
# followers_count/verified/etc. rather than preserving the old value.
_UPSERT_X_AUTHOR_SQL = """
    INSERT INTO corpus.authors (
        platform, platform_author_id, handle, display_name, description,
        location, profile_image_url, verified, verified_type,
        followers_count, following_count, account_created_at, last_synced_at
    ) VALUES (
        'x', %(user_id)s, %(username)s, %(name)s, %(description)s,
        %(location)s, %(profile_image_url)s, %(verified)s, %(verified_type)s,
        %(followers_count)s, %(following_count)s, %(created_at)s, now()
    )
    ON CONFLICT (platform, platform_author_id) DO UPDATE SET
        handle = EXCLUDED.handle,
        display_name = EXCLUDED.display_name,
        description = EXCLUDED.description,
        location = EXCLUDED.location,
        profile_image_url = EXCLUDED.profile_image_url,
        verified = EXCLUDED.verified,
        verified_type = EXCLUDED.verified_type,
        followers_count = EXCLUDED.followers_count,
        following_count = EXCLUDED.following_count,
        account_created_at = EXCLUDED.account_created_at,
        last_synced_at = EXCLUDED.last_synced_at
"""

_SELECT_X_USERS_SQL = """
    SELECT user_id, username, name, description, location, profile_image_url,
           verified, verified_type, followers_count, following_count,
           created_at
    FROM raw.x_users
"""


def sync_x_authors() -> int:
    """Upsert corpus.authors for every raw.x_users row.

    Reads the whole table (not an anti-join) since every X profile may have
    changed since the last sync, so each run is a full refresh.

    Returns the number of rows processed (inserted or refreshed).
    """
    with db.connection() as conn:
        with conn.cursor() as cur:
            cur.execute(_SELECT_X_USERS_SQL)
            rows = cur.fetchall()
            if rows:
                cur.executemany(_UPSERT_X_AUTHOR_SQL, rows)
    logger.info(f"authors: synced {len(rows)} X author(s) from raw.x_users")
    return len(rows)


def sync_reddit_authors() -> int:
    """No-op: raw.reddit_posts carries no author column yet. Always returns 0."""
    logger.info("authors: sync_reddit_authors is a documented no-op — raw.reddit_posts has no author column yet")
    return 0
