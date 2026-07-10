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
import math
import re
from datetime import datetime
from typing import Any, Dict, List, Optional, Set, Tuple

from analysis.src.common.cache import SnapshotCache
from analysis.src.common.settings import get_settings
from analysis.src.reporting.aggregators.base import (
    ACCOUNT_PROFILE_JOIN_SQL,
    X_AUTHOR_JOIN_SQL,
    fetch_task_rows,
    get_bot_flagged_doc_ids,
    get_connection,
    get_high_bot_score_author_ids,
    get_time_cutoff,
)
from analysis.src.reporting.aggregators.constants import (
    NEWS_PLATFORMS, SOCIAL_PLATFORMS, STRONG_CONFIDENCE_THRESHOLD, TOPIC_KEYWORDS,
)
from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS, CATCH_ALL_SUBREDDITS, CATCH_ALL_X_USERS,
    DEM_COLLECTIVE, GOP_COLLECTIVE,
    account_profile_dict, canonicalize_handle, catch_all_profile,
    get_registry, resolve_entity,
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
MAX_SAMPLES_PER_TARGET = 5
MAX_NARRATIVES_PER_TARGET = 5

# Account-level bot exclusion floor for received tone: (doc, target) pairs
# authored by an X account whose author_bot_scores.score meets this are
# withheld from received-tone denominators. Doc-level bot exclusion already
# runs via bot_docs; this catches accounts whose pattern-of-posting scores
# bot-like even when individual posts pass. Excluded counts are surfaced in
# targetTone metadata — never silently dropped.
BOT_SCORE_AUTHOR_EXCLUSION = 0.5

# Received-tone suppression floor: a net score computed from fewer than this
# many (doc, target) pairs is withheld (net=None, lowSample=True) — one
# classified tweet must never render as a +100.0 headline. The volume is
# always emitted so the UI can show the honest n.
MIN_TARGET_SAMPLE_N = 5

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
            # carry the post author's handle for registry matching, plus the
            # curated account classification (account_profiles) so classified
            # non-registry authors get named cards instead of the catch-all.
            # No-op for non-X rows; u.*/x.*/ap.* are NULL and entity routing
            # handles that downstream (walkthrough 057).
            sentiment_rows = fetch_task_rows(
                cursor,
                "SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.published_at, d.title, d.domain_or_subreddit, d.ident, d.text, u.username, x.is_official_tier, "
                "ap.tier, ap.full_name, ap.party, ap.office_title, ap.account_type",
                task_type="sentiment",
                cutoff=cutoff,
                min_confidence=min_conf,
                extra_joins=f"{X_AUTHOR_JOIN_SQL} {ACCOUNT_PROFILE_JOIN_SQL}",
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
                       + COALESCE(x.like_count, 0) + COALESCE(x.quote_count, 0)
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
        }
        now = datetime.now()

        for (
            doc_id, output_json, confidence, source_type, published_at,
            title, domain_or_subreddit, ident, text, x_handle, is_official_tier,
            ap_tier, ap_full_name, ap_party, ap_office_title, ap_account_type,
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
            # LLM-extracted topic (dominant target_mentions topic for this
            # doc) wins; title-keyword heuristic is the fallback for docs
            # with no resolved mention topic; "General" is the honest
            # no-signal bucket.
            topic = doc_topics.get(doc_id) or _extract_topic(title)
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
                x_handle, topic=topic,
            )

            account = None
            if ap_tier:
                account = {
                    "tier": ap_tier, "full_name": ap_full_name,
                    "party": ap_party, "office_title": ap_office_title,
                    "account_type": ap_account_type,
                }
            tier = _route_and_record(
                accum, registry, source_type, domain_or_subreddit, x_handle,
                doc_id, label, conf, data, title, published_at, ident, text,
                is_official_tier=bool(is_official_tier),
                account=account,
                topic=topic,
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
    topic: Optional[str] = None,
) -> Dict[str, Any]:
    """Build the dict representation of a classification sample used by
    every collector. Centralizing this avoids the 3-place duplication
    that pre-split sentiment.py was carrying.

    ``topic`` is the doc's topic attribution (LLM mention topic with
    keyword fallback — see _fetch_doc_topics) so the UI can filter samples
    exactly instead of re-guessing with client-side keyword lists."""
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
        "topic": topic,
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
        topic=s.get("topic"),
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
        topic=topic,
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
    topic: Optional[str] = None,
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
        topic=topic,
    )
    _insert_capped(samples, sample, MAX_DISTRIBUTION_SAMPLES_PER_BUCKET)


