"""
Floors and caps for the Phase 9 strictly-live API query layer, ported
verbatim from the pre-redesign reporting/aggregators/ modules so requests
aggregating corpus/analysis directly render identically to the values
Kobe already reviewed and signed off on. See
docs/audit-trail/analysis/2026-07-24-phase9-prewave.md for the full
name -> origin-module mapping.
"""

from __future__ import annotations

from datetime import timedelta

# Window key -> lookback duration. Panels take one of these four keys as a
# query param; there is no 'all' key -- an all-time view reads corpus/
# analysis with no cutoff at all, it is not a fifth window.
WINDOWS = {
    "24h": timedelta(hours=24),
    "7d": timedelta(days=7),
    "30d": timedelta(days=30),
    "90d": timedelta(days=90),
}

# Strong-vs-mild classification threshold.
# reporting/aggregators/constants.py::STRONG_CONFIDENCE_THRESHOLD.
STRONG_CONFIDENCE_THRESHOLD = 0.7

# Evidence-span presentation caps.
# reporting/aggregators/evidence.py::SNIPPET_MAX_CHARS / MAX_EVIDENCE_PER_SAMPLE.
SNIPPET_MAX_CHARS = 120
MAX_EVIDENCE_PER_SAMPLE = 5

# Per-panel sample caps.
# reporting/aggregators/sentiment/samples.py.
MAX_DISTRIBUTION_SAMPLES_PER_BUCKET = 15
MAX_SAMPLES_PER_TOPIC = 5
MAX_SAMPLES_PER_ENTITY = 10
MAX_SAMPLES_PER_TARGET = 5

# Small-sample suppression floors: below these, a net score is withheld
# (None) rather than rendered from too few data points.
# reporting/aggregators/sentiment/target_tone.py.
MIN_TARGET_SAMPLE_N = 5
BOT_SCORE_AUTHOR_EXCLUSION = 0.5
# reporting/aggregators/sentiment/entities.py.
MIN_SAMPLED_AUTHOR_POSTS = 3
MIN_SAMPLED_AUTHOR_FOLLOWERS = 1000
MAX_SAMPLED_AUTHOR_CARDS = 12
