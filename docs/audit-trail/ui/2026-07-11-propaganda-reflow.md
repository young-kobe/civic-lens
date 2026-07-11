# 2026-07-11 — Propaganda: drop News-vs-social, full-width how-it-works, compact technique legend

The Propaganda page no longer carries a buried, redundant "News vs. social media" card, the
"How this works" panel spans the full width instead of stranding an empty column beside it, and
the "Techniques being used" legend is a tight single-line list instead of a tall stacked one.

## What shipped

- **Removed `NewsVsSocialCard`** (`Propaganda.tsx`) and its `SPLIT_DOT_COLOR` const — its
  flagged-rate/saturation numbers already appear in the top-metrics "News vs social" tier row and
  the reads-as-today line. Dropped the now-orphaned `Card` / `PropagandaSourceSplit` imports and
  the dead `.source-split-*` CSS block (`index.css`).
- **How-this-works full width**: the old `col-span-5` (news-vs-social) + `col-span-7`
  (how-this-works) pair collapsed to a single `col-span-12` `HowThisWorks`, so nothing is stranded.
- **Compact technique legend** (`TechniqueExplorer.tsx` + `index.css`): each row was a stacked
  block (name/count → bar → italic blurb) with `--space-2` padding. It's now a single-line grid
  — name · inline bar · count · pct — with the blurb moved to the button `title`. The left track
  narrowed (`minmax(260px,5fr) 7fr` → `minmax(220px,4fr) 8fr`), giving the examples feed more room.

## Why

- Round-2 review: "News vs social media is buried, awkward, hard to understand — move, turn into a
  legend, or remove" (removed, per the user's choice, since it's duplicate data); the technique
  table was "good information but inefficient and not very pretty… the legend does not need to take
  up that much space." Builds on the angular/serif/color pass in
  `2026-07-11-angular-module-system.md`.

## Follow-ups
- Remaining round-2 reflows: Data Desk (2×2 module block), Bot Detector (2-up amplification cards).
