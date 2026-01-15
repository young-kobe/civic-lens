# Civic Lens

Civic Lens is an open, audit-driven system for measuring how political narratives propagate across news media and online public discourse.

## Architecture
- **Ingestion (`ingest/`)**: Go 1.22+ crawler with SQLite frontier. Polite, resumable, crash-safe.
- **Analysis API (`analysis/`)**: Python FastAPI backend for ETL and AI analysis.
- **Frontend (`ui/`)**: React/Vite modern web interface.

## Prerequisites
- [Go 1.22+](https://go.dev/dl/)
- Python 3.10+
- Node.js 18+ (for UI)

## Quick Start
```powershell
# Build Go ingestion binary
.\run.ps1 ingest

# Run crawler (5 mins)
.\run.ps1 crawl

# Start the full stack (API + UI)
.\run.ps1 dev

# Or run components individually:
.\run.ps1 api   # Starts Python FastAPI server
.\run.ps1 ui    # Starts React Dev server
```

## Data Storage
- **Database**: `data/news.db` (SQLite)
- **Raw content**: `data/raw/sha256/` (content-addressed)

## Configuration
Edit `data/seeds.yaml` to configure:
- RSS feed seeds
- Reddit API credentials
- Crawl rate limits

## Invariants
See [INVARIANTS.md](INVARIANTS.md) for data integrity guarantees.