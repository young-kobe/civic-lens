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

```powershell
# Build Go ingestion binary
.\run.ps1 build

# Run web crawler (news + Reddit)
.\run.ps1 crawl

# Run analysis pipeline (ETL + AI + caching)
.\run.ps1 analyze

# Start API + UI for development
.\run.ps1 dev
```

## Layer Boundaries

1. **Ingestion (Go)**: Fetches and stores raw content. Outputs to `data/civic_lens.db` and `data/raw/`.
2. **Analysis (Python)**: Reads from DB, runs AI analysis, writes to `data/cache/`.
3. **API (FastAPI)**: Serves cached JSON. Stateless, reads from `data/cache/`.
4. **UI (React)**: Consumes API endpoints. No direct DB access.

## Key Files

- `run.ps1` - Main command runner
- `data/seeds.yaml` - RSS feeds, Reddit subreddits, API config
- `data/civic_lens.db` - SQLite database
- `data/cache/*.json` - Pre-computed analysis snapshots
- `docs/walkthroughs/` - Code audit walkthroughs (see below)

## Walkthrough Archival (Required)

Every completed task that involves code changes **must** produce a walkthrough:

1. Save the walkthrough to `docs/walkthroughs/NNN-short-description.md`
2. Use the next sequential number (check existing files for the current max)
3. Update `docs/walkthroughs/README.md` index table with the new entry
4. Walkthroughs document: what changed, why, and verification results
5. This is a permanent code audit trail - never delete existing walkthroughs
