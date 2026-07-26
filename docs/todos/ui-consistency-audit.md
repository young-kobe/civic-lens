# UI consistency + mobile audit — follow-up items

Originally generated from a 2026-04-23 mobile-first audit, against a UI tree that
predates the Phase 9/10 API-contract rewrite. Most named components from that
audit (`Heatmap.tsx`, `CoordinationSummary`, `Review.tsx::Stat`,
`PublicSentiment.tsx::ToneTierRow`, the old `Home.tsx::TabCard` inline-hover
styling) no longer exist or were already resolved by the rewrite — `<TierRow>`
(`components/common/TopMetricsBlock.tsx`) is now the one stat-row renderer,
used by `pages/home/DigestSection.tsx` and `PublicSentiment.tsx`, and
`Home.tsx::TabCard` already moved hover/focus/active styling into the
`.tab-card` CSS class. This file keeps only the items that verifiably still
apply against the current `ui/src` tree.

**A fresh mobile-fit audit is needed post-Phase-10** — the page layouts
(`Review.tsx`, `BotActivityProfiler.tsx`, `PublicSentiment.tsx`) were
substantially rewritten and the 2026-04-23 findings' line numbers and markup
no longer match. Re-run the audit before trusting any specific breakpoint claim.

## Formatter consolidation

- [x] **`formatCount(n)`** — landed in `services/format.ts`; wraps
      `toLocaleString()` with a null/NaN guard (`formatCount(null)` -> `"—"`).
      Used across `RangeCaption`, `DigestSection`, `OutletSignalsPanel`,
      `DataDesk`.
- [ ] **`formatScore(n, decimals=2)`** — still open. Wrap `toFixed()` with a
      null guard for 0-1 scores (not percentages): current hand-rolled call
      sites are `Propaganda.tsx` (mean score), `propaganda/TechniqueExplorer.tsx`
      (per-party mean score), `review/ReviewItemCard.tsx` (confidence), and
      `components/common/DocDetailModal.tsx` (confidence).

## Duplication clusters

- [ ] **`EntityProfileCard`/`EntityHeader` (`components/common/EntityProfileCard.tsx`)
      extraction is done for `PublicSentiment.tsx` and `BotActivityProfiler.tsx`.**
      `Narratives.tsx::NarrativeDetailModal` still hand-rolls its own
      eyebrow/`.metric-value` stat grid, but its stats describe the
      narrative (member posts, net tone, citations), not an entity profile —
      confirm whether it's worth converging on the same primitive or is
      genuinely a different shape before extracting further.
- [ ] The three near-identical received-tone breakdown tables
      (byTopic/bySpeakerTier/byNarrative) that motivated a shared
      `BreakdownTable` no longer exist as described — `PublicSentiment.tsx`
      now renders a single `byTopic` table. Re-check whether any remaining
      table duplication (e.g. `DataDesk.tsx`'s desk-matrix vs. movers table)
      is close enough in shape to warrant a shared component, or drop this
      item.

## Accessibility follow-ups

- [ ] **`filter-pill` focus styles** (`components/common/GlobalFilters.tsx`) —
      ensure focus styles meet contrast on mobile; currently rely on browser
      defaults.
- [ ] **Review controls** (`pages/Review.tsx`, `.review-controls-input`) — the
      task/confidence `<select>` elements and reviewer-ID `<input>` should get
      a mobile-specific accessibility pass (tap target size, label
      association) as part of the fresh mobile audit above.

## Parent-file references

- `docs/audit-trail/ui/2026-04-23-gop-stance-layout-stability-and-mobile-fixes.md` — the original landed fixes this audit followed up on.
