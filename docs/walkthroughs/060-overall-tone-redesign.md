# Walkthrough 060 — Overall Tone page redesign

Phase 5 of the UI Redesign Plan. The Public Sentiment page gets reframed as **Overall Tone**: the three-way entity grid (Phase 3b backend + Phase 4 card) becomes the hero; the standalone Sentiment-by-Topic card is replaced by a tier-divergence panel; GOP Party Stance moves from top-of-page hero to a cross-cutting measure below the frame.

Tab id stays `sentiment` so URL hashes + cache keys don't break.

---

## What changed

### `ui/src/App.tsx`

Tab label: `"Public Sentiment" → "Overall Tone"`, shortLabel: `"Sentiment" → "Tone"`. Id + behavior unchanged.

### `ui/src/pages/Home.tsx`

TabCard tagline + body copy updated to reflect the three-way framing ("net tone… split three ways: news outlets, verified officials, and the general public"). The "Samples are labeled as samples" principles paragraph reworded from "Public Sentiment" to "Overall Tone" for consistency.

### `ui/src/pages/PublicSentiment.tsx` (rewritten)

685 → 530 lines. Dropped inline `DayOfWeekCard`, `SocialVsNewsCard`, `ComparisonPanel`, `TopicSentimentCard`, and `LABEL_BADGE_STYLES` — now fully dead after the page layout change. New layout top → bottom:

