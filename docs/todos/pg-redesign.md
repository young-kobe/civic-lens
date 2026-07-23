# Postgres redesign — master checklist

Full-stack rebuild onto self-hosted Postgres 17: six namespaced schemas
(raw/corpus/analysis/serving/ops/archive), recompute (not translate) all
derived data, build in parallel with the live SQLite stack, then cut over.
Authority: plan `has-our-aggregate-method-async-frog` (2026-07-22). One
section per plan phase. Delete this file when every box is checked — the
audit-trail entries under `docs/audit-trail/<layer>/` are the permanent
record.

## Phase 1 — Infrastructure

- [ ] VPS resize to >=4GB (CPX21/CPX31) — prerequisite before PG can coexist
      with the current stack (measured 2026-07-22: box is CPX11-class,
      ~600Mi free of 1.9Gi). Kobe's manual action.
- [x] `postgres:17-alpine` service in `docker-compose.yml` (pgdata volume,
      `127.0.0.1:5432`, `mem_limit 768m`, `shared_buffers=256MB`,
      `max_connections=30`)
- [x] `./run.sh pg` helper so dev runs the same service
- [x] `data/pg-migrations/0001_north_star.sql` — full greenfield DDL for the
      six schemas (`ops` bootstrap collision fixed 2026-07-22 — see
      `docs/audit-trail/infra/2026-07-22-pg-phase1-infrastructure.md`)
- [x] Port the Go migration runner (`ingest/internal/storage/db/db.go`) to
      Postgres, tracked in `ops.schema_migrations`
- [x] `analysis/src/common/db.py` — psycopg3 `ConnectionPool` singleton,
      `dict_row`, schema-qualified SQL (no `search_path` magic)
- [x] `CIVIC_DATABASE_URL` setting in `analysis/src/common/settings.py`
- [x] `psycopg[binary,pool]` added as a dependency
- [x] Rewrite `docs/DATABASE_SCHEMA.md` to document the north-star schema,
      marked "target — not yet live"
- [x] Tests: migration applies clean; Go + Python round-trip TIMESTAMPTZ
      (live-validated 2026-07-22 against a throwaway Postgres 17 container:
      clean apply, idempotent re-run, per-schema table counts 5/8/18/9/4/11
      match the DDL exactly, Python `TestPoolRoundTripAgainstRealPostgres`
      passed)
- [x] Phase 1 audit-trail entries written (infra, ingestion, analysis —
      see cross-links above)

## Phase 2 — Go ingestion port

- [x] Port `ingest/internal/storage/` to Postgres (`$1` placeholders,
      `ON CONFLICT`, enum casts) — see `internal/frontier/` and
      `internal/runner/` `_postgres.go` siblings
- [x] Frontier gains `FOR UPDATE SKIP LOCKED`
- [x] Writers target `raw.*` / `ops.x_api_budget`
- [x] Storage package branched by DSN scheme so one binary handles both
      backends during the parallel period (`db.DB.IsPostgres()`)
- [x] Frontier prioritization gains per-domain balance quotas read from
      `data/seeds.yaml` (under-represented domains scheduled first,
      prolific domains capped) — `CrawlBalanceConfig`, Postgres-only,
      absent section = unchanged behavior
- [x] Investigate the empty Reddit capture (`reddit_posts_raw = 0` in
      prod): root cause confirmed as Reddit blocking datacenter IPs (not
      a code/creds bug); no code fix applies — see
      `docs/audit-trail/ingestion/2026-07-22-pg-ingestion-port.md` for the
      disposition and the re-enablement item below
- [ ] Deploy: new-ingest timers start writing Postgres `raw.*` +
      shared `data/raw/sha256/` (content-addressed = idempotent across
      both stacks) — Kobe's manual action, blocked on the Phase 1 VPS
      resize
- [x] Tests: ported Go tests; frontier state machine + quota scheduling
      against real Postgres (clean-room verified 2026-07-22: sequential
      and parallel (`-p 4`) gated runs both pass against a throwaway
      container; see audit-trail entry above for the full matrix)
