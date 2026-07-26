"""
GET /api/v1/sentiment aggregation: net tone, intensity distribution,
platform/topic/time-of-day/day-of-week splits, the news/officials/
general-public tier split, and per-entity stance aggregates -- computed
live against corpus.documents + analysis.runs/sentiment_results/
target_mentions. See docs/audit-trail/api/2026-07-24-phase9-sentiment-entities.md.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Optional

from analysis.src.api.models.common import LeanLabel, RangeMeta, SampleDocModel
from analysis.src.api.models.sentiment import (
    DayOfWeekSentiment,
    EntityStanceAggregate,
    PlatformSentiment,
    SentimentDistribution,
    SentimentOverview,
    SentimentPanelResponse,
    StanceCounts,
    TierSplit,
    TimeOfDaySentiment,
    TopicSentiment,
)
from analysis.src.api.queries.base import (
    build_sample_doc,
    resolve_time_range,
    split_admission_counts,
)
from analysis.src.api.queries.constants import (
    BOT_FLAGGED_SHARE_EXCLUSION,
    MAX_DISTRIBUTION_SAMPLES_PER_BUCKET,
    MAX_SAMPLES_PER_ENTITY,
    MIN_TARGET_SAMPLE_N,
    SNIPPET_MAX_CHARS,
    STRONG_CONFIDENCE_THRESHOLD,
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

    range_meta = _build_range_meta(window, start, end, analyzed_rows)
    accum = _aggregate_rows(rows, doc_topics)
    entity_stances = _build_entity_stances(target_rows)
    return _build_response(range_meta, accum, len(analyzed_rows), excluded_bot_docs, entity_stances)


# --------------------------------------------------------------------------- #
#  SQL                                                                        #
# --------------------------------------------------------------------------- #

_SENTIMENT_ROWS_SQL = f"""
    SELECT d.doc_id, d.source_type, d.published_at, d.source_url, d.title,
           d.admission_class, r.confidence, sr.label, ap.tier AS author_tier
    FROM analysis.runs r
    JOIN analysis.sentiment_results sr ON sr.run_id = r.run_id
    JOIN corpus.documents d ON d.doc_id = r.doc_id
    LEFT JOIN corpus.author_profiles ap ON ap.author_id = d.author_id
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

_TARGET_ROWS_SQL = f"""
    SELECT m.entity_id, m.stance, d.doc_id, d.source_url, d.published_at, d.title,
           d.admission_class, m.confidence,
           e.display_name, e.kind, e.lean
    FROM analysis.target_mentions m
    JOIN analysis.runs r ON r.run_id = m.run_id
    JOIN corpus.documents d ON d.doc_id = m.doc_id
    LEFT JOIN corpus.entities e ON e.entity_id = m.entity_id
    WHERE r.is_current AND r.task = 'targets'
      AND m.confidence >= %(min_conf)s AND {_RANGE_PREDICATE}
      AND {_BOT_EXCLUSION_SQL}
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
        "by_platform": {}, "by_topic": {}, "by_tod": {}, "by_dow": {}, "by_tier": {},
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
    party-neutral and topic-tagged."""
    cells: Dict[Any, Dict[str, Any]] = {}

    def cell_for(entity_id: Optional[int], row: Any) -> Dict[str, Any]:
        key = entity_id if entity_id is not None else UNRESOLVED_CATCH_ALL_KEY
        if key not in cells:
            cells[key] = {
                "entity_id": entity_id,
                "display_name": row["display_name"] if entity_id is not None else "Unresolved mentions",
                "kind": row["kind"] if entity_id is not None else None,
                "lean": _lean_label(row["kind"], row["lean"]) if entity_id is not None else None,
                "target_stance": _empty_counts(),
                "samples": [],
            }
        return cells[key]

    for row in target_rows:
        cell = cell_for(row["entity_id"], row)
        cell["target_stance"][row["stance"]] += 1
        cell["samples"].append((row["confidence"], row))

    return [_format_entity_stance(key, cell) for key, cell in cells.items()]


def _lean_label(kind: str, lean_value: str) -> LeanLabel:
    label_kind = "fact" if kind in ("official", "collective") else "curated"
    return LeanLabel(kind=label_kind, value=lean_value)


def _format_entity_stance(key: Any, cell: Dict[str, Any]) -> EntityStanceAggregate:
    samples = sorted(cell["samples"], key=lambda pair: -pair[0])[:MAX_SAMPLES_PER_ENTITY]
    return EntityStanceAggregate(
        entity_id=cell["entity_id"],
        catch_all_key=None if cell["entity_id"] is not None else UNRESOLVED_CATCH_ALL_KEY,
        display_name=cell["display_name"],
        kind=cell["kind"],
        lean=cell["lean"],
        target_stance=_stance_counts(cell["target_stance"]),
        samples=[
            SampleDocModel(**build_sample_doc(
                {**row, "snippet": _snippet(row["title"])},
                admission_class=row["admission_class"],
            ))
            for _conf, row in samples
        ],
    )


def _stance_counts(counts: Dict[str, int]) -> StanceCounts:
    volume = sum(counts.values())
    return StanceCounts(
        positive=counts["positive"], negative=counts["negative"],
        neutral=counts["neutral"], mixed=counts["mixed"],
        volume=volume, net_score=_net_score(counts), low_sample=volume < MIN_TARGET_SAMPLE_N,
    )


# --------------------------------------------------------------------------- #
#  Response assembly                                                         #
# --------------------------------------------------------------------------- #

def _build_response(
    range_meta: RangeMeta, accum: Dict[str, Any], total_analyzed: int, excluded_bot_docs: int,
    entity_stances: List[EntityStanceAggregate],
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
        entity_stances=entity_stances,
        samples=[
            SampleDocModel(**build_sample_doc(
                {**row, "snippet": _snippet(row["title"])},
                admission_class=row["admission_class"],
            ))
            for _conf, row in top_samples
        ],
        disclaimer=DISCLAIMER,
    )


def _bucket_kwargs(counts: Dict[str, int]) -> Dict[str, Any]:
    volume = sum(counts.values())
    return {
        "positive": counts["positive"], "negative": counts["negative"],
        "neutral": counts["neutral"] + counts["mixed"], "volume": volume,
        "net_score": _net_score(counts),
    }
