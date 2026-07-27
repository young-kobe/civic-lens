# Wave 3: full-fidelity UI restored on live Postgres data

**Date:** 2026-07-27
**Layer:** ui
**Todo:** docs/todos/ui-feature-restoration.md
**Follows:** docs/audit-trail/api/2026-07-27-wave2-tier-restoration.md (per-tier
entity arrays, rich rollups), docs/audit-trail/api/2026-07-27-entity-profile-restoration.md
(Wave 1 foundations), docs/audit-trail/ui/2026-07-26-geometry-restoration.md
(the frontend-only geometry pass this wave upgrades to full fidelity)

`PublicSentiment`, `Narratives`, `Propaganda`, `BotActivityProfiler`, and
`DataDesk` now render the pre-cutover design verbatim, live against the
Wave 1+2 API contract instead of the degraded-panel fallbacks the geometry
pass shipped. Every card, modal, and drill-down reads real data with no
lazy-hydration step: samples arrive already rich (`ClassificationSample`/
`FlaggedExample`/`PropagandaExample`) straight off the panel response.

## What shipped

- **Three-way tier columns.** `ThreeWayGrid`/`ThreeWayColumn`/`ThreeWayToolbar`
  (`ui/src/components/common/ThreeWayGrid.tsx`) render the News/Officials/
  Public split on `Propaganda`, `PublicSentiment`, and `Narratives`, sourced
  from the API's per-tier entity arrays (`byNewsOutlet`/`byOfficial`/
  `byGeneralPublic`), with lean-filter pills over `LeanLabel`.
- **Rich cards with curated profiles/avatars.** `PostCard`/`PostCardList`
  (`ui/src/components/common/PostCard.tsx`) is the one place a sampled post
  renders anywhere on the site: three visual flavors by `source_type`,
  confidence always shown next to the AI label, evidence spans highlighted
  verbatim with a quoted fallback, technique/target chips, and author
  avatar/verified badge when the backend supplied one. `EntityProfileCard`
  + `EntityHubLinks` render the curated entity header (founded/circulation/
  subscriber-count-proxy from migration 0007) across all panels.
- **Two-level drill-downs.** Entity card -> entity modal (sentiment/
  propaganda/bot per-entity breakdowns) -> per-story `DocDetailModal`
  (`GET /docs/{doc_id}`, citations in/out) is wired on every panel that
  carries a `docId`: narrative supporting docs, review-queue rows, sample
  cards, cited docs.
- **Posting-cadence heatmap.** `ui/src/pages/bots/PostingCadenceHeatmap.tsx`
  renders the day-by-hour cadence histogram the Wave 2 bots query added,
  replacing the retired scalar coordination index.
- **Honest not-measured states**, unchanged from the geometry pass and
  reconfirmed still labeled correctly: `identicalTextPairs` (needs bot-engine
  pairwise-similarity recompute, not a join — still blocked, see the todo's
  "Blocked" section), link-domain concentration, the copy-paste proxy
  (`template_score` is per-doc, not a distribution), and the polling-frame
  panels (GOP favorability stays retired per
  `docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md`).

## Integration fixes (cross-cutting, this close-out)

- **Barrel export gap.** `ui/src/components/common/index.ts` re-exported a
  stale `SampleCard`/`SampleCardList` pair from a placeholder file instead of
  the restored `PostCard.tsx`, which left `Narratives`/`Propaganda`/
  `BotActivityProfiler` unable to import `sampleToPostCard`,
  `flaggedExampleToPostCard`, `propagandaExampleToPostCard`. Fixed to
  re-export `PostCard`, `PostCardList`, and the three adapter functions
  directly from `PostCard.tsx`.
- **`SampleCard.tsx` deleted, not restored.** It re-exported `PostCard`/
  `PostCardList` under old names as a compatibility shim, but `PostCard.tsx`
  was restored to the pre-cutover `ClassificationSample`/`PostCardData` API
  the same wave — the shim's names no longer matched any real call site.
  Grepped every remaining `SampleDoc[]` consumer
  (`OutletSignalsPanel.tsx`, `BotActivityProfiler.tsx`): both already carry
  their own local `SampleDoc` adapter (`OutletSampleCard`/
  `sampleDocToPostCard`) rather than depending on the shared component, so
  there were zero real callers of `SampleCard`/`SampleCardList` left. Deleted
  the file and dropped the aliases from the barrel instead of resurrecting a
  second `SampleDoc`-based card implementation nothing calls.
- **`TECHNIQUE_LABEL` constant consolidated.** Three implementer branches
  had independently kept an identical `PropagandaTechniqueName -> string`
  lookup table module-local: `PostCard.tsx`, `pages/Propaganda.tsx`, and
  `pages/propaganda/TechniqueExplorer.tsx`. Moved to
  `ui/src/components/common/constants.ts` and re-exported from the barrel;
  all three now import the one copy. `TECHNIQUE_BLURB` (used only in
  `TechniqueExplorer.tsx`) stayed local — single caller, no drift risk.
- **Dead lazy-hydration hooks pruned.** `services/useDocDetail.ts` and
  `services/useEntityProfile.ts` wrapped `services/lazyHydration.ts`'s
  `createLazyResource` for a per-id hydration pattern the restored
  `PostCard`/`EntityProfileCard` no longer use (samples and profiles now
  arrive inline in the panel response). All three files had zero remaining
  importers; deleted together. `DocDetailModal.tsx`'s own `useFetch`-based
  fetch is unaffected — it never used this hook.
- Confirmed already-pruned from the interim geometry pass:
  `pages/narratives/sourceMix.ts`, `pages/propaganda/PropagandaEntityModal.tsx`,
  `pages/propaganda/entityGroup.ts`.

## Why

Five implementer agents restored pages/components in parallel against the
same barrel file and against a `PostCard.tsx` whose public shape changed
mid-wave (old `SampleDoc`-based signature -> restored
`ClassificationSample`-based signature). The barrel and the compatibility
shim both froze on the pre-change shape, so nothing that imported through
`components/common` compiled until this close-out reconciled them against
what actually landed.

## Verification

- `cd ui && npx tsc --noEmit` — zero errors.
- `cd ui && npm run build` — zero errors (one pre-existing chunk-size
  advisory, not an error).
- `PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest discover analysis/tests`
  — 882 passed, 0 failed (backend untouched this wave; run as a regression
  check only).
- `cd ingest && go test ./... -count=1` — all packages pass (Postgres-gated
  suites skip cleanly without `CIVIC_TEST_POSTGRES_DSN`; ingest was untouched
  this wave).

## Follow-ups

- Owner side-by-side eyeball pass vs `pre-cutover-main` for design parity —
  not automatable, tracked as the remaining open box in
  `docs/todos/ui-feature-restoration.md`.
- Copy-paste similarity distribution and per-sample "why flagged" JSONB
  promotion remain blocked pending a decision (see the todo's "Blocked"
  section) — unchanged by this wave.
