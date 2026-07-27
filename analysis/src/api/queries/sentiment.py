"""
GET /api/v1/sentiment aggregation: net tone, intensity distribution,
platform/topic/time-of-day/day-of-week splits, the news/officials/
general-public tier split, and per-entity stance aggregates -- computed
live against corpus.documents + analysis.runs/sentiment_results/
target_mentions. See docs/audit-trail/api/2026-07-24-phase9-sentiment-entities.md.
"""

from __future__ import annotations

import math
from datetime import datetime
from typing import Any, Dict, List, Mapping, Optional, Tuple

from analysis.src.api.models.common import LeanLabel, RangeMeta, SampleDocModel
from analysis.src.api.models.sentiment import (
    DailySentiment,
    DayOfWeekSentiment,
    EntityDailyTonePoint,
    EntitySentimentItem,
    EntityStanceAggregate,
    EntityTopicCell,
    ExpressedAlignment,
    OutboundTargetCell,
    OutboundTargets,
    PlatformSentiment,
    ReceivedNarrativeCell,
    ReceivedSpeakerTierCell,
    ReceivedTone,
    ReceivedTopicCell,
    SentimentDistribution,
    SentimentOverview,
    SentimentPanelResponse,
    StanceCounts,
    TargetToneMeta,
    TierSplit,
    TierStanceCounts,
    TimeOfDaySentiment,
    ToneTrendCell,
    ToneTrendPoint,
    TopicSentiment,
    TopicStanceCounts,
)
from analysis.src.api.queries.base import (
    build_classification_sample,
    build_sample_doc,
    fetch_rich_sample_fields,
    resolve_time_range,
    split_admission_counts,
)
from analysis.src.api.queries.constants import (
    BOT_FLAGGED_SHARE_EXCLUSION,
    DEM_TARGET_ALIASES,
    GOP_TARGET_ALIASES,
    MAX_DISTRIBUTION_SAMPLES_PER_BUCKET,
    MAX_SAMPLED_AUTHOR_CARDS,
    MAX_SAMPLES_PER_ENTITY,
    MAX_SAMPLES_PER_TARGET,
    MIN_SAMPLED_AUTHOR_FOLLOWERS,
    MIN_SAMPLED_AUTHOR_POSTS,
    MIN_TARGET_SAMPLE_N,
    SNIPPET_MAX_CHARS,
    STRONG_CONFIDENCE_THRESHOLD,
)
from analysis.src.api.queries.profiles import (
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    catch_all_profile,
    fetch_entity_profiles,
    sampled_account_profile,
)
from analysis.src.common.db import connection
from analysis.src.common.settings import get_settings

# --------------------------------------------------------------------------- #
#  Module constants (sentiment-panel-local; see queries/constants.py for the  #
#  floors/caps ported from the pre-redesign aggregators)                     #
# --------------------------------------------------------------------------- #

DAY_OF_WEEK_LABELS = ("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun")

DISCLAIMER = "Represents sampled platform discourse, not verified population sentiment"

UNRESOLVED_CATCH_ALL_KEY = "unresolved"

# Stance vocabulary on analysis.target_mentions.stance (sentiment_label enum).
_STANCE_KEYS = ("positive", "negative", "neutral", "mixed")

# Trailing-day cap for a per-entity dailyTone series -- mirrors the panel-
# level `daily` convention but scoped to one entity's own posts.
DAILY_TONE_MAX_DAYS = 30

# Per-panel keys for the intensity-drill-down sample buckets. camelCase --
# unlike SentimentDistribution's own Python fields (aliased to camelCase by
# CamelModel), these are Dict keys, which pydantic's alias_generator does
# NOT touch, so the wire key is whatever string is used here. Matches the
# pre-redesign SentimentSegmentKey vocabulary verbatim so the old JSX's
# `distributionSamples.strongPositive`-style lookups keep working.
DISTRIBUTION_SEGMENTS = (
    "strongPositive", "mildPositive", "neutral", "mildNegative", "strongNegative",
)

# Rich samples per calendar day (Tone-over-time chart's day drill-down) --
# not in queries/constants.py, sentiment-panel-local per the task brief.
DAY_SAMPLES_MAX_PER_DAY = 8

# Top narratives driving an entity's received tone, ported from the
# pre-redesign target_tone.py.
MAX_NARRATIVES_PER_TARGET = 5

# Outbound-target rollup ("who this bucket talks about"): named rows are
# capped; an unresolved raw_target needs to recur before earning its own row.
MAX_OUTBOUND_TARGETS = 8
MIN_OUTBOUND_RAW_RECURRENCE = 2
OUTBOUND_OTHER_LABEL = "Other targets"

# NULL-safe bound predicate: either side of the (start, end) pair may be
# None (unbounded) -- resolve_time_range() is the only place that decides
# what None means (a preset, 'all', or an open-ended custom range).
_RANGE_PREDICATE = (
    "(%(start)s::timestamptz IS NULL OR d.published_at >= %(start)s) "
    "AND (%(end)s::timestamptz IS NULL OR d.published_at <= %(end)s)"
)

_BOT_EXCLUSION_SQL = """
    NOT EXISTS (
        SELECT 1 FROM analysis.author_bot_scores b
        WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
    )
"""


