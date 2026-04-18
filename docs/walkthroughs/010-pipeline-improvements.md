# Pipeline Improvements Walkthrough

## Changes Made

### 1. Timestamp Filtering (30 Days)
**Frontier-level filter** in [ingest.go](file:///c:/Users/kobey/civic-lens/ingest/internal/runner/ingest.go):
- RSS items older than 30 days are skipped at ingestion time
- Prevents stale content from entering the database

**ETL-level validation** in [loader.py](file:///c:/Users/kobey/civic-lens/analysis/src/etl/loader.py):
- Double-checks `published_at` timestamps
- Rejects dates before 2020 or in the future

---

### 2. US Political Content Filter
Added to [loader.py](file:///c:/Users/kobey/civic-lens/analysis/src/etl/loader.py):
- 40+ political keywords (congress, senate, president, republican, democrat, etc.)
- Excludes sports, music, entertainment URLs
- Only US federal/state political content passes through

---

### 3. Batch Size Increase
Changed in [loader.py](file:///c:/Users/kobey/civic-lens/analysis/src/etl/loader.py:212):
- `LIMIT 100` -> `LIMIT 500`
- Safe because heuristics are free; LLM only used for edge cases

---

### 4. Reddit Ingestion Fixed
**Root cause**: Command was never run (not a code bug)
**Result**: 225 posts ingested from 9 subreddits

---

## Verification Results

Pipeline run with new filters:
```
ETL Loaded 132 new documents.
Skipped: 21 old, 72 non-political.
```

| Metric | Before | After |
|--------|--------|-------|
| Docs loaded | 818 | 132 |
| Old content skipped | 0 | 21 |
| Non-political skipped | 0 | 72 |
| Reddit posts | 0 | 225 |

---

## Quota Issue

> [!WARNING]
> Gemini Flash free tier quota exhausted during testing. Analysis couldn't complete.

**Options**:
1. Wait for quota reset (daily limit)
2. Run with `CIVIC_LLM_ENABLED=false` to use heuristics only
3. Upgrade to paid Gemini tier

---

## Files Modified

| File | Change |
|------|--------|
| [ingest.go](file:///c:/Users/kobey/civic-lens/ingest/internal/runner/ingest.go) | 30-day frontier filter |
| [loader.py](file:///c:/Users/kobey/civic-lens/analysis/src/etl/loader.py) | Political filter + batch size |
| civic-ingest.exe | Rebuilt with new filter |
