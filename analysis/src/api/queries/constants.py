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

# Author-level bot-exclusion gate (owner decision 2026-07-25, replacing the
# retired BOT_SCORE_AUTHOR_EXCLUSION -- see
# docs/audit-trail/analysis/2026-07-25-bot-exclusion-gate.md). Label-driven,
# not a numeric score: an author is excluded once at least this SHARE of
# their confidence-floored analyzed posts
# (analysis.author_bot_scores.bot_post_count + .suspicious_post_count,
# over .sample_count) were labelled bot or suspicious by the LLM. 0.5 means
# "at least half this author's analyzed posts were labelled bot or
# suspicious" -- interpretable in a way the old additive score never was.
# The prior threshold was calibrated against a formula that no longer
# exists (the deleted hand-tuned _aggregate_score), so this value is a
# fresh choice carried over as a starting point, not a re-derivation --
# it needs validation against real data before the next acceptance pass.
BOT_FLAGGED_SHARE_EXCLUSION = 0.5
# reporting/aggregators/sentiment/entities.py.
MIN_SAMPLED_AUTHOR_POSTS = 3
MIN_SAMPLED_AUTHOR_FOLLOWERS = 1000
MAX_SAMPLED_AUTHOR_CARDS = 12

# Collective partisan-target alias sets, ported verbatim from
# reporting/entity_registry.py's _GOP_TARGET_ALIASES / _DEM_TARGET_ALIASES.
# Power the gop_collective/dem_collective received-tone rollups: a
# target_mentions row's raw_target, lowercased, is matched against these
# sets when it names the party rather than an individual official.
GOP_TARGET_ALIASES = frozenset({
    "gop", "republican party", "republicans", "republican", "rnc",
    "house republicans", "senate republicans", "congressional republicans",
})
DEM_TARGET_ALIASES = frozenset({
    "democratic party", "democrats", "democrat", "dems", "dnc",
    "house democrats", "senate democrats", "congressional democrats",
})
