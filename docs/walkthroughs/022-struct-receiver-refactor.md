# Struct Receiver Refactoring - Runner Package

Converted standalone runner functions to struct-method patterns, encapsulating shared state (`*app.App`, metrics) into receiver structs.

## Changes

| File | Struct | Methods |
|---|---|---|
| `runner/crawl.go` | `CrawlRunner` | `Run`, `processPage`, `insertArticle` |
| `runner/reddit.go` | `RedditRunner` | `Run`, `insertPost` |
| `runner/x.go` | `XRunner` | `Run`, `insertPost`, `insertUser` |
| `runner/ingest.go` | `IngestRunner` | `Run` |

**Call site** (`cmd/civic-ingest/main.go`): All 4 runners updated to `NewXxxRunner(a).Run(ctx)`.

**Skipped**: `requeue.go` (thin single-call wrapper, no shared state).

## Key Improvements

- `*app.App` is a struct field instead of threaded through every parameter
- Atomic counters in `CrawlRunner` are struct fields instead of raw pointer args
- Inline DB insert logic extracted into dedicated `insertPost`/`insertUser` methods
- X client stored as struct field on `XRunner`

## Verification

| Check | Result |
|---|---|
| `go build ./...` | Pass |
| `go vet ./...` | Pass |
| `go test ./...` (util) | Pass |
| `go test ./...` (frontier) | Pre-existing failure (migration path, unrelated) |
