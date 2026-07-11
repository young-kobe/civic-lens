"""
Public sentiment aggregator — the orchestrating class plus the row-level
helpers and result formatters that don't belong to a more specific submodule.

Aggregates sentiment metrics excluding bot-flagged content. Produces the
headline net score, per-intensity distribution, per-platform / per-topic
/ per-time / per-day-of-week breakdowns, and (walkthrough 057) the
three-way tier split: news outlets / verified officials / general
public. GOP favorability is merged in as a secondary data path.

The class sits at the top of the package's dependency graph: it imports the
sample builders (``samples``), the routing pass (``entities``), and the two
merge families (``favorability`` / ``target_tone``); none of those import
back into this module.
"""

from __future__ import annotations

import json
from datetime import datetime
from typing import Any, Dict, List, Optional, Set

from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators.base import (
    ACCOUNT_PROFILE_JOIN_SQL,
    REDDIT_ENGAGEMENT_JOIN_SQL,
    SAMPLE_ENRICHMENT_SELECT,
    X_AUTHOR_JOIN_SQL,
    build_sample_author,
    build_sample_engagement,
    fetch_task_rows,
    get_bot_flagged_doc_ids,
    get_connection,
    get_high_bot_score_author_ids,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.constants import (
    NEWS_PLATFORMS, SOCIAL_PLATFORMS, STRONG_CONFIDENCE_THRESHOLD, TOPIC_KEYWORDS,
)
from analysis.src.reporting.aggregators.rows import SentimentRow, TargetMentionRow
from analysis.src.reporting.aggregators.sentiment.entities import (
    _consolidate_sampled_authors,
    _route_and_record,
)
from analysis.src.reporting.aggregators.sentiment.favorability import (
    _merge_favorability_data,
)
from analysis.src.reporting.aggregators.sentiment.samples import (
    STRENGTH_BUCKETS,
    _LABEL_MAP,
    _attach_sample_targets,
    _build_doc_targets,
    _collect_strength_sample,
    _collect_topic_sample,
    _increment_bucket,
    _sample_dict_to_model,
)
from analysis.src.reporting.aggregators.sentiment.target_tone import (
    BOT_SCORE_AUTHOR_EXCLUSION,
    MIN_TARGET_SAMPLE_N,
    _fetch_doc_topics,
    _fetch_narrative_doc_map,
    _merge_outbound_targets,
    _merge_target_tone,
    _net_or_none,
)
from analysis.src.reporting.entity_registry import get_registry
from analysis.src.reporting.models import (
    ClassificationSample,
    DayOfWeekSentiment,
    EntitySentimentItem,
    PlatformSentiment,
    PublicSentimentResult,
    SentimentDistribution,
    SentimentOverview,
    TimeWindowSentiment,
    TopicSentiment,
)


# --------------------------------------------------------------------------- #
#  Module constants                                                           #
# --------------------------------------------------------------------------- #

_DOW_LABELS = ("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun")


# --------------------------------------------------------------------------- #
#  Class                                                                      #
# --------------------------------------------------------------------------- #

class SentimentAggregator:
    """Aggregates public sentiment metrics with merged GOP favorability data."""

    def __init__(self, db_path: str):
        self.db_path = db_path
        self.cache = SnapshotCache(get_settings().cache_dir)

    def get_public_sentiment(
        self,
        time_window: str = "24h",
        bot_docs: Optional[Set[int]] = None,
    ) -> PublicSentimentResult:
        """Aggregate sentiment excluding bot-flagged content + rows whose
        confidence is below ``aggregation_min_confidence`` (walkthrough 039).

        Args:
            time_window: 24h | 7d | 30d | 90d | all.
            bot_docs: Pre-computed bot-flagged doc id set. When None, the
                aggregator queries it itself. job_runner passes a cached
                set so the query doesn't run once per aggregator.
        """
        min_conf = get_settings().aggregation_min_confidence
        if bot_docs is None:
            bot_docs = get_bot_flagged_doc_ids(self.db_path, min_confidence=min_conf)
        cutoff = get_time_cutoff(time_window)

        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()

            # Left-join x_posts_raw + x_users_raw so every sentiment row can
            # carry the post author's handle for registry matching, plus the
            # curated account classification (account_profiles) so classified
            # non-registry authors get named cards instead of the catch-all.
            # No-op for non-X rows; u.*/x.*/ap.* are NULL and entity routing
            # handles that downstream (walkthrough 057).
            sentiment_rows = fetch_task_rows(
                cursor,
                "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at, d.title, d.domain_or_subreddit, d.ident, d.text, u.username, x.is_official_tier, "
                "ap.tier, ap.full_name, ap.party, ap.office_title, ap.account_type, "
                f"{SAMPLE_ENRICHMENT_SELECT}",
                task_type="sentiment",
                cutoff=cutoff,
                min_confidence=min_conf,
                extra_joins=(
                    f"{X_AUTHOR_JOIN_SQL} {ACCOUNT_PROFILE_JOIN_SQL} "
                    f"{REDDIT_ENGAGEMENT_JOIN_SQL}"
                ),
            )
            favorability_rows = fetch_task_rows(
                cursor,
                "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at",
                task_type="favorability",
                cutoff=cutoff,
                min_confidence=min_conf,
            )
            # Target mentions: one row per (doc, target) with entity
            # identity resolved at WRITE time (migration 025) — no free-text
            # re-resolution per snapshot. Joined through ai_outputs_latest so
            # a reprocessed doc's superseded mentions drop out; a.output_json
            # rides along only for the doc-level reasoning shown in samples.
            # The per-target confidence floor is SQL-side (row-level mean
            # confidence is deliberately ignored). The engagement sum is a
            # reach proxy for the weighted received-tone variant; NULL
            # counts (non-X docs) coalesce to 0 → weight 1.
            target_sql = f"""
                SELECT m.doc_id, m.entity_key, m.entity_kind, m.entity_party,
                       m.stance, m.topic, m.confidence, m.evidence_json,
                       a.output_json,
                       d.source_type, d.published_at, d.title,
                       d.domain_or_subreddit, d.ident, d.text,
                       u.username, x.is_official_tier, x.author_id, ap.tier,
                       COALESCE(x.retweet_count, 0) + COALESCE(x.reply_count, 0)
                       + COALESCE(x.like_count, 0) + COALESCE(x.quote_count, 0),
                       m.raw_target
                FROM target_mentions m
                JOIN ai_outputs_latest a ON a.output_id = m.output_id
                JOIN docs d ON d.doc_id = m.doc_id
                {X_AUTHOR_JOIN_SQL} {ACCOUNT_PROFILE_JOIN_SQL}
                WHERE m.confidence >= ?
            """
            target_params: List[Any] = [min_conf]
            if cutoff is not None:
                target_sql += " AND d.published_at >= ?"
                target_params.append(cutoff)
            cursor.execute(target_sql, target_params)
            target_rows = cursor.fetchall()
            bot_score_authors = get_high_bot_score_author_ids(
                cursor, min_score=BOT_SCORE_AUTHOR_EXCLUSION,
            )
            narrative_map = _fetch_narrative_doc_map(cursor, cutoff)
            doc_topics = _fetch_doc_topics(cursor, min_conf)

        # ``allowed_sources=None`` means no filter — the only path now that
        # the UI's "Filter by sources" pills have been removed. Internal
        # plumbing kept as an Optional[frozenset] so the per-row scoping
        # in ``_aggregate_rows`` / ``_merge_favorability_data`` short-
        # circuits cleanly without rewriting those hot loops.
        allowed_sources = None
        result = self._process_sentiment_data(
            sentiment_rows, bot_docs, allowed_sources, doc_topics,
        )
        _merge_favorability_data(result, favorability_rows, bot_docs, allowed_sources, self.cache)
        _merge_target_tone(
            result, target_rows, bot_docs,
            bot_score_authors=bot_score_authors, narrative_map=narrative_map,
        )
        _merge_outbound_targets(
            result, target_rows, bot_docs, bot_score_authors=bot_score_authors,
        )
        _attach_sample_targets(result, _build_doc_targets(
            (m.doc_id, m.entity_key, m.stance, m.raw_target)
            for m in map(TargetMentionRow.from_row, target_rows)
        ))
        return result

    def _process_sentiment_data(
        self, rows: List[tuple], bot_docs: Set[int], allowed_sources: Optional[frozenset],
        doc_topics: Optional[Dict[int, str]] = None,
    ) -> PublicSentimentResult:
        accum = self._aggregate_rows(rows, bot_docs, allowed_sources, doc_topics)
        return self._build_result(accum)

    def _aggregate_rows(
        self, rows: List[tuple], bot_docs: Set[int], allowed_sources: Optional[frozenset],
        doc_topics: Optional[Dict[int, str]] = None,
    ) -> Dict[str, Any]:
        """Walk each row once, fanning counts + samples into every
        accumulator the result consumer needs. Keep inline work tight —
        branchy logic lives in module-level helpers below."""
        registry = get_registry()
        doc_topics = doc_topics or {}
        accum: Dict[str, Any] = {
            "strong_pos": 0, "mild_pos": 0, "strong_neg": 0, "mild_neg": 0,
            "neutral": 0, "mixed": 0, "count": 0, "excluded_bots": 0,
            "conf_sum": 0.0,
            "social": {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0},
            "news": {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0},
            "by_platform": {}, "by_topic": {}, "by_time": {}, "by_dow": {},
            "topic_samples": {},
            "strength_samples": {b: [] for b in STRENGTH_BUCKETS},
            # Three-way entity rollups + per-topic tier split (walkthrough 057).
            "by_news_outlet": {}, "by_official": {}, "by_general_public": {},
            "by_topic_tier": {},
            # Per-day per-tier stance counts → the toneTrend daily series.
            "by_day_tier": {},
        }
        now = datetime.now()

        for raw_row in rows:
            r = SentimentRow.from_row(raw_row)
            if r.doc_id in bot_docs:
                accum["excluded_bots"] += 1
                continue
            if allowed_sources is not None and (r.source_type or "") not in allowed_sources:
                continue
            try:
                data = json.loads(r.output_json)
            except json.JSONDecodeError:
                continue

            engagement = build_sample_engagement(
                r.source_type, r.x_retweets, r.x_replies, r.x_likes, r.x_quotes,
                r.reddit_score, r.reddit_comments,
            )
            author = build_sample_author(
                r.source_type, r.x_handle, r.u_name, r.u_avatar, r.u_verified_type,
                r.u_followers, r.u_created_at, bio=r.u_bio,
            )

            label = data.get("label", "NEUTRAL")
            conf = float(data.get("confidence", r.confidence or 0.5))
            strength_key = _count_strength(accum, label, conf)

            label_key = _LABEL_MAP.get(label, "neutral")
            category = _categorize_platform(r.source_type)

            if category == "Social Media":
                accum["social"][label_key] += 1
            elif category == "News Outlets":
                accum["news"][label_key] += 1

            _increment_bucket(accum["by_platform"], category, label_key)
            # LLM-extracted topic (dominant target_mentions topic for this
            # doc) wins; title-keyword heuristic is the fallback for docs
            # with no resolved mention topic; "General" is the honest
            # no-signal bucket.
            topic = doc_topics.get(r.doc_id) or _extract_topic(r.title)
            _increment_bucket(accum["by_topic"], topic, label_key)
            _increment_bucket(accum["by_time"], _time_bucket(r.published_at, now), label_key)
            dow = _day_of_week(r.published_at)
            if dow is not None:
                _increment_bucket(accum["by_dow"], dow, label_key)

            _collect_topic_sample(
                accum["topic_samples"], topic, r.doc_id, label, conf,
                data, r.title, r.source_type, r.published_at, r.domain_or_subreddit, r.ident, r.text,
                r.x_handle, engagement=engagement, author=author,
            )
            _collect_strength_sample(
                accum["strength_samples"], strength_key, r.doc_id, label, conf,
                data, r.title, r.source_type, r.published_at, r.domain_or_subreddit, r.ident, r.text,
                r.x_handle, topic=topic, engagement=engagement, author=author,
            )

            account = None
            if r.ap_tier:
                account = {
                    "tier": r.ap_tier, "full_name": r.ap_full_name,
                    "party": r.ap_party, "office_title": r.ap_office_title,
                    "account_type": r.ap_account_type,
                }
            day = _day_key(r.published_at)
            tier = _route_and_record(
                accum, registry, r.source_type, r.domain_or_subreddit, r.x_handle,
                r.doc_id, label, conf, data, r.title, r.published_at, r.ident, r.text,
                is_official_tier=bool(r.is_official_tier),
                account=account,
                topic=topic,
                engagement=engagement,
                author=author,
                day=day,
            )
            if tier is not None:
                _increment_bucket(accum["by_topic_tier"], f"{topic}\x00{tier}", label_key)
                if day is not None:
                    _increment_bucket(accum["by_day_tier"], f"{day}\x00{tier}", label_key)

            accum["count"] += 1
            accum["conf_sum"] += conf

        _consolidate_sampled_authors(accum["by_general_public"])
        return accum

    def _build_result(self, accum: Dict[str, Any]) -> PublicSentimentResult:
        """Assemble PublicSentimentResult from the aggregation accumulator."""
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
                coverage=_coverage_bucket(accum["count"]),
                confidence=_confidence_bucket(accum["conf_sum"], accum["count"]),
            ),
            distribution=SentimentDistribution(
                strongPositive=accum["strong_pos"], mildPositive=accum["mild_pos"],
                neutral=accum["neutral"] + accum["mixed"],
                mildNegative=accum["mild_neg"], strongNegative=accum["strong_neg"],
            ),
            byPlatform=_format_platform(accum["by_platform"]),
            byTopic=_format_topic(
                accum["by_topic"], accum["topic_samples"], accum["by_topic_tier"],
            ),
            byTimeWindow=_format_time_window(accum["by_time"]),
            byDayOfWeek=_format_day_of_week(accum["by_dow"]),
            distributionSamples=_format_distribution_samples(accum["strength_samples"]),
            byNewsOutlet=_format_entity_items(accum["by_news_outlet"]),
            byOfficial=_format_entity_items(accum["by_official"]),
            byGeneralPublic=_format_entity_items(accum["by_general_public"]),
            disclaimer="Represents sampled platform discourse, not verified population sentiment",
            excluded_bot_content=accum["excluded_bots"],
        )
        result.socialVsNews = {
            "social": {**social, "netScore": round(social_net, 1), "volume": social_total},
            "news": {**news, "netScore": round(news_net, 1), "volume": news_total},
        }
        tone_trend = _format_tone_trend(accum["by_day_tier"])
        if tone_trend:
            result.toneTrend = tone_trend
        return result


