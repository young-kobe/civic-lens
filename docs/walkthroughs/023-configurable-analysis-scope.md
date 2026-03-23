# Configurable Analysis Scope updates

## Overview
Added the ability to dynamically configure which source platforms are processed during the analysis stages (Bot Detection, Sentiment Analysis, Favorability), as well as a configurable batch size for document loading.

## Changes Made
- **analysis/src/common/settings.py**: 
  - Added `run_analysis_on` (default: `"social_media"`, alternatives: `"x"`, `"all"`).
  - Added `loader_batch_size` (default: `500`).
- **analysis/src/etl/loader.py**:
  - Updated `get_unprocessed_docs()` to accept a list of `source_types` instead of a singular `source_type`.
  - Used an SQL `IN` clause to map the Python list into parameterized queries to filter by multiple platforms at once.
  - Added `batch_size` parameter mapped directly from the configuration.
- **analysis/src/scheduler/job_runner.py**:
  - Replaced hardcoded limits (e.g., social media only / X posts only) with a `_get_target_source_types()` helper.
  - Passed `self.settings.loader_batch_size` directly to the `get_unprocessed_docs()` calls inside `run_bot_detection`, `run_sentiment_analysis`, and `run_favorability_analysis`.

## Validation
- Changes ensure no SQL syntax errors when empty arrays or `None` are passed.
- Config-driven execution allows for easier testability and local development overrides without branching code paths.
