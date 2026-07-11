---
description: Core python code instruction set
---

# Agent Instructions: Python Analysis + FastAPI + React Dashboard

## Objective

Build an analysis and reporting pipeline that:
- Loads raw news + Reddit + X data produced by the Go ingestor
- Extracts clean text in Python
- Uses AI to compute sentiment + favorability + bot signals + claim extraction, all with explicit evidence spans
- Clusters repeat claims into narratives (lexical Jaccard by default; embedding-mode opt-in)
- Extracts a partial citation overlay between owned docs (URL mentions; X reply/quote/retweet)
- Pre-computes results into JSON cache files
- Serves cached data via FastAPI
- Displays in React dashboard
- Maintains auditability: every output is traceable to raw sources and model/prompt versions

The system must clearly label reach and sentiment as **proxies** and must avoid claiming to represent all Americans. Reddit outputs must be labeled as "sampled Reddit discourse"; social aggregations covering both sources must be labeled as "Reddit + X".

## Architecture

```
analysis/
├── src/
│   ├── common/          # Shared utilities (logger, cache, settings)
│   ├── engine/          # AI analysis: bot, sentiment+favorability (analyzer),
│   │                    # citations, claims, narrative clustering
│   │   └── models/      # Dataclasses for engine outputs
│   ├── reporting/       # Aggregators + review service
│   │   ├── aggregators/ # Sentiment / bot / geo / narrative / outlet
│   │   ├── models/      # Dataclasses for aggregator outputs
│   │   └── review.py    # Human-in-loop review queue (writes ai_output_evals)
│   ├── etl/             # Data loading and transformation
│   ├── scheduler/       # Pipeline orchestration (job_runner)
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
- Cache is refreshed by running `./run.sh analyze`

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
- `doc_id`, `task_type` (sentiment|bot|favorability|claims|citations)
- `output_json`, `confidence`
- `model_id`, `prompt_version`, `created_at`
- Joined via `prompt_version` → `prompt_versions` for full prompt text per inference

### narrative overlay
- `narratives` (identity, `first_seen_doc_id`, anchor embedding)
- `narrative_docs` (membership)
- `narrative_citations` (partial link graph — owned → owned or owned → external URL)

### human-in-loop
- `ai_output_evals` (per-row correctness markers + golden-set flags)

## Commands

```bash
# Run full analysis pipeline
./run.sh analyze

# Start FastAPI server only
./run.sh api

# Start React dev server
./run.sh ui

# Start both API + UI
./run.sh dev
```

## API Endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Health check |
| `GET /api/cache-status` | Cache freshness metadata |
| `GET /api/sentiment?window=...` | Sentiment + GOP favorability snapshot |
| `GET /api/profiles` | Outlet profiles |
| `GET /api/bot-activity` | Bot activity snapshot |
| `GET /api/geo-sentiment?window=...` | Country-level sentiment for the global heatmap |
| `GET /api/narratives?window=...&limit=...` | Top narratives (claim clusters) |
| `GET /api/review/queue` / `POST /api/review/submit` / `GET /api/review/stats` | Human review flow |

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

1. Can run end-to-end: `./run.sh analyze` populates cache, `./run.sh dev` serves data
2. Dashboard shows sentiment, favorability, bot activity, narratives, and the global heatmap
3. Every data point is traceable to raw sources
4. Reach and sentiment are clearly labeled as proxies/samples
5. Narrative "first seen" is honestly framed as first-ingested-by-us, not world-origin
6. Citation counts are labeled as a partial link graph (owned-only)
