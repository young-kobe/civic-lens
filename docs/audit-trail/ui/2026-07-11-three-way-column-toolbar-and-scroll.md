# 2026-07-11 — Three-way columns: top toolbar, internal scroll, filled public tier

The shared three-way entity frame (News / Officials / Public) gained
cross-column controls and a stable geometry. The per-column sort toggle moved
from a footer control below the cards up into the column header. Past ten
cards a column now scrolls inside itself instead of expanding the page, so a
long tier can no longer stretch the whole layout. A new page-level toolbar
sits above the grid with a lean/party filter and an entity search. On the
Overall Tone page the officials column defaults to engagement-weighted order,
and the public column — which usually carries a single pooled "Other X users"
card — is filled with a "who the public is talking about" target-tone panel.

## What shipped

- **`ui/src/components/common/ThreeWayGrid.tsx`** (shared, all pages):
  - Sort toggle relocated from `.three-way-column-controls` (footer) to a new
    `.three-way-column-head` flex row in the header.
  - `DEFAULT_COLLAPSED_COUNT` 12→10; the "Show all" page-expander replaced by a
    `.three-way-column-scroll` region (`max-height: min(78vh,900px)`,
    `overflow-y:auto`). The full payload always renders; only the visible band
    is bounded. Removes the `expanded` state entirely.
  - New `ThreeWayToolbar` (lean/party filter pills + search input; renders null
    when neither is wired) and `matchesLeanFilter(profile, filter)` — unifies
    outlet/subreddit `lean` and official/account `party` through `leanClass`
    (`theme.ts`), so "Left" catches left outlets and Democratic officials alike.
  - New `defaultSortIdx` and `footer` props on `ThreeWayColumn`.
- **`ui/src/pages/PublicSentiment.tsx`** (`SentimentThreeWayGrid`):
  - Renders `ThreeWayToolbar` with lean filter + "Search officials by name…".
  - Filters all three columns by lean; officials additionally by search.
  - Officials use `OFFICIAL_SORTERS` (engagement first, then posts/tone/name).
  - Public column `footer` renders the highest-volume public bucket's outbound
    targets via the existing `ToneBarRows`.
- **`ui/src/index.css`**: `.three-way-toolbar*`, `.three-way-column-head`,
  `.three-way-column-headings`, `.three-way-column-scroll`,
  `.three-way-column-targets*`.
- **`ui/src/types.ts`**: `EntitySentimentItem.engagementTotal?` (see the
  analysis entry `2026-07-11-official-engagement-total.md`).

## Why

- The 2026-07-11 dashboard review flagged the three-way columns as the worst
  offenders: a short tier trailed dead space (fixed structurally by the
  density pass), the sort control was buried at the bottom, there was no way to
  narrow by side, and the public tier read as a lone card beside two full
  columns. Bounding overflow to an internal scroll keeps the three-column
  geometry stable regardless of tier size.

## Follow-ups

- Lean filter, officials search, and the public-target footer are wired on the
  Overall Tone page only. The Political Narratives and Propaganda pages inherit
  the sort-to-top + internal-scroll changes for free; wiring their toolbars and
  an analogous "targets" footer is a follow-up.
- Engagement-weighted official order only takes effect after a snapshot rebuild
  populates `engagementTotal`; until then the toggle is a no-op over the
  backend's volume order (graceful, not broken).
