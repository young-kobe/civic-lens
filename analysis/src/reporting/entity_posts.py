"""
Row-level drill-down reads for entity detail views.

The snapshot cache stores per-entity aggregates plus a handful of
highest-confidence samples (``MAX_SAMPLES_PER_ENTITY``); everything else a
reader might want to audit is pre-truncated away. This module serves the
full, paginated list of classified posts for one entity card — an indexed
SELECT with LIMIT/OFFSET, not aggregation, so it stays within the "no heavy
aggregation at request time" invariant while giving the API its first
(read-only) database dependency.

Filtering mirrors the sentiment aggregator: task_type='sentiment', the
aggregation confidence floor, doc-level bot exclusion, and one row per doc
(latest output). Entity resolution reuses the registry the aggregator
routes with, so a card and its drill-down cover the same posts.
"""

from __future__ import annotations

import json
from typing import Any, Dict, List, Tuple

from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators.base import (
    REDDIT_ENGAGEMENT_JOIN_SQL,
    SAMPLE_ENRICHMENT_SELECT,
    X_AUTHOR_JOIN_SQL,
    build_sample_author,
    build_sample_engagement,
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.sentiment import (
    CATCH_ALL_VERIFIED_OFFICIALS,
    _build_doc_targets,
    _build_sample_dict,
    _extract_topic,
)
from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    canonicalize_handle,
    canonicalize_news_domain,
    canonicalize_subreddit,
    get_registry,
)

MAX_PAGE_SIZE = 50

# Doc-level bot exclusion, mirroring get_bot_flagged_doc_ids but expressed
# in SQL so it composes with LIMIT/OFFSET instead of materializing the doc
# set in Python per request.
_BOT_EXCLUSION_SQL = """
    a.doc_id NOT IN (
        SELECT b.doc_id FROM ai_outputs_latest b
        JOIN docs bd ON bd.doc_id = b.doc_id
        WHERE b.task_type = 'bot_detection'
          AND b.label = 'bot'
          AND b.confidence >= ?
          AND bd.source_type IN ('reddit_post', 'reddit_comment', 'x_post')
    )
"""

def _with_www(domains: List[str]) -> List[str]:
    """Stored news domains are raw (may carry a www. prefix); match both."""
    out: List[str] = []
    for d in domains:
        out.append(d)
        if not d.startswith("www."):
            out.append(f"www.{d}")
    return out


def _placeholders(values: List[str]) -> str:
    return ",".join("?" for _ in values)


def _entity_filter(kind: str, key: str) -> Tuple[str, List[Any]]:
    """(where_sql, params) scoping docs to one entity card. ``kind`` + ``key``
    match what the aggregator stamps on EntitySentimentItem, so the drill-down
    covers exactly the posts the card counted. Raises ValueError for
    combinations that don't resolve."""
    registry = get_registry()

    if kind == "outlet":
        entity = registry.get_outlet(key)
        if entity is None:
            raise ValueError(f"unknown outlet: {key}")
        domains = _with_www(sorted({entity.domain, *entity.also_domains}))
        return (
            f"d.source_type = 'news' AND lower(d.domain_or_subreddit) IN ({_placeholders(domains)})",
            domains,
        )

    if kind == "official":
        entity = registry.get_official(key)
        if entity is None:
            raise ValueError(f"unknown official: {key}")
        handles = sorted({entity.handle, *entity.also_handles})
        return (
            f"d.source_type = 'x_post' AND lower(u.username) IN ({_placeholders(handles)})",
            handles,
        )

    if kind == "subreddit":
        sub = canonicalize_subreddit(key)
        if not sub or registry.get_subreddit(sub) is None:
            raise ValueError(f"unknown subreddit: {key}")
        variants = [sub, f"r/{sub}"]
        return (
            "d.source_type IN ('reddit_post', 'reddit_comment') "
            f"AND lower(d.domain_or_subreddit) IN ({_placeholders(variants)})",
            variants,
        )

    if kind == "account":
        handle = canonicalize_handle(key)
        if not handle:
            raise ValueError(f"invalid account handle: {key}")
        return ("d.source_type = 'x_post' AND lower(u.username) = ?", [handle])

    if kind == "catch_all":
        return _catch_all_filter(registry, key)

    raise ValueError(f"unknown entity kind: {kind}")


def _catch_all_filter(registry, key: str) -> Tuple[str, List[Any]]:
    """Catch-all buckets are defined by exclusion: everything in the tier
    that did NOT match a registry entity (or, for X, the curated accounts
    list). Must stay in sync with sentiment._route_and_record."""
    if key == CATCH_ALL_OUTLETS:
        domains = _with_www(sorted(registry.outlets.keys()))
        clause = "d.source_type = 'news'"
        if domains:
            clause += f" AND lower(d.domain_or_subreddit) NOT IN ({_placeholders(domains)})"
        return clause, domains

    if key == CATCH_ALL_SUBREDDITS:
        subs: List[str] = []
        for s in sorted(registry.subreddits.keys()):
            subs.extend([s, f"r/{s}"])
        clause = "d.source_type IN ('reddit_post', 'reddit_comment')"
        if subs:
            clause += f" AND lower(d.domain_or_subreddit) NOT IN ({_placeholders(subs)})"
        return clause, subs

    if key in (CATCH_ALL_X_USERS, CATCH_ALL_VERIFIED_OFFICIALS):
        handles = sorted(registry.officials.keys())
        provenance = (
            "COALESCE(x.is_official_tier, 0) = 1"
            if key == CATCH_ALL_VERIFIED_OFFICIALS
            else "COALESCE(x.is_official_tier, 0) = 0"
        )
        clause = f"d.source_type = 'x_post' AND {provenance}"
        params: List[Any] = []
        if handles:
            clause += f" AND (u.username IS NULL OR lower(u.username) NOT IN ({_placeholders(handles)}))"
            params.extend(handles)
        if key == CATCH_ALL_X_USERS:
            # Curated accounts get their own named cards — exclude them here.
            clause += (
                " AND (x.author_id IS NULL OR x.author_id NOT IN ("
                "SELECT author_id FROM account_profiles "
                "WHERE platform = 'x' AND tier IN ('elected_official', 'affiliated')))"
            )
        return clause, params

    raise ValueError(f"unknown catch-all key: {key}")


