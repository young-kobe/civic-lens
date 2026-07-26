from pydantic_settings import BaseSettings, SettingsConfigDict
from typing import Literal

class Settings(BaseSettings):
    # App Config
    app_name: str = "Civic Lens Analysis"
    environment: str = "development"
    log_level: str = "INFO"

    # Postgres. Empty by default; analysis/src/common/db.py refuses to guess
    # a DSN.
    database_url: str = ""
    # ConnectionPool max. Keep above analyze_concurrency: workers borrow a
    # connection per doc. Server-side max_connections=30.
    pg_pool_max: int = 12
    # Worker threads per stage (scheduler/stages.py::run_queue_stage). One
    # in-flight LLM call each; Gemini caps remote calls around 10.
    analyze_concurrency: int = 10

    # Content-addressed raw HTML store written by the Go ingestor. Resolved
    # relative to the working directory — repo root in dev,
    # /var/lib/civic-lens in the prod containers (compose working_dir).
    # Must NOT be resolved from the code tree: the analysis image excludes
    # data/raw (.dockerignore), so a repo-root-relative default silently
    # breaks all news text extraction in production.
    raw_store_dir: str = "data/raw/sha256"

    # API Server
    api_host: str = "0.0.0.0"
    api_port: int = 8000
    # Minimum seconds between pipeline-trigger requests per endpoint. Clients
    # that call /api/run/* faster than this get a 429.
    pipeline_trigger_cooldown_seconds: int = 60

    # Shared-secret that gates every admin endpoint. Clients must send it in
    # the X-Admin-Token header. If this is empty the server returns 503 on
    # all gated endpoints — loud over silently-permissive.
    admin_token: str = ""

    # Which source_types the LLM stages analyze.
    run_analysis_on: Literal["all", "social_media", "x"] = "social_media"

    # Gemini LLM Config
    gemini_api_key: str = ""
    gemini_model: str = "gemini-3.5-flash"
    gemini_temperature: float = 0.0

    # LLM Backend Selection: "gemini", "ollama", or "openai_compat"
    llm_backend: str = "ollama"

    # OpenAI-compatible backend (CIVIC_LLM_BACKEND=openai_compat). Any /v1
    # server slots in — LiteLLM, vLLM, a serverless GPU endpoint — with no
    # engine code changes. Unused unless llm_backend selects it.
    llm_base_url: str = ""
    llm_api_key: str = ""
    llm_model: str = ""
    llm_embedding_model: str = ""
    llm_timeout: int = 120

    # Ollama Config (local inference)
    ollama_host: str = "http://localhost:11434"
    ollama_model: str = "qwen2.5:3b"
    ollama_timeout: int = 120

    # Narrative clustering is embedding-only and REQUIRES this. No default:
    # a wrong name would be a backend mismatch (an Ollama tag reaching
    # Gemini), and a blank one leaves clustering_runs unable to say which
    # model made its vectors. run() refuses to start on blank.
    narrative_embedding_model: str = ""
    # Cosine match threshold. Tuned for nomic-embed-text and NOT portable
    # across embedding models -- recheck on a real claim sample after a swap.
    narrative_embedding_threshold: float = 0.65

    # Minimum run confidence for a row to count in aggregations. Below this
    # the row is dropped from sentiment / narrative aggregates, and bot flags
    # below it don't cause exclusion from sentiment. 0.0 disables filtering.
    aggregation_min_confidence: float = 0.5

    model_config = SettingsConfigDict(
        env_prefix="CIVIC_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        protected_namespaces=()  # Disable protected namespace check for 'model_' prefix
    )

def get_settings() -> Settings:
    return Settings()
