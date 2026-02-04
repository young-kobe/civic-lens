# Civic Lens

Civic Lens is an open, audit-driven system for measuring how political narratives propagate across news media and online public discourse.

## Architecture
- **Ingestion (`ingest/`)**: Go 1.22+ crawler with SQLite frontier. Polite, resumable, crash-safe.
- **Analysis (`analysis/`)**: Python backend for ETL, AI analysis, and pre-computed caching.
- **API (`analysis/src/api/`)**: FastAPI server serving cached analysis results.
- **Frontend (`ui/`)**: React/Vite modern web interface.

## Prerequisites
- [Go 1.22+](https://go.dev/dl/)
- Python 3.10+
- Node.js 18+ (for UI)

## Quick Start

```powershell
# 1. Build and run the crawler (fetches news articles)
.\run.ps1 crawl

# 2. Run the analysis pipeline (ETL + AI + caching)
.\run.ps1 analyze

# 3. Start API + UI
.\run.ps1 dev
```

The API serves pre-computed data from `data/cache/`. Run `.\run.ps1 analyze` periodically to refresh the analysis.

## Commands

| Command | Description |
|---------|-------------|
| `.\run.ps1 build` | Build Go ingestion binary |
| `.\run.ps1 crawl` | Run the web crawler |
| `.\run.ps1 analyze` | Run analysis pipeline (ETL + AI + caching) |
| `.\run.ps1 api` | Start Python FastAPI server |
| `.\run.ps1 ui` | Start React dev server |
| `.\run.ps1 dev` | Start both API and UI |

## Scheduled Analysis

To run the analysis pipeline automatically on a schedule:

```powershell
# Set up Windows Task Scheduler (run as Admin)
.\setup-scheduled-task.ps1              # Default: every 6 hours
.\setup-scheduled-task.ps1 -RunsPerDay 4  # Customize frequency
.\setup-scheduled-task.ps1 -Remove        # Remove the task
```

## Data Storage
- **Database**: `data/civic_lens.db` (SQLite)
- **Raw content**: `data/raw/sha256/` (content-addressed)
- **Analysis cache**: `data/cache/` (pre-computed JSON snapshots)

## Configuration
Edit `data/seeds.yaml` to configure:
- RSS feed seeds
- Reddit API credentials
- Crawl rate limits

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

## Invariants
See [INVARIANTS.md](INVARIANTS.md) for data integrity guarantees.