def get_sentiment_panel(
    window: Optional[str] = None,
    date_from: Optional[datetime] = None,
    date_to: Optional[datetime] = None,
) -> SentimentPanelResponse:
    """Assemble the full sentiment panel for a preset ``window`` (or 'all')
    XOR an explicit ``date_from``/``date_to`` range -- see
    queries/base.py::resolve_time_range."""
    start, end = resolve_time_range(window, date_from, date_to)
    min_conf = get_settings().aggregation_min_confidence
    params = {"start": start, "end": end, "min_conf": min_conf, "bot_floor": BOT_FLAGGED_SHARE_EXCLUSION}

    with connection() as conn:
        rows = conn.execute(_SENTIMENT_ROWS_SQL, params).fetchall()
        analyzed_rows = conn.execute(_TOTAL_ANALYZED_SQL, params).fetchall()
        excluded_bot_docs = conn.execute(_BOT_EXCLUDED_SQL, params).fetchone()["n"]
        doc_topics = _fetch_doc_topics(conn, params)
        target_rows = conn.execute(_TARGET_ROWS_SQL, params).fetchall()
        target_bot_excluded = conn.execute(_TARGET_BOT_EXCLUDED_SQL, params).fetchone()["n"]
        narrative_map = _fetch_narrative_map(conn, target_rows)

        buckets = _build_tier_buckets(rows, doc_topics)
        _consolidate_sampled_x_authors(buckets)
        editorial_official_ids = {
            row["entity_id"] for row in target_rows
            if row["entity_id"] is not None and row["kind"] == "official" and row["target_editorial"]
        }
        tier_entity_ids = {key[1] for key in buckets if key[0] == "entity"}
        profiles = fetch_entity_profiles(conn, tier_entity_ids | editorial_official_ids)

        received, collectives, alignment, baseline, resolved_mentions, unresolved_mentions = (
            _accumulate_target_tone(target_rows, narrative_map)
        )
        dist_pairs, day_pairs = _collect_distribution_and_day_samples(rows)
        doc_ids = _collect_all_sample_doc_ids(buckets, received, collectives, dist_pairs, day_pairs)
        rich_samples = fetch_rich_sample_fields(conn, doc_ids)

    range_meta = _build_range_meta(window, start, end, analyzed_rows)
    accum = _aggregate_rows(rows, doc_topics)
    entity_stances = _build_entity_stances(target_rows)

    tier_items, tier_lookup = _build_tier_items(buckets, profiles, rich_samples)
    _attach_official_stubs(tier_items, tier_lookup, editorial_official_ids, profiles, received)
    _attach_received(tier_lookup, received, rich_samples)
    _attach_expressed_alignment(tier_lookup, alignment)
    _attach_outbound(tier_lookup, buckets, target_rows)

    tone_trend = _build_tone_trend(rows)
    distribution_samples = {
        segment: _build_classification_samples(pairs, rich_samples) for segment, pairs in dist_pairs.items()
    }
    day_samples = {
        day: _build_classification_samples(pairs, rich_samples)
        for day, pairs in sorted(day_pairs.items(), reverse=True)
    }
    target_tone = TargetToneMeta(
        min_sample_n=MIN_TARGET_SAMPLE_N,
        resolved_mentions=resolved_mentions, unresolved_mentions=unresolved_mentions,
        bot_excluded_mentions=target_bot_excluded,
        collectives={
            "gop_collective": _format_received(collectives["gop_collective"], rich_samples),
            "dem_collective": _format_received(collectives["dem_collective"], rich_samples),
        },
        baselines=_format_alignment(baseline),
    )

    return _build_response(
        range_meta, accum, len(analyzed_rows), excluded_bot_docs, entity_stances,
        tier_items, target_tone, tone_trend, distribution_samples, day_samples,
    )


# --------------------------------------------------------------------------- #
#  SQL                                                                        #
# --------------------------------------------------------------------------- #

# Carries every column the own-post tier bucketing (by_news_outlet/
# by_official/by_general_public), toneTrend, distributionSamples, and
# daySamples need -- one row per analyzed doc, one query for all four.
_SENTIMENT_ROWS_SQL = f"""
    SELECT d.doc_id, d.source_type, d.published_at, d.source_url, d.title,
           d.admission_class, d.domain_or_subreddit, d.author_id,
           r.confidence, sr.label,
           ap.tier AS author_tier, ap.entity_id AS author_entity_id,
           e_auth.editorial AS author_entity_editorial, e_auth.kind AS author_entity_kind,
           na.outlet_entity_id, rp.subreddit_entity_id,
           au.handle AS x_handle, au.display_name AS x_display_name,
           au.description AS x_description, au.followers_count AS x_followers_count,
           (COALESCE(xp.retweet_count, 0) + COALESCE(xp.reply_count, 0)
            + COALESCE(xp.like_count, 0) + COALESCE(xp.quote_count, 0)) AS x_engagement
    FROM analysis.runs r
    JOIN analysis.sentiment_results sr ON sr.run_id = r.run_id
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
    LEFT JOIN corpus.entities e_auth ON e_auth.entity_id = ap.entity_id
    LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
    LEFT JOIN corpus.authors au ON au.author_id = d.author_id
    LEFT JOIN corpus.x_posts xp ON xp.doc_id = d.doc_id
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done'
      AND r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
"""

# Every analysis.runs row counted toward the panel's denominator (task='text',
# done, in-range, bot-excluded) -- not just the ones with a sentiment_results
# row, per the "trivial content has no sentiment row, never fake-neutral"
# rule. Carries admission_class + model_id for the RangeMeta honesty block.
_TOTAL_ANALYZED_SQL = f"""
    SELECT d.doc_id, d.admission_class, r.model_id
    FROM analysis.runs r
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done'
      AND r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
"""

_BOT_EXCLUDED_SQL = f"""
    SELECT COUNT(*) AS n
    FROM analysis.runs r
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    WHERE r.task = 'text' AND r.is_current AND r.status = 'done'
      AND r.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
"""

_DOC_TOPICS_SQL = f"""
    SELECT m.doc_id, m.topic, COUNT(*) AS n
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    WHERE r.is_current AND r.task = 'targets'
      AND m.topic IS NOT NULL AND m.topic <> 'Other'
      AND m.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
    GROUP BY m.doc_id, m.topic
    ORDER BY m.doc_id, n DESC, m.topic
"""

# Carries the target entity's own fields (received tone) AND the mentioning
# doc's routing/author fields (outbound "who they're talking about" +
# expressed_alignment) -- one row per (doc, target) mention.
_TARGET_ROWS_SQL = f"""
    SELECT m.entity_id, m.stance, m.topic, m.raw_target, m.confidence,
           d.doc_id, d.source_type, d.source_url, d.published_at, d.title,
           d.admission_class, d.domain_or_subreddit, d.author_id,
           e.display_name, e.kind, e.lean, e.entity_key,
           e.lean_source AS target_lean_source, e.editorial AS target_editorial,
           ap.tier AS author_tier, ap.entity_id AS author_entity_id,
           e_auth.editorial AS author_entity_editorial, e_auth.kind AS author_entity_kind,
           e_auth.lean_source AS author_entity_lean_source,
           na.outlet_entity_id, rp.subreddit_entity_id,
           au.handle AS x_handle, au.display_name AS x_display_name,
           au.description AS x_description, au.followers_count AS x_followers_count,
           (COALESCE(xp.retweet_count, 0) + COALESCE(xp.reply_count, 0)
            + COALESCE(xp.like_count, 0) + COALESCE(xp.quote_count, 0)) AS x_engagement
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    LEFT JOIN corpus.entities e ON e.entity_id = m.entity_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
    LEFT JOIN corpus.entities e_auth ON e_auth.entity_id = ap.entity_id
    LEFT JOIN corpus.news_articles na ON na.doc_id = d.doc_id
    LEFT JOIN corpus.reddit_posts rp ON rp.doc_id = d.doc_id
    LEFT JOIN corpus.authors au ON au.author_id = d.author_id
    LEFT JOIN corpus.x_posts xp ON xp.doc_id = d.doc_id
    WHERE r.is_current AND r.task = 'targets'
      AND m.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
"""

# Mentions withheld by the same author-level bot-flagged-share gate as
# _BOT_EXCLUSION_SQL, counted (not silently dropped) for TargetToneMeta.
_TARGET_BOT_EXCLUDED_SQL = f"""
    SELECT COUNT(*) AS n
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    WHERE r.is_current AND r.task = 'targets'
      AND m.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND EXISTS (
          SELECT 1 FROM analysis.author_bot_scores b
          WHERE b.author_id = d.author_id
          AND (b.bot_post_count + b.suspicious_post_count)::float
              / NULLIF(b.sample_count, 0) >= %(bot_floor)s
      )
"""

