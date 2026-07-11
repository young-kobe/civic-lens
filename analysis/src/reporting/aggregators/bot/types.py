"""
Typed accumulator for the per-doc bot-detection scan.

Replaces the untyped ~11-key dict ``_fetch_bot_detection_data`` used to
return. The fields are exactly the counts + per-doc / per-author
bookkeeping the format + downstream passes read; naming them keeps the
scan and its consumers from drifting on a bare-string dict key.
"""

from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass, field
from typing import Any, Dict, List, Set


@dataclass
class BotDetectionAggregate:
    """Result of the per-doc bot-detection scan.

    Pre-excluded rows (``inference_method='deterministic'``) are dropped
    from every count before this is populated, so ``total_eligible`` is
    the automation-rate denominator of eligible posts only.
    """
    total_eligible: int = 0
    bot_count: int = 0
    suspicious_count: int = 0
    by_cluster: Dict[str, int] = field(default_factory=dict)
    indicators_frequency: Counter = field(default_factory=Counter)
    hourly_distribution: Dict[int, int] = field(default_factory=lambda: defaultdict(int))
    bot_docs_data: List[Dict[str, Any]] = field(default_factory=list)
    bot_doc_ids: Set[int] = field(default_factory=set)
    bot_texts: List[str] = field(default_factory=list)
    bot_urls: List[str] = field(default_factory=list)
    bot_authors: List[str] = field(default_factory=list)
