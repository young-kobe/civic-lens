"""
Public sentiment aggregator.

Aggregates sentiment metrics excluding bot-flagged content. Produces the
headline net score, per-intensity distribution, per-platform / per-topic
/ per-time / per-day-of-week breakdowns, and (walkthrough 057) the
three-way tier split: news outlets / verified officials / general
public. GOP favorability is merged in as a secondary data path.

The file is deliberately structured with the class up top (the public
surface the rest of the system talks to) and the pure helpers below,
following the pattern in ``bot.py`` and peers.
"""

from __future__ import annotations

import json
import re
from datetime import datetime
from typing import Any, Dict, List, Optional, Set

from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators.base import (
    X_AUTHOR_JOIN_SQL,
    fetch_task_rows,
    get_bot_flagged_doc_ids,
    get_connection,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.constants import (
    NEWS_PLATFORMS, SOCIAL_PLATFORMS, STRONG_CONFIDENCE_THRESHOLD, TOPIC_KEYWORDS,
)
from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS, CATCH_ALL_SUBREDDITS, CATCH_ALL_X_USERS,
    catch_all_profile, get_registry, resolve_entity,
)
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

# Keys for per-intensity drill-down sampling — must match SentimentDistribution
# field names so the UI can look them up directly.
STRENGTH_BUCKETS = (
    "strongPositive", "mildPositive", "neutral", "mildNegative", "strongNegative",
)
MAX_DISTRIBUTION_SAMPLES_PER_BUCKET = 15
MAX_SAMPLES_PER_TOPIC = 5
MAX_SAMPLES_PER_ENTITY = 10
MAX_EVIDENCE_PER_SAMPLE = 5

# Sentinel key for x_posts routed to the officials tier by the ingestor's
# is_official_tier provenance flag alone (no editorial registry entity) — D-4.
CATCH_ALL_VERIFIED_OFFICIALS = "verified-officials-provenance"

_DOW_LABELS = ("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun")
_LABEL_MAP = {
    "POSITIVE": "positive",
    "NEGATIVE": "negative",
    "NEUTRAL": "neutral",
    "MIXED": "neutral",
}


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
            # carry the post author's handle for registry matching. No-op
            # for non-X rows; u.username is NULL and entity_routing handles
            # that downstream (walkthrough 057).
            sentiment_rows = fetch_task_rows(
                cursor,
                "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at, d.title, d.domain_or_subreddit, d.ident, d.text, u.username, x.is_official_tier",
                task_type="sentiment",
                cutoff=cutoff,
                min_confidence=min_conf,
                extra_joins=X_AUTHOR_JOIN_SQL,
            )
            favorability_rows = fetch_task_rows(
                cursor,
                "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at",
                task_type="favorability",
                cutoff=cutoff,
                min_confidence=min_conf,
            )

        # ``allowed_sources=None`` means no filter — the only path now that
        # the UI's "Filter by sources" pills have been removed. Internal
        # plumbing kept as an Optional[frozenset] so the per-row scoping
        # in ``_aggregate_rows`` / ``_merge_favorability_data`` short-
        # circuits cleanly without rewriting those hot loops.
        allowed_sources = None
        result = self._process_sentiment_data(sentiment_rows, bot_docs, allowed_sources)
        _merge_favorability_data(result, favorability_rows, bot_docs, allowed_sources, self.cache)
        return result

    def _process_sentiment_data(
        self, rows: List[tuple], bot_docs: Set[int], allowed_sources: Optional[frozenset],
    ) -> PublicSentimentResult:
        accum = self._aggregate_rows(rows, bot_docs, allowed_sources)
        return self._build_result(accum)

    def _aggregate_rows(
        self, rows: List[tuple], bot_docs: Set[int], allowed_sources: Optional[frozenset],
    ) -> Dict[str, Any]:
        """Walk each row once, fanning counts + samples into every
        accumulator the result consumer needs. Keep inline work tight —
        branchy logic lives in module-level helpers below."""
        registry = get_registry()
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
        }
        now = datetime.now()

        for (
            doc_id, output_json, confidence, source_type, published_at,
            title, domain_or_subreddit, ident, text, x_handle, is_official_tier,
        ) in rows:
            if doc_id in bot_docs:
                accum["excluded_bots"] += 1
                continue
            if allowed_sources is not None and (source_type or "") not in allowed_sources:
                continue
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue

            label = data.get("label", "NEUTRAL")
            conf = float(data.get("confidence", confidence or 0.5))
            strength_key = _count_strength(accum, label, conf)

            label_key = _LABEL_MAP.get(label, "neutral")
            category = _categorize_platform(source_type)

            if category == "Social Media":
                accum["social"][label_key] += 1
            elif category == "News Outlets":
                accum["news"][label_key] += 1

            _increment_bucket(accum["by_platform"], category, label_key)
            topic = _extract_topic(title)
            _increment_bucket(accum["by_topic"], topic, label_key)
            _increment_bucket(accum["by_time"], _time_bucket(published_at, now), label_key)
            dow = _day_of_week(published_at)
            if dow is not None:
                _increment_bucket(accum["by_dow"], dow, label_key)

            _collect_topic_sample(
                accum["topic_samples"], topic, doc_id, label, conf,
                data, title, source_type, published_at, domain_or_subreddit, ident, text,
                x_handle,
            )
            _collect_strength_sample(
                accum["strength_samples"], strength_key, doc_id, label, conf,
                data, title, source_type, published_at, domain_or_subreddit, ident, text,
                x_handle,
            )

            tier = _route_and_record(
                accum, registry, source_type, domain_or_subreddit, x_handle,
                doc_id, label, conf, data, title, published_at, ident, text,
                is_official_tier=bool(is_official_tier),
            )
            if tier is not None:
                _increment_bucket(accum["by_topic_tier"], f"{topic}\x00{tier}", label_key)

            accum["count"] += 1
            accum["conf_sum"] += conf

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
#  Sample collection                                                          #
# --------------------------------------------------------------------------- #

