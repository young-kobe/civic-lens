# 2026-07-11 — Tone over time: entity drill-down + interactive legend + evidence

The Tone-over-time chart's overlaid tier lines (news/officials/public) are now interactive. A
color-coded legend sits under the chart: hovering an item explains it and isolates its line;
clicking a tier **drills the chart into that tier's individual entities** (one line each); clicking
an entity opens the shared entity modal with its **live classified posts** as evidence.

## What shipped

- **`ToneTrendPanel.tsx`** rewritten around a generic `MultiLineChart` (used for both the tier
  overview and the entity drill) plus a custom `ToneLegend`:
  - Legend items are buttons — `title` gives the hover explanation, hover sets a `highlightKey`
    that dims the other lines, and `onClick` drills (tiers) or opens evidence (entities).
  - Tier click → `toEntityRows(entitiesByTier[tier])` merges the tier's entities' `dailyTone` into
    date-keyed rows, top 6 by volume, each a line with an ordered categorical color from existing
    tokens (accent / tier teal+amber / lean plum / slate / brick).
  - Entity click → `onOpenEntity(item)` opens the existing `EntitySentimentModal`, whose "Show all
    posts" pages the live `/entity-posts` endpoint — no new modal or fetch path.
  - A "← All groups" control returns from the drill; the "By group / Toward GOP" toggle is unchanged.
  - Replaces the recharts `<Legend>` with the custom one.
- **`PublicSentiment.tsx`**: passes `entitiesByTier` (byNewsOutlet/byOfficial/byGeneralPublic) and
  `onOpenEntity={setActiveEntity}` into the panel.
- **`types.ts`**: `EntitySentimentItem.dailyTone?: EntityDailyTonePoint[]`.
- **`index.css`**: `.tone-legend*` styles.
- **`fixtures.ts`**: `mockDailyTone` seeded onto the news/officials/public entities so the drill and
  divergence render in mock mode without a snapshot rebuild.

## Why

- Review ask: "tone over time needs overlaid line charts with the different entities so you can see
  the divergence… color-coded legend with hover-tip explanation, clickable to open a modal that
  pulls classified evidence." Chosen scope (confirmed): tiers default, drill to entities, evidence
  from the live endpoint. Backed by `analysis/2026-07-11-per-entity-daily-tone.md`.

## Follow-ups
- Entity-line colors reuse existing tokens as a categorical set; if >6 entities matter, run them
  through the dataviz validator and consider a folded "Other" line.
- Real entity divergence needs the snapshot rebuild that populates `dailyTone`.
