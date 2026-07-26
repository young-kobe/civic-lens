# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Civic Lens measures **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** that clusters recurring claims and a **partial citation overlay** between owned sources. It is an audit-driven system: every output is traceable, confidence-scored, labeled as a sample, and never fabricates data (see `docs/INVARIANTS.md` and `.agent/rules/`).

The goal is deliberately scoped to what the data supports. We do not claim to measure "propagation" in the causal sense (origin-in-the-world, full cross-medium flow) — the citation graph only covers edges between docs we ingested, and "first seen" refers to first-ingested-by-us. See `docs/audit-trail/` (analysis and infra buckets) for the rationale and its evolution.

## Four-Layer Architecture

The codebase is strictly layered. Changes should respect these boundaries — do not let a lower layer depend on a higher one.

1. **Ingestion — Go (`ingest/`)**: Crash-resumable crawler + Reddit/X fetchers. Writes raw HTML/JSON to content-addressed storage (`CIVIC_RAW_STORE_DIR`, `data/raw/sha256/<hash>` in dev) and metadata to Postgres `raw.*` tables. Entry: `ingest/cmd/civic-ingest/main.go` (cobra CLI with `migrate|ingest|crawl|reddit|x|requeue-stale` subcommands). Frontier state machine: `queued -> inflight -> done|failed`; `inflight` rows are reset on startup. Migrations live in `data/pg-migrations/` (`0001_north_star.sql` plus incremental files), tracked in `ops.schema_migrations`.
2. **Analysis — Python (`analysis/src/`)**:
   - `etl/documents.py` admits `raw.*` rows into `corpus.documents`/`corpus.authors` (with `admission_class`: `sampled` = ~30-day window, `official_record` = tracked officials' X posts admitted regardless of age) and seeds per-doc work into `ops.task_queue`.
   - `scheduler/pipeline.py` + `scheduler/stages.py` orchestrate the pipeline stage-by-stage, claiming `ops.task_queue` rows with `FOR UPDATE SKIP LOCKED`.
   - `engine/` holds one module per stage: `text.py` (sentiment only — favorability retired, party stance now lives in `targets.py`'s `target_mentions` joined to `corpus.entities.lean`), `targets.py`, `propaganda.py`, `claims.py`, `citations.py` (deterministic), `narrative_clustering.py` (embedding-only), `lean_derivation.py`, `bot_detection.py`, `account_tier.py`.
   - `results/store.py` is the only writer of run-anchored `analysis.*` result tables: every engine calls `open_run()` / `RunHandle.save_*()` / `finish()`, producing one `analysis.runs` row (`task`, `model_id`, `prompt_version_id`, `inference_method`, `confidence`, `is_current`) per analysis attempt.
   - `llm/` wraps Gemini, Ollama, and OpenAI-compatible backends behind `factory.get_llm_client()`, selected by `CIVIC_LLM_BACKEND`. Prompts live in `llm/prompts.py`.
   - `review/service.py` serves the human-in-loop review queue and writes `analysis.evals` / `analysis.golden_labels` — consumed by the Review tab.
3. **API — FastAPI (`analysis/src/api/server.py`)**: Strictly live — every panel aggregates `corpus.*`/`analysis.*` directly at request time via `api/queries/`. There is no cache layer and no snapshot files; `GET /snapshot-status` reads the latest `ops.pipeline_runs` row for freshness instead.
4. **UI — React + Vite + TypeScript (`ui/src/`)**: Dev server proxies `/api/*` to `http://localhost:8000`. Consumes API only; no direct DB access.

**Data flow:** RSS/Reddit/X → Go crawler → content-addressed raw store + `raw.*` → Python ETL (`corpus.*` + `ops.task_queue`) → pipeline stages/engines → `analysis.*` → FastAPI live queries → React.

The schema plus `analysis/tests/contract/` is the contract between analysis and API: a new panel is a new query module reading `corpus.*`/`analysis.*` at request time, not a cache key.

## Commands

All orchestration goes through `run.sh` (bash, Linux/WSL). It auto-creates `analysis/.venv`, loads `.env`, and resolves Go/Node paths.

```bash
./run.sh build                      # Build civic-ingest (Go binary at repo root)
./run.sh migrate                    # Apply DB migrations
./run.sh crawl                      # Run web crawler (default 10m; --duration 5m)
./run.sh reddit                     # Fetch Reddit posts/comments
./run.sh x                          # Fetch X/Twitter posts
./run.sh analyze                    # Full pipeline: ETL + all engine stages
./run.sh analyze --tasks bot,text   # Run specific stages only
./run.sh analyze --limit 50         # Cap docs processed per stage (dev)
./run.sh api                        # FastAPI on :8000
./run.sh ui                         # Vite dev server on :5173
./run.sh dev                        # API + UI together (Ctrl-C stops both)
```

Valid `--tasks` values, in pipeline order (`scheduler/constants.py::STAGE_ORDER`): `etl, account_tier, bot, text, targets, propaganda, citations, claims, bot_rollup, narratives, leans`.

### Tests and typecheck

```bash
# Python tests — run from repo root with PYTHONPATH set so `analysis.src.*` imports resolve.
# The venv python is analysis/.venv/bin/python; a system python3 with the deps also works.
PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest discover analysis/tests                                    # all
PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest analysis.tests.test_engine_claims                          # one module
PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest analysis.tests.test_engine_claims.AnalyzeExtractionMappingTests  # single case

# Postgres-gated tests additionally need CIVIC_TEST_DATABASE_URL pointed at a throwaway
# postgres:17-alpine with migrations applied; they skip cleanly when it is unset.

# Go tests (CIVIC_TEST_POSTGRES_DSN gates Postgres-backed integration tests; skip cleanly when unset)
cd ingest && go test ./...
cd ingest && go test ./internal/runner -run TestReddit   # single package/test

# TypeScript typecheck (no tests in UI currently)
cd ui && npm run typecheck
cd ui && npm run build   # tsc + vite build
```

## Configuration

All Python settings use the `CIVIC_` env prefix and are defined in `analysis/src/common/settings.py` (pydantic-settings). `.env` is loaded automatically by `run.sh` and by pydantic. Key switches:

- `CIVIC_DATABASE_URL` — required; `common/db.py` raises immediately if unset rather than guessing a DSN. Two forms: `@postgres:5432` in-container, `@127.0.0.1:5432` from the host.
- `CIVIC_LLM_BACKEND` = `gemini` | `ollama` | `openai_compat`
- `CIVIC_RUN_ANALYSIS_ON` = `all` | `social_media` | `x` (scopes which `source_type` docs are analyzed)
- `CIVIC_ANALYZE_CONCURRENCY` (worker threads per stage) / `CIVIC_PG_POOL_MAX` (connection pool max — keep above concurrency)
- `CIVIC_NARRATIVE_EMBEDDING_MODEL` (required — no default; clustering refuses to start blank), `CIVIC_NARRATIVE_EMBEDDING_THRESHOLD` — clustering is embedding-only; a backend that cannot embed fails the stage

`data/seeds.yaml` drives the Go ingestor (RSS seeds, subreddits, Reddit API creds, rate limits).

## Project Conventions

- **Plan -> audit-trail workflow.** This is a hard rule. Every non-trivial change follows three steps:
  1. **Plan.** Future work lives in `docs/todos/<initiative>.md` as a checklist. One file per initiative. No speculative "someday" entries — if it's not concrete enough to tick off, it doesn't belong there.
  2. **Execute.** As boxes tick, the code lands.
  3. **Record.** In the same PR, add a dated entry under the affected layer(s) in `docs/audit-trail/<layer>/YYYY-MM-DD-short-slug.md`. Buckets: `ingestion/`, `analysis/`, `api/`, `ui/`, `infra/`. A multi-layer change writes one entry per layer, cross-linked. When every box in a todo is checked, delete the todo file — the audit-trail entries are the permanent record.
  Entries are forward-looking — they describe *the system as it is now* and name what replaced whatever was there, not the old thing on its own. Keep each under ~200 lines; split otherwise. See `docs/audit-trail/README.md` for the entry template.
- **Style and invariants live in `.agent/`** — `rules/code-style.md`, `rules/invariants.md`, `rules/media-analysis.md` are always-on and define DRY/SOLID expectations, per-language style, and labeling requirements. `workflows/global.md`, `go-ingestion.md`, `python-ai-reporting.md` have layer-specific details.
- **No emojis anywhere in the codebase** (see `invariants.md` rule 8).
- **Labeling discipline** (media-analysis rules): Reddit outputs are "sampled Reddit discourse"; "Reach" is a proxy unless backed by real traffic; never claim universal American sentiment. UI must display confidence scores next to AI predictions.
- **AI output contract**: every `analysis.runs` row has `confidence`, `model_id`, and `prompt_version_id` (nullable only for deterministic runs). When adding new LLM tasks, bump the prompt-version constant in `llm/prompts.py` and pass it through `results/store.py`'s `open_run()`/`finish()`.
- **LLM schemas**: structured output is enforced via JSON schemas in `analysis/src/llm/schemas.py`. Gemini, Ollama, and OpenAI-compatible clients go through the same `get_llm_client()` factory — add new tasks by extending the schema and prompts, not by special-casing a backend.

## Platform Notes

Dev environment is Linux/WSL with bash. The canonical entry point is `run.sh` (see Commands); `./setup-cron.sh` schedules the analysis pipeline via cron for dev, while production uses the systemd timer from `deploy/install.sh`. Go produces the `civic-ingest` binary (no extension) at repo root.
