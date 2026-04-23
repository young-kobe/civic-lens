# Code audit + dead-code cleanup

Full pass across all four layers to delete unused code, collapse duplicated abstractions, and retire backwards-compat shims. We're MVP — nothing needs to survive a rename.

## Approach

- [ ] One layer at a time: ingestion (Go) → analysis (Python) → api → ui. Each layer gets its own sub-PR and its own audit-trail entry.
- [ ] For each layer: grep for unused exports, dead branches, and orphaned files. Use the `Explore` agent or `Agent` with a dead-code focus.
- [ ] Delete; never `// deprecated` or `_removed`. If it's unreferenced, remove it.

## Known hotspots

### Analysis (Python)

- [ ] `analysis/src/engine/bot.py` — old heuristics documented in replaced walkthroughs may still be in code paths no caller hits. Confirm every branch runs.
- [ ] Confidence-threshold filters: two philosophies in the same codebase (`a.confidence >= ?` gate vs. `COALESCE(a.confidence, 0)` sort). Pick one; document it.
- [ ] Aggregator accumulator duplication — see the aggregator-audit todo for the concrete list.

### UI (TypeScript)

- [ ] `ui/src/services/fixtures.ts` — mock-only; wrapped behind `VITE_USE_MOCKS`. Confirm it isn't leaking into the prod bundle. Consider a `.dev.ts` extension or compile-time guard.
- [ ] Any remaining references to the old `SentimentOverviewHeader` / `ClassificationSampleCard` (moved in walkthrough 062) or `GlobalHeatmap` (deleted in walkthrough 066).
- [ ] `components/charts/` — are all charts still used? At least one file was pruned when the heatmap went; audit the rest.
- [ ] Dead CSS classes in `index.css` — grep each class against the `*.tsx` tree.

### Ingestion (Go)

- [ ] `ingest/internal/` — any package that predates the frontier state machine rework may carry unused types.
- [ ] Seed-file validation: walk `data/seeds.yaml` against the code paths that consume it.

### API

- [ ] `analysis/src/api/routers/` — confirm every router file binds to a live endpoint; no orphaned handlers.
- [ ] Rate-limit decorators: confirm each one is still correct for the endpoint's cost.

## MVP rules (repeat for this pass)

- No `// removed X` or `# deprecated Y` comments.
- No re-exports kept "for compatibility".
- No optional fields marked "pre-refresh fallback" — the snapshot cache rebuilds on every pipeline run.

## Exit criteria

Each layer's audit-trail entry names specifically what was deleted and the LOC delta. No "general cleanup" — concrete receipts.
