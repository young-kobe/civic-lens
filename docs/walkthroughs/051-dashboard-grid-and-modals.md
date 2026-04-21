# Walkthrough 051: Newspaper-style dashboard grid + pop-out drill-downs

## Goal

Two tightly-related UX changes across every analysis page:

1. **Block / newspaper layout.** Every page previously rendered cards in
   a vertical stack (`flex flex-col`) where every card spanned the full
   content width. On wide desktop viewports this left a lot of empty
   whitespace inside compact cards — the "Net Sentiment" and
   "Total Volume" stats sat at opposite ends of a 1400px row. We
   introduce a 12-col responsive grid so callers can assign spans
   proportional to content density. The result is a newspaper-like
   layout where a tight 3-stat summary sits next to a wider comparison
   panel, and a full-width chart occupies its own row.

2. **Pop-out drill-downs replace inline expansions.** Clicking a
   sentiment distribution segment previously pushed an inline panel of
   sample docs below the card, shifting every subsequent row. Same
   problem on the Topic Row expansions. Both now open a proper modal
   dialog (centered, backdrop, Esc/click-out to close, scroll-locked
   body) so the underlying layout stays put while the reader audits a
   bucket's classifications.

Also cleaned up: GOP Favorability's centered "+X%" hero number that
left the same kind of whitespace as the Sentiment Overview did last
pass — now it's a 2-col compact hero (net favorability on the left,
stance distribution on the right).

## Design decisions

### The 12-col grid

Chose a fixed 12-col base (vs. auto-fit `minmax`) because content
density is the important signal — we want *explicit* pairing, not
whatever the browser decides based on row count. Cards ask for 4, 5,
6, 7, 8, or 12 columns and the grid gives them that share.

Collapses to single-column below 1024px. No middle breakpoint that
breaks into 6-col pairs at tablet width, because the narrative rows
and heatmap matrix both need real width to remain readable — 6-col at
~800px is 400px minus padding, which is below the horizontal minimum
for the 5-col narrative table.

Grid items are `align-items: start` so a tall list doesn't stretch its
neighbor to match.

### The modal

Portal into `document.body` so stacking context doesn't fight the
sticky header, the mobile bottom nav, or any dashboard card with its
own `z-index`. Backdrop uses a light `backdrop-filter: blur(4px)` to
signal "this is a modal layer" without feeling heavy.

Body scroll is locked while open (prevents accidental background
scroll on trackpads and mobile). Focus lands on the close button so
keyboard users have an obvious exit; the rest of the content is
naturally tab-reachable.

Mobile: animation swaps to a bottom-sheet slide-up and the surface
docks to the bottom of the viewport, which is the pattern phone users
expect for a modal sheet.

## Page-by-page layout

### PublicSentiment

```
Row 1  [12] Sampling disclaimer
Row 2  [ 5] SentimentOverviewHeader       [ 7] SocialVsNewsCard
Row 3  [ 5] TopicSentimentCard            [ 7] DayOfWeekCard
Row 4  [12] SentimentDistributionCard
Row 5  [12] GOPFavorabilityCard (compacted hero)
Row 6  [12] MethodTransparencyPanel
```

### Propaganda

```
Row 1  [12] Disclaimer
Row 2  [ 4] Flagged Rate metric   [ 4] Mean Score metric   [ 4] Window metric
Row 3  [ 7] Techniques Used       [ 5] News vs Social Media split
Row 4  [12] Recent Flagged Examples
```

### Bot Activity

```
Row 1  [12] "How to read" disclaimer
Row 2  [12] BotOverviewMetrics (internal 3-col)
Row 3  [ 5] CoordinationSummary    [ 7] Amplification section headline
Row 4  [12] NarrativeAmplificationCard (one per row — expanded state needs room)
Row 5  [ 6] Account Age Distribution   [ 6] Text Similarity Distribution
Row 6  [ 8] Posting Cadence Heatmap   [ 4] Link Domain Concentration
```

