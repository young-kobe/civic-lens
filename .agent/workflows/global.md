---
description: Global rules
---

# Civic Lens Architecture

## Overview

Civic Lens measures **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** that clusters recurring claims and a **partial citation overlay** between owned sources. The goal is scoped to what the data supports — it is not a causal propagation engine. See `docs/walkthroughs/035-goal-narrowing-and-renames.md`.

## Stack

| Layer | Technology | Location |
|-------|------------|----------|
| Ingestion | Go 1.22+ | `ingest/` |
| Analysis | Python 3.10+ | `analysis/` |
| API | FastAPI | `analysis/src/api/` |
| Frontend | React + Vite + TypeScript | `ui/` |
| Database | SQLite | `data/civic_lens.db` |
| Cache | JSON snapshots | `data/cache/` |

## Data Flow

```
RSS/Reddit -> Go Crawler -> SQLite -> Python ETL -> AI Analysis -> Cache -> FastAPI -> React UI
```

## Common Commands

```bash
# Build Go ingestion binary
./run.sh build

# Run web crawler (news + Reddit)
./run.sh crawl

# Run analysis pipeline (ETL + AI + caching)
./run.sh analyze

# Start API + UI for development
./run.sh dev
```

## Layer Boundaries

1. **Ingestion (Go)**: Fetches and stores raw content. Outputs to `data/civic_lens.db` and `data/raw/`.
2. **Analysis (Python)**: Reads from DB, runs AI analysis, writes to `data/cache/`.
3. **API (FastAPI)**: Serves cached JSON. Stateless, reads from `data/cache/`.
4. **UI (React)**: Consumes API endpoints. No direct DB access.

## Key Files

- `run.sh` - Main command runner
- `data/seeds.yaml` - RSS feeds, Reddit subreddits, API config
- `data/civic_lens.db` - SQLite database
- `data/cache/*.json` - Pre-computed analysis snapshots
- `docs/todos/` - Checklists of planned work (see below)
- `docs/audit-trail/` - Permanent record of what shipped, bucketed by layer (see below)
- `docs/walkthroughs/` - Legacy linear log; being consolidated — do not add to it

## Plan -> audit-trail workflow (required)

Non-trivial work follows three steps:

1. **Plan.** Create a checklist at `docs/todos/<initiative>.md`. One file per initiative. Each item concrete enough to tick off.
2. **Execute.** As boxes tick, the code lands.
3. **Record.** In the same PR, add a dated entry under the affected layer(s) at `docs/audit-trail/<layer>/YYYY-MM-DD-short-slug.md`. Buckets: `ingestion/`, `analysis/`, `api/`, `ui/`, `infra/`. Multi-layer changes write one entry per layer, cross-linked.

When every box in a todo is checked, delete the todo file — the audit-trail entries are the permanent record.

Entries describe *the current system*, not the diff from what came before. See `docs/audit-trail/README.md` for the template.

Update `docs/INVARIANTS.md` in the same PR when an invariant is created, changed, or removed.
