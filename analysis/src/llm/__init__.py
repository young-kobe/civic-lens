"""
LLM Package for Civic Lens Analysis.

This package provides LLM client implementations for various backends:
- Gemini (Google Cloud API)
- Ollama (local inference)

Usage:
    from analysis.src.llm import get_llm_client
    
    client = get_llm_client()
    response = client.complete(system_prompt, user_prompt)
"""

from analysis.src.llm.base import BaseLLMClient
from analysis.src.llm.gemini import GeminiClient
from analysis.src.llm.ollama import OllamaClient
from analysis.src.llm.factory import (
    get_llm_client,
    get_gemini_client,
    get_ollama_client,
)

__all__ = [
    "BaseLLMClient",
    "GeminiClient",
    "OllamaClient",
    "get_llm_client",
    "get_gemini_client",
    "get_ollama_client",
]
