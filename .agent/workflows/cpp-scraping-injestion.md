---
description: Core C++ outline
---

# Agent Instructions: C++ Scraping + Ingestion (Correctness & Invariants)

## Objective
Build a crash-resumable, polite, deduplicating news + Reddit ingestion system in C++ with strong invariants and an auditable raw-data trail suitable for downstream AI analysis.

This system must:
- Discover news via RSS (primary) and optionally section pages (secondary).
- Ingest Reddit via the official API (preferred) and store posts + top comments.
- Enforce correctness via deterministic URL canonicalization, persistent frontier state, DB constraints, and restart recovery.

## Non-Goals (v1)
- Perfect “main text extraction” in C++ (Python will do deeper extraction).
- Full robots.txt parser (optional stretch).
- Full browser automation (no Playwright/Selenium).

---

## High-Level Architecture
1. **Seed Loader**
   - RSS feed list for news (allowlist)
   - subreddit list for Reddit
2. **Canonicalizer**
   - canonicalizeUrl(url_raw) -> url_canon
3. **Frontier (SQLite)**
   - persistent queue + state machine
4. **Fetcher (libcurl)**
   - timeouts, redirects, gzip
   - conditional GET (ETag / If-Modified-Since)
   - per-domain rate limiting
5. **Raw Store**
   - content-addressed raw files: `raw/sha256/<hash>.<ext>`
6. **Extractors**
   - RSS parser (discover article URLs)
   - HTML metadata extractor (canonical/title/published if easy)
   - Reddit API client + JSON parser
7. **Persistence**
   - store raw pointers + minimal metadata (not heavy NLP)
8. **Invariant Tests**
   - canonicalization tests
   - frontier state transition tests
   - restart recovery test (requeue stale inflight)

---

## Invariants (Must-Have)
### Canonicalization
- Deterministic: same input URL always yields same `url_canon`.
- Remove fragments `#...`.
- Normalize host casing.
- Remove common tracking params (`utm_*`).
- Canonical URL is the DB primary key for page identity.

### Frontier State Machine
States: `QUEUED(0) -> INFLIGHT(1) -> DONE(2)` or `QUEUED -> INFLIGHT -> QUEUED(backoff)` or `FAILED(3)`
- A URL row exists at most once (`PRIMARY KEY(url_canon)`).
- A URL cannot be queued and inflight simultaneously.
- Claiming a URL is atomic (transaction).
- On startup: any `INFLIGHT` older than threshold is returned to `QUEUED`.

### Raw Content
- Raw content stored immutably by hash.
- DB records must reference `raw_hash` for every successful fetch.
- `raw_hash` must match file contents.

### Politeness
- Per-domain rate limits enforced.
- Redirect depth bounded.
- Timeouts enforced.

### Failure Handling
- No crashes on malformed HTML/JSON.
- Every failure recorded with `last_error` and retry/backoff policy.

---

## Minimum SQLite Schema (v1)
Create these tables (exact names can vary, but keep the intent/constraints):

### pages (frontier)
- url_canon TEXT PRIMARY KEY
- url_raw TEXT NOT NULL
- domain TEXT NOT NULL
- state INTEGER NOT NULL
- priority INTEGER NOT NULL DEFAULT 0
- retries INTEGER NOT NULL DEFAULT 0
- next_fetch_at INTEGER NOT NULL DEFAULT 0
- inflight_at INTEGER NOT NULL DEFAULT 0
- http_status INTEGER
- content_sha256 TEXT
- etag TEXT
- last_modified TEXT
- last_error TEXT

Index: (state, next_fetch_at)

### articles_raw (minimal for downstream)
- article_id INTEGER PRIMARY KEY AUTOINCREMENT
- url_canon TEXT UNIQUE NOT NULL
- domain TEXT NOT NULL
- fetched_at INTEGER NOT NULL
- raw_hash TEXT NOT NULL
- title TEXT
- published_at INTEGER
- extraction_version TEXT NOT NULL

### reddit_posts_raw
- fullname TEXT PRIMARY KEY
- subreddit TEXT NOT NULL
- created_utc INTEGER NOT NULL
- raw_hash TEXT NOT NULL
- title TEXT
- body TEXT
- score INTEGER
- num_comments INTEGER
- extraction_version TEXT NOT NULL

### reddit_comments_raw
- fullname TEXT PRIMARY KEY
- post_fullname TEXT NOT NULL
- subreddit TEXT NOT NULL
- created_utc INTEGER NOT NULL
- raw_hash TEXT NOT NULL
- body TEXT
- score INTEGER
- extraction_version TEXT NOT NULL

---

## Implementation Requirements
### Claim Next Task (Atomic)
- Use `BEGIN IMMEDIATE` transaction.
- Select 1 eligible queued row by (priority DESC, next_fetch_at ASC).
- Update to INFLIGHT with `inflight_at=now`.
- Commit.

### Retry/Backoff
- On transient failures: increment retries, set `next_fetch_at=now+backoff`.
- Backoff policy: exponential, bounded.

### Requeue Stale Inflight
- At startup and periodically: move INFLIGHT older than X seconds back to QUEUED.

### Output Raw Files
- For HTML: `.html`
- For JSON: `.json`
- Compute sha256 of bytes, store at `raw/sha256/<hash>.<ext>` and record hash in DB.

---

## Deliverables
1. CMake-based C++ project builds cleanly.
2. CLI:
   - `ingest --db path.db --raw-dir raw/ --config seeds.yaml --workers N`
3. Seeds config format:
   - list of RSS feeds
   - list of subreddits
4. Unit tests:
   - canonicalization
   - claim/mark/requeue
5. Operational logs:
   - fetch success/failure counts
   - per-domain rate-limit stats (optional v1)

---

## Acceptance Criteria (v1)
- Restart the process mid-run and it resumes without duplicates.
- DB enforces uniqueness constraints and state transitions remain consistent.
- Raw bodies are stored and traceable to DB rows.
- Rate limiting is enforced.
