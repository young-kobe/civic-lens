# 2026-07-11 — Green/red tone palette, fully-angular bars, technique bar-graphic

Follow-up data-viz round after the monochrome-chrome split
([2026-07-11-monochrome-chrome-and-dataviz-palette](./2026-07-11-monochrome-chrome-and-dataviz-palette.md)):
retune the tone palette to green/red, make every bar angular, reconcile the story-lifecycle color
system, and turn the propaganda technique list into an interactive bar graphic paired with the
top-metrics block.

## What shipped

- **Positive tone is now Grafana GREEN, not blue.** `--semantic-positive` `#1a5fd0` → `#1a8a3d`
  (light `#d6ecdd`); the tone-intensity ramp poles follow (`--tone-strong-pos` = green,
  `--tone-mild-pos` `#79c090` soft green; negative side stays red). Blue/red implied a partisan
  Dem-vs-Rep read; green/red reads as "good vs bad tone" and leaves the blue/indigo strictly to
  lean/tier. This also removed the last blue mark on Propaganda's top-metrics (the low-flagged-rate
  "positive" dot was blue → now green). CAVEAT noted in `:root`: green/red is a red-green-CVD-risk
  pair, mitigated because every tone mark always carries its numeric value + sign/label.
- **Every bar is angular** (ask: "bar charts need to be all angular… no rounded edges"). Zeroed the
  radius on `.technique-explorer-row-bar/-fill`, `.tier-row-bar` (was `--radius-full`, a stadium),
  `.mini-metric-bar`, `.digest-story-bar`, `.chart-swatch`, and the two inline account-age /
  similarity bar tracks. Completed the angular system by setting **`--radius-sm: 0`** (buttons,
  inputs, badges) — only `--radius-full` remains, for genuinely circular elements (avatars, stadium
  filter pills, status dots).
- **Story-lifecycle colors reconciled.** `NarrativeLifecyclePanel` origin lines and the `.cross-tier-
  chip-*` chips now both use the canonical speaker-tier palette (news/officials/public = blue/teal/
  amber). Previously the line borrowed source colors (officials rendered as the chrome accent — now
  black after the chrome refactor — and public reused reddit-orange), so the line and the chips
  beside it disagreed. Chips derive a legible darkened-on-hue text + light tint via `color-mix` so
  the chip reads as the same color as its line. (The home "Most repeated claims" bar stays on the
  source palette — it is a platform/source breakdown, a different axis from first-seen tier.)
- **Technique explorer → interactive bar graphic + modal** (`propaganda/TechniqueExplorer.tsx`).
  Was a two-column list-plus-inline-feed. Now a full-width bar chart: hovering a bar surfaces the
  technique's plain-language explanation (`title` + aria), clicking opens a `Modal` of the flagged
  posts carrying it (evidence highlighted). Deep-link `#propaganda?technique=<name>` opens the modal;
  no auto-open on load. Removed the orphaned `.technique-explorer` grid, its media query,
  `.technique-explorer-examples`, and `.technique-explorer-row-active` CSS.
- **Propaganda condensed to one row.** `PropagandaTopMetrics` (the "As of last N days" block,
  `col-span-5`) now sits beside `TechniqueExplorer` (`col-span-7`). Both read from the same windowed
  `data`, so they filter in step with the page's time range; pairing them removes a full-width row.
- **"As of last N days" tier rows stacked + roomy.** In the narrower col-span-5 column the single-line
  label|axis|value|verb row cramped. Added a `rowsClassName` opt-in on `TopMetricsBlock` and a
  `.propaganda-tier-rows` variant that stacks each metric: label + value(%) on top (value right),
  the magnitude bar stretched full width beneath, the explanation wrapped below it, generous
  row spacing, endpoints hidden (they'd collide with the wrapped verb).

## Why

- Review asks: "primary positive/negative color split needs to green and red grafana style, not
  red/blue, which implies democrat vs republican"; "bar charts need to be all angular. angular
  everywhere no rounded edges"; "make the techniques being used a data visual / interactive graphic…
  hover to see a brief explanation and click to open the modal displaying the flagged posts"; "why do
  Story-lifecycle colors differ from Most-repeated-claims"; "put [techniques] on same row as As-of-
  last-N-days on propaganda, filter in step, condense onto one row."

## Verification

- `npm run typecheck` + `npm run build` green.
- Visual pass via fixtures pending (dev env): confirm green positive tone everywhere, no blue/navy on
  Propaganda, all bars square, lifecycle line color == its tier chips, and the technique bars open a
  modal on click / explain on hover, sitting beside the top-metrics block.
