---
description: Go ingestion layer - crawler and data storage
---

# Agent Instructions: Go Ingestion System

## Objective

Build a crash-resumable, polite, deduplicating news + Reddit + X ingestion system in Go with strong invariants and an auditable raw-data trail suitable for downstream AI analysis.

## Architecture

```
ingest/
├── cmd/civic-ingest/    # CLI entrypoint (cobra: migrate|ingest|crawl|reddit|x|requeue-stale)
├── internal/
│   ├── crawler/         # HTTP fetcher with rate limiting
│   ├── frontier/        # URL queue (frontier.go surface, frontier_postgres.go SQL)
│   ├── storage/db/      # Postgres connection + migration runner (db.go, db_postgres.go)
│   ├── extract/         # RSS parser, Reddit API client
│   ├── model/           # Shared data types
│   └── runner/          # Orchestration logic (article_writer_postgres.go, reddit_postgres.go,
│                         # x_postgres.go, x_officials_postgres.go, x_budget_postgres.go)
└── go.mod
```

Postgres is the only backend — `--db` takes a Postgres DSN (`postgres://...`, defaulting to `CIVIC_DATABASE_URL`) and `db.Open` fails loudly on anything else. The SQLite backend was deleted in the Phase 7 post-cutover decommission (2026-07-28).

## Invariants (Must-Have)

### Canonicalization
- Deterministic: same input URL always yields same `url_canon`
- Remove fragments `#...`
- Normalize host casing
- Remove common tracking params (`utm_*`)
- Canonical URL is the DB primary key for page identity

### Frontier State Machine
States (`raw.page_state` enum): `queued -> inflight -> done` or `queued -> inflight -> queued (backoff)` or `failed`
- A URL row exists at most once (`PRIMARY KEY(url_canon)`)
- A URL cannot be queued and inflight simultaneously
- Claiming a URL is atomic (transaction)
- On startup: any `inflight` older than threshold is returned to `queued`

### Raw Content
- Raw content stored immutably by hash in the content-addressed store (`CIVIC_RAW_STORE_DIR`, `data/raw/sha256/<hash>` in dev)
- DB records must reference `raw_hash` for every successful fetch
- `raw_hash` must match file contents

### Politeness
- Per-domain rate limits enforced via token bucket
- Redirect depth bounded
- Timeouts enforced

### Failure Handling
- No panics on malformed HTML/JSON
- Every failure recorded with `last_error` and retry/backoff policy

## Postgres Schema (raw.* — Essential Tables)

Source of truth: `data/pg-migrations/0001_north_star.sql`. Near-1:1 port of the old SQLite tables — deliberately not redesigned; rows must stay byte-faithful to what the crawler/fetchers key on.

### raw.pages (frontier)
- `url_canon TEXT PRIMARY KEY`
- `url_raw TEXT NOT NULL`
- `domain TEXT NOT NULL`
- `state raw.page_state NOT NULL DEFAULT 'queued'` (`queued`/`inflight`/`done`/`failed`)
- `content_sha256 TEXT`
- `last_error TEXT`

### raw.articles
- `url_canon TEXT PRIMARY KEY REFERENCES raw.pages`
- `raw_hash TEXT NOT NULL` — key into the content-addressed store
- `title TEXT`
- `published_at TIMESTAMPTZ`

### raw.reddit_posts
- `fullname TEXT PRIMARY KEY`
- `raw_hash TEXT NOT NULL`
- `subreddit TEXT`
- `created_utc TIMESTAMPTZ`

### raw.x_posts / raw.x_users
- `tweet_id TEXT PRIMARY KEY` / `user_id TEXT PRIMARY KEY`
- `author_id` on `raw.x_posts` deliberately has no FK to `raw.x_users` — capture must never drop a post over referential nicety
- `raw_hash TEXT NOT NULL`, `extraction_version TEXT NOT NULL`

## Commands

```bash
# Build
./run.sh build

# Run crawler
./run.sh crawl
```

## Acceptance Criteria

1. Restart mid-run and resume without duplicates
2. DB enforces uniqueness constraints
3. Raw bodies are stored and traceable to DB rows
4. Rate limiting is enforced per domain
