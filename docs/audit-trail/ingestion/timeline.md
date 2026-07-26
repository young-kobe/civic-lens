# Ingestion timeline (pre-2026-07, consolidated)

Condensed record of `ingest/` (Go crawler + Reddit/X fetchers) history, consolidated from the retired
`docs/walkthroughs/` linear log (see `docs/todos/walkthrough-consolidation.md` for the consolidation
that produced this file). Chronological by original walkthrough number; most entries carry no in-file
date, so ordering is the only reliable signal until the explicit 2026-04 dates below. Where an
outcome was later reversed or superseded, that is called out in the digest.

## 001 — Initial Infrastructure (undated)

Original ingestion layer was C++ (`ingestion-cpp/`), writing content-addressed raw HTML/JSON
(SHA256) and metadata to SQLite, driven by a frontier state machine (`QUEUED -> INFLIGHT ->
DONE|FAILED`). Superseded in full by 004.

## 004 — Go Migration (undated)

Replaced the C++ ingestor outright with the Go stack still in use: cobra CLI (`civic-ingest`) with
`migrate|ingest|crawl|reddit|requeue-stale` subcommands, the frontier state machine, a rate-limited
HTTP client, a robots.txt checker, and RSS/HTML/Reddit extractors, all against the same
content-addressed raw store + SQLite metadata DB. This is the architecture CLAUDE.md still describes.

## 010 — Pipeline Improvements (undated)

Added frontier-level 30-day recency filtering in `ingest.go` (paired with a second check at the ETL
layer). Fixed Reddit ingestion, which had never actually been run rather than being broken. Raised
ETL batch size 100→500 once the political-content filter proved cheap to run per-doc.

## 017 — Civic Lens Analysis Redesign (undated, ingestion slice)

Added `author_profiles`/`engagement_metrics` schema tables (migration 004) and fixed a bot-detection
bug where `x_origin_confidence` was silently never set. `author_profiles`/`engagement_metrics` were
later dropped as dead tables in walkthrough 030.

## 019 — Fix X Data Ingestion Error (undated)

A `sqlite3.IntegrityError` was silently blocking analysis of ingested X posts because the `docs.
source_type` CHECK constraint didn't include `x_post`. Migration `003_allow_x_post_source.sql`
relaxed the constraint; Go's then-hardcoded migration list in `db.go` was updated to register it.
That hardcoded-list mechanism was itself replaced by dynamic discovery in walkthrough 026.

## 020 — X Integration & Global Heatmap (undated, ingestion slice)

