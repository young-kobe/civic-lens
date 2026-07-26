# Code audit + dead-code cleanup

Full pass across all four layers to delete unused code, collapse duplicated abstractions, and retire backwards-compat shims. We're MVP — nothing needs to survive a rename.

## Approach

- [ ] One layer at a time: ingestion (Go) -> analysis (Python) -> api -> ui. Each layer gets its own sub-PR and its own audit-trail entry.
- [ ] For each layer: grep for unused exports, dead branches, and orphaned files. Use the `Explore` agent or `Agent` with a dead-code focus.
- [ ] Delete; never `// deprecated` or `_removed`. If it's unreferenced, remove it.

## Known hotspots

### Ingestion (Go)

- [ ] `ingest/internal/` — any package that predates the frontier state machine rework may carry unused types.
- [ ] Seed-file validation: walk `data/seeds.yaml` against the code paths that consume it.

### UI (TypeScript)

- [ ] Dead CSS classes in `index.css` — grep each class against the `*.tsx` tree.

### API

- [ ] `analysis/src/api/routers/` — confirm every router file binds to a live endpoint; no orphaned handlers.

## MVP rules (repeat for this pass)

- No `// removed X` or `# deprecated Y` comments.
- No re-exports kept "for compatibility".
- No optional fields marked "pre-refresh fallback" — the cache/serving layer rebuilds on every pipeline run.

## Exit criteria

Each layer's audit-trail entry names specifically what was deleted and the LOC delta. No "general cleanup" — concrete receipts.
