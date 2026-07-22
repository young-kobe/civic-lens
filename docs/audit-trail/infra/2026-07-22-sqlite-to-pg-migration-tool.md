# 2026-07-22 — Postgres redesign Phase 3: precious-data migration script

`tools/migrate_sqlite_to_pg.py` moves the two categories of data the redesign
cannot recompute — the ingestion capture layer (`raw.*` + `ops.x_api_budget`)
and, best-effort, the old derived outputs (`archive.*`) — from the legacy
SQLite database into the north-star Postgres schema
(`data/pg-migrations/0001_north_star.sql`). This is Phase 3 of the Postgres
redesign (plan `has-our-aggregate-method-async-frog`, checklist
`docs/todos/pg-redesign.md`), cross-linked with the Phase 1 entries
(`docs/audit-trail/infra/2026-07-22-pg-phase1-infrastructure.md`,
`docs/audit-trail/analysis/2026-07-22-pg-connection-pool.md`) and Phase 2
(`docs/audit-trail/ingestion/2026-07-22-pg-ingestion-port.md`,
`docs/audit-trail/ingestion/2026-07-22-pg-migration-runner.md`). The script is
not yet run against production — see Follow-ups.

## What shipped

- **`tools/migrate_sqlite_to_pg.py`** (~1230 lines): standalone (imports only
  the standard library, `sqlite3`, and `psycopg` — nothing from
  `analysis.src` or `ingest`, so it keeps working after both trees are
  deleted at Phase 11 decommission). Three mutually exclusive CLI modes:
  `--raw`, `--archive`, `--verify`, plus `--sqlite`, `--pg`, `--batch-size`
  (default 1000), `--tolerate-not-null-zeroes`, `--raw-files-dir` (default
  `data/raw/sha256`), `--sample-size` (default 500), `--seed` (default 42).
- **Shared mapping-function layer** (the design's core safety property,
  stated in the module docstring): `epoch_to_datetime` (nullable columns),
  `epoch_to_datetime_not_null` (NOT NULL columns), `is_epoch_missing`,
  `page_state_label`, `int_flag_to_bool` / `nullable_int_flag_to_bool`,
  `raw_json_text` (raw mode, unvalidated passthrough), `best_effort_json`
  (archive mode). A declarative `ColumnSpec`/`TableSpec` layer composes these
  once per column; `--raw`/`--archive` writers and `--verify` both call
  `transform_row(spec, row)` to get the canonical target-shaped value — there
  is no second, hand-written copy of "how a column maps" anywhere in the
  verifier. A narrow `bind_row`/`bind_adapter` step exists only so the writer
  can wrap a parsed JSON value in psycopg's `Json()` adapter before binding;
  `--verify` never touches it, so it always compares against the same
  canonical value the writer computed.