# doc_id -> [(narrative_id, name)] for the mention rows' docs -- powers
# received tone's by_narrative. Scoped to the doc_ids already selected by
# _TARGET_ROWS_SQL rather than the whole corpus.
_NARRATIVE_DOC_MAP_SQL = """
    SELECT nd.doc_id, n.narrative_id, n.name
    FROM analysis.narrative_docs nd
    JOIN analysis.narratives n ON n.narrative_id = nd.narrative_id
    WHERE nd.doc_id = ANY(%(doc_ids)s)
"""


def _fetch_doc_topics(conn, params: Dict[str, Any]) -> Dict[int, str]:
    """doc_id -> dominant resolved target_mentions topic (highest count,
    ties broken alphabetically); docs with no resolved topic are absent
    (``_topic_for_row`` reports those as unclassified, never a guess)."""
    topics: Dict[int, str] = {}
    for row in conn.execute(_DOC_TOPICS_SQL, params).fetchall():
        topics.setdefault(row["doc_id"], row["topic"])
    return topics


def _build_range_meta(
    window: Optional[str], start, end, analyzed_rows: List[Any],
) -> RangeMeta:
    """The honesty block: resolved bounds, the two admission bases among
    the panel's contributing ('text' task, done, in-range) runs, and their
    distinct model_ids -- so a wide range spanning a model/prompt change is
    labeled, not presented as one comparable series."""
    sampled, official_record = split_admission_counts(analyzed_rows)
    model_ids = sorted({row["model_id"] for row in analyzed_rows})
    return RangeMeta(
        window=window, start=start, end=end,
        sampled_doc_count=sampled, official_record_doc_count=official_record,
        model_ids=model_ids,
    )


# --------------------------------------------------------------------------- #
#  Row-level helpers                                                          #
# --------------------------------------------------------------------------- #

def _topic_for_row(doc_id: int, doc_topics: Dict[int, str]) -> str:
    """LLM-resolved topic when analysis.target_mentions has one; the
    literal "General" otherwise -- topic is never guessed from the title."""
    return doc_topics.get(doc_id, "General")


def _time_of_day(dt: datetime) -> str:
    hour = dt.hour
    if 5 <= hour <= 11:
        return "Morning"
    if 12 <= hour <= 16:
        return "Afternoon"
    if 17 <= hour <= 20:
        return "Evening"
    return "Night"


def _day_of_week(dt: datetime) -> str:
    return DAY_OF_WEEK_LABELS[dt.weekday()]


def _tier_for_row(source_type: str, author_tier: Optional[str]) -> str:
    if source_type == "news":
        return "news"
    if source_type == "x_post" and author_tier == "elected_official":
        return "officials"
    return "general_public"


def _net_score(counts: Dict[str, int], min_n: int = MIN_TARGET_SAMPLE_N) -> Optional[float]:
    volume = sum(counts.values())
    if volume < min_n:
        return None
    return round((counts.get("positive", 0) - counts.get("negative", 0)) / volume * 100, 1)


def _snippet(title: Optional[str]) -> Optional[str]:
    if not title:
        return None
    return title[:SNIPPET_MAX_CHARS]


# --------------------------------------------------------------------------- #
#  Aggregation                                                                #
# --------------------------------------------------------------------------- #

def _empty_counts() -> Dict[str, int]:
    return {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}


def _aggregate_rows(rows: List[Any], doc_topics: Dict[int, str]) -> Dict[str, Any]:
    """Walk each sentiment row once, fanning counts into every bucket the
    response needs, plus a capped, confidence-sorted sample list."""
    accum: Dict[str, Any] = {
        "strong_pos": 0, "mild_pos": 0, "strong_neg": 0, "mild_neg": 0, "neutral": 0,
        "conf_sum": 0.0, "count": 0,
        "by_platform": {}, "by_topic": {}, "by_tod": {}, "by_dow": {}, "by_tier": {}, "by_day": {},
        "samples": [],
    }
    for row in rows:
        label = row["label"]
        conf = row["confidence"]
        dt = row["published_at"]
        label_key = "neutral" if label == "mixed" else label
        _bump_distribution(accum, label, conf)

        topic = _topic_for_row(row["doc_id"], doc_topics)
        _increment(accum["by_platform"], row["source_type"], label_key)
        _increment(accum["by_topic"], topic, label_key)
        _increment(accum["by_tod"], _time_of_day(dt), label_key)
        _increment(accum["by_dow"], _day_of_week(dt), label_key)
        _increment(accum["by_tier"], _tier_for_row(row["source_type"], row["author_tier"]), label_key)
        _increment(accum["by_day"], dt.date().isoformat(), label_key)

        accum["count"] += 1
        accum["conf_sum"] += conf
        accum["samples"].append((conf, row))

    return accum


def _bump_distribution(accum: Dict[str, Any], label: str, conf: float) -> None:
    if label == "positive":
        key = "strong_pos" if conf >= STRONG_CONFIDENCE_THRESHOLD else "mild_pos"
        accum[key] += 1
    elif label == "negative":
        key = "strong_neg" if conf >= STRONG_CONFIDENCE_THRESHOLD else "mild_neg"
        accum[key] += 1
    else:
        accum["neutral"] += 1


def _increment(bucket: Dict[str, Dict[str, int]], key: str, label_key: str) -> None:
    bucket.setdefault(key, _empty_counts())
    bucket[key][label_key] += 1


def _build_entity_stances(target_rows: List[Any]) -> List[EntityStanceAggregate]:
    """One aggregate per entity target_mentions names (keyed by entity_id, or
    the unresolved-mentions catch-all). Sourced from target_mentions alone,
    party-neutral and topic-tagged. Also fans each mention into its entity's
    per-topic (GROUP BY entity_id, topic) and per-speaker-tier stance
    breakdowns."""
    cells: Dict[Any, Dict[str, Any]] = {}

    def cell_for(entity_id: Optional[int], row: Any) -> Dict[str, Any]:
        key = entity_id if entity_id is not None else UNRESOLVED_CATCH_ALL_KEY
        if key not in cells:
            cells[key] = {
                "entity_id": entity_id,
                "entity_key": row["entity_key"] if entity_id is not None else None,
                "display_name": row["display_name"] if entity_id is not None else "Unresolved mentions",
                "kind": row["kind"] if entity_id is not None else None,
                "lean": _lean_label(row["kind"], row["lean"]) if entity_id is not None else None,
                "target_stance": _empty_counts(),
                "by_topic": {},
                "by_tier": {},
                "samples": [],
            }
        return cells[key]

    for row in target_rows:
        cell = cell_for(row["entity_id"], row)
        cell["target_stance"][row["stance"]] += 1
        cell["samples"].append((row["confidence"], row))
        topic = row["topic"] or "General"
        _bump_stance_bucket(cell["by_topic"], topic, row["stance"])
        tier = _tier_for_row(row["source_type"], row["author_tier"])
        _bump_stance_bucket(cell["by_tier"], tier, row["stance"])

    return [_format_entity_stance(key, cell) for key, cell in cells.items()]


