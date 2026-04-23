# UI consistency + mobile audit — follow-up items

Generated from the 2026-04-23 mobile-first UI audit. The P0 items and the most visible P1s landed in `docs/audit-trail/ui/2026-04-23-gop-stance-layout-stability-and-mobile-fixes.md`; the rest are tracked here so none get lost.

## Mobile fit (remaining)

- [ ] **Review controls row wraps poorly 320–480px.** `ui/src/pages/Review.tsx:155–196`. `flex items-center gap-4 flex-wrap` with no flex-basis on the pill cluster leaves the "Only conf ≤" label + select pair on one line when space is tight. The reviewer-ID input is fixed `width: 140px` — ~23% of viewport at 640px. Give the inputs flex-basis / mobile-specific full-width behavior.
- [ ] **Heatmap cellSize responsive.** `ui/src/components/charts/Heatmap.tsx`. The scroll fix landed today aligns labels and cells, but a 12px cell size still forces horizontal scroll at <480px. Consider shrinking to 8–10px cells at `max-width: 640px` so the full 24h matrix fits without scroll.
- [ ] **Select dropdowns in Review** (`Review.tsx:156–179`). Inline `padding: 4px 8px` — feels cramped on phones. Add a mobile-specific rule bumping padding + height.

## Single-source-of-truth refactors

- [ ] **Unify stat-row renderers.** Three implementations doing the same visual job:
  - `Review.tsx::Stat` (ad-hoc div with `.num` class)
  - `BotActivityProfiler.tsx::CoordinationSummary` row (flex with inline borderBottom)
  - `PublicSentiment.tsx::ToneTierRow` (uses the shared `<TierRow>`)
  Migrate the first two onto `<TierRow>`. Extend `<TierRow>`'s API if needed (it already supports label + value + color; CoordinationSummary needs a denser variant).
- [ ] **Generalize `.bot-section-label` or remove it.** Currently a one-off on the Bot page. If the pattern (section label band that sits next to a Card on desktop, flattens on mobile) is worth keeping, promote to a shared `<SectionHeader>` in `components/common/` and use across pages. Otherwise delete and let the bot page use the same inline eyebrow + text pattern as Narratives / Propaganda.
- [ ] **`Home.tsx::TabCard` inline styles.** `ui/src/pages/Home.tsx:17–62`. Hand-rolled button-as-card with `onMouseEnter` / `onMouseLeave` for border + shadow + transform. Extract to `<CardButton>` in `components/common/` or add an `onClick` + hover-shadow variant to `<Card>`. Home is the only consumer today but the pattern will recur.

## Formatter consolidation

- [ ] **Promote `formatRelativeDate` to `services/format.ts`.** Currently local in `Narratives.tsx:68–76`. Usable by any "first seen / last updated" timestamp. Same file should grow:
- [ ] **`formatCount(n)` — wraps `toLocaleString()` with null/NaN guard.** `formatCount(null)` → `"—"`. Eliminates scattered `(value ?? 0).toLocaleString()` calls.
- [ ] **`formatScore(n, decimals=2)` — wraps `toFixed()` with guard.** For propaganda scores, confidence values, and coordination index where the number is a 0-1 score, not a percentage.

## Accessibility follow-ups

- [ ] **Filter pills in Review** — ensure focus styles meet contrast on mobile. Currently rely on browser defaults; document in the component file.
- [ ] **Heatmap cells** — `onMouseEnter` fires only on desktop; mobile taps trigger `onFocus` via keyboard focus but not via tap. Add `onTouchStart` or switch to a `<button>` element with `onClick`.

## Parent-file references

- `docs/audit-trail/ui/2026-04-23-gop-stance-layout-stability-and-mobile-fixes.md` — today's landed fixes.
- `docs/audit-trail/ui/2026-04-22-modal-mobile-centering.md` — earlier mobile pass (bottom-sheet → centered modals).
- `docs/audit-trail/ui/2026-04-22-three-way-grid-primitive.md` — the shared-primitive template this work should follow.
