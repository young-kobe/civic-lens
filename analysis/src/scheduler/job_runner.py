#!/usr/bin/env python3
"""
Civic Lens Analysis Job Runner.

Standalone script that runs the complete analysis pipeline:
1. ETL: Load new raw content into docs table
2. Analysis: Bot detection, Sentiment, Favorability
3. Clustering: Group related documents
4. Caching: Pre-compute and save aggregation snapshots

Usage:
    python -m analysis.src.scheduler.job_runner

Can be scheduled via:
- Manual: .\run.ps1 analyze
- Cron/Task Scheduler: Run this script on a schedule
"""

import sys
import os
import time
from datetime import datetime, timezone
from pathlib import Path

# Ensure project root is in path
project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings
from analysis.src.common.cache import SnapshotCache
from analysis.src.etl.loader import ContentLoader
from analysis.src.engine.bot import HybridBotDetector
from analysis.src.engine.sentiment import HybridSentimentAnalyzer
from analysis.src.engine.favorability import FavorabilityAnalyzer
from analysis.src.engine.clustering import ContentClusterer
from analysis.src.engine.prompts import (
    SENTIMENT_PROMPT_VERSION, BOT_PROMPT_VERSION, FAVORABILITY_PROMPT_VERSION,
)
from analysis.src.reporting.aggregators import Aggregator
from analysis.src.etl.polling import PollingDataScraper, PollingDataError

logger = get_logger("job_runner")


