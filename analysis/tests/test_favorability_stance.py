"""
Unit tests for GOP favorability stance handling
(``analysis.src.reporting.aggregators.sentiment.favorability``).

WHY these matter: ``overall_gop_stance`` is a four-value enum
(favorable / unfavorable / neutral / mixed), but the UI's favorability surface
is a THREE-bucket shape whose percentages must sum to 100, and the field can
arrive with off-enum casing. The aggregator must therefore normalize case and
fold ``mixed`` into neutral (non-directional: counted in the denominator, zero
net) exactly like the movers favorability path — otherwise mixed/uppercase rows
silently skew every percentage and vanish from the per-platform counts.
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
    def test_mixed_and_uppercase_fold_into_neutral(self):
        rows = [
            _row(1, "favorable"),
            _row(2, "unfavorable"),
            _row(3, "neutral"),
            _row(4, "mixed"),        # non-directional -> neutral
            _row(5, "FAVORABLE"),    # off-enum casing -> favorable
            _row(6, "Mixed"),        # cased mixed -> neutral
        ]
        distribution, by_platform, _daily, count = _parse_favorability_rows(
            rows, bot_docs=set(), allowed_sources=None,
        )
        self.assertEqual(count, 6)
        # No separate mixed bucket; mixed + neutral collapse together.
        self.assertNotIn("mixed", distribution)
        self.assertEqual(distribution["favorable"], 2)   # favorable + FAVORABLE
        self.assertEqual(distribution["unfavorable"], 1)
        self.assertEqual(distribution["neutral"], 3)      # neutral + mixed + Mixed

    def test_mixed_rows_counted_in_per_platform(self):
        rows = [_row(1, "mixed", "x_post"), _row(2, "favorable", "x_post")]
        _distribution, by_platform, _daily, _count = _parse_favorability_rows(
            rows, bot_docs=set(), allowed_sources=None,
        )
        # The mixed row must not vanish from the platform split.
        self.assertEqual(by_platform["x_post"]["neutral"], 1)
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

    def test_percentages_sum_to_100_with_folded_mixed(self):
        # 2 favorable, 1 unfavorable, 3 neutral (incl. former mixed) = 6 total.
        distribution = {"favorable": 2, "unfavorable": 1, "neutral": 3}
        by_platform = {"x_post": {"favorable": 2, "unfavorable": 1, "neutral": 3}}
        fav = self._result(distribution, by_platform).gopFavorability
        self.assertAlmostEqual(
            fav["favorable"] + fav["unfavorable"] + fav["neutral"], 100.0, places=1,
        )
        # net = (fav - unfav) / total, denominator includes the folded neutral.
        self.assertAlmostEqual(fav["netFavorability"], round((2 - 1) / 6 * 100, 1), places=1)

    def test_platform_group_labels_are_stable(self):
        distribution = {"favorable": 1, "unfavorable": 0, "neutral": 0}
        by_platform = {
            "x_post": {"favorable": 1, "unfavorable": 0, "neutral": 0},
            "reddit": {"favorable": 0, "unfavorable": 0, "neutral": 0},
            "news": {"favorable": 0, "unfavorable": 0, "neutral": 0},
        }
        groups = {p["group"] for p in self._result(distribution, by_platform).gopByPlatform}
        self.assertEqual(groups, {"X", "Reddit", "News"})
        self.assertNotIn("X_post", groups)


class _NoCache:
    def load(self, _key):
        return None


if __name__ == "__main__":
    unittest.main()
