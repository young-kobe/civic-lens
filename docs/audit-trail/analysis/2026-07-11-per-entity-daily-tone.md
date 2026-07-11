# 2026-07-11 — Per-entity daily net-tone series

Each `EntitySentimentItem` now carries a `dailyTone` series — its own net tone day by day,
suppressed below the sample floor. This powers the Tone-over-time chart's tier→entity drill-down
(see UI entry `2026-07-11-tone-over-time-drilldown.md`).

## What shipped

- **`aggregators/sentiment/entities.py`**: `_init_entity_bucket` seeds `by_day: {}`;
  `_route_and_record` takes a `day` arg and threads it to `_collect_entity_sample`;
  `_consolidate_sampled_authors` folds demoted authors' `by_day` into the catch-all.
- **`aggregators/sentiment/samples.py`**: `_collect_entity_sample` increments `by_day[day]` per
  counted post.
- **`aggregators/sentiment/aggregator.py`**: computes `day = _day_key(...)` once and passes it to
  `_route_and_record` (reused for the existing `by_day_tier`); `_format_entity_items` builds
  `daily_tone` — dates ascending, trailing `_TONE_TREND_MAX_DAYS`, net suppressed below
  `MIN_TARGET_SAMPLE_N` (same floor and gap semantics as the tier `toneTrend`).
- **`reporting/models/aggregator_models.py`**: `EntitySentimentItem.daily_tone` (default `[]`),
  serialized as `dailyTone` only when non-empty (older snapshots stay byte-identical).

## Why

- The tier `toneTrend` (news/officials/public) answered "which group is diverging" but not "who
  inside that group is driving it." A per-entity daily series lets the UI drill a tier into its
  individual outlets/officials/accounts. The per-post day key was already computed for the tier
  series; this reuses it at the entity level.

## Follow-ups
- Populated on the next snapshot rebuild (`./run.sh analyze --tasks text,snapshots`). Pre-rebuild
  snapshots omit `dailyTone`; the UI treats absent as "no drill available" for that tier.
