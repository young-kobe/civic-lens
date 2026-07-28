"""
Outbound "who this bucket talks about" rollup (news + public tiers only;
an official's own outbound tone is expressed_alignment instead, see
received.py).
"""

from __future__ import annotations

from typing import Any, Dict, List, Mapping, Optional, Tuple

from analysis.src.api.models.sentiment import EntitySentimentItem, OutboundTargetCell, OutboundTargets
from analysis.src.api.queries.constants import DEM_TARGET_ALIASES, GOP_TARGET_ALIASES, MIN_TARGET_SAMPLE_N
from analysis.src.api.queries.sentiment.routing import _STANCE_KEYS, _empty_counts, _net_score, _route_outbound_bucket

# Outbound-target rollup ("who this bucket talks about"): named rows are
# capped; an unresolved raw_target needs to recur before earning its own row.
MAX_OUTBOUND_TARGETS = 8
MIN_OUTBOUND_RAW_RECURRENCE = 2
OUTBOUND_OTHER_LABEL = "Other targets"


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
