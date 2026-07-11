# 2026-07-11 — Top-metrics: tier-row width + legible intensity bar

The Overall Tone top-metrics block no longer strands a wide empty band to the right of
the tier rows, and the tone-intensity bar reads as an actual bar instead of a tiny sliver.

## What shipped

- **Tier-row template** (`index.css` `.tier-row`): `160px 1fr 72px 1fr` →
  `160px minmax(0,1fr) 72px max-content`. The trailing `1fr` verb column left a wide empty
  band right of the short "…· 620 sampled posts" text; the verb is now content-sized and the
  axis bar (2nd column) absorbs the slack — a bigger, more legible tone bar and no dead space.
  The `.tier-row-has-trail` sparkline variant keeps its own template.
- **Intensity bar** (`PublicSentiment.tsx` `IntensityMini` + `index.css`): the bar reused the
  shared 4-column `.mini-metric-visual` grid, which left an empty 60px trend slot and a trailing
  `1fr` pad and pinned the bar to 120px×8px. Added an `is-intensity` modifier that lays the
  wrapper out with flex (bar `flex: 1 1 auto`, 110–300px, 11px tall; legend right after; wraps on
  narrow screens) — the bar fills its space and the internal whitespace is gone. Colors come from
  the Phase A `--tone-*` standardization, so the bar now matches the tier-row tone blue/red.

## Why

- Round-2 design review circled both the right-side empty band and the "hard to see, off-palette"
  intensity bar. See `2026-07-11-angular-module-system.md` for the palette standardization this
  builds on.

## Follow-ups
- Remaining round-2 reflows: Propaganda, Data Desk, Bot Detector.
