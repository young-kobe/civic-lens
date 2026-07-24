# 2026-07-24 — Phase 10: adapt the UI to the strictly-live Phase 9 API contract

The React UI now speaks the Phase 9 strictly-live API (`analysis/src/api/models/*.py`, `/api/v1/*`) directly. Every request goes straight to a live aggregation query — there is no cache layer, no `snapshot-status` list of per-panel timestamps, and no dev-mock fixture system. `ui/src/types.ts` mirrors the pydantic response models field-for-field (camelCase); `ui/src/services/api.ts` calls the new paths and params (`window=24h|7d|30d|90d|all`, movers excepted). Several pre-redesign UI features had no backing data left in the new contract and are removed rather than faked; each removal is called out below and in the code (`Phase 10 adaptation note` comments at the top of the affected files).

## What shipped

- **`types.ts`** rewritten from scratch against the pydantic models: `LeanLabel`, `RangeMeta`, `SampleDoc`, per-panel response shapes (`SentimentPanelResponse`, `BotActivityResponse`, `NarrativesResponse`, `PropagandaOverview`, `OutletProfilesResponse`, `MoversResponse`, `EntityPostsResponse`/`EntityProfileResponse`, `DocumentDetail`, `SnapshotStatusResponse`). Review types (`ReviewQueueItem`, `ReviewSubmission`, `ReviewStats`) stay snake_case — the `/review/*` routes return plain dicts, not a `CamelModel` (see `analysis/src/review/service.py`).
- **`services/api.ts`** rewritten: one fetch function per endpoint, `window` includes `'all'` everywhere except `fetchMovers` (typed `MoversWindow = Exclude<TimeWindow, 'all'>`, matching the router's 400 on `window=all`). Added `fetchDocument`, `fetchEntityPosts`, `fetchEntityProfile`. Dropped the `VITE_USE_MOCKS` fixture system (`services/fixtures.ts` deleted) — the fixtures modeled the old cache shape byte-for-byte and had no path forward under the new contract.
- **`services/transformers.ts` deleted** — the new contract is trustworthy field-for-field, so pages consume `types.ts` shapes directly.
- **`services/topics.ts` deleted** — topic tabs now render straight from `SentimentPanelResponse.byTopic` (whatever strings the backend resolves), not a hardcoded 15-topic taxonomy with icon sets.
- **`services/freshness.ts`**: `latestSnapshotTimestamp` (many per-panel timestamps) replaced by `pipelineRunTimestamp` (the single latest `ops.pipeline_runs` row via `/snapshot-status`).
- **New shared components** (`components/common/`):
  - `LeanLabel.tsx` — the one place a lean renders: `kind=fact` → "Party: `<Value>`", `kind=curated` → "Media lean: `<Value>`", `kind=derived` → "Content leans `<Value>`" always with its evidence (share/confidence/sample count). `chip` variant for cards/lists, `full` variant for modals.
  - `RangeCaption.tsx` — renders every aggregate response's `range` block: sampled vs official-record doc counts, plus a caveat when `modelIds.length > 1` (the range spans a model/prompt version change).
  - `AdmissionBadge.tsx` — "Sampled" vs "Official record" badge, used on every sample card, doc-detail view, citation row, and review-queue item.
  - `DocDetailModal.tsx` — the universal doc drill-down (`GET /docs/{doc_id}`): core fields, admission class, subtype extras, every current analysis result, and citations in/out (citation rows with a resolvable `docId` open a nested `DocDetailModal`).
- **`SampleCard.tsx` replaces `PostCard.tsx`** — the API now only hands the UI a `SampleDocModel` (docId, sourceUrl, snippet, confidence, admissionClass, publishedAt); per-sample AI labels, reasoning, evidence-span highlighting, engagement, and author metadata no longer travel with a sample. `SampleCard` is deliberately thin: admission badge, confidence, snippet, source link, and a "Read full document →" button into `DocDetailModal` where the full analysis now lives.
- **`EntityProfileCard.tsx`, `RankedEntityList.tsx`** rewritten against a minimal `EntityLike` shape (`kind`, `displayName`, optional `LeanLabel`) — the rich pre-redesign `EntityProfile` (blurb, owner, founded, office, party, subreddit subscriber proxy) has no equivalent; panels now only return `display_name`/`kind`/(sometimes) a `LeanLabel`.
- **`ThreeWayGrid.tsx`**: dropped `ThreeWayToolbar`/`matchesLeanFilter`/`LeanFilter` — the lean/party filter pills read a rich per-entity `party`/`lean` field client-side that no longer exists; reintroducing it would mean guessing lean instead of only ever showing the backend's evidence-backed `LeanLabel`.
- **`MoversTicker.tsx`** rewritten for the new `ToneMover`/`FavorabilityMover` shape (both now entity-shaped: `entityKey`/`kind`/`displayName` — the favorability mover is no longer a hardcoded "GOP party stance" row, it's whichever entity had the largest favorability shift).
- **`EntityHubLinks.tsx` deleted** — cross-page entity deep-linking assumed one shared `kind:key` identifier across panels. The new contract keys entities inconsistently (`EntityStanceAggregate.entityId` is numeric, `EntityBotRate.entityKey` is a string natural key), so the join can't be done client-side without guessing.
- **`GlobalFilters.tsx`**: added `'All time'` to the shared window selector (requirement 1). Movers-consuming pages (`DataDesk`, `home/DigestSection`) fall back to a bounded window (`90d`, `7d` respectively) when `'all'` is selected, since `GET /movers` rejects it.
- **Pages rewritten**: `PublicSentiment`, `BotActivityProfiler`, `Narratives`, `Propaganda`, `DataDesk`, `Review`, `Home`/`home/DigestSection`, plus subcomponents `publicSentiment/OutletSignalsPanel`, `propaganda/TechniqueExplorer`, `review/ReviewItemCard`. Deleted (no backing data): `publicSentiment/ToneTrendPanel`, `publicSentiment/TopicTabBar`, `narratives/NarrativeLifecyclePanel`, `bots/CoordinationEvidencePanel`.

## Removed — no equivalent in the Phase 9 contract

- **Daily tone-trend series** (`toneTrend`, `gopTrend`) and the entire "Tone over time" chart/tier-drilldown. `SentimentPanelResponse` has no per-day series at all.
- **GOP favorability rollup** and the polling-vs-online comparison. Favorability is now a per-entity `StanceCounts` inside `entityStances`, not a site-wide GOP number.
- **Per-entity target-tone-by-topic, engagement weighting, received-tone-by-speaker-tier/narrative breakdowns.** `EntityStanceAggregate` carries one `favorability` + one `targetStance` `StanceCounts` pair, no sub-breakdowns.
- **Bot Detector**: coordination index, top amplified domain clusters, posting-cadence heatmap, copy-paste-similarity distribution, per-narrative LLM "why flagged" bullets/hashtags/targets. `BotActivityResponse` carries `behavioralSignals` (per-label stylometric averages) and `accountAgeBuckets` instead.
- **Propaganda**: the entire per-entity flagged-rate leaderboard and three-way entity grid (`by_news_outlet`/`by_official`/`by_general_public` are gone from `PropagandaOverviewModel` — only `byTechnique`/`bySource`/`byParty` remain), and per-example technique breakdowns (`examples` is now a plain `SampleDocModel` list). The technique-evidence modal now shows `TechniqueCount.sampleEvidence` verbatim quotes instead of filtered post cards.
- **Narratives**: `first_seen_*` tracking (entity, tier, account profile), the cross-tier "spreading across groups" panel, and the entity-grouped three-way grid. `NarrativeSummaryModel` carries no first-seen field at all; the page is now a flat ranked list by member-doc count.
- **Data Desk cross-signal matrix**: narratives and propaganda columns dropped (neither panel exposes a per-entity breakdown anymore); the sentiment↔bots join is now by `(kind, displayName)` instead of a shared `kind:key`, since the two panels key entities differently (`entityId` vs `entityKey`).
- **Cross-page entity deep-linking** (`#tab?entity=kind:key`) — no shared entity identifier across panels to join on.
- **Per-task review label pickers** (sentiment/favorability/bot-specific dropdowns) — `raw_response` is an opaque per-task JSON blob under the new contract; the review card now renders it as formatted JSON and asks for a free-text `expected_label` only when marking a row golden.

## Why

Phase 9 (`docs/audit-trail/analysis/2026-07-24-phase9-prewave.md` and siblings) rewrote the API to aggregate `corpus.*`/`analysis.*` live per request instead of serving pre-computed JSON snapshots, and rebuilt every response model from scratch on the Postgres schema rather than translating the old SQLite-era cache shape. Several UI features had been built against per-panel enrichments (entity bios, engagement weighting, per-day trends, first-seen tracking) that existed only in the retired `reporting/aggregators/` snapshot builders and were never carried into the new query layer — Phase 10's job was to consume what the new contract actually provides, honestly, rather than keep rendering placeholders for data that no longer exists.

## Judgment calls

- Entities are grouped into three columns by `corpus.entities.kind` (`outlet` → News, `official`/`collective` → Officials, `subreddit` → Communities) wherever the old News/Officials/Public three-way split still made sense (Sentiment, Bot Detector) — this is the closest honest mapping onto the new flat, kind-tagged entity lists.
- `DocDetailModal`'s `analysisResults[].fields` (task-specific, untyped on the wire) render as a plain key/value list rather than per-task custom renderers — matches the backend's own choice not to type this field.

## Follow-ups

- `analysis/src/api/queries/*` could expose a per-entity join key that survives across panels (numeric `entity_id` everywhere) if the Data Desk cross-signal matrix and cross-page entity linking are wanted back.
- `GET /entity-profile/{entity_id}` (`fetchEntityProfile` in `services/api.ts`) is wired but not yet consumed by any page — no dedicated entity-profile view exists in this phase.