# --------------------------------------------------------------------------- #
#  Coverage + confidence derivation (audit U-6)                               #
# --------------------------------------------------------------------------- #

# Overview coverage is derived from how many sentiment rows survived the bot +
# min-confidence filters; confidence from the mean per-row model confidence.
# Both were hardcoded "medium" before U-6. Thresholds are deliberately coarse —
# the headline chip is a rough "how much / how sure", not a precise metric.
_COVERAGE_LOW_MAX = 50      # < 50 sampled rows → thin coverage
_COVERAGE_HIGH_MIN = 500    # >= 500 sampled rows → broad coverage
_CONFIDENCE_LOW_MAX = 0.6   # mean row confidence < 0.6 → low
_CONFIDENCE_HIGH_MIN = 0.8  # mean row confidence >= 0.8 → high


def _coverage_bucket(volume: int) -> str:
    if volume < _COVERAGE_LOW_MAX:
        return "low"
    if volume >= _COVERAGE_HIGH_MIN:
        return "high"
    return "medium"


def _confidence_bucket(conf_sum: float, count: int) -> str:
    if count <= 0:
        return "low"
    mean_conf = conf_sum / count
    if mean_conf < _CONFIDENCE_LOW_MAX:
        return "low"
    if mean_conf >= _CONFIDENCE_HIGH_MIN:
        return "high"
    return "medium"


