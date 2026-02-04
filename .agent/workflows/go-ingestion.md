---
description: Go ingestion layer - crawler and data storage
---

# Agent Instructions: Go Ingestion System

## Objective

Build a crash-resumable, polite, deduplicating news + Reddit ingestion system in Go with strong invariants and an auditable raw-data trail suitable for downstream AI analysis.

## Architecture

```
ingest/
├── cmd/ingest/         # CLI entrypoint
├── internal/
│   ├── crawler/        # HTTP fetcher with rate limiting
│   ├── frontier/       # SQLite-backed URL queue
│   ├── extract/        # RSS parser, Reddit API client
│   ├── model/          # Shared data types
│   └── runner/         # Orchestration logic
└── go.mod
```

## Invariants (Must-Have)

### Canonicalization
- Deterministic: same input URL always yields same `url_canon`
- Remove fragments `#...`
- Normalize host casing
- Remove common tracking params (`utm_*`)
- Canonical URL is the DB primary key for page identity

### Frontier State Machine
States: `QUEUED(0) -> INFLIGHT(1) -> DONE(2)` or `QUEUED -> INFLIGHT -> QUEUED(backoff)` or `FAILED(3)`
- A URL row exists at most once (`PRIMARY KEY(url_canon)`)
- A URL cannot be queued and inflight simultaneously
- Claiming a URL is atomic (transaction)
- On startup: any `INFLIGHT` older than threshold is returned to `QUEUED`

### Raw Content
- Raw content stored immutably by hash at `data/raw/sha256/<hash>.<ext>`
- DB records must reference `raw_hash` for every successful fetch
- `raw_hash` must match file contents

### Politeness
- Per-domain rate limits enforced via token bucket
- Redirect depth bounded
- Timeouts enforced

### Failure Handling
- No panics on malformed HTML/JSON
- Every failure recorded with `last_error` and retry/backoff policy

## SQLite Schema (Essential Tables)

### pages (frontier)
- `url_canon TEXT PRIMARY KEY`
- `url_raw TEXT NOT NULL`
- `domain TEXT NOT NULL`
- `state INTEGER NOT NULL`
- `content_sha256 TEXT`
- `last_error TEXT`

### articles_raw
- `article_id INTEGER PRIMARY KEY AUTOINCREMENT`
- `url_canon TEXT UNIQUE NOT NULL`
- `raw_hash TEXT NOT NULL`
- `title TEXT`
- `published_at INTEGER`

### reddit_posts_raw / reddit_comments_raw
- `fullname TEXT PRIMARY KEY`
- `raw_hash TEXT NOT NULL`
- `subreddit TEXT NOT NULL`
- `created_utc INTEGER NOT NULL`

## Commands

```powershell
# Build
.\run.ps1 build

# Run crawler
.\run.ps1 crawl
```

## Acceptance Criteria

1. Restart mid-run and resume without duplicates
2. DB enforces uniqueness constraints
3. Raw bodies are stored and traceable to DB rows
4. Rate limiting is enforced per domain
