"""
Pydantic response models for GET /outlet-profiles: per-domain/subreddit
net tone x bot rate, with the preserved bot-content-inclusion exception
(see queries/outlets.py's module docstring).
"""

from __future__ import annotations

from typing import List, Optional

from analysis.src.api.models.common import CamelModel, RangeMeta, SampleDocModel


class OutletProfile(CamelModel):
    """Cross-signal profile for one domain/subreddit. `net_tone` is
    -100..100 points; None when the outlet has zero sentiment-scored docs
    in range (never emitted once the volume floor admits the row)."""

    outlet_key: str
    source_type: str
    net_tone: Optional[float] = None
    bot_rate_pct: float
    volume: int
    total_scanned: int
    samples: List[SampleDocModel] = []


class OutletProfilesResponse(CamelModel):
    """GET /outlet-profiles payload. `disclaimer` states the preserved
    exception up front: unlike every other panel, bot-authored content is
    INCLUDED here on purpose. `range` is the RangeMeta honesty block
    (owner decision 2026-07-24)."""

    range: RangeMeta
    disclaimer: str
    outlets: List[OutletProfile] = []
