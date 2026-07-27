# Read-side queries restore full-fidelity dashboard data

**Date:** 2026-07-26
**Layer:** api (cross-link: [ui](../ui/2026-07-26-geometry-restoration.md))
**Todo:** docs/todos/ui-feature-restoration.md

The "restorable from data already stored" backlog is implemented: pure
SELECT additions over `corpus.*`/`analysis.*` — no engine changes, no
recompute, no new tables.

- `SentimentPanelResponse.daily` — per-day net tone/volume within the
  window (computed from rows the panel already fetches).
- `EntityStanceAggregate.byTopic` / `.receivedByTier` — per-topic and
  per-speaker-tier stance breakdowns from `analysis.target_mentions`.
- `NarrativeSummaryModel.firstSeenAt`/`firstSeenDocId` (existing columns,
  now selected) and `.meanConfidence` — mean `narrative_docs.confidence`
  over ALL in-window member docs (claim-match confidence; there is no
  coherent per-doc runs.confidence join for a narrative).
- `BotActivityResponse.coordinationIndex` + `.postingCadence` — the
  retired coordination-index semantics (max single-hour share of
  bot-flagged posting) recomputed as post-processing over the 24-bucket
  hour-of-day histogram.
- `PropagandaOverviewModel.byEntity` (top-20 leaderboard via the three
  doc-to-entity paths: outlet_entity_id, subreddit_entity_id,
  author_profiles.entity_id) and `.byTier` (News/Officials/Public split).
- Dual identifiers: entity-shaped models now emit BOTH `entityId` and
  `entityKey` (movers, entity posts/profile, stance aggregates, bot
  rates, propaganda rows) so cross-page joins are exact.
- `GET /docs/{id}` author join recorded separately in
  2026-07-26-docs-author-join.md.

Contract snapshots re-recorded deliberately for every touched response;
integration tests added per aggregate (confidence floor, is_current, and
window behavior asserted). Full suite: 769 passed, 0 failed.
