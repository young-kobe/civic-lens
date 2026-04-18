"""
Ollama LLM Client for Civic Lens Analysis.

Provides a client for local LLM inference via the Ollama REST API.
Designed for edge devices like Jetson Orin Nano.
"""

import json
import time
from typing import Any, Dict, Optional
import requests
from analysis.src.llm.base import BaseLLMClient
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class OllamaClient(BaseLLMClient):
    """
    Client for Ollama REST API (local LLM inference).
    
    Designed for running on Jetson Orin Nano or similar edge hardware.
    Communicates with the Ollama server over HTTP.
    
    Supports structured output via JSON schema in the format parameter.
    """
    
    def __init__(
        self,
        host: str = "http://localhost:11434",
        model: str = "llama3.2:3b",
        timeout: int = 60,
        max_retries: int = 1,
    ):
        super().__init__()
        self.host = host.rstrip("/")
        self.model = model
        self.timeout = timeout
        self.max_retries = max_retries
        self._available = self._check_availability()
    
    def _check_availability(self) -> bool:
        """Check if Ollama server is reachable."""
        try:
            response = requests.get(f"{self.host}/api/tags", timeout=5)
            if response.status_code == 200:
                logger.info(f"Ollama server available at {self.host}")
                return True
            return False
        except requests.exceptions.RequestException as e:
            logger.warning(f"Ollama server not reachable at {self.host}: {e}")
            return False
    
    @property
    def is_available(self) -> bool:
        """Check if the Ollama server is reachable."""
        return self._available
    
    def complete(
        self,
        system_prompt: str,
        user_prompt: str,
        response_schema: Optional[Dict[str, Any]] = None,
        temperature: Optional[float] = None,
    ) -> Dict[str, Any]:
        """
        Send a completion request to Ollama.
        
        Args:
            system_prompt: System instructions for the model
            user_prompt: User message/query
            response_schema: JSON schema for structured output (optional)
            temperature: Override default temperature (optional)
            
        Returns:
            Parsed JSON response as a dictionary
            
        Raises:
            RuntimeError: If server is not available
            ValueError: If response cannot be parsed as JSON
        """
        if not self.is_available:
            raise RuntimeError(f"Ollama server not available at {self.host}")
        
        full_prompt = f"{system_prompt}\n\n---\n\n{user_prompt}"
        
        # Use schema for structured output, fall back to basic JSON mode
        format_param = response_schema if response_schema else "json"
        
        last_error = None
        for attempt in range(self.max_retries):
            try:
                response = requests.post(
                    f"{self.host}/api/generate",
                    json={
                        "model": self.model,
                        "prompt": full_prompt,
                        "format": format_param,
                        "stream": False,
                        "options": {
                            "temperature": temperature if temperature is not None else 0.0,
                        },
                    },
                    timeout=self.timeout,
                )
                response.raise_for_status()
                
                result = response.json()
                
                if "eval_count" in result:
                    self.total_tokens_used += result.get("eval_count", 0)
                
                response_text = result.get("response", "")
                return self.parse_json_response(response_text, schema=response_schema)
                
            except requests.exceptions.Timeout:
                last_error = TimeoutError(f"Ollama request timed out after {self.timeout}s")
                logger.warning(f"Ollama timeout (attempt {attempt + 1}/{self.max_retries})")
            except requests.exceptions.RequestException as e:
                last_error = e
                logger.warning(
                    f"Ollama API call failed (attempt {attempt + 1}/{self.max_retries}): {e}"
                )
            except Exception as e:
                last_error = e
                logger.warning(
                    f"Ollama response parsing failed (attempt {attempt + 1}/{self.max_retries}): {e}"
                )
            
            if attempt < self.max_retries - 1:
                time.sleep(1)
        
        raise RuntimeError(f"Ollama API call failed after {self.max_retries} retries: {last_error}")
