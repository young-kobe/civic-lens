# 024 - Sentiment, Caching, and UI Fixes Refactor

## 1. Overview
This update addresses several formatting, labeling, filtering, and caching inconsistencies across the `analysis` backend and `ui` frontend. These changes ensure accurate chart visualizations, reliable cache hits, and strict content type separation.

## 2. Backend Fixes (`analysis/src/`)

### Sentiment Aggregation (Mixed Mapping)
- **Issue**: The `MIXED` LLM label was causing holes in the topic/platform/time-window sentiment charts because the UI only expected `positive`, `negative`, and `neutral`. As a result, the sum of percentages did not match the total volume.
- **Fix**: Updated `analysis/src/reporting/aggregators/sentiment.py` to map the `MIXED` label directly into `neutral` within the aggregation buckets (`label_map = {"MIXED": "neutral"}`). This ensures donut charts and volume metrics reflect 100% of the sample perfectly, while preserving the raw `MIXED` label in the DB for accuracy.

### Story Clusters Filtering
- **Issue**: Content type filtering (`articles` vs `social`) previously allowed `mixed` clusters to leak through, polluting the UI tabs.
- **Fix**: Refactored `_format_story_clusters` in `analysis/src/reporting/aggregators/story.py` to evaluate strictly: `articles` filters only return pure `articles`, and `social` only returns pure `social`.

### Caching Pre-computation
- **Issue**: The `/api/stories` endpoint requested cache keys parameterized by content type (e.g., `stories_{window}_{content_type}`), but the background `save_snapshots()` in `job_runner.py` only pre-computed `stories_{window}`.
- **Fix**: Nested a `content_type` loop in `job_runner.py` to write explicitly pre-computed cache snapshots for `["all", "articles", "social"]`. Also added parameter validation to `/api/stories` in `server.py` to reject extraneous content queries (returning a 400 Bad Request if it's not one of those 3 values).

### Typed Configuration
- **Update**: Improved typing in `analysis/src/common/settings.py` by converting `run_analysis_on`'s type hint from `str` to `Literal["all", "social_media", "x"]`.

## 3. Frontend Fixes (`ui/src/`)

### Content Type Labels
- **Issue**: Any non-social cluster was summarily labeled `articles/posts` in the UI, which meant pure article clusters were mislabeled.
- **Fix**: Adjusted the UI logic in `ui/src/pages/StoryClusters.tsx` to conditionally render item counts as `articles`, `posts`, or `articles/posts` strictly matching the incoming `cluster.contentType`.

### Net Sentiment Visuals
- **Fix**: Appended a missing `%` suffix to the net sentiment score text display within `ui/src/pages/PublicSentiment.tsx` to clarify the metric unit.
