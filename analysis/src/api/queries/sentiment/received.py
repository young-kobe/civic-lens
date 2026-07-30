"""
Received-tone accumulation and formatting: how sampled discourse talks
ABOUT a tracked official (or party collective), including provenance
(who the tone comes from) and expressed same/cross-party alignment.
"""

from __future__ import annotations

import math
from typing import Any, Dict, List, Mapping, Optional, Tuple

from analysis.src.api.models.sentiment import (
    EntitySentimentItem,
    ExpressedAlignment,
    ReceivedNarrativeCell,
    ReceivedSourceCell,
    ReceivedSpeakerTierCell,
    ReceivedTone,
    ReceivedTopicCell,
)
from analysis.src.api.queries.constants import DEM_TARGET_ALIASES, GOP_TARGET_ALIASES, MAX_SAMPLES_PER_TARGET, MIN_TARGET_SAMPLE_N
from analysis.src.api.queries.profiles import is_official_kind, sampled_account_profile
from analysis.src.api.queries.sentiment.routing import (
    _STANCE_KEYS,
    _bump_stance_bucket,
    _build_classification_samples,
    _empty_counts,
    _net_score,
    _normalize_party,
    _route_received_source,
    _speaker_tier_4way,
    _top_pairs,
)

# Top narratives driving an entity's received tone, ported from the
# pre-redesign target_tone.py.
MAX_NARRATIVES_PER_TARGET = 5

# Received-tone provenance ("who this tone comes from"): named individual
# sources are capped; the (source_class x lean) rollup itself is not.
MAX_RECEIVED_TOP_SOURCES = 8

_SOURCE_CLASS_GROUP_NOUN = {
    "news_outlet": "news outlets", "subreddit": "subreddits",
    "official": "officials", "account": "accounts", "x_user": "x users",
}


def _engagement_weight(engagement: Optional[int]) -> float:
    """Log-damped reach-proxy weight: 1 for zero/unknown engagement,
    growing sub-linearly so one viral post can't own an official's
    weighted net alone."""
    return 1.0 + math.log1p(max(0, engagement or 0))


def _received_accum_default() -> Dict[str, Any]:
    return {
        "counts": _empty_counts(),
        "by_topic": {}, "by_speaker_tier": {}, "by_narrative": {}, "by_source": {},
        "w_pos": 0.0, "w_neg": 0.0, "w_total": 0.0,
        "samples": [],
    }


def _received_source_entity_ids(target_rows: List[Any]) -> set:
    """Registry entity ids on the SOURCE side of a received-tone mention
    (outlet/subreddit/author entity) -- widens fetch_entity_profiles'
    fetch beyond the tier-bucket ids so received_from_top's registry
    cells can carry an entity_profile."""
    ids: set = set()
    for row in target_rows:
        source_class, ident, _lean = _route_received_source(row)
        if source_class not in ("x_user", "other"):
            ids.add(ident)
    return ids


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
    source_class, ident, lean = _route_received_source(row)
    source_cell = accum["by_source"].setdefault(
        (source_class, ident),
        {"source_class": source_class, "ident": ident, "lean": lean, "counts": _empty_counts(), "row": row},
    )
    source_cell["counts"][stance] += 1
    accum["samples"].append((row["confidence"], row))


def _accumulate_target_tone(
    target_rows: List[Any], narrative_map: Dict[int, List[Tuple[int, str]]],
) -> Tuple[Dict[int, Dict[str, Any]], Dict[str, Dict[str, Any]], Dict[int, Dict[str, Dict[str, int]]],
           Dict[str, Dict[str, int]], int, int]:
    """One pass over target_rows building: received tone per official
    entity_id (kind='official', see profiles.is_official_kind), the two
    party-collective received accumulators (raw_target alias match), each
    official-speaker's same/cross-party expressed_alignment cells, the
    global alignment baseline, and the resolved/unresolved mention counts
    (resolved = entity_id IS NOT NULL, the strict registry-resolution
    reading of the column -- collective alias matches on an unresolved
    raw_target still count as unresolved here, even though they get their
    own received-tone rollup below)."""
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

        if entity_id is not None and is_official_kind(row["kind"]):
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
            author_entity_id is not None and is_official_kind(row["author_entity_kind"])
            and entity_id is not None and entity_id != author_entity_id
        ):
            speaker_party = _normalize_party(row["author_entity_lean_source"])
            target_party = _normalize_party(row["target_lean_source"])
            if speaker_party and target_party:
                cell = "same" if speaker_party == target_party else "cross"
                cells = alignment.setdefault(author_entity_id, {"same": _empty_counts(), "cross": _empty_counts()})
                cells[cell][stance] += 1
                baseline[cell][stance] += 1

    return received, collectives, alignment, baseline, resolved_mentions, unresolved_mentions


def _group_label(source_class: str, lean: Optional[str]) -> str:
    """Simple, deterministic label for a received_from_groups cell -- the
    UI builds its own polished display label from source_class + lean
    (raw enum values), this is just a stable fallback/debug string."""
    if source_class == "other":
        return "other"
    return f"{lean or 'unknown'} {_SOURCE_CLASS_GROUP_NOUN[source_class]}"


