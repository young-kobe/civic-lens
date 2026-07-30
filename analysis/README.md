# Civic Lens - Analysis Module

This module admits raw ingested content into the corpus, runs the analysis
engines over it, and serves the results live via FastAPI. Everything reads
and writes Postgres (`CIVIC_DATABASE_URL`); there is no cache layer.

## Architecture

- **ETL (`src/etl/`)**: `documents.py` admits `raw.*` rows into
  `corpus.documents`/`corpus.authors` (with `admission_class`: `sampled`
  ~30-day window, `official_record` for tracked officials' X posts) and
  seeds per-doc work into `ops.task_queue`.
- **Scheduler (`src/scheduler/`)**: `pipeline.py` + `stages.py` run the
  pipeline stage-by-stage, claiming `ops.task_queue` rows with
  `FOR UPDATE SKIP LOCKED`. Stage order lives in `constants.py::STAGE_ORDER`.
- **Engine (`src/engine/`)**: one module per stage.
  - `text.py`: sentiment (sentiment only — party stance comes from
    `targets.py` mentions joined to `corpus.entities.lean`).
  - `targets.py`: target/stance mention extraction.
  - `propaganda.py`: LLM propaganda-technique detection (LLM-only).
  - `claims.py`: LLM claim extraction.
  - `citations.py`: deterministic citation edges between ingested docs.
  - `narrative_clustering.py`: embedding-only claim clustering.
  - `lean_derivation.py`: deterministic derived leans (authors, narratives).
  - `bot_detection.py` / `account_tier.py`: bot signals and author tiers.
  - `text_prep.py`: shared deterministic pre-LLM gates.
- **Results (`src/results/store.py`)**: the only writer of run-anchored
  `analysis.*` tables — every engine goes through
  `open_run()` / `RunHandle.save_*()` / `finish()`.
- **LLM (`src/llm/`)**: Gemini, Ollama, and OpenAI-compatible clients behind
  `factory.get_llm_client()`, selected by `CIVIC_LLM_BACKEND` (`gemini` |
  `ollama` | `openai_compat`). Structured output is enforced via JSON schemas
  in `schemas.py`; prompts + version constants in `prompts.py`.
- **Review (`src/review/service.py`)**: human-in-loop review queue; writes
  `analysis.evals` / `analysis.golden_labels`.
- **API (`src/api/`)**: FastAPI server (`server.py`). Strictly live — every
  panel aggregates `corpus.*`/`analysis.*` at request time via
  `api/queries/`; `/snapshot-status` reads the latest `ops.pipeline_runs`
  row for freshness.

## Workflows

The canonical entry point for everything below is `run.sh` at the repo root
(see the top-level README). The commands here are the underlying invocations.

### 1. Setup

```bash
pip install -r requirements.txt
```

### 2. Running the API

```bash
python -m analysis.src.main   # or: ./run.sh api
```

Server runs at `http://localhost:8000`; health at `/health`, data under `/api/v1`.

### 3. Running the pipeline

```bash
./run.sh analyze                     # full pipeline
./run.sh analyze --tasks bot,text    # specific stages
./run.sh analyze --limit 50          # cap docs per stage (dev)
```

### 4. Testing

Run from the repo root with `PYTHONPATH` set so `analysis.src.*` imports resolve:

```bash
# all tests
python -m unittest discover analysis/tests

# a single module / test
python -m unittest analysis.tests.test_engine_claims
python -m unittest analysis.tests.test_engine_claims.AnalyzeExtractionMappingTests
```

Postgres-gated tests need `CIVIC_TEST_DATABASE_URL` pointed at a throwaway
`postgres:17-alpine` with migrations applied; they skip cleanly when unset.