- **Epoch-zero rule, precisely mirroring the Go precedent** (`ingest/internal
  /runner/pgtime.go`, `frontier_postgres.go`'s `pgEpochZero`): nullable
  columns map 0/NULL -> SQL NULL. NOT NULL columns split into two groups:
  *sentinel* (`raw.pages.next_fetch_at`/`inflight_at`, where the DDL itself
  defaults to `to_timestamp(0)` and 0 means "ready now"/"never claimed", not
  unset — never counted as a problem) and *strict* (`raw.articles.fetched_at`,
  `raw.x_posts.created_at`/`fetched_at`, `raw.x_users.fetched_at`,
  `ops.x_api_budget.last_updated` — real data-quality signals). Strict-column
  zeroes are written (there is nowhere else to put them) but tallied; `--raw`
  exits 1 and prints the per-column counts unless `--tolerate-not-null-zeroes`
  is passed. Archive columns are all nullable in the archive schema, so they
  always use the lenient mapping; zero/NULL is counted and reported but never
  fails the import (archive is "insurance," per the plan).
- **Idempotency**: every `--raw` table upserts via
  `INSERT ... ON CONFLICT (natural pk) DO UPDATE SET ...`, keyed on the same
  natural key the Go ingestor already upserts on (`url_canon`, `fullname`,
  `tweet_id`, `user_id`, `month_key`) — re-running `--raw` at cutover syncs
  only the delta. `--archive` uses `ON CONFLICT (pk) DO NOTHING` instead
  (verbatim-preserved integer/text PKs) — a safety default for an
  accidental re-run, not a requirement the plan stated; archive is
  conceptually one-time and read-only after import.
- **Transaction granularity — one commit per batch, not per table**
  (justified in the module docstring and `migrate_table`'s docstring):
  `raw.pages` alone is 1.3M+ rows in production. A single table-wide
  transaction would hold a long-lived lock and, on failure partway, roll back
  everything already migrated in that table, which defeats the point of the
  idempotent design (a restarted run just re-upserts already-migrated rows
  harmlessly via `ON CONFLICT`). Per-batch commits bound transaction size and
  leave most of a table durably migrated if a later batch fails.
- **`--verify` battery**: per-table row counts (raw + ops always; archive
  tables only when populated — "archive when present" per the plan, an empty
  archive table reads as "not imported yet," not a failure); per-column NULL
  counts (computed by re-running the shared transform over a single
  streaming pass of the source, compared against
  `COUNT(*) FILTER (WHERE col IS NULL)` on the target); min/max/sum over every
  column marked `aggregatable`, timestamp columns compared via
  `EXTRACT(EPOCH FROM col)` so the comparison is apples-to-apples with the
  source epoch integers; PK uniqueness proven independently on both sides via
  `GROUP BY ... HAVING COUNT(*) > 1` (not just trusted to the Postgres PK
  constraint); a seeded (`random.Random(seed).sample`) random sample of up to
  `--sample-size` rows compared field-by-field through the same transform,
  with two narrow, documented equality exceptions (`_values_match`): float
  tolerance, and a raw JSON string compared against Postgres's
  already-decoded `jsonb` object (Postgres does not hand back the exact text
  it was given for a jsonb column); every distinct `raw_hash` on a
  `raw_hash`-carrying table resolves to a file under
  `--raw-files-dir/<hash[:2]>/<hash>.*` (content-addressed layout, matching
  `ingest/internal/storage/rawstore/rawstore.go`), missing-file failures
  listing the first 10 hashes. Exits 1 on any failure with a `FAIL:` line per
  broken check; exits 0 and prints `VERIFY: PASS` otherwise.
- **`tools/test_migrate_sqlite_to_pg.py`**: standalone `unittest` (matching
  the repo's existing convention, not pytest — chosen because the module
  under test is deliberately standalone and does not belong under
  `analysis/tests/`, which the module must outlive). Two tiers: pure
  mapping-function unit tests (always run, no I/O) and a full `--raw`/
  `--archive`/`--verify` integration suite gated on
  `CIVIC_TEST_POSTGRES_DSN` (distinct from the runtime `CIVIC_DATABASE_URL`,
  same convention as `db_postgres_test.go`) against a throwaway Postgres 17
  the DSN points at, applying `0001_north_star.sql` directly via `psycopg`
  (no Go binary dependency in the test itself). The fixture applies every
  `data/migrations/*.sql` file in order via the `sqlite3` CLI, then inserts
  rows exercising every mapped table and edge case: all 4 page states with
  sentinel-zero `next_fetch_at`/`inflight_at`; a strict NOT-NULL epoch
  problem in `articles_raw`/`x_posts_raw`/`x_users_raw`/`x_api_budget`;
  nullable epoch 0 and NULL; valid/empty-string/invalid JSON in
  `x_posts_raw.context_annotations_json` and `docs.metadata_json`; both
  int-flag values in `is_official_tier`/`verified`/`protected`; a `raw_hash`
  with a real file plus one deliberately inserted-and-removed missing hash.

## Why

- The plan calls this "the one truly irreversible-if-wrong step" in the
  whole redesign (Risks: "Raw migration is the one irreversible-if-wrong
  step" — X/Reddit capture history is unrefetchable) — hence the emphasis on
  a single shared mapping layer the verifier cannot drift from, an
  idempotent re-runnable `--raw` for the cutover-day delta sync, and a
  verify battery thorough enough to gate cutover rather than a single
  row-count sanity check.
- The epoch-zero rule mirrors the Phase 2 Go writer port exactly (rather
  than re-deriving a similar-but-different rule in Python) so a human
  auditing "why is this timestamp 1970" gets one answer across both
  ingestion paths, not two.
- Archive is explicitly "best-effort insurance" per the plan (verbatim
  import "just to have," final SQLite kept as a cold R2 artifact regardless)
  — it must never abort over one malformed JSON blob or a stale epoch-zero
  row from years-old data; those are counted and reported, never fatal.
- Transaction-per-batch (not per-table) was an explicit "choose and justify"
  item in the task brief: the production `raw.pages` row count (1.3M+, see
  the plan's Context section) makes a single table-wide transaction both a
  long-held lock and an all-or-nothing unit that would waste already-correct
  work on a late failure.

## Validation performed this task

- Synthetic fixture: all 25 `data/migrations/*.sql` files applied via the
  `sqlite3` CLI in order onto a fresh temp file, confirmed clean — no drift
  between the numbered migration set and the assumed source schema.
- Throwaway `postgres:17-alpine` container: `civic-ingest migrate` applied
  `0001_north_star.sql` cleanly against it (reusing the Phase 1-validated
  DDL as-is, no changes needed here).
- `--raw` clean run against the fixture: wrote all 6 tables (pages,
  articles, reddit_posts, x_posts, x_users, x_api_budget), correctly
  identified 4 planted strict-epoch problems (one each in
  `raw.articles.fetched_at`, `raw.x_posts.created_at`,
  `raw.x_users.fetched_at`, `ops.x_api_budget.last_updated`), exited 1
  without `--tolerate-not-null-zeroes` and 0 with it. Spot-checked via
  `psql`: page states mapped to all 4 enum labels correctly, sentinel
  columns (`next_fetch_at`/`inflight_at`) held real epoch-zero timestamps
  where the fixture planted 0 (not counted as problems), `context_annotations`
  cast to `jsonb` correctly for both the populated and empty-string
  (-> NULL) cases, `verified`/`protected` mapped both int-flag values to
  BOOLEAN correctly.
- Idempotent re-run: updated one source `pages` row's `priority`/
  `last_error` after the first `--raw` run, re-ran `--raw` — target row
  count stayed at 4 and the updated values propagated via `ON CONFLICT DO
  UPDATE`.
- `--archive` clean run: imported all 9 non-evals tables verbatim,
  confirmed `ai_output_evals` (0 source rows) was correctly skipped and
  logged as such; re-ran `--archive` a second time to confirm `ON CONFLICT
  DO NOTHING` makes it a safe no-op (row counts unchanged, no PK-collision
  crash). Separately populated a copy fixture with one `ai_output_evals` row
  and confirmed the "unexpected nonzero, importing" path fires and the row
  lands correctly (int flags -> BOOLEAN).
- Two deliberately invalid JSON blobs (`docs.metadata_json`,
  `ai_outputs.output_json`) were counted, reported, and stored as NULL
  rather than aborting the archive import.
- Confirmed `--raw`'s deliberately different posture on invalid JSON: a
  malformed `context_annotations_json` value on a throwaway `x_posts_raw`
  row made the `INSERT` fail loud with `psycopg.errors.InvalidTextRepresentation`
  (exit 1) and the whole failing batch rolled back cleanly (row count
  unaffected) — `raw_json_text` intentionally does not validate, matching
  `nullableJSON()` in `pgtime.go`; this is the correct behavior for the
  load-bearing raw path (a real API-capture bug should crash loud, not be
  silently swallowed).
- `--verify` all-green on the correctly migrated pair (379 checks passed,
  `VERIFY: PASS`, exit 0), then two deliberate tamper scenarios each
  reproduced with the corresponding automated test
  (`tools/test_migrate_sqlite_to_pg.py::TestRawArchiveVerifyIntegration`):
  deleting one target row (`raw.x_users` `u2`) failed 9 independent checks
  (row count, 5 NULL counts, 2 aggregate mismatches, the sample-row lookup)
  and exited 1 with clear per-check messages; pointing `--raw-files-dir` at
  an empty directory failed the raw_hash-resolution check on all 4
  raw_hash-carrying tables and exited 1, listing the affected hash(es).
  Also exercised the narrower case of one missing hash among otherwise
  resolvable ones (a single extra `reddit_posts_raw` row) to confirm
  detection does not require a wholesale-missing directory.
- Full automated suite: `CIVIC_TEST_POSTGRES_DSN=... python -m unittest
  tools.test_migrate_sqlite_to_pg -v` — 15 tests, all pass (8 pure
  mapping-function unit tests + 7 integration tests); with the env var unset,
  the 7 integration tests report `skipped` and the 8 unit tests still run
  and pass.
- Container hygiene: every throwaway `postgres:17-alpine` container used
  this task was stopped and removed; `docker ps -a` / `docker volume ls`
  confirmed no leftover containers or volumes after each teardown; the
  scratch fixture directory was removed.

## Follow-ups (tracked in `docs/todos/pg-redesign.md`)

- **Owner action, not done here**: dry-run `migrate_sqlite_to_pg.py
  --raw`/`--archive`/`--verify` against a copy of the production DB. No
  production data exists on this dev machine — the synthetic fixture above
  is the local ceiling; Kobe runs this against a real copy before cutover.
- Performance at production scale (`raw.pages` 1.3M+ rows) was not measured
  in this task — `executemany` per batch is straightforward, not
  micro-optimized; a slow but correct one-time/at-cutover run was judged
  acceptable given the plan's emphasis on correctness over speed for this
  step. Worth timing during the owner's prod dry run.
- Phase 4 onward (ETL rewrite, analysis plumbing, engines, ...) are
  unstarted; this entry covers Phase 3 only.