def _bump_stance_bucket(bucket: Dict[str, Dict[str, int]], key: str, stance: str) -> None:
    bucket.setdefault(key, _empty_counts())
    bucket[key][stance] += 1


def _lean_label(kind: str, lean_value: str) -> LeanLabel:
    label_kind = "fact" if kind in ("official", "collective") else "curated"
    return LeanLabel(kind=label_kind, value=lean_value)


def _format_entity_stance(key: Any, cell: Dict[str, Any]) -> EntityStanceAggregate:
    samples = sorted(cell["samples"], key=lambda pair: -pair[0])[:MAX_SAMPLES_PER_ENTITY]
    return EntityStanceAggregate(
        entity_id=cell["entity_id"],
        entity_key=cell["entity_key"],
        catch_all_key=None if cell["entity_id"] is not None else UNRESOLVED_CATCH_ALL_KEY,
        display_name=cell["display_name"],
        kind=cell["kind"],
        lean=cell["lean"],
        target_stance=_stance_counts(cell["target_stance"]),
        by_topic=_format_topic_stances(cell["by_topic"]),
        received_by_tier=_format_tier_stances(cell["by_tier"]),
        samples=[
            SampleDocModel(**build_sample_doc(
                {**row, "snippet": _snippet(row["title"])},
                admission_class=row["admission_class"],
            ))
            for _conf, row in samples
        ],
    )


def _stance_kwargs(counts: Dict[str, int]) -> Dict[str, Any]:
    volume = sum(counts.values())
    return {
        "positive": counts["positive"], "negative": counts["negative"],
        "neutral": counts["neutral"], "mixed": counts["mixed"],
        "volume": volume, "net_score": _net_score(counts), "low_sample": volume < MIN_TARGET_SAMPLE_N,
    }


def _stance_counts(counts: Dict[str, int]) -> StanceCounts:
    return StanceCounts(**_stance_kwargs(counts))


def _format_topic_stances(buckets: Dict[str, Dict[str, int]]) -> List[TopicStanceCounts]:
    """General last, then descending volume -- same ordering convention as
    the panel-level by_topic."""
    return sorted(
        (TopicStanceCounts(topic=k, **_stance_kwargs(v)) for k, v in buckets.items()),
        key=lambda t: (t.topic != "General", -t.volume),
    )


def _format_tier_stances(buckets: Dict[str, Dict[str, int]]) -> List[TierStanceCounts]:
    return [TierStanceCounts(tier=k, **_stance_kwargs(v)) for k, v in buckets.items()]


# --------------------------------------------------------------------------- #
#  Response assembly                                                         #
# --------------------------------------------------------------------------- #

def _build_response(
    range_meta: RangeMeta, accum: Dict[str, Any], total_analyzed: int, excluded_bot_docs: int,
    entity_stances: List[EntityStanceAggregate],
    tier_items: Dict[str, List[EntitySentimentItem]], target_tone: TargetToneMeta,
    tone_trend: List[ToneTrendPoint], distribution_samples: Dict[str, List[Any]],
    day_samples: Dict[str, List[Any]],
) -> SentimentPanelResponse:
    total_pos = accum["strong_pos"] + accum["mild_pos"]
    total_neg = accum["strong_neg"] + accum["mild_neg"]
    count = accum["count"]
    net_score = round((total_pos - total_neg) / count * 100, 1) if count > 0 else None
    mean_confidence = round(accum["conf_sum"] / count, 3) if count > 0 else None

    top_samples = sorted(accum["samples"], key=lambda pair: -pair[0])[:MAX_DISTRIBUTION_SAMPLES_PER_BUCKET]

    return SentimentPanelResponse(
        range=range_meta,
        overview=SentimentOverview(
            net_score=net_score, volume=count, total_analyzed=total_analyzed,
            trivial_content_docs=max(0, total_analyzed - count),
            excluded_bot_docs=excluded_bot_docs, mean_confidence=mean_confidence,
        ),
        distribution=SentimentDistribution(
            strong_positive=accum["strong_pos"], mild_positive=accum["mild_pos"],
            neutral=accum["neutral"], mild_negative=accum["mild_neg"],
            strong_negative=accum["strong_neg"],
        ),
        by_platform=[
            PlatformSentiment(platform=k, **_bucket_kwargs(v)) for k, v in accum["by_platform"].items()
        ],
        by_topic=sorted(
            (TopicSentiment(topic=k, **_bucket_kwargs(v)) for k, v in accum["by_topic"].items()),
            key=lambda t: (t.topic != "General", -t.volume),
        ),
        by_time_of_day=[
            TimeOfDaySentiment(bucket=k, **_bucket_kwargs(v)) for k, v in accum["by_tod"].items()
        ],
        by_day_of_week=sorted(
            (DayOfWeekSentiment(day=k, **_bucket_kwargs(v)) for k, v in accum["by_dow"].items()),
            key=lambda d: DAY_OF_WEEK_LABELS.index(d.day),
        ),
        by_tier=[TierSplit(tier=k, **_bucket_kwargs(v)) for k, v in accum["by_tier"].items()],
        daily=sorted(
            (DailySentiment(date=k, **_bucket_kwargs(v)) for k, v in accum["by_day"].items()),
            key=lambda d: d.date,
        ),
        entity_stances=entity_stances,
        samples=[
            SampleDocModel(**build_sample_doc(
                {**row, "snippet": _snippet(row["title"])},
                admission_class=row["admission_class"],
            ))
            for _conf, row in top_samples
        ],
        disclaimer=DISCLAIMER,
        by_news_outlet=tier_items["news"],
        by_official=tier_items["officials"],
        by_general_public=tier_items["public"],
        target_tone=target_tone,
        tone_trend=tone_trend,
        distribution_samples=distribution_samples,
        day_samples=day_samples,
    )


def _bucket_kwargs(counts: Dict[str, int]) -> Dict[str, Any]:
    volume = sum(counts.values())
    return {
        "positive": counts["positive"], "negative": counts["negative"],
        "neutral": counts["neutral"] + counts["mixed"], "volume": volume,
        "net_score": _net_score(counts),
    }


# --------------------------------------------------------------------------- #
#  Own-post tier bucketing: by_news_outlet / by_official / by_general_public  #
# --------------------------------------------------------------------------- #
#
# Bucket keys are ("entity", entity_id) for a registry-resolved entity,
# ("catchall", sentinel) for one of the three catch-all cards, or
# ("handle", x_handle) for a provisional unmatched-X-author bucket that
# _consolidate_sampled_x_authors() later either promotes to a named card
# or folds into ("catchall", CATCH_ALL_X_USERS).

