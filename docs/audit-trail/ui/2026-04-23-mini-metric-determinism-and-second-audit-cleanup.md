# 2026-04-23 — Deterministic `.mini-metric` layout + second-pass audit cleanup

Real fix for the GOP party stance card reflowing across source filters, plus a handful of issues surfaced by a deeper second-pass UI audit. This supersedes the "placeholder sparkline" fix from earlier today — that was a symptom patch; the root cause was structural.

## What shipped

### `.mini-metric` — deterministic 3-column grid + visual wrapper

`ui/src/index.css` + `ui/src/pages/PublicSentiment.tsx`.

**Root cause.** The CSS was `grid-template-columns: auto auto 1fr auto`. The component had two consumers:

- `GOPMini` rendered four direct children: label, value, trend, bar.
- `IntensityMini` rendered four direct children: label, value, bar, hint.

Grid places anonymous children in document order, so the `bar` ended up in *different columns* in the two consumers — column 4 in GOP, column 3 in Intensity. The `1fr` column absorbed whatever followed it. Swapping source filters changed which widgets had data, which changed which child was present, which changed which column absorbed the `1fr` expansion. Two components that shared a class looked structurally different with no visual rhyme or reason.

**Structural fix.** The top-level grid is now three columns: `auto auto 1fr`. The third column always hosts a single `.mini-metric-visual` flex wrapper, which in turn holds the component-specific widgets at fixed widths (trend 60px, bar 120px). Adding or removing a widget inside the wrapper *cannot* reshape the outer grid, because the grid only ever sees one child in the visuals column.

Consumers now do:

```tsx
<div className="mini-metric">
  <span className="mini-metric-label">…</span>
  <span className="mini-metric-value">…</span>
  <span className="mini-metric-visual">
    {/* widgets at fixed widths */}
  </span>
</div>
```

On mobile (≤640px) the grid collapses to 2 columns × 2 rows — label + value on top, visual wrapper spans the full card below. Same structure both components, identical visual weight, no filter-dependent reflow.

### `IntensityMini` — no more early `return null`

Same file. When `total === 0` (no sentiment distribution in the filtered window), the component used to return `null`. Its parent `.top-metrics-aux` is `grid-template-columns: 1fr 1fr` — losing one child collapsed the grid to a single column and expanded `GOPMini` to fill, producing a completely different page layout for filters that happened to have no distribution data.

Now renders a placeholder with an em-dash value and a dashed-outline bar slot, keeping the 2-column aux grid stable. Same treatment the `.mini-metric-trend-empty` slot got for GOPMini's missing trend.

### `Home.tsx::TabCard` — hover/focus/active now CSS-only

The button used inline `onMouseEnter` / `onMouseLeave` to toggle border + shadow + transform on hover. Keyboard users who tabbed through saw no focus state; touch users got no feedback on tap. Refactored to a new `.tab-card` class with `:hover`, `:focus-visible`, and `:active` rules. The focus-visible variant adds a 2px accent ring on top of the normal hover shadow so keyboard navigation is clearly signaled. Inline event handlers removed.

### Dead `react-simple-maps` dependency removed

`ui/package.json`. The geo-sentiment feature was deleted earlier in the week but `react-simple-maps` + `@types/react-simple-maps` stayed in the dep list. Zero imports anywhere in `ui/src/`. Removed. `npm install` + `npm run build` clean; bundle size unchanged (vite was already tree-shaking the unused import) but the dep tree is smaller and `npm audit` has two fewer transitive nodes to check.

## Audit findings not acted on (verified false or out of scope)

- **"Orphaned CSS classes"** (`page-header`, `brand-lockup`, `tick-*`, `col-span-4/5/7`, `metric-delta`) — audit agent claimed these had no JSX usage. Verified each has ≥1 live consumer in `App.tsx` / `MetricCard.tsx` / `Sparkline.tsx` / page files. Not removed.
- **"Dead exports"** (`entityLeanAccent`, `entityChipLabel`, `sentimentStats`) — agent said these were unused; grep confirms all three are called from page files. Not removed.
- **`useFetch` race condition** — agent suspected a stale-data flash on rapid filter changes. Reviewed: `cacheKey` always embeds the filter parameters, so a new filter produces a new key and cannot return cached data for a different filter. All consumers guard with `if (loading) return <LoadingCard/>` before rendering `data`. Not a user-visible bug; no change.

## Why

User, after a visible inconsistency between filter states:

> *"you did not fix the the gop party stance bug… there is both a trend and a bar chart, and they are pushing each other errouneously with no data and in different states."*
>
> *"re-audit the UI to ensure consistency, modularity, robust formatting and styling for both mobile and desktop, and ensure that the build is clean and doesnt include bloat."*

The "ensure we are consistent everywhere" ask pushes toward a structural fix, not a placeholder-in-the-empty-slot patch. This entry is the structural fix.

## Validation

- `npm run typecheck` clean.
- `npm run build` succeeds; 629.50 kB / 181.63 kB gzipped.
- Two consumers of `.mini-metric` now render into an identical 3-column grid regardless of filter state, child count, or data availability.

## Follow-ups

Remaining items from both audit passes tracked in `docs/todos/ui-consistency-audit.md` — stat-row unification onto `<TierRow>`, `.bot-section-label` generalization or removal, `formatRelativeDate` / `formatCount` / `formatScore` promotion, Review controls-row wrap, Heatmap cellSize responsive rule, Home.tsx step-card extraction.
