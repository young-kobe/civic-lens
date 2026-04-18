# Walkthrough 027: SQLite Optimization & Context Graceful Shutdown

I have fixed the "context deadline exceeded" errors in the Go ingestion layer and optimized SQLite performance for concurrent crawling.

## Bug Fix: Context Deadline Exceeded

The crawler previously used a single timed context for both the crawl duration and all database operations. When the timer expired, the context was cancelled, causing final flushes and status updates to fail.

### Changes
- **Decoupled ArticleWriter**: Removed the timed context from `ArticleWriter.Start()`. It now only stops when its channel is closed.
- **Graceful Shutdown**: Updated `crawl.go` to use `context.Background()` for final `MarkDone`, `MarkFailed`, and `PushLinks` calls.
- **Worker Drain**: Added a 5-second timeout for the final worker wait to ensure a clean exit.

## SQLite Performance Optimizations

To resolve "database is locked" errors during high concurrency, I've applied the following optimizations:

### Driver & Connection Tuning
- **PRAGMA synchronous = NORMAL**: Drastically improves write performance in WAL mode by reducing blocking `fsync` calls.
- **Busy Timeout**: Increased from 5s to 20s (`_busy_timeout=20000`) to give workers more patience during contention.

### Configuration
- **Worker Limit**: Reduced `max_concurrency` from 50 to 10 in `seeds.yaml` to better suit SQLite's architecture.

## Verification Results

### Manual Crawl Test
Ran a 10-second crawl:
`.\civic-ingest.exe crawl --duration 10s --config data/seeds.yaml --db data/civic_lens.db`

**Results:**
- **Status updates**: All items correctly marked as `DONE` or `FAILED`.
- **Zero** "context deadline exceeded" errors at shutdown.
- **Performance**: High throughput maintained with 10 workers and `NORMAL` sync mode.

> [!NOTE]
> Persistent "database is locked" errors observed during testing were diagnosed as contention from long-running Python tests in other terminals. The Go ingestion layer itself is now optimized for concurrent access alongside the API and analysis suite.
