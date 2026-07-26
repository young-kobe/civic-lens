---
description: Global rules
---

# Civic Lens Architecture

## Overview

Civic Lens measures **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** that clusters recurring claims and a **partial citation overlay** between owned sources. The goal is scoped to what the data supports — it is not a causal propagation engine. See `docs/audit-trail/` (analysis and infra buckets).

## Stack

| Layer | Technology | Location |
|-------|------------|----------|
| Ingestion | Go 1.22+ | `ingest/` |
| Analysis | Python 3.12+ | `analysis/` |
| API | FastAPI | `analysis/src/api/` |
| Frontend | React + Vite + TypeScript | `ui/` |
| Database | Postgres 17 (`raw`/`corpus`/`analysis`/`ops`/`archive` schemas) | `data/pg-migrations/` |

## Data Flow

```
RSS/Reddit/X -> Go Crawler -> raw store + raw.* -> Python ETL -> corpus.* + ops.task_queue
             -> pipeline stages/engines -> analysis.* -> FastAPI (live queries) -> React UI
```

## Common Commands

```bash
# Build Go ingestion binary
./run.sh build

# Start Postgres (dev)
./run.sh pg

# Run web crawler (news + Reddit)
./run.sh crawl

# Run analysis pipeline (ETL + AI, all engine stages)
./run.sh analyze

# Start API + UI for development
./run.sh dev
```

## Layer Boundaries

1. **Ingestion (Go)**: Fetches and stores raw content. Writes to the content-addressed store (`CIVIC_RAW_STORE_DIR`) and `raw.*` Postgres tables.
2. **Analysis (Python)**: Reads `raw.*`, ETLs into `corpus.*`, runs pipeline stages, writes `analysis.*` via `results/store.py`.
3. **API (FastAPI)**: Strictly live — aggregates `corpus.*`/`analysis.*` at request time. No cache layer.
4. **UI (React)**: Consumes API endpoints. No direct DB access.

## Key Files

- `run.sh` - Main command runner
- `data/seeds.yaml` - RSS feeds, Reddit subreddits, API config
- `data/pg-migrations/` - Postgres schema migrations (`0001_north_star.sql` + incremental files)
- `docs/todos/` - Checklists of planned work (see below)
- `docs/audit-trail/` - Permanent record of what shipped, bucketed by layer (see below)

## Plan -> audit-trail workflow (required)

Non-trivial work follows three steps:

1. **Plan.** Create a checklist at `docs/todos/<initiative>.md`. One file per initiative. Each item concrete enough to tick off.
2. **Execute.** As boxes tick, the code lands.
3. **Record.** In the same PR, add a dated entry under the affected layer(s) at `docs/audit-trail/<layer>/YYYY-MM-DD-short-slug.md`. Buckets: `ingestion/`, `analysis/`, `api/`, `ui/`, `infra/`. Multi-layer changes write one entry per layer, cross-linked.

When every box in a todo is checked, delete the todo file — the audit-trail entries are the permanent record.

Entries describe *the current system*, not the diff from what came before. See `docs/audit-trail/README.md` for the template.

Update `docs/INVARIANTS.md` in the same PR when an invariant is created, changed, or removed.
