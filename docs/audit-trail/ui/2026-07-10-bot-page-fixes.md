# 2026-07-10 — Bot Detector: windowed, evidence modals, no slug leakage

Analysis-layer counterpart:
`docs/audit-trail/analysis/2026-07-10-bot-amplification-real-narratives.md`.

## What shipped

- **Windowing**: the Bots tab takes the global `filters` prop and fetches
  `/bot-activity?window=` like every other tab. Previously it fetched the
  API's 24h default while displaying "Full sample · all collected data,
  not time-windowed" — wrong on both counts. That band, the hidden window
  pills, and `GlobalFilters`' now-dead `windowScoped` escape hatch are all
  gone; the snapshot freshness reads `bot_activity_{window}`.
- **Entity cards open evidence modals**: `BotEntityCard` renders a modal
  (stats + the entity's flagged posts with excerpts and per-post "View
  original ↗" links + a secondary "Visit source ↗") instead of its
  primary click navigating to the external site.
- **Amplification cards/modal**: titles are real narrative names (case
  preserved — no more uppercased slugs); the headline copy says
  "amplifying the same narrative" instead of "pushing the same talking
  point"; Top Hashtags / Targets sections render only when populated; the
  Key Phrases section (which echoed the indicator slug) is deleted; an
  honest empty state explains the section fills when flagged posts
  overlap tracked narratives.
- **Shared `FlaggedPostList`** (`components/common/`): one evidence-list
  renderer used by both the amplification modal and the entity modal.
- **Cross-page cleanups from the same audit**: canonical `sourceLabel()`
  + `formatRelativeDate()` in `services/format.ts` replace three
  hand-rolled source-label builders and two identical date formatters
  (SupportingDocsTable, Propaganda examples, Narratives first-seen) —
  killing the two fallbacks that leaked raw `source_type` enums into
  copy, plus the same leak in the Review admin card/stats. Propaganda's
  zero-doc entity cards now open the entity modal (which has explicit
  empty copy) instead of external-linking. Remaining extraction
  candidates (EntityModalStats/Links, BreakdownTable, confidence chip)
  are logged in `docs/todos/ui-consistency-audit.md`.
