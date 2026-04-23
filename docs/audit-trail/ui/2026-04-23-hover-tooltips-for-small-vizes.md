# 2026-04-23 — Hover tooltips + sample-size context on small visualizations

Added native `title` tooltips to the handful of small bars + trend chips that previously had only a generic `aria-label` (or nothing at all). Each tooltip names what the widget represents, gives the numeric breakdown, and includes the underlying sample size so a reader can tell at a glance whether a number is load-bearing or comes from a sparse bucket.

## What shipped

All changes use the native `title` attribute — no popover library, no custom JS, no extra DOM. Tooltips show on hover (desktop) and long-press (mobile). The attribute degrades to invisible text, so none of the additions affect layout density.

### Mini-metrics (PublicSentiment top banner)

`ui/src/pages/PublicSentiment.tsx::GOPMini` and `IntensityMini`:

- **Card-level title** on the wrapping `.mini-metric`: one-sentence summary of what the card shows + sample size. "Net GOP favorability: +3.6% across 1,284 sampled posts."
- **Trend sparkline title**: "Daily net GOP favorability over the last 7 days in this filter. Sample: 1,284 posts." — or, when no trend data, the existing "not enough points" message.
- **Stance bar title**: percentage breakdown with sample size. "GOP stance distribution across 1,284 sampled posts: 18% favorable · 67% neutral · 15% unfavorable."
- **Intensity bar title**: 5-bucket breakdown for IntensityMini.
- **"of posts" hint** gets its own title explaining which bucket it refers to.

### MoversTicker

`ui/src/components/common/MoversTicker.tsx::EntityPill` + `FavorabilityPill`:

Each pill now tooltips as "[label]: net tone moved from −3.2 → +0.9 (+4.1 pts) vs. the previous window. Sample: 412 posts now, 388 before." — both endpoints named, delta restated, volume on each side shown so the reader can spot a "big mover" that's actually noise from a small sample.

### Narratives source bar

`ui/src/pages/Narratives.tsx::SourceBar`:

- Wrapper `title` gives a one-line summary of the entire mix.
- Each segment keeps its per-source `title` but now expresses the count as both absolute number AND percentage of total: "News: 27 of 45 docs (60%)."

### Bot similarity bar

`ui/src/pages/BotActivityProfiler.tsx::SimilarityBar`:

Each band (high / medium / low) tooltips its interpretive context: "High similarity (>80%): 12% of pairwise text comparisons fall in this similarity band. Natural discourse typically clusters in the low band (<50%); values above 80% indicate near-duplicate posts."

## Why

User feedback: *"can we include hover tip legends for the small bar / trend graphs that dont have them? to make it more clear what they represent/ sample size etc. keep it small to not crowd the page."*

Two design constraints pulled in the same direction:

1. **Keep the page uncluttered** — the mini-metric strip and Movers ticker are dense-by-design; adding visible legends would break the "at-a-glance" read.
2. **Make every number defensible** — the project's "never fabricate" invariant extends to "never present a number without an easy way to see its sample size."

Native `title` hits both: zero footprint when idle, accessible via hover + long-press + screen reader, universally supported.

## Validation

- `npm run typecheck` + `npm run build` clean; bundle 629.63 kB / 181.77 kB gzipped.
- No accessibility regressions: `aria-label` stays on widgets that previously had one (screen readers still announce the same content); `title` is additive.

## Follow-ups

None for this change. If tooltips later need richer layout (images, formatted lists), wrapping individual tooltips in a shared `<Tooltip>` component is a drop-in migration — the consumers already pass plain strings.
