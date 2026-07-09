"""
LLM Client Factory for Civic Lens Analysis.

Provides factory functions to get the appropriate LLM client
based on configuration settings.
"""

from typing import Optional, Union
from analysis.src.llm.gemini import GeminiClient
from analysis.src.llm.ollama import OllamaClient
from analysis.src.llm.openai_compat import OpenAICompatClient
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)

# Singleton instances for shared use
_gemini_client_instance: Optional[GeminiClient] = None
_ollama_client_instance: Optional[OllamaClient] = None
_openai_compat_client_instance: Optional[OpenAICompatClient] = None


def get_gemini_client(
    api_key: Optional[str] = None,
    model: Optional[str] = None,
) -> GeminiClient:
    """
    Get or create the shared Gemini client instance.
    
    Args:
        api_key: API key (uses settings if not provided)
        model: Model name (uses settings if not provided)
        
    Returns:
        GeminiClient instance
    """
    global _gemini_client_instance
    
    if _gemini_client_instance is None:
        from analysis.src.common.settings import get_settings
        settings = get_settings()
        
        _gemini_client_instance = GeminiClient(
            api_key=api_key or settings.gemini_api_key,
            model=model or settings.gemini_model,
            temperature=settings.gemini_temperature,
        )
    
    return _gemini_client_instance


def get_ollama_client(
    host: Optional[str] = None,
    model: Optional[str] = None,
) -> OllamaClient:
    """
    Get or create the shared Ollama client instance.
    
    Args:
        host: Ollama server URL (uses settings if not provided)
        model: Model name (uses settings if not provided)
        
    Returns:
        OllamaClient instance
    """
    global _ollama_client_instance
    
    if _ollama_client_instance is None:
        from analysis.src.common.settings import get_settings
        settings = get_settings()
        
        _ollama_client_instance = OllamaClient(
            host=host or settings.ollama_host,
            model=model or settings.ollama_model,
            timeout=settings.ollama_timeout,
        )
    
    return _ollama_client_instance


def get_openai_compat_client() -> OpenAICompatClient:
    """
    Get or create the shared OpenAI-compatible client instance.

    Configured entirely from settings (CIVIC_LLM_BASE_URL, CIVIC_LLM_API_KEY,
    CIVIC_LLM_MODEL, CIVIC_LLM_EMBEDDING_MODEL, CIVIC_LLM_TIMEOUT).

    Returns:
        OpenAICompatClient instance
    """
    global _openai_compat_client_instance

    if _openai_compat_client_instance is None:
        from analysis.src.common.settings import get_settings
        settings = get_settings()

        _openai_compat_client_instance = OpenAICompatClient(
            base_url=settings.llm_base_url,
            api_key=settings.llm_api_key,
            model=settings.llm_model,
            embedding_model=settings.llm_embedding_model,
            timeout=settings.llm_timeout,
        )

    return _openai_compat_client_instance


def get_llm_client() -> Union[GeminiClient, OllamaClient, OpenAICompatClient]:
    """
    Get the configured LLM client based on settings.

    Reads CIVIC_LLM_BACKEND from settings to determine which backend to use:
    - "gemini": Use Google Gemini API (cloud)
    - "ollama": Use Ollama REST API (local inference)
    - "openai_compat": Any OpenAI-compatible /v1 server (LiteLLM, vLLM,
      serverless GPU endpoint, custom token router)

    Returns:
        GeminiClient, OllamaClient, or OpenAICompatClient instance
    """
    from analysis.src.common.settings import get_settings
    settings = get_settings()

    backend = settings.llm_backend.lower()
    if backend == "ollama":
        logger.info("Using Ollama backend for LLM inference")
        return get_ollama_client()
    elif backend == "openai_compat":
        logger.info("Using OpenAI-compatible backend for LLM inference")
        return get_openai_compat_client()
    else:
        logger.info("Using Gemini backend for LLM inference")
        return get_gemini_client()
