"""
Pure bot-signal calculations (unit-testable, no DB access).

Coordination-index, text-similarity (identical pairs + shingle-Jaccard
buckets), and link-domain concentration. The similarity helpers are
imported directly by tests, so their names and semantics are contracts.
"""

import re
from collections import Counter
from typing import Any, Dict, List, Tuple


def _compute_coordination_index(hourly_distribution: Dict[int, int]) -> float:
    if not hourly_distribution:
        return 0.0
    total = sum(hourly_distribution.values())
    if total == 0:
        return 0.0
    return max(hourly_distribution.values()) / total


def _shingles(text: str, k: int = 8) -> set:
    """k-word shingle set for a piece of text. Used as a cheap similarity basis."""
    words = re.findall(r"\w+", text.lower())
    if len(words) < k:
        return {" ".join(words)} if words else set()
    return {" ".join(words[i:i + k]) for i in range(len(words) - k + 1)}


def _jaccard(a: set, b: set) -> float:
    if not a or not b:
        return 0.0
    inter = len(a & b)
    return inter / len(a | b) if (a | b) else 0.0


def _text_similarity_signals(
    bot_texts: List[str],
    *,
    max_compare: int = 400,
) -> Tuple[int, Dict[str, int]]:
    """Return (identicalTextPairs, copyPasteSimilarityBuckets).

    Identical-text count is cheap: pairs of texts with normalized lowercase
    identical content. Similarity buckets use 8-gram Jaccard over a capped
    sample to stay O(N^2) safe. Buckets: high (>=0.7), medium (0.3-0.7),
    low (<0.3).
    """
    sample = bot_texts[:max_compare]
    identical = 0
    buckets = {"high": 0, "medium": 0, "low": 0}
    if len(sample) < 2:
        return 0, buckets

    normalized = [re.sub(r"\s+", " ", t.strip().lower()) for t in sample]
    # Identical-text pair count (exact matches).
    counts = Counter(normalized)
    for text, n in counts.items():
        if n > 1:
            identical += n * (n - 1) // 2

    # Shingle-based buckets over a capped pairwise comparison.
    shingle_sets = [_shingles(t) for t in normalized]
    for i in range(len(shingle_sets)):
        for j in range(i + 1, len(shingle_sets)):
            sim = _jaccard(shingle_sets[i], shingle_sets[j])
            if sim >= 0.7:
                buckets["high"] += 1
            elif sim >= 0.3:
                buckets["medium"] += 1
            else:
                buckets["low"] += 1
    return identical, buckets


def _link_domain_concentration(bot_urls: List[str]) -> List[Dict[str, Any]]:
    if not bot_urls:
        return []
    counter = Counter(bot_urls)
    total = sum(counter.values())
    top = counter.most_common(5)
    return [
        {
            "domain": domain,
            "percentage": round((count / total) * 100, 1) if total else 0.0,
        }
        for domain, count in top
    ]
