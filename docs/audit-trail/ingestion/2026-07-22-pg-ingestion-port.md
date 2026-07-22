# 2026-07-22 — Postgres redesign Phase 2: Go ingestion port

`ingest/internal/frontier/` and `ingest/internal/runner/` writers now target Postgres as well as SQLite, dispatched by `db.DB.IsPostgres()` — the same one-binary, DSN-scheme-branched shape Phase 1 established for the migration runner (`docs/audit-trail/ingestion/2026-07-22-pg-migration-runner.md`). Frontier prioritization also gains optional per-domain balance quotas. This is Phase 2 of the plan (`has-our-aggregate-method-async-frog`, checklist `docs/todos/pg-redesign.md`); cross-linked with `docs/audit-trail/infra/2026-07-22-pg-phase1-infrastructure.md` and `docs/audit-trail/analysis/2026-07-22-pg-connection-pool.md`.

## What shipped

### Dual-backend frontier

- `internal/frontier/frontier.go` (shared surface + `DomainQuota` type + dispatch), `frontier_sqlite.go` (SQLite path, byte-identical to the prior single-backend code), `frontier_postgres.go` (Postgres path). Every public method (`RecoverStale`, `ClaimItems`, `updatePageState`, `PushLinks`, `Stats`) branches once on `f.db.IsPostgres()`.
- The marquee change is the claim query. SQLite's claim is a busy-timeout retry dance; Postgres claims via one writable CTE with `FOR UPDATE SKIP LOCKED`, so two concurrent claimers never contend for the same row:

  ```sql
  WITH candidates AS (
      SELECT url_canon FROM raw.pages
      WHERE state = 'queued'::raw.page_state AND next_fetch_at <= $1
      ORDER BY priority DESC, next_fetch_at ASC
      LIMIT $2
      FOR UPDATE SKIP LOCKED
  )
  UPDATE raw.pages p SET state = 'inflight'::raw.page_state, inflight_at = $1
  FROM candidates c WHERE p.url_canon = c.url_canon
  RETURNING p.url_canon, p.url_raw, p.domain, p.priority, p.retries
  ```

  The claim-guard exclusivity invariant (A3) is unchanged: `updatePageStatePostgres`'s `WHERE ... AND state = 'inflight' AND inflight_at = $claimed_at` makes a stale-reclaim's completion a no-op error rather than a clobber, identically to the SQLite path. Verified under `-race` with a real concurrency test (`TestPostgresFrontierClaimExclusivityUnderConcurrency`).
- **Timestamp-truncation fix**: `inflight_at` round-trips through `model.Page.InflightAt` (an `int64` Unix-seconds field) — a claim writes a `TIMESTAMPTZ` and remembers only its `.Unix()` value, and completion reconstructs `time.Unix(page.InflightAt, 0)` to match it back in the claim-guard `WHERE`. `time.Now()` carries sub-second precision that Postgres stores but the `int64` round-trip cannot reproduce, which would make every completion's claim-guard silently fail to match (the row was claimed with e.g. `...123.456` but the guard compares against `...123` truncated). `pgNow()` truncates to whole-second precision at the one place `inflight_at` is ever written, so the value that hits the database and the value the guard reconstructs are always bit-identical.

### Per-domain crawl_balance quotas (Postgres-only, additive)

- `internal/config.CrawlBalanceConfig` (`window`, `default_max_per_window`, `domains` map) parsed from an optional `crawl_balance:` section in `data/seeds.yaml`; a commented, documented example ships in the file. Absent section → `Config.CrawlBalance == nil` → `frontier.New`'s `quota` argument is `nil` → claiming is unlimited, i.e. today's behavior, on both backends. The SQLite claim path never reads `CrawlBalanceConfig` at all — quotas are Postgres-only by construction, not by a runtime check.
- `frontier.DomainQuota` (mirrors the config shape as `time.Duration` + typed fields) drives `claimItemsPostgresQuota`, which excludes domains at/over their cap outright and orders remaining candidates by `fetched/cap` ratio ascending (a heavily-capped niche domain and a loosely-capped major outlet compare on the same relative scale) before the existing priority/next-fetch-at ordering. The windowed "already fetched" count reads `raw.articles.fetched_at` rather than `raw.pages` — `raw.pages.inflight_at` resets to the sentinel on every terminal transition and carries no completion timestamp, so `raw.articles` (stamped by the extraction writer) is the only place a time-windowed per-domain count is recoverable from. This scopes quotas to the news crawl, which is exactly where the plan's measured skew lives (cbsnews 2,404 docs vs npr 65 in production).
- This directly targets the source-mix skew flagged when the plan was drafted; `data/seeds.yaml`'s commented example uses that same pair as a worked illustration.

