# LLM Integration Walkthrough

Integrated Gemini Flash API into the Civic Lens analysis backend using a hybrid approach: deterministic algorithms compute measurable signals while the LLM handles taxonomy labeling and interpretation.

## Changes Made

### New Files

| File | Purpose |
|------|---------|
| [llm_client.py](file:///c:/Users/kobey/civic-lens/analysis/src/common/llm_client.py) | Gemini API client with retry logic and JSON parsing |
| [favorability.py](file:///c:/Users/kobey/civic-lens/analysis/src/engine/favorability.py) | GOP favorability analyzer with entity extraction |
| [test_llm_engines.py](file:///c:/Users/kobey/civic-lens/analysis/tests/test_llm_engines.py) | 19 tests for hybrid engines |

### Modified Files

| File | Changes |
|------|---------|
| [settings.py](file:///c:/Users/kobey/civic-lens/analysis/src/common/settings.py) | Added Gemini config: `gemini_api_key`, `gemini_model`, `llm_enabled` |
| [sentiment.py](file:///c:/Users/kobey/civic-lens/analysis/src/engine/sentiment.py) | Replaced with `HybridSentimentAnalyzer` |
| [bot.py](file:///c:/Users/kobey/civic-lens/analysis/src/engine/bot.py) | Replaced with `HybridBotDetector` |
| [aggregators.py](file:///c:/Users/kobey/civic-lens/analysis/src/reporting/aggregators.py) | Added bot filtering + `get_public_sentiment()`, `get_gop_favorability()` |
| [server.py](file:///c:/Users/kobey/civic-lens/analysis/src/api/server.py) | New endpoints: `/api/sentiment`, `/api/favorability` |
| [requirements.txt](file:///c:/Users/kobey/civic-lens/analysis/requirements.txt) | Added `google-generativeai>=0.8.0` |

---

## API Endpoints

| Endpoint | Description | Bot Filtered |
|----------|-------------|--------------|
| `GET /api/sentiment` | Aggregated public sentiment | Yes |
| `GET /api/favorability` | GOP favorability metrics | Yes |
| `GET /api/stories` | Story clusters | Yes |
| `GET /api/profiles` | Outlet profiles | No (for transparency) |

---

## LLM Prompts

All prompts are embedded in engine classes with structured JSON output schemas:

- **Sentiment**: `HybridSentimentAnalyzer.SYSTEM_PROMPT` - classifies POSITIVE/NEGATIVE/NEUTRAL/MIXED
- **Bot Detection**: `HybridBotDetector.SYSTEM_PROMPT` - classifies human/bot/suspicious  
- **Favorability**: `FavorabilityAnalyzer.SYSTEM_PROMPT` - classifies favorable/unfavorable/neutral/mixed

---

## Enabling LLM

Set environment variables:
```powershell
$env:CIVIC_GEMINI_API_KEY = "your-api-key"
$env:CIVIC_LLM_ENABLED = "true"
```

Or add to `.env` file in project root.

---

## Test Results

```
Ran 22 tests in 0.004s
OK
```

All engines work with deterministic fallback when LLM is disabled.
