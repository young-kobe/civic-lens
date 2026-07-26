# Civic Lens Architecture and Data Flow

This document illustrates the data flow and architectural layers of the Civic Lens application, Postgres-era (post pg-redesign cutover).

Civic Lens measures **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** (embedding-based claim-cluster view) and a **partial citation overlay** between owned sources. See `CLAUDE.md` and `docs/audit-trail/` for the scoping rationale.

```mermaid
flowchart TD
    subgraph IngestionLayer ["Ingestion Layer (Go)"]
        Crawler(Web Crawler)
        RedditFetcher(Reddit API Fetcher)
        XFetcher(X/Twitter API Fetcher)
    end

    subgraph RawLayer ["Capture Layer (Postgres raw.* + content-addressed store)"]
        RawFiles["Raw Content Store<br>(CIVIC_RAW_STORE_DIR, sha256-addressed)"]
        RawSchema["raw.pages / raw.articles /<br>raw.reddit_posts / raw.x_posts / raw.x_users"]
    end

    subgraph EtlLayer ["ETL (Python)"]
        Documents(etl/documents.py)
    end

    subgraph CorpusLayer ["Normalized Corpus (Postgres corpus.*)"]
        Corpus["corpus.documents / corpus.authors /<br>corpus.entities (curated lean, never LLM-fed)"]
        TaskQueue["ops.task_queue<br>(FOR UPDATE SKIP LOCKED)"]
    end

    subgraph PipelineLayer ["Pipeline Stages (scheduler/pipeline.py + stages.py)"]
        BotEngine(bot_detection.py)
        TextEngine(text.py - sentiment only)
        TargetsEngine(targets.py - per-entity stance)
        PropagandaEngine(propaganda.py)
        CitationEngine(citations.py - deterministic)
        ClaimEngine(claims.py)
        NarrativeEngine(narrative_clustering.py - embedding only)
        LeanEngine(lean_derivation.py - deterministic)
        AccountTierEngine(account_tier.py - deterministic)
    end

    subgraph AnalysisLayer ["Results (Postgres analysis.*, results/store.py the only run-anchored writer)"]
        Runs["analysis.runs<br>(task, model_id, prompt_version_id, confidence, is_current)"]
        Results["sentiment_results / target_mentions /<br>propaganda_results+techniques / claims /<br>bot_signals / citations / narratives / author_leans"]
    end

    subgraph APILayer ["API Layer (FastAPI, strictly live)"]
        APIServer(server.py + api/queries/*)
    end

    subgraph UILayer ["UI Layer (React/TypeScript)"]
        Dashboard(React Frontend App)
    end

    Crawler -->|Raw HTML/metadata| RawFiles
    Crawler -->|Frontier state| RawSchema
    RedditFetcher -->|Posts/comments| RawSchema
    XFetcher -->|Tweets/users| RawSchema

    RawSchema -->|Read raw rows| Documents
    RawFiles -->|Extract clean text - Trafilatura| Documents
    Documents -->|Admit: 30-day sample or official_record| Corpus
    Documents -->|Seed per-doc work| TaskQueue

    TaskQueue -->|Claim work| BotEngine
    TaskQueue -->|Claim work| TextEngine
    TaskQueue -->|Claim work| TargetsEngine
    TaskQueue -->|Claim work| PropagandaEngine
    TaskQueue -->|Claim work| CitationEngine
    TaskQueue -->|Claim work| ClaimEngine
    Corpus -->|Read docs/authors/entities| BotEngine
    Corpus -->|Read docs| TextEngine
    Corpus -->|Read docs + corpus.entities.lean join| TargetsEngine
    Corpus -->|Read docs| PropagandaEngine
    Corpus -->|Read docs + x_posts referenced_tweet fields| CitationEngine
    Corpus -->|Read docs| ClaimEngine
    Corpus -->|Read author_profiles/entities| AccountTierEngine

    ClaimEngine -->|analysis.claims| NarrativeEngine

    BotEngine -->|via results/store.py| Runs
    TextEngine -->|via results/store.py| Runs
    TargetsEngine -->|via results/store.py| Runs
    PropagandaEngine -->|via results/store.py| Runs
    CitationEngine -->|via results/store.py| Runs
    ClaimEngine -->|via results/store.py| Runs
    Runs --> Results
    NarrativeEngine -->|direct write, batch job| Results
    TargetsEngine -->|target_mentions evidence| LeanEngine
    LeanEngine -->|direct write, full rebuild| Results
    AccountTierEngine -->|direct write| Corpus

    Results -->|Aggregate at request time| APIServer
    Corpus -->|Aggregate at request time| APIServer

    APIServer -->|JSON Responses via HTTP| Dashboard
    Dashboard -->|Admin: trigger pipeline stage| APIServer
```