def _sanitize_evidence(spans: list, max_count: int = MAX_EVIDENCE_PER_SAMPLE) -> list:
    """Deduplicate + strip placeholders from an LLM's evidence_spans list.

    Placeholder text (``exact quote``-style) and duplicates are dropped;
    short spans (< 4 chars) are skipped. Single-word and ``@mention``
    spans are preserved — the LLM can legitimately point at a single
    loaded word like ``Satanists`` as evidence.
    """
    seen: set = set()
    result: list = []
    placeholder_pattern = re.compile(r"^exact quote", re.IGNORECASE)
    for span in spans:
        if not isinstance(span, str):
            continue
        trimmed = span.strip()
        if not trimmed or len(trimmed) < 4:
            continue
        if placeholder_pattern.match(trimmed):
            continue
        key = trimmed.lower()
        if key in seen:
            continue
        seen.add(key)
        result.append(trimmed)
        if len(result) >= max_count:
            break
    return result


def _build_sample_dict(
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
    x_handle: Optional[str] = None,
) -> Dict[str, Any]:
    """Build the dict representation of a classification sample used by
    every collector. Centralizing this avoids the 3-place duplication
    that pre-split sentiment.py was carrying."""
    date_str = (
        datetime.fromtimestamp(published_at).strftime("%b %d, %Y")
        if published_at else None
    )
    url = None
    if ident:
        if ident.startswith(("http://", "https://")):
            url = ident
        elif source_type and source_type.startswith("reddit"):
            post_id = ident.replace("t3_", "").replace("t1_", "")
            url = f"https://reddit.com/r/{domain_or_subreddit or 'all'}/comments/{post_id}"
        elif source_type == "x_post" and x_handle:
            # Synthesize the tweet permalink so every X sample is auditable
            # (invariant C1 / audit A-7), mirroring narrative.py's builder.
            url = f"https://x.com/{x_handle}/status/{ident}"
    return {
        "doc_id": doc_id,
        "label": label,
        "confidence": confidence,
        "reasoning": data.get("reasoning", ""),
        "evidence_spans": _sanitize_evidence(data.get("evidence_spans", [])),
        "sarcasm_detected": bool(data.get("sarcasm_detected", False)),
        "title": title or "",
        "source_type": source_type or "unknown",
        # X rows carry the author handle as the display name (the UI renders
        # "X · @<source_name>"); domain_or_subreddit is literally "x.com" and
        # would render "X · @x.com". Handle-less X rows (author missing from
        # x_users_raw — the join is a LEFT JOIN) get None so the UI degrades
        # to a bare "X", matching narrative.py's _build_source_label.
        # News/Reddit keep domain/subreddit.
        "source_name": (
            (x_handle or None) if source_type == "x_post"
            else domain_or_subreddit
        ),
        "date": date_str,
        "full_text": text or "",
        "url": url,
    }


