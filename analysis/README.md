# Civic Lens - Analysis Module

This module handles the extraction, transformation, and AI analysis of news and social media content.

## Architecture

The analysis pipeline is modularized into the following components:

-   **ETL (`src/etl/`)**: Loads raw data from SQLite, normalizes it into `docs`, and handles deduplication.
-   **Engine (`src/engine/`)**: Core analysis logic.
    -   `bot.py`: Detects bot-like patterns (heuristic).
    -   `sentiment.py`: Analyzes text sentiment.
    -   `clustering.py`: Groups stories using TF-IDF and Cosine Similarity.
-   **Reporting (`src/reporting/`)**: Aggregates results into outlet profiles and story summaries.
-   **API (`src/api/`)**: FastAPI server exposing endpoints for the UI.

## Workflows

### 1. Setup
Install dependencies:
```bash
pip install -r requirements.txt
```

### 2. Running the Backend
The entry point is `src/main.py`, which starts the FastAPI server (Uvicorn).
```bash
python src/main.py
```
Server runs at `http://localhost:8000`.

### 3. API Usage
-   **Trigger Analysis**: `POST /api/run/analysis` (Queues bot/sentiment tasks)
-   **Trigger Clustering**: `POST /api/run/clustering`
-   **Get Stories**: `GET /api/stories`
-   **Get Profiles**: `GET /api/profiles`

### 4. Testing
Run the unit and integration tests:
```bash
# Unit tests for core logic
python analysis/tests/test_engines.py

# Integration tests for API
python analysis/tests/test_api.py

# Full workflow test against local DB
python analysis/tests/test_workflow.py
```
