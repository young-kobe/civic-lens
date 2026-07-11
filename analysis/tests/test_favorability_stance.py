"""
Unit tests for GOP favorability stance handling
(``analysis.src.reporting.aggregators.sentiment.favorability``).

WHY these matter: ``overall_gop_stance`` is a four-value enum
(favorable / unfavorable / neutral / mixed), and ``mixed`` (both genuine praise
AND criticism) is semantically distinct from ``neutral`` (no stance). The
aggregator must normalize off-enum casing (like movers' defensive str().lower())
AND keep ``mixed`` as its own bucket rather than folding it into neutral —
folding would overstate indifference and drop mixed rows from the per-platform
counts. ``mixed`` counts in the denominator with zero net contribution (so
netFavorability is unaffected), and all four buckets sum to ~100.
"""

import json
import sys
import unittest
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.reporting.aggregators.sentiment.favorability import (
    _parse_favorability_rows,
    _format_favorability_result,
)
from analysis.src.reporting.models import (
    PublicSentimentResult,
    SentimentDistribution,
    SentimentOverview,
)


def _empty_result() -> PublicSentimentResult:
    return PublicSentimentResult(
        overview=SentimentOverview(netScore=0, volume=0, coverage=0, confidence=0),
        distribution=SentimentDistribution(
            strongPositive=0, mildPositive=0, neutral=0,
            mildNegative=0, strongNegative=0,
        ),
        byPlatform=[],
        disclaimer="",
        excluded_bot_content=0,
    )


def _row(doc_id, stance, source_type="x_post", pub_at=None):
    # (doc_id, output_json, confidence, source_type, pub_at)
    return (doc_id, json.dumps({"overall_gop_stance": stance}), 0.9, source_type, pub_at)


class ParseStanceTests(unittest.TestCase):
    def test_case_normalized_and_mixed_kept_distinct(self):
        rows = [
            _row(1, "favorable"),
            _row(2, "unfavorable"),
            _row(3, "neutral"),
            _row(4, "mixed"),        # its own bucket, NOT folded into neutral
            _row(5, "FAVORABLE"),    # off-enum casing -> favorable
            _row(6, "Mixed"),        # cased mixed -> mixed
            _row(7, "bogus"),        # truly out-of-enum -> neutral
        ]
        distribution, _by_platform, _daily, count = _parse_favorability_rows(
            rows, bot_docs=set(), allowed_sources=None,
        )
        self.assertEqual(count, 7)
        self.assertEqual(distribution["favorable"], 2)    # favorable + FAVORABLE
        self.assertEqual(distribution["unfavorable"], 1)
        self.assertEqual(distribution["neutral"], 2)      # neutral + bogus
        self.assertEqual(distribution["mixed"], 2)        # mixed + Mixed

    def test_mixed_rows_counted_in_per_platform(self):
        rows = [_row(1, "mixed", "x_post"), _row(2, "favorable", "x_post")]
        _distribution, by_platform, _daily, _count = _parse_favorability_rows(
            rows, bot_docs=set(), allowed_sources=None,
        )
        # The mixed row must not vanish from the platform split.
        self.assertEqual(by_platform["x_post"]["mixed"], 1)
        self.assertEqual(by_platform["x_post"]["favorable"], 1)


class FormatStanceTests(unittest.TestCase):
    def _result(self, distribution, by_platform):
        result = _empty_result()
        _format_favorability_result(
            result, distribution, by_platform, daily_net={},
            count=sum(distribution.values()),
            cache=_NoCache(),
        )
        return result

    def test_four_buckets_sum_to_100_and_net_ignores_mixed(self):
        # 2 favorable, 1 unfavorable, 1 neutral, 2 mixed = 6 total.
        distribution = {"favorable": 2, "unfavorable": 1, "neutral": 1, "mixed": 2}
        by_platform = {"x_post": {"favorable": 2, "unfavorable": 1, "neutral": 1, "mixed": 2}}
        fav = self._result(distribution, by_platform).gopFavorability
        # mixed is its own visible bucket; the four sum to ~100.
        self.assertAlmostEqual(
            fav["favorable"] + fav["unfavorable"] + fav["neutral"] + fav["mixed"],
            100.0, places=1,
        )
        # net = (fav - unfav) / total; mixed sits in the denominator, zero net.
        self.assertAlmostEqual(fav["netFavorability"], round((2 - 1) / 6 * 100, 1), places=1)

    def test_per_platform_carries_mixed_and_stable_labels(self):
        distribution = {"favorable": 1, "unfavorable": 0, "neutral": 0, "mixed": 1}
        by_platform = {
            "x_post": {"favorable": 1, "unfavorable": 0, "neutral": 0, "mixed": 1},
            "reddit": {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0},
            "news": {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0},
        }
        by_plat = self._result(distribution, by_platform).gopByPlatform
        groups = {p["group"] for p in by_plat}
        self.assertEqual(groups, {"X", "Reddit", "News"})
        self.assertNotIn("X_post", groups)
        x_row = next(p for p in by_plat if p["group"] == "X")
        self.assertEqual(x_row["mixed"], 1)


class _NoCache:
    def load(self, _key):
        return None


if __name__ == "__main__":
    unittest.main()
