from pydantic_settings import BaseSettings, SettingsConfigDict
from typing import Optional, Literal

class Settings(BaseSettings):
    # App Config
    app_name: str = "Civic Lens Analysis"
    environment: str = "development"
    log_level: str = "INFO"
    
    # Database
    db_path: str = "data/civic_lens.db"
    # Postgres redesign (Phase 1, plumbing only — nothing reads this yet).
    # Empty by default; analysis/src/common/db.py refuses to guess a DSN.
    database_url: str = ""
    # Postgres ConnectionPool max. Keep above analyze_concurrency: workers
    # borrow a connection per doc. Server-side max_connections=30.
    pg_pool_max: int = 12
    # Worker threads per stage (scheduler/stages.py::run_queue_stage). One
    # in-flight LLM call each; Gemini caps remote calls around 10.
    analyze_concurrency: int = 10
    
    # Cache for pre-computed analysis snapshots
    cache_dir: str = "data/cache"

    # Content-addressed raw HTML store written by the Go ingestor. Resolved
    # relative to the working directory like db_path/cache_dir — repo root in
    # dev, /var/lib/civic-lens in the prod containers (compose working_dir).
    # Must NOT be resolved from the code tree: the analysis image excludes
    # data/raw (.dockerignore), so a repo-root-relative default silently
    # breaks all news text extraction in production.
    raw_store_dir: str = "data/raw/sha256"
    
    # API Server
    api_host: str = "0.0.0.0"
    api_port: int = 8000
    # Emit a warning — but still serve — when a cached snapshot is older than
    # this. The expected cadence of save_snapshots() is at least daily, so a
    # day-old cache means the pipeline has silently stopped running.
    stale_cache_warn_seconds: int = 24 * 60 * 60
    # Minimum seconds between pipeline-trigger requests per endpoint. Clients
    # that call /api/run/* faster than this get a 429. Prevents pile-up of
    # background tasks from a misbehaving client (audit 2026-04-19 §3).
    pipeline_trigger_cooldown_seconds: int = 60

    # Shared-secret that gates every admin endpoint (/api/run/*, /api/review/*,
    # /api/cache-status). Clients must send it in the X-Admin-Token header. If
    # this is empty the server returns 503 on all gated endpoints — the
    # misconfiguration is intentional (loud over silently-permissive).
    admin_token: str = ""
    
    # Analysis Scope & Batching
    run_analysis_on: Literal["all", "social_media", "x"] = "social_media"
    # Per-run ceiling on unscored docs handed to each pipeline stage.
    # Upper bound on per-stage LLM spend per cron fire (4×/day analyze).
    # Was 500; dropped to 200 so a single run can't blow through the $15/mo
    # Gemini budget even if the backlog spikes. Override via
    # CIVIC_LOADER_BATCH_SIZE if you intentionally want to catch up a backlog.
    loader_batch_size: int = 200
    
    # Gemini LLM Config
    gemini_api_key: str = ""
    # 3.5-flash is the current cheap+fast tier (2.0-flash is EOL; 2.5-flash
    # superseded). Still a few cents per pipeline run at our volume.
    # Override via CIVIC_GEMINI_MODEL if pricing or capability changes again.
    gemini_model: str = "gemini-3.5-flash"
    gemini_temperature: float = 0.0
    llm_enabled: bool = True  # LLM is primary classifier; heuristics are supplemental
    # Max concurrent LLM calls per analysis stage. The stages are network-bound
    # (~10s/call), so a bounded thread pool cuts wall-clock roughly linearly.
    # DB writes stay serial regardless — see job_runner._map_llm_concurrent.
    # Keep at/under the backend's rate limit (Gemini AFC caps remote calls ~10).
    llm_concurrency: int = 5

    # LLM Backend Selection: "gemini", "ollama", or "openai_compat"
    llm_backend: str = "ollama"

    # OpenAI-compatible backend (CIVIC_LLM_BACKEND=openai_compat). Speaks the
    # /v1 REST surface, so any OpenAI-compatible server slots in behind it —
    # LiteLLM, vLLM, a serverless GPU endpoint, or a custom token router —
    # with no engine code changes. Unused unless llm_backend selects it.
    llm_base_url: str = ""
    llm_api_key: str = ""
    llm_model: str = ""
    llm_embedding_model: str = ""
    llm_timeout: int = 120

    # Ollama Config (for local LLM on Orin Nano or other local inference)
    ollama_host: str = "http://localhost:11434"
    # ollama_model: str = "qwen2.5:0.5b"
    ollama_model: str = "qwen2.5:3b"
    ollama_timeout: int = 120  # seconds (local inference is slower)
    
    # Polling Data Config
    polling_enabled: bool = True  # Feature flag for live polling
    polling_cache_ttl: int = 3600  # Cache TTL in seconds (1 hour)

    # Narrative clustering: "embedding" (semantic, default) or "jaccard" (lexical).
    # Embedding mode needs Ollama + nomic-embed-text; the clusterer transparently
    # falls back to Jaccard per-claim when the embedding call fails so the
    # default change is safe for installs without Ollama (walkthrough 039).
    narrative_similarity_mode: Literal["jaccard", "embedding"] = "embedding"
    narrative_embedding_model: str = "nomic-embed-text"
    # Match threshold per mode — Jaccard ~0.3, embedding cosine ~0.65 for nomic-embed-text.
    narrative_jaccard_threshold: float = 0.3
    narrative_embedding_threshold: float = 0.65

    # Minimum ai_outputs.confidence for a row to count in aggregations (walkthrough 039).
    # Below this the row is dropped from sentiment / geo / narrative aggregates, and
    # bot flags below this don't cause exclusion from sentiment. Set to 0.0 to
    # disable filtering entirely.
    aggregation_min_confidence: float = 0.5

    # Account tier classification (walkthrough 036).
    # Path is relative to the repo root; resolved in job_runner. Loader is
    # curated-only — the LLM-driven tier classifier was removed on 2026-04-25
    # in favour of the verified_officials.yaml + curated-YAML pair.
    known_accounts_yaml: str = "data/known_political_x_accounts.yaml"

    model_config = SettingsConfigDict(
        env_prefix="CIVIC_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        protected_namespaces=()  # Disable protected namespace check for 'model_' prefix
    )

def get_settings() -> Settings:
    return Settings()
