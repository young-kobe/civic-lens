"""
Public data-retrieval endpoints. All are served from pre-computed snapshots
where possible (see ``job_runner.save_snapshots``), falling back to live
aggregation on a miss.
"""

from fastapi import APIRouter, HTTPException, Query
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
from analysis.src.reporting.entity_posts import MAX_PAGE_SIZE, fetch_entity_posts

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
):
    """Returns sentiment for the time window.

    Source separation is built into the response shape via the three-tier
    rollup (news outlets / officials / general public), so the previous
    ``source`` query param has been removed alongside the UI's
    "Filter by sources" pills."""
    return get_cached_or_fallback(
        cache,
        f"sentiment_{window}",
        lambda: sentiment_agg.get_public_sentiment(time_window=window),
        lambda s: s.to_dict(),
    )


@router.get("/bot-activity")
def get_bot_activity(window: WindowLiteral = "24h"):
    """Returns bot-activity metrics (automation rate, coordination, behavioral
    signals) for the time window. The window applies a published_at cutoff in
    the aggregator so the numbers match the selected pill (audit U-1a)."""
    return get_cached_or_fallback(
        cache,
        f"bot_activity_{window}",
        lambda: bot_agg.get_bot_activity(time_window=window),
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
):
    """Returns propaganda-technique overview for the window. Source
    separation is built into the three-tier response shape; the previous
    ``source`` query param has been removed alongside the UI's
    "Filter by sources" pills."""
    return get_cached_or_fallback(
        cache,
        f"propaganda_{window}",
        lambda: propaganda_agg.get_propaganda_overview(time_window=window),
        lambda overview: overview.to_dict(),
    )


@router.get("/entity-posts")
@limiter.limit("30/minute")
def get_entity_posts(
    request: Request,
    kind: str,
    key: str,
    window: WindowLiteral = "7d",
    limit: int = Query(default=20, ge=1, le=MAX_PAGE_SIZE),
    offset: int = Query(default=0, ge=0),
):
    """Paginated classified posts behind one entity card, newest first.

    Live read (not snapshot-cached), like ``/movers``: the cache stores only
    a handful of highest-confidence samples per entity, and this endpoint
    exists precisely to show what the cache pre-truncates. It is an indexed
    SELECT with LIMIT/OFFSET — row retrieval, not aggregation — so the
    "no heavy aggregation at request time" rule still holds.
    """
    try:
        return fetch_entity_posts(
            settings.db_path, kind=kind, key=key,
            window=window, limit=limit, offset=offset,
        )
    except ValueError as exc:
        raise HTTPException(status_code=404, detail=str(exc))


@router.get("/movers")
@limiter.limit("20/minute")
def get_movers(request: Request, window: WindowLiteral = "7d"):
    """Returns top biggest movers in political tone and GOP favorability
    between the current window and the previous equivalent window.

    Computed live (not snapshot-cached) — it's a two-SQL-pass diff, cheap
    enough to run per-request at the rate limit configured here."""
    return movers_agg.get_movers(time_window=window).to_dict()


@router.get("/eval-accuracy")
def get_eval_accuracy():
    """Per-task human-review agreement (from ai_output_evals), for the UI's
    "human agreement" chips next to AI-derived numbers. Public and
    aggregate-only: task-level counts and percentages, no reviewer identity
    or per-row labels (those stay behind the admin-gated /review/stats).
    Accuracy is suppressed server-side below the public review floor."""
    from analysis.src.reporting.review import ReviewService
    return get_cached_or_fallback(
        cache,
        "eval_accuracy",
        lambda: ReviewService(settings.db_path).get_public_accuracy(),
        lambda stats: stats,
    )


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
