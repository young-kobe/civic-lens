# UI timeline (pre-2026-07, consolidated)

Condensed record of `ui/src/` (React + Vite + TypeScript) history, consolidated from the retired
`docs/walkthroughs/` linear log (see `docs/todos/walkthrough-consolidation.md`). Chronological by
original walkthrough number; most entries carry no in-file date. Walkthroughs 051-066 form one
continuous initiative — the "UI Redesign Plan" (phases 1-12) — converging on the three-way
News/Officials/Public entity frame the UI still uses today; that arc is called out where it repeatedly
revises its own prior decisions.

## 002 — Frontend API Integration (undated)

Wired the React frontend to the first real FastAPI backend, replacing mock data across Story
Clusters, GOP Favorability, Public Sentiment, Bot Activity Profiler pages with a new `api.ts`/
`transformers.ts` pattern (decouple backend schema from UI types) — still the convention. Added the
Vite dev-proxy to `:8000`.

## 003 — Dashboard UI Implementation (undated)

Full initial redesign: a CSS design-system (typography/color/spacing tokens), common components
(Card, MetricCard, Tabs, ConfidenceBadge, MethodPopover, ExportMenu), chart components (Sparkline,
StackedBar, SentimentBar, TrendStrip, Heatmap), and four tab pages. Established labeling conventions
still enforced today: "sampled" Reddit/social data, calibrated "suspected/likely" bot language,
confidence badges next to every AI prediction.

## 005 — Python Analysis Refactoring (undated, UI slice)

Removed the last remaining mock bot data from the UI once the bot-activity endpoint went live.

## 011 — Dashboard Fixes (undated)

Fixed a type mismatch crashing the GOP Favorability page and a bug where the 24h/7d/30d/90d
time-filter buttons weren't actually threaded through to API calls on three pages.

## 013 — Analysis & UI Implementation (undated, UI slice)

Initial React app (`App.jsx`, `StoriesList.jsx`, `OutletProfile.jsx`) — plain JSX, predating the
TypeScript rewrite that 003/005 established.

## 014 — Dashboard Data & UX Improvements (undated, UI slice)

Layout reordering on Public Sentiment and GOP Favorability: comparisons moved up, a stacked-bar
platform-favorability chart and dynamic trend-chart titles added.

## 017 — Civic Lens Analysis Redesign (undated, UI slice)

Threaded `content_type` through UI query params (`ContentTypeTabs`, badges, contextual labels) to
match the backend's article/social/mixed classification.

## 018 — Analysis Refinement (undated, UI slice)

Replaced inline GOP favorability markup with dedicated `GOPFavorabilityCard`/`GOPPollingComparison`
components.

## 020 — X Integration & Global Heatmap (undated, UI slice)

Added `GlobalHeatmap.tsx`, a sentiment-colored world map page — decommissioned in full, along with
its backing endpoint and aggregator, in walkthrough 066.

## 021 — LLM Reasoning & Sentiment Visual Refactor (undated)

Replaced flat sentiment bars with donut charts, net-score badges, sarcasm indicators, and expandable
reasoning panels once the backend started surfacing LLM reasoning/evidence spans.

## 024 — Sentiment, Caching, and UI Fixes (undated, UI slice)

Fixed content-type item-count labels and a missing `%` suffix on net-sentiment display.

## 028 — Sentiment, Polling, UI Enrichment (undated, UI slice)

Added evidence filtering and source/date attribution in the UI; relabeled sentiment pills to
"POSITIVE TONE" etc. to stop them being confused with GOP-favorability labels.

## 029 — Clustering Removal & LLM Hardening (undated, UI slice)

Deleted `StoryClusters.tsx` and `GOPFavorability.tsx` outright — the latter was already orphaned,
calling a `/api/favorability` endpoint that no longer existed.

## 030 — Audit Remediation, Layers 2-4 (undated, UI slice)

Added sampling-disclaimer banners on Reddit- and X-sourced views, clarified Bot Activity Profiler
legends.

## 031 — UI Terminal-Density Refactor (undated)

Full visual rewrite toward a "Bloomberg terminal" aesthetic on a light background: IBM Plex Sans +
JetBrains Mono numerics, rebuilt neutral/semantic color tokens, collapsed border radii, new
`.num`/`.eyebrow`/`.status-strip` utility classes applied across the App shell and reference pages,
kept stable enough that later components inherited it automatically. A "neo-tech" teal/magenta
palette variant was prototyped and reverted.

## 033 — Narrative Reader Layer (undated, UI slice)

Added the first `Narratives.tsx` page (claim text, first-seen doc, source mix, sparkline, sentiment,
citation count) with the same sampling-disclaimer pattern as other pages — rewritten twice more in
061/062.