- [ ] Reddit fetcher placement at cutover: run the fetcher from a
      residential network connection (not the VPS) writing to the
      networked Postgres instance with owner-scoped credentials, since
      Reddit blocks datacenter-IP source addresses — Kobe's manual
      action at cutover

## Phase 3 — Precious-data migration script

- [x] `tools/migrate_sqlite_to_pg.py` (stdlib `sqlite3` + `psycopg`, no old
      code deps)
- [x] `--raw` mode: raw tables + `x_api_budget`; epoch -> `to_timestamp`;
      idempotent `ON CONFLICT` keyed on natural PKs (re-runnable at cutover
      for the delta)
- [x] `--archive` mode: verbatim import per the archive schema (evals only
      if nonzero)
- [x] `--verify` mode: the verification battery (row counts, NULL counts,
      min/max/sum over key columns, random 500-row field sample, raw_hash
      -> file resolution, PK uniqueness)
- [x] Tested against a synthetic fixture DB (all 25 `data/migrations/*.sql`
      applied, every mapped table + edge case exercised) and a throwaway
      `postgres:17-alpine` container — see
      `docs/audit-trail/infra/2026-07-22-sqlite-to-pg-migration-tool.md`;
      `tools/test_migrate_sqlite_to_pg.py` (gated on
      `CIVIC_TEST_POSTGRES_DSN`) is the repeatable form
- [ ] Owner action: dry-run `migrate_sqlite_to_pg.py --raw`/`--archive`/
      `--verify` against a copy of the production DB — no prod data exists
      on this dev machine, so the synthetic fixture is the local ceiling;
      this run is Kobe's, against a real copy, before cutover

## Phase 4 — ETL rewrite