def _source_identity(
    source_class: str, ident: Any, sample_row: Mapping[str, Any], profiles: Dict[int, Any],
) -> Tuple[str, Optional[str], Optional[Any]]:
    """(label, entity_key, entity_profile) for one received_from_top cell.
    Registry sources (news_outlet/subreddit/official/account) look up
    their profile from the widened fetch_entity_profiles set -- a missing
    id renders entity_profile=None rather than raising (provenance
    profiles are a separate lookup concern from the tier-bucket
    ValueError contract in _format_entity_item). x_user sources are
    unresolved sampled X authors -- handle as label, entity_key always
    None, profile built from the row's own already-fetched bio fields."""
    if source_class == "x_user":
        profile = sampled_account_profile(
            ident, sample_row.get("x_display_name"), sample_row.get("x_description"),
        )
        return ident, None, profile
    profile = profiles.get(ident)
    if profile is None:
        return f"entity-{ident}", None, None
    return profile.display_name, profile.key, profile


def _received_source_groups(by_source: Dict[Any, Dict[str, Any]], total_volume: int) -> List[ReceivedSourceCell]:
    """Rollup by (source_class, lean) -- always includes the 'other'
    bucket when it has any volume, so shares sum to 1.0 over the received
    block's total."""
    rollup: Dict[Tuple[str, Optional[str]], Dict[str, int]] = {}
    for cell in by_source.values():
        lean = cell["lean"] if cell["source_class"] != "other" else None
        counts = rollup.setdefault((cell["source_class"], lean), _empty_counts())
        for stance, n in cell["counts"].items():
            counts[stance] += n

    cells = []
    for (source_class, lean), counts in rollup.items():
        volume = sum(counts.values())
        low_sample = volume < MIN_TARGET_SAMPLE_N
        cells.append(ReceivedSourceCell(
            source_class=source_class, label=_group_label(source_class, lean), lean=lean,
            share=round(volume / total_volume, 3) if total_volume else 0.0,
            volume=volume, net=None if low_sample else _net_score(counts), low_sample=low_sample,
        ))
    cells.sort(key=lambda c: (-c.volume, c.label))
    return cells


def _received_source_top(
    by_source: Dict[Any, Dict[str, Any]], total_volume: int, profiles: Dict[int, Any],
) -> List[ReceivedSourceCell]:
    """Top MAX_RECEIVED_TOP_SOURCES named individual sources by volume,
    excluding the 'other' bucket (that one has no single named source)."""
    cells = []
    for (source_class, _ident), cell in by_source.items():
        if source_class == "other":
            continue
        counts = cell["counts"]
        volume = sum(counts.values())
        low_sample = volume < MIN_TARGET_SAMPLE_N
        sample_row = cell["row"]
        label, entity_key, profile = _source_identity(source_class, cell["ident"], sample_row, profiles)
        cells.append(ReceivedSourceCell(
            source_class=source_class, label=label, entity_key=entity_key, lean=cell["lean"],
            entity_profile=profile,
            share=round(volume / total_volume, 3) if total_volume else 0.0,
            volume=volume, net=None if low_sample else _net_score(counts), low_sample=low_sample,
        ))
    cells.sort(key=lambda c: (-c.volume, c.label))
    return cells[:MAX_RECEIVED_TOP_SOURCES]


def _format_received(
    accum: Dict[str, Any], rich_samples: Dict[int, dict], profiles: Dict[int, Any],
) -> ReceivedTone:
    """Assembles one ReceivedTone, including received_from_groups/_top
    provenance. Applies uniformly to officials' received blocks AND the
    gop/dem collective rollups (both flow through _accumulate_received) --
    a collective's provenance denominator is built from the same
    alias-matched-unresolved rows that feed its net tone, not from an
    entity_id-resolved mention."""
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
        received_from_groups=_received_source_groups(accum["by_source"], volume),
        received_from_top=_received_source_top(accum["by_source"], volume, profiles),
        samples=_build_classification_samples(samples, rich_samples),
    )


def _format_alignment(cells: Dict[str, Dict[str, int]]) -> ExpressedAlignment:
    return ExpressedAlignment(
        same_party_net=_net_score(cells["same"]), same_party_volume=sum(cells["same"].values()),
        cross_party_net=_net_score(cells["cross"]), cross_party_volume=sum(cells["cross"].values()),
    )


def _attach_received(
    lookup: Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem],
    received: Dict[int, Dict[str, Any]], rich_samples: Dict[int, dict], profiles: Dict[int, Any],
) -> None:
    for entity_id, accum in received.items():
        item = lookup.get(("officials", ("entity", entity_id)))
        if item is not None:
            item.received = _format_received(accum, rich_samples, profiles)


def _attach_expressed_alignment(
    lookup: Dict[Tuple[str, Tuple[str, Any]], EntitySentimentItem],
    alignment: Dict[int, Dict[str, Dict[str, int]]],
) -> None:
    for entity_id, cells in alignment.items():
        item = lookup.get(("officials", ("entity", entity_id)))
        if item is not None:
            item.expressed_alignment = _format_alignment(cells)