## 034 — Review UI (undated, UI slice)

Added the Review tab: task selector, confidence filter, localStorage-based reviewer ID, a stats bar
with traffic-light accuracy coloring, and a single-item review card with verdict/correction/golden-set
flag.

## 035 — Goal Narrowing & Honesty Renames (undated, UI slice)

A political-framing pass across every page plus a new Home landing page explaining the tool and every
tab; renamed the Bot Activity Profiler with a plain-language disclaimer; split Narratives into
News/Social sections. See the analysis timeline's 035 entry for the full rationale behind the
`origin_*` → `first_seen_*` rename this UI work mirrors — it is still the operative naming
convention.

## 036 — Account Tier Classification (undated, UI slice)

Split the Social Media Narratives section into Elected / Affiliated / General Public / Reddit
sub-cards, driven by the new `account_profiles` tier data. This 4-way split was itself dropped in
061 in favor of the 3-way News/Officials/Public frame.

## 043 — Propaganda Surfaces (undated, UI slice)

Added the first Propaganda tab: technique breakdown, News-vs-Social split, evidence-span examples.

## 046 — UI Audit Remediation (2026-04-20)

Closed the audit's UI-layer findings. Removed dead code (`transformBotData`, unused `.grid-4` CSS).
Centralized the palette into `theme.ts` (`SEMANTIC_COLORS`), replacing hardcoded hex literals across
five files. Decomposed oversized pages: `PublicSentiment.tsx` 885→417 lines (5 subcomponents
extracted), `Review.tsx` 470→222 lines, `GlobalHeatmap`'s ~125-line inline `<style>` block moved to
`index.css`. Added `fetchJSON`/`useFetch` with a module-level cache and mounted-ref guard. Unified
retry UX on `refetch()` (no more `window.location.reload()`). First accessibility pass: aria-labels
and roles on charts, confidence meters, sliders.

## 047 — Pre-Deploy Hardening, PR-C (2026-04-21 launch window)

Stripped `?admin=<token>` from the URL via `history.replaceState` after persisting to localStorage,
preventing Referer leakage of the admin token; wired `npm audit` into CI.

## 050 — Sentiment DoW + Drill-down + Mobile Fit (undated)

Added a per-intensity sample drill-down (`SampleDrawer` reusing `ClassificationSampleCard`) and
compacted the sentiment overview header to a 3-stat row. Cross-cutting mobile fixes: Narratives'
fixed 5-column grid, Bot Heatmap's 24×7 grid confined to its own scrollable card, filter-bar
`flex-wrap` on phones, a `body { overflow-x: hidden }` safety net, and a mobile/desktop tab-label
mismatch ("Claims" vs. "Narratives") reconciled.

## 051 — Dashboard Grid + Pop-out Drill-downs (undated)

Introduced the 12-col responsive `.dashboard-grid` (spans 4/5/6/7/8/12, single-column below 1024px)
applied across all four data pages, replacing full-width vertical card stacks. Added a
portal-rendered `Modal` component (backdrop blur, scroll lock, Esc/click-out, mobile bottom-sheet)
that later became the shared drill-down mechanism for the whole UI.

## 052 — Label Renames + Intensity Reframe (undated, UI slice)

Pure label renames (Net Sentiment → Overall Tone, GOP Favorability → GOP Party Stance, etc.) with
backend field names left stable. Reframed the Sentiment Distribution card around
"Intense/Measured/Neutral" with a templated "Reads as: ..." sentence — the precursor to the
"reads-as-today" headline pattern used across every data page from walkthrough 060 on.

## 053 — Editorial Tokens + GlobalTicker (undated) — Phase 1 of the UI Redesign Plan

Added editorial typography tokens (`--font-lead`, serif `.card-title`/`.card-subtitle`) and a new
shared `GlobalTicker` component, replacing the old `SentimentOverviewHeader` on Sentiment and adding
ticker strips to Narratives/Propaganda/Bot for the first time. Deleting the now-orphaned
`SentimentOverviewHeader.tsx` was deferred to a planned "Phase 11 cleanup" — that plan was overridden
and the file was deleted early, in walkthrough 060.

## 058 — Narrative + Propaganda Entity Routing + UI Types (undated, UI slice) — Phase 3b

Mirrored the backend's new entity-routing fields into `types.ts` (`EntityProfile`,
`EntitySentimentItem`, `PropagandaEntityItem`, all optional for backward compatibility) and populated
dev-mode `fixtures.ts` so the three-way frame could be built against representative data before real
snapshots existed.

## 059 — EntityProfileCard Component (undated) — Phase 4

