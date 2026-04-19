"""
Civic Lens Analysis API Server.

Provides endpoints for:
- ETL operations (loading raw content)
- Analysis triggers (bot detection, sentiment, favorability)
- Aggregated data retrieval (served from pre-computed cache)
- Cache status and metadata
"""

from typing import Optional
from fastapi import FastAPI, BackgroundTasks, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.common.cache import SnapshotCache
from analysis.src.etl.loader import ContentLoader
from analysis.src.engine.bot import HybridBotDetector
from analysis.src.engine.analyzer import Analyzer
from analysis.src.reporting.aggregators import (
    OutletAggregator,
    SentimentAggregator,
    BotAggregator,
    GeoAggregator,
    NarrativeAggregator,
)
from analysis.src.reporting.review import ReviewService

app = FastAPI(title="Civic Lens API")
settings = get_settings()
logger = get_logger("api")

# CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # TODO: Lock down in prod
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Services
loader = ContentLoader(settings.db_path)
cache = SnapshotCache(settings.cache_dir)
outlet_agg = OutletAggregator(settings.db_path)
sentiment_agg = SentimentAggregator(settings.db_path)
bot_agg = BotAggregator(settings.db_path)
geo_agg = GeoAggregator(settings.db_path)
narrative_agg = NarrativeAggregator(settings.db_path)
review_service = ReviewService(settings.db_path)

# Analyzers - only needed for on-demand analysis triggers
bot_detector = HybridBotDetector(llm_enabled=settings.llm_enabled)
analyzer = Analyzer(llm_enabled=settings.llm_enabled)


# =============================================================================
# Health & Status
# =============================================================================

@app.get("/health")
def health():
    """Health check endpoint."""
    return {
        "status": "ok",
        "app_name": settings.app_name,
        "llm_enabled": settings.llm_enabled,
    }


@app.get("/api/cache-status")
def get_cache_status():
    """
    Returns metadata for all cached snapshots.

    Useful for displaying when data was last updated.
    """
    return {
        "snapshots": cache.get_all_metadata(),
        "cache_dir": settings.cache_dir
    }


# =============================================================================
# Pipeline Triggers (Manual)
# =============================================================================

@app.post("/api/run/etl")
def run_etl():
    """Triggers raw content loading into docs table."""
    count = loader.load_new_raw_content()
    return {"new_docs": count}


@app.post("/api/run/analysis")
def run_analysis(background_tasks: BackgroundTasks):
    """Triggers background analysis (Bot, Sentiment, Favorability)."""
    background_tasks.add_task(process_analysis_queue)
    return {"status": "Analysis queued"}


@app.post("/api/run/full-pipeline")
def run_full_pipeline(background_tasks: BackgroundTasks):
    """
    Triggers the complete analysis pipeline in background.

    Use .\\run.ps1 analyze for synchronous execution instead.
    """
    from analysis.src.scheduler.job_runner import AnalysisJobRunner

    def run_pipeline():
        runner = AnalysisJobRunner()
        runner.run_full_pipeline()

    background_tasks.add_task(run_pipeline)
    return {"status": "Full pipeline queued"}


# =============================================================================
# Data Retrieval - Individual endpoints (for direct access if needed)
# =============================================================================

import time as _time
from datetime import datetime as _datetime

# Emit a warning — but still serve — when a cached snapshot is older than this.
# The expected cadence of save_snapshots() is at least daily, so a day-old
# cache means the pipeline has silently stopped running.
STALE_CACHE_WARN_SECONDS = 24 * 60 * 60


def _get_cached_or_fallback(cache_key: str, fallback_fn, transform_fn=None):
    """Serve from cache when available, otherwise compute live.

    When the cached snapshot is older than ``STALE_CACHE_WARN_SECONDS`` this
    logs a warning so operators know the pipeline is lagging — it does not
    regenerate on the fly because aggregations can take seconds and shouldn't
    block a UI request.
    """
    cached, meta = cache.load_with_meta(cache_key)
    if cached is not None:
        if meta and meta.generated_at:
            try:
                generated_at = _datetime.fromisoformat(meta.generated_at)
                age_seconds = _time.time() - generated_at.timestamp()
                if age_seconds > STALE_CACHE_WARN_SECONDS:
                    logger.warning(
                        f"Cache '{cache_key}' is stale: generated "
                        f"{age_seconds / 3600:.1f}h ago — pipeline may be lagging"
                    )
            except ValueError:
                logger.warning(f"Cache '{cache_key}' has unparseable generated_at: {meta.generated_at}")
        return cached

    logger.warning(f"Cache miss for '{cache_key}', computing live")
    result = fallback_fn()
    if transform_fn:
        return transform_fn(result)
    return result


@app.get("/api/sentiment")
def get_public_sentiment(window: str = "24h"):
    """Returns sentiment filtered by time window. Query param: ?window=24h|7d|30d"""
    return _get_cached_or_fallback(
        f"sentiment_{window}",
        lambda: sentiment_agg.get_public_sentiment(time_window=window),
        lambda s: s.to_dict()
    )


@app.get("/api/profiles")
def get_profiles():
    """
    Returns outlet profiles with sentiment and bot rate metrics.

    Note: This endpoint includes ALL content for transparency in outlet analysis.
    Data is served from pre-computed cache when available.
    """
    return _get_cached_or_fallback(
        "profiles",
        outlet_agg.get_outlet_profiles,
        lambda profiles: [p.to_dict() for p in profiles]
    )


