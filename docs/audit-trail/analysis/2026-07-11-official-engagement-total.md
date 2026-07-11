# 2026-07-11 — Per-entity engagement total on sentiment cards

Each per-entity sentiment accumulator now sums the engagement (likes +
reposts + replies + quotes) of every post it counts, and the total rides out
on `EntitySentimentItem` as `engagementTotal`. This powers the officials
column's engagement-weighted default sort on the Overall Tone page (see the UI
entry `2026-07-11-three-way-column-toolbar-and-scroll.md`).

## What shipped

- **`aggregators/sentiment/entities.py`**: `_init_entity_bucket` seeds
  `engagement_total: 0`; `_consolidate_sampled_authors` folds demoted authors'
  totals into the "Other X users" catch-all.
- **`aggregators/sentiment/samples.py`**: `_collect_entity_sample` adds
  `sum(engagement.values())` per counted post — before the sample cap, so it
  covers ALL posts in the window, not just the retained samples.
- **`aggregators/sentiment/aggregator.py`**: `_format_entity_items` passes
  `engagement_total` through to the model.
- **`reporting/models/aggregator_models.py`**: `EntitySentimentItem.engagement_total`
  (default 0), serialized as `engagementTotal` only when non-zero — older
  cached snapshots and news outlets (no engagement signal) stay byte-identical.

## Why

- The officials column ranked by raw post volume, which surfaced whoever posted
  most, not whoever landed. Engagement-weighted order surfaces the
  highest-reach voices first — the signal a reader of that column wants. The
  data (per-post engagement) was already flowing to the sample builder; it was
  simply never aggregated to the entity.

## Follow-ups

- Takes effect on the next snapshot rebuild (`./run.sh analyze --tasks
  text,snapshots` or a full run). Pre-rebuild snapshots omit `engagementTotal`;
  the UI treats absent as 0 and falls back to volume order.
