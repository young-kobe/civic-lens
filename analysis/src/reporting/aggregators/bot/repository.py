"""
Database scans for the bot aggregator.

Three read passes, all cutoff-windowed by ``published_at`` so the numbers
match the selected time pill:
  - ``_fetch_bot_detection_data``: per-doc automation-rate + bookkeeping.
  - ``_fetch_behavior_signals``: account-age / reuse from the flagged authors.
  - ``_fetch_entity_rollups``: per-entity bot-classification rates.

Pre-excluded rows (``inference_method='deterministic'``) are pre-exclusion
markers for electeds / affiliated / government-verified accounts and are
dropped from every denominator and indicator count. News is excluded by
contract: articles are not accounts.
"""

from __future__ import annotations

import datetime
import json
import re
from collections import Counter
from typing import Any, Dict, List, Tuple

from analysis.src.reporting.aggregators.base import X_AUTHOR_JOIN_SQL
from analysis.src.reporting.aggregators.bot.entities import (
    _build_bot_entity_item,
    _entity_sort_key,
    _resolve_entity_profile,
)
from analysis.src.reporting.aggregators.bot.narratives import _flagged_example
from analysis.src.reporting.aggregators.bot.types import BotDetectionAggregate
from analysis.src.reporting.aggregators.rows import BotDetectionRow
from analysis.src.reporting.entity_registry import (
    get_registry,
    route_reporting_entity,
)
from analysis.src.reporting.models import BotEntityItem


_URL_RE = re.compile(r"https?://([^/\s]+)", re.IGNORECASE)

# Canonical 11-column projection shared by both bot-detection scans (see
# BotDetectionRow). Column order matches the dataclass field order so
# BotDetectionRow.from_row can unpack it positionally.
_BOT_DETECTION_SELECT = (
    "a.doc_id, a.output_json, a.confidence, a.inference_method, "
    "d.source_type, d.domain_or_subreddit, d.published_at, d.ident, d.text, "
    "u.username, x.is_official_tier"
)


def _fetch_bot_detection_data(cursor, cutoff=None) -> BotDetectionAggregate:
    """
    Returns counts + per-doc / per-author bookkeeping used downstream.
    Pre-excluded rows (inference_method='deterministic') are dropped from
    every count so the automation rate denominator is eligible posts only.
    ``cutoff`` (unix seconds, or None) windows the scan by published_at.
    """
    # LEFT JOIN x_posts_raw + x_users_raw so bot-flagged x_posts
    # carry the author handle we need to synthesize an X permalink in
    # _narrative_amplification (C1 invariant: every evidence surface
    # links back to the original). This join is a flagged duplication
    # hotspot; see docs/todos/backend-aggregator-audit.md §1.
    cursor.execute(
        "SELECT "
        + _BOT_DETECTION_SELECT
        + """
        FROM ai_outputs_latest a
        JOIN docs d ON a.doc_id = d.doc_id
        """
        + X_AUTHOR_JOIN_SQL
        + """
        WHERE a.task_type = 'bot_detection'
          AND d.source_type != 'news'
        """
        + ("" if cutoff is None else " AND d.published_at >= ?"),
        () if cutoff is None else (cutoff,),
    )
    agg = BotDetectionAggregate()
    bot_idents: List[str] = []  # for author lookup (tweet_ids)

    for raw in cursor.fetchall():
        row = BotDetectionRow.from_row(raw)
        # Drop pre-exclusion rows from the denominator entirely.
        if (row.inference_method or "") == "deterministic":
            continue
        agg.total_eligible += 1
        try:
            data = json.loads(row.output_json) if row.output_json else {}
        except json.JSONDecodeError:
            continue
        label = data.get("label", "human")
        if label == "bot":
            agg.bot_count += 1
            agg.bot_doc_ids.add(row.doc_id)
            agg.bot_docs_data.append({
                "doc_id": row.doc_id,
                "data": data,
                "confidence": row.confidence,
                "source_type": row.source_type,
                "domain": row.domain_or_subreddit,
                "ident": row.ident,
                "x_handle": row.username,
                "text": row.text,
                "pub_at": row.published_at,
            })
            if row.domain_or_subreddit:
                agg.by_cluster[row.domain_or_subreddit] = (
                    agg.by_cluster.get(row.domain_or_subreddit, 0) + 1
                )
            for indicator in data.get("indicators", []):
                agg.indicators_frequency[indicator] += 1
            if row.published_at:
                agg.hourly_distribution[
                    datetime.datetime.fromtimestamp(row.published_at).hour
                ] += 1
            if row.text:
                agg.bot_texts.append(row.text)
                for m in _URL_RE.finditer(row.text):
                    agg.bot_urls.append(m.group(1).lower())
            if row.source_type == "x_post" and row.ident:
                bot_idents.append(row.ident)
        elif label == "suspicious":
            agg.suspicious_count += 1

    # Resolve X author_ids for bot-flagged x_posts (needed for accountReuse).
    if bot_idents:
        placeholders = ",".join("?" * len(bot_idents))
        cursor.execute(
            f"SELECT author_id FROM x_posts_raw WHERE tweet_id IN ({placeholders})",
            bot_idents,
        )
        agg.bot_authors = [r[0] for r in cursor.fetchall() if r[0]]

    return agg


