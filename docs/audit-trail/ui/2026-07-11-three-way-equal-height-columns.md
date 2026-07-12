# 2026-07-11 — Three-way columns cap to equal height

The News / Officials / Public columns in every three-way frame now cap to the same height and align
at the bottom. Previously only a column with more than 10 items got the internal scroll cap, so a
shorter tier (e.g. News with a handful of outlets) rendered full-height and towered over a capped
neighbor, stranding dead space beside it.

## What shipped

- **`ThreeWayGrid.tsx`** — every column's card body is now always wrapped in
  `.three-way-column-scroll` (was gated on `items.length > collapsedCount`); removed the now-unused
  `collapsedCount` prop + `DEFAULT_COLLAPSED_COUNT`.
- **`index.css` `.three-way-column-scroll`** — the scroll region is now `flex: 1 1 auto; min-height:
  0` so it grows to fill its column, with a shared `max-height: min(72vh, 760px)`. Since the grid is
  `align-items: stretch`, all columns take the same height; each body flex-fills that height (a short
  tier no longer leaves a gap below its cards) and a long tier caps at the max-height and scrolls
  internally. The Bot Detector two-way grid inherits the same behavior.

## Why

- Review screenshot: the Officials column was capped/scrolled short while News ran full-height,
  leaving a large empty band beside it. Ask: cap the columns to the same size.

## Verification

- `typecheck` + `build` green. Visual check via fixtures: the three tiers should now bottom-align
  with no stranded dead space; a tier longer than the cap scrolls inside its column.
