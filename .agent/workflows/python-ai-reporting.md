---
description: Core python code instruction set
---

# Agent Instructions: Python Analysis + FastAPI + React Dashboard

## Objective

Build an analysis and reporting pipeline that:
- Loads raw news + Reddit + X data produced by the Go ingestor (`raw.*` Postgres tables + content-addressed store)
- Extracts clean text and admits it into the normalized corpus (`corpus.*`)
- Claims per-doc work from a Postgres task queue and runs one engine per stage: bot detection, sentiment, per-entity target stance, propaganda-technique detection, deterministic citation extraction, LLM claim extraction, embedding-based narrative clustering, deterministic political-lean derivation, account-tier classification — all with explicit evidence spans where applicable
- Writes every analysis attempt as a traceable `analysis.runs` row (task, model_id, prompt_version_id, confidence) via `results/store.py`
- Serves live-aggregated results via FastAPI — no cache layer
- Displays in React dashboard
- Maintains auditability: every output is traceable to raw sources and model/prompt versions

The system must clearly label reach and sentiment as **proxies** and must avoid claiming to represent all Americans. Reddit outputs must be labeled as "sampled Reddit discourse"; social aggregations covering both sources must be labeled as "Reddit + X".

## Architecture

```
analysis/
├── src/
│   ├── common/          # Shared utilities (logger, settings, db pool, canonicalize)
│   ├── engine/          # One module per pipeline stage: bot_detection, text (sentiment-only),
│   │                    # targets (per-entity stance), propaganda, citations (deterministic),
│   │                    # claims, narrative_clustering (embedding-only), lean_derivation
│   │                    # (deterministic), account_tier (deterministic)
│   ├── results/         # store.py — the only writer of run-anchored analysis.* tables
│   ├── etl/             # documents.py (raw.* -> corpus.*), authors.py, queue.py
│   ├── scheduler/       # pipeline.py + stages.py — task-queue-driven orchestration
│   ├── review/          # service.py — human-in-loop review queue (writes analysis.evals/golden_labels)
│   └── api/             # FastAPI server: routers/ (thin) + queries/ (live aggregation) + models/
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
raw.* (Postgres) -> etl/documents.py -> corpus.* + ops.task_queue
                 -> scheduler/pipeline.py + stages.py (claim work, FOR UPDATE SKIP LOCKED)
                 -> engine/*.py -> results/store.py -> analysis.*
                 -> api/queries/* (live aggregation) -> FastAPI -> React
```

## Key Invariants

### ETL
- Every `corpus.documents` row links to a `raw_hash`
- ETL is deterministic under fixed library versions
- `corpus.documents.raw_hash` must always exist and correspond to raw bytes
- `admission_class` (`sampled` vs `official_record`) governs which docs bypass the ~30-day recency window — only tracked active officials' X posts

### AI Analysis
- Every `analysis.runs` row has `model_id` (NOT NULL, always) and `confidence`
- `prompt_version_id` is required whenever `status='done'` and `inference_method` is `llm`/`hybrid`
- Outputs include evidence spans where applicable; a span that fails verbatim-substring validation either drops the result entirely (claims) or caps confidence (sentiment, propaganda) — never both silently
- No hallucination: if data is missing, return null, not a guess

### Results Contract (`results/store.py`)
- Engines call `open_run()` -> `RunHandle.save_*()` -> `finish()`; nothing reaches Postgres before `finish()` commits the run row plus all accumulated result rows in one transaction
- `is_current` flips the predecessor to `false` before the new row inserts (same transaction) — the query layer always reads `is_current` rows only
- A `failed` run never flips a predecessor and discards all accumulated results — stale-but-valid beats broken
- Two documented exceptions write `analysis.*` directly instead of through a run: `author_bot_scores` (materialized rollup, `bot_detection.py::refresh_author_bot_scores()`) and the narrative tables (batch job over many docs, `narrative_clustering.py`)

## Data Model

See `docs/DATABASE_SCHEMA.md` for the full reference. Core tables:

### corpus.documents
- `doc_id`, `source_type` (`news`/`reddit_post`/`x_post`), `natural_key` (url_canon/fullname/tweet_id)
- `published_at`, `title`, `body`, `source_url` (NOT NULL, invariant C1)
- `raw_hash`, `etl_version`, `admission_class`

### analysis.runs (traceable)
- `run_id`, `task`, `doc_id` XOR `author_id`, `status`, `model_id`, `prompt_version_id`, `inference_method`, `confidence`, `is_current`, `raw_response` (JSONB), `error`
- One run feeds one or more typed result tables (e.g. a `propaganda` run writes both `propaganda_results` and `propaganda_techniques`)

### narrative overlay
- `analysis.narratives` (identity, `first_seen_doc_id`, `anchor_embedding`)
- `analysis.narrative_docs` (membership, `confidence`, `added_by_run`)
- `analysis.citations` (partial link graph — owned -> owned or owned -> external URL)

### human-in-loop
- `analysis.evals` (per-run verdict), `analysis.golden_labels` (run-independent expected answer)

## Commands

```bash
# Run full analysis pipeline
./run.sh analyze
./run.sh analyze --tasks bot,text   # specific stages

# Start FastAPI server only
./run.sh api

# Start React dev server
./run.sh ui

# Start both API + UI
./run.sh dev
```

## API Endpoints

Health is unversioned; everything else mounts under `/api/v1` (`analysis/src/api/routers/`, thin wrappers over `analysis/src/api/queries/`).

| Endpoint | Description |
|----------|-------------|
| `GET /health` | Health check |
| `GET /api/v1/snapshot-status` | Latest pipeline run status/freshness (`ops.pipeline_runs`) |
| `GET /api/v1/sentiment?window=...` | Sentiment panel: net tone, distribution, splits, per-entity stance |
| `GET /api/v1/bot-activity` | Bot activity |
| `GET /api/v1/propaganda?window=...` | Propaganda-technique overview |
| `GET /api/v1/movers?window=...` | Largest sentiment movers |
| `GET /api/v1/narratives?window=...&limit=...` | Top narratives (claim clusters) |
| `GET /api/v1/entity-posts` / `GET /api/v1/entity-profile/{entity_id}` | Entity-scoped posts/profile |
| `GET /api/v1/outlet-profiles` | Outlet profiles |
| `GET /api/v1/docs/{doc_id}` | Universal doc drill-down (no time predicate) |
| `GET /api/v1/review/queue` / `POST /api/v1/review/submit` / `GET /api/v1/review/stats` | Human review flow (`review/service.py`) — admin |
| `POST /api/v1/run/*` | Trigger a pipeline stage in the background — admin |

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

1. Can run end-to-end: `./run.sh analyze` populates `analysis.*`, `./run.sh dev` serves it live
2. Dashboard shows sentiment, per-entity stance, bot activity, propaganda, narratives
3. Every data point is traceable to a source document and an `analysis.runs` row
4. Reach and sentiment are clearly labeled as proxies/samples
5. Narrative "first seen" is honestly framed as first-ingested-by-us, not world-origin
6. Citation counts are labeled as a partial link graph (owned-only)
