from pydantic_settings import BaseSettings, SettingsConfigDict
from typing import Optional

class Settings(BaseSettings):
    # App Config
    app_name: str = "Civic Lens Analysis"
    environment: str = "development"
    log_level: str = "INFO"
    
    # Database
    db_path: str = "data/civic_lens.db"
    
    # API Server
    api_host: str = "0.0.0.0"
    api_port: int = 8000
    
    # Analysis Config
    model_sentiment: str = "distilbert-base-uncased-finetuned-sst-2-english"
    clustering_threshold: float = 0.3
    
    # Gemini LLM Config
    gemini_api_key: str = ""
    gemini_model: str = "gemini-2.0-flash"
    gemini_temperature: float = 0.0
    llm_enabled: bool = False  # Feature flag for gradual rollout
    
    model_config = SettingsConfigDict(
        env_prefix="CIVIC_",
        env_file=".env",
        env_file_encoding="utf-8",
        extra="ignore"
    )

def get_settings() -> Settings:
    return Settings()
