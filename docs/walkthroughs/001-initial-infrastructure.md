# Civic Lens - Initial Infrastructure Walkthrough

The core infrastructure for Civic Lens has been established, implementing a strictly separated architecture for correctness (C++) and analysis (Python).

## 1. Directory Structure

- **/ingestion-cpp**: The deterministic crawler.
  - `src/main.cpp`: Entry point.
  - `src/canonicalizer.{hpp,cpp}`: Robust URL normalization.
  - `src/frontier.{hpp,cpp}`: SQLite-backed state machine (`QUEUED` -> `INFLIGHT` -> `DONE`).
  - `src/fetcher.{hpp,cpp}`: Libcurl wrapper with per-domain rate limiting.
  - `src/storage.{hpp,cpp}`: Content-addressed storage (SHA256).
  - `CMakeLists.txt`: Build configuration (requires `CURL` and `SQLite3`).

- **/analysis-python**: The AI and reporting layer.
  - `src/etl.py`: Pipeline to clean raw content into `docs` table.
  - `src/features.py`: Computes non-AI signals (Readability, Text Hash).
  - `src/ai.py`: Interfaces for Stance, Reach, and Sentiment classifiers.
  - `src/app.py`: Streamlit dashboard for Audit and Visualization.
  - `requirements.txt`: Python dependencies.

- **/data**: Shared state.
  - `schema.sql`: Strict SQLite schema enforcing invariants.
  - `raw/`: Directory for immutable raw content blobs.
  - `db/`: Directory for SQLite databases.

- **INVARIANTS.md**: The "Constitution" of the system, defining non-negotiable correctness properties.

## 2. How to Run

### C++ Ingestion
1.  **Dependencies**: Ensure `libcurl` and `sqlite3` are installed (e.g., via `vcpkg`).
2.  **Build**:
    ```bash
    cd ingestion-cpp
    mkdir build && cd build
    cmake ..
    cmake --build .
    ```
3.  **Run**: `./civic_lens_ingestion` (Starts the crawler/verifier).

### Python Analysis
1.  **Setup**:
    ```bash
    cd analysis-python
    python -m venv .venv
    .venv/Scripts/activate
    pip install -r requirements.txt
    ```
2.  **Run Dashboard**:
    ```bash
    streamlit run src/app.py
    ```
3.  **Run ETL**:
    ```bash
    python src/etl.py
    ```

## 3. Verification Checklist

The following invariants have been implemented in code:
- [x] **URL Canonicalization**: Implemented in `canonicalizer.cpp` using `libcurl`.
- [x] **Frontier Safety**: Implemented in `frontier.cpp` with atomic transactions.
- [x] **Raw Immutability**: Implemented in `storage.cpp` via SHA256 addressing.
- [x] **Traceability**: `schema.sql` enforces `raw_hash` links; `app.py` allows auditing them.

## Next Steps
- Configure the initial Seed list for the C++ crawler.
- Connect a real LLM model in `ai.py` (currently a prototype stub).
- Verify the build on your local machine.
