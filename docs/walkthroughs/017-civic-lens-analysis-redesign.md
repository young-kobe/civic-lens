# Civic Lens Analysis Redesign - Walkthrough

## Changes Made

### Phase 1: Fix X Post Sentiment Classification

#### [sentiment.py](file:///c:/Users/kobey/civic-lens/analysis/src/reporting/aggregators/sentiment.py)

Removed redundant `'twitter'`, added `'x_post'` to `SOCIAL_PLATFORMS`:

```diff
-SOCIAL_PLATFORMS = frozenset(['reddit_post', 'reddit_comment', 'twitter', 'social'])
+SOCIAL_PLATFORMS = frozenset(['reddit_post', 'reddit_comment', 'social', 'x_post'])
```

**Impact**: X posts now correctly appear in the Social Media section of Public Sentiment.

---

### Phase 2: Hierarchy Schema

#### [004_hierarchy_tables.sql](file:///c:/Users/kobey/civic-lens/data/migrations/004_hierarchy_tables.sql) [NEW]

Two new tables:
- `author_profiles` - classifies entities as individual/group/organization with influence scores
- `engagement_metrics` - tracks engagement with weighted formula: `likes + (2 * retweets) + (1.5 * replies) + (1.5 * quotes)`

#### [db.go](file:///c:/Users/kobey/civic-lens/ingest/internal/storage/db/db.go)

Registered migration 4 in the migration list.

---

### Phase 3: Bot Detection Fix

#### [bot.py](file:///c:/Users/kobey/civic-lens/analysis/src/engine/bot.py)

Fixed `x_origin_confidence` bug - was never set (always `None`), so the foreign origin score boost never triggered:

```diff
 if country_code:
     x_foreign_origin_flag = country_code.upper() != "US"
+    x_origin_confidence = "high"  # Geotagged data is reliable
```

---

### Phase 4: Story Cluster Improvements

#### [story.py](file:///c:/Users/kobey/civic-lens/analysis/src/reporting/aggregators/story.py)

Major rewrite:
- Content type classification (`articles`, `social`, `mixed`) for each cluster
- Meaningful titles for social clusters (truncated post text instead of "Unnamed Cluster")
- Proper source mix mapping: `x_post` -> "X Posts" with type `social`
- Snippet extraction falls back to post text when metadata is empty
- Content type filtering: `get_stories(content_type='social')` returns only social clusters

#### [aggregator_models.py](file:///c:/Users/kobey/civic-lens/analysis/src/reporting/models/aggregator_models.py)

Added `contentType` field to `StoryCluster` dataclass.

---

### Phase 5: API + UI Updates

#### [server.py](file:///c:/Users/kobey/civic-lens/analysis/src/api/server.py)

Added `content_type` query param: `/api/stories?window=24h&content_type=social`

#### [types.ts](file:///c:/Users/kobey/civic-lens/ui/src/types.ts)

Added `ContentType` type and `contentType` field to `Cluster` interface.

#### [api.ts](file:///c:/Users/kobey/civic-lens/ui/src/services/api.ts)

Updated `fetchStories` to accept `ContentTypeFilter` parameter.

#### [transformers.ts](file:///c:/Users/kobey/civic-lens/ui/src/services/transformers.ts)

Added `contentType` mapping with `'mixed'` fallback for cached data.

#### [StoryClusters.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/StoryClusters.tsx)

- Added `ContentTypeTabs` component (All / Articles / Social Posts)
- Content type badges on cluster list items
- Contextual labels: "posts" for social clusters, "articles/posts" for mixed
- Cluster detail header shows content type badge
- Representative content section adapts title based on type

---

## Validation

- TypeScript build compiled with zero errors
- All files modified across full stack: Go, Python, TypeScript/React

## Next Steps

- Run `.\run.ps1 migrate` to apply the new migration
- Run `.\run.ps1 analyze` to reprocess data with fixes
- Verify UI in browser after starting dev server
