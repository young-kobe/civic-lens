# Walkthrough 065 — Bot Detector page redesign + page-header consistency

Phase 8 of the UI Redesign Plan plus follow-on consistency work touching every data page.

---

## What changed

### `ui/src/pages/BotActivityProfiler.tsx`

- **Reads-as-today headline** added below the existing GlobalTicker. `readsAsToday(data)` template names the automation rate ("elevated at X%" / "sits at X%" / "low at X%"), the top flagged cluster, and the top amplification narrative with its suspected bot volume.
- **Yellow "How to read this page" warning box at the top removed.** Replaced with a `CollapsibleInfo` at the bottom of the page (same visual language as the other data pages). The honest caveat ("flags are leads, not verdicts") moves into that collapsible body; the page now leads with the reads-as-today headline instead of a pre-caveat.
- Filters.timeRange threaded through so the reads-as-today eyebrow ("As of last 90 days") reflects the active filter.

### Page-header consistency — PublicSentiment / Propaganda / Narratives / Bot

All four data pages now render the same three-element header in the same order:

1. `GlobalTicker` — page-specific stat strip.
2. `.reads-as-today` headline — derived sentence + `"As of {window}"` eyebrow.
3. Page content.

Tone page was missing steps 1 and 2 before this pass; added a `buildSentimentTickerItems(data)` + `readsAsToday(data)` pair matching the pattern established by Propaganda (walkthrough 064) and Narratives (walkthrough 062).

### `ui/src/services/timeWindow.ts` — new shared helper

Two pages had their own `WINDOW_LABEL` map (PublicSentiment + Propaganda), a third was about to need one (Narratives for the eyebrow; Bot for filters-through-to-eyebrow). Consolidated into one file:

```ts
export function formatTimeWindow(range: Filters['timeRange']): string;
export function asOfTodayEyebrow(range: Filters['timeRange']): string;  // "As of last 7 days"
```

The eyebrow format is `As of {formatTimeWindow(range)}` — user-directed after an earlier version said "As of today · last 7 days" and read as redundant. All four pages now use `asOfTodayEyebrow(filters.timeRange)` so changing the format once changes it everywhere.

### Source links on propaganda examples

Side fix landed in the same pass. `PropagandaExample` gains an optional `url` field; the aggregator synthesizes it using the same `_build_doc_url` helper `NarrativeAggregator` already uses (news → doc.ident when it's an http URL; reddit → `/r/{sub}/comments/{id}`; x_post → `https://x.com/{handle}/status/{ident}`). `ExampleRow` renders a `"View original ↗"` link in the row header when the URL is present.

Captured as an invariant (`docs/INVARIANTS.md` C1) and a feedback memory: any UI surface that shows a single doc as evidence must outbound-link to the original. Non-negotiable.

### `docs/INVARIANTS.md`

New bullet under C1 Data Fidelity:

> **Source attribution on evidence**: Any UI surface that shows an individual doc as evidence — flagged example, classification sample, supporting doc, narrative citation — MUST link back to the original source (news article URL, X tweet permalink, or Reddit post link). The aggregator is responsible for synthesizing the URL when it isn't stored literally. A doc row without a link is a bug, not a layout choice.

---

## Why these choices

**Why defer the "Amplification by tier" Phase-8 bullet?** The three-way rollup is one entity-grid away from a parity with Tone + Propaganda + Narratives, but `BotAggregator` doesn't currently emit per-entity bot-amplification data. Wiring it up means (1) adding `by_news_outlet` / `by_official` / `by_general_public` lists to `BotOverview` with author-handle + subreddit joins (same pattern as Phase 3b added to Sentiment and Propaganda), (2) an aggregator pass that buckets flagged accounts by the entity they most amplify. That's a proper walkthrough of its own, not a follow-on to a UI pass. Phase-8 delivers the shell the future bullet will drop into.

**Why remove the yellow warning box at the top instead of keeping it there?** Two reasons: (1) it diverged from every other page's top-of-page shape (ticker + reads-as-today). (2) Per durable guidance (walkthrough 062, the "how this page works" pattern), the plan is self-documenting content + a collapsible backup — not an unskippable banner. The caveat is stronger when it sits below the page's own evidence than when it sits above it.

**Why bundle the "As of" format change into Phase 8 rather than a separate pass?** It's the same consistency-across-pages problem. All the affected call sites already exist in Phase 8's diff (the eyebrow strings I'm adding to Bot + touching on Tone), so the rename is one extra replace.

**Why `asOfTodayEyebrow` as a function instead of a constant map?** The eyebrow is a composed string per-page, not a static label. A function is the right abstraction.

**Why keep `formatTimeWindow` as its own export when it's a 5-line function?** Three callers already: Propaganda's `windowLabel`, PublicSentiment's `TopMetrics`, and the `asOfTodayEyebrow` internal. Exporting lets future pages avoid defining their own map.

---

## Verification

```
npm run typecheck   # clean
npm run build       # clean, 3.6s
python -m unittest analysis.tests.test_propaganda_surfaces analysis.tests.test_rich_aggregators   # 13/13 pass
```

Manual dev-mode sanity (`VITE_USE_MOCKS=true`):

- All four data pages now render the same header shape: ticker → reads-as-today → content.
- Eyebrow reads "As of last 7 days" (or whichever window is active). Toggling the time-range filter updates the eyebrow live.
- Propaganda examples now show a "View original ↗" link on each row; modal drill-down rows show it too.

---

## Files touched

- `analysis/src/reporting/aggregators/propaganda.py` — `PropagandaExample.url`; `_fetch_examples` selects `d.ident`; `_build_doc_url` call at construction.
- `ui/src/types.ts` — `PropagandaExample.url?: string | null`.
- `ui/src/pages/BotActivityProfiler.tsx` — reads-as-today + filter threading + yellow banner → `CollapsibleInfo` footer.
- `ui/src/pages/PublicSentiment.tsx` — GlobalTicker + reads-as-today added; `TopMetrics` eyebrow now uses shared helper.
- `ui/src/pages/Propaganda.tsx` — eyebrow uses shared helper; reads-as-today eyebrow threaded through filter.
- `ui/src/pages/Narratives.tsx` — reads-as-today eyebrow threaded through filter.
- `ui/src/services/timeWindow.ts` — new (`formatTimeWindow` + `asOfTodayEyebrow`).
- `ui/src/services/fixtures.ts` — propaganda examples expanded with explicit `url` values.
- `ui/src/index.css` — `.example-row-head` flex-wrap; new `.example-row-link` styling.
- `docs/INVARIANTS.md` — new C1 bullet: "Source attribution on evidence".
- `.claude/...memory/feedback_evidence_source_links.md` — feedback memory.
- `docs/walkthroughs/README.md` — index row for 065.
- `docs/ui-redesign-plan.md` — Phase 8 checkboxes marked done / deferred per bullet.

---

## Follow-ups carried forward

- **Amplification-by-tier grid**: needs `BotAggregator` entity rollups first. Proper walkthrough after the backend aggregator audit lands.
- **whyFlagged sanitation at the detector level**: still UI-side. Backend task.
- **Remove the local `WINDOW_LABEL` lingering in any page that re-adds it**: the shared helper is authoritative now; drop-in replacements only.
- **"As of today · last X" format re-visit**: if a future page wants the literal current date in the eyebrow (for on-call operational views), extend `asOfTodayEyebrow` with an optional timestamp parameter rather than forking the function.
