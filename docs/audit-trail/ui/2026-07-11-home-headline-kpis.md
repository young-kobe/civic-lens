# 2026-07-11 — Home digest: headline KPI strip

The "This week in the sample" digest leads with a row of headline numbers — the volume, overall
tone, and coverage the per-tier/signal blocks below don't state outright.

## What shipped

- **`home/DigestSection.tsx`** `HeadlineStats`: a 4-KPI strip (Sampled posts · Overall tone ·
  Stories tracked · Topics covered), rendered above the movers ticker. All derived from the same
  7d snapshots the digest already loads — `sentiment.overview.volume/netScore`,
  `narratives.length`, `sentiment.byTopic` (count + the top topic as the detail line). Overall-tone
  value is tone-colored.
- **`index.css`** `.digest-kpis` / `.digest-kpi*`: a 4-up grid of bordered stat tiles (2-up on
  phones), big tabular-mono values.

## Why

- Review ask: "update homepage to use our richer metrics for this week in the sample." The digest
  showed per-tier tone, top claims, and two signal tiles but never the headline volume/tone/coverage
  numbers a reader wants first.
