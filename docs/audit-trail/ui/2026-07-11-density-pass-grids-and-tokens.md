# 2026-07-11 — Density pass: equal-height grids + tightened spacing scale

The dashboard now hugs content in every grid, not just the 12-col
`dashboard-grid`. The three-way / two-way entity frames and the `grid-2` /
`grid-3` utility rows were still stretching a short column to its tallest
sibling's height, which left the large empty bands visible under a short
"Politicians & Officials" tier, beside the Data Desk "Human review agreement"
card, and below the Bot Detector similarity/domain panels. All four grid
families now `align-items: start`. The large end of the spacing scale
(`--space-8/10/12/16`) and the page chrome paddings were pulled down a notch so
sections read as grouped modules rather than loose stacks.

## What shipped

- **Equal-height grids** (`ui/src/index.css`): added `align-items: start` to
  `.three-way-grid`, `.two-way-grid`, `.grid-2`, and `.grid-3` — mirroring the
  `.dashboard-grid` fix from `2026-07-10-density-and-high-contrast-pass.md`.
  This is the primary fix for the circled dead space on the Overall Tone,
  Political Narratives, Data Desk, and Bot Detector pages.
- **Tightened spacing tokens** (`:root`): `--space-8` 2rem→1.5rem,
  `--space-10` 2.5rem→1.75rem, `--space-12` 3rem→2.25rem, `--space-16` 4rem→3rem.
  The 0–6 fine-grain steps are unchanged (they drive dense inner layouts where
  shrinking further crowds).
- **Chrome paddings**: `<main>` padding `--space-4/--space-10` → `--space-3/--space-6`
  (`ui/src/App.tsx`); `.page-header` bottom margin `--space-4`→`--space-3`;
  `.surface-hero` `--space-8 --space-6`→`--space-6 --space-5`; `.top-metrics`
  and `.reads-as-today` `--space-3 --space-4`→`--space-2 --space-3`.

## Why

- `2026-07-10-density-and-high-contrast-pass.md` fixed only `.dashboard-grid`.
  The other four grid families kept the default `align-items: stretch`, so the
  same "short card inflates into dead whitespace" problem persisted anywhere
  those grids paired an uneven pair/trio — exactly the areas flagged in the
  2026-07-11 dashboard review screenshots.
- The spacing scale's high end was set for a looser register than the
  Bloomberg-dense modules the page is now targeting.

## Follow-ups

- Three-way columns still cap/expand and lack a top toolbar (sort/search/lean
  filter) and a populated "The Public" column — separate PR (Phase 2 of the
  2026-07-11 dashboard-review plan).
- Bot Detector's empty "narratives with suspected bot amplification" state
  still pairs a tall coordination card with a short section-label band when
  there is no amplification data; situational, left as-is.
