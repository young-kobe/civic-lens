# Dashboard Fixes Walkthrough

## Changes Made

### 1. GOP Favorability Error - Fixed

The `SourceComparison` component was crashing with "Cannot read properties of undefined (reading 'favorable')" because of a type mismatch.

**Files modified:**
- [types.ts](file:///c:/Users/kobey/civic-lens/ui/src/types.ts) - Simplified `PollingSocialComparison` to only include `polling` and `social` fields
- [transformers.ts](file:///c:/Users/kobey/civic-lens/ui/src/services/transformers.ts) - Fixed mapping from API response (`onlineSentiment`/`pollingData`) to frontend types (`polling`/`social`)

render_diffs(file:///c:/Users/kobey/civic-lens/ui/src/types.ts)

---

### 2. Timeframe Filtering - Fixed

Time filter buttons (24h, 7d, 30d, 90d) were not working because pages were not passing the filter to API calls.

**Files modified:**
- [StoryClusters.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/StoryClusters.tsx) - Pass `filters.timeRange` to `fetchStories()`
- [PublicSentiment.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/PublicSentiment.tsx) - Pass `filters.timeRange` to `fetchSentiment()`
- [GOPFavorability.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/GOPFavorability.tsx) - Pass `filters.timeRange` to `fetchFavorability()`

---

### 3. Source Mix Grey Square - Fixed

The StackedBar chart was displaying grey because source types from the API (`news_article`, `reddit_post`) did not match the color mapping keys (`news`, `reddit`, `social`, `other`).

**File modified:**
- [story.py](file:///c:/Users/kobey/civic-lens/analysis/src/reporting/aggregators/story.py) - Added type normalization in `_format_source_mix()`

render_diffs(file:///c:/Users/kobey/civic-lens/analysis/src/reporting/aggregators/story.py)

---

## Not Bugs

**98% Neutral Sentiment**: This likely reflects actual database state - either documents have not been analyzed, or the analyzed content is genuinely neutral.

**Bot Activity Profiler Sparse Data**: Bot detection is designed primarily for social media (Reddit/X). With mostly news article data, bot detection flags are expected to be minimal.

---

## Test Results

All 4 aggregator tests pass:

```
tests/test_rich_aggregators.py::TestRichAggregators::test_get_stories_rich PASSED
tests/test_rich_aggregators.py::TestRichAggregators::test_get_public_sentiment_rich PASSED
tests/test_rich_aggregators.py::TestRichAggregators::test_get_gop_favorability_rich PASSED
tests/test_rich_aggregators.py::TestRichAggregators::test_get_outlet_profiles_rich PASSED
```

---

## Manual Verification

Start both servers and test in browser:

```powershell
# Terminal 1: API server
cd c:\Users\kobey\civic-lens\analysis
.\.venv\Scripts\activate
python -m analysis.src.api.server

# Terminal 2: Frontend
cd c:\Users\kobey\civic-lens\ui
npm run dev
```

Then open `http://localhost:5173` and verify:
1. **GOP Favorability** loads without error
2. **Time filters** update data when clicked
3. **Source Mix** shows colored bars (not grey)