def _collect_entity_sample(
    entity_accum: Dict[str, Any],
    doc_id: int, label: str, confidence: float,
    data: Dict[str, Any],
    title: Optional[str], source_type: Optional[str], published_at: Optional[float],
    domain_or_subreddit: Optional[str], ident: Optional[str], text: Optional[str],
    x_handle: Optional[str] = None,
    topic: Optional[str] = None,
) -> None:
    """Bump an entity bucket's counters + append a sample when there's room."""
    label_key = _LABEL_MAP.get(label, "neutral")
    entity_accum[label_key] += 1
    entity_accum["volume"] += 1
    if topic is not None:
        _increment_bucket(entity_accum["by_topic"], topic, label_key)
    if not data.get("reasoning"):
        return
    sample = _build_sample_dict(
        doc_id, label, confidence, data, title, source_type,
        published_at, domain_or_subreddit, ident, text, x_handle,
        topic=topic,
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
    account: Optional[Dict[str, Any]] = None,
    topic: Optional[str] = None,
) -> Optional[str]:
    """Resolve the row's tier + entity via the shared entity_routing
    module, then bucket the row into the right per-entity accumulator
    (sentiment-specific shape).

    ``is_official_tier`` carries the ingestor's x_posts_raw provenance flag
    so a post fetched via the verified-officials timeline lands in the
    officials tier even when its stored handle doesn't match the editorial
    registry (audit D-4).

    ``account`` is the author's curated account_profiles classification
    (tier / full_name / party / office_title / account_type), or None for
    unclassified authors. A classified X author who isn't in the editorial
    registry gets a named per-account card (kind='account') instead of
    disappearing into the "Other X users" catch-all; the
    ``elected_official`` tier additionally upgrades the row to the
    officials column.

    Returns the tier label ('news' | 'officials' | 'public') for use by
    the per-topic three-way split; None for unknown source_types.
    """
    tier, entity = resolve_entity(
        registry, source_type, domain_or_subreddit, x_handle,
        is_official_tier=is_official_tier,
    )
    if tier is None:
        return None

    account_key = None
    if (
        source_type == "x_post" and entity is None and account is not None
        and x_handle and account.get("tier") in ("elected_official", "affiliated")
    ):
        account_key = canonicalize_handle(x_handle)
        if account["tier"] == "elected_official":
            tier = "officials"

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
        elif account_key is not None:
            key = account_key
            profile = account_profile_dict(
                account_key, account["tier"], account.get("full_name"),
                account.get("party"), account.get("office_title"),
                account.get("account_type"),
            )
            kind = "account"
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
        elif account_key is not None:
            key = account_key
            profile = account_profile_dict(
                account_key, account["tier"], account.get("full_name"),
                account.get("party"), account.get("office_title"),
                account.get("account_type"),
            )
            kind = "account"
        elif source_type == "x_post":
            key = CATCH_ALL_X_USERS
            profile = catch_all_profile(
                CATCH_ALL_X_USERS, "Other X users",
                "X posts whose author is not in the tracked officials registry "
                "or the curated political-accounts list.",
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
        topic=topic,
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
        # Per-topic stance counts for the entity's own posts — powers the
        # topic-scoped expressed score in the profile modal.
        "by_topic": {},
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


# --------------------------------------------------------------------------- #
#  Target-tone merge — received vs. expressed split                            #
# --------------------------------------------------------------------------- #

_STANCE_KEYS = ("positive", "negative", "neutral", "mixed")


def _empty_stance_counts() -> Dict[str, int]:
    return {k: 0 for k in _STANCE_KEYS}


def _net_or_none(counts: Dict[str, int], min_n: int = MIN_TARGET_SAMPLE_N) -> Optional[float]:
    """Net score for a stance-count cell, or None when below the suppression
    floor — small-n cells report their volume but never a headline number."""
    total = sum(counts.values())
    if total < min_n:
        return None
    return round((counts["positive"] - counts["negative"]) / total * 100, 1)


def _normalize_party(party: Optional[str]) -> Optional[str]:
    """Collapse registry party values to the R/D axis used for alignment.
    ``independent-dem`` caucuses with Democrats; anything else is outside
    the two-party alignment frame and returns None."""
    if not party:
        return None
    p = party.strip().lower()
    if p == "r":
        return "R"
    if p in ("d", "independent-dem"):
        return "D"
    return None


def _fetch_narrative_doc_map(cursor, cutoff: Optional[int]) -> Dict[int, List[Tuple[int, str]]]:
    """doc_id → [(narrative_id, name)] for docs clustered into narratives,
    bounded by the time window. Lets received tone answer WHICH recurring
    claims drive the mentions of an official, not just how many there are.
    Empty when the narrative tables haven't been created (fresh/test DBs)."""
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='narrative_docs'"
    )
    if not cursor.fetchone():
        return {}
    sql = (
        "SELECT nd.doc_id, n.narrative_id, n.name "
        "FROM narrative_docs nd JOIN narratives n ON n.narrative_id = nd.narrative_id"
    )
    params: tuple = ()
    if cutoff is not None:
        sql += " JOIN docs d ON d.doc_id = nd.doc_id WHERE d.published_at >= ?"
        params = (cutoff,)
    cursor.execute(sql, params)
    out: Dict[int, List[Tuple[int, str]]] = {}
    for doc_id, narrative_id, name in cursor.fetchall():
        out.setdefault(doc_id, []).append((narrative_id, name))
    return out