# --------------------------------------------------------------------------- #
#  Row-level helpers (pure functions)                                         #
# --------------------------------------------------------------------------- #

def _categorize_platform(source_type: Optional[str]) -> str:
    if source_type in SOCIAL_PLATFORMS:
        return "Social Media"
    if source_type in NEWS_PLATFORMS:
        return "News Outlets"
    return "Other"


def _extract_topic(title: Optional[str]) -> str:
    if not title:
        return "General"
    title_lower = title.lower()
    for topic, keywords in TOPIC_KEYWORDS.items():
        if any(kw in title_lower for kw in keywords):
            return topic
    return "General"


def _day_of_week(published_at: Any) -> Optional[str]:
    """Short weekday label (Mon..Sun) or None when the timestamp is unparseable."""
    dt = _parse_published_at(published_at)
    if dt is None:
        return None
    return _DOW_LABELS[dt.weekday()]


def _day_key(published_at: Any) -> Optional[str]:
    """Calendar-day bucket key ('YYYY-MM-DD') for the toneTrend series, or
    None when the timestamp is unparseable. Same local-time convention as
    the GOP daily trend (_track_daily_favorability)."""
    dt = _parse_published_at(published_at)
    if dt is None:
        return None
    return dt.strftime("%Y-%m-%d")


