# 2026-07-11 — Tone-over-time: click a point to read that day's posts

Clicking a point on the Tone-over-time line chart now opens a modal of that calendar day's sampled
posts (mirroring the tone-intensity segment drill-down). The old weekday-rhythm bar chart was
removed in favor of this per-date drill-down, which scales with the page's time-range filter — the
chart is fetched per window, so a wider range simply yields more day points.

## Backend (analysis)

- **`sentiment/samples.py`** — `_collect_day_sample(day_samples, day, …)`: appends a built sample to
  a calendar-day bucket (`YYYY-MM-DD`), capped, reasoning-gated (same as the other collectors).
- **`sentiment/aggregator.py`** — a `day_samples` accumulator, filled with the row's `_day_key` day
  (the same key that drives the toneTrend series); `_format_day_samples` materializes
  `{date -> [ClassificationSample]}` (empty days dropped); emitted as
  `PublicSentimentResult.daySamples`.
- **`models/aggregator_models.py`** — `daySamples` field + `to_dict` serialization.
- **Test** (`test_sample_enrichment.py`) — asserts `daySamples` is populated, keyed by `YYYY-MM-DD`,
  and its samples carry the evidence/reasoning enrichment.
- `byDayOfWeek` still computed/emitted for backward compat, but nothing in the UI consumes it now.

## Frontend (ui)

- **`types.ts` / `transformers.ts`** — `PublicSentimentData.daySamples?` passed through.
- **`ToneTrendPanel.tsx`** — removed the weekday `WeekdayStrip` bar chart entirely (and its
  `Bar`/`BarChart`/`Cell` imports). Both the tier `LineChart` and the GOP `AreaChart` now take an
  `onDateClick`: clicking anywhere on the plot resolves the point's date (`activeLabel`) and opens
  the day modal; cursor is a pointer and the subtitle gains "Click a point to read that day's posts."
  The three tier lines (News / Officials / Public) are unchanged.
- **`PublicSentiment.tsx`** — `activeDate` state + `DaySamplesModal` (title formatted as
  "Friday, Jul 4, 2026"), opened from the point click, rendering the day's posts via `PostCardList`.
- **`fixtures.ts`** — `mockDaySamples()` keyed by the same `isoDay` dates as `mockToneTrend`, so a
  clicked point resolves to posts in mock mode.

## Time-range wiring

The chart already scopes to the page's time-range filter: the sentiment snapshot is fetched per
window (`fetchSentiment(window)`), so `toneTrend` — and now `daySamples` — reflect the selected
24h/7d/30d/90d range with no extra wiring.

## Verification

- Sentiment/aggregator tests green (32); UI `typecheck` + `build` green. Real data populates after
  the next `save_snapshots`; fixtures show it now.
