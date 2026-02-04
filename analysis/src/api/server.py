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
from analysis.src.engine.sentiment import HybridSentimentAnalyzer
from analysis.src.engine.favorability import FavorabilityAnalyzer
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
sentiment_analyzer = HybridSentimentAnalyzer(
    model_name=settings.model_sentiment,
    llm_enabled=settings.llm_enabled
)
favorability_analyzer = FavorabilityAnalyzer(llm_enabled=settings.llm_enabled)
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
def get_stories(window: str = "24h"):
    """Returns story clusters filtered by time window. Query param: ?window=24h|7d|30d"""
    return _get_cached_or_fallback(
        f"stories_{window}",
        lambda: aggregator.get_stories(time_window=window),
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


@app.get("/api/favorability")
def get_gop_favorability(window: str = "24h"):
    """Returns favorability filtered by time window. Query param: ?window=24h|7d|30d"""
    return _get_cached_or_fallback(
        f"favorability_{window}",
        lambda: aggregator.get_gop_favorability(time_window=window),
        lambda f: f.to_dict()
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
    
    # 1. Bot Detection - runs first
    docs = loader.get_unprocessed_docs("bot_detection")
    logger.info(f"Processing {len(docs)} docs for bot detection")
    for doc in docs:
        result = bot_detector.analyze_full(doc['text'], doc.get('metadata'))
        output = result.to_dict()
        loader.save_ai_output(
            doc['doc_id'], 
            "bot_detection", 
            output, 
            result.confidence
        )
    
    # 2. Sentiment Analysis
    docs = loader.get_unprocessed_docs("sentiment")
    logger.info(f"Processing {len(docs)} docs for sentiment analysis")
    for doc in docs:
        result = sentiment_analyzer.analyze_full(doc['text'])
        output = result.to_dict()
        loader.save_ai_output(
            doc['doc_id'], 
            "sentiment", 
            output, 
            result.confidence
        )
    
    # 3. Favorability Analysis
    docs = loader.get_unprocessed_docs("favorability")
    logger.info(f"Processing {len(docs)} docs for favorability analysis")
    for doc in docs:
        result = favorability_analyzer.analyze_full(doc['text'])
        output = result.to_dict()
        loader.save_ai_output(
            doc['doc_id'], 
            "favorability", 
            output,
            result.overall_confidence
        )
    
    logger.info("Background analysis complete.")
