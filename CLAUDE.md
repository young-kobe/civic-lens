# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Civic Lens measures **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** that clusters recurring claims and a **partial citation overlay** between owned sources. It is an audit-driven system: every output is traceable, confidence-scored, labeled as a sample, and never fabricates data (see `docs/INVARIANTS.md` and `.agent/rules/`).

The goal is deliberately scoped to what the data supports. We do not claim to measure "propagation" in the causal sense (origin-in-the-world, full cross-medium flow) — the citation graph only covers edges between docs we ingested, and "first seen" refers to first-ingested-by-us. See walkthrough 035 for the rationale.

## Four-Layer Architecture

The codebase is strictly layered. Changes should respect these boundaries — do not let a lower layer depend on a higher one.

1. **Ingestion — Go (`ingest/`)**: Crash-resumable crawler + Reddit/X fetchers. Writes raw HTML/JSON to content-addressed storage (`data/raw/sha256/<hash>`) and metadata to SQLite (`data/civic_lens.db`). Entry: `ingest/cmd/civic-ingest/main.go` (cobra CLI with `migrate|ingest|crawl|reddit|x|requeue-stale` subcommands). Frontier state machine: `QUEUED -> INFLIGHT -> DONE|FAILED`; `INFLIGHT` rows are reset on startup.
2. **Analysis — Python (`analysis/src/`)**:
   - `etl/loader.py` normalizes raw → `docs` table, filtered to ~30 days of US-politics content.
   - `engine/` runs bot detection, unified sentiment+favorability (`analyzer.py`), deterministic citation extraction (`citation_extractor.py`), LLM claim extraction (`claim_extractor.py`), and lexical / embedding narrative clustering (`narrative_clusterer.py`). Writes to `ai_outputs`, `narratives`, `narrative_docs`, `narrative_citations`.
   - `llm/` wraps Gemini and Ollama behind `factory.get_llm_client()`, selected by `CIVIC_LLM_BACKEND`.
   - `reporting/aggregators/` pre-computes dashboard data into `SnapshotCache` (`data/cache/*.json`) at multiple time windows (`24h|7d|30d|90d`).
   - `reporting/review.py` serves the human-in-loop review queue and writes `ai_output_evals` (golden-set + correctness markers) — consumed by the Review tab.
   - `scheduler/job_runner.py` orchestrates the full pipeline (ETL → bot → text → citations → claims → narratives → snapshots).
3. **API — FastAPI (`analysis/src/api/server.py`)**: Stateless; serves pre-computed JSON from `data/cache/`. Heavy aggregation does *not* happen at request time — it runs in `job_runner.save_snapshots()`.
4. **UI — React + Vite + TypeScript (`ui/src/`)**: Dev server proxies `/api/*` to `http://localhost:8000`. Consumes API only; no direct DB access.

**Data flow:** RSS/Reddit/X → Go crawler → SQLite + raw files → Python ETL → LLM analysis → JSON snapshot cache → FastAPI → React.

The cache is the contract between analysis and API: to make new data appear in the UI, add it to an aggregator and have `job_runner.save_snapshots()` write it under a key the API reads.

## Commands

All orchestration goes through `run.sh` (bash, Linux/WSL). It auto-creates `analysis/.venv`, loads `.env`, and resolves Go/Node paths.

```bash
./run.sh build                      # Build civic-ingest (Go binary at repo root)
./run.sh migrate                    # Apply DB migrations
./run.sh crawl                      # Run web crawler (default 10m; --duration 5m)
./run.sh reddit                     # Fetch Reddit posts/comments
./run.sh x                          # Fetch X/Twitter posts
./run.sh analyze                    # Full pipeline: ETL + AI + caching
./run.sh analyze --tasks bot,text   # Run specific stages only
./run.sh analyze --limit 50         # Cap docs processed per stage (dev)
./run.sh api                        # FastAPI on :8000
./run.sh ui                         # Vite dev server on :5173
./run.sh dev                        # API + UI together (Ctrl-C stops both)
```

Valid `--tasks` values: `etl, bot, text, targets, propaganda, citations, claims, narratives, accounts, bot_rollup, snapshots`.