def _fetch_doc_topics(cursor, min_conf: float) -> Dict[int, str]:
    """doc_id → dominant LLM-extracted topic from target_mentions.

    The schema-enforced per-target topic (TARGET_TOPIC_ENUM) is the real
    topic signal; the title-keyword heuristic (_extract_topic) is only the
    fallback for docs with no resolved mention topic. Joined through
    ai_outputs_latest so superseded outputs' mentions don't vote. 'Other'
    is excluded — an all-'Other' doc falls through to the keyword fallback
    rather than surfacing a bucket that overlaps with "General". Ties break
    by higher mention count, then alphabetically (stable across runs).
    Empty when the table hasn't been created (fresh/test DBs)."""
    cursor.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='target_mentions'"
    )
    if not cursor.fetchone():
        return {}
    cursor.execute(
        """
        SELECT m.doc_id, m.topic, COUNT(*) AS n
        FROM target_mentions m
        JOIN ai_outputs_latest a ON a.output_id = m.output_id
        WHERE m.topic IS NOT NULL AND m.topic != 'Other'
          AND m.confidence >= ?
        GROUP BY m.doc_id, m.topic
        ORDER BY m.doc_id, n DESC, m.topic
        """,
        (min_conf,),
    )
    topics: Dict[int, str] = {}
    for doc_id, topic, _n in cursor.fetchall():
        topics.setdefault(doc_id, topic)
    return topics


def _speaker_tier(tier: Optional[str], ap_tier: Optional[str]) -> Optional[str]:
    """Coarse WHO-is-talking bucket for a target_sentiment row: 'officials'
    (registry match, provenance flag, or curated elected_official tier),
    'affiliated' (curated politically-affiliated accounts), else the
    resolve_entity tier ('news' | 'public' | None)."""
    if tier == "officials" or ap_tier == "elected_official":
        return "officials"
    if ap_tier == "affiliated":
        return "affiliated"
    return tier


def _engagement_weight(engagement: Any) -> float:
    """Log-damped reach-proxy weight for one (doc, target) pair: 1 for zero
    or unknown engagement, growing sub-linearly so one viral post can't
    single-handedly own an official's weighted net."""
    try:
        e = max(0, int(engagement or 0))
    except (TypeError, ValueError):
        e = 0
    return 1.0 + math.log1p(e)


def _format_received(accum: Dict[str, Any]) -> Dict[str, Any]:
    """Materialize a per-target accumulator into the wire shape stored on
    EntitySentimentItem.received / targetTone.collectives."""
    counts = accum["counts"]
    volume = sum(counts.values())
    low_sample = volume < MIN_TARGET_SAMPLE_N
    by_topic = []
    for topic, topic_counts in accum["by_topic"].items():
        topic_volume = sum(topic_counts.values())
        by_topic.append({
            "topic": topic,
            "net": _net_or_none(topic_counts),
            "volume": topic_volume,
            "lowSample": topic_volume < MIN_TARGET_SAMPLE_N,
        })
    by_topic.sort(key=lambda c: -c["volume"])

    by_speaker_tier = []
    for speaker, tier_counts in accum["by_speaker"].items():
        tier_volume = sum(tier_counts.values())
        by_speaker_tier.append({
            "tier": speaker,
            "net": _net_or_none(tier_counts),
            "volume": tier_volume,
            "lowSample": tier_volume < MIN_TARGET_SAMPLE_N,
        })
    by_speaker_tier.sort(key=lambda c: -c["volume"])

    by_narrative = []
    for nid, cell in accum["by_narrative"].items():
        n_volume = sum(cell["counts"].values())
        by_narrative.append({
            "narrativeId": nid,
            "name": cell["name"],
            "net": _net_or_none(cell["counts"]),
            "volume": n_volume,
            "lowSample": n_volume < MIN_TARGET_SAMPLE_N,
        })
    by_narrative.sort(key=lambda c: -c["volume"])
    by_narrative = by_narrative[:MAX_NARRATIVES_PER_TARGET]

    # Engagement-weighted net shares the raw-pair suppression floor: the
    # weighting changes emphasis within a sample, never rescues a thin one.
    weighted_net = None
    if not low_sample and accum["w_total"] > 0:
        weighted_net = round(
            (accum["w_pos"] - accum["w_neg"]) / accum["w_total"] * 100, 1
        )

    return {
        "net": _net_or_none(counts),
        "volume": volume,
        "lowSample": low_sample,
        "engagementWeightedNet": weighted_net,
        "byTopic": by_topic,
        "bySpeakerTier": by_speaker_tier,
        "byNarrative": by_narrative,
        "samples": accum["samples"],
    }