def _time_bucket(published_at: Any, now: datetime) -> str:
    dt = _parse_published_at(published_at)
    if dt is None:
        return "Unknown"
    delta = now - dt
    if delta.days < 1:
        return "24 hours"
    if delta.days < 7:
        return "7 days"
    if delta.days < 30:
        return "30 days"
    return "90+ days"


def _parse_published_at(published_at: Any) -> Optional[datetime]:
    try:
        if isinstance(published_at, (int, float)):
            return datetime.fromtimestamp(published_at)
        if isinstance(published_at, str):
            dt = datetime.fromisoformat(published_at.replace("Z", "+00:00"))
            if dt.tzinfo:
                dt = dt.replace(tzinfo=None)
            return dt
    except (ValueError, TypeError, OSError):
        return None
    return None


def _count_strength(accum: Dict[str, Any], label: str, conf: float) -> str:
    """Increment the overall intensity counters and return the per-row
    drill-down bucket key.

    MIXED folds into the neutral headline counter and the neutral
    drill-down bucket — mirrors how the UI renders Tone Intensity."""
    if label == "POSITIVE":
        if conf >= STRONG_CONFIDENCE_THRESHOLD:
            accum["strong_pos"] += 1
            return "strongPositive"
        accum["mild_pos"] += 1
        return "mildPositive"
    if label == "NEGATIVE":
        if conf >= STRONG_CONFIDENCE_THRESHOLD:
            accum["strong_neg"] += 1
            return "strongNegative"
        accum["mild_neg"] += 1
        return "mildNegative"
    if label == "MIXED":
        accum["mixed"] += 1
        return "neutral"
    accum["neutral"] += 1
    return "neutral"


