# 2026-04-22 — Modal mobile centering

Modal popups now render in the same vertical position on mobile as on desktop (top-biased with a 5vh gutter + rise-in animation). The bottom-sheet layout that previously pinned modals to the bottom of the mobile viewport was removed.

## What shipped

`ui/src/index.css` — the `@media (max-width: 640px)` block that styled modals was reduced to a tighter backdrop gutter only:

- Removed `align-items: flex-end` on `.modal-backdrop` (was pinning to the bottom).
- Removed the `border-radius: var(--radius-lg) var(--radius-lg) 0 0` override on `.modal-surface` (was flattening the bottom corners for bottom-sheet look).
- Removed the `modal-slide-up` animation (transform from `translateY(100%)` → reinforced the bottom-sheet feel).
- Backdrop padding set to `5vh var(--space-2) var(--space-2)` — same 5vh top as desktop, narrower horizontal gutter so the surface can use the full viewport width.
- Max height relaxed slightly to 90vh (was 92vh in the bottom-sheet variant).

Net effect: mobile modals inherit the desktop anchoring + `modal-rise-in` animation + fully-rounded corners.

## Why

The bottom-sheet variant read as "stuck to the bottom" rather than "centered dialog". User-directed fix: mobile should be first-class with desktop.

## Follow-ups

- Tiny-phone (`max-width: 480px`) block doesn't touch modals today; if specific pages grow very tall tables in a modal (e.g. SupportingDocsTable on Narratives), watch for overflow at that width and add a targeted rule if needed.
