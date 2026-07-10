# Civic Lens - Ingestion Module

This module crawls and ingests content from news RSS/web feeds, Reddit, and X. It is a
crash-resumable Go crawler that writes raw HTML/JSON to content-addressed storage
(`data/raw/sha256/<hash>`) and metadata to SQLite (`data/civic_lens.db`).

## Architecture

- **Language**: Go 1.22+
- **Storage**: content-addressed raw blobs under `data/raw/sha256/` + SQLite metadata.
- **Frontier state machine**: `QUEUED -> INFLIGHT -> DONE | FAILED`. `INFLIGHT` rows are
  reset to `QUEUED` on startup, so an interrupted run resumes cleanly.
- **Key packages (`internal/`)**:
  - `frontier`: URL queue + state machine.
  - `httpclient` / `robots`: polite fetching (per-domain token bucket, robots.txt).
  - `runner`: crawl / Reddit / X fetch orchestration.
  - `extract`: HTML normalization.
  - `storage`: content-addressed blob writes + SQLite persistence.
  - `config`: seeds/config loading.

## Workflows

### 1. Build

```bash
go build -o civic-ingest ./cmd/civic-ingest   # or: .\run.ps1 build
```

### 2. Commands

The CLI is a cobra app with these subcommands:

```bash
./civic-ingest migrate         # apply DB migrations
./civic-ingest crawl           # run the web crawler (news via RSS/HTML)
./civic-ingest reddit          # fetch Reddit posts/comments
./civic-ingest x               # fetch X/Twitter posts
./civic-ingest requeue-stale   # reset stuck INFLIGHT rows
```

### 3. Storage

- `pages`: crawl frontier state (one row per canonical URL).
- `articles_raw`: parsed news records, each carrying the `raw_hash` of its source blob.
- `reddit_posts_raw`: parsed Reddit records, likewise `raw_hash`-linked.
- Raw bytes live at `data/raw/sha256/<hash>` and are immutable once written.

## Configuration

Seeds and runtime settings (RSS seeds, subreddits, Reddit/X API creds, rate limits) live
in `data/seeds.yaml`. Command-line flags override where applicable.

## Tests

```bash
go test ./...
go test ./internal/runner -run TestReddit   # single package/test
```