def fetch_entity_posts(
    db_path: str,
    kind: str,
    key: str,
    window: str = "7d",
    limit: int = 20,
    offset: int = 0,
) -> Dict[str, Any]:
    """Paginated classified posts for one entity card, newest first.

    Returns ``{"items": [<ClassificationSample dict>...], "total": int,
    "limit": int, "offset": int}``. ``total`` counts every matching post so
    the UI can page honestly. Raises ValueError for unresolvable kind/key.
    """
    limit = max(1, min(int(limit), MAX_PAGE_SIZE))
    offset = max(0, int(offset))
    min_conf = get_settings().aggregation_min_confidence
    cutoff = get_time_cutoff(window)
    entity_sql, entity_params = _entity_filter(kind, key)

    # ai_outputs_latest guarantees one sentiment row per doc (reruns /
    # prompt-version bumps append rows; only the newest counts).
    where = (
        f"a.task_type = 'sentiment' AND a.confidence >= ? "
        f"AND {_BOT_EXCLUSION_SQL} AND ({entity_sql})"
    )
    params: List[Any] = [min_conf, min_conf, *entity_params]
    if cutoff is not None:
        where += " AND d.published_at >= ?"
        params.append(cutoff)

    base = (
        f"FROM ai_outputs_latest a JOIN docs d ON a.doc_id = d.doc_id "
        f"{X_AUTHOR_JOIN_SQL} {REDDIT_ENGAGEMENT_JOIN_SQL} WHERE {where}"
    )

    with get_connection(db_path) as conn:
        cursor = conn.cursor()
        cursor.execute(f"SELECT COUNT(*) {base}", tuple(params))
        total = cursor.fetchone()[0]
        # Dominant LLM mention topic per doc (mirrors sentiment.py's
        # _fetch_doc_topics: latest outputs only, 'Other' excluded, ties by
        # count then alphabetically); title-keyword fallback happens below.
        llm_topic_sql = """
            (SELECT m.topic FROM target_mentions m
             JOIN ai_outputs_latest la ON la.output_id = m.output_id
             WHERE m.doc_id = a.doc_id AND m.topic IS NOT NULL
               AND m.topic != 'Other' AND m.confidence >= ?
             GROUP BY m.topic ORDER BY COUNT(*) DESC, m.topic LIMIT 1)
        """
        cursor.execute(
            "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, "
            "d.published_at, d.title, d.domain_or_subreddit, d.ident, d.text, "
            f"u.username, {llm_topic_sql}, {SAMPLE_ENRICHMENT_SELECT} "
            f"{base} ORDER BY d.published_at DESC, a.doc_id DESC LIMIT ? OFFSET ?",
            (min_conf,) + tuple(params) + (limit, offset),
        )
        rows = cursor.fetchall()

        # Target chips for this page's docs, same rules as the snapshot
        # samples (_attach_sample_targets): frozen mentions, floor applied.
        doc_targets: Dict[int, List[Dict[str, str]]] = {}
        page_doc_ids = [r[0] for r in rows]
        if page_doc_ids:
            cursor.execute(
                "SELECT m.doc_id, m.entity_key, m.stance, m.raw_target "
                "FROM target_mentions m "
                "JOIN ai_outputs_latest la ON la.output_id = m.output_id "
                f"WHERE m.confidence >= ? AND m.doc_id IN ({','.join('?' for _ in page_doc_ids)})",
                (min_conf, *page_doc_ids),
            )
            doc_targets = _build_doc_targets(cursor.fetchall())

    items: List[Dict[str, Any]] = []
    for (
        doc_id, output_json, confidence, source_type, published_at,
        title, domain_or_subreddit, ident, text, x_handle, llm_topic,
        x_retweets, x_replies, x_likes, x_quotes,
        u_name, u_avatar, u_verified_type, u_followers, u_created_at,
        u_bio, reddit_score, reddit_comments,
    ) in rows:
        try:
            data = json.loads(output_json)
        except json.JSONDecodeError:
            continue
        conf = float(data.get("confidence", confidence or 0.5))
        sample = _build_sample_dict(
            doc_id, data.get("label", "NEUTRAL"), conf, data,
            title, source_type, published_at, domain_or_subreddit, ident, text,
            x_handle, topic=llm_topic or _extract_topic(title),
            engagement=build_sample_engagement(
                source_type, x_retweets, x_replies, x_likes, x_quotes,
                reddit_score, reddit_comments,
            ),
            author=build_sample_author(
                source_type, x_handle, u_name, u_avatar, u_verified_type,
                u_followers, u_created_at, bio=u_bio,
            ),
        )
        sample["targets"] = doc_targets.get(doc_id)
        items.append(sample)

    return {"items": items, "total": total, "limit": limit, "offset": offset}
