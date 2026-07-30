# 2026-07-30 — Propaganda centerpiece: density constellation replaces the technique bar list

The Propaganda page's col-span-7 card is now a density constellation: a hand-rolled SVG beeswarm where every dot is one flagged post from the `examplesByEntity` pool, positioned by technique density (`overallScore`, 0–1) and colored by speaker tier (`--tier-news` / `--tier-officials` / `--tier-public`). The six techniques shrink from a ~400px bar list to a compact chip row that doubles as legend and filter. Zero backend change — the payload already carried everything.

## What shipped

- `pages/propaganda/DensityConstellation.tsx`: deterministic layout (`layoutSwarm`, a pure exported function — 50 fixed bins, docId-ordered stacking alternating above/below the baseline, no randomness so the same payload always paints the same picture; clamped bins render a `+N` mono annotation, never a silent drop). Hover/keyboard tooltip (`.chart-tooltip`) with source, density, techniques, snippet; one tab stop with arrow-key dot walking and `aria-activedescendant`; per-dot `aria-label`. Tier per dot resolves through the `byNewsOutlet`/`byOfficial`/`byGeneralPublic` key space, falling back on the doc's `source_type`.
- `pages/propaganda/TechniqueExplorer.tsx` rewritten: technique chips (label + count + % of flagged) toggle a filter that dims non-matching dots and round-trip the existing `?technique=` deep link; the selected chip offers "Read evidence quotes", which opens the unchanged verbatim-evidence modal. `ByPartySection` stays as the card footer (short, honest, orthogonal). Card note carries the sampling caveat: up to 500 flagged posts, capped per speaker — a sample, not the full corpus.
- `index.css`: `.technique-chip*` and `.density-*` classes in the square-cornered hairline register; mobile block (200px swarm, smaller dots, reduced ticks). The dead `.technique-explorer-row*` bar-list CSS is removed.
- Tier trio validated with the dataviz palette checker (CVD separation passes; the sub-3:1 contrast warning is relieved by the always-on text legend + tooltips).

## Why

- The bar list spent ~400px on ~20 numbers, and its two halves (technique counts, by-party rates) were unrelated measures sharing one card. The constellation shows the distribution the page is actually about — how saturated flagged posts are and who wrote them — at the same size.
- SVG over recharts: recharts has no beeswarm layout, so the layout math is custom either way; pure DOM/SVG is house precedent (`PostingCadenceHeatmap`).
