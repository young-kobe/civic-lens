# Civic Lens

[![CI](https://github.com/young-kobe/civic-lens/actions/workflows/ci.yml/badge.svg)](https://github.com/young-kobe/civic-lens/actions/workflows/ci.yml)
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL--3.0-blue.svg)](./LICENSE)
![Go](https://img.shields.io/badge/Go-crash--resumable_ingester-00ADD8.svg)
![Python](https://img.shields.io/badge/Python-analysis_pipeline-3776AB.svg)
![TypeScript](https://img.shields.io/badge/TypeScript-React_%2B_Vite-3178c6.svg)
![Postgres](https://img.shields.io/badge/Postgres-single_source_of_truth-4169E1.svg)
![FastAPI](https://img.shields.io/badge/FastAPI-strictly_live-009688.svg)
![LLM](https://img.shields.io/badge/LLM-Gemini_%7C_Ollama_%7C_OpenAI--compat-a855f7.svg)
![Outputs](https://img.shields.io/badge/every_output-traceable_%26_confidence--scored-1a8a3d.svg)
[![Live](https://img.shields.io/badge/live-civic--lens.info-brightgreen.svg)](https://civic-lens.info)

Civic Lens is an audit-driven system for measuring **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** that clusters recurring claims and a **partial citation overlay** between owned sources. Every output is traceable, confidence-scored, and labeled as a sample; the system never fabricates data.

The goal is deliberately scoped: this is a sampled-discourse tracker with a narrative overlay, not a causal propagation engine. The citation graph only covers edges between docs we ingested, and narrative "first seen" means first-ingested-by-us, not world-origin. See `docs/audit-trail/` for the scoping rationale and its evolution.

## Architecture

- **Ingestion (`ingest/`)**: Go 1.22+ crawler with a Postgres-backed frontier. Polite, resumable, crash-safe. Fetches news (RSS + web), Reddit, and X, writing raw content to a content-addressed store and metadata to `raw.*` Postgres tables.
- **Analysis (`analysis/`)**: Python backend for ETL and AI analysis — bot detection, sentiment, per-entity target stance, propaganda-technique detection, deterministic citation extraction, LLM claim extraction, embedding-based narrative clustering, and deterministic political-lean derivation. Pipeline stages claim work from a Postgres task queue and write typed, run-anchored results.
- **API (`analysis/src/api/`)**: FastAPI server, strictly live — every panel aggregates the Postgres schema directly at request time. No cache layer.
- **Frontend (`ui/`)**: React + Vite + TypeScript dashboard.

See `docs/INVARIANTS.md` for data-integrity invariants, `docs/ARCHITECTURE_DIAGRAM.md` for the data-flow diagram, and `docs/DATABASE_SCHEMA.md` for the full schema reference.

## Prerequisites

- [Go 1.22+](https://go.dev/dl/)
- Python 3.12+
- Node.js 20+ (for UI)
- Docker (for Postgres)

## Quick Start

```bash
# 1. Start Postgres (dev)
./run.sh pg

# 2. Apply DB migrations
./run.sh migrate

# 3. Build and run the crawler (news + Reddit + X)
./run.sh crawl

# 4. Run the analysis pipeline (ETL + AI)
./run.sh analyze

# 5. Start API + UI
./run.sh dev
```

The API queries Postgres live on every request. Run `./run.sh analyze` periodically (or via cron/systemd timer) to bring in new data.

## Commands

| Command | Description |
|---------|-------------|
| `./run.sh build` | Build Go ingestion binary |
| `./run.sh migrate` | Apply pending DB migrations |
| `./run.sh pg` | Start Postgres (dev, via Docker Compose) |
| `./run.sh crawl` | Run the web crawler (news via RSS/HTML) |
| `./run.sh reddit` | Fetch Reddit posts/comments |
| `./run.sh x` | Fetch X/Twitter posts |
| `./run.sh analyze` | Full analysis pipeline (ETL + all engine stages) |
| `./run.sh analyze --tasks bot,text` | Run specific pipeline stages |
| `./run.sh api` | Start FastAPI server |
| `./run.sh ui` | Start React dev server |
| `./run.sh dev` | Start both API and UI |

## Scheduled Analysis

```bash
./setup-cron.sh                    # Default: every 6 hours (4 runs/day)
./setup-cron.sh --runs-per-day 8   # Customize frequency
./setup-cron.sh --remove           # Remove the cron entry
```

For production, use the systemd timers installed by `deploy/install.sh` instead of cron — see `deploy/README.md`.

## Data Storage

- **Database**: Postgres 17, four active schemas — `raw` (Go-written capture layer), `corpus` (normalized documents/authors/entity registry), `analysis` (runs + typed per-task results), `ops` (task queue, run provenance, migration ledger). An `archive` schema holds a one-time, read-only import of the pre-Postgres data.
- **Raw content**: content-addressed store at `CIVIC_RAW_STORE_DIR` (`data/raw/sha256/` in dev).

## Configuration

Set environment variables (prefixed `CIVIC_`) in `.env`. See `analysis/src/common/settings.py` for the full list. Key switches:

| Variable | Purpose |
|----------|---------|
| `CIVIC_DATABASE_URL` | Required Postgres DSN; the API and pipeline refuse to start without it |
| `CIVIC_LLM_BACKEND` | `gemini` \| `ollama` \| `openai_compat` |
| `CIVIC_RUN_ANALYSIS_ON` | `all` \| `social_media` \| `x` — which `source_type` docs get analyzed |
| `CIVIC_NARRATIVE_EMBEDDING_MODEL` | Required — narrative clustering is embedding-only and refuses to run without it |
| `CIVIC_ANALYZE_CONCURRENCY` / `CIVIC_PG_POOL_MAX` | Pipeline worker concurrency and Postgres pool size (keep the pool above concurrency) |
| `CIVIC_ADMIN_TOKEN` | Shared secret gating admin/review/pipeline-trigger endpoints |

Crawler seeds (RSS, subreddits, Reddit/X API creds, rate limits) live in `data/seeds.yaml`.

## API Endpoints

Health is unversioned; everything else is mounted under `/api/v1`. Every panel queries Postgres live; `run/*` and `review/*` are write/admin surfaces gated by `CIVIC_ADMIN_TOKEN`.

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Health check |
| `GET /api/v1/snapshot-status` | Latest pipeline run status/freshness |
| `GET /api/v1/sentiment?window=7d` | Net tone, distribution, platform/topic/time splits, per-entity stance |
| `GET /api/v1/bot-activity` | Bot activity |
| `GET /api/v1/propaganda?window=7d` | Propaganda-technique overview |
| `GET /api/v1/movers?window=7d` | Largest sentiment movers |
| `GET /api/v1/narratives?window=7d&limit=20` | Top narratives (claim clusters) with per-source breakdown |
| `GET /api/v1/entity-posts` / `GET /api/v1/entity-profile/{entity_id}` | Entity-scoped posts and profile |
| `GET /api/v1/outlet-profiles` | Outlet-level profiles |
| `GET /api/v1/docs/{doc_id}` | Universal document drill-down |
| `GET /api/v1/review/queue?task=text` | Human-review queue (lowest-confidence first) — admin |
| `POST /api/v1/review/submit` | Submit a human verdict (feeds golden set / calibration) — admin |
| `POST /api/v1/run/*` | Trigger a pipeline stage in the background — admin |

## Invariants

See [`docs/INVARIANTS.md`](docs/INVARIANTS.md) for the correctness checklist. Key points: every `analysis.runs` row traces back to a source document (or author) and carries `model_id`, `prompt_version_id`, and `confidence`; evidence spans must be verbatim substrings of source text; and no metric that is a proxy for something larger (sentiment, reach) may be presented without that framing.

## License

Civic Lens is open source under the [AGPL-3.0](LICENSE) so its methodology is fully inspectable, but it is a single-maintainer project that does not accept code contributions — see [`CONTRIBUTING.md`](CONTRIBUTING.md).
