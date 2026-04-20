"""
Public sentiment aggregator.

Aggregates sentiment metrics excluding bot-flagged content.
Provides separate breakdowns for Social Media vs News Outlets.
Includes merged GOP favorability data.
"""

import json
import re
from collections import Counter
from datetime import datetime, timedelta
from typing import Any, Dict, List, Optional, Set, Tuple

from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
    get_bot_flagged_doc_ids,
    fetch_task_rows,
)
from analysis.src.reporting.aggregators.constants import (
    SOCIAL_PLATFORMS, NEWS_PLATFORMS, TOPIC_KEYWORDS,
    STRONG_CONFIDENCE_THRESHOLD,
)
from analysis.src.reporting.models import (
    SentimentOverview,
    SentimentDistribution,
    PlatformSentiment,
    PublicSentimentResult,
    TopicSentiment,
    ClassificationSample,
    TimeWindowSentiment,
)


class SentimentAggregator:
    """Aggregates public sentiment metrics with merged GOP favorability data."""
    
    def __init__(self, db_path: str):
        self.db_path = db_path
        settings = get_settings()
        self.cache = SnapshotCache(settings.cache_dir)
    
    def _categorize_platform(self, source_type: str) -> str:
        """Categorize source_type into Social Media or News Outlets."""
        if source_type in SOCIAL_PLATFORMS:
            return "Social Media"
        elif source_type in NEWS_PLATFORMS:
            return "News Outlets"
        return "Other"
    
    def _extract_topic(self, title: str) -> str:
        """Extract topic from title using keyword matching."""
        if not title:
            return "General"
        title_lower = title.lower()
        for topic, keywords in TOPIC_KEYWORDS.items():
            if any(kw in title_lower for kw in keywords):
                return topic
        return "General"
    
    def _get_time_bucket(self, published_at: Any, now: datetime) -> str:
        """Get time bucket for a document based on published_at."""
        try:
            # Handle different formats
            if isinstance(published_at, (int, float)):
                # Unix timestamp
                pub_dt = datetime.fromtimestamp(published_at)
            elif isinstance(published_at, str):
                # ISO format string
                pub_dt = datetime.fromisoformat(published_at.replace('Z', '+00:00'))
                if pub_dt.tzinfo:
                    pub_dt = pub_dt.replace(tzinfo=None)
            else:
                return "Unknown"
            
            delta = now - pub_dt
            if delta.days < 1:
                return "24 hours"
            elif delta.days < 7:
                return "7 days"
            elif delta.days < 30:
                return "30 days"
            else:
                return "90+ days"
        except (ValueError, TypeError, OSError):
            return "Unknown"
    
    def get_public_sentiment(
        self,
        time_window: str = "24h",
        bot_docs: Optional[Set[int]] = None,
    ) -> PublicSentimentResult:
        """
        Aggregate sentiment EXCLUDING bot-flagged content AND rows whose
        confidence is below ``aggregation_min_confidence`` (walkthrough 039).
        Includes merged GOP favorability data.

        Args:
            time_window: Filter by time window (24h, 7d, 30d, 90d, all)
            bot_docs: Optional pre-computed bot-flagged doc id set. When None,
                the aggregator queries it itself. job_runner passes a cached
                set so the query doesn't run once per aggregator.
        """
        min_conf = get_settings().aggregation_min_confidence
        if bot_docs is None:
            bot_docs = get_bot_flagged_doc_ids(self.db_path, min_confidence=min_conf)
        cutoff = get_time_cutoff(time_window)

        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()

            sentiment_rows = fetch_task_rows(
                cursor,
                "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at, d.title, d.domain_or_subreddit, d.ident, d.text",
                task_type="sentiment",
                cutoff=cutoff,
                min_confidence=min_conf,
            )
            favorability_rows = fetch_task_rows(
                cursor,
                "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at",
                task_type="favorability",
                cutoff=cutoff,
                min_confidence=min_conf,
            )

        result = self._process_sentiment_data(sentiment_rows, bot_docs)
        self._merge_favorability_data(result, favorability_rows, bot_docs)
        return result

    def _process_sentiment_data(self, rows: List[tuple], bot_docs: Set[int]) -> PublicSentimentResult:
        """Process sentiment data into structured response with social vs news separation."""
        accum = self._aggregate_sentiment_rows(rows, bot_docs)
        return self._build_sentiment_result(accum)

    def _aggregate_sentiment_rows(self, rows: List[tuple], bot_docs: Set[int]) -> Dict[str, Any]:
        """Parse raw sentiment rows into intermediate aggregation structures."""
        accum: Dict[str, Any] = {
            "strong_pos": 0, "mild_pos": 0, "strong_neg": 0, "mild_neg": 0,
            "neutral": 0, "mixed": 0, "count": 0, "excluded_bots": 0,
            "social": {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0},
            "news": {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0},
            "by_platform": {}, "by_topic": {}, "by_time": {},
            "topic_samples": {},
        }
        now = datetime.now()
        label_map = {"POSITIVE": "positive", "NEGATIVE": "negative", "NEUTRAL": "neutral", "MIXED": "neutral"}

        for doc_id, output_json, confidence, source_type, published_at, title, domain_or_subreddit, ident, text in rows:
            if doc_id in bot_docs:
                accum["excluded_bots"] += 1
                continue
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue

            label = data.get('label', 'NEUTRAL')
            conf = float(data.get('confidence', confidence or 0.5))
            self._count_sentiment_strength(accum, label, conf)

            label_key = label_map.get(label, "neutral")
            platform = source_type or 'unknown'
            category = self._categorize_platform(platform)

            if category == "Social Media":
                accum["social"][label_key] += 1
            elif category == "News Outlets":
                accum["news"][label_key] += 1

            self._increment_bucket(accum["by_platform"], category, label_key)
            topic = self._extract_topic(title)
            self._increment_bucket(accum["by_topic"], topic, label_key)
            self._increment_bucket(accum["by_time"], self._get_time_bucket(published_at, now), label_key)
            self._collect_topic_sample(
                accum["topic_samples"], topic, doc_id, label, conf,
                data, title, source_type, published_at, domain_or_subreddit, ident, text,
            )
            accum["count"] += 1

        return accum

    @staticmethod
    def _count_sentiment_strength(accum: Dict[str, Any], label: str, conf: float) -> None:
        """Increment mild/strong positive/negative counters based on confidence."""
        if label == 'POSITIVE':
            key = "strong_pos" if conf >= STRONG_CONFIDENCE_THRESHOLD else "mild_pos"
        elif label == 'NEGATIVE':
            key = "strong_neg" if conf >= STRONG_CONFIDENCE_THRESHOLD else "mild_neg"
        elif label == 'MIXED':
            key = "mixed"
        else:
            key = "neutral"
        accum[key] += 1

    @staticmethod
    def _increment_bucket(bucket: Dict[str, Dict[str, int]], key: str, label_key: str) -> None:
        """Increment a label count within a named bucket."""
        if key not in bucket:
            bucket[key] = {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}
        bucket[key][label_key] += 1

    @staticmethod
    def _collect_topic_sample(
        topic_samples: Dict[str, List[Dict[str, Any]]],
        topic: str,
        doc_id: int,
        label: str,
        confidence: float,
        data: Dict[str, Any],
        title: Optional[str],
        source_type: Optional[str],
        published_at: Optional[float],
        domain_or_subreddit: Optional[str],
        ident: Optional[str],
        text: Optional[str],
    ) -> None:
        """Collect a classification sample for a topic (capped at 5 per topic)."""
        MAX_SAMPLES_PER_TOPIC = 5
        MAX_EVIDENCE_PER_SAMPLE = 5
        if topic not in topic_samples:
            topic_samples[topic] = []
        samples = topic_samples[topic]
        reasoning = data.get("reasoning", "")
        if not reasoning:
            return

        # Skip if this doc_id is already represented in the topic's samples
        # (ai_outputs can have multiple rows per doc across prompt versions / reruns)
        if any(s["doc_id"] == doc_id for s in samples):
            return

        raw_spans = data.get("evidence_spans", [])
        clean_spans = SentimentAggregator._sanitize_evidence(raw_spans, MAX_EVIDENCE_PER_SAMPLE)

        date_str = datetime.fromtimestamp(published_at).strftime('%b %d, %Y') if published_at else None
        url = None
        if ident:
            if ident.startswith("http"):
                url = ident
            elif source_type and source_type.startswith("reddit"):
                post_id = ident.replace("t3_", "").replace("t1_", "")
                url = f"https://reddit.com/r/{domain_or_subreddit or 'all'}/comments/{post_id}"

        sample = {
            "doc_id": doc_id,
            "label": label,
            "confidence": confidence,
            "reasoning": reasoning,
            "evidence_spans": clean_spans,
            "sarcasm_detected": bool(data.get("sarcasm_detected", False)),
            "title": title or "",
            "source_type": source_type or "unknown",
            "source_name": domain_or_subreddit,
            "date": date_str,
            "full_text": text or "",
            "url": url,
        }
        if len(samples) < MAX_SAMPLES_PER_TOPIC:
            samples.append(sample)
            samples.sort(key=lambda s: s["confidence"], reverse=True)
        elif confidence > samples[-1]["confidence"]:
            samples[-1] = sample
            samples.sort(key=lambda s: s["confidence"], reverse=True)

    @staticmethod
    def _sanitize_evidence(spans: list, max_count: int = 5) -> list:
        """
        Clean evidence spans: deduplicate, remove placeholders, filter trivial.

        Removes:
        - Placeholder text (e.g. 'exact quote 1')
        - @-mention-only spans
        - Very short or single-word spans
        - Duplicate spans (case-insensitive)
        """
        seen: set = set()
        result: list = []
        placeholder_pattern = re.compile(r"^exact quote", re.IGNORECASE)
        mention_pattern = re.compile(r"^@\w+$")

        for span in spans:
            if not isinstance(span, str):
                continue
            trimmed = span.strip()
            if not trimmed or len(trimmed) < 4:
                continue
            if placeholder_pattern.match(trimmed):
                continue
            # Filter placeholders, but do NOT filter by word count or mentions
            # since a single word like 'Satanists' or a single mention '@x' is valid evidence if the LLM chose it.
            key = trimmed.lower()
            if key in seen:
                continue
            seen.add(key)
            result.append(trimmed)
            if len(result) >= max_count:
                break
        return result

    def _build_sentiment_result(self, accum: Dict[str, Any]) -> PublicSentimentResult:
        """Construct PublicSentimentResult from pre-aggregated data."""
        total_pos = accum["strong_pos"] + accum["mild_pos"]
        total_neg = accum["strong_neg"] + accum["mild_neg"]
        total = total_pos + total_neg + accum["neutral"] + accum["mixed"]
        net_score = ((total_pos - total_neg) / total * 100) if total > 0 else 0

        social = accum["social"]
        news = accum["news"]
        social_total = sum(social.values())
        news_total = sum(news.values())
        social_net = ((social["positive"] - social["negative"]) / social_total * 100) if social_total > 0 else 0
        news_net = ((news["positive"] - news["negative"]) / news_total * 100) if news_total > 0 else 0

        result = PublicSentimentResult(
            overview=SentimentOverview(
                netScore=round(net_score, 1), volume=accum["count"],
                coverage="medium", confidence="medium",
            ),
            distribution=SentimentDistribution(
                strongPositive=accum["strong_pos"], mildPositive=accum["mild_pos"],
                neutral=accum["neutral"] + accum["mixed"],
                mildNegative=accum["mild_neg"], strongNegative=accum["strong_neg"],
            ),
            byPlatform=self._format_platform_sentiment(accum["by_platform"]),
            byTopic=self._format_topic_sentiment(accum["by_topic"], accum["topic_samples"]),
            byTimeWindow=self._format_time_window_sentiment(accum["by_time"]),
            disclaimer="Represents sampled platform discourse, not verified population sentiment",
            excluded_bot_content=accum["excluded_bots"],
        )
        result.socialVsNews = {
            "social": {**social, "netScore": round(social_net, 1), "volume": social_total},
            "news": {**news, "netScore": round(news_net, 1), "volume": news_total},
        }
        return result
    
    def _merge_favorability_data(
        self, result: PublicSentimentResult, fav_rows: List[tuple], bot_docs: Set[int]
    ) -> None:
        """
        Merge GOP favorability data into the sentiment result.
        Processes favorability rows into stance breakdown, trend, and platform data.
        """
        distribution, by_platform, daily_net, count = self._parse_favorability_rows(fav_rows, bot_docs)
        if count == 0:
            return
        self._format_favorability_result(result, distribution, by_platform, daily_net, count)

    def _parse_favorability_rows(
        self, fav_rows: List[tuple], bot_docs: Set[int]
    ) -> tuple:
        """Parse raw favorability rows into intermediate aggregation structures."""
        distribution = {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0}
        by_platform: Dict[str, Dict[str, int]] = {}
        daily_net: Dict[str, Dict[str, Any]] = {}
        count = 0

        for doc_id, output_json, confidence, source_type, pub_at in fav_rows:
            if doc_id in bot_docs:
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
            platform = source_type or 'unknown'
            if platform in ('reddit_post', 'reddit_comment'):
                platform = 'reddit'
            if platform not in by_platform:
                by_platform[platform] = {"favorable": 0, "unfavorable": 0, "neutral": 0}
            if stance in by_platform[platform]:
                by_platform[platform][stance] += 1

            # Track daily trend
            if pub_at:
                self._track_daily_favorability(daily_net, pub_at, stance)

            count += 1

        return distribution, by_platform, daily_net, count

    def _track_daily_favorability(
        self, daily_net: Dict[str, Dict[str, Any]], pub_at: float, stance: str
    ) -> None:
        """Accumulate daily favorability scores for trend data."""
        try:
            date_str = datetime.fromtimestamp(pub_at).strftime('%Y-%m-%d')
            if date_str not in daily_net:
                daily_net[date_str] = {"score": 0, "count": 0}
            val = 1 if stance == 'favorable' else (-1 if stance == 'unfavorable' else 0)
            daily_net[date_str]["score"] += val
            daily_net[date_str]["count"] += 1
        except (ValueError, TypeError, OSError):
            pass

    def _format_favorability_result(
        self,
        result: PublicSentimentResult,
        distribution: Dict[str, int],
        by_platform: Dict[str, Dict[str, int]],
        daily_net: Dict[str, Dict[str, Any]],
        count: int,
    ) -> None:
        """Format parsed favorability data into the PublicSentimentResult fields."""
        total = sum(distribution.values())
        net_favorability = ((distribution["favorable"] - distribution["unfavorable"]) / total * 100) if total > 0 else 0

        result.gopFavorability = {
            "favorable": round((distribution["favorable"] / total) * 100, 1) if total else 0,
            "unfavorable": round((distribution["unfavorable"] / total) * 100, 1) if total else 0,
            "neutral": round((distribution["neutral"] / total) * 100, 1) if total else 0,
            "netFavorability": round(net_favorability, 1),
            "sampleSize": count,
            "sourceCount": len(by_platform),
            "lastUpdated": datetime.now().isoformat(),
        }

        result.gopTrend = [
            {"date": date, "value": round((stats["score"] / stats["count"]) * 100, 1) if stats["count"] > 0 else 0}
            for date, stats in sorted(daily_net.items())
        ][-30:]

        result.gopByPlatform = [
            {"group": platform.capitalize(), "favorable": stats["favorable"], "unfavorable": stats["unfavorable"], "neutral": stats["neutral"]}
            for platform, stats in by_platform.items()
        ]

        polling_data = self.cache.load("polling_gop")
        if polling_data:
            result.pollingVsSocial = {
                "onlineSentiment": {
                    "favorable": result.gopFavorability["favorable"],
                    "unfavorable": result.gopFavorability["unfavorable"],
                    "neutral": result.gopFavorability["neutral"],
                },
                "pollingData": polling_data,
            }

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
    
    def _format_topic_sentiment(
        self,
        by_topic: Dict[str, Dict[str, int]],
        topic_samples: Dict[str, List[Dict[str, Any]]],
    ) -> List[TopicSentiment]:
        """Format topic sentiment breakdown with classification samples."""
        topics = []
        for topic, counts in by_topic.items():
            raw_samples = topic_samples.get(topic, [])
            sarcasm_count = sum(1 for s in raw_samples if s.get("sarcasm_detected"))
            volume = sum(counts.values())
            sarcasm_rate = round(sarcasm_count / volume * 100, 1) if volume > 0 else 0.0
            samples = [
                ClassificationSample(
                    doc_id=s["doc_id"], label=s["label"],
                    confidence=s["confidence"], reasoning=s["reasoning"],
                    evidence_spans=s["evidence_spans"],
                    sarcasm_detected=s["sarcasm_detected"],
                    title=s.get("title"), source_type=s.get("source_type"),
                    source_name=s.get("source_name"),
                    date=s.get("date"),
                    full_text=s.get("full_text", ""),
                    url=s.get("url"),
                )
                for s in raw_samples
            ]
            topics.append(TopicSentiment(
                topic=topic, positive=counts["positive"],
                negative=counts["negative"], neutral=counts["neutral"],
                volume=volume, sarcasm_rate=sarcasm_rate,
                classification_samples=samples,
            ))
        # Pin "General" to the top; sort the rest by volume descending.
        return sorted(topics, key=lambda t: (t.topic != "General", -t.volume))
    
    def _format_time_window_sentiment(self, by_time_window: Dict[str, Dict[str, int]]) -> List[TimeWindowSentiment]:
        """Format time window sentiment breakdown in chronological order."""
        # Define order for time buckets
        order = {"24 hours": 0, "7 days": 1, "30 days": 2, "90+ days": 3, "Unknown": 4}
        
        windows = [
            TimeWindowSentiment(
                window=window,
                positive=counts["positive"],
                negative=counts["negative"],
                neutral=counts["neutral"],
                volume=sum(counts.values()),
            )
            for window, counts in by_time_window.items()
        ]
        return sorted(windows, key=lambda w: order.get(w.window, 999))