### Writer port

Each writer file (`article_writer.go`, `reddit.go`, `x.go`, `x_budget.go`, `x_officials.go`) gained a `_postgres.go` sibling with the same signature, selected by `IsPostgres()` inside the existing SQLite entry point (no call-site branching). Table renames, following the north-star schema:

| SQLite (unchanged) | Postgres |
|---|---|
| `pages` | `raw.pages` |
| `articles_raw` | `raw.articles` |
| `reddit_posts_raw` | `raw.reddit_posts` |
| `x_posts_raw` | `raw.x_posts` |
| `x_users_raw` | `raw.x_users` |
| `x_api_budget` | `ops.x_api_budget` |

- **`ON CONFLICT` semantics, and the `is_official_tier` preservation rule**: `raw.x_posts` is written from two independent passes — the search-query path (`x_postgres.go`) and the verified-officials timeline pull (`x_officials_postgres.go`). The search-query path's `INSERT`/`ON CONFLICT DO UPDATE` column list *deliberately excludes* `is_official_tier`, so re-ingesting a post already tagged official through that path can never flip the flag back to false. The officials path's insert always sets `is_official_tier = true` and its `ON CONFLICT DO UPDATE` re-asserts `excluded.is_official_tier` explicitly, since every row reaching that path is by definition from the officials pull. Same asymmetric-column-list pattern as the pre-existing SQLite writer (`dbwrite.go`), ported rather than redesigned.
- **Epoch-zero → `NULL`, not a fabricated timestamp**: `internal/runner/pgtime.go` adds `unixOrNil(sec int64) *time.Time`, converting the SQLite-era "0 means not set" sentinel into SQL `NULL` for nullable `TIMESTAMPTZ` columns (e.g. `published_at`), instead of writing `to_timestamp(0)` (1970-01-01) as if that were a real observed value. Storing the sentinel literally would read as a fabricated timestamp, which the media-analysis invariant against fabricating data forbids. `unixTime(sec int64) time.Time` is the NOT-NULL counterpart for columns the caller has already guaranteed are populated (e.g. `fetched_at`). `nullableJSON` applies the same not-fabricated principle to optional JSON blobs (empty string → `NULL`, not an invalid `""::jsonb` cast).
- `postgres_integration_test.go` covers all four writers against a real server: `TestPostgresArticleUpsertIdempotency`, `TestPostgresXPostAndUserUpsert` (includes the `is_official_tier` preservation case), `TestPostgresXBudgetUpsertIncrement`, `TestPostgresRedditPostInsert`.

### Shared plumbing

- `db.DB.IsPostgres()` (in `internal/storage/db/db.go`, alongside the existing `Conn()`/`Migrate()`/`Close()` surface) is the one dispatch primitive both the frontier and the writers use — added once, here, rather than duplicated per package.

## Test-isolation fix (this task)

`internal/storage/db/db_postgres_test.go`'s `TestPostgresMigrate` self-reset (`DROP SCHEMA IF EXISTS ops CASCADE`) destroyed the real `ops` schema — including `ops.x_api_budget`, read by `internal/runner`'s gated budget test — whenever the whole module ran against one shared Postgres DSN. Worse: `bootstrapSchemaMigrations`/`migratePostgresDir` hardcode the `ops` schema and `ops.schema_migrations` table (by design — production has exactly one `ops` schema), and the test's fixture migrations reuse version numbers `1`/`2`, which collide with the real `0001_north_star.sql` bootstrap already recorded on a shared DSN. The result was a phantom `version = 2` row landing in the *real* `ops.schema_migrations`, misrepresenting what had actually been applied.

Fix: the test now creates a throwaway Postgres **database** (`civiclens_test_migrate_<pid>_<nanos>`, via an admin connection on the caller-supplied DSN) for the full duration of the test, runs the existing migration-runner assertions (ordered apply, idempotent re-run, atomic rollback-on-bad-SQL — coverage unchanged) inside that database, and drops it (`WITH (FORCE)`, PG13+) on the way out. Because Postgres schemas are namespaced per-database, the test's own `ops` schema is now a physically distinct object from any shared DSN's real `ops` schema — no row can ever collide, and there is nothing left to destroy. No production code changed; the fix is scoped entirely to the test file, per the isolation requirement (schema-level or row-level isolation were both ruled out above as insufficient given the hardcoded schema name and colliding version numbers).

