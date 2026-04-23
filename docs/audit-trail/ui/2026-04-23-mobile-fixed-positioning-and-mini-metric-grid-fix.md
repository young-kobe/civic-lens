# 2026-04-23 — Mobile fixed positioning + mini-metric grid layout fix

Follow-up to the earlier mini-metric restructure. Screenshots in `docs/evidence/` showed two distinct classes of bug persisting in production:

1. Bottom nav tabs missing or clipped on the Bot Detector page on iOS.
2. GOP stance sparkline + stance-bar visibly misaligned vs. Tone Intensity's bar + hint — despite both using `.mini-metric`.

Root-cause diagnosis in each case turned up a structural issue that the earlier "add a visual wrapper" pass didn't fix.

## What shipped

### 1. Bottom nav tabs on iOS — body `position: fixed` regression

`ui/src/index.css`, `body` rule.

The `.nav-tabs` has `position: fixed; left: 0; right: 0; bottom: 0` on phones. On Tone it rendered correctly; on Bot (longer page) and Review it either didn't render or showed truncated to 3 of 5 tabs. The cause wasn't a descendant transform (the obvious culprit) — it was **the body's own CSS**:

```css
body {
  background-attachment: fixed;   /* iOS: creates a containing block */
  overflow-x: hidden;              /* iOS: creates a scroll container */
}
```

In combination, iOS Safari's layout engine treats the body as a scroll container and positions `position: fixed` descendants relative to the body's scroll context rather than the visual viewport. On long pages the fixed tabs effectively scroll off-screen; the visible clip width is the body's reported scroll width, which in some layouts is less than the viewport.

Fix:

- Removed `background-attachment: fixed` — the SVG-data-URI paper-grain overlay still renders identically as a non-repeating static pattern. The "fixed" attachment was purely decorative (keeps the noise stationary during scroll); removing it is undetectable on a 140×140 repeating pattern at 7% opacity.
- Replaced `overflow-x: hidden` with `overflow-x: clip`. Both prevent stray wide children from forcing horizontal scroll, but `clip` does NOT create a scroll container — which means `position: fixed` continues to anchor to the visual viewport as specified. Browser support is ~97% (all iOS 16+, modern Chrome/Firefox/Edge); older browsers fall back to allowing horizontal scroll, which individual components (heatmap, tables) already guard against in their own wrappers.

Both changes are documented inline in the rule so whoever next opens the file doesn't think the fixed background was load-bearing.

### 2. Mini-metric visual grid — fully deterministic slot widths

`ui/src/index.css`, `.mini-metric-visual` / `.mini-metric-bar` / `.mini-metric-trend` / `.mini-metric-hint` rules.

The earlier fix moved trend + bar into a `.mini-metric-visual` flex wrapper with `justify-content: flex-end; gap: var(--space-2)`. That kept the widgets inside one grid cell regardless of child count — but it didn't prevent flex sizing quirks from producing different visual widths between GOP (trend + bar) and Intensity (bar + hint). A screenshot from production showed the GOP stance bar visibly narrower than the Intensity bar even though both specified `width: 120px`.

Replaced the flex wrapper with a grid:

```css
.mini-metric-visual {
  display: grid;
  grid-template-columns: 60px 120px auto 1fr;
  align-items: center;
  column-gap: var(--space-2);
}
.mini-metric-trend { grid-column: 1; width: 60px; height: 22px; overflow: hidden; }
.mini-metric-bar   { grid-column: 2; width: 120px; height: 8px; }
.mini-metric-hint  { grid-column: 3; }
/* Column 4 (1fr) absorbs trailing space */
```

Every widget now sits in a fixed column position regardless of which widgets are rendered. GOP's trend is in column 1 and its bar in column 2; Intensity's bar is in column 2 and its hint in column 3 — meaning the two cards align widget-by-widget across the aux grid.

Phone override (`max-width: 640px`) uses a narrower 3-slot grid (`44px 1fr auto`) where the bar expands to fill available width on a narrow viewport. Both cards collapse identically.

### 3. Noisy narrative/indicator filter restored on the UI (transition guard)

`ui/src/pages/BotActivityProfiler.tsx`.

The previous change moved the `=None` / `=0` / `=null` / `=undefined` / trailing-`=` filter into the backend bot detector (`_sanitize_llm_indicators` in `analysis/src/engine/bot.py`) and removed the UI-side `sanitizeWhyFlagged` + `isNoiseNarrative` workarounds. That's the right long-term shape, but the production screenshots confirmed stale `ai_outputs` rows still surface for up to 7 days (until they age out of the snapshot windows) — users were seeing `account_age=None days` in the Bot Detector banner and narrative cards.

Re-added a slim filter: `isNoiseLabel(label)` matches the same noise patterns and is consulted in two places:

- `readsAsToday()` picks the first non-noise narrative for the banner, instead of blindly taking `narrativeAmplification[0]`.
- `NarrativeAmplificationCard` filters noise out of `whyFlagged` AND suppresses the whole card when `narrative.narrative` itself is a noise label.

The filter is documented as a transition-period guard; once the snapshot cache fully rebuilds with only post-fix rows (7-day window, faster if the operator forces a `run.ps1 analyze` now) it becomes a no-op and can be deleted.

### 4. Review controls — mobile-standardized inputs

`ui/src/pages/Review.tsx` + `ui/src/index.css`.

The controls row was rendered with `flex items-center gap-4 flex-wrap` + inline styles: a 140px-fixed reviewer-ID input + 4px/8px padding on selects. On 320-380px viewports this wrapped awkwardly, the inputs didn't meet the 44px tap target, and the Skip button floated to weird positions.

Extracted to a dedicated `.review-controls` class set:

- Desktop: horizontal flex row, skip button right-aligned via `margin-left: auto`.
- Phone (≤640px): stacks every field full-width, labels above inputs, inputs gain `min-height: 44px` + `padding: 10px 12px` for WCAG tap targets, Skip button spans full width.

Also now Review's inputs share the same `.review-controls-input` base class (border, radius, padding, background), eliminating three copies of the same inline-style object.

## Why

User reported both bugs still visible in deployed production screenshots. The mini-metric fix from earlier today was structurally correct (wrapping widgets in a single grid cell) but didn't go far enough — flex sizing produced between-component drift that only a fully-deterministic grid template removes. The tab-bar fix is iOS-specific behavior that doesn't reproduce on macOS Safari or desktop Chrome, making it easy to miss in local dev.

## Validation

- `npm run typecheck` + `npm run build` clean. UI bundle 629.28 kB / 181.56 kB gzipped; CSS 58.29 kB / 10.56 kB gzipped (slight growth from the new `.review-controls` + `.mini-metric-visual` rules).

## Follow-ups

- Once the snapshot cache fully rebuilds (within ~7 days of the next successful `analyze` run against the post-fix bot detector), the `isNoiseLabel` filter in `BotActivityProfiler.tsx` becomes a no-op and can be removed. Tracked under `docs/todos/bot-propaganda-entity-signals.md`.
- If any CSS-reliant page still fails on a specific iOS version, the fallback is to hoist `<Tabs>` out of `.app-container` and render it as a direct child of `<body>` via a portal. Not needed after the body-rule fix but keeping it in mind.
