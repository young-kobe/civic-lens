---
description: Global rules
---

# Civic Lens Architecture

## Overview

Civic Lens measures how political narratives propagate across news media and online discourse.

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
