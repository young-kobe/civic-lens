"""
Public data-retrieval endpoints. All are served from pre-computed snapshots
where possible (see ``job_runner.save_snapshots``), falling back to live
aggregation on a miss.
"""

from typing import Literal

from fastapi import APIRouter, Query
from starlette.requests import Request

from analysis.src.api.cache_utils import WindowLiteral, get_cached_or_fallback
from analysis.src.api.rate_limits import limiter
from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators import (
    BotAggregator,
    MoversAggregator,
    NarrativeAggregator,
    PropagandaAggregator,
    SentimentAggregator,
)

# Kept in lockstep with ui/src/types.ts Filters.sourceType.
SourceLiteral = Literal["all", "news", "reddit", "social"]

settings = get_settings()
cache = SnapshotCache(settings.cache_dir)
sentiment_agg = SentimentAggregator(settings.db_path)
bot_agg = BotAggregator(settings.db_path)
narrative_agg = NarrativeAggregator(settings.db_path)
propaganda_agg = PropagandaAggregator(settings.db_path)
movers_agg = MoversAggregator(settings.db_path)

router = APIRouter(tags=["data"])

# Narrative cache stores a window-keyed top-100; anything at or below this
# limit is a cache hit, anything above goes live.
NARRATIVE_CACHE_SIZE = 100


@router.get("/sentiment")
def get_public_sentiment(
    window: WindowLiteral = "24h",
    source: SourceLiteral = "all",
):
    """Returns sentiment filtered by time window (and optionally by source).

    The snapshot cache stores the unfiltered variant; any non-"all" source
    filter bypasses the cache and computes live. This avoids exploding the
    cache into 4x entries for a rarely-used filter dimension and keeps the
    scheduled aggregation cost flat."""
    if source == "all":
        return get_cached_or_fallback(
            cache,
            f"sentiment_{window}",
            lambda: sentiment_agg.get_public_sentiment(time_window=window),
            lambda s: s.to_dict(),
        )
    return sentiment_agg.get_public_sentiment(
        time_window=window, source_filter=source,
    ).to_dict()


@router.get("/bot-activity")
def get_bot_activity():
    """Returns bot-activity metrics (automation rate, coordination, behavioral signals)."""
    return get_cached_or_fallback(
        cache,
        "bot_activity",
        bot_agg.get_bot_activity,
        lambda b: b.to_dict(),
    )


@router.get("/narratives")
@limiter.limit("10/minute")
def get_narratives(
    request: Request,
    window: WindowLiteral = "7d",
    limit: int = Query(default=20, ge=1, le=500),
):
    """Returns top narratives in the time window.

    The cache stores a window-keyed top-100; limits <= 100 hit cache and are
    sliced to the caller's limit. Limits between 101 and 500 skip the cache and
    compute live — the upper bound exists so a `?limit=1_000_000` request
    can't blow up the SQL aggregator (audit §1.3).
    """
    if limit <= NARRATIVE_CACHE_SIZE:
        cached = get_cached_or_fallback(
            cache,
            f"narratives_{window}",
            lambda: narrative_agg.get_top_narratives(time_window=window, limit=NARRATIVE_CACHE_SIZE),
            lambda narratives: [n.to_dict() for n in narratives],
        )
        if isinstance(cached, list):
            return cached[:limit]
        return cached

    narratives = narrative_agg.get_top_narratives(time_window=window, limit=limit)
    return [n.to_dict() for n in narratives]


@router.get("/propaganda")
def get_propaganda(
    window: WindowLiteral = "7d",
    source: SourceLiteral = "all",
):
    """Returns propaganda-technique overview for the window (optionally
    filtered by source). Non-"all" source bypasses the cache for the same
    reason as /sentiment."""
    if source == "all":
        return get_cached_or_fallback(
            cache,
            f"propaganda_{window}",
            lambda: propaganda_agg.get_propaganda_overview(time_window=window),
            lambda overview: overview.to_dict(),
        )
    return propaganda_agg.get_propaganda_overview(
        time_window=window, source_filter=source,
    ).to_dict()


@router.get("/movers")
@limiter.limit("20/minute")
def get_movers(request: Request, window: WindowLiteral = "7d"):
    """Returns top biggest movers in political tone and GOP favorability
    between the current window and the previous equivalent window.

    Computed live (not snapshot-cached) — it's a two-SQL-pass diff, cheap
    enough to run per-request at the rate limit configured here."""
    return movers_agg.get_movers(time_window=window).to_dict()


@router.get("/snapshot-status")
def get_snapshot_status():
    """Returns `generated_at` + `doc_count` per cached snapshot.

    Public (non-admin): the UI uses this to show the real "data refreshed at"
    timestamp in the header and in each page's GlobalTicker, instead of
    render-time ``new Date()``. The absolute ``cache_dir`` path is NOT
    exposed here — only the key, its ISO timestamp, and the doc count. Admin
    operators get the fuller view at ``/admin/cache-status``.

    No per-route rate limit — inherits the server-wide 120/min-per-IP
    default, matching the plain ``/sentiment`` + ``/bot-activity`` pattern.
    The SPA calls it once per page mount and the useFetch module cache
    dedupes across tab switches, so a well-behaved client sits well under
    the cap. Cost is bounded: ~14 file stats + JSON _meta reads per call.
    """
    return {
        "snapshots": [
            {
                "key": m["key"],
                "generated_at": m["generated_at"],
                "doc_count": m["doc_count"],
            }
            for m in cache.get_all_metadata()
        ],
    }
