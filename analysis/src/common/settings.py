from pydantic_settings import BaseSettings, SettingsConfigDict
from typing import Optional, Literal

class Settings(BaseSettings):
    # App Config
    app_name: str = "Civic Lens Analysis"
    environment: str = "development"
    log_level: str = "INFO"
    
    # Database
    db_path: str = "data/civic_lens.db"
    
    # Cache for pre-computed analysis snapshots
    cache_dir: str = "data/cache"
    
    # API Server
    api_host: str = "0.0.0.0"
    api_port: int = 8000
    
    # Analysis Scope & Batching
    run_analysis_on: Literal["all", "social_media", "x"] = "social_media"
    loader_batch_size: int = 100
    
    # Gemini LLM Config
    gemini_api_key: str = ""
    gemini_model: str = "gemini-2.0-flash"
    gemini_temperature: float = 0.0
    llm_enabled: bool = True  # LLM is primary classifier; heuristics are supplemental
    
    # LLM Backend Selection: "gemini" or "ollama"
    llm_backend: str = "ollama"
    
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
    # Path is relative to the repo root; resolved in job_runner.
    known_accounts_yaml: str = "data/known_political_x_accounts.yaml"
    account_classifier_llm_enabled: bool = True

    model_config = SettingsConfigDict(
        env_prefix="CIVIC_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore",
        protected_namespaces=()  # Disable protected namespace check for 'model_' prefix
    )

def get_settings() -> Settings:
    return Settings()
