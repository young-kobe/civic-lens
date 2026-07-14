# 2026-07-10 — Give each page a distinct identity; render posts as post cards

Phase 1 of the UI depth overhaul (`docs/todos/ui-depth-overhaul.md`). The four
data pages previously shared one identical skeleton (ticker → headline →
metrics → three-way profile-card grid → collapsible), the app's only chart was
hidden inside the Narratives modal, and cached data (`gopTrend`,
`distributionSamples`, `byDayOfWeek`, narrative `timeline`) was never rendered.
Each page now leads with a signature visualization, sampled posts render as
self-rendered post cards with inline evidence highlighting everywhere, and a
new numbers-forward Data Desk tab joins the nav. UI-only: every new surface
reads data the snapshot cache already served.

## What shipped

- **PostCard** (`components/common/PostCard.tsx`): the one way a sampled post
  renders — x/reddit/news flavors, evidence spans highlighted inline as
  `<mark>` (quoted-below fallback when a span falls outside the preview, so
  evidence is never dropped), visible confidence on every AI label, reasoning
  behind a "Why this label" disclosure, permalink out (invariant C1).
  `PostCardList` wrapper requires a `sampleNote` so samples stay labeled as
  samples. Adapters from `ClassificationSample` / `SupportingDoc` /
  `FlaggedExample` / `PropagandaExample`. Replaces and retires
  `SupportingDocsTable.tsx`, `FlaggedPostList.tsx`, and Propaganda's
  `ExampleRow` (files + CSS deleted).
- **Comprehension layer**: `services/glossary.ts` is the single source for
  reader-facing definitions and plain-language buckets (mean score →
  light/moderate/heavy "technique saturation"; coordination index →
  low/moderate/high). `components/common/DefinitionChip.tsx` renders a
  dotted-underline term whose definition opens on click/tap — the
  touch-visible replacement for title= tooltips. Entity-card stats can carry
  a mini dot-on-axis bar (`EntityStat.bar`) so bare numbers get a visual
  anchor.
- **Deep links** (`services/deepLink.ts`): `#<tab>?<param>=<value>` scheme on
  top of the existing hash routing — `#narratives?open=<id>`,
  `#propaganda?technique=<name>`. `App.tsx` parses the tab segment before
  the `?`; old bare-hash links still work. Tone's `?topic=` search param
  migrated to a hash param with a legacy read fallback. Bot amplification
  cards and the Home digest link into the Narratives modal by id.
- **Page identities**:
  - *Overall Tone*: `publicSentiment/ToneTrendPanel.tsx` renders the daily
    GOP net-favorability series + weekday-rhythm bars;
    `TopicDivergencePanel` promoted to directly under the headline;
    intensity-bar segments click through to `distributionSamples` post
    cards; officials' received-tone tables became dot-on-axis bar rows, and
    each official card surfaces its top received-tone topic as a
    `readsAs` line.
  - *Political Narratives*: `narratives/NarrativeLifecyclePanel.tsx` — top-8
    story rows with inline daily-volume areas colored by first-seen group;
    cross-narrative citations in the modal are clickable jumps.
  - *Propaganda*: `propaganda/TechniqueExplorer.tsx` — select a technique,
    read its flagged posts with that technique's evidence highlighted;
    deep-linkable. All "mean score X / 1" copy reads as saturation levels.
  - *Bot Detector*: `bots/CoordinationEvidencePanel.tsx` — chain-of-evidence
    funnel (scanned → flagged → coordination level → top linked domains),
    replacing the BotOverviewMetrics row that duplicated the ticker.
  - *Home*: `home/DigestSection.tsx` — live 7d digest (tier tone rows, top
    claims with source-mix bars, the previously-unmounted `MoversTicker`,
    propaganda/bot tiles), all deep-linking into their tabs.
- **Data Desk** (new tab `desk`, `pages/DataDesk.tsx`): the numbers-forward
  page — sortable cross-signal entity matrix (client-side join across the
  four snapshots on `kind:key`), full movers board, small multiples, and
  pipeline health (snapshot freshness + human-review agreement).
- **Card de-duplication**: only Tone keeps full profile cards. Propaganda and
  Bots grids use the new `RankedEntityList` leaderboard rows (rank, avatar,
  rate bar); Narratives cards use a new `variant="compact"` (no blurb).
  `ThreeWayColumn` now owns client-side sort toggles and a "Show all (N)"
  expander (`items`/`renderItem(s)`/`sorters` props).
- Dead code removed: `SupportingDocsTable.tsx`, `FlaggedPostList.tsx`,
  `MetricCard.tsx` (unreferenced after the Bots funnel), their CSS blocks,
  and the old `.technique-row*` / `.example-row*` (minus the still-used
  `.example-row-link`) rules. `formatCount()` added to `services/format.ts`.

## Why

- Adversarial UI review (2026-07-10) found the pages structurally
  interchangeable, the best content buried in modals or never rendered,
  definitions trapped in hover tooltips invisible on touch, and post
  evidence rendered as dry table rows despite `full_text`/`evidence_spans`
  being in the payload. Kobe confirmed the card grid was overused and asked
  for a journalistic metrics page.
- Decisions fixed with Kobe: self-rendered post cards (no third-party embed
  scripts), general-reader-first with analytical density one click deeper,
  full-stack phased with Phase 1 restricted to data already in the cache.

## Verification

- `npm run typecheck` and `npm run build` pass. `python -m unittest discover
  analysis/tests`: 382/388 pass; the 6 failures are `test_api.TestAPI`
  integration tests that need a running API server — no backend code changed.

## Follow-ups

- Phase 2 (aggregator enrichment: per-tier daily tone series, engagement +
  author on samples, per-example bot evidence, outlet-profiles wiring) and
  Phase 3 (entity hub cross-links, search) tracked in
  `docs/todos/ui-depth-overhaul.md`.
- Bundle is over rolldown's 500 kB chunk warning (recharts) — consider
  code-splitting the chart panels if it grows further.
