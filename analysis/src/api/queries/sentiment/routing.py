"""
Row-routing decisions (own-post tier, outbound bucket, received-tone
source, speaker tier, party alignment) plus the small counting/formatting
primitives shared across the sentiment package. Base tier: no other
sentiment submodule is imported here.
"""

from __future__ import annotations

from datetime import datetime
from typing import Any, Dict, List, Mapping, Optional, Tuple

from analysis.src.api.models.common import LeanLabel
from analysis.src.api.queries.base import build_classification_sample
from analysis.src.api.queries.constants import MIN_TARGET_SAMPLE_N, OFFICIAL_AUTHOR_TIERS, SNIPPET_MAX_CHARS
from analysis.src.api.queries.profiles import (
    CATCH_ALL_OUTLETS,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    is_official_kind,
)

DAY_OF_WEEK_LABELS = ("Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun")

# Stance vocabulary on analysis.target_mentions.stance (sentiment_label enum).
_STANCE_KEYS = ("positive", "negative", "neutral", "mixed")


# --------------------------------------------------------------------------- #
#  Shared counting/formatting primitives                                     #
# --------------------------------------------------------------------------- #

def _empty_counts() -> Dict[str, int]:
    return {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0}


def _increment(bucket: Dict[str, Dict[str, int]], key: str, label_key: str) -> None:
    bucket.setdefault(key, _empty_counts())
    bucket[key][label_key] += 1


def _bump_stance_bucket(bucket: Dict[str, Dict[str, int]], key: str, stance: str) -> None:
    bucket.setdefault(key, _empty_counts())
    bucket[key][stance] += 1


def _net_score(counts: Dict[str, int], min_n: int = MIN_TARGET_SAMPLE_N) -> Optional[float]:
    volume = sum(counts.values())
    if volume < min_n:
        return None
    return round((counts.get("positive", 0) - counts.get("negative", 0)) / volume * 100, 1)


def _top_pairs(pairs: List[Tuple[float, Any]], cap: int) -> List[Tuple[float, Any]]:
    return sorted(pairs, key=lambda pair: -pair[0])[:cap]


def _snippet(title: Optional[str]) -> Optional[str]:
    if not title:
        return None
    return title[:SNIPPET_MAX_CHARS]


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
    if source_type == "x_post" and author_tier in OFFICIAL_AUTHOR_TIERS:
        return "officials"
    return "general_public"


def _lean_label(kind: str, lean_value: str) -> LeanLabel:
    label_kind = "fact" if kind in ("official", "collective") else "curated"
    return LeanLabel(kind=label_kind, value=lean_value)


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


# --------------------------------------------------------------------------- #
#  Own-post tier bucketing route                                             #
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
            if is_official_kind(row["author_entity_kind"]):
                return "officials", ("entity", author_entity_id)
            return "public", ("entity", author_entity_id)
        handle = row.get("x_handle")
        if handle:
            return "public", ("handle", handle.lower().lstrip("@"))
        return "public", ("catchall", CATCH_ALL_X_USERS)
    return None


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


# --------------------------------------------------------------------------- #
#  Received-tone source route, speaker tier, party alignment                 #
# --------------------------------------------------------------------------- #

def _route_received_source(row: Mapping[str, Any]) -> Tuple[str, Any, Optional[str]]:
    """Resolve one received-tone mention row to (source_class, ident,
    lean) -- WHO the tone comes from, the inbound mirror of
    _route_own_post/_route_outbound_bucket. ident is an entity_id for a
    registry source, a lowercased handle for an unresolved sampled X
    author, or None for the single 'other' bucket every unroutable row
    (unmatched outlet/subreddit, unknown source_type, no author identity
    at all) folds into -- so received_from_groups' shares always sum to
    1.0."""
    source_type = row["source_type"]
    if source_type == "news":
        outlet_entity_id = row["outlet_entity_id"]
        if outlet_entity_id is not None:
            return "news_outlet", outlet_entity_id, row["outlet_lean"]
        return "other", None, None
    if source_type == "reddit_post":
        subreddit_entity_id = row["subreddit_entity_id"]
        if subreddit_entity_id is not None:
            return "subreddit", subreddit_entity_id, row["subreddit_lean"]
        return "other", None, None
    if source_type == "x_post":
        author_entity_id = row["author_entity_id"]
        if author_entity_id is not None:
            source_class = "official" if is_official_kind(row["author_entity_kind"]) else "account"
            return source_class, author_entity_id, row["author_entity_lean"]
        handle = row.get("x_handle")
        if handle:
            return "x_user", handle.lower().lstrip("@"), None
        return "other", None, None
    return "other", None, None


def _speaker_tier_4way(row: Mapping[str, Any]) -> str:
    """WHO authored the mention: news / officials / affiliated / public --
    a finer vocabulary than the panel-level 3-way _tier_for_row, needed so
    received tone can separate 'officials talking about X' from 'curated
    affiliated accounts talking about X'. Deliberately does NOT collapse
    onto OFFICIAL_AUTHOR_TIERS: it subdivides that canonical set into its
    two constituent tiers rather than routing them together."""
    if row["source_type"] == "news":
        return "news"
    tier = row["author_tier"]
    if tier == "elected_official":
        return "officials"
    if tier == "affiliated":
        return "affiliated"
    return "public"


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
