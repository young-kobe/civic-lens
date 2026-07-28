"""
GET /api/v1/entity-posts and GET /api/v1/entity-profile/{entity_id}
aggregation -- entity drill-down reads against corpus.entities joined to
corpus.documents (via author_profiles/news_articles/reddit_posts) and
analysis.target_mentions/propaganda_results. See docs/audit-trail/api/
2026-07-24-phase9-sentiment-entities.md.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Optional

from analysis.src.api.models.common import LeanLabel, RangeMeta
from analysis.src.api.models.entities import (
    AdmissionSplit,
    EntityPostRow,
    EntityPostsResponse,
    EntityProfileResponse,
    MonthlyActivity,
    StanceDistribution,
)
from analysis.src.api.queries.base import (
    build_sample_doc,
    resolve_time_range,
    split_admission_counts,
)
from analysis.src.api.queries.constants import (
    BOT_FLAGGED_SHARE_EXCLUSION,
    MIN_TARGET_SAMPLE_N,
    SNIPPET_MAX_CHARS,
)
from analysis.src.api.queries.profiles import fetch_entity_profiles
from analysis.src.common.db import connection
from analysis.src.common.settings import get_settings

# --------------------------------------------------------------------------- #
#  Module constants (entity-panel-local)                                     #
# --------------------------------------------------------------------------- #

ENTITY_POSTS_PAGE_SIZE = 20

# NULL-safe bound predicate, same convention as queries/sentiment/sql.py --
# either side of (start, end) may be None (unbounded).
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

# The "candidates" CTE covers every way a doc relates to an entity: a
# target_mentions row naming it (`mentions`), or the doc being authored by
# it -- an official/collective's own X post (author_profiles.entity_id), a
# news outlet's own article (news_articles.outlet_entity_id), or a
# subreddit's own post (reddit_posts.subreddit_entity_id) (`authored_by`).
_ENTITY_POSTS_SQL = f"""
    WITH candidates AS (
        SELECT m.doc_id AS doc_id, 'mentions' AS relation, m.confidence AS confidence
        FROM analysis.target_mentions m
        JOIN analysis.runs r ON r.run_id = m.run_id
        WHERE m.entity_id = %(entity_id)s AND r.is_current

        UNION ALL

        SELECT d.doc_id, 'authored_by', tr.confidence
        FROM corpus.documents d
        JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
        LEFT JOIN analysis.runs tr
            ON tr.doc_id = d.doc_id AND tr.task = 'text' AND tr.is_current AND tr.status = 'done'
        WHERE ap.entity_id = %(entity_id)s

        UNION ALL

        SELECT na.doc_id, 'authored_by', tr.confidence
        FROM corpus.news_articles na
        LEFT JOIN analysis.runs tr
            ON tr.doc_id = na.doc_id AND tr.task = 'text' AND tr.is_current AND tr.status = 'done'
        WHERE na.outlet_entity_id = %(entity_id)s

        UNION ALL

        SELECT rp.doc_id, 'authored_by', tr.confidence
        FROM corpus.reddit_posts rp
        LEFT JOIN analysis.runs tr
            ON tr.doc_id = rp.doc_id AND tr.task = 'text' AND tr.is_current AND tr.status = 'done'
        WHERE rp.subreddit_entity_id = %(entity_id)s
    ),
    resolved AS (
        SELECT doc_id,
               CASE WHEN COUNT(DISTINCT relation) > 1 THEN 'both' ELSE MIN(relation) END AS relation,
               MAX(confidence) AS confidence
        FROM candidates
        GROUP BY doc_id
    )
    SELECT resolved.doc_id, resolved.relation, resolved.confidence,
           d.source_url, d.published_at, d.admission_class, d.title
    FROM resolved
    JOIN corpus.documents d ON d.doc_id = resolved.doc_id
    WHERE resolved.confidence IS NOT NULL AND resolved.confidence >= %(min_conf)s
      AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
    ORDER BY d.published_at DESC, resolved.doc_id DESC
