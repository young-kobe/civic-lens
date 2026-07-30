"""
GET /api/v1/public-posts aggregation -- the sentiment page's public
column: a corpus-wide paginated feed of non-official Reddit/X posts,
engagement-ordered, topic-filterable. See docs/audit-trail/api/
2026-07-30-public-posts-feed.md.
"""

from __future__ import annotations

from datetime import datetime
from typing import Optional

from analysis.src.api.models.sentiment import PublicPostsResponse
from analysis.src.api.queries.base import (
    build_classification_sample,
    fetch_rich_sample_fields,
    resolve_time_range,
)
from analysis.src.api.queries.constants import BOT_FLAGGED_SHARE_EXCLUSION
from analysis.src.common.db import connection
from analysis.src.common.settings import get_settings

PUBLIC_POSTS_PAGE_SIZE = 20

# The UI's "General" tab: docs whose current targets runs resolved no
# topic at all (mirrors routing.py's _topic_for_row default).
GENERAL_TOPIC = "General"

# NULL-safe bound predicate, same convention as queries/sentiment/sql.py.
_RANGE_PREDICATE = (
    "(%(start)s::timestamptz IS NULL OR d.published_at >= %(start)s) "
    "AND (%(end)s::timestamptz IS NULL OR d.published_at <= %(end)s)"
)

_BOT_EXCLUSION_SQL = """
    NOT EXISTS (
        SELECT 1 FROM analysis.author_bot_scores b
        WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
    )
"""

# One dominant topic per doc, mirroring sentiment/sql.py::_DOC_TOPICS_SQL
# semantics (current targets runs, non-'Other', confidence-floored) reduced
# to the highest-mention-count topic, alphabetical tiebreak. NULL when the
# doc has no qualifying mention -- the "General" bucket.
_DOMINANT_TOPIC_LATERAL = """
    LEFT JOIN LATERAL (
        SELECT m.topic
        FROM analysis.target_mentions m
        JOIN analysis.runs tr ON tr.run_id = m.run_id
        WHERE m.doc_id = d.doc_id
          AND tr.is_current AND tr.task = 'targets'
          AND m.topic IS NOT NULL AND m.topic <> 'Other'
          AND m.confidence >= %(min_conf)s
        GROUP BY m.topic
        ORDER BY COUNT(*) DESC, m.topic
        LIMIT 1
    ) dom ON TRUE
"""

# Everything the feed predicate needs: a current done sentiment run
# (guarantees build_classification_sample's label contract), the range and
# bot-exclusion gates shared with the sentiment panel, social-only source
# types (news is its own column), and officials excluded by the canonical
# kind='official' predicate (queries/profiles.py::is_official_kind --
# editorial is provenance, never routing), matching the ETL's
# official_record admission so no "Official record" doc can enter this feed.
_FEED_FROM_SQL = f"""
    FROM analysis.runs r
    JOIN analysis.sentiment_results sr ON sr.run_id = r.run_id
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
    LEFT JOIN corpus.entities e_auth ON e_auth.entity_id = ap.entity_id
    LEFT JOIN corpus.x_posts xp ON xp.doc_id = d.doc_id
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
    {_DOMINANT_TOPIC_LATERAL}
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done'
      AND r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
      AND d.source_type IN ('reddit_post', 'x_post')
      AND (e_auth.kind IS NULL OR e_auth.kind <> 'official')
"""

_TOPIC_MATCH_CLAUSE = " AND dom.topic = %(topic)s"
_TOPIC_GENERAL_CLAUSE = " AND dom.topic IS NULL"

_COUNT_SELECT = "SELECT COUNT(*) AS n"

# "Most relevant" = deterministic engagement-led ordering: raw platform
# counts summed as a reach proxy (X retweet/reply/like/quote, Reddit
# score/comments -- a cross-platform blend the UI labels as a proxy), with
# published_at + doc_id tiebreaks so the order is total and reproducible.
# Confidence is label certainty, not relevance -- display only.
_PAGE_SELECT = """
    SELECT d.doc_id, dom.topic AS topic,
           (COALESCE(xp.retweet_count, 0) + COALESCE(xp.reply_count, 0)
            + COALESCE(xp.like_count, 0) + COALESCE(xp.quote_count, 0)
            + COALESCE(rp.score, 0) + COALESCE(rp.num_comments, 0)) AS engagement
"""

_PAGE_TAIL = """
    ORDER BY engagement DESC, d.published_at DESC, d.doc_id DESC
    LIMIT %(limit)s OFFSET %(offset)s
"""


def _topic_clause(topic: Optional[str]) -> str:
    if topic is None:
        return ""
    if topic == GENERAL_TOPIC:
        return _TOPIC_GENERAL_CLAUSE
    return _TOPIC_MATCH_CLAUSE


def get_public_posts(
    window: Optional[str] = None,
    date_from: Optional[datetime] = None,
    date_to: Optional[datetime] = None,
    topic: Optional[str] = None,
    page: int = 1,
) -> PublicPostsResponse:
    """Engagement-ordered page of non-official Reddit/X posts, paginated in
    SQL (LIMIT/OFFSET -- this is corpus-wide, unlike /entity-posts'
    fetch-then-slice). ``topic`` filters on the doc's dominant resolved
    topic ('General' = no resolved topic); each returned item carries that
    same attribution, never a guess."""
    start, end = resolve_time_range(window, date_from, date_to)
    min_conf = get_settings().aggregation_min_confidence
    page = max(1, page)
    params = {
        "start": start, "end": end, "min_conf": min_conf,
        "bot_floor": BOT_FLAGGED_SHARE_EXCLUSION, "topic": topic,
        "limit": PUBLIC_POSTS_PAGE_SIZE,
        "offset": (page - 1) * PUBLIC_POSTS_PAGE_SIZE,
    }
    base = _FEED_FROM_SQL + _topic_clause(topic)

    with connection() as conn:
        total = conn.execute(_COUNT_SELECT + base, params).fetchone()["n"]
        page_rows = conn.execute(_PAGE_SELECT + base + _PAGE_TAIL, params).fetchall()
        rich = fetch_rich_sample_fields(conn, [row["doc_id"] for row in page_rows])

    items = []
    for row in page_rows:
        sample = build_classification_sample(row["doc_id"], rich[row["doc_id"]])
        sample.topic = row["topic"] if row["topic"] is not None else GENERAL_TOPIC
        items.append(sample)

    return PublicPostsResponse(
        window=window, topic=topic, page=page,
        page_size=PUBLIC_POSTS_PAGE_SIZE, total=total, items=items,
    )
