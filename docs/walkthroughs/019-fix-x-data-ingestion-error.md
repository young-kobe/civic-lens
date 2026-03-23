# Walkthrough: Fix X Data Ingestion Error

I have resolved the `sqlite3.IntegrityError: CHECK constraint failed` error that was preventing X (Twitter) data from being analyzed.

## Changes

### 1. Database Migration
I created a new migration file to update the `docs` table schema. This migration relaxes the `CHECK` constraint on the `source_type` column to allow `'x_post'`, which was previously rejected.

#### [003_allow_x_post_source.sql](file:///C:/Users/kobey/civic-lens/data/migrations/003_allow_x_post_source.sql)
```sql
-- Relax check constraint on docs.source_type to allow 'x_post'
...
CREATE TABLE IF NOT EXISTS docs_new (
    ...
    source_type TEXT NOT NULL CHECK(source_type IN ('news', 'reddit', 'reddit_post', 'reddit_comment', 'x_post')),
    ...
);
...
```

### 2. Go Ingestion Update
I updated the hardcoded migration list in `ingest/internal/storage/db/db.go` and rebuilt the `civic-ingest` binary to ensure the new migration file was picked up by the application.

#### [db.go](file:///C:/Users/kobey/civic-lens/ingest/internal/storage/db/db.go)
```diff
 	migrations := []struct {
 		Version  int
 		Filename string
 	}{
 		{1, "001_initial.sql"},
 		{2, "002_x_tables.sql"},
+		{3, "003_allow_x_post_source.sql"},
 	}
```

## Verification Results

### Automated Analysis Pipeline
I ran the analysis pipeline (`.\run.ps1 analyze`) and confirmed that it successfully passed the ETL step where it previously failed.

**Result:**
```
job_runner - INFO - Step 1/5: Running ETL...
analysis.src.etl.loader - INFO - ETL Loaded 445 new documents. Skipped: 87 old, 307 non-political.
job_runner - INFO - ETL complete: 445 new documents loaded
job_runner - INFO - Step 2/5: Running bot detection...
```

### Database Inspection
I verified via a python script that the database schema version is now `3` and the `docs` table definition includes `x_post`.

```
--- schema_version ---
(3, 1769448098)
...
--- docs table schema ---
CREATE TABLE "docs" (
    ...
    source_type TEXT NOT NULL CHECK(source_type IN ('news', ... 'x_post')),
    ...
)
```