def _route_own_post(row: Mapping[str, Any]) -> Optional[Tuple[str, Tuple[str, Any]]]:
    """Resolve one own-post row (a _SENTIMENT_ROWS_SQL or _TARGET_ROWS_SQL
    row -- both carry the same routing columns) to (tier, bucket_key).
    tier is 'news' | 'officials' | 'public'. None for an unknown
    source_type."""
    source_type = row["source_type"]
    if source_type == "news":
        if row["outlet_entity_id"] is not None:
            return "news", ("entity", row["outlet_entity_id"])
        return "news", ("catchall", CATCH_ALL_OUTLETS)
    if source_type == "reddit_post":
        if row["subreddit_entity_id"] is not None:
            return "public", ("entity", row["subreddit_entity_id"])
        return "public", ("catchall", CATCH_ALL_SUBREDDITS)
    if source_type == "x_post":
        author_entity_id = row["author_entity_id"]
        if author_entity_id is not None:
            if row["author_entity_kind"] == "official" and row["author_entity_editorial"]:
                return "officials", ("entity", author_entity_id)
            return "public", ("entity", author_entity_id)
        handle = row.get("x_handle")
        if handle:
            return "public", ("handle", handle.lower().lstrip("@"))
        return "public", ("catchall", CATCH_ALL_X_USERS)
    return None


def _empty_entity_bucket(tier: str) -> Dict[str, Any]:
    return {
        "tier": tier, "followers": 0,
        "positive": 0, "negative": 0, "neutral": 0,
        "engagement_total": 0,
        "by_topic": {}, "by_day": {},
        "samples": [],
    }


def _build_tier_buckets(rows: List[Any], doc_topics: Dict[int, str]) -> Dict[Tuple[str, Any], Dict[str, Any]]:
    """Walk each own-post row once into its (tier, bucket_key) accumulator.
    Handle-keyed buckets are provisional -- _consolidate_sampled_x_authors
    resolves them afterward."""
    buckets: Dict[Tuple[str, Any], Dict[str, Any]] = {}
    for row in rows:
        routed = _route_own_post(row)
        if routed is None:
            continue
        tier, key = routed
        bucket = buckets.setdefault(key, _empty_entity_bucket(tier))

        label_key = "neutral" if row["label"] == "mixed" else row["label"]
        bucket[label_key] += 1
        topic = _topic_for_row(row["doc_id"], doc_topics)
        _increment(bucket["by_topic"], topic, label_key)
        day = row["published_at"].date().isoformat()
        _increment(bucket["by_day"], day, label_key)
        if key[0] == "handle":
            bucket["followers"] = max(bucket["followers"], row.get("x_followers_count") or 0)
        if row["source_type"] == "x_post":
            bucket["engagement_total"] += row.get("x_engagement") or 0
        bucket["samples"].append((row["confidence"], row))
    return buckets


def _bucket_volume(bucket: Dict[str, Any]) -> int:
    return bucket["positive"] + bucket["negative"] + bucket["neutral"]


def _merge_bucket_into(dst: Dict[str, Any], src: Dict[str, Any]) -> None:
    for label_key in ("positive", "negative", "neutral"):
        dst[label_key] += src[label_key]
    dst["engagement_total"] += src["engagement_total"]
    for topic, counts in src["by_topic"].items():
        target = dst["by_topic"].setdefault(topic, _empty_counts())
        for label_key, n in counts.items():
            target[label_key] += n
    for day, counts in src["by_day"].items():
        target = dst["by_day"].setdefault(day, _empty_counts())
        for label_key, n in counts.items():
            target[label_key] += n
    dst["samples"].extend(src["samples"])


def _consolidate_sampled_x_authors(buckets: Dict[Tuple[str, Any], Dict[str, Any]]) -> None:
    """Fold sub-floor sampled-author buckets back into ('catchall',
    CATCH_ALL_X_USERS): only handles clearing BOTH MIN_SAMPLED_AUTHOR_POSTS
    and MIN_SAMPLED_AUTHOR_FOLLOWERS keep a named card, capped at
    MAX_SAMPLED_AUTHOR_CARDS by volume. Everyone else's counts, topics,
    days, and samples merge into the catch-all -- pooled, never dropped."""
    handle_keys = [key for key in buckets if key[0] == "handle"]
    if not handle_keys:
        return
    qualifying = sorted(
        (
            key for key in handle_keys
            if _bucket_volume(buckets[key]) >= MIN_SAMPLED_AUTHOR_POSTS
            and buckets[key]["followers"] >= MIN_SAMPLED_AUTHOR_FOLLOWERS
        ),
        key=lambda key: -_bucket_volume(buckets[key]),
    )
    keep = set(qualifying[:MAX_SAMPLED_AUTHOR_CARDS])
    demoted = [key for key in handle_keys if key not in keep]
    if not demoted:
        return
    catch_key = ("catchall", CATCH_ALL_X_USERS)
    catch = buckets.setdefault(catch_key, _empty_entity_bucket("public"))
    for key in demoted:
        _merge_bucket_into(catch, buckets.pop(key))


def _top_pairs(pairs: List[Tuple[float, Any]], cap: int) -> List[Tuple[float, Any]]:
    return sorted(pairs, key=lambda pair: -pair[0])[:cap]


def _entity_net_score(counts: Dict[str, int]) -> float:
    """Expressed net for an EntitySentimentItem's own posts -- unlike the
    received/topic/daily nets, this one is never suppressed (matches the
    pre-redesign contract's non-optional EntitySentimentItem.netScore)."""
    volume = sum(counts.values())
    if volume == 0:
        return 0.0
    return round((counts["positive"] - counts["negative"]) / volume * 100, 1)


def _build_classification_samples(pairs: List[Tuple[float, Any]], rich_samples: Dict[int, dict]) -> List[Any]:
    """Capped (conf, row) pairs -> rich ClassificationSampleModels, deduped
    by doc_id, confidence-desc order preserved."""
    seen: set = set()
    out = []
    for _conf, row in pairs:
        doc_id = row["doc_id"]
        if doc_id in seen:
            continue
        seen.add(doc_id)
        fields = rich_samples.get(doc_id)
        if fields is None:
            continue
        out.append(build_classification_sample(doc_id, fields))
    return out


def _format_entity_topic_cells(by_topic: Dict[str, Dict[str, int]]) -> List[EntityTopicCell]:
    cells = [
        EntityTopicCell(
            topic=topic, net=_net_score(counts), volume=sum(counts.values()),
            low_sample=sum(counts.values()) < MIN_TARGET_SAMPLE_N,
        )
        for topic, counts in by_topic.items()
    ]
    cells.sort(key=lambda c: -c.volume)
    return cells


def _format_daily_tone(by_day: Dict[str, Dict[str, int]]) -> List[EntityDailyTonePoint]:
    days = sorted(by_day.items())[-DAILY_TONE_MAX_DAYS:]
    return [
        EntityDailyTonePoint(
            date=day, net=_net_score(counts), volume=sum(counts.values()),
            low_sample=sum(counts.values()) < MIN_TARGET_SAMPLE_N,
        )
        for day, counts in days
    ]


