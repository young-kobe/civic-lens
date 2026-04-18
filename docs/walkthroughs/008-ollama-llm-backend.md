# Ollama LLM Backend Integration - Walkthrough

## Summary

Added Ollama as an alternative LLM backend for local inference on the Jetson Orin Nano, eliminating dependency on Gemini API quota during testing.

## Changes Made

### New Package: `analysis/src/llm/`

| File | Purpose |
|------|---------|
| [base.py](file:///c:/Users/kobey/civic-lens/analysis/src/llm/base.py) | Abstract `BaseLLMClient` class |
| [gemini.py](file:///c:/Users/kobey/civic-lens/analysis/src/llm/gemini.py) | `GeminiClient` for Google API |
| [ollama.py](file:///c:/Users/kobey/civic-lens/analysis/src/llm/ollama.py) | `OllamaClient` for local inference |
| [factory.py](file:///c:/Users/kobey/civic-lens/analysis/src/llm/factory.py) | `get_llm_client()` factory function |

### Configuration

[settings.py](file:///c:/Users/kobey/civic-lens/analysis/src/common/settings.py) - Added:
- `llm_backend`: `"gemini"` or `"ollama"`
- `ollama_host`: Server URL (default `http://localhost:11434`)
- `ollama_model`: Model name (default `llama3.2:3b`)
- `ollama_timeout`: Request timeout in seconds

### Documentation

- [.env.example](file:///c:/Users/kobey/civic-lens/.env.example) - Environment configuration template
- [OLLAMA_SETUP.md](file:///c:/Users/kobey/civic-lens/docs/OLLAMA_SETUP.md) - Orin Nano setup guide

## Usage

To switch to Ollama for testing, set in `.env`:

```env
CIVIC_LLM_BACKEND=ollama
CIVIC_LLM_ENABLED=true
CIVIC_OLLAMA_HOST=http://192.168.1.XX:11434
CIVIC_OLLAMA_MODEL=llama3.2:3b
```

## Verification

- All imports verified working
- Existing tests pass
- Class inheritance verified: `OllamaClient -> BaseLLMClient -> ABC`

## Next Steps

1. Install Ollama on Orin Nano (see `docs/OLLAMA_SETUP.md`)
2. Pull a model: `ollama pull llama3.2:3b`
3. Configure network access
4. Update `.env` with Nano's IP address
5. Test with: `python -m analysis.src.main`