"""

_ENTITY_ROW_SQL = (
    "SELECT entity_id, entity_key, display_name, kind, lean "
    "FROM corpus.entities WHERE entity_id = %(entity_id)s"
)

# The entity's own doc history (officials/collectives via author_profiles,
# outlets via news_articles, subreddits via reddit_posts) -- ALL-TIME, no
# window cutoff, so an inactive official's backfilled history never
# vanishes. Carries the current propaganda run's techniques_validated (NULL
# when no propaganda run has completed for that doc) for the profile's
# propaganda rate.
_AUTHORED_DOCS_SQL = """
    SELECT d.doc_id, d.published_at, d.admission_class, pr.techniques_validated
    FROM corpus.documents d
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id AND ap.entity_id = %(entity_id)s
    LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id AND na.outlet_entity_id = %(entity_id)s
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id AND rp.subreddit_entity_id = %(entity_id)s
    LEFT JOIN analysis.runs pruns
        ON pruns.doc_id = d.doc_id AND pruns.task = 'propaganda' AND pruns.is_current AND pruns.status = 'done'
    LEFT JOIN analysis.propaganda_results pr ON pr.run_id = pruns.run_id
    WHERE (ap.author_id IS NOT NULL OR na.doc_id IS NOT NULL OR rp.doc_id IS NOT NULL)
      AND NOT EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
"""

# Distinct model_id among every done, current analysis run against the
# entity's own doc history -- the RangeMeta honesty block's model_ids for
# entity-profile (all-time, so this covers the entity's entire analyzed
# history regardless of how many model/prompt versions ran across it).
_AUTHORED_MODEL_IDS_SQL = """
    SELECT DISTINCT r.model_id
    FROM corpus.documents d
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id AND ap.entity_id = %(entity_id)s
    LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id AND na.outlet_entity_id = %(entity_id)s
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id AND rp.subreddit_entity_id = %(entity_id)s
    JOIN analysis.runs r ON r.doc_id = d.doc_id AND r.is_current AND r.status = 'done'
    WHERE (ap.author_id IS NOT NULL OR na.doc_id IS NOT NULL OR rp.doc_id IS NOT NULL)
      AND NOT EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
"""

_STANCE_RECEIVED_SQL = """
    SELECT m.stance, COUNT(*) AS n
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    WHERE m.entity_id = %(entity_id)s AND r.is_current AND r.task = 'targets'
      AND m.confidence >= %(min_conf)s
      AND NOT EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
    GROUP BY m.stance
"""

_STANCE_EXPRESSED_SQL = """
    SELECT tm.stance, COUNT(*) AS n
    FROM analysis.target_mentions tm
    JOIN analysis.runs r ON r.run_id = tm.run_id
    JOIN corpus.documents d ON d.doc_id = tm.doc_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id AND ap.entity_id = %(entity_id)s
    LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id AND na.outlet_entity_id = %(entity_id)s
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id AND rp.subreddit_entity_id = %(entity_id)s
    WHERE r.is_current AND r.task = 'targets' AND tm.confidence >= %(min_conf)s
      AND (ap.author_id IS NOT NULL OR na.doc_id IS NOT NULL OR rp.doc_id IS NOT NULL)
      AND NOT EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
    GROUP BY tm.stance