def _format_entity_item(
    key: Tuple[str, Any], bucket: Dict[str, Any],
    profiles: Dict[int, Any], rich_samples: Dict[int, dict],
) -> EntitySentimentItem:
    kind_tag, ident = key
    if kind_tag == "entity":
        profile = profiles.get(ident)
        if profile is None:
            raise ValueError(
                f"entity_id={ident} routed into a sentiment bucket but missing from corpus.entities"
            )
        item_key = profile.key
    elif kind_tag == "catchall":
        profile = catch_all_profile(ident)
        item_key = ident
    else:  # sampled X handle that survived consolidation
        sample_row = bucket["samples"][0][1] if bucket["samples"] else {}
        profile = sampled_account_profile(
            ident, sample_row.get("x_display_name"), sample_row.get("x_description"),
        )
        item_key = ident

    counts = {"positive": bucket["positive"], "negative": bucket["negative"], "neutral": bucket["neutral"]}
    samples = _top_pairs(bucket["samples"], MAX_SAMPLES_PER_ENTITY)
    return EntitySentimentItem(
        key=item_key, kind=profile.kind, entity_profile=profile,
        positive=counts["positive"], negative=counts["negative"], neutral=counts["neutral"],
        volume=sum(counts.values()), net_score=_entity_net_score(counts),
        classification_samples=_build_classification_samples(samples, rich_samples),
        by_topic=_format_entity_topic_cells(bucket["by_topic"]),
        engagement_total=bucket["engagement_total"],
        daily_tone=_format_daily_tone(bucket["by_day"]),
    )


def _build_tier_items(
    buckets: Dict[Tuple[str, Any], Dict[str, Any]], profiles: Dict[int, Any], rich_samples: Dict[int, dict],
) -> Tuple[Dict[str, List[EntitySentimentItem]], Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem]]:
    grouped: Dict[str, List[EntitySentimentItem]] = {"news": [], "officials": [], "public": []}
    lookup: Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem] = {}
    for key, bucket in buckets.items():
        item = _format_entity_item(key, bucket, profiles, rich_samples)
        tier = bucket["tier"]
        grouped[tier].append(item)
        lookup[(tier, key)] = item
    for tier_items in grouped.values():
        tier_items.sort(key=lambda item: (item.kind == "catch_all", -item.volume))
    return grouped, lookup


def _attach_official_stubs(
    grouped: Dict[str, List[EntitySentimentItem]],
    lookup: Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem],
    editorial_official_ids: set, profiles: Dict[int, Any], received: Dict[int, Dict[str, Any]],
) -> None:
    """An editorial official who never posted in the window but IS
    discussed still needs a card -- received tone is exactly the metric
    that exists without their participation. Skips officials with no
    received evidence either (nothing to show)."""
    existing_ids = {k[1][1] for k in lookup if k[0] == "officials" and k[1][0] == "entity"}
    for entity_id in editorial_official_ids - existing_ids:
        if entity_id not in received:
            continue
        profile = profiles[entity_id]
        item = EntitySentimentItem(
            key=profile.key, kind=profile.kind, entity_profile=profile,
            positive=0, negative=0, neutral=0, volume=0, net_score=0.0,
        )
        grouped["officials"].append(item)
        lookup[("officials", ("entity", entity_id))] = item


# --------------------------------------------------------------------------- #
#  Received tone, expressed alignment, party-collective rollups             #
# --------------------------------------------------------------------------- #

def _normalize_party(lean_source: Optional[str]) -> Optional[str]:
    """Collapse a corpus.entities.lean_source string to the R/D alignment
    axis. 'independent-dem' caucuses with Democrats; anything else falls
    outside the two-party alignment frame."""
    if not lean_source:
        return None
    value = lean_source.strip().lower()
    if value == "r":
        return "R"
    if value in ("d", "independent-dem"):
        return "D"
    return None


def _speaker_tier_4way(row: Mapping[str, Any]) -> str:
    """WHO authored the mention: news / officials / affiliated / public --
    a finer vocabulary than the panel-level 3-way _tier_for_row, needed so
    received tone can separate 'officials talking about X' from 'curated
    affiliated accounts talking about X'."""
    if row["source_type"] == "news":
        return "news"
    tier = row["author_tier"]
    if tier == "elected_official":
        return "officials"
    if tier == "affiliated":
        return "affiliated"
    return "public"


def _engagement_weight(engagement: Optional[int]) -> float:
    """Log-damped reach-proxy weight: 1 for zero/unknown engagement,
    growing sub-linearly so one viral post can't own an official's
    weighted net alone."""
    return 1.0 + math.log1p(max(0, engagement or 0))


def _received_accum_default() -> Dict[str, Any]:
    return {
        "counts": _empty_counts(),
        "by_topic": {}, "by_speaker_tier": {}, "by_narrative": {},
        "w_pos": 0.0, "w_neg": 0.0, "w_total": 0.0,
        "samples": [],
    }


def _accumulate_received(
    accum: Dict[str, Any], row: Mapping[str, Any], narrative_map: Dict[int, List[Tuple[int, str]]],
) -> None:
    stance = row["stance"]
    topic = row["topic"] or "General"
    accum["counts"][stance] += 1
    _bump_stance_bucket(accum["by_topic"], topic, stance)
    _bump_stance_bucket(accum["by_speaker_tier"], _speaker_tier_4way(row), stance)
    weight = _engagement_weight(row.get("x_engagement"))
    accum["w_total"] += weight
    if stance == "positive":
        accum["w_pos"] += weight
    elif stance == "negative":
        accum["w_neg"] += weight
    for narrative_id, name in narrative_map.get(row["doc_id"], []):
        cell = accum["by_narrative"].setdefault(narrative_id, {"name": name, "counts": _empty_counts()})
        cell["counts"][stance] += 1
    accum["samples"].append((row["confidence"], row))


