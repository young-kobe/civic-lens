# Walkthrough 053 — Editorial design tokens + GlobalTicker

Phase 1 of the UI Redesign Plan (`docs/ui-redesign-plan.md`). Foundational pass: typography tokens, a handful of CSS utilities, and a reusable `GlobalTicker` component that replaces the sentiment-only overview strip with one shared stat strip across every data page.

Deliberately small. No page logic changed beyond inserting the ticker at the top of each dashboard grid.

---

## What changed

### Design tokens + typography (`ui/src/index.css`)

- **`--font-lead`**: new token; serif, same family as `--font-display`, intended for standfirst / "reads as today" prose introduced in Phase 5. Declared now so later phases don't have to pick a font at the same time they're moving markup.
- **`.card-title`**: rewritten. Was `text-xs` uppercase small-caps 700. Now `text-lg` serif (`var(--font-display)`), 600, letterspacing-tight, `font-optical-sizing: auto`. Card headings read as headlines instead of shelf labels.
- **`.card-subtitle`**: rewritten as deck-style — serif italic, `text-sm`, `var(--neutral-500)`. Was a tiny grey non-italic blurb; now reads as a standfirst under the headline.
- **`.lead`** (new utility): serif, `text-xl`, relaxed line-height, `var(--neutral-800)`. Reserved for Phase 5's "reads as today" headline cards; declared now so the typography system is coherent when we get there.
- **`.section-divider` / `.section-divider-label`** (new utility): thin horizontal rule with a centered small-caps byline. Used inside a dashboard row to announce a new block of cards (e.g. "By News Outlet" above a three-way grid in Phase 5/6).
- **`.col-rule-left` / `.col-rule-right`** (new utility): hairline vertical border + inside padding on a grid child. Pairs neighboring cards with an editorial column rule without requiring a dedicated element; collapses to no-op below 1024px.

### GlobalTicker (`ui/src/components/common/GlobalTicker.tsx`)

Thin stat strip, ~40 px tall, single row on desktop, wraps on mobile. Replaces `SentimentOverviewHeader` on Public Sentiment and adds a matching overview strip to Narratives / Propaganda / Bot pages (which had none).

Props:

- `items: TickerItem[]` — ordered list of `{ label, value, hint?, tone?, emphasis?, ariaLabel? }`. The caller formats the value string (sign, %, thousands separators) so the component stays presentation-only.
- `refreshed?: string` — timestamp, right-aligned. Prefixed with the existing pulsing `.tick-live` dot.
- `accentColor?: string` — optional left-edge accent; used on Overall Tone to signal net direction (green / red / grey) and on Propaganda / Bot to signal flagged-rate severity.
- `ariaLabel?: string` — strip-level accessible name.

Items render as `<label> <value> <hint>` triplets separated by hairline dividers. `tone` switches the value color to positive / negative / accent / warning / neutral; `emphasis: true` sizes the value at `text-xl` (used for the lead metric).

CSS classes (prefixed `.global-ticker-*`) live in `index.css` beside the existing `.status-strip`.

### Page wiring

- **`pages/PublicSentiment.tsx`**: imports GlobalTicker, drops `SentimentOverviewHeader`. Stats: Overall Tone (%), Scored docs, Confidence, Social − News gap (only when both sides have volume; hint flips to "wide" when `|gap| > 20`). Accent color reflects tone sign. The `SentimentOverviewHeader.tsx` file stays on disk for now — Phase 11 of the plan handles the actual removal after the full page redesign lands.
- **`pages/Propaganda.tsx`**: inserts GlobalTicker above the disclaimer; deletes the three-card `MetricCard` row (flagged rate, mean score, window) since the ticker carries them. Adds a 4th item: top technique + its doc count. Accent color reflects flagged-rate severity.
- **`pages/BotActivityProfiler.tsx`**: inserts GlobalTicker above the disclaimer. Stats: automation rate, coordination index, flagged accounts, confidence. `BotOverviewMetrics` stays — it remains the authoritative detail surface; the ticker just brings the page to parity with the others.
- **`pages/Narratives.tsx`**: inserts GlobalTicker above the disclaimer. Stats: total tracked, new in last 24h, window, top-amplified narrative name + supporting-doc count. Narratives has no overview object, so the helper derives stats from the `NarrativeSummary[]` list directly.