## Data Flow Description

1. **Ingestion (Go)**: Crawlers fetch data concurrently using a job-queue model (the `raw.pages` frontier). Raw payloads are saved to the content-addressed store (`CIVIC_RAW_STORE_DIR`, SHA-256 keyed). Structured metadata (URLs, post ids, timestamps, engagement counts) is written to `raw.*` Postgres tables — a near-1:1 port of the old ingestion schema, deliberately not redesigned.
2. **ETL (Python)**: `etl/documents.py` reads `raw.*` rows, checks admission (ordinary docs need the ~30-day recency window; a tracked active official's X post is admitted regardless of age, labeled `admission_class = 'official_record'`), extracts clean text from the raw store (Trafilatura for news), and writes `corpus.documents` plus its subtype tables (`corpus.news_articles`/`reddit_posts`/`x_posts`). It also seeds one `ops.task_queue` row per doc per applicable stage.
3. **Pipeline (Python)**: `scheduler/pipeline.py` runs stages in `STAGE_ORDER` (`etl, account_tier, bot, text, targets, propaganda, citations, claims, bot_rollup, narratives, leans`); `scheduler/stages.py` claims `ops.task_queue` rows per stage with `FOR UPDATE SKIP LOCKED` and dispatches worker threads (`CIVIC_ANALYZE_CONCURRENCY`). Engines:
   - `bot_detection.py` — deterministic stylometric/account signal battery feeds an LLM call; a successful classification is `hybrid`.
   - `text.py` — sentiment only (favorability retired); a single LLM call per doc.
   - `targets.py` — per-entity stance (`target_mentions`), resolved against `corpus.entities`; this is where party stance now lives (joined to `corpus.entities.lean` at read time, never fed into the prompt).
   - `propaganda.py` — LLM-detected rhetorical techniques, evidence-validated.
   - `citations.py` — deterministic: URL mentions in text plus X reply/quote/retweet edges snapshotted onto `corpus.x_posts` at ETL time.
   - `claims.py` — LLM claim extraction with evidence-span verification; a claim whose span isn't a verbatim substring is dropped entirely.
   - `narrative_clustering.py` — embedding-only clustering of current claims into narratives; requires `CIVIC_NARRATIVE_EMBEDDING_MODEL` and fails loud if the backend can't embed.
   - `lean_derivation.py` — deterministic; pools directional `target_mentions` evidence into `analysis.author_leans`/`narrative_leans`, full rebuild per invocation.
   - `account_tier.py` — deterministic; classifies authors against the curated `corpus.entities` registry.
4. **Results (Postgres `analysis.*`)**: `results/store.py` is the sole writer of run-anchored typed result tables — every engine's `process()` opens a run, accumulates typed rows, and commits both in one transaction (`analysis.runs` plus its `sentiment_results`/`target_mentions`/etc.). `author_bot_scores` (rollup) and the narrative tables are the two documented exceptions, written directly by their computing module in a batch, not per-run.
5. **API (FastAPI, strictly live)**: `api/queries/` aggregates `corpus.*`/`analysis.*` directly at request time — there is no cache layer. `GET /snapshot-status` reads `ops.pipeline_runs` for freshness.
6. **UI (React)**: The React frontend queries the FastAPI endpoints under `/api/v1`.