@app.get("/api/bot-activity")
def get_bot_activity():
    """
    Returns bot activity metrics including suspected automation rate,
    coordination patterns, and behavioral signals.

    Note: Classification is heuristic-based and may include false positives.
    Data is served from pre-computed cache when available.
    """
    return _get_cached_or_fallback(
        "bot_activity",
        bot_agg.get_bot_activity,
        lambda b: b.to_dict()
    )


@app.get("/api/narratives")
def get_narratives(window: str = "7d", limit: int = 20):
    """Returns top narratives in the time window with spread and sentiment metadata.

    The cache stores a window-keyed top-100; this endpoint slices to the
    caller's ``limit`` so any limit <= 100 is a cache hit (walkthrough 041).
    Limits above 100 skip the cache and compute live.
    Query params: ?window=24h|7d|30d|90d&limit=20
    """
    if limit <= 100:
        cached = _get_cached_or_fallback(
            f"narratives_{window}",
            lambda: narrative_agg.get_top_narratives(time_window=window, limit=100),
            lambda narratives: [n.to_dict() for n in narratives],
        )
        if isinstance(cached, list):
            return cached[:limit]
        return cached

    narratives = narrative_agg.get_top_narratives(time_window=window, limit=limit)
    return [n.to_dict() for n in narratives]


# =============================================================================
# Human review (ai_output_evals)
# =============================================================================
# Future: gate these endpoints behind admin auth. For now they are open and
# rely on the reviewer_id field for attribution.

class ReviewSubmission(BaseModel):
    ai_output_id: int
    is_correct: Optional[int] = None  # 1 correct, 0 incorrect, None unscored
    human_label: Optional[str] = None
    human_confidence: Optional[float] = None
    is_golden: bool = False
    reviewer_id: Optional[str] = None
    notes: Optional[str] = None


@app.get("/api/review/queue")
def get_review_queue(
    task: str,
    source_type: Optional[str] = None,
    confidence_max: Optional[float] = None,
    limit: int = 20,
    offset: int = 0,
):
    """Return up to ``limit`` unreviewed AI outputs ordered lowest-confidence first."""
    return review_service.get_queue(
        task_type=task,
        source_type=source_type,
        confidence_max=confidence_max,
        limit=limit,
        offset=offset,
    )


@app.post("/api/review/submit")
def submit_review(payload: ReviewSubmission):
    """Persist a human review for an AI output. Replaces any existing review."""
    try:
        return review_service.submit(
            ai_output_id=payload.ai_output_id,
            is_correct=payload.is_correct,
            human_label=payload.human_label,
            human_confidence=payload.human_confidence,
            is_golden=payload.is_golden,
            reviewer_id=payload.reviewer_id,
            notes=payload.notes,
        )
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


@app.get("/api/review/stats")
def get_review_stats(task: Optional[str] = None):
    """Per-task counts of total outputs, reviewed, correct, incorrect, golden, accuracy %."""
    return review_service.get_stats(task_type=task)


@app.get("/api/geo-sentiment")
def get_geo_sentiment(window: str = "7d"):
    """
    Returns X posts aggregated by country with sentiment scores.

    Uses explicit country_code from X API geo-tags (no heuristics). Served
    from pre-computed cache when available (walkthrough 041); falls back to
    live aggregation.
    Query param: ?window=24h|7d|30d|90d
    """
    return _get_cached_or_fallback(
        f"geo_sentiment_{window}",
        lambda: geo_agg.get_country_sentiment(time_window=window),
    )



# =============================================================================
# Background Processing (Legacy - prefer job_runner.py)
# =============================================================================

def process_analysis_queue():
    """
    Process unanalyzed documents through all analysis engines.

    Order: Bot Detection -> Sentiment -> Favorability
    Bot detection runs first so subsequent analyses can reference bot status.

    Note: For full pipeline including caching, use job_runner.py instead.
    """
    logger.info("Starting background analysis...")

    # 1. Bot Detection - runs first (social media only)
    docs = loader.get_unprocessed_docs("bot_detection")
    SOCIAL_SOURCE_TYPES = frozenset(["reddit_post", "reddit_comment", "x_post"])
    social_docs = [doc for doc in docs if doc.get("source_type") in SOCIAL_SOURCE_TYPES]
    logger.info(f"Processing {len(social_docs)} social media docs for bot detection")
    for doc in social_docs:
        result = bot_detector.analyze_full(doc['text'], doc.get('metadata'))
        output = result.to_dict()
        loader.save_ai_output(
            doc['doc_id'],
            "bot_detection",
            output,
            result.confidence
        )

    # 2. Unified Sentiment + Favorability Analysis
    docs = loader.get_unprocessed_docs("sentiment")
    logger.info(f"Processing {len(docs)} docs for unified analysis")
    for doc in docs:
        sentiment_res, favorability_res = analyzer.analyze_full(doc['text'])
        loader.save_ai_output(
            doc['doc_id'],
            "sentiment",
            sentiment_res.to_dict(),
            sentiment_res.confidence
        )
        loader.save_ai_output(
            doc['doc_id'],
            "favorability",
            favorability_res.to_dict(),
            favorability_res.overall_confidence
        )

    logger.info("Background analysis complete.")
