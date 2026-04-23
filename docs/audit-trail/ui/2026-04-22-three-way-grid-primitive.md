# 2026-04-22 — Shared ThreeWayGrid + ThreeWayColumn primitives

The three-way News / Officials / Public frame that runs across every data page is now a reusable pair of components. Four near-identical inline implementations collapsed to one source of truth.

## What shipped

- `ui/src/components/common/ThreeWayGrid.tsx` (new) — exports:
  - `ThreeWayGrid` — thin wrapper around `<div className="three-way-grid">`.
  - `ThreeWayColumn` — renders the header/byline block and either `children` (when populated) or the per-column empty-state copy. Generic over what caller renders as children.
- `ui/src/components/common/index.ts` — both exports added to the barrel.
- Migrations:
  - `PublicSentiment.tsx` — dropped inline `ThreeWayGrid` + `ThreeWayColumn` components (~55 lines), renamed the page-specific wrapper to `SentimentThreeWayGrid`, wired to the shared primitive.
  - `Narratives.tsx` — dropped inline `ThreeWayColumn` (~30 lines), renamed remainder to `NarrativeThreeWayColumn`, wired to shared primitive + `<ThreeWayGrid>` wrapper (was raw `<div className="three-way-grid">`).
  - `Propaganda.tsx` — dropped inline `ThreeWayColumn` (~20 lines) and converted `ThreeWayEntityGrid` to use the shared primitive.
  - `BotActivityProfiler.tsx` — dropped inline `BotThreeWayColumn` (~20 lines), collapsed `BotThreeWayGrid` to use the shared primitive, and **removed the `if (!hasAny) return null` early-return** so the three-way frame stays visible even when some tiers are empty (per-column empty copy handles it honestly).

Net: ~125 lines of duplicated wrapper markup and empty-state plumbing replaced with a 50-line shared component.

## Why

The CSS (`.three-way-grid`, `.three-way-column`, `.three-way-column-header`, `.three-way-column-byline`) was already shared; the markup that produced it was not. Four copies of the same pattern were drifting — Bot hid the grid entirely when all tiers were empty, Propaganda showed per-column empty copy, Tone and Narratives did too. The user noticed the inconsistency and asked for a shared component. Now every tab behaves identically on empty tiers: column with its header + an honest "no news articles in this window" line.

The extraction also makes the frame a first-class contract. Any new page adopting the three-way frame starts from the primitive instead of re-deriving the markup.

## API

```tsx
<ThreeWayGrid>
  <ThreeWayColumn
    header="The News"
    byline="Top outlets by coverage volume"
    empty="No news articles in this window."
    isEmpty={news.length === 0}
  >
    {news.map((item) => <EntityProfileCard key={item.key} … />)}
  </ThreeWayColumn>
  … officials column …
  … public column …
</ThreeWayGrid>
```

Deliberately children-based rather than render-prop + generic `items[]`. Every page already had its own item-rendering logic with different card types; passing mapped nodes as children keeps that flexibility without coupling the column component to any specific item type.

## Follow-ups

None. If a fifth consumer appears and wants the same "top-N slice + render each" loop, promote that slicing logic into a second primitive. Today the per-page slicing + render is the only variance and stays inline.
