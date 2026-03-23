# LLM Reasoning & Sentiment Visual Refactor - Walkthrough

## Summary

Surfaced LLM classification reasoning through the full stack and replaced the flat-bar "Sentiment by Topic" visualization with an elegant, data-rich card layout featuring donut charts, net score badges, sarcasm indicators, and expandable reasoning panels.

## Changes Made

### Backend: Sarcasm Detection & Reasoning Pipeline

**Problem**: The LLM already produced `reasoning` and `evidence_spans`, but they were discarded by the aggregator.

| File | Change |
|------|--------|
| [prompts.py](file:///c:/Users/kobey/civic-lens/analysis/src/engine/prompts.py) | Added rule 5 (sarcasm detection with examples), made reasoning mandatory |
| [schemas.py](file:///c:/Users/kobey/civic-lens/analysis/src/llm/schemas.py) | Added `sarcasm_detected` boolean, made `reasoning` required |
| [engine_models.py](file:///c:/Users/kobey/civic-lens/analysis/src/engine/models/engine_models.py) | Added `sarcasm_detected: bool = False` to `SentimentResult` |
| [sentiment.py (engine)](file:///c:/Users/kobey/civic-lens/analysis/src/engine/sentiment.py) | Wired `sarcasm_detected` through `_llm_classify()` |
| [aggregator_models.py](file:///c:/Users/kobey/civic-lens/analysis/src/reporting/models/aggregator_models.py) | Added `ClassificationSample` dataclass, extended `TopicSentiment` with `sarcasm_rate` and `classification_samples` |
| [sentiment.py (aggregator)](file:///c:/Users/kobey/civic-lens/analysis/src/reporting/aggregators/sentiment.py) | Added `_collect_topic_sample()`, updated `_format_topic_sentiment()` to attach top-5 classification samples per topic |

---

### Frontend: Types, Transformer, and Visual Refactor

| File | Change |
|------|--------|
| [types.ts](file:///c:/Users/kobey/civic-lens/ui/src/types.ts) | Added `ClassificationSample` interface, extended `SentimentBreakdown` |
| [transformers.ts](file:///c:/Users/kobey/civic-lens/ui/src/services/transformers.ts) | Pass through `classificationSamples` and `sarcasm_rate` |
| [PublicSentiment.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/PublicSentiment.tsx) | New components: `MiniDonut`, `TopicRow`, `ClassificationSampleCard`, `TopicSentimentCard` |

**New UI design** replaces flat bars with:
- CSS-only conic-gradient donut charts per topic
- Color-coded net sentiment badges
- Sarcasm rate indicators (amber pills)
- Expandable reasoning panels with label badges, confidence %, evidence spans

---

### Test Fixes

| File | Fix |
|------|-----|
| [test_engines.py](file:///c:/Users/kobey/civic-lens/analysis/tests/test_engines.py) | Updated imports to `HybridBotDetector`/`HybridSentimentAnalyzer` |
| [test_rich_aggregators.py](file:///c:/Users/kobey/civic-lens/analysis/tests/test_rich_aggregators.py) | Fixed DB schema, source_type values, bot doc type; added 2 new tests for `classificationSamples` and favorability merging |

---

## Verification

### Test Results

```
8 passed, 0 failed
```

- `test_bot_detector` - PASSED
- `test_sentiment_analyzer` - PASSED
- `test_clustering` - PASSED
- `test_get_stories_rich` - PASSED
- `test_get_public_sentiment_rich` - PASSED
- `test_sentiment_has_topic_classification_samples` - PASSED (NEW)
- `test_sentiment_favorability_merged` - PASSED (NEW)
- `test_get_outlet_profiles_rich` - PASSED

### TypeScript Compilation

`npx tsc --noEmit` - no errors.
