# 2026-07-11 — Modal hierarchy fix, target attribution, topic deltas, tone-page density

Four UI workstreams from the 2026-07-11 plan (backend halves:
`../analysis/2026-07-11-outbound-targets.md`). The driver was a modal
audit finding a globally inverted hierarchy: every dialog's title
rendered as an 11px uppercase grey eyebrow while its section headers were
16px near-black.

## What shipped

- **Modal shell** (`components/common/Modal.tsx`): the title is a real
  `h2.modal-title` — display serif at text-xl, ink, 3-line clamp for
  claim-sentence titles — with an optional `kicker` line above it that
  takes the accent color. `aria-labelledby` points at the h2. Section
  headers standardized on `h3.card-title` (the Bots amplification modal
  used 11px eyebrows).
- **"Who they're talking about"** (`PublicSentiment.tsx`): public-tier
  entity modals render `item.outbound` via the same ToneBarRows primitive
  as the officials' received-tone tables, with a provenance note.
  `PostCard` renders per-post target chips ("about Trump · negative",
  stance-colored). Types: `OutboundTargets`/`OutboundTargetCell`/
  `SampleTarget`; fixtures cover both.
- **EntitySentimentModal decongestion**: the three look-alike ToneBarRows
  tables get one-line `modal-section-lede` framings (WHAT the mentions
  are about / WHO they come from / WHICH claims they ride on); `n=` type
  10px -> 11px; ellipsized row labels get title tooltips; received-tone
  stat caveats collapse to one line; the classified-posts header carries
  a mono count ("140 of 3,212").
- **NarrativeDetailModal** (`Narratives.tsx`): timeline chart 80px ->
  200px with a readable date axis (`Sparkline` gained `showXAxis`);
  the hand-rolled source-mix dot list replaced by SourceBar's own legend;
  citation-edge copy humanized ("Posts carrying this story link out to
  ... (3 links between them)" instead of "Cites into ... (3 edges)");
  kicker "Narrative".
- **Card-to-modal dedup**: the amplification card shows its top-2
  whyFlagged signals + "+N more in the details" (the modal keeps the full
  list, posts moved below the why); `NarrativeEntityModal` lists compact
  `.narrative-entity-row` rows (title, tone, posts, chevron) instead of
  re-stacking full cards. The now-unreferenced `NarrativeCard` component
  and its CSS were deleted.
- **TopicDivergencePanel**: the single max-spread cell became the three
  pair deltas (News vs Public, News vs Officials, Officials vs Public) —
  tier-color dot pairs + signed mono values, em dash when a tier is
  suppressed (deltas span -200..+200 and format with explicit bounds).
  Max spread remains the sort key.
- **Tone-page density** (`TopMetricsBlock`/`PublicSentiment.tsx`):
  `TierRow` gained a `trail` slot rendering a per-tier daily sparkline
  from `toneTrend` (unfiltered view only — the series is global;
  suppressed days draw as gaps; tier colors match the divergence panel;
  hidden below 900px). `IntensityMini` shows all five bucket shares as a
  dot+pct legend instead of only the largest bucket. Hairline dividers
  between tier rows.

## Why

- The dialog's own name must outrank its section headers; fixing that in
  the one shared shell fixed all nine modals at once.
- The remaining changes convert dead whitespace and duplicate content
  into the information a reader opens each surface for — with every new
  number keeping the existing suppression floors and sample framing.

## Verification

- `npm run typecheck` + `npm run build` clean; backend suite green except
  the six pre-existing live-server test_api cases.
