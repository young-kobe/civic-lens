"""
Pydantic response models for GET /movers: window-over-window tone and
favorability shifts, each carrying its own current/previous sample sizes.
"""

from __future__ import annotations

from typing import List, Optional

from analysis.src.api.models.common import CamelModel, EntityProfileModel, RangeMeta


class ToneMover(CamelModel):
    """One entity (`corpus.entities`) whose net political tone shifted
    between the previous equal-length period and the current one.
    `delta_pts` is signed: current_net - prev_net. Carries both
    `entity_id` (numeric) and `entity_key` (stable slug) -- owner decision
    2026-07-26: emit both so cross-page joins are exact, not a
    (kind, displayName) guess. `entity_profile` restores the old contract's
    editorial card (blurb/party/office/etc.) alongside the numbers."""

    entity_id: int
    entity_key: str
    kind: str
    display_name: str
    current_net: float
    prev_net: float
    delta_pts: float
    current_volume: int
    prev_volume: int
    entity_profile: EntityProfileModel


class FavorabilityMover(CamelModel):
    """The single largest window-over-window favorability shift among
    entities with `analysis.target_mentions` stance coverage in both
    periods (source swapped from the retired `analysis.favorability_stances`
    2026-07-25; the metric and shape are unchanged). Carries both
    `entity_id` and `entity_key`, same dual-identifier convention as
    ToneMover."""

    entity_id: int
    entity_key: str
    kind: str
    display_name: str
    current_net: float
    prev_net: float
    delta_pts: float
    current_volume: int
    prev_volume: int


class MoversResponse(CamelModel):
    """GET /movers payload. `current_range`/`previous_range` are each a
    RangeMeta (owner decision 2026-07-24) -- one per compared period, so
    the UI can label both halves of the comparison honestly."""

    current_range: RangeMeta
    previous_range: RangeMeta
    tone_movers: List[ToneMover] = []
    top_favorability_mover: Optional[FavorabilityMover] = None
