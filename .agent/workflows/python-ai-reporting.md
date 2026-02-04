---
description: Core python code instruction set
---

# Agent Instructions: Python Analysis + FastAPI + React Dashboard

## Objective

Build an analysis and reporting pipeline that:
- Loads raw news + Reddit data produced by the Go ingestor
- Extracts clean text in Python
- Uses AI to compute topic/stance/framing and bot-detection signals with explicit evidence spans
- Pre-computes results into JSON cache files
- Serves cached data via FastAPI
- Displays in React dashboard
- Maintains auditability: every output is traceable to raw sources and model/prompt versions

The system must clearly label reach and sentiment as **proxies** and must avoid claiming to represent all Americans. Reddit outputs must be labeled as "sampled Reddit discourse."

## Architecture

```
analysis/
├── src/
│   ├── common/          # Shared utilities (logger, cache)
│   ├── engine/          # AI analysis (sentiment, bot, clustering)
│   │   └── models/      # Dataclasses for engine outputs
│   ├── reporting/       # Aggregators for dashboard data
│   │   ├── aggregators/ # Domain-specific aggregators
│   │   └── models/      # Dataclasses for aggregator outputs
│   ├── etl/             # Data loading and transformation
│   └── api/             # FastAPI server
└── requirements.txt

ui/
├── src/
│   ├── components/      # Reusable UI components
│   ├── pages/           # Dashboard pages
│   ├── services/        # API client
│   └── types.ts         # TypeScript interfaces
└── package.json
```

## Data Flow

```
SQLite -> ETL (loader.py) -> Engine (AI analysis) -> Aggregators -> Cache (JSON) -> FastAPI -> React
```

## Key Invariants

### ETL
- Every row in `docs` table links to a `raw_hash`
- ETL is deterministic under fixed library versions
- `docs.raw_hash` must always exist and correspond to raw bytes

### AI Analysis
- AI classifications must include confidence scores
- Outputs include evidence spans where applicable
- No hallucination: if data is missing, return null, not a guess

### Cache Architecture
- All dashboard data is pre-computed and stored in `data/cache/`
- FastAPI serves cached JSON directly (stateless)
- Cache is refreshed by running `.\run.ps1 analyze`

## Data Model

### docs (core)
- `doc_id` (stable id)
- `source_type`: `news` | `reddit_post` | `reddit_comment` | `x_post`
- `url_canon` or `fullname`
- `domain` or `subreddit`
- `published_at`, `fetched_at`
- `title`, `text` (clean)
- `raw_hash`

### ai_outputs (traceable)
- `doc_id`, `task_type` (sentiment|bot|favorability)
- `output_json`, `confidence`
- `model_id`, `prompt_version`, `created_at`

## Commands

```powershell
# Run full analysis pipeline
.\run.ps1 analyze

# Start FastAPI server only
.\run.ps1 api

# Start React dev server
.\run.ps1 ui

# Start both API + UI
.\run.ps1 dev
```

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Health check |
| `GET /api/cache-status` | Cache freshness metadata |
| `GET /api/stories` | Story clusters |
| `GET /api/sentiment` | Public sentiment data |
| `GET /api/favorability` | GOP favorability metrics |
| `GET /api/profiles` | Outlet profiles |
| `GET /api/bot-activity` | Bot detection metrics |

## Proxy Labeling Requirements

### "Reach" Proxies
Unless external traffic data is available, compute proxies:
- Reddit score/comment count references
- Syndication/near-duplicate footprint

Label these explicitly as proxies.

### "How Americans feel"
Must be labeled as:
- "Reddit sample sentiment/themes" or "X sample sentiment"

Include:
- Platform breakdown
- Time window
- Sample sizes

Avoid universal language about national sentiment.

## Acceptance Criteria

1. Can run end-to-end: `.\run.ps1 analyze` populates cache, `.\run.ps1 dev` serves data
2. Dashboard shows clusters, sentiment, favorability, bot activity
3. Every data point is traceable to raw sources
4. Reach and sentiment are clearly labeled as proxies/samples