# --------------------------------------------------------------------------- #
#  Formatters — produce response objects from the accumulator                 #
# --------------------------------------------------------------------------- #

def _format_platform(by_platform: Dict[str, Dict[str, int]]) -> List[PlatformSentiment]:
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


def _format_topic(
    by_topic: Dict[str, Dict[str, int]],
    topic_samples: Dict[str, List[Dict[str, Any]]],
    by_topic_tier: Dict[str, Dict[str, int]],
) -> List[TopicSentiment]:
    """Build TopicSentiment rows with samples + the three-way tier split."""
    tier_lookup = _split_topic_tier(by_topic_tier)
    topics: List[TopicSentiment] = []
    for topic, counts in by_topic.items():
        raw_samples = topic_samples.get(topic, [])
        sarcasm_count = sum(1 for s in raw_samples if s.get("sarcasm_detected"))
        volume = sum(counts.values())
        sarcasm_rate = round(sarcasm_count / volume * 100, 1) if volume > 0 else 0.0

        tiers = tier_lookup.get(topic, {})
        news_net, news_vol = _net_from_tier(tiers.get("news"))
        off_net, off_vol = _net_from_tier(tiers.get("officials"))
        pub_net, pub_vol = _net_from_tier(tiers.get("public"))

        topics.append(TopicSentiment(
            topic=topic, positive=counts["positive"],
            negative=counts["negative"], neutral=counts["neutral"],
            volume=volume, sarcasm_rate=sarcasm_rate,
            classification_samples=[_sample_dict_to_model(s) for s in raw_samples],
            newsNet=news_net, officialsNet=off_net, publicNet=pub_net,
            newsVolume=news_vol, officialsVolume=off_vol, publicVolume=pub_vol,
        ))
    # Pin "General" to the top; sort the rest by volume descending.
    return sorted(topics, key=lambda t: (t.topic != "General", -t.volume))


def _split_topic_tier(
    by_topic_tier: Dict[str, Dict[str, int]],
) -> Dict[str, Dict[str, Dict[str, int]]]:
    """Unpack the composite ``"topic\\x00tier"`` keys into a nested
    ``topic → tier → counts`` mapping. The composite key keeps the
    accumulator flat (cheaper increments on the hot path)."""
    out: Dict[str, Dict[str, Dict[str, int]]] = {}
    for composite, counts in by_topic_tier.items():
        if "\x00" not in composite:
            continue
        topic, tier = composite.split("\x00", 1)
        out.setdefault(topic, {})[tier] = counts
    return out


def _net_from_tier(counts: Optional[Dict[str, int]]):
    """(netScore, volume) for a tier; (None, 0) when empty so the UI can
    distinguish "no data" from a real zero."""
    if not counts:
        return None, 0
    total = sum(counts.values())
    if total == 0:
        return None, 0
    net = (counts.get("positive", 0) - counts.get("negative", 0)) / total * 100
    return round(net, 1), total


_TONE_TREND_TIERS = ("news", "officials", "public")
_TONE_TREND_MAX_DAYS = 30


