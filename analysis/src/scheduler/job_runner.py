#!/usr/bin/env python3
"""
Civic Lens Analysis Job Runner.

Standalone script that runs the complete analysis pipeline:
1. ETL: Load new raw content into docs table
2. Analysis: Bot detection, Sentiment, Favorability
3. Caching: Pre-compute and save aggregation snapshots

Usage:
    python -m analysis.src.scheduler.job_runner

Can be scheduled via:
- Manual: .\run.ps1 analyze
- Cron/Task Scheduler: Run this script on a schedule
"""

import sys
import os
import time
import argparse
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
from analysis.src.engine.analyzer import Analyzer
from analysis.src.engine.citation_extractor import CitationExtractor
from analysis.src.engine.claim_extractor import ClaimExtractor
from analysis.src.engine.narrative_clusterer import NarrativeClusterer
from analysis.src.engine.prompts import (
    BOT_PROMPT_VERSION, TEXT_ANALYSIS_PROMPT_VERSION, CLAIM_EXTRACTION_PROMPT_VERSION,
    BOT_SYSTEM_PROMPT, TEXT_ANALYSIS_SYSTEM_PROMPT, CLAIM_EXTRACTION_SYSTEM_PROMPT,
)
from analysis.src.reporting.aggregators import (
    OutletAggregator,
    SentimentAggregator,
    BotAggregator,
)
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
        self.outlet_agg = OutletAggregator(self.settings.db_path)
        self.sentiment_agg = SentimentAggregator(self.settings.db_path)
        self.bot_agg = BotAggregator(self.settings.db_path)

        # Resolve model_id for DB tracking
        if self.settings.llm_backend.lower() == "ollama":
            self.model_id = self.settings.ollama_model
        else:
            self.model_id = self.settings.gemini_model

        # Initialize analyzers
        self.bot_detector = HybridBotDetector(llm_enabled=self.settings.llm_enabled)
        self.analyzer = Analyzer(
            llm_enabled=self.settings.llm_enabled
        )
        self.citation_extractor = CitationExtractor(self.settings.db_path)
        self.claim_extractor = ClaimExtractor(llm_enabled=self.settings.llm_enabled)
        self.narrative_clusterer = NarrativeClusterer(self.settings.db_path)
        self.polling_scraper = PollingDataScraper() if self.settings.polling_enabled else None
    
    def run_etl(self) -> int:
        """Run ETL to load new raw content. Returns count of new docs."""
        logger.info("Step 1/7: Running ETL...")
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
    
    def run_bot_detection(self, limit: int | None = None) -> int:
        """Run bot detection on unprocessed docs scoped by configuration. Returns count processed."""
        logger.info(f"Step 2/7: Running bot detection (scope: {self.settings.run_analysis_on})...")
        source_types = self._get_target_source_types()
        batch_size = limit if limit is not None else self.settings.loader_batch_size
        docs = self.loader.get_unprocessed_docs(
            "bot_detection", 
            source_types=source_types, 
            batch_size=batch_size
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
                system_prompt=BOT_SYSTEM_PROMPT,
            )
            logger.info(
                f"[bot {i}/{total}] doc={doc['doc_id']} "
                f"label={result.label} conf={result.confidence:.2f} "
                f"reason={result.reasoning[:80] if result.reasoning else 'N/A'}"
            )
        
        logger.info(f"Bot detection complete: {total} docs processed")
        return total
    
    def run_text_analysis(self, limit: int | None = None) -> int:
        """Run combined sentiment and favorability analysis on unprocessed docs. Returns count processed."""
        logger.info(f"Step 3/7: Running text analysis (sentiment + favorability) (scope: {self.settings.run_analysis_on})...")
        source_types = self._get_target_source_types()
        
        # We look for docs that haven't been processed for sentiment
        # (Assuming sentiment and favorability process exactly the same docs in unified pipeline)
        batch_size = limit if limit is not None else self.settings.loader_batch_size
        docs = self.loader.get_unprocessed_docs(
            "sentiment", 
            source_types=source_types, 
            batch_size=batch_size
        )
        
        total = len(docs)
        logger.info(f"Processing {total} docs for unified text analysis")
        
        for i, doc in enumerate(docs, 1):
            sent_result, fav_result = self.analyzer.analyze_full(doc['text'])
            
            # Save Sentiment
            self.loader.save_ai_output(
                doc['doc_id'],
                "sentiment",
                sent_result.to_dict(),
                sent_result.confidence,
                model_id=self.model_id,
                prompt_version=TEXT_ANALYSIS_PROMPT_VERSION,
                system_prompt=TEXT_ANALYSIS_SYSTEM_PROMPT,
            )

            # Save Favorability
            self.loader.save_ai_output(
                doc['doc_id'],
                "favorability",
                fav_result.to_dict(),
                fav_result.overall_confidence,
                model_id=self.model_id,
                prompt_version=TEXT_ANALYSIS_PROMPT_VERSION,
                system_prompt=TEXT_ANALYSIS_SYSTEM_PROMPT,
            )
            
            logger.info(
                f"[text-analysis {i}/{total}] doc={doc['doc_id']} type={doc.get('source_type', 'unknown')} "
                f"sent={sent_result.label}({sent_result.confidence:.2f}) "
                f"fav={fav_result.overall_gop_stance}({fav_result.overall_confidence:.2f})"
            )
        
        logger.info(f"Text analysis complete: {total} docs processed")
        return total

    def run_citation_extraction(self, limit: int | None = None) -> int:
        """Extract cross-source citation edges. Deterministic, no LLM. Returns count of edges written."""
        logger.info(f"Step 4/7: Running citation extraction...")
        batch_size = limit if limit is not None else self.settings.loader_batch_size
        docs = self.loader.get_unprocessed_docs(
            "citations",
            source_types=None,  # citations apply to every source type
            batch_size=batch_size,
        )
        if not docs:
            logger.info("Citation extraction: no unprocessed docs")
            return 0

        processed, edges = self.citation_extractor.extract_batch(docs)
        logger.info(f"Citation extraction complete: {processed} docs, {edges} edges written")
        return edges

    def run_claim_extraction(self, limit: int | None = None) -> int:
        """Extract canonical claim statements from unprocessed docs. LLM-driven. Returns count of docs processed."""
        logger.info(f"Step 5/7: Running claim extraction (scope: {self.settings.run_analysis_on})...")
        if not self.settings.llm_enabled:
            logger.warning("Claim extraction requires llm_enabled=true; skipping")
            return 0

        source_types = self._get_target_source_types()
        batch_size = limit if limit is not None else self.settings.loader_batch_size
        docs = self.loader.get_unprocessed_docs(
            "claims", source_types=source_types, batch_size=batch_size
        )
        total = len(docs)
        if total == 0:
            logger.info("Claim extraction: no unprocessed docs")
            return 0

        logger.info(f"Processing {total} docs for claim extraction")
        for i, doc in enumerate(docs, 1):
            result = self.claim_extractor.extract(doc["text"])
            self.loader.save_ai_output(
                doc["doc_id"],
                "claims",
                result.to_dict(),
                # Overall row confidence: best claim's confidence, or 0 if none extracted.
                max((c.confidence for c in result.claims), default=0.0),
                model_id=self.model_id,
                prompt_version=CLAIM_EXTRACTION_PROMPT_VERSION,
                system_prompt=CLAIM_EXTRACTION_SYSTEM_PROMPT,
            )
            logger.info(
                f"[claims {i}/{total}] doc={doc['doc_id']} extracted={len(result.claims)}"
            )
        logger.info(f"Claim extraction complete: {total} docs processed")
        return total

    def run_narrative_clustering(self) -> dict:
        """Cluster unassigned claims into narratives. Returns summary dict."""
        logger.info("Step 6/7: Running narrative clustering...")
        return self.narrative_clusterer.run()

    def save_snapshots(self) -> dict:
        """
        Pre-compute all aggregations and save to cache.

        Saves multiple time-windowed versions of sentiment (with merged GOP favorability).
        Returns dict with counts for each cached endpoint.
        """
        logger.info("Step 7/7: Saving aggregation snapshots to cache...")
        results = {}

        # Time windows to pre-compute for time-sensitive endpoints
        time_windows = ["24h", "7d", "30d", "90d"]

        # Public Sentiment (includes merged GOP favorability) - cache all time windows
        for window in time_windows:
            sentiment = self.sentiment_agg.get_public_sentiment(time_window=window)
            self.cache.save(f"sentiment_{window}", sentiment.to_dict(), doc_count=sentiment.overview.volume)
            results[f"sentiment_{window}"] = sentiment.overview.volume

        # Outlet Profiles (not time-windowed - shows all-time data)
        profiles = self.outlet_agg.get_outlet_profiles()
        profiles_data = [p.to_dict() for p in profiles]
        self.cache.save("profiles", profiles_data, doc_count=len(profiles_data))
        results["profiles"] = len(profiles_data)

        # Bot Activity (not time-windowed)
        bot_activity = self.bot_agg.get_bot_activity()
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
    
    def run_full_pipeline(self, tasks: list[str] | None = None, limit: int | None = None) -> dict:
        """
        Run the specified analysis pipeline tasks.
        
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
            "text_analysis": 0,
            "citations": 0,
            "claims": 0,
            "narratives": {},
            "snapshots": {},
            "duration_seconds": 0,
            "status": "success"
        }

        try:
            # If no tasks specified, run all
            run_all = not tasks

            if run_all or "etl" in tasks:
                summary["etl_new_docs"] = self.run_etl()
            if run_all or "bot" in tasks:
                summary["bot_detection"] = self.run_bot_detection(limit=limit)
            if run_all or "text" in tasks:
                summary["text_analysis"] = self.run_text_analysis(limit=limit)
            if run_all or "citations" in tasks:
                summary["citations"] = self.run_citation_extraction(limit=limit)
            if run_all or "claims" in tasks:
                summary["claims"] = self.run_claim_extraction(limit=limit)
            if run_all or "narratives" in tasks:
                summary["narratives"] = self.run_narrative_clustering()
            if run_all or "snapshots" in tasks:
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
    parser = argparse.ArgumentParser(description="Civic Lens Analysis Job Runner")
    parser.add_argument("--tasks", type=str, help="Comma-separated tasks to run: etl, bot, text, citations, claims, narratives, snapshots. Defaults to all.")
    parser.add_argument("--limit", type=int, help="Limit maximum documents processed per analysis stage (useful for dev/testing)")
    args = parser.parse_args()

    tasks_to_run = [t.strip().lower() for t in args.tasks.split(",")] if args.tasks else None

    logger.info(f"Civic Lens Analysis Job Runner starting. Tasks: {args.tasks or 'all'}, Limit: {args.limit or 'default'}")
    
    runner = AnalysisJobRunner()
    summary = runner.run_full_pipeline(tasks=tasks_to_run, limit=args.limit)
    
    # Return appropriate exit code
    return 0 if summary["status"] == "success" else 1


if __name__ == "__main__":
    sys.exit(main())