def _fetch_behavior_signals(
    cursor, bot_doc_ids: set[int], bot_authors: List[str],
) -> Dict[str, Any]:
    """Compute the no-longer-stubbed behavioral signals.

    - accountAgeDistribution: bucket x_users_raw.created_at for the flagged
      X authors. Falls back to an empty list if we have no X authors.
    - avgPostsPerSuspectedAccount + accountReuse: from bot_authors counts.
    - identicalTextPairs + copyPasteSimilarity: computed in the caller
      from bot_texts.
    """
    buckets = {
        "< 7 days": 0, "7-30 days": 0, "30-90 days": 0,
        "90-365 days": 0, "1-3 years": 0, "3+ years": 0,
        "unknown": 0,
    }
    if bot_authors:
        unique_authors = list(set(bot_authors))
        placeholders = ",".join("?" * len(unique_authors))
        cursor.execute(
            f"SELECT user_id, created_at FROM x_users_raw WHERE user_id IN ({placeholders})",
            unique_authors,
        )
        now = int(datetime.datetime.now(datetime.timezone.utc).timestamp())
        for _uid, created_at in cursor.fetchall():
            if not created_at:
                buckets["unknown"] += 1
                continue
            age_days = (now - created_at) / 86400
            if age_days < 7:
                buckets["< 7 days"] += 1
            elif age_days < 30:
                buckets["7-30 days"] += 1
            elif age_days < 90:
                buckets["30-90 days"] += 1
            elif age_days < 365:
                buckets["90-365 days"] += 1
            elif age_days < 365 * 3:
                buckets["1-3 years"] += 1
            else:
                buckets["3+ years"] += 1

    total_buckets = sum(buckets.values())
    account_age_distribution = [
        {
            "range": k,
            "count": v,
            "percentage": round((v / total_buckets) * 100, 1) if total_buckets else 0.0,
        }
        for k, v in buckets.items()
    ]

    # Per-author counts of bot-flagged posts (accountReuse + avg per account).
    author_counts = Counter(bot_authors)
    unique_authors = len(author_counts)
    total_author_bot_posts = sum(author_counts.values())
    avg_posts_per_suspected = (
        total_author_bot_posts / unique_authors if unique_authors else 0.0
    )
    reuse_authors = sum(1 for _, c in author_counts.items() if c > 1)
    account_reuse = reuse_authors / unique_authors if unique_authors else 0.0

    return {
        "account_age_distribution": account_age_distribution,
        "avg_posts_per_suspected_account": round(avg_posts_per_suspected, 2),
        "account_reuse": round(account_reuse, 3),
    }


def _fetch_entity_rollups(cursor, cutoff=None) -> Dict[str, List[BotEntityItem]]:
    """Per-entity bot-classification rates for the two-way grid.

    Runs one joined scan of ai_outputs.task='bot_detection' rows together
    with x author-handle lookup, then buckets each row into officials /
    public via ``route_reporting_entity`` — including the ingestor's
    ``is_official_tier`` provenance flag, so posts pulled via the
    verified-officials timeline land in the officials column even when
    the stored handle doesn't match the editorial registry (audit D-4;
    without this the Politicians & Officials tier stayed empty). Each
    entity also collects up to ``_SAMPLES_PER_ENTITY`` of its
    bot-flagged posts, confidence-ranked, so the card can open an
    evidence modal instead of dead-ending at an external link.

    News is excluded by contract (2026-07-11): articles are not
    accounts, so "automation rate of an outlet's articles" is not a
    real metric. ``by_news_outlet`` stays in the payload, permanently
    empty, so stale caches and older UI builds keep parsing.
    """
    cursor.execute(
        "SELECT "
        + _BOT_DETECTION_SELECT
        + """
        FROM ai_outputs_latest a
        JOIN docs d ON d.doc_id = a.doc_id
        """
        + X_AUTHOR_JOIN_SQL
        + """
        WHERE a.task_type = 'bot_detection'
          AND d.source_type != 'news'
        """
        + ("" if cutoff is None else " AND d.published_at >= ?"),
        () if cutoff is None else (cutoff,),
    )
    # (tier, key) -> accumulator
    accum: Dict[Tuple[str, str], Dict[str, Any]] = {}
    registry = get_registry()
    for raw in cursor.fetchall():
        row = BotDetectionRow.from_row(raw)
        if (row.inference_method or "") == "deterministic":
            continue
        try:
            data = json.loads(row.output_json) if row.output_json else {}
        except json.JSONDecodeError:
            continue
        label = data.get("label", "human")

        route = route_reporting_entity(
            registry, row.source_type, row.domain_or_subreddit, row.username,
            is_official_tier=bool(row.is_official_tier),
        )
        if route is None:
            continue
        profile = _resolve_entity_profile(route)
        key = route.key

        slot = accum.setdefault((route.tier, key), {
            "kind": profile["kind"], "profile": profile,
            "total": 0, "bot": 0, "flagged": [],
        })
        slot["total"] += 1
        if label == "bot":
            slot["bot"] += 1
            slot["flagged"].append((
                float(row.confidence or 0.0),
                _flagged_example(
                    row.doc_id, row.text, row.source_type,
                    row.domain_or_subreddit, row.username, row.ident,
                    data=data, confidence=row.confidence,
                ),
            ))

    rollups: Dict[str, List[BotEntityItem]] = {
        "news": [], "officials": [], "public": [],
    }
    for (tier, key), s in accum.items():
        rollups[tier].append(_build_bot_entity_item(key, s))
    for tier, items in rollups.items():
        items.sort(key=_entity_sort_key)
    return rollups
