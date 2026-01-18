"""
Aggregator for Civic Lens reporting.

Provides filtered aggregations that exclude bot-flagged content from:
- Public sentiment metrics
- GOP favorability metrics  
- Story clusters

Satisfies requirement: bot-flagged content should not appear in sentiment,
favorability, or cluster API returns.
"""

import contextlib
import datetime
import json
import sqlite3
import time
from typing import Any, Dict, List, Optional, Set

from analysis.src.common.logger import get_logger
from analysis.src.reporting.models import (
    OutletProfile,
    MomentumData,
    SourceMixItem,
    TimelinePoint,
    ArticlePreview,
    StoryCluster,
    SentimentOverview,
    SentimentDistribution,
    PlatformSentiment,
    PublicSentimentResult,
    FavorabilityOverall,
    TrendPoint,
    PlatformFavorability,
    PollingSocialComparison,
    GOPFavorabilityResult,
    BotOverview,
    NarrativeAmplification,
    CoordinationStats,
    BehavioralSignals,
    BotActivityData,
)

logger = get_logger(__name__)


class Aggregator:
    """
    Aggregates analysis results with bot filtering.
    
    All public-facing metrics exclude documents flagged as 'bot'.
    """
    
    def __init__(self, db_path: str):
        self.db_path = db_path
        
    @contextlib.contextmanager
    def _get_conn(self):
        conn = sqlite3.connect(self.db_path)
        try:
            yield conn
        finally:
            conn.close()
    
    def _get_bot_flagged_doc_ids(self) -> Set[int]:
        """
        Get all doc_ids that have been flagged as 'bot'.
        
        These documents are excluded from sentiment, favorability, and cluster aggregations.
        """
        with self._get_conn() as conn:
            cursor = conn.cursor()
            
            # Check if ai_outputs table exists
            cursor.execute("""
                SELECT name FROM sqlite_master 
                WHERE type='table' AND name='ai_outputs'
            """)
            if not cursor.fetchone():
                logger.warning("ai_outputs table does not exist - no bot filtering applied")
                return set()
            
            cursor.execute("""
                SELECT doc_id, output_json
                FROM ai_outputs
                WHERE task_type = 'bot_detection'
            """)
            
            bot_docs = set()
            for doc_id, output_json in cursor.fetchall():
                try:
                    data = json.loads(output_json)
                    # Exclude if labeled as 'bot' (not 'suspicious' - those may be human)
                    if data.get('label') == 'bot' or data.get('is_bot') is True:
                        bot_docs.add(doc_id)
                except json.JSONDecodeError:
                    continue
            
            logger.info(f"Found {len(bot_docs)} bot-flagged documents to exclude")
            return bot_docs

    # -------------------------------------------------------------------------
    # Outlet Profiles
    # -------------------------------------------------------------------------

    def get_outlet_profiles(self) -> List[OutletProfile]:
        """
        Aggregates sentiment and bot scores by domain/subreddit.
        Includes ALL content (bots included) for transparency in outlet analysis.
        """
        with self._get_conn() as conn:
            cursor = conn.cursor()
            
            cursor.execute("""
                SELECT d.domain_or_subreddit, a.task_type, a.output_json
                FROM ai_outputs a
                JOIN docs d ON a.doc_id = d.doc_id
                WHERE a.task_type IN ('sentiment', 'bot_detection')
            """)
            
            rows = cursor.fetchall()
            profiles = self._aggregate_outlet_data(rows)
            return self._format_outlet_profiles(profiles)

    def _aggregate_outlet_data(self, rows: List[tuple]) -> Dict[str, Dict[str, Any]]:
        """Aggregate raw outlet data from DB rows."""
        profiles: Dict[str, Dict[str, Any]] = {}
        
        for domain, task, output_json in rows:
            if domain not in profiles:
                profiles[domain] = {
                    "sentiment_score": 0, 
                    "sentiment_count": 0, 
                    "bot_flags": 0, 
                    "total_scanned": 0
                }
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            if task == 'sentiment':
                label = data.get('label')
                val = 1 if label == 'POSITIVE' else (-1 if label == 'NEGATIVE' else 0)
                profiles[domain]["sentiment_score"] += val
                profiles[domain]["sentiment_count"] += 1
                
            elif task == 'bot_detection':
                label = data.get('label')
                if label in ['bot', 'suspicious']:
                    profiles[domain]["bot_flags"] += 1
                profiles[domain]["total_scanned"] += 1
        
        return profiles

    def _format_outlet_profiles(self, profiles: Dict[str, Dict[str, Any]]) -> List[OutletProfile]:
        """Format aggregated outlet data into response objects."""
        results = []
        for domain, stats in profiles.items():
            avg_sentiment = stats["sentiment_score"] / stats["sentiment_count"] if stats["sentiment_count"] > 0 else 0
            bot_rate = stats["bot_flags"] / stats["total_scanned"] if stats["total_scanned"] > 0 else 0
            
            profile = OutletProfile(
                outlet=domain,
                avg_sentiment=round(avg_sentiment, 3),
                bot_rate=round(bot_rate, 3),
                volume=stats["sentiment_count"],
                total_scanned=stats["total_scanned"],
            )
            results.append(profile)
            
        return sorted(results, key=lambda x: x.bot_rate, reverse=True)

    # -------------------------------------------------------------------------
    # Story Clusters
    # -------------------------------------------------------------------------

    def get_stories(self) -> List[StoryCluster]:
        """
        Returns rich cluster data matching frontend expectations.
        Excludes bot-flagged content.
        """
        bot_docs = self._get_bot_flagged_doc_ids()
        
        with self._get_conn() as conn:
            cursor = conn.cursor()
            raw_clusters = self._fetch_cluster_data(cursor, bot_docs)
            
        return self._format_story_clusters(raw_clusters)

    def _fetch_cluster_data(self, cursor: sqlite3.Cursor, bot_docs: Set[int]) -> Dict[int, Dict[str, Any]]:
        """Fetch and organize cluster data from database."""
        cursor.execute("""
            SELECT 
                c.cluster_id, 
                c.name, 
                c.summary,
                d.doc_id, 
                d.published_at, 
                d.source_type, 
                d.ident, 
                d.title,
                d.metadata_json
            FROM clusters c
            JOIN cluster_assignments ca ON c.cluster_id = ca.cluster_id
            JOIN docs d ON ca.doc_id = d.doc_id
        """)
        
        clusters: Dict[int, Dict[str, Any]] = {}
        now = time.time()
        
        for row in cursor.fetchall():
            c_id, c_name, c_summary, d_id, pub_at, src_type, ident, title, meta_json = row
            
            if d_id in bot_docs:
                continue
                
            if c_id not in clusters:
                clusters[c_id] = {
                    "id": c_id,
                    "title": c_name,
                    "summary_text": c_summary,
                    "doc_count": 0,
                    "docs": [],
                    "sources": {},
                    "timeline": {},
                    "now": now,
                }
            
            cluster = clusters[c_id]
            cluster["doc_count"] += 1
            
            # Parse snippet from metadata
            snippet = self._extract_snippet(meta_json)
            
            cluster["docs"].append({
                "id": d_id,
                "title": title or "Untitled",
                "source": ident,
                "snippet": snippet,
                "published_at": pub_at,
            })
            
            # Track source mix
            source_type = src_type or 'other'
            cluster["sources"][source_type] = cluster["sources"].get(source_type, 0) + 1
            
            # Track timeline by day
            if pub_at:
                day = datetime.datetime.fromtimestamp(pub_at).strftime('%a')
                cluster["timeline"][day] = cluster["timeline"].get(day, 0) + 1
        
        return clusters

    def _extract_snippet(self, meta_json: Optional[str]) -> str:
        """Extract description snippet from metadata JSON."""
        if not meta_json:
            return ''
        try:
            return json.loads(meta_json).get('description', '')
        except json.JSONDecodeError:
            return ''

    def _format_story_clusters(self, clusters: Dict[int, Dict[str, Any]]) -> List[StoryCluster]:
        """Format raw cluster data into StoryCluster response objects."""
        results = []
        
        for c_id, c in clusters.items():
            momentum = self._compute_momentum(c["docs"], c["now"])
            source_mix = self._format_source_mix(c["sources"])
            timeline = self._format_timeline(c["timeline"])
            articles = self._format_articles(c["docs"])
            summary_points = [d["title"] for d in sorted(c["docs"], key=lambda x: x["published_at"] or 0, reverse=True)[:3]]
            
            cluster = StoryCluster(
                id=c_id,
                title=c["title"] or "Unnamed Cluster",
                articleCount=c["doc_count"],
                momentum=momentum,
                primarySources=list(c["sources"].keys())[:3],
                summary=summary_points,
                keyClaims=[],
                entities={"people": [], "organizations": [], "locations": []},
                sourceMix=source_mix,
                timeline=timeline,
                articles=articles,
            )
            results.append(cluster)
        
        return sorted(results, key=lambda x: x.articleCount, reverse=True)[:20]

    def _compute_momentum(self, docs: List[Dict[str, Any]], now: float) -> MomentumData:
        """Compute momentum (24h delta) for a cluster."""
        one_day_ago = now - 86400
        
        count_24h = sum(1 for d in docs if d["published_at"] and d["published_at"] > one_day_ago)
        count_prev_24h = sum(
            1 for d in docs 
            if d["published_at"] and d["published_at"] <= one_day_ago and d["published_at"] > (one_day_ago - 86400)
        )
        
        if count_prev_24h > 0:
            delta = ((count_24h - count_prev_24h) / count_prev_24h) * 100
        elif count_24h > 0:
            delta = 100.0  # New emerging topic
        else:
            delta = 0.0
        
        return MomentumData(delta24h=round(delta, 1))

    def _format_source_mix(self, sources: Dict[str, int]) -> List[SourceMixItem]:
        """Format source distribution into SourceMixItem list."""
        return [
            SourceMixItem(name=k.capitalize(), value=v, type=k)
            for k, v in sources.items()
        ]

    def _format_timeline(self, timeline_data: Dict[str, int]) -> List[TimelinePoint]:
        """Format timeline data with proper day ordering."""
        days_order = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
        return [
            TimelinePoint(date=day, value=timeline_data[day])
            for day in days_order if day in timeline_data
        ]

    def _format_articles(self, docs: List[Dict[str, Any]]) -> List[ArticlePreview]:
        """Format top 3 recent articles as previews."""
        sorted_docs = sorted(docs, key=lambda x: x["published_at"] or 0, reverse=True)
        return [
            ArticlePreview(
                id=d["id"],
                title=d["title"],
                source=d["source"],
                snippet=d["snippet"][:100] + "..." if len(d["snippet"]) > 100 else d["snippet"],
                reason="Recent coverage",
            )
            for d in sorted_docs[:3]
        ]

    # -------------------------------------------------------------------------
    # Public Sentiment
    # -------------------------------------------------------------------------

    def get_public_sentiment(self) -> PublicSentimentResult:
        """
        Aggregate sentiment EXCLUDING bot-flagged content.
        Breakdowns by platform for the frontend.
        """
        bot_docs = self._get_bot_flagged_doc_ids()
        
        with self._get_conn() as conn:
            cursor = conn.cursor()
            
            cursor.execute("""
                SELECT a.doc_id, a.output_json, a.confidence, d.source_type
                FROM ai_outputs a
                JOIN docs d ON a.doc_id = d.doc_id
                WHERE a.task_type = 'sentiment'
            """)
            
            rows = cursor.fetchall()
        
        return self._process_sentiment_data(rows, bot_docs)

    def _process_sentiment_data(self, rows: List[tuple], bot_docs: Set[int]) -> PublicSentimentResult:
        """Process sentiment data into structured response."""
        distribution = {"POSITIVE": 0, "NEGATIVE": 0, "NEUTRAL": 0, "MIXED": 0}
        by_platform: Dict[str, Dict[str, int]] = {}
        total_confidence = 0.0
        count = 0
        excluded_bot_count = 0
        
        for doc_id, output_json, confidence, source_type in rows:
            if doc_id in bot_docs:
                excluded_bot_count += 1
                continue
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            label = data.get('label', 'NEUTRAL')
            if label in distribution:
                distribution[label] += 1
            
            # Platform breakdown
            platform = source_type or 'unknown'
            if platform not in by_platform:
                by_platform[platform] = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
            
            key_map = {"POSITIVE": "positive", "NEGATIVE": "negative", "NEUTRAL": "neutral", "MIXED": "mixed"}
            if label in key_map:
                by_platform[platform][key_map[label]] += 1
            
            total_confidence += confidence or 0.5
            count += 1
        
        # Calculate net score
        total = sum(distribution.values())
        net_score = ((distribution["POSITIVE"] - distribution["NEGATIVE"]) / total * 100) if total > 0 else 0
        
        # Format platform breakdown
        platform_breakdown = self._format_platform_sentiment(by_platform)
        
        return PublicSentimentResult(
            overview=SentimentOverview(
                netScore=round(net_score, 1),
                volume=count,
                coverage="medium",
                confidence="medium",
            ),
            distribution=SentimentDistribution(
                strongPositive=distribution["POSITIVE"],
                mildPositive=0,
                neutral=distribution["NEUTRAL"],
                mildNegative=0,
                strongNegative=distribution["NEGATIVE"],
            ),
            byPlatform=platform_breakdown,
            disclaimer="Represents sampled platform discourse, not verified population sentiment",
            excluded_bot_content=excluded_bot_count,
        )

    def _format_platform_sentiment(self, by_platform: Dict[str, Dict[str, int]]) -> List[PlatformSentiment]:
        """Format platform sentiment breakdown."""
        return [
            PlatformSentiment(
                platform=platform,
                positive=counts["positive"],
                negative=counts["negative"],
                neutral=counts["neutral"],
                volume=sum(counts.values()),
            )
            for platform, counts in by_platform.items()
        ]

    # -------------------------------------------------------------------------
    # GOP Favorability
    # -------------------------------------------------------------------------

    def get_gop_favorability(self) -> GOPFavorabilityResult:
        """
        Aggregate GOP favorability EXCLUDING bot-flagged content.
        Includes trend data and platform breakdown.
        """
        bot_docs = self._get_bot_flagged_doc_ids()
        
        with self._get_conn() as conn:
            cursor = conn.cursor()
            
            cursor.execute("""
                SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at
                FROM ai_outputs a
                JOIN docs d ON a.doc_id = d.doc_id
                WHERE a.task_type = 'favorability'
            """)
            
            rows = cursor.fetchall()
        
        return self._process_favorability_data(rows, bot_docs)

    def _process_favorability_data(self, rows: List[tuple], bot_docs: Set[int]) -> GOPFavorabilityResult:
        """Process favorability data into structured response."""
        distribution = {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0}
        by_platform: Dict[str, Dict[str, int]] = {}
        daily_net: Dict[str, Dict[str, Any]] = {}
        count = 0
        excluded_bot_count = 0
        
        for doc_id, output_json, confidence, source_type, pub_at in rows:
            if doc_id in bot_docs:
                excluded_bot_count += 1
                continue
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            stance = data.get('overall_gop_stance', 'neutral')
            if stance not in distribution:
                stance = 'neutral'
            distribution[stance] += 1
            
            # Platform breakdown (normalize reddit types)
            platform = self._normalize_platform(source_type)
            if platform not in by_platform:
                by_platform[platform] = {"favorable": 0, "unfavorable": 0, "neutral": 0}
            if stance in by_platform[platform]:
                by_platform[platform][stance] += 1
            
            # Track daily trend
            if pub_at:
                self._update_daily_trend(daily_net, pub_at, stance)
            
            count += 1
        
        # Compute metrics
        total = sum(distribution.values())
        net_favorability = ((distribution["favorable"] - distribution["unfavorable"]) / total * 100) if total > 0 else 0
        
        # Format results
        trend_data = self._format_trend_data(daily_net)
        platform_data = self._format_platform_favorability(by_platform)
        
        return GOPFavorabilityResult(
            overall=FavorabilityOverall(
                favorable=round((distribution["favorable"] / total) * 100, 1) if total else 0,
                unfavorable=round((distribution["unfavorable"] / total) * 100, 1) if total else 0,
                neutral=round((distribution["neutral"] / total) * 100, 1) if total else 0,
                netFavorability=round(net_favorability, 1),
                sampleSize=count,
                sourceCount=len(by_platform),
                lastUpdated=datetime.datetime.now().isoformat(),
                dateRange="All time",
            ),
            trend=trend_data[-30:],
            trendAnnotations=[],
            byPlatform=platform_data,
            pollingVsSocial=PollingSocialComparison(
                polling={"favorable": 42, "unfavorable": 53, "neutral": 5},
                social={
                    "favorable": round((distribution["favorable"] / total) * 100, 1) if total else 0,
                    "unfavorable": round((distribution["unfavorable"] / total) * 100, 1) if total else 0,
                    "neutral": round((distribution["neutral"] / total) * 100, 1) if total else 0,
                },
            ),
        )

    def _normalize_platform(self, source_type: Optional[str]) -> str:
        """Normalize platform names."""
        platform = source_type or 'unknown'
        if platform in ('reddit_post', 'reddit_comment'):
            return 'reddit'
        return platform

    def _update_daily_trend(self, daily_net: Dict[str, Dict[str, Any]], pub_at: int, stance: str) -> None:
        """Update daily trend accumulator."""
        date_str = datetime.datetime.fromtimestamp(pub_at).strftime('%Y-%m-%d')
        if date_str not in daily_net:
            daily_net[date_str] = {"score": 0, "count": 0}
        
        val = 1 if stance == 'favorable' else (-1 if stance == 'unfavorable' else 0)
        daily_net[date_str]["score"] += val
        daily_net[date_str]["count"] += 1

    def _format_trend_data(self, daily_net: Dict[str, Dict[str, Any]]) -> List[TrendPoint]:
        """Format daily trend data."""
        return [
            TrendPoint(
                date=date,
                value=round((stats["score"] / stats["count"]) * 100, 1) if stats["count"] > 0 else 0,
            )
            for date, stats in sorted(daily_net.items())
        ]

    def _format_platform_favorability(self, by_platform: Dict[str, Dict[str, int]]) -> List[PlatformFavorability]:
        """Format platform favorability breakdown."""
        return [
            PlatformFavorability(
                group=platform.capitalize(),
                favorable=stats["favorable"],
                unfavorable=stats["unfavorable"],
                neutral=stats["neutral"],
            )
            for platform, stats in by_platform.items()
        ]

    # -------------------------------------------------------------------------
    # Bot Activity
    # -------------------------------------------------------------------------

    def get_bot_activity(self) -> BotActivityData:
        """
        Aggregate bot activity metrics.
        Provides overview, narrative amplification, coordination stats, and behavioral signals.
        """
        with self._get_conn() as conn:
            cursor = conn.cursor()
            bot_data = self._fetch_bot_detection_data(cursor)
        
        return self._format_bot_activity(bot_data)

    def _fetch_bot_detection_data(self, cursor: sqlite3.Cursor) -> Dict[str, Any]:
        """Fetch bot detection data from database."""
        cursor.execute("""
            SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.domain_or_subreddit, d.published_at
            FROM ai_outputs a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type = 'bot_detection'
        """)
        
        total_scanned = 0
        bot_count = 0
        suspicious_count = 0
        by_cluster: Dict[str, int] = {}
        indicators_frequency: Dict[str, int] = {}
        hourly_distribution: Dict[int, int] = {}
        
        bot_docs_data = []
        
        for doc_id, output_json, confidence, source_type, domain, pub_at in cursor.fetchall():
            total_scanned += 1
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            label = data.get('label', 'human')
            
            if label == 'bot':
                bot_count += 1
                bot_docs_data.append({
                    "doc_id": doc_id,
                    "data": data,
                    "domain": domain,
                    "pub_at": pub_at,
                })
                
                # Track cluster/domain
                by_cluster[domain] = by_cluster.get(domain, 0) + 1
                
                # Track indicators
                for indicator in data.get('indicators', []):
                    indicators_frequency[indicator] = indicators_frequency.get(indicator, 0) + 1
                
                # Track hourly posting
                if pub_at:
                    hour = datetime.datetime.fromtimestamp(pub_at).hour
                    hourly_distribution[hour] = hourly_distribution.get(hour, 0) + 1
                    
            elif label == 'suspicious':
                suspicious_count += 1
        
        return {
            "total_scanned": total_scanned,
            "bot_count": bot_count,
            "suspicious_count": suspicious_count,
            "by_cluster": by_cluster,
            "indicators_frequency": indicators_frequency,
            "hourly_distribution": hourly_distribution,
            "bot_docs_data": bot_docs_data,
        }

    def _format_bot_activity(self, bot_data: Dict[str, Any]) -> BotActivityData:
        """Format bot detection data into BotActivityData response."""
        total_scanned = bot_data["total_scanned"]
        bot_count = bot_data["bot_count"]
        suspicious_count = bot_data["suspicious_count"]
        by_cluster = bot_data["by_cluster"]
        indicators_frequency = bot_data["indicators_frequency"]
        hourly_distribution = bot_data["hourly_distribution"]
        
        # Compute automation rate
        automation_rate = (bot_count / total_scanned * 100) if total_scanned > 0 else 0
        
        # Top clusters (domains/subreddits with most bot activity)
        top_clusters = sorted(by_cluster.items(), key=lambda x: x[1], reverse=True)[:5]
        
        # Compute coordination index (simplified: based on timing patterns)
        coordination_index = self._compute_coordination_index(hourly_distribution)
        
        # Top indicators as narrative amplification proxies
        narratives = self._compute_narrative_amplification(indicators_frequency, bot_data["bot_docs_data"])
        
        # Build behavioral signals
        posting_cadence = [
            {"day": 0, "hour": hour, "value": count}
            for hour, count in sorted(hourly_distribution.items())
        ]
        
        return BotActivityData(
            overview=BotOverview(
                suspectedAutomationRate=round(automation_rate, 1),
                coordinationIndex=round(coordination_index, 2),
                topClusters=[c[0] for c in top_clusters],
                totalFlaggedAccounts=bot_count + suspicious_count,
                confidence="medium" if total_scanned > 100 else "low",
            ),
            narrativeAmplification=narratives,
            coordinationStats=CoordinationStats(
                burstTimingSimilarity=round(coordination_index, 2),
                accountReuse=0.0,
                identicalTextPairs=0,
                avgPostsPerSuspectedAccount=1.0,
            ),
            behavioralSignals=BehavioralSignals(
                accountAgeDistribution=[],
                postingCadence=posting_cadence,
                copyPasteSimilarity={"high": 0, "medium": 0, "low": 0},
                linkDomainConcentration=[
                    {"domain": c[0], "percentage": round(c[1] / bot_count * 100, 1) if bot_count > 0 else 0}
                    for c in top_clusters[:5]
                ],
            ),
        )

    def _compute_coordination_index(self, hourly_distribution: Dict[int, int]) -> float:
        """Compute coordination index based on posting time concentration."""
        if not hourly_distribution:
            return 0.0
        
        total = sum(hourly_distribution.values())
        if total == 0:
            return 0.0
        
        # Higher concentration in fewer hours = higher coordination
        max_hour_count = max(hourly_distribution.values())
        return max_hour_count / total

    def _compute_narrative_amplification(
        self, 
        indicators_frequency: Dict[str, int], 
        bot_docs_data: List[Dict[str, Any]]
    ) -> List[NarrativeAmplification]:
        """Compute narrative amplification from bot indicators."""
        if not indicators_frequency:
            return []
        
        # Group by top indicators
        top_indicators = sorted(indicators_frequency.items(), key=lambda x: x[1], reverse=True)[:3]
        
        narratives = []
        for idx, (indicator, count) in enumerate(top_indicators):
            narratives.append(NarrativeAmplification(
                id=idx + 1,
                narrative=indicator,
                confidence="medium" if count > 5 else "low",
                examplePosts=[],  # Would need to fetch actual post content
                topHashtags=[],
                topPhrases=[indicator],
                targets=[],
                suspectedBotVolume=count,
                whyFlagged=[indicator],
            ))
        
        return narratives