The existing `MetricCard` helper is still used on Bot (and elsewhere); nothing was deleted except the three redundant Propaganda cards.

---

## Why these choices

**Why keep `SentimentOverviewHeader.tsx` around?** The plan (`docs/ui-redesign-plan.md`, Decisions row "existing ambiguous cards" and Phase 11 cleanup) explicitly batches component deletions into Phase 11 after replacements have had a pass of real use. Orphan-imports risk breaking in-flight branches; leaving the file on disk is cheap.

**Why format values in the caller rather than inside the ticker?** Each page formats sentiment / rates / counts with different conventions (signed %, unsigned fixed, raw counts, text labels). Pushing formatting into the component would require a tagged-union of value types and would leak presentation logic into one shared place. The current API lets each page keep its own formatter while reusing the layout / typography.

**Why `emphasis: boolean` instead of a `size` enum?** Only two states matter today: the lead metric gets `text-xl`, everything else gets `text-base`. An enum would overspecify without a real third case on the horizon.

**Why is the timestamp per-page instead of flowing down from a shared context?** Ticker-level refresh timestamps should ultimately come from the snapshot metadata (cache `generated_at`), but that field isn't threaded through the snapshots today. The current `new Date()` fallback is honest about "rendered at": each ticker displays the render time. A follow-up after Phase 3 will wire the real snapshot timestamp through the API types.

**Why `.card-title` turned into a serif headline at `text-lg`?** Editorial pivot is the whole point of the redesign. The prior uppercase small-caps treatment read as a shelf label / tab tag — fine for a flat dashboard, too thin for a "data-rich editorial publication". The card-header's grey chip background is unchanged; the serif title sits on it cleanly at `text-lg`.

**Why not audit the grey card-header background in this pass?** Visual polish to the card chrome (e.g. dropping the grey bar so the serif title floats on the card body) is Phase 9's territory. Phase 1 restricts itself to tokens + ticker so regressions stay diagnosable.

---

## Verification

- `cd ui && npm run typecheck` — clean.
- `cd ui && npm run build` — clean, 3.59s.
- Visual verification left to the user (no automated UI tests exist yet).

---

## Files touched

- `ui/src/index.css` — +`--font-lead` token, rewrites `.card-title` + `.card-subtitle`, adds `.lead`, `.section-divider`, `.col-rule-left`/`.col-rule-right`, `.global-ticker*` block.
- `ui/src/components/common/GlobalTicker.tsx` — new.
- `ui/src/components/common/index.ts` — export `GlobalTicker` + its types.
- `ui/src/pages/PublicSentiment.tsx` — drop `SentimentOverviewHeader` import; add GlobalTicker wiring helper.
- `ui/src/pages/Propaganda.tsx` — add GlobalTicker; remove `MetricCard` import + its 3-card row.
- `ui/src/pages/BotActivityProfiler.tsx` — add GlobalTicker above disclaimer.
- `ui/src/pages/Narratives.tsx` — add GlobalTicker above disclaimer; add `buildNarrativeTickerItems` helper.
- `docs/walkthroughs/README.md` — index row for 053.
- `docs/ui-redesign-plan.md` — check off Phase 1 tasks.

---

## Follow-ups carried into later phases

- Snapshot cache `generated_at` needs to be exposed through API + `PublicSentimentData` / `PropagandaOverview` / bot / narratives responses so the ticker's refresh timestamp is data-driven instead of `new Date()`. Cleanest to bundle with Phase 3 aggregator changes where `EntityProfile` / new accumulators land anyway.
- Phase 5 will use `.lead` + `.section-divider` in the Overall Tone redesign; they were declared now so Phase 5 is typography-free.
- Phase 9 chart polish should audit the card-header grey bar in light of the new serif title.