"""


def get_entity_posts(
    entity_id: int,
    window: Optional[str] = None,
    date_from: Optional[datetime] = None,
    date_to: Optional[datetime] = None,
    page: int = 1,
) -> EntityPostsResponse:
    """Paginated docs mentioning/authored-by ``entity_id``, newest first.

    Accepts a preset ``window`` (or 'all') XOR an explicit ``date_from``/
    ``date_to`` range -- see queries/base.py::resolve_time_range. Historical
    data stays queryable forever (owner decision 2026-07-24): windows scope
    the aggregate denominator, never document existence, so an official's
    official_record history older than every preset window is reachable via
    'all' or an explicit range.
    """
    start, end = resolve_time_range(window, date_from, date_to)
    min_conf = get_settings().aggregation_min_confidence
    params = {
        "entity_id": entity_id, "start": start, "end": end, "min_conf": min_conf,
        "bot_floor": BOT_FLAGGED_SHARE_EXCLUSION,
    }
    page = max(1, page)

    with connection() as conn:
        entity = _require_entity(conn, entity_id)
        rows = conn.execute(_ENTITY_POSTS_SQL, params).fetchall()

    total = len(rows)
    start_idx = (page - 1) * ENTITY_POSTS_PAGE_SIZE
    page_rows = rows[start_idx:start_idx + ENTITY_POSTS_PAGE_SIZE]

    items = [
        EntityPostRow(
            relation=row["relation"],
            **build_sample_doc(
                {**row, "snippet": _snippet(row["title"])},
                admission_class=row["admission_class"],
            ),
        )
        for row in page_rows
    ]
    return EntityPostsResponse(
        entity_id=entity_id, entity_key=entity["entity_key"], window=window,
        page=page, page_size=ENTITY_POSTS_PAGE_SIZE, total=total, items=items,
    )


def get_entity_profile(entity_id: int) -> EntityProfileResponse:
    """ALL-TIME entity profile: no window cutoff anywhere in this
    function -- an inactive official's history must never vanish."""
    min_conf = get_settings().aggregation_min_confidence
    params = {"entity_id": entity_id, "min_conf": min_conf, "bot_floor": BOT_FLAGGED_SHARE_EXCLUSION}

    with connection() as conn:
        entity = _require_entity(conn, entity_id)
        authored_rows = conn.execute(_AUTHORED_DOCS_SQL, params).fetchall()
        model_id_rows = conn.execute(_AUTHORED_MODEL_IDS_SQL, params).fetchall()
        received_rows = conn.execute(_STANCE_RECEIVED_SQL, params).fetchall()
        expressed_rows = conn.execute(_STANCE_EXPRESSED_SQL, params).fetchall()
        entity_profiles = fetch_entity_profiles(conn, [entity_id])

    sampled, official_record = split_admission_counts(authored_rows)
    first_activity, last_activity = _activity_bounds(authored_rows)
    propaganda_rate, propaganda_analyzed = _propaganda_rate(authored_rows)

    return EntityProfileResponse(
        entity_id=entity_id,
        entity_key=entity["entity_key"],
        display_name=entity["display_name"],
        kind=entity["kind"],
        lean=LeanLabel(
            kind="fact" if entity["kind"] in ("official", "collective") else "curated",
            value=entity["lean"],
        ),
        profile=entity_profiles[entity_id],
        range=RangeMeta(
            window=None, start=None, end=None,
            sampled_doc_count=sampled, official_record_doc_count=official_record,
            model_ids=sorted({row["model_id"] for row in model_id_rows}),
        ),
        analyzed_doc_counts=AdmissionSplit(sampled=sampled, official_record=official_record),
        stance_received=_stance_distribution(received_rows),
        stance_expressed=_stance_distribution(expressed_rows),
        propaganda_rate=propaganda_rate,
        propaganda_analyzed_docs=propaganda_analyzed,
        first_activity=first_activity,
        last_activity=last_activity,
        monthly_activity=_monthly_activity(authored_rows),
    )


# --------------------------------------------------------------------------- #
#  Helpers                                                                    #
# --------------------------------------------------------------------------- #

def _require_entity(conn, entity_id: int) -> Dict[str, Any]:
    row = conn.execute(_ENTITY_ROW_SQL, {"entity_id": entity_id}).fetchone()
    if row is None:
        raise ValueError(f"unknown entity_id: {entity_id}")
    return row


def _snippet(title: Optional[str]) -> Optional[str]:
    if not title:
        return None
    return title[:SNIPPET_MAX_CHARS]


def _activity_bounds(rows: List[Any]):
    if not rows:
        return None, None
    timestamps = [row["published_at"] for row in rows]
    return min(timestamps), max(timestamps)


def _propaganda_rate(rows: List[Any]):
    analyzed = [row for row in rows if row["techniques_validated"] is not None]
    if not analyzed:
        return None, 0
    flagged = sum(1 for row in analyzed if row["techniques_validated"] > 0)
    return round(flagged / len(analyzed), 3), len(analyzed)


def _monthly_activity(rows: List[Any]) -> List[MonthlyActivity]:
    months: Dict[str, Dict[str, int]] = {}
    for row in rows:
        month = row["published_at"].strftime("%Y-%m")
        cell = months.setdefault(month, {"doc_count": 0, "sampled": 0, "official_record": 0})
        cell["doc_count"] += 1
        cell[row["admission_class"]] += 1
    return [
        MonthlyActivity(
            month=month, doc_count=cell["doc_count"],
            sampled_count=cell["sampled"], official_record_count=cell["official_record"],
        )
        for month, cell in sorted(months.items())
    ]


def _stance_distribution(rows: List[Any]) -> StanceDistribution:
    """Both stance_received and stance_expressed read target_mentions'
    sentiment_label vocabulary (positive/negative/neutral/mixed) directly."""
    counts = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
    for row in rows:
        counts[row["stance"]] += row["n"]
    volume = sum(counts.values())
    net_score = None
    if volume >= MIN_TARGET_SAMPLE_N:
        net_score = round((counts["positive"] - counts["negative"]) / volume * 100, 1)
    return StanceDistribution(**counts, volume=volume, net_score=net_score)
