"""
SQL query constants for the sentiment panel, plus thin fetch helpers that
execute them -- see panel.py for the orchestration that calls these.
"""

from __future__ import annotations

from typing import Any, Dict, List, Tuple

# NULL-safe bound predicate: either side of the (start, end) pair may be
# None (unbounded) -- resolve_time_range() is the only place that decides
# what None means (a preset, 'all', or an open-ended custom range).
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

# Carries every column the own-post tier bucketing (by_news_outlet/
# by_official/by_general_public), toneTrend, distributionSamples, and
# daySamples need -- one row per analyzed doc, one query for all four.
_SENTIMENT_ROWS_SQL = f"""
    SELECT d.doc_id, d.source_type, d.published_at, d.source_url, d.title,
           d.admission_class, d.domain_or_subreddit, d.author_id,
           r.confidence, sr.label,
           ap.tier AS author_tier, ap.entity_id AS author_entity_id,
           e_auth.kind AS author_entity_kind,
           na.outlet_entity_id, rp.subreddit_entity_id,
           au.handle AS x_handle, au.display_name AS x_display_name,
           au.description AS x_description, au.followers_count AS x_followers_count,
           (COALESCE(xp.retweet_count, 0) + COALESCE(xp.reply_count, 0)
            + COALESCE(xp.like_count, 0) + COALESCE(xp.quote_count, 0)) AS x_engagement
    FROM analysis.runs r
    JOIN analysis.sentiment_results sr ON sr.run_id = r.run_id
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
    LEFT JOIN corpus.entities e_auth ON e_auth.entity_id = ap.entity_id
    LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
    LEFT JOIN corpus.authors au ON au.author_id = d.author_id
    LEFT JOIN corpus.x_posts xp ON xp.doc_id = d.doc_id
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done'
      AND r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
"""

# Every analysis.runs row counted toward the panel's denominator (task='text',
# done, in-range, bot-excluded) -- not just the ones with a sentiment_results
# row, per the "trivial content has no sentiment row, never fake-neutral"
# rule. Carries admission_class + model_id for the RangeMeta honesty block.
_TOTAL_ANALYZED_SQL = f"""
    SELECT d.doc_id, d.admission_class, r.model_id
    FROM analysis.runs r
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done'
      AND r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
"""

_BOT_EXCLUDED_SQL = f"""
    SELECT COUNT(*) AS n
    FROM analysis.runs r
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done'
      AND r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
"""

_DOC_TOPICS_SQL = f"""
    SELECT m.doc_id, m.topic, COUNT(*) AS n
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    WHERE r.is_current AND r.task = 'targets'
      AND m.topic IS NOT NULL AND m.topic <> 'Other'
      AND m.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
    GROUP BY m.doc_id, m.topic
    ORDER BY m.doc_id, n DESC, m.topic
"""

# Carries the target entity's own fields (received tone) AND the mentioning
# doc's routing/author fields (outbound "who they're talking about" +
# expressed_alignment) -- one row per (doc, target) mention.
_TARGET_ROWS_SQL = f"""
    SELECT m.entity_id, m.stance, m.topic, m.raw_target, m.confidence,
           d.doc_id, d.source_type, d.source_url, d.published_at, d.title,
           d.admission_class, d.domain_or_subreddit, d.author_id,
           e.display_name, e.kind, e.lean, e.entity_key,
           e.lean_source AS target_lean_source,
           ap.tier AS author_tier, ap.entity_id AS author_entity_id,
           e_auth.kind AS author_entity_kind,
           e_auth.lean_source AS author_entity_lean_source, e_auth.lean AS author_entity_lean,
           na.outlet_entity_id, e_out.lean AS outlet_lean,
           rp.subreddit_entity_id, e_sub.lean AS subreddit_lean,
           au.handle AS x_handle, au.display_name AS x_display_name,
           au.description AS x_description, au.followers_count AS x_followers_count,
           (COALESCE(xp.retweet_count, 0) + COALESCE(xp.reply_count, 0)
            + COALESCE(xp.like_count, 0) + COALESCE(xp.quote_count, 0)) AS x_engagement
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    LEFT JOIN corpus.entities e ON e.entity_id = m.entity_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
    LEFT JOIN corpus.entities e_auth ON e_auth.entity_id = ap.entity_id
    LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
    LEFT JOIN corpus.entities e_out ON e_out.entity_id = na.outlet_entity_id
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
    LEFT JOIN corpus.entities e_sub ON e_sub.entity_id = rp.subreddit_entity_id
    LEFT JOIN corpus.authors au ON au.author_id = d.author_id
    LEFT JOIN corpus.x_posts xp ON xp.doc_id = d.doc_id
    WHERE r.is_current AND r.task = 'targets'
      AND m.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
"""

# Mentions withheld by the same author-level bot-flagged-share gate as
# _BOT_EXCLUSION_SQL, counted (not silently dropped) for TargetToneMeta.
_TARGET_BOT_EXCLUDED_SQL = f"""
    SELECT COUNT(*) AS n
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    WHERE r.is_current AND r.task = 'targets'
      AND m.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
"""

# doc_id -> [(narrative_id, name)] for the mention rows' docs -- powers
# received tone's by_narrative. Scoped to the doc_ids already selected by
# _TARGET_ROWS_SQL rather than the whole corpus.
_NARRATIVE_DOC_MAP_SQL = """
    SELECT nd.doc_id, n.narrative_id, n.name
    FROM analysis.narrative_docs nd
    JOIN analysis.narratives n ON n.narrative_id = nd.narrative_id
    WHERE nd.doc_id = ANY(%(doc_ids)s)
"""


def _fetch_doc_topics(conn, params: Dict[str, Any]) -> Dict[int, str]:
    """doc_id -> dominant resolved target_mentions topic (highest count,
    ties broken alphabetically); docs with no resolved topic are absent
    (``_topic_for_row`` reports those as unclassified, never a guess)."""
    topics: Dict[int, str] = {}
    for row in conn.execute(_DOC_TOPICS_SQL, params).fetchall():
        topics.setdefault(row["doc_id"], row["topic"])
    return topics


def _fetch_narrative_map(conn, target_rows: List[Any]) -> Dict[int, List[Tuple[int, str]]]:
    doc_ids = list({row["doc_id"] for row in target_rows})
    if not doc_ids:
        return {}
    out: Dict[int, List[Tuple[int, str]]] = {}
    for row in conn.execute(_NARRATIVE_DOC_MAP_SQL, {"doc_ids": doc_ids}).fetchall():
        out.setdefault(row["doc_id"], []).append((row["narrative_id"], row["name"]))
    return out
