"""
Public sentiment aggregator package.

Split from the former single ``sentiment.py`` module into cohesive
submodules (samples / favorability / target_tone / entities / aggregator)
behind a thin orchestrator. The JSON contract is unchanged — every
calculation, threshold, field name, sort, and filter is preserved.

This ``__init__`` re-exports the exact set of names imported from
``aggregators.sentiment`` elsewhere in the repo, so those import sites keep
working unchanged:

  * ``SentimentAggregator`` — aggregators/__init__, test_sample_enrichment.
  * ``CATCH_ALL_VERIFIED_OFFICIALS`` — test_official_tier_routing
    (re-exported from entity_registry, where the constant now lives).
  * ``MIN_TARGET_SAMPLE_N`` — test_target_tone_aggregation.
  * ``_build_sample_dict`` — entity_posts, test_analysis_remediation.
  * ``_build_doc_targets`` / ``_extract_topic`` — entity_posts.
"""

from analysis.src.reporting.aggregators.sentiment.aggregator import (
    SentimentAggregator,
    _extract_topic,
)
from analysis.src.reporting.aggregators.sentiment.samples import (
    _build_doc_targets,
    _build_sample_dict,
)
from analysis.src.reporting.aggregators.sentiment.target_tone import (
    MIN_TARGET_SAMPLE_N,
)
from analysis.src.reporting.entity_registry import CATCH_ALL_VERIFIED_OFFICIALS

__all__ = [
    "SentimentAggregator",
    "CATCH_ALL_VERIFIED_OFFICIALS",
    "MIN_TARGET_SAMPLE_N",
    "_build_sample_dict",
    "_build_doc_targets",
    "_extract_topic",
]
