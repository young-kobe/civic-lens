# Analysis & UI Implementation Walkthrough

## Summary of Changes
I have implemented a modular analysis pipeline and a React-based frontend structure.

### 1. Database Schema
Updated `data/schema.sql`:
- Added `ai_outputs` table to store analysis results.
- Added `clusters` and `cluster_assignments` tables to support story grouping.
- Updated `docs` table to support `reddit_post` and `reddit_comment`.

### 2. Backend Modules (`analysis/src/`)
- **`engine/`**: Core logic.
    - `bot.py`: Rule-based bot detection (heuristic).
    - `sentiment.py`: Sentiment analysis (keyword-based prototype, ready for LLM).
    - `clustering.py`: TF-IDF + Cosine Similarity clustering.
- **`etl/`**: `loader.py` moves raw data to normalized `docs`.
- **`reporting/`**: `aggregators.py` generates outlet profiles.
- **`api/`**: `server.py` (FastAPI) exposes these features via REST.

### 3. Frontend (`ui/`)
- Created a **React + Vite** project structure.
- **`App.jsx`**: Main tabbed interface.
- **`StoriesList.jsx`**: Displays top story clusters.
- **`OutletProfile.jsx`**: Charts sentiment and bot flags by outlet.

## How to Run

### prerequisites
1.  **Python Dependencies**:
    ```bash
    pip install -r analysis/requirements.txt
    ```
2.  **Node.js** (Required for UI):
    Ensure you have Node.js installed.

### Running the Backend
1.  Navigate to `civic-lens` root.
2.  Start the server:
    ```bash
    uvicorn analysis.src.api.server:app --reload
    ```
    The API will be available at `http://localhost:8000`.

### Running the Frontend
1.  Navigate to `ui/`.
2.  Install dependencies:
    ```bash
    npm install
    ```
3.  Start the dev server:
    ```bash
    npm run dev
    ```
    Open `http://localhost:5173` in your browser.

## Verification Scenarios
- **Bot Detection**: The system currently flags comments with "buy now" or "click here" as suspicious/bot.
- **Clustering**: Similar texts ("apple banana" vs "apple banana") will be grouped together. 
- **Tests**:
    - `python analysis/tests/test_engines.py`: Unit tests for Bot, Sentiment, Clustering.
    - `python analysis/tests/test_api.py`: Integration tests for the API.
    - `python analysis/tests/test_workflow.py`: End-to-end workflow test.
