# Pre-rewrite page geometry and card design restored

**Date:** 2026-07-26
**Layer:** ui (cross-link: [api](../api/2026-07-26-docs-author-join.md))
**Todo:** docs/todos/ui-feature-restoration.md

The Phase 10 contract adaptation had replaced the Bloomberg-dense visual
layer with stripped-down pages (-8,313 lines of JSX). Owner decision
2026-07-26: the tree at tag `pre-cutover-main` is the visual source of
truth; the current API contract is the data source of truth; data-dead
panels keep their old geometry and render a degraded cousin of the data.

## The system as it is now

- `PostCard`/`PostCardList` (old 504-line card) is back: renders instantly
  from the thin `SampleDoc`, then lazily hydrates author line, engagement,
  tone/technique/target chips, and evidence-span highlighting from the
  existing `GET /docs/{id}` (module-cached, 4-concurrent queue in
  `services/lazyHydration.ts`). `SampleCard` is a re-export shim.
- `EntityProfileCard` (lean-colored border, hydrating variant),
  `EntityHubLinks`, and `ThreeWayGrid`'s toolbar + lean filter pills are
  restored; `matchesLeanFilter` now reads `LeanLabel`.
- Page trees restored from `pre-cutover-main`: `PublicSentiment` (topic
  tabs from `byTopic`, tone-trend panel over day-of-week/time-of-day
  buckets, divergence/polling as quiet empty-frames), `Narratives`
  (lifecycle panel, cross-tier list and three-way columns re-keyed to
  dominant source, since no first-seen-entity concept survives in the
  contract), `BotActivityProfiler` (coordination frames degraded,
  behavioral cards over live buckets), `Propaganda` (leaderboard and grid
  degraded to `bySource`), `DataDesk` (matrix grouping, freshness card
  over `pipelineRun`), `ReviewItemCard` (per-task evidence rendering,
  duck-typed off `raw_response`).
- `index.css` needed no geometry changes -- the old rules had survived.

## Not restorable without contract additions (tracked in the todo)

Per-entity narrative grouping (first-seen entity), per-entity propaganda
rates, coordination evidence fields, polling comparison, daily tone
series, per-entity sample filtering. The "restorable from stored data"
section of the todo lists the read-side queries that would upgrade each.
