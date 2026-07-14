# 2026-07-12 — Propaganda: top-3 flagged-offenders leaderboard replaces the top-metrics block

The Propaganda page's "As of last N days" top-metrics block is replaced by a **top-3 leaderboard** of
the highest flagged-rate sources in the selected window, rendered with the same ranked row-card the
three-way columns use. Same slot (`col-span-5`), same size.

## What shipped (`Propaganda.tsx`)

- `topFlaggedOffenders(data)` — pools `by_news_outlet` + `by_official` + `by_general_public`, drops
  catch-all buckets and sources under `MIN_OFFENDER_VOLUME` (10 scored posts, so a lone flagged post
  can't read as a 100% rate), sorts by `flagged_rate_pct` desc (tie-break by flagged count), takes 3.
- `TopFlaggedLeaderboard` — a `Card` titled "Top flagged offenders" (subtitle carries the window, so
  the date-range scoping stays visible) rendering `RankedEntityList` — the exact ranked card used in
  the three-way columns; each row opens the same entity modal. It's driven by the windowed payload,
  so the global date-range selector filters it.
- `RankedEntityList` gained an optional `description` slot rendered **under the name** (opt-in, so the
  three-way columns stay compact; rows now top-align). `toOffenderRow` fills it with the entity blurb
  (who they are, clamped) + a why-line ("34 of 88 posts flagged · high technique saturation"), giving
  the card more vertical presence and explaining why each source ranks. New `.ranked-entity-main` /
  `-desc` / `-blurb` / `-why` CSS.
- Removed `PropagandaTopMetrics` and its now-orphaned imports (`TierRow`, `TopMetricsBlock`,
  `TierRowDot`, `RATE_ENDPOINTS`) and the dead `.propaganda-tier-rows` CSS variant. The overall
  flagged-rate / top-technique headline it used to show still lives in the page's `GlobalTicker`, so
  nothing is lost.

## Why

- Review ask: show a top-3 leaderboard of the highest flag-rate offenders for the selected
  timeframe, using the same three-way-column row card, replacing the "As of last N days" content in
  full at the same size.

## Verification

- `typecheck` + `build` green. In fixtures the board shows a cross-tier top 3 (e.g. POTUS, Fox News,
  r/Conservative by flagged rate); on real data it reflects the selected window.