def _format_tone_trend(
    by_day_tier: Dict[str, Dict[str, int]],
) -> List[Dict[str, Any]]:
    """Materialize the per-day per-tier stance counts into the toneTrend
    series: dates ascending, one {net, volume} cell per tier per day.

    A tier's net is suppressed (None) below MIN_TARGET_SAMPLE_N — a
    three-post day must not draw a +100 spike; the UI renders a gap. The
    volume is always emitted so the honest n stays visible. Capped to the
    trailing _TONE_TREND_MAX_DAYS to bound payload size, matching gopTrend.
    """
    days: Dict[str, Dict[str, Dict[str, int]]] = {}
    for composite, counts in by_day_tier.items():
        if "\x00" not in composite:
            continue
        day, tier = composite.split("\x00", 1)
        days.setdefault(day, {})[tier] = counts

    series: List[Dict[str, Any]] = []
    for day in sorted(days)[-_TONE_TREND_MAX_DAYS:]:
        row: Dict[str, Any] = {"date": day}
        for tier in _TONE_TREND_TIERS:
            counts = days[day].get(tier)
            volume = sum(counts.values()) if counts else 0
            row[tier] = {
                "net": _net_or_none(counts) if counts else None,
                "volume": volume,
            }
        series.append(row)
    return series


def _format_entity_items(bucket: Dict[str, Dict[str, Any]]) -> List[EntitySentimentItem]:
    """Materialize per-entity accumulators into EntitySentimentItem, with
    real registry entities first and catch-all buckets sorted to the end."""
    items: List[EntitySentimentItem] = []
    for key, stats in bucket.items():
        volume = stats["volume"]
        if volume == 0:
            continue
        net = (stats["positive"] - stats["negative"]) / volume * 100
        # Topic-scoped expressed cells. Same suppression floor as received
        # tone: a 1-post topic slice reports its volume, never a +/-100
        # headline (net=None + lowSample instead).
        by_topic = sorted(
            (
                {
                    "topic": t,
                    "net": _net_or_none(counts),
                    "volume": sum(counts.values()),
                    "lowSample": sum(counts.values()) < MIN_TARGET_SAMPLE_N,
                }
                for t, counts in stats["by_topic"].items()
            ),
            key=lambda cell: -cell["volume"],
        )
        # Per-entity daily net-tone series (trailing window), suppressed below
        # the sample floor per day so a 1-post day never draws a spike. Powers
        # the Tone-over-time chart's tier→entity drill-down.
        daily_tone = [
            {
                "date": d,
                "net": _net_or_none(counts),
                "volume": sum(counts.values()),
                "lowSample": sum(counts.values()) < MIN_TARGET_SAMPLE_N,
            }
            for d, counts in sorted(stats.get("by_day", {}).items())[-_TONE_TREND_MAX_DAYS:]
        ]
        items.append(EntitySentimentItem(
            key=key,
            kind=stats["kind"],
            positive=stats["positive"],
            negative=stats["negative"],
            neutral=stats["neutral"],
            volume=volume,
            netScore=round(net, 1),
            entity_profile=stats["profile"],
            classification_samples=[_sample_dict_to_model(s) for s in stats["samples"]],
            expressed_by_topic=by_topic,
            engagement_total=stats.get("engagement_total", 0),
            daily_tone=daily_tone,
        ))
    items.sort(key=lambda it: (it.kind == "catch_all", -it.volume))
    return items


def _format_time_window(by_time_window: Dict[str, Dict[str, int]]) -> List[TimeWindowSentiment]:
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


def _format_day_of_week(by_dow: Dict[str, Dict[str, int]]) -> List[DayOfWeekSentiment]:
    order = {label: i for i, label in enumerate(_DOW_LABELS)}
    rows = [
        DayOfWeekSentiment(
            day=day,
            positive=counts["positive"],
            negative=counts["negative"],
            neutral=counts["neutral"],
            volume=sum(counts.values()),
        )
        for day, counts in by_dow.items()
    ]
    return sorted(rows, key=lambda r: order.get(r.day, 999))


def _format_distribution_samples(
    strength_samples: Dict[str, List[Dict[str, Any]]],
) -> Dict[str, List[ClassificationSample]]:
    """Drop empty buckets so the UI doesn't render empty drill-down panels."""
    return {
        bucket: [_sample_dict_to_model(s) for s in raw_samples]
        for bucket, raw_samples in strength_samples.items()
        if raw_samples
    }
