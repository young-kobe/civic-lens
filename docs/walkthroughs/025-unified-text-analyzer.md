# 025: Unified Text Analyzer

## Objective
Optimize the LLM processing pipeline by merging the sentiment and favorability analyses into a single inference pass. This effectively cuts the LLM inference execution time and load in half without losing the nuanced distinction between generic textual sentiment and entity-specific favorability metrics.

## Key Changes

1. **`Analyzer` Refactor (`analysis/src/engine/analyzer.py`)**
   - Deprecated the independent `HybridSentimentAnalyzer` and `FavorabilityAnalyzer` classes.
   - Built a single unified `Analyzer` class. This class compiles all deterministic keyword heuristics (both positive/negative sentiment words and proxy keywords near GOP entities) into a unified context payload passed to the local LLM.
   - It parses the single combined LLM response block into independent `SentimentResult` and `FavorabilityResult` python dataclasses to abstract away the unification from upstream aggregators.

2. **Prompt and Schema Consolidation (`analysis/src/llm/schemas.py`, `analysis/src/engine/prompts.py`)**
   - Merged `SENTIMENT_SCHEMA` and `FAVORABILITY_SCHEMA` under `TEXT_ANALYSIS_SCHEMA`, pulling all requisite arrays and values into one definition.
   - Revised the overarching `TEXT_ANALYSIS_SYSTEM_PROMPT` to explicitly teach the model the semantic difference between textual "Sentiment" (tone of the writing; e.g. "it is horrible that politician X exists" is negative) vs "Favorability" (the stance toward a specific entity; e.g. "politician X is unfairly attacked" is negative sentiment but favorable toward X).

3. **Pipeline Orchestrator Updates (`analysis/src/scheduler/job_runner.py`)**
   - Refactored `run_sentiment_analysis()` and `run_favorability_analysis()` into a singular `run_text_analysis()`.
   - The unified analyzer processes unprocessed documents in a single loop efficiently, unpacking the payload and passing the component values to `self.loader.save_ai_output()` for both `'sentiment'` and `'favorability'` explicitly using the common `TEXT_ANALYSIS_PROMPT_VERSION`. 

## Validation
Confirmed via execution of the Python analysis pipeline (`.\run.ps1 analyze`). The unified code runs properly and populates both databases explicitly.
