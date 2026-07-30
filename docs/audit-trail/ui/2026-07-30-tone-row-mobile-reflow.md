# 2026-07-30 — Reflow tone/provenance rows on narrow viewports

The shared received-tone rows (`ToneBarRows`, `ReceivedProvenanceBlock` — used by the "How the parties are talked about" panel, the sentiment public-column footer, and the entity modal) now collapse to a two-row layout on narrow viewports instead of painting past their card and getting chopped by `body { overflow-x: clip }`. CSS-only; no TSX changes.

## What shipped

- `index.css`: `@media (max-width: 1024px)` reflow for `.provenance-group-row` (394px fixed minimum — overflowed inside the party panel's two-up grid well above phone width): label + share% on row one, share track + wrapping meta on row two. `@media (max-width: 640px)` reflow for `.tone-bar-row` (344px minimum): label/net/volume on row one, axis spanning row two. Explicit `grid-row`/`grid-column` placement because the visual sits mid-DOM — auto-flow would strand the trailing numbers on a third row. `.provenance-top-meta` wraps on phones.
- `.grid-2`/`.grid-3` responsive tracks switched from bare `1fr` to `minmax(0, 1fr)`, matching `.dashboard-grid` — a bare `1fr` track floors at the item's min-content width, which is what let the oversized card widen past the page rails in the first place.

## Why

- The rows were extracted for the party-tone panel (f58239b) from the entity modal, whose `overflow-y: auto` body had silently contained their horizontal overflow. On the page, a 375px phone gives the card ~285px of content box against a 394px minimum — ~110px of clipped spill that read as broken page formatting.
- The fix follows the established per-block `@media` reflow pattern (`.mini-metric`, `.tier-row`, `.lifecycle-row`).
