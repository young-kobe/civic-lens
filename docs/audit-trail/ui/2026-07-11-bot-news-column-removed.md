# 2026-07-11 — Bot Detector drops The News column

News is no longer bot-scored (articles are not accounts — analysis entry:
`../analysis/2026-07-11-news-doc-quality.md`), so the Bot Detector's
three-way grid became a two-way grid.

## What shipped

- **`TwoWayGrid`** (`components/common/ThreeWayGrid.tsx`): two-column
  variant sharing `ThreeWayColumn`; CSS `.two-way-grid` mirrors
  `.three-way-grid` (collapses to one column below 1024px).
- **Bot Detector** (`pages/BotActivityProfiler.tsx`): the grid renders
  Officials + Public only, with a `card-note` under it: "News articles are
  not bot-scored: articles are not accounts, so an outlet has no automation
  rate." Reads of `by_news_outlet` (deep-link resolution, funnel total)
  remain and degrade gracefully on stale caches.
- **Data Desk** (`pages/DataDesk.tsx`): news rows no longer feed the Bot
  rate column (em dash); the column header tooltip states why.
- Bot fixtures ship `by_news_outlet: []` to match the contract.

## Why

- A third of the grid was dedicated to a metric the system now refuses to
  compute. Removing the column (rather than a permanent explainer column)
  keeps the page about what IS measured; the one-line note covers the why.