### Tests and typecheck

```bash
# Python tests — run from repo root with PYTHONPATH set so `analysis.src.*` imports resolve.
# The venv python is analysis/.venv/bin/python; a system python3 with the deps also works.
PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest analysis.tests.test_engines
PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest analysis.tests.test_engines.TestEngines.test_bot_detector  # single test
PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest discover analysis/tests                                    # all

# Go tests
cd ingest && go test ./...
cd ingest && go test ./internal/runner -run TestReddit   # single package/test

# TypeScript typecheck (no tests in UI currently)
cd ui && npm run typecheck
cd ui && npm run build   # tsc + vite build
```

## Configuration

All Python settings use the `CIVIC_` env prefix and are defined in `analysis/src/common/settings.py` (pydantic-settings). `.env` is loaded automatically by `run.sh` and by pydantic. Key switches:

- `CIVIC_LLM_BACKEND` = `gemini` | `ollama`
- `CIVIC_LLM_ENABLED` = `true` | `false`
- `CIVIC_RUN_ANALYSIS_ON` = `all` | `social_media` | `x` (scopes which `source_type` docs are analyzed)
- `CIVIC_LOADER_BATCH_SIZE`
- `CIVIC_NARRATIVE_EMBEDDING_MODEL` (blank = backend default), `CIVIC_NARRATIVE_EMBEDDING_THRESHOLD` — clustering is embedding-only; a backend that cannot embed fails the stage

`data/seeds.yaml` drives the Go ingestor (RSS seeds, subreddits, Reddit API creds, rate limits).

## Project Conventions

- **Plan -> audit-trail workflow.** This is a hard rule. Every non-trivial change follows three steps:
  1. **Plan.** Future work lives in `docs/todos/<initiative>.md` as a checklist. One file per initiative. No speculative "someday" entries — if it's not concrete enough to tick off, it doesn't belong there.
  2. **Execute.** As boxes tick, the code lands.
  3. **Record.** In the same PR, add a dated entry under the affected layer(s) in `docs/audit-trail/<layer>/YYYY-MM-DD-short-slug.md`. Buckets: `ingestion/`, `analysis/`, `api/`, `ui/`, `infra/`. A multi-layer change writes one entry per layer, cross-linked. When every box in a todo is checked, delete the todo file — the audit-trail entries are the permanent record.
  Entries are forward-looking — they describe *the system as it is now* and name what replaced whatever was there, not the old thing on its own. Keep each under ~200 lines; split otherwise. See `docs/audit-trail/README.md` for the entry template.
  The pre-existing `docs/walkthroughs/` linear log is being consolidated into this structure (see `docs/todos/walkthrough-consolidation.md`); until that lands, treat the walkthroughs as a secondary source and do not add to them.
- **Style and invariants live in `.agent/`** — `rules/code-style.md`, `rules/invariants.md`, `rules/media-analysis.md` are always-on and define DRY/SOLID expectations, per-language style, and labeling requirements. `workflows/global.md`, `go-ingestion.md`, `python-ai-reporting.md` have layer-specific details.
- **No emojis anywhere in the codebase** (see `invariants.md` rule 8).
- **Labeling discipline** (media-analysis rules): Reddit outputs are "sampled Reddit discourse"; "Reach" is a proxy unless backed by real traffic; never claim universal American sentiment. UI must display confidence scores next to AI predictions.
- **AI output contract**: every `ai_outputs` row has `confidence`, `model_id`, and `prompt_version`. When adding new LLM tasks, bump the prompt-version constant in `engine/prompts.py` and pass it through `loader.save_ai_output()`.
- **LLM schemas**: structured output is enforced via JSON schemas in `analysis/src/llm/schemas.py`. Both Gemini and Ollama clients go through the same `get_llm_client()` factory — add new tasks by extending the schema and prompts, not by special-casing either backend.

## Platform Notes

Dev environment is Linux/WSL with bash. The canonical entry point is `run.sh` (see Commands); `./setup-cron.sh` schedules the analysis pipeline via cron for dev, while production uses the systemd timer from `deploy/install.sh`. Go produces the `civic-ingest` binary (no extension) at repo root.
