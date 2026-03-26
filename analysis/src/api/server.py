"""
Civic Lens Analysis API Server.

Provides endpoints for:
- ETL operations (loading raw content)
- Analysis triggers (bot detection, sentiment, favorability)
- Aggregated data retrieval (served from pre-computed cache)
- Cache status and metadata
"""

from fastapi import FastAPI, BackgroundTasks, HTTPException
from fastapi.middleware.cors import CORSMiddleware

from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.common.cache import SnapshotCache
from analysis.src.etl.loader import ContentLoader
from analysis.src.engine.bot import HybridBotDetector
from analysis.src.engine.analyzer import Analyzer
from analysis.src.engine.clustering import ContentClusterer
from analysis.src.reporting.aggregators import Aggregator
from analysis.src.reporting.aggregators.geo import GeoAggregator

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
aggregator = Aggregator(settings.db_path)
cache = SnapshotCache(settings.cache_dir)

# Analyzers - only needed for on-demand analysis triggers
bot_detector = HybridBotDetector(llm_enabled=settings.llm_enabled)
analyzer = Analyzer(llm_enabled=settings.llm_enabled)
clusterer = ContentClusterer()


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


@app.post("/api/run/clustering")
def run_clustering():
    """Triggers document clustering."""
    docs = loader.get_all_docs_for_clustering()
    clusters = clusterer.cluster_documents(docs, threshold=settings.clustering_threshold)
    loader.save_clusters(clusters)
    return {"clusters_created": len(clusters)}


@app.post("/api/run/full-pipeline")
def run_full_pipeline(background_tasks: BackgroundTasks):
    """
    Triggers the complete analysis pipeline in background.
    
    Use .\run.ps1 analyze for synchronous execution instead.
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

def _get_cached_or_fallback(cache_key: str, fallback_fn, transform_fn=None):
    """Get data from cache, falling back to live computation if cache is empty."""
    cached = cache.load(cache_key)
    if cached is not None:
        return cached
    
    logger.warning(f"Cache miss for '{cache_key}', computing live")
    result = fallback_fn()
    if transform_fn:
        return transform_fn(result)
    return result


@app.get("/api/stories")
def get_stories(window: str = "24h", content_type: str = "all"):
    """Returns story clusters filtered by time window and content type.
    
    Query params: ?window=24h|7d|30d&content_type=all|articles|social
    """
    if content_type not in ["all", "articles", "social"]:
        raise HTTPException(status_code=400, detail="Invalid content_type. Must be 'all', 'articles', or 'social'")
        
    return _get_cached_or_fallback(
        f"stories_{window}_{content_type}",
        lambda: aggregator.get_stories(time_window=window, content_type=content_type),
        lambda stories: [s.to_dict() for s in stories]
    )


@app.get("/api/sentiment")
def get_public_sentiment(window: str = "24h"):
    """Returns sentiment filtered by time window. Query param: ?window=24h|7d|30d"""
    return _get_cached_or_fallback(
        f"sentiment_{window}",
        lambda: aggregator.get_public_sentiment(time_window=window),
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
        aggregator.get_outlet_profiles,
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
        aggregator.get_bot_activity,
        lambda b: b.to_dict()
    )


@app.get("/api/geo-sentiment")
def get_geo_sentiment(window: str = "7d"):
    """
    Returns X posts aggregated by country with sentiment scores.
    
    Uses explicit country_code from X API geo-tags (no heuristics).
    Query param: ?window=24h|7d|30d|90d
    """
    geo_agg = GeoAggregator(settings.db_path)
    return geo_agg.get_country_sentiment(time_window=window)



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
