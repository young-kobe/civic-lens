# 2026-07-30 — Restore officials-pass test coverage on Postgres

The officials-pass tests deleted with the SQLite fixture (see
`2026-07-28-delete-go-sqlite-backend.md`'s "known gap") are back, rebuilt
against the real Postgres schema instead of a hermetic in-memory fixture.
`ingest/internal/runner/x_officials_integration_test.go` now covers the
cache-reuse, per-account failure isolation, and budget-exhaustion contracts
that `runOfficialsPass` (`x_officials.go`) is supposed to guarantee, gated on
`CIVIC_TEST_POSTGRES_DSN` the same way as every other Postgres-backed test in
the package.

## What shipped

- `TestLookupCachedUserIDPostgres_ReturnsCachedRow`,
  `_CaseInsensitiveOnUsername`, `_MissingReturnsEmpty` — exercise
  `lookupCachedUserIDPostgres` (`x_officials_postgres.go`) directly against a
  seeded `raw.x_users` row: cache hit, case-insensitive match, and the
  empty-string (not error) result for a handle never fetched before.
- `TestRunOfficialsPass_SuccessTagsRowsAsOfficial` — full pass against two
  stub-API officials; every resulting `raw.x_posts` row carries
  `is_official_tier = true`, scoped to this test's own tweet IDs (not a bare
  `COUNT(*)`, since the table is shared with other tests and real data).
- `TestRunOfficialsPass_FailedHandleDoesNotAbortRun` — one handle's lookup
  returns a suspended-account error; the pass must still process and succeed
  on the next handle instead of returning early.
- `TestRunOfficialsPass_UsesCachedUserIDOnRerun` — runs the same pass twice;
  the user-by-username endpoint (billed per call) must be hit exactly once
  across both passes, while the timeline endpoint is hit once per pass.
- `TestRunOfficialsPass_BudgetExhaustionSkipsRemainingHandles` — three
  handles, a ceiling sized so the first two consume it exactly; the third
  must be reported `Skipped` (never `Failed`) with its lookup and timeline
  endpoints never called at all.

All five scenario tests share `newOfficialsPostgresHarness` (real
`*db.DB` from `openTestPostgres` + a temp-dir `rawstore` + an `x.Client`
pointed at an `httptest` stub server reused from the old fixture almost
verbatim — the stub was never SQLite-specific). Row cleanup uses
`pgtest_official_`-prefixed handles/tweet/user IDs, deleted by exact ID
before and after each test (never `TRUNCATE`), matching
`frontier_postgres_test.go`'s `cleanupDomains` convention. The
budget-exhaustion test additionally pins its `ops.x_api_budget` row to a
synthetic far-future `month_key` (`newFixedMonthBudgetTracker`, via the
existing unexported `newXBudgetTracker(..., now func() time.Time)` seam) so
repeated runs against the shared test database never see spend carried over
from a prior invocation — the real current-month row would otherwise drift
across a `-count=1` loop and make the ceiling assertion depend on execution
history.

Not re-added: an `insertOfficialPost` tagging test — `postgres_integration_test.go`'s
`TestPostgresXPostAndUserUpsert` already asserts `is_official_tier = true`
after an officials-pass upsert, plus the I-6 semantic (a later topic-query
upsert must not clear the flag), so a fourth copy would be pure duplication.
The backfill-officials walk (`loadActiveOfficials`, `countStoredPosts`) was
restored earlier and lives in `backfill_officials_integration_test.go` —
untouched here.

## Why

The SQLite fixture's schema and mock DB were deleted with the rest of the
dual-backend dispatch layer in the Phase 7 decommission, taking these tests
with it since they had no path to a real database. The gap sat tracked in
`docs/todos/recompute-acceptance-and-tuning.md` under "Test coverage" until
now.

## Gates

`go build ./...`, `go vet ./...` clean. `go test ./internal/runner -run
'Officials|LookupCachedUserID' -count=1` run three consecutive times against
a throwaway `postgres:17-alpine` (migrations 0001-0008 applied) — all green,
no flakes. `go test ./...` gated and ungated both clean (ungated run skips
every Postgres-gated test with `CIVIC_TEST_POSTGRES_DSN not set`). Verified
zero leftover rows in `raw.x_posts`, `raw.x_users`, and `ops.x_api_budget`
after the three runs; container and its anonymous volume torn down after.