class AnalysisJobRunner:
    """
    Orchestrates the complete analysis pipeline.
    
    Designed to be run as a scheduled background job.
    """
    
    def __init__(self):
        self.settings = get_settings()
        self.cache = SnapshotCache(self.settings.cache_dir)
        self.loader = ContentLoader(self.settings.db_path)
        self.aggregator = Aggregator(self.settings.db_path)
        
        # Resolve model_id for DB tracking
        if self.settings.llm_backend.lower() == "ollama":
            self.model_id = self.settings.ollama_model
        else:
            self.model_id = self.settings.gemini_model
        
        # Initialize analyzers
        self.bot_detector = HybridBotDetector(llm_enabled=self.settings.llm_enabled)
        self.sentiment_analyzer = HybridSentimentAnalyzer(
            model_name=self.settings.model_sentiment,
            llm_enabled=self.settings.llm_enabled
        )
        self.favorability_analyzer = FavorabilityAnalyzer(
            llm_enabled=self.settings.llm_enabled
        )
        self.clusterer = ContentClusterer()
        self.polling_scraper = PollingDataScraper() if self.settings.polling_enabled else None
    
    def run_etl(self) -> int:
        """Run ETL to load new raw content. Returns count of new docs."""
        logger.info("Step 1/5: Running ETL...")
        count = self.loader.load_new_raw_content()
        logger.info(f"ETL complete: {count} new documents loaded")
        return count
    
    def _get_target_source_types(self) -> list[str] | None:
        """Get the source_types filter based on configured analysis scope."""
        if self.settings.run_analysis_on == "social_media":
            return ["reddit_post", "reddit_comment", "x_post"]
        elif self.settings.run_analysis_on == "x":
            return ["x_post"]
        return None  # "all" or any other value means no filter
    
    def run_bot_detection(self) -> int:
        """Run bot detection on unprocessed docs scoped by configuration. Returns count processed."""
        logger.info(f"Step 2/5: Running bot detection (scope: {self.settings.run_analysis_on})...")
        source_types = self._get_target_source_types()
        docs = self.loader.get_unprocessed_docs(
            "bot_detection", 
            source_types=source_types, 
            batch_size=self.settings.loader_batch_size
        )
        
        total = len(docs)
        logger.info(f"Processing {total} docs for bot detection")
        
        for i, doc in enumerate(docs, 1):
            result = self.bot_detector.analyze_full(doc['text'], doc.get('metadata'))
            output = result.to_dict()
            self.loader.save_ai_output(
                doc['doc_id'], 
                "bot_detection", 
                output, 
                result.confidence,
                model_id=self.model_id,
                prompt_version=BOT_PROMPT_VERSION,
            )
            logger.info(
                f"[bot {i}/{total}] doc={doc['doc_id']} "
                f"label={result.label} conf={result.confidence:.2f} "
                f"reason={result.reasoning[:80] if result.reasoning else 'N/A'}"
            )
        
        logger.info(f"Bot detection complete: {total} docs processed")
        return total
    
    def run_sentiment_analysis(self) -> int:
        """Run sentiment analysis on unprocessed docs. Returns count processed."""
        logger.info(f"Step 3/5: Running sentiment analysis (scope: {self.settings.run_analysis_on})...")
        source_types = self._get_target_source_types()
        docs = self.loader.get_unprocessed_docs(
            "sentiment", 
            source_types=source_types, 
            batch_size=self.settings.loader_batch_size
        )
        
        total = len(docs)
        logger.info(f"Processing {total} docs for sentiment analysis")
        
        for i, doc in enumerate(docs, 1):
            result = self.sentiment_analyzer.analyze_full(doc['text'])
            output = result.to_dict()
            self.loader.save_ai_output(
                doc['doc_id'], 
                "sentiment", 
                output, 
                result.confidence,
                model_id=self.model_id,
                prompt_version=SENTIMENT_PROMPT_VERSION,
            )
            logger.info(
                f"[sentiment {i}/{total}] doc={doc['doc_id']} type={doc.get('source_type', 'unknown')} "
                f"label={result.label} conf={result.confidence:.2f} "
                f"evidence={result.evidence_spans[:3]}"
            )
        
        logger.info(f"Sentiment analysis complete: {total} docs processed")
        return total
    
    def run_favorability_analysis(self) -> int:
        """Run favorability analysis on unprocessed docs. Returns count processed."""
        logger.info(f"Step 4/5: Running favorability analysis (scope: {self.settings.run_analysis_on})...")
        source_types = self._get_target_source_types()
        docs = self.loader.get_unprocessed_docs(
            "favorability", 
            source_types=source_types, 
            batch_size=self.settings.loader_batch_size
        )
        
        total = len(docs)
        logger.info(f"Processing {total} docs for favorability analysis")
        
        for i, doc in enumerate(docs, 1):
            result = self.favorability_analyzer.analyze_full(doc['text'])
            output = result.to_dict()
            self.loader.save_ai_output(
                doc['doc_id'], 
                "favorability", 
                output,
                result.overall_confidence,
                model_id=self.model_id,
                prompt_version=FAVORABILITY_PROMPT_VERSION,
            )
            entities = result.gop_entities_found[:3]
            logger.info(
                f"[favorability {i}/{total}] doc={doc['doc_id']} type={doc.get('source_type', 'unknown')} "
                f"stance={result.overall_gop_stance} conf={result.overall_confidence:.2f} "
                f"entities={entities}"
            )
        
        logger.info(f"Favorability analysis complete: {total} docs processed")
        return total
    
    def run_clustering(self) -> int:
        """Run document clustering. Returns count of clusters created."""
        logger.info("Step 5/5: Running clustering...")
        docs = self.loader.get_all_docs_for_clustering()
        clusters = self.clusterer.cluster_documents(
            docs, 
            threshold=self.settings.clustering_threshold
        )
        self.loader.save_clusters(clusters)
        logger.info(f"Clustering complete: {len(clusters)} clusters created")
        return len(clusters)
    
    def save_snapshots(self) -> dict:
        """
        Pre-compute all aggregations and save to cache.
        
        Saves multiple time-windowed versions for stories and sentiment.
        Sentiment now includes merged GOP favorability data.
        Returns dict with counts for each cached endpoint.
        """
        logger.info("Saving aggregation snapshots to cache...")
        results = {}
        
        # Time windows to pre-compute for time-sensitive endpoints
        time_windows = ["24h", "7d", "30d", "90d"]
        
        # Stories - cache all time windows
        for window in time_windows:
            stories = self.aggregator.get_stories(time_window=window)
            stories_data = [s.to_dict() for s in stories]
            self.cache.save(f"stories_{window}", stories_data, doc_count=len(stories_data))
            results[f"stories_{window}"] = len(stories_data)
        
        # Public Sentiment (includes merged GOP favorability) - cache all time windows
        for window in time_windows:
            sentiment = self.aggregator.get_public_sentiment(time_window=window)
            self.cache.save(f"sentiment_{window}", sentiment.to_dict(), doc_count=sentiment.overview.volume)
            results[f"sentiment_{window}"] = sentiment.overview.volume
        
        # Outlet Profiles (not time-windowed - shows all-time data)
        profiles = self.aggregator.get_outlet_profiles()
        profiles_data = [p.to_dict() for p in profiles]
        self.cache.save("profiles", profiles_data, doc_count=len(profiles_data))
        results["profiles"] = len(profiles_data)
        
        # Bot Activity (not time-windowed)
        bot_activity = self.aggregator.get_bot_activity()
        self.cache.save("bot_activity", bot_activity.to_dict(), doc_count=bot_activity.overview.totalFlaggedAccounts)
        results["bot_activity"] = 1
        
        # Polling Data (fetched live from RealClearPolling)
        if self.polling_scraper:
            try:
                polling_data = self.polling_scraper.fetch_gop_favorability()
                self.cache.save("polling_gop", polling_data, doc_count=1)
                results["polling_gop"] = 1
                logger.info(f"Polling data cached: {polling_data}")
            except PollingDataError as e:
                logger.error(f"Failed to fetch polling data: {e}")
                results["polling_gop"] = 0
        
        logger.info(f"Snapshots saved: {results}")
        return results
    
    def run_full_pipeline(self) -> dict:
        """
        Run the complete analysis pipeline.
        
        Returns summary of what was processed.
        """
        start_time = time.time()
        logger.info("=" * 60)
        logger.info("STARTING ANALYSIS PIPELINE")
        logger.info(f"Time: {datetime.now(timezone.utc).isoformat()}")
        logger.info("=" * 60)
        
        summary = {
            "started_at": datetime.now(timezone.utc).isoformat(),
            "etl_new_docs": 0,
            "bot_detection": 0,
            "sentiment": 0,
            "favorability": 0,
            "clusters": 0,
            "snapshots": {},
            "duration_seconds": 0,
            "status": "success"
        }
        
        try:
            summary["etl_new_docs"] = self.run_etl()
            summary["bot_detection"] = self.run_bot_detection()
            summary["sentiment"] = self.run_sentiment_analysis()
            summary["favorability"] = self.run_favorability_analysis()
            summary["clusters"] = self.run_clustering()
            summary["snapshots"] = self.save_snapshots()
        except Exception as e:
            logger.error(f"Pipeline failed: {e}")
            summary["status"] = "failed"
            summary["error"] = str(e)
            raise
        finally:
            summary["duration_seconds"] = round(time.time() - start_time, 2)
            logger.info("=" * 60)
            logger.info("PIPELINE COMPLETE")
            logger.info(f"Duration: {summary['duration_seconds']}s")
            logger.info(f"Summary: {summary}")
            logger.info("=" * 60)
        
        return summary


def main():
    """Entry point for the job runner."""
    logger.info("Civic Lens Analysis Job Runner starting...")
    
    runner = AnalysisJobRunner()
    summary = runner.run_full_pipeline()
    
    # Return appropriate exit code
    return 0 if summary["status"] == "success" else 1


if __name__ == "__main__":
    sys.exit(main())