- [x] `analysis/src/etl/registry_sync.py` (YAML -> entities/aliases/curated
      profiles). Lands with the owner's political-lean single-convention
      decision (one flat `corpus.political_lean` enum, one `lean` column
      everywhere, no separate `party` column anywhere in the schema —
      supersedes the plan's original two-column party/lean sketch):
      flattening constants in `analysis/src/common/registry.py`,
      `lean_source` provenance, never fed into an LLM prompt — see
      `docs/audit-trail/analysis/2026-07-22-pg-lean-unification-registry-sync.md`.
      **Retired same day** by owner decision: DB-native curation (source of
      truth moves from YAML-in-git to `corpus.entities` itself, for
      readability of hands-on curation). `registry_sync.py` and its test
      file are deleted; `data/pg-migrations/0002_entity_registry_seed.sql`
      is the one-time replacement (shipped — seeded 587 entities / 30
      aliases from the final sync of the real YAMLs). `common/registry.py`
      slimmed to only the two canonicalizers `documents.py` still needs. See
      `docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md`.
- [x] Freeze the four registry YAMLs (one-line header comment each,
      2026-07-22): they stay in git, read-only, only for the old-stack's
      `analysis/src/reporting/entity_registry.py` until it retires in Phase
      9, at which point the YAMLs can be deleted too.
- [x] `analysis/src/etl/authors.py` (X authors from `raw.x_users`; Reddit
      is a documented no-op — `raw.reddit_posts` carries no author column;
      news gets no synthetic author — see
      `docs/audit-trail/analysis/2026-07-22-pg-etl-authors-documents-queue.md`).
      Accepted deviation: news docs carry `author_id NULL` always — outlets
      are `corpus.entities` (`kind='outlet'`), never a synthetic per-domain
      `corpus.authors` row; outlet identity is read via the
      `news_articles.outlet_entity_id` FK (below), not an author join.
- [x] `analysis/src/etl/documents.py` (tightened US-politics filter —
      word-boundary matching closes the bill/billion-class hole,
      `www.comparecards.com` seeded in `data/seeds.yaml`'s new
      `domain_filter.deny`; 30-day rule carried over; per-window
      per-domain doc cap (`max_docs_per_domain_per_window`) for sample
      balance; `pg-1` `etl_version` stamp). `news_articles.outlet_entity_id`
      / `reddit_posts.subreddit_entity_id` FKs (plan-specified, briefly
      flagged as a discrepancy against the first-landed 0001, now added to
      both the DDL and documents.py — resolved by this closure) resolve at
      ETL time by canonicalizing `domain`/`subreddit` against the curated
      `entity_key`/alias set (`analysis/src/common/registry.py`); NULL when
      unmatched (never blocks a doc); a `documents.py` re-run backfills the
      FK once the entity is later curated into `corpus.entities` — see the
      closure audit-trail entry above and, for the DB-native-curation
      reversal, `docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md`.
- [x] `analysis/src/etl/queue.py` (seeds `ops.task_queue` per a
      job_runner.py-derived task-applicability matrix; `account_tier`
      excluded — author-scoped, not doc-scoped; `reset_stale_in_progress`
      included for Phase 7 to call)
- [ ] Retire `analysis/src/etl/loader.py` (740 lines) — spans Phases 4-7
- [x] Tests: `test_etl_authors.py` / `test_etl_documents.py` /
      `test_etl_queue.py`, 60 tests (40 no-DB, 20 gated on
      `CIVIC_TEST_DATABASE_URL`) — FK integrity, idempotent re-run, cap
      enforcement, deny-list rejection, outlet/subreddit entity FK
      resolution (matched/unmatched/backfill-after-later-curation) all
      live-verified against a throwaway `postgres:17-alpine` container with
      0001 applied. (`test_registry_sync.py` verified `registry_sync.py` +
      `common/registry.py` the same way at the time; both the module and
      its test file are deleted post-retirement — see
      `docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md`.)
- [x] Owner decision (decided 2026-07-22): promote-all, with an editorial
      flag. Every curated account in `known_political_x_accounts.yaml`
      (549 unique people) is promoted to its own `corpus.entities` row
      (`kind='official'`), `editorial=false`; the 3 hand-edited registries
      stay `editorial=true`. Originally implemented in `registry_sync.py`
      (`_sync_promoted_officials`, since deleted); `entities.editorial`
      column added to `0001_north_star.sql`; the promoted rows themselves
      now live in `0002_entity_registry_seed.sql` — see
      `docs/audit-trail/analysis/2026-07-22-pg-lean-unification-registry-sync.md`
      and `docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md`.
- [ ] Future decision: `corpus.entity_kind` has a `'collective'` value with
      no populating YAML/logic yet (no registry loader writes
      `kind='collective'` today) — decide what populates it (e.g. parties,
      caucuses, PACs) or drop the value if it stays unused.

## Phase 5 — Analysis plumbing

- [ ] `analysis/src/llm/client.py` — single retry/backoff/schema-validation
      wrapper; backends become transport-only
- [ ] `analysis/src/results/store.py` — `open_run() -> RunHandle`, typed
      per-task writers, `finish_run` flips `is_current` transactionally
      (only module writing `analysis.*` results)
- [ ] `analysis/src/engine/validation.py` + one constants module (evidence-
      span validator, ends the 3-vs-4-word / 0.2-vs-0.3-cap drift)
- [ ] Prompt-version registration carried over into `analysis.prompt_versions`
- [ ] Tests: retry/validation/store against real Postgres (`is_current`
      flip, partial-unique enforcement, transactionality)

## Phase 6 — Engines

- [ ] `bot` engine ported to `analyze(doc) -> dataclass` + store call
- [ ] `analyzer` (text -> sentiment + favorability) ported
- [ ] `target_extractor` ported
- [ ] `propaganda_detector` ported
- [ ] `claim_extractor` ported
- [ ] `citation_extractor` ported (now emits a run row)
- [ ] `account_classifier` ported
- [ ] `narrative_clusterer` ported (`clustering_runs` provenance; revisit
      fragmentation thresholds — 8,477 narratives / 7,116 docs today)
- [ ] Bot rollup becomes a plain SQL aggregate over `bot_signals`
- [ ] New deterministic `analysis/src/engine/lean_derivation.py` stage
      (writes `analysis.author_leans` + `analysis.narrative_leans`)
- [ ] Test fixtures moved from sqlite tempfiles to a Postgres test schema

## Phase 7 — Scheduler

- [ ] `analysis/src/scheduler/stages.py` (`StageSpec` dataclass, SKIP LOCKED
      claim loop, workers write via their own pooled connections)
- [ ] Rewritten `job_runner.py` (registry + budget guard + `pipeline_runs`
      recording); `_map_llm_concurrent` serial-write-back machinery dies
- [ ] Tests: claim/complete/fail/retry/stale-reclaim
- [ ] End-to-end `--limit 20` run against Ollama on dev

## Phase 8 — Recompute run

- [ ] Pilot `--limit 200` on the box: measure per-doc latency, token cost,
      PG memory under `docker stats`
- [ ] Kobe picks the backend (Gemini vs Ollama) with real numbers
- [ ] Full recompute in resumable chunks via the queue (crash resumes free)
- [ ] Acceptance: queue drains, failure rate <5%
- [ ] 20-doc random traceability audit (doc -> run -> result ->
      prompt_version -> raw_hash -> file on disk)

## Phase 9 — Serving + API

- [ ] `analysis/src/serving/` rollup builders (logic ported from
      `reporting/aggregators/`, joins become FK joins, output becomes
      `serving.*` rows)
- [ ] Bot/narrative/entity rollups gain lean dimensions (join
      `author_leans` / `narrative_leans` / `entities.lean` — one flat lean
      column, no separate `party` column; see the Phase 4 registry_sync
      entry)
- [ ] Presentation invariant (owner decision 2026-07-22): fact/curated/derived
      lean labels visually+verbally distinct; derived lean always with
      evidence counts + continuous lean_share (no spectrum buckets); lean
      never fed to LLM prompts; codify in `.agent/rules/media-analysis.md`
- [ ] API endpoints query `serving.*`
- [ ] `review.py` reads `analysis.runs` / writes `analysis.evals` +
      `analysis.golden_labels`
- [ ] `/entity-posts` and drill-downs query `corpus`/`analysis` directly
- [ ] Fresh pydantic response models
- [ ] Retire `common/cache.py`, `api/cache_utils.py`,
      `reporting/models/aggregator_models.py` (785 lines), `data/cache/`
- [ ] `/snapshot-status` reads `serving.refreshes`
- [ ] API contract snapshot tests become the new UI contract

## Phase 10 — UI adaptation

- [ ] Regenerate `ui/src/types.ts` from the API contract
- [ ] Update `services/api.ts` / `transformers.ts` (tolerance shrinks —
      shapes are now trustworthy)
- [ ] Page wiring updates where fields changed
- [ ] Verify: `npm run typecheck` + `npm run build` + manual pass of every
      tab

## Phase 11 — Verify, cut over, decommission

- [ ] Side-by-side acceptance battery run (every UI tab renders with
      confidence + links; counts/splits within explainable tolerance;
      per-domain composition report; derived leans carry confidence +
      sample counts wherever surfaced)
- [ ] Runbook step 1: disable old timers, old API keeps serving
- [ ] Runbook step 2: final `--raw` delta sync + `--verify`
- [ ] Runbook step 3: top-up recompute
- [ ] Runbook step 4: rebuild rollups, smoke new API on a second port,
      check every UI tab
- [ ] Runbook step 5: flip compose to the new API, enable new timers
- [ ] Runbook step 6: final `sqlite3 .backup` -> age-encrypt -> R2 cold
      artifact, stop litestream
- [ ] Runbook step 7: 48h watch
- [ ] Decommission: remove litestream service + `deploy/litestream.yml`
- [ ] `deploy/backup.sh` becomes `pg_dump -Fc` + age + rclone (same
      timer/retention pattern)
- [ ] Update systemd units
- [ ] Drop `modernc.org/sqlite` + the SQLite storage branch
- [ ] Delete dead modules
- [ ] Rewrite CLAUDE.md data-flow + architecture docs
- [ ] Audit-trail entries per affected layer
- [ ] Delete this todo file when every box above is checked
