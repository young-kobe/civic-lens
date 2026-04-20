# 044 — Ingest-Layer Audit Remediation (2026-04-20)

Applies every finding in §1 of the 2026-04-19 non-security audit
(`docs/audits/04_19_2026.md`). The audit's layers 2–4 (analysis, API, UI)
are not addressed here; this walkthrough is deliberately scoped to
ingestion so the change is reviewable in isolation.

## Scope

All thirteen findings in §1 (Findings + LOC-reduction targets + Test
gaps + Recommendations). The only items intentionally deferred are
those the audit itself deferred ("Go interface extraction for fetchers
— medium-effort refactor, not on the critical path").

## Changes

### Dead code removed

- `internal/model/model.go`: dropped `ArticleRaw`, `RedditComment`,
  and the duplicate `CrawlResult` struct. `CrawlResult` survives in
  `internal/runner/crawl.go` where it is actually used.
- `internal/config/config.go`: dropped `CrawlConfig.MaxConcurrentDomain`
  (never read). `data/seeds.yaml` loses the matching key.
- `internal/extract/reddit/reddit.go`: dropped the OAuth trio
  (`authenticate`, `FetchSubredditPosts`, `FetchPostComments`) plus the
  `ClientID` / `ClientSecret` Config fields and `accessToken` /
  `tokenExpiry` client state. The only remaining surface is the public
  `.json`-endpoint pair.
- The Reddit client no longer returns a `[]model.RedditComment` —
  callers only ever read `c.Body`, so `parseComments` became
  `parseCommentBodies` returning `[]string`, and `FetchPostComments­Public`
  became `FetchPostCommentBodiesPublic`.

### Correctness fixes (the only non-aesthetic item in §1)

- **Context propagation.** `runner/crawl.go` and
  `runner/article_writer.go` used `context.Background()` for every DB
  write inside the worker loop, so outer-context cancellation (SIGINT,
  timeout) never reached them. All six sites now thread the real
  `ctx`; the writer carries its own cancel-on-Close context so pending
  transactions abort cleanly.
- **Worker drain on shutdown.** `CrawlRunner.Run` replaced the
  `sem.Acquire(cleanupCtx, MaxConcurrency)` 5-second timeout with a
  `sync.WaitGroup` that unconditionally waits for workers. The outer
  ctx is already cancelled by then, so workers exit promptly; there is
  no longer a path where a long worker gets orphaned.
- **`INFLIGHT` recovery in every runner.** `Frontier.EnsureRecovered`
  is a new method that runs `RecoverStale` and logs. `CrawlRunner`,
  `RedditRunner`, and `XRunner` all call it at startup — previously
  only the crawl runner did, so a Reddit/X fetcher crash would leave
  rows stuck `INFLIGHT` forever.
- **SQLite busy-timeout pragma.** Concurrent `MarkDone` calls were
  hitting `SQLITE_BUSY`. The root cause: `modernc.org/sqlite`
  silently ignores the `_busy_timeout=` DSN param, so the 20-second
  wait we thought was configured wasn't actually applied. Switched to
  the driver's documented `_pragma=busy_timeout(20000)` form (plus
  `_pragma=journal_mode(WAL)`, `_pragma=synchronous(NORMAL)`, and
  `_pragma=foreign_keys(on)`) in `internal/storage/db/db.go`.

### LOC / DRY fixes

- **Shared DB-write helper.** `internal/runner/dbwrite.go`'s
  `upsertRow(ctx, conn, table, cols, vals)` replaces the three
  hand-written `INSERT OR REPLACE` blocks in `runner/crawl.go` *(via
  `ArticleWriter`)*, `runner/reddit.go`, and `runner/x.go`. Per-runner
  insert sites are one call each.
- **Frontier state transitions.** `MarkDone` and the two
  `MarkFailed` branches were three near-identical SQL blocks. They
  now funnel through a private `updatePageState(ctx, urlCanon,
  state, updates)` helper, which also resets `inflight_at` in one
  place. The only non-parameterized case (`retries = retries + 1`)
  is handled via an expression-splicing sentinel.
- **`ClaimItems` via `UPDATE … RETURNING`.** Replaces the old
  select-then-loop-update pattern with a single statement, no
  explicit transaction needed. Dropped ~40 LoC.
- **`processPage` decomposition.** Split into `checkRobots`,
  `fetchPage`, `storeRaw`, and `extractAndEnqueue`. The six scattered
  `MarkFailed` sites collapsed into one (`failPage`) that tags each
  error with a category (`robots` / `fetch` / `http` / `store`) so
  ops can tell them apart in `last_error`.
- **`RecoverStale` logging.** The crawl runner used to log the
  recovery count on top of `RecoverStale`'s existing log — now only
  `EnsureRecovered` logs, once.

### Error categorization

- `Frontier.PushLinks` previously `continue`d silently on both bad
  input and DB errors. It now returns a `PushStats{Added, Malformed,
  DBErrors}` and surfaces a non-nil error when any DB writes failed,
  so the ingest runner can print a categorized line per seed.

### Trivia

- Fixed the `"FetchX"` → `"Fetch X"` typo in
  `cmd/civic-ingest/main.go`.

## Tests

- Existing `TestFrontierBasicOperations` and
  `TestFrontierRecoverStale` kept passing (updated to the new
  `PushStats` return type).
- New tests in `internal/frontier/frontier_test.go`:
  - `TestFrontierMarkFailedPermanent` — permanent=true jumps to
    `StateFailed` regardless of retries remaining.
  - `TestFrontierMarkFailedRetryBackoff` — retryable failure
    schedules `next_fetch_at` at least `1<<retries` minutes out and
    increments the DB counter.
  - `TestFrontierMarkFailedExhaustsRetries` — reaching `maxRetries`
    auto-promotes to `StateFailed`.
  - `TestFrontierConcurrentMarkDone` — 20 goroutines calling
    `MarkDone` on distinct pages all succeed (busy_timeout fix
    verified).
  - `TestFrontierPushLinksMalformed` — `PushStats.Malformed` counts
    URLs that fail `url.Parse`, separately from `Added` and
    `DBErrors`.
- Test setup consolidated into `newTestFrontier(t, maxRetries)` so
  the migration copy boilerplate lives in one place.

## Verification

```bash
cd ingest
go vet ./...          # clean
go build ./...        # clean
go test ./...         # ok frontier, storage/db, util
```

Binary still builds to `civic-ingest.exe` at repo root.

## Items explicitly deferred

- Go `APIClient` interface extraction for Reddit/X fetchers. The
  audit tagged this as medium-effort and not on the critical path;
  keeping it out of this PR so the diff stays scoped to the audit's
  "OPEN — correctness / LOC / hygiene" items.

## Files touched

```
cmd/civic-ingest/main.go
data/seeds.yaml
internal/config/config.go
internal/extract/reddit/reddit.go
internal/frontier/frontier.go
internal/frontier/frontier_test.go
internal/model/model.go
internal/runner/article_writer.go
internal/runner/crawl.go
internal/runner/dbwrite.go            (new)
internal/runner/ingest.go
internal/runner/reddit.go
internal/runner/x.go
internal/storage/db/db.go
```
