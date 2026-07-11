"""
Narrative aggregator package.

Split from the former single ``narrative.py`` into three modules — a batched
``repository`` (SQL), a pure ``projector`` (NarrativeSummary assembly), and the
``aggregator`` entry point — to fix the per-narrative N+1 query pattern without
changing any API behaviour. ``NarrativeAggregator`` is re-exported here so
``from analysis.src.reporting.aggregators.narrative import NarrativeAggregator``
keeps resolving unchanged.
"""

from analysis.src.reporting.aggregators.narrative.aggregator import (
    DEFAULT_LIMIT,
    NarrativeAggregator,
    TOP_SUPPORTING_DOCS_LIMIT,
)

__all__ = ["NarrativeAggregator", "DEFAULT_LIMIT", "TOP_SUPPORTING_DOCS_LIMIT"]
