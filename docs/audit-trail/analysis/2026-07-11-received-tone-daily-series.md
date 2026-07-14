# 2026-07-11 — Received-tone daily series (received.dailyTone)

Each tracked official's `received` tone now carries a per-day series — "tone directed AT them" over
time — so the Tone page can overlay the most-criticized / most-praised officials on the
tone-over-time chart. Previously the only daily series was *expressed* tone (an entity's own posts),
which answers a different question.

## What shipped

- **`reporting/aggregators/sentiment/target_tone.py`**
  - `_merge_target_tone` accumulates a `by_day` stance-count cell per target (keyed by a local
    `_received_day_key` — defined in-module to avoid a cycle with `aggregator`, which imports this
    module). The mention's `published_at` day is cached in the per-doc context.
  - `_format_received` materializes `dailyTone` — `[{date, net, volume, lowSample}]`, trailing
    `_RECEIVED_TREND_MAX_DAYS` (30), net suppressed below `MIN_TARGET_SAMPLE_N` per day — mirroring
    the expressed `daily_tone` shape so both overlay on the same chart at the same resolution.
- **`analysis/tests/test_received_daily_tone.py`** (new) — pins per-day net + per-day suppression
  (a 1-mention day is a gap, not a spike) and the empty-series fallback on older-shape accumulators.

## Why

- Tone-page feature (user ask): surface the most negatively / positively talked-about officials and
  overlay their received tone over time. The ranking reads from existing `received.net` /
  `bySpeakerTier`; the overlay needed this new daily series.

## Verification

- Target-tone + rich-aggregator tests green (25); the series appears on real data after the next
  `save_snapshots`. UI: [2026-07-11-tone-targets-panel](../ui/2026-07-11-tone-targets-panel.md).