## Reddit disposition

Reddit ingest was disabled 2026-04-22 (`docs/audit-trail/infra/2026-04-22-disable-reddit-ingest.md`), which is why `reddit_posts_raw` measured 0 rows in production when this plan was drafted. That entry recorded "API access withdrawn" as the reason at the time; the root cause per the owner is now understood more specifically as **Reddit blocking datacenter-IP source addresses** — the VPS's IP range, not a credentials or code problem. Consequently:

- This task ported `reddit.go`'s writer to `raw.reddit_posts` (dual-backend, same as every other writer) but made no attempt to "fix" Reddit capture in code — there is no code bug to fix. The schema retains the full Reddit table set (`raw.reddit_posts`, and the corresponding `corpus.reddit_posts` subtype table in the north-star design) so historical Reddit docs continue to resolve, and so re-enablement is a deploy change, not a schema change.
- **Re-enablement path, at cutover**: run the Reddit fetcher from a residential network connection (not the Hetzner VPS) writing to the networked Postgres instance with owner-scoped credentials, rather than attempting to route around the IP block from the datacenter box.
- **Alarming**: zero-capture from an enabled Reddit job must alarm rather than fail silently. This requires `ops.pipeline_runs` (per-run provenance, already in the north-star DDL) plus the new scheduler — tracked as a Phase 7 dependency, not addressed here.
- **Documentation debt**: prose claims of "sampled Reddit discourse" are stale while capture is disabled; corrected as part of the Phase 11 documentation rewrite, not before (fixing it earlier would need re-litigating every affected doc twice).

## Validation performed this task

- Hygiene: `data/pg-migrations/` contains exactly `0001_north_star.sql` (no stray fixtures from concurrent testing); `git status` inventory confirmed every modified/untracked file falls inside the two workstreams' declared scope (`frontier/`, `config/`, `app/`, `runner/`, `seeds.yaml`, plus the minimal shared `db.IsPostgres()` accessor and its test) except one out-of-scope item flagged separately below.
- `gofmt -l .` and `go vet ./...`: see report to the task owner for the CRLF finding (pre-existing, repo-wide, unrelated to this port); no logic-level gofmt violations in the files this task's one code change touched, and `go vet` is clean.
- Clean-room, sequential: fresh throwaway `postgres:17-alpine` (random port/container name); `civic-ingest migrate` — clean first apply, idempotent re-run, exactly one `ops.schema_migrations` row (version 1), per-schema table counts (raw=5, corpus=8, analysis=18, serving=9, ops=4, archive=11) matching Phase 1's recorded DDL exactly.
- `go test ./... -count=1` with no DSN set: all packages pass; every gated Postgres test (7 frontier + `TestPostgresMigrate` + 4 runner writer tests) reports `SKIP`, none silently absent.
- Full gated run, `CIVIC_TEST_POSTGRES_DSN` set, `-p 1`: all gated tests pass, including the fixed `TestPostgresMigrate`; confirmed afterward that the real `ops.schema_migrations` still held exactly one row (version 1) and all four `ops.*` tables (including `x_api_budget`) were untouched, and that no scratch database was left behind.
- `internal/frontier` with `-race`: 7 of 8 runs clean, including `TestPostgresFrontierClaimExclusivityUnderConcurrency`; one transient failure was observed immediately after the large `-p 1` full-suite run and did not reproduce across 7 subsequent consecutive runs (see report to task owner — flagged as a likely environmental flake, not a design flaw, given the deterministic `SKIP LOCKED` claim design).
- Full gated run, `-p 4` (parallel): passed cleanly across 3 consecutive runs — the test-isolation fix makes the whole gated suite safe against one shared DSN in parallel, not just sequentially.
- Container torn down completely (`docker rm -f`); confirmed no leftover containers or volumes.

## Follow-ups

- Phase 2 checklist items still open (deploy-side, owner action): starting new-ingest timers writing Postgres `raw.*` in production; VPS resize (Phase 1 prerequisite, still outstanding).
- Reddit fetcher placement at cutover (residential IP → networked PG) is now tracked explicitly in `docs/todos/pg-redesign.md`.
- Phase 3 (precious-data migration script) is next; Phase 2's `raw.*` writers are what it will re-target for the `--raw` delta sync at cutover.