def _sample_dict_to_model(s: Dict[str, Any]) -> ClassificationSample:
    """Convert a sample dict into the ClassificationSample dataclass.
    Used in every list-level formatter to avoid repeated field hoisting."""
    return ClassificationSample(
        doc_id=s["doc_id"],
        label=s["label"],
        confidence=s["confidence"],
        reasoning=s["reasoning"],
        evidence_spans=s["evidence_spans"],
        sarcasm_detected=s["sarcasm_detected"],
        title=s.get("title"),
        source_type=s.get("source_type"),
        source_name=s.get("source_name"),
        date=s.get("date"),
        full_text=s.get("full_text", ""),
        url=s.get("url"),
    )


def _insert_capped(samples: List[Dict[str, Any]], sample: Dict[str, Any], cap: int) -> None:
    """Keep the list sorted by confidence desc and capped at ``cap``.
    De-dupes by doc_id so the same row can't show up twice from the
    ai_outputs table (which may have multiple rows per doc across prompt
    versions / reruns)."""
    if any(s["doc_id"] == sample["doc_id"] for s in samples):
        return
    if len(samples) < cap:
        samples.append(sample)
        samples.sort(key=lambda s: s["confidence"], reverse=True)
    elif sample["confidence"] > samples[-1]["confidence"]:
        samples[-1] = sample
        samples.sort(key=lambda s: s["confidence"], reverse=True)


def _collect_topic_sample(
    topic_samples: Dict[str, List[Dict[str, Any]]],
    topic: str,
    doc_id: int, label: str, confidence: float,
    data: Dict[str, Any],
    title: Optional[str], source_type: Optional[str], published_at: Optional[float],
    domain_or_subreddit: Optional[str], ident: Optional[str], text: Optional[str],
    x_handle: Optional[str] = None,
) -> None:
    if not data.get("reasoning"):
        return
    samples = topic_samples.setdefault(topic, [])
    sample = _build_sample_dict(
        doc_id, label, confidence, data, title, source_type,
        published_at, domain_or_subreddit, ident, text, x_handle,
    )
    _insert_capped(samples, sample, MAX_SAMPLES_PER_TOPIC)


def _collect_strength_sample(
    strength_samples: Dict[str, List[Dict[str, Any]]],
    bucket: str,
    doc_id: int, label: str, confidence: float,
    data: Dict[str, Any],
    title: Optional[str], source_type: Optional[str], published_at: Optional[float],
    domain_or_subreddit: Optional[str], ident: Optional[str], text: Optional[str],
    x_handle: Optional[str] = None,
) -> None:
    """Append a sample to one of the five STRENGTH_BUCKETS. Silently
    drops rows for unknown buckets so callers can pass the key through
    without branching."""
    samples = strength_samples.get(bucket)
    if samples is None or not data.get("reasoning"):
        return
    sample = _build_sample_dict(
        doc_id, label, confidence, data, title, source_type,
        published_at, domain_or_subreddit, ident, text, x_handle,
    )
    _insert_capped(samples, sample, MAX_DISTRIBUTION_SAMPLES_PER_BUCKET)


def _collect_entity_sample(
    entity_accum: Dict[str, Any],
    doc_id: int, label: str, confidence: float,
    data: Dict[str, Any],
    title: Optional[str], source_type: Optional[str], published_at: Optional[float],
    domain_or_subreddit: Optional[str], ident: Optional[str], text: Optional[str],
    x_handle: Optional[str] = None,
) -> None:
    """Bump an entity bucket's counters + append a sample when there's room."""
    label_key = _LABEL_MAP.get(label, "neutral")
    entity_accum[label_key] += 1
    entity_accum["volume"] += 1
    if not data.get("reasoning"):
        return
    sample = _build_sample_dict(
        doc_id, label, confidence, data, title, source_type,
        published_at, domain_or_subreddit, ident, text, x_handle,
    )
    _insert_capped(entity_accum["samples"], sample, MAX_SAMPLES_PER_ENTITY)


