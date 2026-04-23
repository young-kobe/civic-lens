# 2026-04-23 — GOP stance layout stability + mobile P0/P1 fixes

A cluster of small changes driven by user-reported inconsistencies and the first pass of a deeper mobile-first UI audit. Targets the most visible bugs: the GOP stance card reflowing across source filters, plus tap-target and overflow issues on Review and Bot pages at phone widths.

## What shipped

### GOP stance — trend slot now always rendered

`ui/src/pages/PublicSentiment.tsx::GOPMini`. The `.mini-metric` CSS grid is `grid-template-columns: auto auto 1fr auto` — 4 columns: label, value, trend, stance-bar. The prior code conditionally rendered the Sparkline only when `trend && trend.length > 1`. When the active source filter (`news` or `social`) produced a trend with 0 or 1 daily points, the Sparkline was omitted entirely; the stance-bar then reflowed into the `1fr` slot and ballooned in width. Visually: the card looked different under different filters while showing the same data.

Fix: the trend slot is now always rendered. When there's no trend to draw, a new `.mini-metric-trend-empty` placeholder paints a single dashed baseline in the same 60×22 slot (`ui/src/index.css` next to `.mini-metric-trend`). Grid shape stays stable across filters; the stance-bar stays pinned on the right.

### `formatPct` policy — out-of-range returns fallback, not clamped value

`ui/src/services/format.ts`. Earlier today the guard clamped out-of-range values to `[min, max]` before display. User: *"we must not show incorrect numbers."* A clamped `100%` derived from a buggy `197` is still a false number, and the reader can't tell it apart from a legitimate `100%`. Policy revised: out-of-range now returns the fallback (`"—"` by default) and the dev-mode console warning still fires. Honest "no trustworthy value" beats plausible-looking misinformation.

Full rationale recorded in `docs/audit-trail/ui/2026-04-22-percent-display-guard.md` (updated).

### Mobile tap targets — `.btn-sm` now 44px on phones

`ui/src/index.css` — `.btn-sm` keeps its compact 28px min-height on desktop (mouse precision is fine) but upsizes to `min-height: 44px` with larger padding and 12px font at `max-width: 640px`, meeting the WCAG 2.1 touch-target minimum. Affects the "Skip this one," "Correct," "Incorrect," and "View details" buttons in Review + Bot modals that were hard to tap accurately on phones.

### Review stats bar — collapses to 1-col at ≤480px

`ui/src/index.css`. `.review-stats-grid` already had breakpoints at 768px (→3 col) and 640px (→2 col). The audit flagged the 2-col layout clipping labels like "Reviewer ID" and "Your confidence" at 320–480px. Added a third breakpoint at `max-width: 480px` that drops to 1 column and removes the `grid-column: span 2` override on the last child. Labels breathe.

### Review item source-text — long URLs no longer force horizontal scroll

`ui/src/pages/review/ReviewItemCard.tsx`. The `<details>` block rendering the raw source text used `whiteSpace: 'pre-wrap'` but had no word-break rule. A single long unbroken URL or concatenated token would blow past the viewport on phones. Added `overflowWrap: 'anywhere'` + `wordBreak: 'break-word'` so any string breaks on character boundaries when it can't find whitespace.

### Heatmap — hour-labels row now scrolls in sync with the grid

`ui/src/components/charts/Heatmap.tsx`. Previously the hour-labels row sat *outside* the horizontal-scroll container and the 24-cell grid sat inside it. On phones that couldn't fit 24 columns the grid would scroll but the labels stayed pinned, producing a visible misalignment (labels pointing at the wrong cells). Moved the hour-labels row inside the same `overflow-x: auto` wrapper and gave it `minWidth: 'max-content'` so labels and cells scroll together.

## Why

Two user messages:

> *"the gop party stance ticker chart visuals are inconsistent depending on if the source is filtered by all sources or news/socials. this goes back to my consistency point. fix this and ensure we are consistent everywhere! single source of truth for reusable components"*
>
> *"the bot and review tabs in mobile do not fit the screen correctly. we need to ENSURE that the entire ui is modular and reuses common components themes and styles across the app."*

The GOP stance fix is the direct answer to the first; the mobile fixes kick off a broader campaign to address the second.

## Audit follow-ups (tracked, not yet done)

A thorough mobile-first UI audit ran today and surfaced a larger set of issues than fit in one change. Cataloged in `docs/todos/ui-consistency-audit.md`. Headline items carried over:

- **P1** — Review controls row (`gap-4` without min-width or flex-basis) wraps awkwardly at 320–480px.
- **P1** — `.bot-section-label` is a one-off pattern on the Bot page that should either generalize to a shared `<SectionHeader>` or go away.
- **P2** — Three different stat-row implementations across Review / Bot / PublicSentiment. `<TierRow>` already exists; unify Review's ad-hoc `Stat` and Bot's `CoordinationSummary` row onto it.
- **P2** — `Home.tsx::TabCard` is a hand-rolled button with inline hover styles. Extract to a shared `<CardButton>` (or fold into `<Card>` with an `onClick` prop).
- **P2** — `formatRelativeDate` is local to `Narratives.tsx`; promote to `services/format.ts`.
- **P2** — `toLocaleString()` and `toFixed(N)` calls scatter across pages. Add `formatCount(n)` + `formatScore(n)` helpers to centralize.

## Validation

`npm run typecheck` clean after each edit in the chain.
