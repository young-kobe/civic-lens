"""
GOP favorability merge — the secondary data path folded into the sentiment
result.

Leaf module: parses ``task_type='favorability'`` rows into a distribution,
per-platform split, and daily net trend, then formats them onto the
``PublicSentimentResult``. No dependency on the other sentiment submodules.
"""

from __future__ import annotations

import json
from datetime import datetime
from typing import Any, Dict, List, Optional, Set

from analysis.src.common.cache import SnapshotCache
from analysis.src.reporting.models import PublicSentimentResult


def _merge_favorability_data(
    result: PublicSentimentResult,
    fav_rows: List[tuple],
    bot_docs: Set[int],
    allowed_sources: Optional[frozenset],
    cache: SnapshotCache,
) -> None:
    """Merge GOP favorability into the sentiment result. No-op when
    every row is filtered (bot-flagged / wrong source / parse failure)
    so downstream consumers see a clean 'no data' via the default
    None fields."""
    distribution, by_platform, daily_net, count = _parse_favorability_rows(
        fav_rows, bot_docs, allowed_sources,
    )
    if count == 0:
        return
    _format_favorability_result(result, distribution, by_platform, daily_net, count, cache)


def _parse_favorability_rows(
    fav_rows: List[tuple],
    bot_docs: Set[int],
    allowed_sources: Optional[frozenset],
) -> tuple:
    distribution = {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0}
    by_platform: Dict[str, Dict[str, int]] = {}
    daily_net: Dict[str, Dict[str, Any]] = {}
    count = 0

    for doc_id, output_json, _confidence, source_type, pub_at in fav_rows:
        if doc_id in bot_docs:
            continue
        if allowed_sources is not None and (source_type or "") not in allowed_sources:
            continue
        try:
            data = json.loads(output_json)
        except json.JSONDecodeError:
            continue

        stance = data.get("overall_gop_stance", "neutral")
        if stance not in distribution:
            stance = "neutral"
        distribution[stance] += 1

        platform = source_type or "unknown"
        if platform in ("reddit_post", "reddit_comment"):
            platform = "reddit"
        by_platform.setdefault(platform, {"favorable": 0, "unfavorable": 0, "neutral": 0})
        if stance in by_platform[platform]:
            by_platform[platform][stance] += 1

        if pub_at:
            _track_daily_favorability(daily_net, pub_at, stance)

        count += 1

    return distribution, by_platform, daily_net, count


def _track_daily_favorability(
    daily_net: Dict[str, Dict[str, Any]],
    pub_at: float,
    stance: str,
) -> None:
    try:
        date_str = datetime.fromtimestamp(pub_at).strftime("%Y-%m-%d")
    except (ValueError, TypeError, OSError):
        return
    bucket = daily_net.setdefault(date_str, {"score": 0, "count": 0})
    bucket["score"] += 1 if stance == "favorable" else (-1 if stance == "unfavorable" else 0)
    bucket["count"] += 1


def _format_favorability_result(
    result: PublicSentimentResult,
    distribution: Dict[str, int],
    by_platform: Dict[str, Dict[str, int]],
    daily_net: Dict[str, Dict[str, Any]],
    count: int,
    cache: SnapshotCache,
) -> None:
    total = sum(distribution.values())
    net_favorability = ((distribution["favorable"] - distribution["unfavorable"]) / total * 100) if total > 0 else 0

    result.gopFavorability = {
        "favorable": round((distribution["favorable"] / total) * 100, 1) if total else 0,
        "unfavorable": round((distribution["unfavorable"] / total) * 100, 1) if total else 0,
        "neutral": round((distribution["neutral"] / total) * 100, 1) if total else 0,
        "netFavorability": round(net_favorability, 1),
        "sampleSize": count,
        "sourceCount": len(by_platform),
        "lastUpdated": datetime.now().isoformat(),
    }

    result.gopTrend = [
        {
            "date": date,
            "value": round((stats["score"] / stats["count"]) * 100, 1) if stats["count"] > 0 else 0,
        }
        for date, stats in sorted(daily_net.items())
    ][-30:]

    result.gopByPlatform = [
        {
            "group": platform.capitalize(),
            "favorable": stats["favorable"],
            "unfavorable": stats["unfavorable"],
            "neutral": stats["neutral"],
        }
        for platform, stats in by_platform.items()
    ]

    polling_data = cache.load("polling_gop")
    if polling_data:
        result.pollingVsSocial = {
            "onlineSentiment": {
                "favorable": result.gopFavorability["favorable"],
                "unfavorable": result.gopFavorability["unfavorable"],
                "neutral": result.gopFavorability["neutral"],
            },
            "pollingData": polling_data,
        }