# --------------------------------------------------------------------------- #
#  Entity routing wrapper                                                     #
# --------------------------------------------------------------------------- #

def _route_and_record(
    accum: Dict[str, Any],
    registry,
    source_type: Optional[str],
    domain_or_subreddit: Optional[str],
    x_handle: Optional[str],
    doc_id: int,
    label: str,
    conf: float,
    data: Dict[str, Any],
    title: Optional[str],
    published_at: Any,
    ident: Optional[str],
    text: Optional[str],
    is_official_tier: bool = False,
) -> Optional[str]:
    """Resolve the row's tier + entity via the shared entity_routing
    module, then bucket the row into the right per-entity accumulator
    (sentiment-specific shape).

    ``is_official_tier`` carries the ingestor's x_posts_raw provenance flag
    so a post fetched via the verified-officials timeline lands in the
    officials tier even when its stored handle doesn't match the editorial
    registry (audit D-4).

    Returns the tier label ('news' | 'officials' | 'public') for use by
    the per-topic three-way split; None for unknown source_types.
    """
    tier, entity = resolve_entity(
        registry, source_type, domain_or_subreddit, x_handle,
        is_official_tier=is_official_tier,
    )
    if tier is None:
        return None

    if tier == "news":
        bucket_dict = accum["by_news_outlet"]
        if entity is not None:
            key, profile, kind = entity.domain, entity.profile_dict(), "outlet"
        else:
            key = CATCH_ALL_OUTLETS
            profile = catch_all_profile(
                CATCH_ALL_OUTLETS, "Other news outlets",
                "News docs whose domain is not in the tracked outlet registry.",
            )
            kind = "catch_all"
    elif tier == "officials":
        bucket_dict = accum["by_official"]
        if entity is not None:
            key, profile, kind = entity.handle, entity.profile_dict(), "official"
        else:
            # Routed to officials purely by the is_official_tier provenance
            # flag (verified-officials timeline pull) with no editorial entity
            # to render — bucket into a dedicated verified-officials catch-all.
            key = CATCH_ALL_VERIFIED_OFFICIALS
            profile = catch_all_profile(
                CATCH_ALL_VERIFIED_OFFICIALS, "Verified officials",
                "X posts pulled via the verified-officials timeline whose "
                "handle is not individually in the editorial officials registry.",
            )
            kind = "catch_all"
    else:  # public
        bucket_dict = accum["by_general_public"]
        if entity is not None:
            key, profile, kind = entity.subreddit, entity.profile_dict(), "subreddit"
        elif source_type == "x_post":
            key = CATCH_ALL_X_USERS
            profile = catch_all_profile(
                CATCH_ALL_X_USERS, "Other X users",
                "X posts whose author is not in the tracked officials registry.",
            )
            kind = "catch_all"
        else:
            key = CATCH_ALL_SUBREDDITS
            profile = catch_all_profile(
                CATCH_ALL_SUBREDDITS, "Other subreddits",
                "Reddit posts whose subreddit is not in the tracked subreddit registry.",
            )
            kind = "catch_all"

    _init_entity_bucket(bucket_dict, key, kind, profile)
    _collect_entity_sample(
        bucket_dict[key], doc_id, label, conf, data,
        title, source_type, published_at, domain_or_subreddit, ident, text, x_handle,
    )
    return tier


def _init_entity_bucket(
    bucket: Dict[str, Dict[str, Any]],
    key: str,
    kind: str,
    profile: Dict[str, Any],
) -> None:
    """Create a fresh per-entity accumulator if ``key`` isn't present."""
    if key in bucket:
        return
    bucket[key] = {
        "kind": kind, "profile": profile,
        "positive": 0, "negative": 0, "neutral": 0, "volume": 0,
        "samples": [],
    }


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


def _increment_bucket(bucket: Dict[str, Dict[str, int]], key: str, label_key: str) -> None:
    bucket.setdefault(key, {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0})
    bucket[key][label_key] += 1


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


