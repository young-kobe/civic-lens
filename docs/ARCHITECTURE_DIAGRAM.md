# Civic Lens Architecture and Data Flow

This document illustrates the data flow and architectural layers of the Civic Lens application.

Civic Lens measures **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** (claim-cluster view) and a **partial citation overlay** between owned sources. The diagram below reflects the pipeline as of walkthrough 035.

```mermaid
flowchart TD
    %% Define Subgraphs for Layers
    subgraph IngestionLayer ["Ingestion Layer (Go)"]
        Crawler(Web Crawler - crawl.go)
        RedditFetcher(Reddit API Fetcher - reddit.go)
        XFetcher(X/Twitter API Fetcher - x.go)
    end

    subgraph DataLayer ["Data Persistence Layer (SQLite + Filesystem)"]
        RawFiles["(Raw Content Storage<br>File System hash-based)"]
        SQLiteDB["(SQLite Database<br>data/civic_lens.db)"]
    end

    subgraph AnalysisLayer ["Analysis & ETL Layer (Python)"]
        Loader(ETL Loader - loader.py)
        BotEngine(Bot Detection Engine)
        TextEngine(Sentiment + Favorability Analyzer)
        CitationEngine(Citation Extractor)
        ClaimEngine(Claim Extractor - LLM)
        NarrativeEngine(Narrative Clusterer)
    end

    subgraph APILayer ["API Layer (FastAPI)"]
        APIServer(FastAPI Server - server.py)
        Cache["(JSON File Cache<br>data/cache)"]
    end

    subgraph UILayer ["UI Layer (React/TypeScript)"]
        Dashboard(React Frontend App)
    end

    %% Ingestion Flow
    Crawler -->|Extracts HTML/Metadata| SQLiteDB
    Crawler -->|Saves Raw HTML| RawFiles
    RedditFetcher -->|Extracts Posts/Comments| SQLiteDB
    XFetcher -->|Extracts Tweets/Users| SQLiteDB

    %% ETL Flow
    SQLiteDB -->|Reads Raw Data| Loader
    RawFiles -->|Reads HTML Text| Loader
    Loader -->|Normalizes & Filters - 30 days/US Politics | SQLiteDB

    %% Analysis Flow
    SQLiteDB -->|Fetch Normalized Docs| BotEngine
    SQLiteDB -->|Fetch Normalized Docs| TextEngine
    SQLiteDB -->|Fetch Normalized Docs| CitationEngine
    SQLiteDB -->|Fetch Normalized Docs| ClaimEngine
    SQLiteDB -->|Read pending claims| NarrativeEngine

    BotEngine -->|ai_outputs (bot)| SQLiteDB
    TextEngine -->|ai_outputs (sentiment, favorability)| SQLiteDB
    CitationEngine -->|narrative_citations| SQLiteDB
    ClaimEngine -->|ai_outputs (claims)| SQLiteDB
    NarrativeEngine -->|narratives, narrative_docs| SQLiteDB

    %% API and Cache Flow
    SQLiteDB -->|On-demand/Aggregated Queries| APIServer
    APIServer -->|Reads/Writes Snapshots| Cache
    Cache -->|Serves Fast Aggregated Data| APIServer

    %% UI Flow
    APIServer -->|JSON Responses via HTTP| Dashboard
    Dashboard -->|User Triggers - Run ETL/Analysis| APIServer
```

## Data Flow Description

1. **Ingestion (Go):** The Go crawlers fetch data concurrently using a job-queue model (Frontier). The raw HTML payload is saved directly to the file system using a SHA-256 hash address for optimal storage (`data/raw/sha256/`). Structured metadata (URLs, post ids, timestamps) are pushed into the centralized SQLite database.
2. **Persistence (SQLite & FS):** The SQLite database relies on WAL mode (`_journal=WAL`) to allow concurrent reads while a single writer operates. 
3. **ETL (Python):** `loader.py` retrieves the raw db records, checks recency (last 30 days) and domain relevance (US politics constraints), fetches the corresponding raw HTML from the filesystem to extract clean text (using Trafilatura), and stores it into the unified `docs` table.
4. **Analysis (Python):** Python engines iteratively pull unprocessed `docs` through: (a) bot detection (heuristic + optional LLM); (b) unified sentiment + GOP favorability analyzer (LLM with heuristic fallback, evidence-span validated); (c) deterministic citation extraction (URL mentions + X quote/reply/retweet); (d) LLM claim extraction; (e) narrative clustering (lexical Jaccard default, embedding-mode opt-in). Results land in `ai_outputs`, `narratives`, `narrative_docs`, and `narrative_citations`.
5. **API & Aggregation:** FastAPI serves the aggregated reporting metrics. To avoid heavy recalculations during reads, it aggressively caches the JSON output into a `data/cache` filesystem structure for fast API serving.
6. **UI (React):** The React frontend queries the FastAPI endpoints.