### Narratives

```
Row 1  [ 8] "What a narrative is" banner   [ 4] Social Media Narratives explainer
Row 2  [12] News Media Narratives
Row 3  [12] Social · Elected Officials
Row 4  [12] Social · Politically Affiliated
Row 5  [12] Social · General Public
Row 6  [12] Social · Reddit
Row 7  [12] Other Narratives (if present)
```

Only the framing banners pair up on Narratives. The narrative rows
themselves use a 5-column table layout (`1fr 100px 80px 90px 80px`)
whose 1fr claim column needs at least ~300 px to read, so pairing
sections at 6+6 on desktop would collapse the title to a useless
ellipsis. Full-width per section stays readable.

## GOP Favorability internal compaction

Before: big centered `+X.X%` on its own row, then stance distribution
bar on a new row, then trend, then polling.

After: two-column hero (`auto-fit, minmax(200px, 1fr)`): net
favorability stat on the left (eyebrow + big number + doc/platform
count as subtext) and stance distribution on the right, separated by
a left border. Trend + polling stay below.

Matches the treatment applied to `SentimentOverviewHeader` in
walkthrough 050 for visual consistency.

## Component + CSS additions

### `ui/src/components/common/Modal.tsx` (new)

Portal-rendered dialog. Props:
- `isOpen` / `onClose`
- `title` / `subtitle` (aria-labelledby / aria-describedby wired)
- `accentColor` (applied to the left border + title eyebrow so the
  modal reads as belonging to a specific bucket / segment)
- `maxWidth` (defaults to 860px; docs tables fit)

Exported from `components/common/index.ts`.

### `SentimentDistributionCard`

Replaced the inline `SampleDrawer` section with a `Modal` render.
Segment click toggles `openBucket` state; the modal is conditionally
rendered with the matching bucket's samples, segment accent color, and
an honest subtitle ("Showing N of total · sorted by confidence").

### `TopicRow`

Replaced the inline expand-below-card expansion (with chevron arrow
rotation) with a modal click target. The trailing affordance changed
from `▾` to a small "View ›" label in the accent color because it's
now navigating to a dialog rather than toggling an adjacent panel.

### `BotActivityProfiler` / `BehavioralSignalsPanel`

Panel now emits its four cards as direct grid children via a fragment
rather than nesting them in its own `grid-2`. This lets the outer
dashboard-grid assign spans per card based on content width needs
(heatmap at 8, domain list at 4, age + similarity bars at 6 each).
Extracted a `SimilarityBar` helper so the three repeated bars in the
Text Similarity card read as data rather than three copy-pasted
scaffolds.

### CSS (`index.css`)

- `.dashboard-grid` + `.col-span-{4,5,6,7,8,12}` utilities, with a
  single 1024 px breakpoint that collapses every non-12 span to
  single-column.
- `.modal-backdrop`, `.modal-surface`, `.modal-header`, `.modal-body`,
  `.modal-close` — portal modal styles. Two keyframe animations:
  centered fade-rise on desktop, bottom-sheet slide-up on mobile.

## Verification

- `ui/ npm run typecheck` clean.
- `ui/ npm run build` clean (bundle growth ~3 KB JS, ~1 KB CSS —
  mostly the Modal component and grid utilities).
- Python aggregator tests unchanged and pass (no backend changes this
  pass).

## Follow-ups worth tracking

- The SocialVsNewsCard inner layout still uses a centered "Net Score"
  stat on top of each comparison column. Same compaction pattern
  would work but the disparity of space on the inner cards is modest,
  so left alone for now.
- MiniDonut (56 px) used inside TopicRow remains unchanged — no
  meaningful interaction needed at that size.
- Mobile (below 1024 px) collapses every non-12 span to single-column.
  If a middle breakpoint ever feels like a missed opportunity, adding
  a 768–1023 px rule that keeps 6+6 pairs for the compact cards is
  straightforward.
