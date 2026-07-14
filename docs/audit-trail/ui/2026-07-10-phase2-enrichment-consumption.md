# 2026-07-10 — Render engagement, authors, bot evidence, and the per-group trend

UI consumption of the Phase 2 aggregator enrichment (analysis entry:
`../analysis/2026-07-10-sample-enrichment-tonetrend.md`). Every new field
is optional in `types.ts` — a stale cached snapshot renders exactly the
Phase 1 UI.

## What shipped

- **PostCard adapters** (`components/common/PostCard.tsx`) pass through the
  new sample fields: X cards show the author line (display name, stored
  avatar preferred over the unavatar fallback) and the engagement row
  ("1,043 likes · 210 reposts · ...", labeled a reach proxy); reddit post
  cards show points/comments; bot-flavor cards show flag confidence,
  indicator chips (with the transition noise filter mirroring
  `isNoiseLabel`), and reasoning behind the existing disclosure.
- **ToneTrendPanel** (`pages/publicSentiment/ToneTrendPanel.tsx`): the
  per-group daily series (news / officials / public, same colors as the
  divergence panel) is now the primary chart, with suppressed low-sample
  days drawn as line gaps (`connectNulls=false`) and per-day volumes in the
  tooltip; the GOP series moved behind a "By group / Toward GOP" toggle.
  Pre-2a snapshots render the GOP chart alone, as before.
- **OutletSignalsPanel** (`pages/publicSentiment/OutletSignalsPanel.tsx`) on
  the Tone page: per-domain net tone x flagged share from
  `GET /outlet-profiles` (`fetchOutletProfiles` in `services/api.ts`), with
  the bots-included disclaimer rendered as the card note.
- `types.ts`: `SampleEngagement`, `SampleAuthor`, `ToneTrendPoint`,
  `OutletProfileItem(s)`; `FlaggedExample` evidence fields. `fixtures.ts`
  covers all of them (mock toneTrend with suppressed officials days,
  enriched samples, bot evidence, `mockOutletProfiles`).

## Why

- Phase 1 built the display surfaces (PostCard rows, trend panel, funnel)
  shaped for this data; Phase 2 filled them. See
  `docs/todos/ui-depth-overhaul.md`.

## Follow-ups

- Phase 3: entity hub link rows, client-side search, divergence-row topic
  self-links.
