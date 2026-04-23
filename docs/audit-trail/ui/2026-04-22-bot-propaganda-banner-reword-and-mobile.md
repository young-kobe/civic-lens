# 2026-04-22 — Bot + Propaganda: banner reword, empty grid, and bot mobile spacing

Three small fixes to the reader-facing surface on `/bot-activity` and `/propaganda` that landed together.

## What shipped

### Propaganda — always render the three-way grid

`ui/src/pages/Propaganda.tsx` previously guarded `<ThreeWayEntityGrid>` with a `hasEntityData` check that hid the whole grid when every tier was empty. Removed. The grid now always renders alongside the GlobalTicker and reads-as-today banner, with per-column empty copy from `ThreeWayColumn`. Matches Tone / Narratives / Bot — inconsistency the user flagged: *"didn't we fix this?"* — the grid-always-render change had landed on the other three pages but this one slipped through.

### Propaganda — banner reword (less "X%", more plain English)

`ui/src/pages/Propaganda.tsx::readsAsToday`:

- "News leans on these techniques more than social media (34.5% vs 0.0% flagged)." → *"News is leaning on these techniques more than social media right now."*
- "Loaded language is the most common, appearing in 197% of flagged posts." → *"Loaded language is the technique we're seeing the most."*

Drops the paired-percentage construction and the per-technique share entirely. The specific numbers still live in `PropagandaTopMetrics` and `TechniquesCard` below — the banner's job is framing, not measurement restatement.

The 197% was also fixed on the backend (see `analysis/2026-04-22-propaganda-technique-pct-dedup.md`), but removing the phrasing from the banner is belt-and-suspenders: even a bounded number reads awkwardly next to a clean English sentence.

### Bot — banner reword + noisy-narrative filter

`ui/src/pages/BotActivityProfiler.tsx::readsAsToday`:

- "Suspected automation rate is elevated at 66.9%." → *"A high share of the posts we scanned look automated — roughly 67%."* (three bands: high / mid / low, all phrased as plain observations)
- "Most-amplified cluster: 'www.foxnews.com'." → *"A lot of those suspect posts are pointing back at foxnews.com."* (new `friendlyCluster()` strips the `www.` prefix)
- "35 suspected bot posts are amplifying 'account_age=None days'." — the narrative label was a raw signal string, not a topic. New `isNoiseNarrative()` matches the same `=None|=0|=null|=undefined|=` patterns that `sanitizeWhyFlagged` already uses on the amplification cards, and the banner picks the first *clean* narrative via `narrativeAmplification.find((n) => !isNoiseNarrative(n.narrative))`. If every candidate is noise, the banner simply omits the line — partial framing beats a misleading one.

### Bot — mobile spacing

Two tweaks:

- `BotOverviewMetrics` — removed `mb-4`. The wrapper is a direct child of `.dashboard-grid`, which already supplies row gap; the extra 16px was doubling spacing on mobile where `grid-3` collapses to one column.
- The col-span-7 "Narratives with Suspected Bot Amplification" label band used inline `background + border + borderRadius + padding`. On mobile it stacked under `CoordinationSummary` and read as an orphan panel between two real Cards. Moved the styling to a new `.bot-section-label` class that *keeps* the panel look on desktop (where it pairs visually with Coordination) and drops the frame at `max-width: 1024px` so it reads as a plain section header on mobile.

CSS lives next to `.reads-as-today` in `ui/src/index.css`.

## Why

User feedback:

> *"the bot page in mobile has spacing issues. As of last 7 days / Suspected automation rate is elevated at 66.9%. Most-amplified cluster: 'www.foxnews.com'. 35 suspected bot posts are amplifying 'account_age=None days'. fix this bug. i like the most amplified cluster output, but can we rephrase in less mathmatical term for end user?"*
>
> *"News leans on these techniques more than social media (34.5% vs 0.0% flagged). Loaded language is the most common, appearing in 197% of flagged posts. the propaganda blurb needs this refinement as well."*
>
> *"also the propaganda page does not display the empty three tier columns like the other pages do. didnt we fix this?"*

The cluster rename and percentage-drop match the same direction we took on Tone + Narratives last week: banners frame, cards measure. The noise-narrative filter is the UI compensating for a backend signal that the detector should eventually stop emitting — captured as a follow-up.

## Follow-ups

- The bot detector still emits raw `key=value` strings (`account_age=None days`, `followers=0`) as "narratives" when the underlying field has no value. `sanitizeWhyFlagged` + `isNoiseNarrative` filter them on the UI side; the canonical fix is in the detector's signal generator. Tracked in `docs/todos/bot-propaganda-entity-signals.md`.
