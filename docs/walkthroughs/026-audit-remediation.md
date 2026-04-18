# Walkthrough 026: Audit Remediation

Remediated findings from the 03/23/2026 code audit targeting database bottlenecks, concurrency limitations, and hardcoded migrations in both Go and Python layers.

## Problem
The initial architecture had several scalability and maintainability silos:
1. **DB Throughput**: SQLite connection pool was locked to 1, preventing parallel reads even in WAL mode.
2. **Synchronous Writes**: The Go crawler performed synchronous DB inserts in the middle of page processing, blocking the worker pool.
3. **Hardcoded Migrations**: Adding a new database table required manual updates to a static struct array in `db.go`.
4. **ETL Scalability**: Python ETL loaded and inserted documents sequentially without batching.
5. **Connection Management**: Python database connections lacked robust busy timeouts, leading to intermittent "database is locked" errors.

## Solution

### 1. Go: Concurrency & Database Improvements
- **Connection Pool**: Increased `MaxOpenConns` to 4 and `MaxIdleConns` to 2 in `internal/storage/db/db.go`, enabling concurrent readers while relying on SQLite WAL mode and busy timeouts for write serialization.
- **Async Article Writer**: Introduced `ArticleWriter` in `internal/runner/article_writer.go`. This uses a buffered channel and a background goroutine to batch article inserts into transactions (every 50 items or 2s), freeing crawler workers immediately.
- **Dynamic Migrations**: Refactored `Migrate()` to use `os.ReadDir` to discover `*.sql` files in `data/migrations/`. 

### 2. Python: ETL & Connection Improvements
- **Batched ETL**: Refactored `ContentLoader.load_new_raw_content` to use `executemany` for bulk inserts and periodic commits (every 100 rows), significantly reducing transaction overhead.
- **Robust Connections**: Wrapped SQLite connection creation in a `contextmanager` that enforces `PRAGMA busy_timeout = 5000` and `PRAGMA journal_mode = WAL` on every new connection.

## Verification Results

### Automated Tests
- **Go Migration Test**: Added `db_test.go` verifying that the dynamic discovery correctly applies migrations in lexicographical order.
- **Go Crawler Test**: Updated `frontier_test.go` to use the real migration files, ensuring schema parity for all unit tests.
- **Python Loader Test**: Created `test_loader.py` using `tempfile` isolation to verify batched loading and `busy_timeout` configuration.
- **Suite Pass**: All tests passed across both languages.

```powershell
# Go
cd ingest; go test ./...
# Python
python -m pytest analysis/tests/test_loader.py
```

## Impact
- **Improved Crawl Speed**: Crawlers no longer wait for disk I/O on every successful extraction.
- **Reduced Lock Contention**: API readers can now query the database simultaneously with background analysis and ingestion.
- **Maintainability**: New database changes now only require adding a `.sql` file to the migrations directory.
