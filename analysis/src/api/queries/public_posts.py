"""
Public-column feed aggregation for the three lensed endpoints: GET
/public-posts (tone), /propaganda-public-posts, and /bot-public-posts.
All three share the same frame -- non-official Reddit/X posts,
engagement-ordered, SQL-paginated -- and differ only in which analysis
task qualifies a doc and what each item carries. See docs/audit-trail/
api/2026-07-30-public-posts-feed.md and
2026-07-30-propaganda-bot-public-feeds.md.
"""

from __future__ import annotations

from collections import defaultdict
from datetime import datetime
from typing import Optional

from analysis.src.api.models.bots import BotPublicPostsResponse
from analysis.src.api.models.propaganda import PropagandaExample, PropagandaPublicPostsResponse
from analysis.src.api.models.sentiment import PublicPostsResponse
from analysis.src.api.queries.base import (
    build_classification_sample,
    fetch_doc_targets,
    fetch_rich_sample_fields,
    resolve_time_range,
)
# Same-package reuse of the pages' own example builders, so a feed card is
# byte-identical to the same doc's card in that page's entity drill-down.
from analysis.src.api.queries.bots import _FLAGGED_EXAMPLE_TEXT_CHARS, _build_flagged_example
from analysis.src.api.queries.constants import BOT_FLAGGED_SHARE_EXCLUSION, SNIPPET_MAX_CHARS
from analysis.src.api.queries.propaganda import _fetch_example_techniques
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


# --------------------------------------------------------------------------- #
#  Propaganda lens -- docs with a current done 'propaganda' run.              #
# --------------------------------------------------------------------------- #

# Bot-excluded to match the page's own example pool (propaganda.py::
# _fetch_example_rows); every scored post qualifies, flagged or clean --
# a feed of flagged-only posts would hide the clean baseline.
_PROPAGANDA_FEED_FROM_SQL = f"""
    FROM analysis.runs r
    JOIN analysis.propaganda_results pr ON pr.run_id = r.run_id
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
    LEFT JOIN corpus.entities e_auth ON e_auth.entity_id = ap.entity_id
    LEFT JOIN corpus.authors a ON a.author_id = d.author_id
    LEFT JOIN corpus.x_posts xp ON xp.doc_id = d.doc_id
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
    WHERE r.task = 'propaganda'::analysis.task
      AND r.is_current AND r.status = 'done'::analysis.run_status
      AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
      AND d.source_type IN ('reddit_post', 'x_post')
      AND (e_auth.kind IS NULL OR e_auth.kind <> 'official')
"""

_PROPAGANDA_PAGE_SELECT = """
    SELECT d.doc_id, r.run_id, d.source_type::text AS source_type,
           d.domain_or_subreddit, d.title, d.body, d.source_url,
           COALESCE(pr.density, 0.0) AS density,
           a.handle AS author_handle,
           (COALESCE(xp.retweet_count, 0) + COALESCE(xp.reply_count, 0)
            + COALESCE(xp.like_count, 0) + COALESCE(xp.quote_count, 0)
            + COALESCE(rp.score, 0) + COALESCE(rp.num_comments, 0)) AS engagement
"""


def get_propaganda_public_posts(
    window: Optional[str] = None,
    date_from: Optional[datetime] = None,
    date_to: Optional[datetime] = None,
    page: int = 1,
) -> PropagandaPublicPostsResponse:
    """Engagement-ordered page of non-official Reddit/X posts scored for
    propaganda techniques. Items are PropagandaExample -- the same shape the
    page's entity drill-down uses -- so a clean post shows its true density
    and an empty technique list, never a fabricated flag. ``party`` is
    always None here: the feed excludes officials, the only entities that
    carry one."""
    start, end = resolve_time_range(window, date_from, date_to)
    page = max(1, page)
    params = {
        "start": start, "end": end, "bot_floor": BOT_FLAGGED_SHARE_EXCLUSION,
        "limit": PUBLIC_POSTS_PAGE_SIZE,
        "offset": (page - 1) * PUBLIC_POSTS_PAGE_SIZE,
    }

    with connection() as conn:
        total = conn.execute(_COUNT_SELECT + _PROPAGANDA_FEED_FROM_SQL, params).fetchone()["n"]
        rows = conn.execute(
            _PROPAGANDA_PAGE_SELECT + _PROPAGANDA_FEED_FROM_SQL + _PAGE_TAIL, params,
        ).fetchall()
        technique_rows = _fetch_example_techniques(conn, [row["run_id"] for row in rows])
        targets_by_doc = fetch_doc_targets(conn, [row["doc_id"] for row in rows])

    techniques_by_run = defaultdict(list)
    for row in technique_rows:
        techniques_by_run[row["run_id"]].append({
            "technique": row["technique"],
            "evidence_span": row["evidence_span"],
            "confidence": row["confidence"],
        })

    items = [
        PropagandaExample(
            doc_id=row["doc_id"],
            source_type=row["source_type"],
            domain=row["domain_or_subreddit"],
            title=row["title"],
            overall_score=row["density"],
            text_preview=(row["body"] or "")[:SNIPPET_MAX_CHARS],
            url=row["source_url"],
            techniques=techniques_by_run.get(row["run_id"], []),
            author_handle=row["author_handle"],
            party=None,
            targets=targets_by_doc.get(row["doc_id"]) or None,
        )
        for row in rows
    ]
    return PropagandaPublicPostsResponse(
        window=window, page=page, page_size=PUBLIC_POSTS_PAGE_SIZE, total=total, items=items,
    )


