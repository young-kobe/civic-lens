# Delete the Go SQLite backend (Phase 7 decommission)

**Date**: 2026-07-28
**Layer**: ingestion
**Cross-links**: infra: `2026-07-28-post-cutover-decommission.md`,
analysis: `2026-07-28-drop-favorability-stances.md`

## What the system does now

`civic-ingest` is Postgres-only. `db.Open` accepts a `postgres://` /
`postgresql://` DSN and fails loudly on anything else; `--db` defaults to
`CIVIC_DATABASE_URL` with no SQLite file fallback. The dual-backend
dispatch layer (`db.DB.IsPostgres()` and every branch that consulted it in
`frontier.go`, `article_writer.go`, `x_budget.go`, `x.go`,
`x_officials.go`, `reddit.go`) is gone — each call site invokes the
Postgres implementation directly. `ingest/docker-entrypoint.sh` is now a
plain exec of the binary.

Deleted along with the dispatch: `db_sqlite.go`, `frontier_sqlite.go`,
`runner/dbwrite.go` (SQLite-only upsert helpers), the 25-file
`data/migrations/` SQLite directory, and the `modernc.org/sqlite`
dependency. `data/pg-migrations/` is the only migration path.

## Test coverage after the deletion

SQLite-fixture tests were deleted or ported, not silently dropped:

- Pure-logic tests survive as-is or were extracted (DSN validation,
  migration discovery, `ceilDiv`/budget math, `validateCanonical`
  hostile-canonical invariant, `postsNeeded`/`targetFor`).
- Four frontier regression tests with real value (malformed-URL
  classification, wrong-key-is-loud, stale-reclaim guard,
  retries-exhausted promotion) were ported into the
  `CIVIC_TEST_POSTGRES_DSN`-gated `frontier_postgres_test.go`.
- `TestPostgresArticleUpsertIdempotency` gained row-level cleanup (the
  frontier package's `cleanupDomains` convention) — its leftover
  `raw.pages` row made frontier claim-count assertions flaky on a shared
  test instance once these gated tests became the only DB-backed coverage.

**Known gap** (tracked in `docs/todos/recompute-acceptance-and-tuning.md`):
the officials-pass integration scenarios (cache reuse, budget-exhaustion
skip, per-account failure isolation) and the backfill-officials walk tests
ran against a hermetic SQLite fixture and were deleted with it. Restoring
that coverage means building a PG-gated fixture over
`raw.x_users`/`raw.x_posts`/`ops.x_api_budget`.

## Gates

`go build`, `go vet`, `go test ./...` clean, including the gated packages
against a throwaway postgres:17-alpine with migrations 0001-0008 applied
(three consecutive `-count=1` runs). `grep -rni sqlite --include="*.go"`
over `ingest/` returns nothing; `modernc` absent from go.mod/go.sum.
