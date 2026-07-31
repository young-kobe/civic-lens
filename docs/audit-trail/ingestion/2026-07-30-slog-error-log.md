# 2026-07-30 — Ingest logging is slog; Error-level records mirror to ops.error_log

The Go ingest layer now logs through `log/slog` instead of the previous
mix of stdlib `log` (13 sites) and bare `fmt.Printf` error lines (~35
sites indistinguishable from progress output). The default logger is
installed in `internal/app` right after the DB opens:
a text handler to stderr wrapped by `internal/errorlog.Handler`, which
mirrors every `Error`-level record into `ops.error_log`
(`0009_error_log.sql`, `source='ingest'`) — the ingest half of the
durable-error-log initiative (`analysis/2026-07-30-durable-error-log.md`).

## What shipped

- `internal/errorlog`: slog.Handler wrapper. `component` attr becomes the
  column (default "ingest"); remaining attrs marshal into `context` JSONB;
  the insert is best-effort with a 5s timeout and never propagates a
  failure (stderr fallback). Process-wide rate cap: 200 durable rows per
  rolling hour, then one "cap reached" marker and stderr-only — same
  contract as the Python writer. Nil handler/nil db are safe no-ops.
- Converted sites, all with structured attrs and per-file components:
  `runner/reddit.go`, `runner/ingest.go`, `runner/x_officials.go` (all 8
  error sites), `runner/backfill_officials.go`, and
  `runner/article_writer_postgres.go`'s previously logged-and-swallowed
  DB-write failures — every Reddit/X/backfill/article-write failure is now
  durable for the first time. `frontier.go` stale recovery and
  `cmd/civic-ingest/main.go`'s fatal path converted too.
- Level discipline: `slog.Error` = durable row; `Warn`/`Info` = stderr
  only. Two crawl-path demotions to Warn, commented inline: `MarkDone` /
  `MarkFailed` write failures after a stale-recovery re-claim are the
  benign race `frontier.go` documents, and they can fire at per-page
  volume. Page FETCH failures are untouched — `failPage()` →
  `raw.pages.last_error` remains their durable record, and routing them
  here would be the exact error-storm the rate cap exists to prevent.
- Human-readable run summaries/counters remain `fmt.Printf`: they are CLI
  output, not errors.
- Tests: `internal/errorlog/errorlog_test.go` — ungated (nil-db safety,
  delegation, component precedence, cap boundary) plus
  `CIVIC_TEST_POSTGRES_DSN`-gated insert round-trip. The gated fixture
  hand-mirrors the 0009 DDL for the same CWD reason the frontier gated
  tests do.

## Why

- A Reddit fetch or X insert failure previously survived only as a
  stdout line under Docker's 30 MB json-file rotation — zero durable
  record, invisible by the time anyone looked.
- slog gives the layer levels and structured fields for free, and the
  handler seam is what lets "durable" be a property of the logger rather
  than a call the 40-odd sites each have to remember.
