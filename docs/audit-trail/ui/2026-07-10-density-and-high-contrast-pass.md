# 2026-07-10 — Bloomberg-density card layout + high-contrast tone graph

The dashboard now sizes cards to their content instead of stacking uniform
full-width slabs. The 12-col `dashboard-grid` aligns cards to the top of each
row (`align-items: start`) rather than stretching every card to its tallest
row sibling, so short cards no longer inflate into dead whitespace. Sections
that hold narrow content are paired side-by-side. The "As of last 7 days"
top-metrics graph reads its values as filled diverging bars (not a single
low-contrast dot), and the tone/tier colors are a targeted high-contrast set.

## What shipped

- **Removed** the "What each group is saying" topic-divergence panel
  (`TopicDivergencePanel.tsx`, its render in `PublicSentiment.tsx`, and the
  `.topic-divergence-*` CSS block). The three-group tone read still lives in the
  top-metrics tier rows; the shared `SentimentBreakdown` group fields it used
  stay (also consumed by `TopMetrics` + fixtures).
- **Content-sized cards** (`index.css`): `.dashboard-grid { align-items: start }`;
  dropped the forced `flex:1` / `.card { height:100% }` equal-height rules;
  `.card` / `.card-header` padding tightened `--space-4 → --space-3`.
- **Paired rows** (was all `col-span-12`): Tone — "Tone over time" +
  "Source signals" (6/6); Propaganda — "News vs. social media" (5) +
  "How this works" (7); Data Desk — "Movers board" (5) + "Small multiples" (7);
  Bots — the three distribution cards as a `col-span-4 ×3` band.
- **Diverging-bar tier graph** (`TopMetricsBlock.tsx` `TierRow` + `.tier-row-bar`
  CSS): a single-value row draws a filled bar from the 0 midpoint (tone axis) or
  the left edge (rate axis); multi-value rows (news vs social) keep enlarged,
  ringed markers. Replaces the 9px dot on a faint track.
- **Bounded** the propaganda technique-explorer examples column
  (`max-height: min(70vh,620px); overflow-y:auto`) so a long feed no longer
  towers the card and leaves the 6-item technique list in a huge empty track.
- **Headings → sans**: `.card-title` / `.card-subtitle` moved from
  `--font-display` (serif) to `--font-family` (Inter, 700, tight tracking).
- **Targeted high-contrast palette** (`index.css` `:root` + `theme.ts`): vivid
  `--semantic-positive #1a5fd0` / `--semantic-negative #d0261a` /
  `--semantic-warning #c2740a`; new speaker-tier hues
  `--tier-news #4a6fc0` (slate) / `--tier-officials #009e8b` (teal) /
  `--tier-public #c27a10` (amber), mirrored as `COLORS.tier*` and used by the
  tier sparklines (`PublicSentiment.tsx`) and the "Tone over time" series
  (`ToneTrendPanel.tsx`). UI chrome (header, borders, brand) stays ink-blue.

## Why

- The prior layout placed nearly every section at `col-span-12` and force-
  stretched cards to equal row height, producing the "uniform slab + big empty
  gutters" look the whole page read as. Bloomberg density = cards sized to
  content, narrow ones paired.
- The top-metrics dot marker was small and, under the deep-ink monochrome
  palette, low-contrast and near-colorless — hard to read the value at a glance.
  A filled diverging bar encodes magnitude and direction directly.
- Partially supersedes the pure-monochrome decision in
  `2026-07-11-deep-ink-retheme.md` for the *data* colors (tone bars + tier
  identity) only; the ink-blue UI chrome and the lean/source palettes from that
  retheme are unchanged. New tone/tier colors were run through the dataviz
  `validate_palette.js` (pos/neg CVD dE 98.3; tier trio worst adjacent dE 45+;
  all ≥3:1 on `#fcfcfb`).

## Follow-ups

- Paired-column spans (Tone 6/6, Propaganda 5/7, Desk 5/7) are sensible
  defaults set without a live render in this environment — verify against
  screenshots and tune (e.g. Source-signals table width) on the running app.
- Narratives "Story lifecycles" / "Stories spreading across groups" left at
  full width; revisit pairing them if it reads well.
- Modal `ToneBarRows` still use the dot-on-axis marker; could adopt the same
  bar language for consistency.
