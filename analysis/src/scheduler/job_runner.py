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

# Ensure project root is in path. job_runner lives at
# <repo>/analysis/src/scheduler/job_runner.py, so four parents up is the repo.
# (The previous five-parent value landed on C:\Users\kobey and only worked
# because run.ps1 sets PYTHONPATH separately. Any code joining project_root to
# a data-file path — like known_accounts.yaml — would miss the repo.)
project_root = Path(__file__).parent.parent.parent.parent
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
from analysis.src.engine.account_classifier import AccountClassifier
from analysis.src.engine.propaganda_detector import PropagandaDetector
from analysis.src.engine.prompts import (
    BOT_PROMPT_VERSION, TEXT_ANALYSIS_PROMPT_VERSION, CLAIM_EXTRACTION_PROMPT_VERSION,
    PROPAGANDA_PROMPT_VERSION,
    BOT_SYSTEM_PROMPT, TEXT_ANALYSIS_SYSTEM_PROMPT, CLAIM_EXTRACTION_SYSTEM_PROMPT,
    PROPAGANDA_SYSTEM_PROMPT,
    BOT_USER_PROMPT_TEMPLATE, TEXT_ANALYSIS_USER_PROMPT_TEMPLATE,
    CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE,
    PROPAGANDA_USER_PROMPT_TEMPLATE,
)
from analysis.src.reporting.aggregators import (
    OutletAggregator,
    SentimentAggregator,
    BotAggregator,
    NarrativeAggregator,
    GeoAggregator,
    PropagandaAggregator,
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
        self.narrative_agg = NarrativeAggregator(self.settings.db_path)
        self.geo_agg = GeoAggregator(self.settings.db_path)
        self.propaganda_agg = PropagandaAggregator(self.settings.db_path)

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
        self.propaganda_detector = (
            PropagandaDetector(llm_enabled=True)
            if self.settings.llm_enabled else PropagandaDetector(llm_enabled=False)
        )
        self.narrative_clusterer = self._build_narrative_clusterer()
        self.account_classifier = AccountClassifier(
            db_path=self.settings.db_path,
            llm_enabled=self.settings.llm_enabled and self.settings.account_classifier_llm_enabled,
        )
        self.polling_scraper = PollingDataScraper() if self.settings.polling_enabled else None

    def _build_narrative_clusterer(self) -> NarrativeClusterer:
        """Construct the narrative clusterer with the configured similarity mode.

        Embedding mode requires a working LLM client; if either the client init
        or the configured embedding call fails, the clusterer transparently
        falls back to Jaccard at runtime per claim.
        """
        mode = self.settings.narrative_similarity_mode
        embedding_client = None
        if mode == "embedding" and self.settings.llm_enabled:
            try:
                from analysis.src.llm import get_llm_client
                client = get_llm_client()
                if client.is_available:
                    embedding_client = client
                else:
                    logger.warning("Embedding mode requested but LLM client unavailable; falling back to jaccard at runtime")
            except Exception as e:
                logger.warning(f"Failed to init embedding client for narratives: {e}; falling back to jaccard")
        return NarrativeClusterer(
            db_path=self.settings.db_path,
            mode=mode,
            embedding_client=embedding_client,
            embedding_model=self.settings.narrative_embedding_model,
            jaccard_threshold=self.settings.narrative_jaccard_threshold,
            embedding_threshold=self.settings.narrative_embedding_threshold,
        )
    
    def run_etl(self) -> int:
        """Run ETL to load new raw content. Returns count of new docs."""
        logger.info("Step 1/10: Running ETL...")
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
        """Run bot detection on unprocessed docs scoped by configuration.

        For x_post docs:
          - pre-exclude authors classified as elected_official / affiliated
            (account_profiles) or whose X profile is verified_type='government'.
            Those get a bot_detection row written with label='human',
            inference_method='deterministic', indicators=['pre-excluded: ...']
            so coverage is complete but they are never flagged as bots.
          - non-excluded x_posts get their metadata enriched with the author's
            x_users_raw fields (verified, followers/following/listed/tweet_count,
            created_at) before classification.

        Walkthrough 040.
        """
        import sqlite3 as _sqlite
        logger.info(f"Step 2/10: Running bot detection (scope: {self.settings.run_analysis_on})...")
        source_types = self._get_target_source_types()
        batch_size = limit if limit is not None else self.settings.loader_batch_size
        docs = self.loader.get_unprocessed_docs(
            "bot_detection",
            source_types=source_types,
            batch_size=batch_size
        )

        total = len(docs)
        logger.info(f"Processing {total} docs for bot detection")

        # One-time helper connection for per-doc metadata lookups and pre-exclusion.
        enrich_conn = _sqlite.connect(self.settings.db_path)
        try:
            enrich_conn.execute("PRAGMA busy_timeout = 5000")
            excluded_count = 0
            processed = 0

            for i, doc in enumerate(docs, 1):
                pre_exclude_reason = None
                metadata = dict(doc.get("metadata") or {})

                if doc.get("source_type") == "x_post" and doc.get("ident"):
                    enriched, pre_exclude_reason = self._enrich_x_metadata(
                        enrich_conn, doc["ident"], metadata
                    )
                    metadata = enriched

                if pre_exclude_reason is not None:
                    # Write a coverage row so we never re-process this doc, but
                    # guarantee it isn't flagged as a bot.
                    excluded_output = {
                        "is_bot": False,
                        "label": "human",
                        "confidence": 1.0,
                        "indicators": [f"pre-excluded: {pre_exclude_reason}"],
                        "reasoning": "Pre-excluded from bot detection by policy.",
                        "inference_method": "deterministic",
                        "llm_text_likelihood": 0.0,
                    }
                    self.loader.save_ai_output(
                        doc["doc_id"],
                        "bot_detection",
                        excluded_output,
                        confidence=1.0,
                        model_id="bot-pre-exclusion",
                        prompt_version=BOT_PROMPT_VERSION,
                        system_prompt=None,  # no LLM prompt used
                        inference_method="deterministic",
                    )
                    excluded_count += 1
                    logger.info(
                        f"[bot {i}/{total}] doc={doc['doc_id']} PRE-EXCLUDED ({pre_exclude_reason})"
                    )
                    continue

                result = self.bot_detector.analyze_full(doc["text"], metadata)
                output = result.to_dict()
                self.loader.save_ai_output(
                    doc["doc_id"],
                    "bot_detection",
                    output,
                    result.confidence,
                    model_id=self.model_id,
                    prompt_version=BOT_PROMPT_VERSION,
                    system_prompt=BOT_SYSTEM_PROMPT,
                    user_prompt_template=BOT_USER_PROMPT_TEMPLATE,
                    inference_method=result.inference_method,
                )
                processed += 1
                logger.info(
                    f"[bot {i}/{total}] doc={doc['doc_id']} "
                    f"label={result.label} conf={result.confidence:.2f} "
                    f"llm_text={result.llm_text_likelihood:.2f}"
                )
        finally:
            enrich_conn.close()

        logger.info(
            f"Bot detection complete: {total} docs processed "
            f"({processed} scored, {excluded_count} pre-excluded)"
        )
        return total

    def _enrich_x_metadata(
        self,
        conn,
        tweet_id: str,
        metadata: dict,
    ) -> tuple[dict, str | None]:
        """Join x_users_raw + account_profiles for an x_post.

        Returns (enriched_metadata, pre_exclude_reason). When a reason is
        returned, the caller must skip LLM/heuristic bot detection for this
        doc and write a pre-excluded marker row instead.
        """
        row = conn.execute(
            """
            SELECT p.author_id,
                   u.verified, u.verified_type, u.followers_count, u.following_count,
                   u.listed_count, u.tweet_count, u.created_at,
                   ap.tier
            FROM x_posts_raw p
            LEFT JOIN x_users_raw u ON u.user_id = p.author_id
            LEFT JOIN account_profiles ap
              ON ap.platform = 'x' AND ap.author_id = p.author_id
            WHERE p.tweet_id = ?
            """,
            (tweet_id,),
        ).fetchone()
        if row is None:
            return metadata, None

        (author_id, verified, verified_type, followers, following,
         listed, tweet_count, user_created, tier) = row

        # Pre-exclusion rules (walkthrough 040).
        if tier in ("elected_official", "affiliated"):
            return metadata, f"tier={tier}"
        if (verified_type or "").lower() == "government":
            return metadata, "verified_type=government"

        enriched = dict(metadata)
        enriched["platform"] = "x"
        enriched["author_id"] = author_id
        enriched["verified"] = bool(verified)
        enriched["verified_type"] = verified_type or ""
        enriched["user_followers"] = followers or 0
        enriched["user_following"] = following or 0
        enriched["user_listed"] = listed or 0
        enriched["user_tweet_count"] = tweet_count or 0
        if user_created:
            enriched["user_created_at"] = user_created
        return enriched, None
    
    def run_text_analysis(self, limit: int | None = None) -> int:
        """Run combined sentiment and favorability analysis on unprocessed docs. Returns count processed."""
        logger.info(f"Step 3/10: Running text analysis (sentiment + favorability) (scope: {self.settings.run_analysis_on})...")
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
                user_prompt_template=TEXT_ANALYSIS_USER_PROMPT_TEMPLATE,
                inference_method=sent_result.inference_method,
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
                user_prompt_template=TEXT_ANALYSIS_USER_PROMPT_TEMPLATE,
                inference_method=fav_result.inference_method,
            )
            
            logger.info(
                f"[text-analysis {i}/{total}] doc={doc['doc_id']} type={doc.get('source_type', 'unknown')} "
                f"sent={sent_result.label}({sent_result.confidence:.2f}) "
                f"fav={fav_result.overall_gop_stance}({fav_result.overall_confidence:.2f})"
            )
        
        logger.info(f"Text analysis complete: {total} docs processed")
        return total

    def run_propaganda_detection(self, limit: int | None = None) -> int:
        """Detect propaganda techniques on unprocessed political docs (LLM-driven).
        Runs on every source type — propaganda techniques exist in news editorials
        AND in social posts, and honest aggregation later needs both. Returns
        count of docs processed (walkthrough 042).
        """
        logger.info(
            f"Step 4/10: Running propaganda detection (scope: {self.settings.run_analysis_on})..."
        )
        if not self.settings.llm_enabled:
            logger.warning("Propaganda detection requires llm_enabled=true; skipping")
            return 0

        source_types = self._get_target_source_types()
        batch_size = limit if limit is not None else self.settings.loader_batch_size
        docs = self.loader.get_unprocessed_docs(
            "propaganda", source_types=source_types, batch_size=batch_size,
        )
        total = len(docs)
        if total == 0:
            logger.info("Propaganda detection: no unprocessed docs")
            return 0

        logger.info(f"Processing {total} docs for propaganda detection")
        for i, doc in enumerate(docs, 1):
            result = self.propaganda_detector.detect(doc["text"])
            payload = result.to_dict()
            self.loader.save_ai_output(
                doc["doc_id"],
                "propaganda",
                payload,
                result.overall_propaganda_score,
                model_id=self.model_id,
                prompt_version=PROPAGANDA_PROMPT_VERSION,
                system_prompt=PROPAGANDA_SYSTEM_PROMPT,
                user_prompt_template=PROPAGANDA_USER_PROMPT_TEMPLATE,
                inference_method=result.inference_method,
            )
            logger.info(
                f"[propaganda {i}/{total}] doc={doc['doc_id']} "
                f"techniques={len(result.techniques)} "
                f"score={result.overall_propaganda_score:.2f}"
            )
        logger.info(f"Propaganda detection complete: {total} docs processed")
        return total

    def run_citation_extraction(self, limit: int | None = None) -> int:
        """Extract cross-source citation edges. Deterministic, no LLM. Returns count of edges written."""
        logger.info(f"Step 5/10: Running citation extraction...")
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
        logger.info(f"Step 6/10: Running claim extraction (scope: {self.settings.run_analysis_on})...")
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
                user_prompt_template=CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE,
                inference_method="llm",
            )
            logger.info(
                f"[claims {i}/{total}] doc={doc['doc_id']} extracted={len(result.claims)}"
            )
        logger.info(f"Claim extraction complete: {total} docs processed")
        return total

    def run_narrative_clustering(self) -> dict:
        """Cluster unassigned claims into narratives. Returns summary dict."""
        logger.info("Step 7/10: Running narrative clustering...")
        return self.narrative_clusterer.run()

    def run_account_classification(self) -> dict:
        """Seed curated accounts then run the LLM classifier on unclassified
        high-volume X authors. Returns a summary dict."""
        logger.info("Step 8/10: Running account tier classification...")
        yaml_path = project_root / self.settings.known_accounts_yaml
        curated = self.account_classifier.load_curated(yaml_path)
        llm_written = self.account_classifier.classify_with_llm()
        summary = {
            "curated_elected_official": curated.get("elected_official", 0),
            "curated_affiliated": curated.get("affiliated", 0),
            "llm_written": llm_written,
        }
        logger.info(f"Account classification complete: {summary}")
        return summary

    def run_account_bot_rollup(self) -> dict:
        """Aggregate per-post bot_detection rows by (platform, author_id) into
        author_bot_scores for X authors (walkthrough 040). Reddit stays at
        per-post scoring because we do not surface a reddit author field.
        Returns a summary dict."""
        import sqlite3 as _sqlite
        import json as _json
        import statistics as _stats

        logger.info("Step 9/10: Rolling up per-post bot scores to author level...")
        conn = _sqlite.connect(self.settings.db_path)
        try:
            conn.execute("PRAGMA busy_timeout = 5000")
            cursor = conn.cursor()

            # Join ai_outputs bot_detection → docs → x_posts_raw to get author_id.
            # Exclude pre-excluded rows (indicators mention 'pre-excluded') so
            # gov/electeds aren't rolled up.
            cursor.execute(
                """
                SELECT x.author_id,
                       a.output_json,
                       a.confidence,
                       a.inference_method
                FROM ai_outputs a
                JOIN docs d ON d.doc_id = a.doc_id
                JOIN x_posts_raw x ON x.tweet_id = d.ident
                WHERE a.task_type = 'bot_detection'
                  AND d.source_type = 'x_post'
                  AND COALESCE(a.inference_method, '') != 'deterministic'
                """
            )
            rows = cursor.fetchall()

            per_author: dict[str, dict] = {}
            for author_id, output_json, confidence, inf_method in rows:
                if not author_id:
                    continue
                try:
                    payload = _json.loads(output_json) if output_json else {}
                except _json.JSONDecodeError:
                    continue
                label = payload.get("label", "human")
                score_field = (payload.get("deterministic_signals") or {}).get(
                    "aggregated_score"
                )
                llm_text_lik = payload.get("llm_text_likelihood")
                bucket = per_author.setdefault(author_id, {
                    "scores": [],
                    "bot_posts": 0,
                    "suspicious_posts": 0,
                    "llm_text_liks": [],
                })
                if isinstance(score_field, (int, float)):
                    bucket["scores"].append(float(score_field))
                if isinstance(llm_text_lik, (int, float)):
                    bucket["llm_text_liks"].append(float(llm_text_lik))
                if label == "bot":
                    bucket["bot_posts"] += 1
                elif label == "suspicious":
                    bucket["suspicious_posts"] += 1

            now = int(time.time())
            written = 0
            for author_id, b in per_author.items():
                if not b["scores"]:
                    continue
                mean_score = sum(b["scores"]) / len(b["scores"])
                var = _stats.pvariance(b["scores"]) if len(b["scores"]) > 1 else 0.0
                llm_mean = (
                    sum(b["llm_text_liks"]) / len(b["llm_text_liks"])
                    if b["llm_text_liks"] else None
                )
                cursor.execute(
                    """
                    INSERT INTO author_bot_scores
                        (platform, author_id, score, variance, sample_count,
                         bot_post_count, suspicious_post_count,
                         llm_text_likelihood_mean, updated_at)
                    VALUES ('x', ?, ?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(platform, author_id) DO UPDATE SET
                        score = excluded.score,
                        variance = excluded.variance,
                        sample_count = excluded.sample_count,
                        bot_post_count = excluded.bot_post_count,
                        suspicious_post_count = excluded.suspicious_post_count,
                        llm_text_likelihood_mean = excluded.llm_text_likelihood_mean,
                        updated_at = excluded.updated_at
                    """,
                    (
                        author_id,
                        round(mean_score, 4),
                        round(var, 4),
                        len(b["scores"]),
                        b["bot_posts"],
                        b["suspicious_posts"],
                        round(llm_mean, 4) if llm_mean is not None else None,
                        now,
                    ),
                )
                written += 1
            conn.commit()
            summary = {"authors_scored": written, "post_rows_considered": len(rows)}
            logger.info(f"Bot rollup complete: {summary}")
            return summary
        finally:
            conn.close()

    def save_snapshots(self) -> dict:
        """
        Pre-compute all aggregations and save to cache.

        Saves multiple time-windowed versions of sentiment (with merged GOP favorability).
        Returns dict with counts for each cached endpoint.
        """
        logger.info("Step 10/10: Saving aggregation snapshots to cache...")
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

        # Narratives — cache top 100 per time window; the API slices to the
        # caller's requested limit (walkthrough 041). Previously the key was
        # hardcoded to limit=20 so any other limit in the query param missed
        # cache and triggered live aggregation.
        NARRATIVE_CACHE_SIZE = 100
        for window in time_windows:
            narratives = self.narrative_agg.get_top_narratives(
                time_window=window, limit=NARRATIVE_CACHE_SIZE,
            )
            data = [n.to_dict() for n in narratives]
            self.cache.save(f"narratives_{window}", data, doc_count=len(data))
            results[f"narratives_{window}"] = len(data)

        # Geo sentiment — cache per time window (walkthrough 041). Previously
        # the endpoint recomputed live on every UI request because no snapshot
        # was ever written.
        for window in time_windows:
            geo = self.geo_agg.get_country_sentiment(time_window=window)
            total_posts = int(geo.get("total_posts", 0)) if isinstance(geo, dict) else 0
            self.cache.save(f"geo_sentiment_{window}", geo, doc_count=total_posts)
            results[f"geo_sentiment_{window}"] = total_posts

        # Propaganda — cache per time window (walkthrough 043).
        for window in time_windows:
            propaganda = self.propaganda_agg.get_propaganda_overview(time_window=window)
            self.cache.save(
                f"propaganda_{window}",
                propaganda.to_dict(),
                doc_count=propaganda.total_eligible_docs,
            )
            results[f"propaganda_{window}"] = propaganda.flagged_docs

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
            "propaganda": 0,
            "citations": 0,
            "claims": 0,
            "narratives": {},
            "account_classification": {},
            "bot_rollup": {},
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
            if run_all or "propaganda" in tasks:
                summary["propaganda"] = self.run_propaganda_detection(limit=limit)
            if run_all or "citations" in tasks:
                summary["citations"] = self.run_citation_extraction(limit=limit)
            if run_all or "claims" in tasks:
                summary["claims"] = self.run_claim_extraction(limit=limit)
            if run_all or "narratives" in tasks:
                summary["narratives"] = self.run_narrative_clustering()
            if run_all or "accounts" in tasks:
                summary["account_classification"] = self.run_account_classification()
            if run_all or "bot_rollup" in tasks:
                summary["bot_rollup"] = self.run_account_bot_rollup()
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
    parser.add_argument("--tasks", type=str, help="Comma-separated tasks to run: etl, bot, text, propaganda, citations, claims, narratives, accounts, bot_rollup, snapshots. Defaults to all.")
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
