"""
Intent guard for the received-tone daily series (received.dailyTone).

WHY: the Tone page overlays "tone directed AT an official" over time and ranks
the most-criticized / most-praised officials. That needs a per-day received-tone
series with the SAME small-sample suppression as every other tone number — a
1-mention day must render as a gap (net=None), never a +/-100 spike. This pins
that `_format_received` emits one dailyTone point per day, net-suppressed below
the floor, sorted by date.
"""

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting.aggregators.sentiment.target_tone import (
    MIN_TARGET_SAMPLE_N,
    _format_received,
)


def _counts(pos, neg, neu=0):
    return {"positive": pos, "negative": neg, "neutral": neu}


def _accum(by_day):
    """A per-target accumulator with only the fields _format_received reads;
    overall counts are the sum across days."""
    total = _counts(0, 0, 0)
    for c in by_day.values():
        for k in total:
            total[k] += c.get(k, 0)
    return {
        "kind": "official",
        "counts": total,
        "by_topic": {},
        "by_speaker": {},
        "by_narrative": {},
        "by_day": by_day,
        "w_pos": 0.0, "w_neg": 0.0, "w_total": 0.0,
        "samples": [],
    }


class TestReceivedDailyTone(unittest.TestCase):
    def test_daily_series_per_day_net_and_suppression(self):
        self.assertGreaterEqual(5, MIN_TARGET_SAMPLE_N - 0)  # floor sanity
        by_day = {
            "2026-07-02": _counts(6, 2),   # n=8 -> net +50.0
            "2026-07-03": _counts(1, 4),   # n=5 -> net -60.0
            "2026-07-04": _counts(1, 0),   # n=1 -> suppressed (None)
        }
        out = _format_received(_accum(by_day))
        daily = out["dailyTone"]
        self.assertEqual([d["date"] for d in daily],
                         ["2026-07-02", "2026-07-03", "2026-07-04"])
        self.assertEqual(daily[0]["net"], 50.0)
        self.assertEqual(daily[0]["volume"], 8)
        self.assertFalse(daily[0]["lowSample"])
        self.assertEqual(daily[1]["net"], -60.0)
        self.assertIsNone(daily[2]["net"])
        self.assertTrue(daily[2]["lowSample"])
        self.assertEqual(daily[2]["volume"], 1)

    def test_absent_by_day_yields_empty_series(self):
        accum = _accum({})
        accum.pop("by_day")  # simulate an older-shape accum
        self.assertEqual(_format_received(accum)["dailyTone"], [])


if __name__ == "__main__":
    unittest.main()
