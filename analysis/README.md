# Civic Lens - Analysis Module

This module handles the extraction, transformation, and AI analysis of news and social
media content. It reads raw blobs ingested by the Go crawler, normalizes them into `docs`,
runs the analysis engines, and pre-computes the JSON snapshot caches the API serves.

## Architecture

- **ETL (`src/etl/`)**: `loader.py` loads raw data from SQLite, normalizes it into the
  `docs` table (filtered to ~30 days of US-politics content), deduplicates, and stamps an
  `etl_version` onto every row.
- **Engine (`src/engine/`)**: core analysis logic.
  - `bot.py`: bot / coordinated-inauthenticity detection (heuristic + optional LLM).
  - `analyzer.py`: unified sentiment + GOP-favorability classification.
  - `citation_extractor.py`: deterministic citation extraction between owned sources.
  - `claim_extractor.py`: LLM claim extraction.
  - `propaganda_detector.py`: LLM propaganda-technique detection (LLM-only, no fallback).
  - `narrative_clusterer.py`: lexical (Jaccard) / embedding narrative clustering.
  - `account_classifier.py`: account-tier classification.
  - `text_prep.py`: shared deterministic pre-LLM gates (sentence-boundary truncation,
    trivial-content short-circuit).
- **LLM (`src/llm/`)**: Gemini, Ollama, and OpenAI-compatible clients behind
  `factory.get_llm_client()`, selected by `CIVIC_LLM_BACKEND` (`gemini` | `ollama` |
  `openai_compat`). The `openai_compat` client speaks the `/v1` REST surface
  (`CIVIC_LLM_BASE_URL` + `CIVIC_LLM_API_KEY`), so any OpenAI-compatible server — a local
  inference runtime or a hosted endpoint — slots in with no engine changes. Structured
  output is enforced via JSON schemas in `schemas.py`; prompts + version constants in
  `prompts.py`.
- **Reporting (`src/reporting/`)**: `aggregators/` pre-compute dashboard data into the
  snapshot cache (`data/cache/*.json`) at multiple time windows; `review.py` serves the
  human-in-loop review queue and writes `ai_output_evals`.
- **Scheduler (`src/scheduler/`)**: `job_runner.py` orchestrates the full pipeline
  (ETL -> bot -> text -> citations -> claims -> propaganda -> narratives -> snapshots).
- **API (`src/api/`)**: FastAPI server (`server.py`) exposing versioned endpoints under
  `/api/v1`. Stateless — it serves the pre-computed cache, not live aggregation.

## Workflows

The canonical entry point for everything below is `run.sh` at the repo root (see the
top-level README). The commands here are the underlying invocations.

### 1. Setup

```bash
pip install -r requirements.txt
```

### 2. Running the API

`src/main.py` boots the FastAPI app (Uvicorn) defined in `api/server.py`:

```bash
python -m analysis.src.main   # or: ./run.sh api
```

Server runs at `http://localhost:8000`; health at `/health`, data under `/api/v1`.

### 3. Running the pipeline

```bash
./run.sh analyze                     # full pipeline
./run.sh analyze --tasks bot,text    # specific stages
```

### 4. Testing

Run from the repo root with `PYTHONPATH` set so `analysis.src.*` imports resolve:

```bash
# all tests
python -m unittest discover analysis/tests

# a single module / test
python -m unittest analysis.tests.test_engines
python -m unittest analysis.tests.test_engines.TestEngines.test_bot_detector
```