New reusable `EntityProfileCard.tsx`: lean-colored border, serif display name, italic blurb, mono
stats strip, self-contained detail modal. New `leanClass()` helper in `theme.ts` derives `left/
center/right/mixed/neutral` from party or lean/tilt data. A hatched-border CSS treatment was used
for "mixed" lean specifically to avoid reading as a severity/warning color. Deferred a per-entity
14-day sparkline (aggregators didn't yet emit per-entity daily timelines) — still pending.

## 060 — Overall Tone Page Redesign (undated) — Phase 5

Renamed the "Public Sentiment" tab to "Overall Tone" (tab id kept as `sentiment` so URL hashes and
cache keys didn't break). Rewrote `PublicSentiment.tsx` around a templated "reads-as-today" headline
and a three-way grid of `EntityProfileCard`s (News/Officials/Public), plus a new
`TopicDivergencePanel`. GOP Party Stance demoted to a "cross-cutting measure" below the frame.
**Deleted outright** `SentimentOverviewHeader.tsx` and `TopicRow.tsx` — reversing 053's plan to defer
those deletions to a later cleanup phase, once "delete unused code when obvious" guidance landed.

## 061 — Political Narratives Page Redesign (undated) — Phase 6

Rewrote `Narratives.tsx` with the same three-way tier treatment as Overall Tone, adding a
`CrossTierPanel` (narratives flagged `cross_tier=true`) and a conditional `AmplificationPanel`
(propaganda_score ≥ 0.4 or bot_pushed_fraction ≥ 0.3). **Dropped** the old 4-way elected/affiliated/
general-public/Reddit grouping from 036 in favor of `first_seen_tier_group`'s 3-way buckets. Explicitly
declined to add any directed-graph/propagation-direction visual for cross-tier narratives — correctly
judged as fabricated signal, since only tier membership (not adoption order) is known; consistent with
035's scoping decision against causal-propagation claims. This narrative-per-card layout was itself
revised one walkthrough later, in 062.

## 062 — Shared Primitives + Narratives Entity Grid (undated)

Extracted shared primitives to `components/common/`: `ClassificationSampleCard` (moved from
`pages/publicSentiment/`), `CollapsibleInfo` (replacing four inline `<details>` blocks), and
`TopMetricsBlock`/`TierRow` (used by both Tone and Propaganda headers). Restructured
`Narratives.tsx` again: the three-way grid now keys one `EntityProfileCard` per first-seen entity
(via `groupNarrativesByEntity`) rather than one card per narrative, with 061's narrative-level cards
becoming the modal body — explicitly framed as closing out the last unfinished piece of Phase 6.

## 063 — Modal Back Nav + Supporting Docs Table (undated)

Added `onBack`/`backLabel` props to the shared `Modal` (a back arrow pops one nesting level instead
of dismissing entirely), enabling the narrative detail modal to return to the entity modal without
tearing down the parent. Added a shared `SupportingDocsTable` component (headline/source/when/tone/
reasoning/external link) with a `classificationSampleToSupportingDoc` adapter, reused by both the
Narratives detail modal and the Tone page's entity modal. This is the UI half of the C1 invariant
(every per-doc evidence surface must outbound-link to its original source).

## 064 — Propaganda Page Redesign (undated) — Phase 7

Brought Propaganda to parity with Tone/Narratives: GlobalTicker, reads-as-today headline, an
entity-drill-down `PropagandaEntityModal` filtering the examples list client-side, and a shared
`ExampleRow`. Deliberately kept the entity-card grid rather than switching to a ranked bar chart, for
visual consistency across pages.

## 065 — Bot Detector Redesign + Page-Header Consistency (undated) — Phase 8

Added a reads-as-today headline to the Bot page; removed its yellow "how to read this page" warning
banner in favor of a bottom `CollapsibleInfo` (demoted from banner to backup reference). Standardized
all four data pages on the same header order: ticker → reads-as-today → content, via a new shared
`timeWindow.ts` helper. Deferred an "Amplification by tier" section pending a backend rollup — closed
in 066.

## 066 — Movers Ticker + Bot Entity Rollups + Geo Removal (undated) — Phase 12 close-out

Added a `MoversTicker` primitive (60s CSS marquee, pauses on hover, disabled under
`prefers-reduced-motion`) on Overall Tone and Political Narratives, surfacing the biggest window-over-
window movers in tone and GOP favorability. Closed 065's deferred item with a three-way
`BotThreeWayGrid`/`BotEntityCard` (per-entity bot-rate thresholds: red >10%, amber >3%). **Fully
removed** `GlobalHeatmap.tsx` and every related theme token/CSS/test as part of the geo-sentiment
stack decommission — see the analysis timeline's 066 entry for the rationale.