def _accumulate_target_tone(
    target_rows: List[Any], narrative_map: Dict[int, List[Tuple[int, str]]],
) -> Tuple[Dict[int, Dict[str, Any]], Dict[str, Dict[str, Any]], Dict[int, Dict[str, Dict[str, int]]],
           Dict[str, Dict[str, int]], int, int]:
    """One pass over target_rows building: received tone per editorial
    official entity_id, the two party-collective received accumulators
    (raw_target alias match), each official-speaker's same/cross-party
    expressed_alignment cells, the global alignment baseline, and the
    resolved/unresolved mention counts (resolved = entity_id IS NOT NULL,
    the strict registry-resolution reading of the column -- collective
    alias matches on an unresolved raw_target still count as unresolved
    here, even though they get their own received-tone rollup below)."""
    received: Dict[int, Dict[str, Any]] = {}
    collectives = {"gop_collective": _received_accum_default(), "dem_collective": _received_accum_default()}
    alignment: Dict[int, Dict[str, Dict[str, int]]] = {}
    baseline = {"same": _empty_counts(), "cross": _empty_counts()}
    resolved_mentions = 0
    unresolved_mentions = 0

    for row in target_rows:
        stance = row["stance"]
        if stance not in _STANCE_KEYS:
            continue
        entity_id = row["entity_id"]
        if entity_id is not None:
            resolved_mentions += 1
        else:
            unresolved_mentions += 1

        if entity_id is not None and row["kind"] == "official" and row["target_editorial"]:
            accum = received.setdefault(entity_id, _received_accum_default())
            _accumulate_received(accum, row, narrative_map)
        elif entity_id is None:
            raw_lower = (row["raw_target"] or "").strip().lower()
            if raw_lower in GOP_TARGET_ALIASES:
                _accumulate_received(collectives["gop_collective"], row, narrative_map)
            elif raw_lower in DEM_TARGET_ALIASES:
                _accumulate_received(collectives["dem_collective"], row, narrative_map)

        author_entity_id = row["author_entity_id"]
        if (
            author_entity_id is not None and row["author_entity_kind"] == "official"
            and row["author_entity_editorial"] and entity_id is not None and entity_id != author_entity_id
        ):
            speaker_party = _normalize_party(row["author_entity_lean_source"])
            target_party = _normalize_party(row["target_lean_source"])
            if speaker_party and target_party:
                cell = "same" if speaker_party == target_party else "cross"
                cells = alignment.setdefault(author_entity_id, {"same": _empty_counts(), "cross": _empty_counts()})
                cells[cell][stance] += 1
                baseline[cell][stance] += 1

    return received, collectives, alignment, baseline, resolved_mentions, unresolved_mentions


def _format_received(accum: Dict[str, Any], rich_samples: Dict[int, dict]) -> ReceivedTone:
    counts = accum["counts"]
    volume = sum(counts.values())
    low_sample = volume < MIN_TARGET_SAMPLE_N

    by_topic = [
        ReceivedTopicCell(
            topic=topic, net=_net_score(c), volume=sum(c.values()), low_sample=sum(c.values()) < MIN_TARGET_SAMPLE_N,
        )
        for topic, c in accum["by_topic"].items()
    ]
    by_topic.sort(key=lambda cell: -cell.volume)

    by_speaker_tier = [
        ReceivedSpeakerTierCell(
            tier=tier, net=_net_score(c), volume=sum(c.values()), low_sample=sum(c.values()) < MIN_TARGET_SAMPLE_N,
        )
        for tier, c in accum["by_speaker_tier"].items()
    ]
    by_speaker_tier.sort(key=lambda cell: -cell.volume)

    by_narrative = []
    for narrative_id, cell in accum["by_narrative"].items():
        n_volume = sum(cell["counts"].values())
        by_narrative.append(ReceivedNarrativeCell(
            narrative_id=narrative_id, name=cell["name"], net=_net_score(cell["counts"]),
            volume=n_volume, low_sample=n_volume < MIN_TARGET_SAMPLE_N,
        ))
    by_narrative.sort(key=lambda cell: -cell.volume)
    by_narrative = by_narrative[:MAX_NARRATIVES_PER_TARGET]

    weighted_net = None
    if not low_sample and accum["w_total"] > 0:
        weighted_net = round((accum["w_pos"] - accum["w_neg"]) / accum["w_total"] * 100, 1)

    samples = _top_pairs(accum["samples"], MAX_SAMPLES_PER_TARGET)
    return ReceivedTone(
        net=_net_score(counts), volume=volume, low_sample=low_sample,
        engagement_weighted_net=weighted_net,
        by_topic=by_topic, by_speaker_tier=by_speaker_tier, by_narrative=by_narrative,
        samples=_build_classification_samples(samples, rich_samples),
    )


def _format_alignment(cells: Dict[str, Dict[str, int]]) -> ExpressedAlignment:
    return ExpressedAlignment(
        same_party_net=_net_score(cells["same"]), same_party_volume=sum(cells["same"].values()),
        cross_party_net=_net_score(cells["cross"]), cross_party_volume=sum(cells["cross"].values()),
    )


def _attach_received(
    lookup: Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem],
    received: Dict[int, Dict[str, Any]], rich_samples: Dict[int, dict],
) -> None:
    for entity_id, accum in received.items():
        item = lookup.get(("officials", ("entity", entity_id)))
        if item is not None:
            item.received = _format_received(accum, rich_samples)


def _attach_expressed_alignment(
    lookup: Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem],
    alignment: Dict[int, Dict[str, Dict[str, int]]],
) -> None:
    for entity_id, cells in alignment.items():
        item = lookup.get(("officials", ("entity", entity_id)))
        if item is not None:
            item.expressed_alignment = _format_alignment(cells)


# --------------------------------------------------------------------------- #
#  Outbound "who this bucket talks about" rollup (news + public tiers only)  #
# --------------------------------------------------------------------------- #

def _route_outbound_bucket(
    row: Mapping[str, Any], buckets: Dict[Tuple[str, Any], Dict[str, Any]],
) -> Optional[Tuple[str, Tuple[str, Any]]]:
    """The mentioning doc's own (tier, bucket_key) -- officials excluded
    (outbound is a news/public surface; an official's own outbound tone is
    expressed_alignment instead). Falls back to the tier's catch-all when
    the doc's own bucket was consolidated away (e.g. a sub-floor sampled
    author) or never had its own sentiment-bucketed post."""
    routed = _route_own_post(row)
    if routed is None:
        return None
    tier, key = routed
    if tier == "officials":
        return None
    if key not in buckets:
        if key[0] == "handle":
            fallback = CATCH_ALL_X_USERS
        elif tier == "news":
            fallback = CATCH_ALL_OUTLETS
        else:
            fallback = CATCH_ALL_SUBREDDITS
        key = ("catchall", fallback)
        if key not in buckets:
            return None
    return tier, key


def _outbound_group(row: Mapping[str, Any]) -> Tuple[Optional[Tuple[str, Any]], Optional[Dict[str, Any]]]:
    """The target-side grouping key for one outbound mention row: a
    resolved entity, a party-collective alias match, or a recurring raw
    free-text target. None, None for a one-off raw target with nothing to
    group under yet (folded into 'Other targets' by volume in
    _format_outbound instead)."""
    entity_id = row["entity_id"]
    if entity_id is not None:
        return ("entity", entity_id), {
            "label": row["display_name"], "kind": row["kind"],
            "entity_key": row["entity_key"], "counts": _empty_counts(),
        }
    raw = (row["raw_target"] or "").strip()
    raw_lower = raw.lower()
    if raw_lower in GOP_TARGET_ALIASES:
        return ("collective", "gop"), {
            "label": "Republicans (party)", "kind": "collective", "entity_key": None, "counts": _empty_counts(),
        }
    if raw_lower in DEM_TARGET_ALIASES:
        return ("collective", "dem"), {
            "label": "Democrats (party)", "kind": "collective", "entity_key": None, "counts": _empty_counts(),
        }
    if not raw:
        return None, None
    return ("raw", raw_lower), {"label": raw, "kind": "raw", "entity_key": None, "counts": _empty_counts()}