def _format_entity_items(bucket: Dict[str, Dict[str, Any]]) -> List[EntitySentimentItem]:
    """Materialize per-entity accumulators into EntitySentimentItem, with
    real registry entities first and catch-all buckets sorted to the end."""
    items: List[EntitySentimentItem] = []
    for key, stats in bucket.items():
        volume = stats["volume"]
        if volume == 0:
            continue
        net = (stats["positive"] - stats["negative"]) / volume * 100
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


# --------------------------------------------------------------------------- #
#  GOP favorability merge                                                     #
# --------------------------------------------------------------------------- #

def _merge_favorability_data(
    result: PublicSentimentResult,
    fav_rows: List[tuple],
    bot_docs: Set[int],
    allowed_sources: Optional[frozenset],
    cache: SnapshotCache,
) -> None:
    """Merge GOP favorability into the sentiment result. No-op when
    every row is filtered (bot-flagged / wrong source / parse failure)
    so downstream consumers see a clean 'no data' via the default
    None fields."""
    distribution, by_platform, daily_net, count = _parse_favorability_rows(
        fav_rows, bot_docs, allowed_sources,
    )
    if count == 0:
        return
    _format_favorability_result(result, distribution, by_platform, daily_net, count, cache)


def _parse_favorability_rows(
    fav_rows: List[tuple],
    bot_docs: Set[int],
    allowed_sources: Optional[frozenset],
) -> tuple:
    distribution = {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0}
    by_platform: Dict[str, Dict[str, int]] = {}
    daily_net: Dict[str, Dict[str, Any]] = {}
    count = 0

    for doc_id, output_json, _confidence, source_type, pub_at in fav_rows:
        if doc_id in bot_docs:
            continue
        if allowed_sources is not None and (source_type or "") not in allowed_sources:
            continue
        try:
            data = json.loads(output_json)
        except json.JSONDecodeError:
            continue

        stance = data.get("overall_gop_stance", "neutral")
        if stance not in distribution:
            stance = "neutral"
        distribution[stance] += 1

        platform = source_type or "unknown"
        if platform in ("reddit_post", "reddit_comment"):
            platform = "reddit"
        by_platform.setdefault(platform, {"favorable": 0, "unfavorable": 0, "neutral": 0})
        if stance in by_platform[platform]:
            by_platform[platform][stance] += 1

        if pub_at:
            _track_daily_favorability(daily_net, pub_at, stance)

        count += 1

    return distribution, by_platform, daily_net, count


def _track_daily_favorability(
    daily_net: Dict[str, Dict[str, Any]],
    pub_at: float,
    stance: str,
) -> None:
    try:
        date_str = datetime.fromtimestamp(pub_at).strftime("%Y-%m-%d")
    except (ValueError, TypeError, OSError):
        return
    bucket = daily_net.setdefault(date_str, {"score": 0, "count": 0})
    bucket["score"] += 1 if stance == "favorable" else (-1 if stance == "unfavorable" else 0)
    bucket["count"] += 1


def _format_favorability_result(
    result: PublicSentimentResult,
    distribution: Dict[str, int],
    by_platform: Dict[str, Dict[str, int]],
    daily_net: Dict[str, Dict[str, Any]],
    count: int,
    cache: SnapshotCache,
) -> None:
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
        {
            "date": date,
            "value": round((stats["score"] / stats["count"]) * 100, 1) if stats["count"] > 0 else 0,
        }
        for date, stats in sorted(daily_net.items())
    ][-30:]

    result.gopByPlatform = [
        {
            "group": platform.capitalize(),
            "favorable": stats["favorable"],
            "unfavorable": stats["unfavorable"],
            "neutral": stats["neutral"],
        }
        for platform, stats in by_platform.items()
    ]

    polling_data = cache.load("polling_gop")
    if polling_data:
        result.pollingVsSocial = {
            "onlineSentiment": {
                "favorable": result.gopFavorability["favorable"],
                "unfavorable": result.gopFavorability["unfavorable"],
                "neutral": result.gopFavorability["neutral"],
            },
            "pollingData": polling_data,
        }
