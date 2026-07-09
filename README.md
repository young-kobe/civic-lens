# Civic Lens

Civic Lens is an audit-driven system for measuring **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** that clusters recurring claims and a **partial citation overlay** between owned sources. Every output is traceable, confidence-scored, and labeled as a sample; the system never fabricates data.

The goal is deliberately scoped: this is a sampled-discourse tracker with a narrative overlay, not a causal propagation engine. See `docs/walkthroughs/035-goal-narrowing-and-renames.md` for the scoping rationale.

## Architecture
- **Ingestion (`ingest/`)**: Go 1.22+ crawler with SQLite frontier. Polite, resumable, crash-safe. Fetches news (RSS + web), Reddit, and X.
- **Analysis (`analysis/`)**: Python backend for ETL and AI analysis — bot detection, unified sentiment + GOP favorability, deterministic citation extraction, LLM claim extraction, propaganda-technique detection, narrative clustering — plus pre-computed snapshot caching.
- **API (`analysis/src/api/`)**: FastAPI server serving cached analysis results.
- **Frontend (`ui/`)**: React + Vite + TypeScript dashboard.

See `docs/INVARIANTS.md` for data-integrity invariants and `docs/ARCHITECTURE_DIAGRAM.md` for the data-flow diagram.

## Prerequisites
- [Go 1.22+](https://go.dev/dl/)
- Python 3.10+
- Node.js 18+ (for UI)

## Quick Start

```powershell
# 1. Apply DB migrations
.\run.ps1 migrate

# 2. Build and run the crawler (news + Reddit + X)
.\run.ps1 crawl

# 3. Run the analysis pipeline (ETL + AI + caching)
.\run.ps1 analyze

# 4. Start API + UI
.\run.ps1 dev
```

The API serves pre-computed data from `data/cache/`. Run `.\run.ps1 analyze` periodically (or via the scheduled task) to refresh.

## Commands

| Command | Description |
|---------|-------------|
| `.\run.ps1 build` | Build Go ingestion binary |
| `.\run.ps1 migrate` | Apply pending DB migrations |
| `.\run.ps1 crawl` | Run the web crawler (news via RSS/HTML) |
| `.\run.ps1 reddit` | Fetch Reddit posts/comments |
| `.\run.ps1 x` | Fetch X/Twitter posts |
| `.\run.ps1 analyze` | Full analysis pipeline (ETL + bot + text + citations + claims + narratives + snapshots) |
| `.\run.ps1 analyze -Tasks bot,text` | Run specific pipeline stages |
| `.\run.ps1 api` | Start FastAPI server |
| `.\run.ps1 ui` | Start React dev server |
| `.\run.ps1 dev` | Start both API and UI |

## Scheduled Analysis

```powershell
.\setup-scheduled-task.ps1              # Default: every 6 hours
.\setup-scheduled-task.ps1 -RunsPerDay 4  # Customize frequency
.\setup-scheduled-task.ps1 -Remove        # Remove the task
```

## Data Storage
- **Database**: `data/civic_lens.db` (SQLite, WAL mode)
- **Raw content**: `data/raw/sha256/` (content-addressed)
- **Analysis cache**: `data/cache/` (pre-computed JSON snapshots)

## Configuration

Set environment variables (prefixed `CIVIC_`) in `.env`. See `analysis/src/common/settings.py` for the full list. Key switches:
- `CIVIC_LLM_BACKEND` = `gemini` | `ollama`
- `CIVIC_LLM_ENABLED` = `true` | `false`
- `CIVIC_RUN_ANALYSIS_ON` = `all` | `social_media` | `x`
- `CIVIC_NARRATIVE_SIMILARITY_MODE` = `jaccard` | `embedding`

Crawler seeds (RSS, subreddits, Reddit/X API creds, rate limits) live in `data/seeds.yaml`.

## API Endpoints

Health is unversioned; everything else is mounted under `/api/v1`. Read routes serve pre-computed snapshots; `run/*` and `review/*` are write/admin surfaces gated by `CIVIC_ADMIN_TOKEN`.

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Health check |
| `GET /api/v1/cache-status` | Cache freshness metadata |
| `GET /api/v1/snapshot-status` | Per-snapshot build status |
| `GET /api/v1/sentiment?window=7d` | Sentiment + GOP favorability snapshot |
| `GET /api/v1/bot-activity` | Bot activity snapshot |
| `GET /api/v1/propaganda?window=7d` | Propaganda-technique snapshot |
| `GET /api/v1/movers?window=7d` | Largest sentiment movers |
| `GET /api/v1/narratives?window=7d&limit=20` | Top narratives (claim clusters) with per-source breakdown |
| `GET /api/v1/review/queue?task=sentiment` | Human-review queue (lowest-confidence first) — admin |
| `POST /api/v1/review/submit` | Submit a human verdict (feeds golden set / calibration) — admin |
| `GET /api/v1/review/stats?task=sentiment` | Reviewer accuracy + coverage stats — admin |
| `POST /api/v1/run/etl` | Trigger ETL in the background — admin |
| `POST /api/v1/run/full-pipeline` | Trigger the full pipeline in the background — admin |

## Invariants

See [`docs/INVARIANTS.md`](docs/INVARIANTS.md) for the correctness checklist. Key points: every row in `docs` and `ai_outputs` is traceable to a `raw_hash`, all AI outputs carry a confidence score, evidence spans must be verbatim substrings of source text, and no metric that is a proxy for something larger (sentiment, reach) may be presented without that framing.
