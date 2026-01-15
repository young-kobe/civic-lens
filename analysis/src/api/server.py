"""
Civic Lens Analysis API Server.

Provides endpoints for:
- ETL operations (loading raw content)
- Analysis triggers (bot detection, sentiment, favorability)
- Aggregated data retrieval with bot filtering
"""

from fastapi import FastAPI, BackgroundTasks
from fastapi.middleware.cors import CORSMiddleware

from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.etl.loader import ContentLoader
from analysis.src.engine.bot import HybridBotDetector
from analysis.src.engine.sentiment import HybridSentimentAnalyzer
from analysis.src.engine.favorability import FavorabilityAnalyzer
from analysis.src.engine.clustering import ContentClusterer
from analysis.src.reporting.aggregators import Aggregator

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

# Services - initialize with LLM when enabled
loader = ContentLoader(settings.db_path)
aggregator = Aggregator(settings.db_path)
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


# =============================================================================
# Pipeline Triggers
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


# =============================================================================
# Data Retrieval - Bot-Filtered
# =============================================================================

@app.get("/api/stories")
def get_stories():
    """
    Returns story clusters EXCLUDING bot-flagged content.
    """
    return aggregator.get_stories_filtered()


@app.get("/api/sentiment")
def get_public_sentiment():
    """
    Returns aggregated public sentiment EXCLUDING bot-flagged content.
    
    Note: Results represent sampled platform discourse, not verified population sentiment.
    """
    return aggregator.get_public_sentiment()


@app.get("/api/favorability")
def get_gop_favorability():
    """
    Returns GOP favorability metrics EXCLUDING bot-flagged content.
    
    Note: Proxy metric based on sampled media/social discourse, not polling data.
    """
    return aggregator.get_gop_favorability()


@app.get("/api/profiles")
def get_profiles():
    """
    Returns outlet profiles with sentiment and bot rate metrics.
    
    Note: This endpoint includes ALL content for transparency in outlet analysis.
    """
    return aggregator.get_outlet_profiles()


# =============================================================================
# Background Processing
# =============================================================================

def process_analysis_queue():
    """
    Process unanalyzed documents through all analysis engines.
    
    Order: Bot Detection -> Sentiment -> Favorability
    Bot detection runs first so subsequent analyses can reference bot status.
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
