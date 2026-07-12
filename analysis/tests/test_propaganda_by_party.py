"""
Intent guard for the propaganda by-party rollup (Phase 4).

WHY: the Propaganda page shows which partisan SIDE leaned harder on persuasion
techniques by grouping the tracked officials' per-entity buckets by party. This
pins the aggregation contract: rows are grouped by the official's party, the
flagged rate / mean score are volume-weighted across that party's officials,
officials with no party are excluded (not bucketed as a phantom side), and the
result is sorted by flagged rate descending.
"""

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting.aggregators.propaganda import _rollup_by_party


def _bucket(party, total, flagged, score_sum):
    """One per-official accumulator entry as _accumulate_entity builds it."""
    return {
        "kind": "official",
        "profile": {"party": party} if party is not None else {},
        "total": total,
        "flagged": flagged,
        "score_sum": score_sum,
    }


class TestRollupByParty(unittest.TestCase):
    def test_groups_officials_by_party_and_weights_by_volume(self):
        by_official = {
            "@rep_a": _bucket("R", total=100, flagged=40, score_sum=30.0),
            "@rep_b": _bucket("R", total=100, flagged=20, score_sum=20.0),
            "@rep_c": _bucket("D", total=50, flagged=5, score_sum=5.0),
        }
        out = {p.party: p for p in _rollup_by_party(by_official)}
        self.assertEqual(set(out), {"R", "D"})

        r = out["R"]
        self.assertEqual(r.party_label, "Republican")
        self.assertEqual(r.official_count, 2)
        self.assertEqual(r.total_docs, 200)
        self.assertEqual(r.flagged_docs, 60)
        self.assertEqual(r.flagged_rate_pct, 30.0)          # 60/200
        self.assertEqual(r.mean_score, 0.25)                # 50/200

        d = out["D"]
        self.assertEqual(d.flagged_rate_pct, 10.0)          # 5/50
        self.assertEqual(d.official_count, 1)

    def test_partyless_officials_excluded(self):
        """Catch-all / unaffiliated officials (no party) must not appear as a
        side — they'd read as a real partisan bucket."""
        by_official = {
            "@rep_a": _bucket("R", 10, 3, 2.0),
            "_catch_all": _bucket(None, 90, 1, 1.0),
        }
        out = _rollup_by_party(by_official)
        self.assertEqual([p.party for p in out], ["R"])

    def test_sorted_by_flagged_rate_desc(self):
        by_official = {
            "@d": _bucket("D", 100, 50, 40.0),   # 50%
            "@r": _bucket("R", 100, 10, 5.0),    # 10%
        }
        out = _rollup_by_party(by_official)
        self.assertEqual([p.party for p in out], ["D", "R"])

    def test_empty_when_no_officials(self):
        self.assertEqual(_rollup_by_party({}), [])


if __name__ == "__main__":
    unittest.main()