# --------------------------------------------------------------------------- #
#  Bot lens -- docs with a current done 'bot' run (bot_signals verdict).      #
# --------------------------------------------------------------------------- #

# No bot-author exclusion: this page measures automation, so excluding
# bot-heavy authors would delete its subject. Empty-body docs are excluded
# in SQL (not skipped post-page) so `total` and the page size stay honest.
_BOT_FEED_FROM_SQL = f"""
    FROM analysis.bot_signals bs
    JOIN analysis.runs r ON r.run_id = bs.run_id
        AND r.is_current AND r.status = 'done'::analysis.run_status
    JOIN corpus.documents d ON d.doc_id = bs.doc_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
    LEFT JOIN corpus.entities e_auth ON e_auth.entity_id = ap.entity_id
    LEFT JOIN corpus.authors a ON a.author_id = d.author_id
    LEFT JOIN corpus.x_posts xp ON xp.doc_id = d.doc_id
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
    WHERE r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND COALESCE(TRIM(d.body), '') <> ''
      AND d.source_type IN ('reddit_post', 'x_post')
      AND (e_auth.kind IS NULL OR e_auth.kind <> 'official')
"""

_BOT_PAGE_SELECT = """
    SELECT bs.doc_id, bs.label::text AS bot_label, r.confidence,
           d.source_type::text AS source_type, d.domain_or_subreddit,
           d.source_url, a.handle AS author_handle,
           LEFT(d.body, %(flagged_chars)s) AS flagged_text,
           r.raw_response -> 'llm' -> 'indicators' AS indicators_json,
           r.raw_response -> 'llm' ->> 'reasoning' AS reasoning,
           (COALESCE(xp.retweet_count, 0) + COALESCE(xp.reply_count, 0)
            + COALESCE(xp.like_count, 0) + COALESCE(xp.quote_count, 0)
            + COALESCE(rp.score, 0) + COALESCE(rp.num_comments, 0)) AS engagement
"""


def get_bot_public_posts(
    window: Optional[str] = None,
    date_from: Optional[datetime] = None,
    date_to: Optional[datetime] = None,
    page: int = 1,
) -> BotPublicPostsResponse:
    """Engagement-ordered page of non-official Reddit/X posts scored by the
    bot detector, every verdict included (bot/suspicious/human) -- the feed
    shows the clean baseline, not just flags. Items reuse the page's
    FlaggedExample builder with ``label`` populated."""
    start, end = resolve_time_range(window, date_from, date_to)
    min_conf = get_settings().aggregation_min_confidence
    page = max(1, page)
    params = {
        "start": start, "end": end, "min_conf": min_conf,
        "flagged_chars": _FLAGGED_EXAMPLE_TEXT_CHARS,
        "limit": PUBLIC_POSTS_PAGE_SIZE,
        "offset": (page - 1) * PUBLIC_POSTS_PAGE_SIZE,
    }

    with connection() as conn:
        total = conn.execute(_COUNT_SELECT + _BOT_FEED_FROM_SQL, params).fetchone()["n"]
        rows = conn.execute(_BOT_PAGE_SELECT + _BOT_FEED_FROM_SQL + _PAGE_TAIL, params).fetchall()

    items = []
    for row in rows:
        example = _build_flagged_example(row)
        if example is None:
            # Unreachable given the SQL empty-body exclusion; fail loud
            # rather than silently shrink the page if that ever drifts.
            raise ValueError(f"doc_id={row['doc_id']} paged into the bot feed with no body text")
        example.label = row["bot_label"]
        items.append(example)

    return BotPublicPostsResponse(
        window=window, page=page, page_size=PUBLIC_POSTS_PAGE_SIZE, total=total, items=items,
    )
