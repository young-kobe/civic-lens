# 2026-07-11 — Page frame + three-way wireframe + layout fixes

The dashboard adopts Bloomberg's containment model: a **continuous outer frame** (hairline rails
down both edges of the content column), **modules spaced** inside it (not touching), and grouped
components like the three-way grid wrapped in their **own bordered wireframe with vertical column
dividers**. (An earlier pass that made every module touch edge-to-edge via zeroed gaps + negative-
margin overlap was reverted — it malformed the three-way toolbar and read as cramped; the frame +
spacing model below is the current one.)

## What shipped

- **Page frame** (`index.css` `.app-container`): `border-left`/`border-right` hairlines + full-
  viewport `min-height` so the rails run the whole page, Bloomberg-style.
- **Module spacing restored**: `.dashboard-grid` / `.grid-2/3/auto` back to `gap: var(--space-3)`
  (bumped for readability); stacked modules in a cell get a `--space-3` top margin. Removed the
  negative-margin overlap.
- **Three-way wireframe** (`ThreeWayGrid.tsx` + `index.css`): `ThreeWayGrid`/`TwoWayGrid` now wrap
  their columns in a `.three-way-frame` (outer border) with an optional `toolbar` header band; the
  grid has `gap: 0` with a `border-right` hairline dividing each column; columns get padding and
  their cards go back to normal `6px` spacing. Every page's three-way grid reads as one contained
  module.
- **Toolbar fixed + boxed** (`ThreeWayToolbar`): now the frame's header band (bottom rule only, no
  free-floating bar that overflowed). The **officials search input was removed** (it malformed the
  bar); the lean/party filter stays.
- **Narratives "Search claims" box removed** (`Narratives.tsx`): the input, its `query` state, and
  the client-side filtering are gone (every panel just renders the full window); dead
  `.narrative-search` CSS removed.
- **Data Desk right column**: Small multiples + Movers stack in the right column (Movers fills the
  formerly-empty box). Small-multiples cells carry a `title` so hovering the sparkline (not just the
  label) shows the full story name.
- **Narratives**: Story lifecycles + Stories-spreading share a row (col-span-6); lifecycles capped
  at 5.

## Why

- Iterative Bloomberg review: "a wireframe to encapsulate the entire 3-way grid… vertical dividers
  between the columns… the entire page should have continuous borders on edges containing the whole
  page… bump vertical spacing between modules slightly." The touch-everything approach conflicted
  with that (and the toolbar overflow); a page frame + contained module groups is the correct read.

## Later refinements (same day)
- **Column header stacked**: the sort control moved off the title's row to its own line beneath
  (`ThreeWayColumn`), and the title now spans the full column width so its underline reads as a
  clean section rule (`.three-way-column-head` is a block; removed `.three-way-column-headings`).
- **Home tone digest**: the top-metrics `TierRow` overflowed in the ~300px digest block (Phase B's
  `max-content` verb). Added a `.digest-tier-rows .tier-row` grid-areas override that stacks it
  (label + value / axis / verb) and hides the ±100 endpoint labels there.

## Follow-ups
- Verify via fixtures; tune rail/divider weights and the three-way frame padding on the running app.
