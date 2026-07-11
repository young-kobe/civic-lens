"""
Bot activity aggregator package.

Re-exports ``BotAggregator`` (the public entry point) plus the pure
text-similarity / link-concentration helpers that the test suite imports
directly from ``aggregators.bot``.
"""

from analysis.src.reporting.aggregators.bot.aggregator import BotAggregator
from analysis.src.reporting.aggregators.bot.metrics import (
    _jaccard,
    _link_domain_concentration,
    _shingles,
    _text_similarity_signals,
)

__all__ = [
    "BotAggregator",
    "_jaccard",
    "_link_domain_concentration",
    "_shingles",
    "_text_similarity_signals",
]
