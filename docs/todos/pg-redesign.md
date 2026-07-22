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

- [ ] Port `ingest/internal/storage/` to Postgres (`$1` placeholders,
      `ON CONFLICT`, enum casts)
- [ ] Frontier gains `FOR UPDATE SKIP LOCKED`
- [ ] Writers target `raw.*` / `ops.x_api_budget`
- [ ] Storage package branched by DSN scheme so one binary handles both
      backends during the parallel period
- [ ] Frontier prioritization gains per-domain balance quotas read from
      `data/seeds.yaml` (under-represented domains scheduled first,
      prolific domains capped)
- [ ] Investigate and fix the empty Reddit capture (`reddit_posts_raw = 0`
      in prod — creds/seeds/silently broken?)
- [ ] Deploy: new-ingest timers start writing Postgres `raw.*` +
      shared `data/raw/sha256/` (content-addressed = idempotent across
      both stacks)
- [ ] Tests: ported Go tests; frontier state machine + quota scheduling
      against real Postgres

## Phase 3 — Precious-data migration script

- [ ] `tools/migrate_sqlite_to_pg.py` (stdlib `sqlite3` + `psycopg`, no old
      code deps)
- [ ] `--raw` mode: raw tables + `x_api_budget`; epoch -> `to_timestamp`;
      idempotent `ON CONFLICT` keyed on natural PKs (re-runnable at cutover
      for the delta)
- [ ] `--archive` mode: verbatim import per the archive schema (evals only
      if nonzero)
- [ ] `--verify` mode: the verification battery (row counts, NULL counts,
      min/max/sum over key columns, random 500-row field sample, raw_hash
      -> file resolution, PK uniqueness)
- [ ] Tested against a copy of the production DB

## Phase 4 — ETL rewrite

- [ ] `analysis/src/etl/registry_sync.py` (YAML -> entities/aliases/curated
      profiles)
- [ ] `analysis/src/etl/authors.py`
- [ ] `analysis/src/etl/documents.py` (tightened US-politics filter —
      comparecards-class non-political domains must fail it; 30-day rule
      carried over; per-window per-domain doc cap for sample balance;
      `pg-` `etl_version` stamp)
- [ ] `analysis/src/etl/queue.py` (seeds `ops.task_queue`)
- [ ] Retire `analysis/src/etl/loader.py` (740 lines)
- [ ] Tests: doc counts vs old within tolerance; FK integrity; idempotent
      re-run; filter rejects known-leak domains (e.g. comparecards.com)

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
      `author_leans` / `narrative_leans` / `entities.party,lean`)
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