Added a full Go X API v2 client (rate limiting, `XConfig`) plus new DB tables (migration
`002_x_tables.sql`) and model types. This is the ingestion half of the X/Twitter integration; the
paired geo-aggregation feature it fed (walkthrough 020's Python/UI half) was fully decommissioned in
walkthrough 066.

## 022 — Struct Receiver Refactor (undated)

Pure refactor, no behavior change: converted the standalone `runner` package functions (`crawl.go`,
`reddit.go`, `x.go`, `ingest.go`) into struct-method receivers (`CrawlRunner`, `RedditRunner`,
`XRunner`, `IngestRunner`) encapsulating `*app.App` and metrics as fields, called from `main.go` as
`NewXxxRunner(a).Run(ctx)`. `requeue.go` was deliberately left alone as too thin to warrant it.

## 026 — Audit Remediation: throughput & migrations (undated)

Fixed SQLite scalability limits under Go: raised the connection pool (`MaxOpenConns=4`), added an
async buffered-channel `ArticleWriter` so crawl workers stop blocking on DB writes, and replaced the
hardcoded per-migration-file list with dynamic `os.ReadDir` discovery of `data/migrations/*.sql`.
This SQLite-era tuning is superseded by the ongoing Postgres rewrite.

## 027 — SQLite Optimization & Graceful Shutdown (undated)

Fixed shutdown races: `ArticleWriter.Start()` decoupled from the timed crawl context so it only stops
on channel close, `context.Background()` used for final `MarkDone`/`MarkFailed`/`PushLinks` calls, and
a bounded worker-drain wait added. SQLite tuning: `synchronous=NORMAL`, busy timeout 5s→20s,
`max_concurrency` 50→10 in `seeds.yaml`. Entirely SQLite-era work, now superseded by the PG rewrite.

## 038 — Frontier CHECK constraint (undated, ingestion slice)

Added a `CHECK` constraint on `pages.state` (values 0-3) since SQLite had no validation and a stray
bad `UPDATE` would previously have succeeded silently.

## 044 — Ingest-Layer Audit Remediation (2026-04-20)

Closed every §1 finding of the 2026-04-19 non-security audit, scoped to ingestion alone so the diff
stayed reviewable in isolation. Correctness: real request `ctx` threaded through every worker DB write
(previously `context.Background()` everywhere, so SIGINT/timeout never propagated); unconditional
`sync.WaitGroup` drain replacing a 5s shutdown timeout; `Frontier.EnsureRecovered` shared by all three
runners (previously only the crawl runner reset stuck `INFLIGHT` rows, so a Reddit/X crash left them
stuck forever); SQLite `busy_timeout` actually applied via the driver's `_pragma=busy_timeout(...)`
form (the `_busy_timeout=` DSN param modernc's driver ignores silently). DRY/decomposition: shared
`upsertRow` helper, `UPDATE ... RETURNING` for `ClaimItems`, `processPage` split into
`checkRobots/fetchPage/storeRaw/extractAndEnqueue`, categorized `PushStats`. Removed the unused
Reddit OAuth client and its config fields as dead code — **reversed** five walkthroughs later (049)
when Reddit started blocking the production Hetzner IP and OAuth-based access turned out to be
needed after all.

## 047 — Pre-Deploy Hardening, PR-B (2026-04-21 launch window)

Go-side slice of the five-PR pre-deploy security remediation. Added an SSRF-via-redirect guard
(`validateURL` rejects private/loopback/link-local/metadata IPs and localhost-family hostnames,
re-checked on every redirect hop; does not resolve DNS, relies on the origin firewall as a
compensating control) and 10MB body-size caps on RSS/Reddit/X fetches.

## 048 — Pipeline Cost Controls + Seed Refresh (undated, just before launch)

X had moved to prepaid per-resource pricing ($0.005/post read, $0.010/user read) that the existing
`seeds.yaml` cost estimate didn't reflect — unthrottled, the seed set would have pulled ~63k
tweets/month with no spend ceiling. Added migration 017's `x_api_budget` table (one row per UTC
month) and a Go `XBudgetTracker` that checks `OverBudget()` before each query-loop iteration and
records spend *before* the local DB insert (since the API call already spent the credit regardless of
insert success). Refreshed `seeds.yaml`: X queries pruned 21→8, `max_tweets_per_query` 100→10,
5 new RSS feeds added, subreddits rebalanced. Target ≤$25/month for X, ≤$48/month total.

## 049 — Launch (live 2026-04-21)

Cutover-day ingestion fixes: a migrations-path symlink needed because `civic-ingest migrate` resolves
paths relative to the DB directory; DB ownership drift after migrations ran as root; the crawl timer
needed to run `civic-ingest ingest` before `crawl` since `crawl` only drains an already-populated
frontier. **Reddit 403'd from the production Hetzner IP** (datacenter-IP block), forcing
`civic-lens-reddit.timer` to be disabled at launch and directly reopening 044's OAuth-removal
decision as an open follow-up. `realclearpolling.com` also blocked the polling scraper (non-blocking,
unrelated to ingest proper).

## 056 — X Official-Timeline Queries (undated, follows the 2026-04-22 audit in 055)

Config-only fix (`data/seeds.yaml`, no Go code) for the entity-registry blocker found in 055: 0% of
ingested X docs matched `verified_officials.yaml` because ingest only ran topic queries, never
per-official timelines. Added a pure-timeline `from:` query across 9 top officials plus a
cabinet-secretary × topic intersection query for 7 more. `max_tweets_per_query` 10→8 and
`monthly_budget_cents` 2500→3000 made room (~$27.50/mo projected against the new $30 cap). A
follow-up deep-pull endpoint (`GET /2/users/:id/tweets`) was proposed as a fallback if this proved
insufficient — no later walkthrough records that gate being exercised.
