"""
Public data-retrieval endpoints. All are served from pre-computed snapshots
where possible (see ``job_runner.save_snapshots``), falling back to live
aggregation on a miss.
"""

from fastapi import APIRouter, Query
from starlette.requests import Request

from analysis.src.api.cache_utils import WindowLiteral, get_cached_or_fallback
from analysis.src.api.rate_limits import limiter
from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators import (
    BotAggregator,
    GeoAggregator,
    NarrativeAggregator,
    PropagandaAggregator,
    SentimentAggregator,
)

settings = get_settings()
cache = SnapshotCache(settings.cache_dir)
sentiment_agg = SentimentAggregator(settings.db_path)
bot_agg = BotAggregator(settings.db_path)
geo_agg = GeoAggregator(settings.db_path)
narrative_agg = NarrativeAggregator(settings.db_path)
propaganda_agg = PropagandaAggregator(settings.db_path)

router = APIRouter(tags=["data"])

# Narrative cache stores a window-keyed top-100; anything at or below this
# limit is a cache hit, anything above goes live.
NARRATIVE_CACHE_SIZE = 100


@router.get("/sentiment")
def get_public_sentiment(window: WindowLiteral = "24h"):
    """Returns sentiment filtered by time window."""
    return get_cached_or_fallback(
        cache,
        f"sentiment_{window}",
        lambda: sentiment_agg.get_public_sentiment(time_window=window),
        lambda s: s.to_dict(),
    )


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
def get_propaganda(window: WindowLiteral = "7d"):
    """Returns propaganda-technique overview for the window."""
    return get_cached_or_fallback(
        cache,
        f"propaganda_{window}",
        lambda: propaganda_agg.get_propaganda_overview(time_window=window),
        lambda overview: overview.to_dict(),
    )


@router.get("/geo-sentiment")
@limiter.limit("10/minute")
def get_geo_sentiment(request: Request, window: WindowLiteral = "7d"):
    """Returns X posts aggregated by country with sentiment scores.

    Uses explicit country_code from X API geo-tags (no heuristics).
    """
    return get_cached_or_fallback(
        cache,
        f"geo_sentiment_{window}",
        lambda: geo_agg.get_country_sentiment(time_window=window),
    )