def _get_or_create_official_item(
    result: PublicSentimentResult,
    by_key: Dict[str, EntitySentimentItem],
    registry,
    key: str,
) -> Optional[EntitySentimentItem]:
    """Find the official's expressed-tone item, or create a zero-volume one.

    An official who never posted in the window but is discussed by others
    still needs a card — received tone is exactly the metric that exists
    without their participation."""
    item = by_key.get(key)
    if item is not None:
        return item
    official = registry.officials.get(key)
    if official is None:
        return None
    item = EntitySentimentItem(
        key=key, kind="official",
        positive=0, negative=0, neutral=0, volume=0, netScore=0.0,
        entity_profile=official.profile_dict(),
    )
    result.byOfficial.append(item)
    by_key[key] = item
    return item


def _merge_target_tone(
    result: PublicSentimentResult,
    target_rows: List[tuple],
    bot_docs: Set[int],
    bot_score_authors: Optional[Set[str]] = None,
    narrative_map: Optional[Dict[int, List[Tuple[int, str]]]] = None,
) -> None:
    """Merge pre-resolved target_mentions rows (one per (doc, target),
    identity frozen at write time — migration 025) into the result:

    * ``received`` onto each tracked official's EntitySentimentItem — the
      reputational signal (how sampled posts talk ABOUT them), kept separate
      from netScore, which stays the expressed signal (their own posts).
      Enriched with an engagement-weighted net (reach proxy), a
      by-speaker-tier split (WHO is talking: news / officials / affiliated /
      public), and the top narratives driving the mentions.
    * ``expressed_alignment`` per official speaker — stance toward same-party
      vs cross-party tracked targets, the control that separates "criticizing
      the other side" (expected) from something newsworthy.
    * ``result.targetTone`` — suppression threshold, resolution coverage,
      party-collective rollups, global alignment baselines, and the count of
      mentions excluded because their author's account-level bot score is
      high (``bot_score_authors``, from author_bot_scores).

    The per-mention confidence floor is applied by the SQL fetch (row-level
    confidence is a mean and would hide a confident target next to an
    uncertain one). Unresolved mentions arrive as rows with a NULL
    entity_key — persisted, counted, never silently dropped. No-op when no
    rows exist, so pre-target snapshots keep their exact shape.
    """
    if not target_rows:
        return
    bot_score_authors = bot_score_authors or set()
    narrative_map = narrative_map or {}

    registry = get_registry()
    received: Dict[str, Dict[str, Any]] = {}
    alignment: Dict[str, Dict[str, Dict[str, int]]] = {}
    baseline = {"same": _empty_stance_counts(), "cross": _empty_stance_counts()}
    resolved_mentions = 0
    unresolved_mentions = 0
    bot_excluded_mentions = 0

    # Per-doc context cache: speaker resolution, engagement weight, doc
    # narratives, and the doc-level reasoning are identical for every
    # mention of the same doc.
    doc_ctx: Dict[int, Dict[str, Any]] = {}

    for (
        doc_id, key, kind, party, stance, topic, conf, evidence_json,
        output_json, source_type, published_at, title, domain_or_subreddit,
        ident, text, x_handle, is_official_tier, author_id, ap_tier,
        engagement,
    ) in target_rows:
        if doc_id in bot_docs:
            continue
        if author_id and author_id in bot_score_authors:
            # Account-level exclusion: the author's cross-post bot rollup is
            # high, so none of their stances count toward received tone. The
            # count keeps the exclusion auditable.
            bot_excluded_mentions += 1
            continue
        if stance not in _STANCE_KEYS:
            continue

        ctx = doc_ctx.get(doc_id)
        if ctx is None:
            try:
                reasoning = (json.loads(output_json) or {}).get("reasoning") or ""
            except json.JSONDecodeError:
                reasoning = ""
            # Speaker resolution for the alignment control: only posts
            # authored by a registry-matched official carry a speaker party.
            speaker_key = None
            speaker_party = None
            tier, entity = resolve_entity(
                registry, source_type, domain_or_subreddit, x_handle,
                is_official_tier=bool(is_official_tier),
            )
            if tier == "officials" and entity is not None:
                speaker_key = entity.handle
                speaker_party = _normalize_party(entity.party)
            ctx = {
                "reasoning": reasoning,
                "weight": _engagement_weight(engagement),
                "narratives": narrative_map.get(doc_id, []),
                "speaker_key": speaker_key,
                "speaker_party": speaker_party,
                "speaker_tier": _speaker_tier(tier, ap_tier),
            }
            doc_ctx[doc_id] = ctx

        if key is None:
            unresolved_mentions += 1
            continue
        resolved_mentions += 1
        weight = ctx["weight"]

        accum = received.setdefault(key, {
            "kind": kind,
            "counts": _empty_stance_counts(),
            "by_topic": {},
            "by_speaker": {},
            "by_narrative": {},
            "w_pos": 0.0, "w_neg": 0.0, "w_total": 0.0,
            "samples": [],
        })
        accum["counts"][stance] += 1
        accum["by_topic"].setdefault(
            topic or "Other", _empty_stance_counts()
        )[stance] += 1
        if ctx["speaker_tier"] is not None:
            accum["by_speaker"].setdefault(
                ctx["speaker_tier"], _empty_stance_counts()
            )[stance] += 1
        for nid, n_name in ctx["narratives"]:
            cell = accum["by_narrative"].setdefault(
                nid, {"name": n_name, "counts": _empty_stance_counts()},
            )
            cell["counts"][stance] += 1
        accum["w_total"] += weight
        if stance == "positive":
            accum["w_pos"] += weight
        elif stance == "negative":
            accum["w_neg"] += weight

        try:
            evidence_spans = json.loads(evidence_json) if evidence_json else []
        except json.JSONDecodeError:
            evidence_spans = []
        sample = _build_sample_dict(
            doc_id, stance.upper(), conf,
            {"reasoning": ctx["reasoning"], "evidence_spans": evidence_spans},
            title, source_type, published_at, domain_or_subreddit, ident, text,
            x_handle, topic=topic or "Other",
        )
        _insert_capped(accum["samples"], sample, MAX_SAMPLES_PER_TARGET)

        # Alignment cell — self-references are excluded: "same party"
        # means another entity on the speaker's side, not self-praise.
        target_party = _normalize_party(party)
        if (ctx["speaker_key"] and ctx["speaker_party"] and target_party
                and key != ctx["speaker_key"]):
            cell = "same" if ctx["speaker_party"] == target_party else "cross"
            cells = alignment.setdefault(ctx["speaker_key"], {
                "same": _empty_stance_counts(), "cross": _empty_stance_counts(),
            })
            cells[cell][stance] += 1
            baseline[cell][stance] += 1

    by_key = {item.key: item for item in result.byOfficial}
    collectives: Dict[str, Dict[str, Any]] = {}
    for key, accum in received.items():
        formatted = _format_received(accum)
        if key in (GOP_COLLECTIVE, DEM_COLLECTIVE):
            collectives[key] = formatted
            continue
        item = _get_or_create_official_item(result, by_key, registry, key)
        if item is not None:
            item.received = formatted

    for speaker_key, cells in alignment.items():
        item = _get_or_create_official_item(result, by_key, registry, speaker_key)
        if item is None:
            continue
        item.expressed_alignment = {
            "samePartyNet": _net_or_none(cells["same"]),
            "samePartyVolume": sum(cells["same"].values()),
            "crossPartyNet": _net_or_none(cells["cross"]),
            "crossPartyVolume": sum(cells["cross"].values()),
        }

    # Re-sort so officials created target-only slot in with the rest.
    result.byOfficial.sort(key=lambda it: (it.kind == "catch_all", -it.volume))

    result.targetTone = {
        "minSampleN": MIN_TARGET_SAMPLE_N,
        "resolvedMentions": resolved_mentions,
        "unresolvedMentions": unresolved_mentions,
        "botExcludedMentions": bot_excluded_mentions,
        "engagementWeight": (
            "1 + ln(1 + retweets + replies + likes + quotes) per sampled "
            "post; engagement counts are a reach proxy, not verified reach"
        ),
        "collectives": collectives,
        "baselines": {
            "samePartyNet": _net_or_none(baseline["same"]),
            "samePartyVolume": sum(baseline["same"].values()),
            "crossPartyNet": _net_or_none(baseline["cross"]),
            "crossPartyVolume": sum(baseline["cross"].values()),
        },
    }