1. **Sampling disclaimer** (kept, invariant rule — never imply universal American sentiment).
2. **GlobalTicker** (kept from walkthrough 053; ticker items include Overall Tone, Scored, Confidence, and Social − News gap).
3. **Reads-as-today headline card** — serif lead paragraph above the three-way frame. Derived client-side via `readsAsToday(data)`: headline net-tone sentence + (when divergence is meaningful) "{tierA} and {tierB} diverge most on {topic}".
4. **Three-way grid** — `News Outlets` / `Verified Officials` / `General Public` columns. Each column shows up to 6 `EntityProfileCard`s from walkthrough 059, sourced from `data.byNewsOutlet` / `byOfficial` / `byGeneralPublic`. Empty-state copy on the Officials column points at walkthrough 056 (timeline ingestion) as the fix path.
5. **Topic Divergence panel** — replaces standalone Sentiment-by-Topic card.
6. **Tone Intensity** — existing `SentimentDistributionCard` (kept; walkthrough 052's reframing stands).
7. **GOP Party Stance** — existing `GOPFavorabilityCard` reframed in its subtitle as a "cross-cutting measure", rendered below the three-way frame (was top-of-page in the old layout).
8. **Methodology** — reworded; new paragraph on the three-way framing that points readers at the YAML registries.

### `ui/src/pages/publicSentiment/TopicDivergencePanel.tsx` (new)

~215 lines. One row per topic: label + doc volume on the left, a −100..+100 axis with up to three colored dots (news/officials/public) positioned by each tier's net tone, a divergence-range number on the right. Sorted by range desc so the most-polarized topics surface first.

Clicking a row opens a modal (reuses common `Modal`) with that topic's classification samples via `ClassificationSampleCard`. Rows with fewer than two tiers of data are filtered out — the panel's whole value proposition is the side-by-side.

### `ui/src/index.css`

~160 lines of component CSS added above the EntityProfileCard block:

- `.topic-divergence-*` — legend, rows grid, axis line with a dashed zero marker, positioned dots. Mobile breakpoint stacks label/range above the axis.
- `.reads-as-today` — accent-bordered card with an eyebrow label + `.lead`-class serif paragraph.
- `.three-way-grid` — three equal columns on desktop, single column below 1024px. `.three-way-column-header` + `.three-way-column-byline` style the column tops.

### Deletions

Two fully-orphaned files removed outright:

- `ui/src/pages/publicSentiment/SentimentOverviewHeader.tsx` (replaced by GlobalTicker in walkthrough 053; had no remaining importer).
- `ui/src/pages/publicSentiment/TopicRow.tsx` (only used by the deleted `TopicSentimentCard`; replaced by `TopicDivergencePanel`).

Plan's Phase 11 lists these for cleanup — doing it now because "delete unused code when obvious" beats "leave dead modules floating for a cleanup phase".

---

## Why these choices

**Why render `readsAsToday` in the page, not inside a component?** The sentence is derived from `overview` + `byTopic` and rendered into a single `<p>`. Extracting a `ReadsAsToday` component would be a one-consumer wrapper around a template string — the trim-boilerplate principle says inline it.

**Why inline the three-way grid instead of exposing `ThreeWayGrid` in `components/common/`?** Same principle: it's one consumer today (this page). Phase 6 (Narratives) and Phase 7 (Propaganda) will likely want their own column framings (narrative lists vs propaganda entity rankings) rather than reusing this exact grid. If three pages land on literally the same grid shape, extract then.

**Why keep `SentimentDistributionCard` (Tone Intensity) rather than collapse it into the new layout?** It's the drill-down surface for Tone Intensity buckets — a different concern from the three-way grid. Walkthrough 052's reframing ("Intense / Measured / Neutral as lead stats") is orthogonal to the Phase 5 entity framing; both have their own place.

**Why a 6-per-column cap on the three-way grid?** Aggregator output is sorted by volume desc with catch-alls last. 6 keeps the grid dense without scroll, and the common case today (20 outlet registry × ~6 populated outlets after ingest filters) means we typically show all of them. Expandable drill-down via the card modal; deep browsing via a future Phase-12 "all entities" page if it's ever needed.

**Why template the Reads-as sentence instead of using an LLM?** An LLM-generated headline would be non-deterministic + cost per request. The template captures the most-useful shape (net tone + top-divergence topic) deterministically and is easy to debug. If the copy feels stilted after real users see it, iterate on the template.

**Why delete the orphaned files now vs. Phase 11?** Walkthrough 053 explicitly left `SentimentOverviewHeader.tsx` on disk pending Phase 11; that was before the user's "delete unused code" guidance landed in `.claude/memory/feedback_trim_boilerplate.md`. Applying the guidance retroactively — these files have zero importers, the plan's replacements are now load-bearing, nothing's A/B testing. Delete.

**Why leave `.topic-row-button` CSS in `index.css`?** Two lines of CSS; fits in a future broader CSS audit. Not worth touching the file for a one-block delete in this pass.

---

## Verification

```
cd ui
npm run typecheck   # clean
npm run build       # clean, 3.3s
```

Visual check still pending in a running dev server — the mock fixtures from walkthrough 058 populate the three-way grid realistically, the Reads-as headline derives against real data, and the divergence panel sorts correctly.

---

## Files touched

- `ui/src/App.tsx` — tab label/shortLabel.
- `ui/src/pages/Home.tsx` — tagline + body + principles copy.
- `ui/src/pages/PublicSentiment.tsx` — major rewrite (−155 lines).
- `ui/src/pages/publicSentiment/TopicDivergencePanel.tsx` — new.
- `ui/src/pages/publicSentiment/SentimentOverviewHeader.tsx` — **deleted**.
- `ui/src/pages/publicSentiment/TopicRow.tsx` — **deleted**.
- `ui/src/index.css` — three new CSS blocks (topic divergence, reads-as-today, three-way grid).
- `docs/walkthroughs/README.md` — index row for 060.
- `docs/ui-redesign-plan.md` — Phase 5 boxes checked; cross-references updated.

---

## Follow-ups carried forward

- **ClassificationSampleCard relocation** — now used by `SentimentDistributionCard`, `EntityProfileCard` (via modal), and `TopicDivergencePanel`. That's 3 consumers — one more and it graduates to `components/common/`. Phase 11 candidate.
- **Entity sparkline on cards** — still deferred (aggregators don't emit per-entity 14-day trends yet). When they do, wire into `EntityProfileCard` props + render inside the stats strip.
- **Orphaned `.topic-row-button` CSS block** — minor cleanup for a future pass.
- **`ThreeWayGrid` extraction** — if Phases 6+7 end up rendering the same 3-col structure, extract to `components/common/` at that point. Today, inline in the page that uses it.
