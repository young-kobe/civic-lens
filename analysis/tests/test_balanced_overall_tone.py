"""
Intent guard for the balanced "overall tone" headline.

WHY: the sample is not evenly split across speaker tiers — a window can carry
~900 news posts next to ~100 from the public. A volume-pooled net would let the
tier we happened to sample most dictate "overall tone." The headline instead
weights each tier (news / officials / public) EQUALLY: the mean of the per-tier
nets. This pins that contract.
"""

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting.aggregators.sentiment.aggregator import _balanced_net


def _counts(pos, neg, neu=0):
    return {"positive": pos, "negative": neg, "neutral": neu}


class TestBalancedOverallTone(unittest.TestCase):
    def test_news_volume_cannot_skew_the_headline(self):
        # 900 strongly-negative news posts vs 100 strongly-positive public posts.
        by_tier = {
            "news": _counts(0, 900),      # net -100
            "public": _counts(100, 0),    # net +100
        }
        # Volume-pooled would be about -80 (news dominates); balanced is the mean
        # of the two tier nets → 0.0.
        self.assertEqual(_balanced_net(by_tier, fallback=-80.0), 0.0)

    def test_equal_weight_across_three_tiers(self):
        by_tier = {
            "news": _counts(0, 100),      # -100
            "officials": _counts(50, 50), #    0
            "public": _counts(100, 0),    # +100
        }
        self.assertEqual(_balanced_net(by_tier, fallback=999), 0.0)

    def test_empty_tiers_drop_out_of_the_mean(self):
        by_tier = {
            "news": _counts(30, 10),      # +50
            "officials": _counts(0, 0),   # no posts -> excluded
        }
        self.assertEqual(_balanced_net(by_tier, fallback=999), 50.0)

    def test_falls_back_to_pooled_when_no_tier_has_data(self):
        self.assertEqual(_balanced_net({}, fallback=-12.3), -12.3)


if __name__ == "__main__":
    unittest.main()