def _format_outbound(groups: Dict[Any, Dict[str, Any]]) -> OutboundTargets:
    named = []
    other = _empty_counts()
    for cell in groups.values():
        volume = sum(cell["counts"].values())
        if cell["kind"] == "raw" and volume < MIN_OUTBOUND_RAW_RECURRENCE:
            for stance, n in cell["counts"].items():
                other[stance] += n
            continue
        named.append(cell)
    named.sort(key=lambda cell: -sum(cell["counts"].values()))
    overflow = named[MAX_OUTBOUND_TARGETS:]
    named = named[:MAX_OUTBOUND_TARGETS]
    for cell in overflow:
        for stance, n in cell["counts"].items():
            other[stance] += n

    targets = [
        OutboundTargetCell(
            label=cell["label"], entity_key=cell["entity_key"], kind=cell["kind"],
            net=_net_score(cell["counts"]), volume=sum(cell["counts"].values()),
            low_sample=sum(cell["counts"].values()) < MIN_TARGET_SAMPLE_N,
        )
        for cell in named
    ]
    other_volume = sum(other.values())
    if other_volume:
        targets.append(OutboundTargetCell(
            label=OUTBOUND_OTHER_LABEL, entity_key=None, kind="other",
            net=_net_score(other), volume=other_volume, low_sample=other_volume < MIN_TARGET_SAMPLE_N,
        ))
    return OutboundTargets(
        min_sample_n=MIN_TARGET_SAMPLE_N, volume=sum(t.volume for t in targets), targets=targets,
    )


def _attach_outbound(
    lookup: Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem],
    buckets: Dict[Tuple[str, Any], Dict[str, Any]], target_rows: List[Any],
) -> None:
    accum: Dict[Tuple[str, Tuple[str, Any]], Dict[Any, Dict[str, Any]]] = {}
    for row in target_rows:
        if row["stance"] not in _STANCE_KEYS:
            continue
        routed = _route_outbound_bucket(row, buckets)
        if routed is None:
            continue
        group_key, cell_seed = _outbound_group(row)
        if group_key is None:
            continue
        cell = accum.setdefault(routed, {}).setdefault(group_key, cell_seed)
        cell["counts"][row["stance"]] += 1

    for routed, groups in accum.items():
        item = lookup.get(routed)
        if item is not None:
            item.outbound = _format_outbound(groups)


# --------------------------------------------------------------------------- #
#  toneTrend (day-by-tier expressed net), distribution/day rich samples      #
# --------------------------------------------------------------------------- #

def _tone_trend_cell(counts: Optional[Dict[str, int]]) -> ToneTrendCell:
    counts = counts or _empty_counts()
    return ToneTrendCell(net=_net_score(counts), volume=sum(counts.values()))


def _build_tone_trend(rows: List[Any]) -> List[ToneTrendPoint]:
    by_day_tier: Dict[str, Dict[str, Dict[str, int]]] = {}
    for row in rows:
        day = row["published_at"].date().isoformat()
        tier = _tier_for_row(row["source_type"], row["author_tier"])
        label_key = "neutral" if row["label"] == "mixed" else row["label"]
        _increment(by_day_tier.setdefault(day, {}), tier, label_key)

    return [
        ToneTrendPoint(
            date=day,
            news=_tone_trend_cell(by_day_tier[day].get("news")),
            officials=_tone_trend_cell(by_day_tier[day].get("officials")),
            public=_tone_trend_cell(by_day_tier[day].get("general_public")),
        )
        for day in sorted(by_day_tier)
    ]


def _distribution_segment(label: str, confidence: float) -> str:
    if label == "positive":
        return "strongPositive" if confidence >= STRONG_CONFIDENCE_THRESHOLD else "mildPositive"
    if label == "negative":
        return "strongNegative" if confidence >= STRONG_CONFIDENCE_THRESHOLD else "mildNegative"
    return "neutral"


def _insert_capped_pair(pairs: List[Tuple[float, Any]], pair: Tuple[float, Any], cap: int) -> None:
    pairs.append(pair)
    pairs.sort(key=lambda p: -p[0])
    del pairs[cap:]


def _collect_distribution_and_day_samples(
    rows: List[Any],
) -> Tuple[Dict[str, List[Tuple[float, Any]]], Dict[str, List[Tuple[float, Any]]]]:
    dist_pairs: Dict[str, List[Tuple[float, Any]]] = {segment: [] for segment in DISTRIBUTION_SEGMENTS}
    day_pairs: Dict[str, List[Tuple[float, Any]]] = {}
    for row in rows:
        segment = _distribution_segment(row["label"], row["confidence"])
        _insert_capped_pair(dist_pairs[segment], (row["confidence"], row), MAX_DISTRIBUTION_SAMPLES_PER_BUCKET)
        day = row["published_at"].date().isoformat()
        day_pairs.setdefault(day, [])
        _insert_capped_pair(day_pairs[day], (row["confidence"], row), DAY_SAMPLES_MAX_PER_DAY)
    return dist_pairs, day_pairs


def _collect_all_sample_doc_ids(
    buckets: Dict[Tuple[str, Any], Dict[str, Any]],
    received: Dict[int, Dict[str, Any]], collectives: Dict[str, Dict[str, Any]],
    dist_pairs: Dict[str, List[Tuple[float, Any]]], day_pairs: Dict[str, List[Tuple[float, Any]]],
) -> set:
    doc_ids: set = set()
    for bucket in buckets.values():
        for _conf, row in _top_pairs(bucket["samples"], MAX_SAMPLES_PER_ENTITY):
            doc_ids.add(row["doc_id"])
    for accum in list(received.values()) + list(collectives.values()):
        for _conf, row in _top_pairs(accum["samples"], MAX_SAMPLES_PER_TARGET):
            doc_ids.add(row["doc_id"])
    for pairs in dist_pairs.values():
        doc_ids.update(row["doc_id"] for _conf, row in pairs)
    for pairs in day_pairs.values():
        doc_ids.update(row["doc_id"] for _conf, row in pairs)
    return doc_ids


def _fetch_narrative_map(conn, target_rows: List[Any]) -> Dict[int, List[Tuple[int, str]]]:
    doc_ids = list({row["doc_id"] for row in target_rows})
    if not doc_ids:
        return {}
    out: Dict[int, List[Tuple[int, str]]] = {}
    for row in conn.execute(_NARRATIVE_DOC_MAP_SQL, {"doc_ids": doc_ids}).fetchall():
        out.setdefault(row["doc_id"], []).append((row["narrative_id"], row["name"]))
    return out
