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
- [ ] Empty-tweet capture filter (ingestion) — owner deferred 2026-07-23

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
- [x] `analysis/src/etl/documents.py` admission-gate decomposition
      (Phase 6 Wave 1, 2026-07-23): `is_index_page` split into a shared
      `_TextStats` computation + four named predicate functions; per-source
      `_admit_news_pretext`/`_admit_news_posttext`/`_admit_reddit`/`_admit_x`
      verdict functions (`AdmissionVerdict`) replace the inline if/continue
      chains; `DocLoadResult.rejections` adds a reason-keyed tally alongside
      the existing named counters for rejection observability — see
      `docs/audit-trail/analysis/2026-07-23-pg-engines-wave1.md`
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

- [x] `analysis/src/llm/client.py` — single retry/backoff/schema-validation
      wrapper over a `complete_once()` transport hook; Gemini/Ollama/
      OpenAICompat backends gain the hook additively (their own `complete()`
      retry loops stay as a compatibility shim for the pre-Phase-6 engines
      still calling them directly — full transport-only shrink lands per
      engine as Phase 6 ports them onto `llm/client.py`)
- [x] `analysis/src/results/store.py` — `open_run() -> RunHandle`, typed
      per-task writers, `finish()` flips `is_current` transactionally
      (only module writing `analysis.*` results); `analysis.runs.error`
      restored as its own column (owner decision 2026-07-22, pgAdmin
      readability) — see
      `docs/audit-trail/analysis/2026-07-22-pg-analysis-plumbing.md`
- [x] `analysis/src/engine/validation.py` + one constants module (evidence-
      span validator, ends the 3-vs-4-word / 0.2-vs-0.3-cap drift)
- [x] Prompt-version registration carried over into `analysis.prompt_versions`
      (`results/store.py::register_prompt_version`, idempotent upsert)
- [x] Tests: retry/validation/store against real Postgres (`is_current`
      flip, partial-unique enforcement, transactionality) — clean-room
      verified 2026-07-22 against a throwaway `postgres:17-alpine`
      container: full suite 598 tests / 0 skips gated on
      `CIVIC_TEST_DATABASE_URL`, 598/37 skipped ungated, all pass; a
      throwaway cross-component smoke script exercised `llm/client.py` +
      `engine/validation.py` + `results/store.py` together (register
      prompt version, open a run, save a validated sentiment row, finish,
      confirm `is_current`, then a second run supersedes the first) — see
      the audit-trail entry above for the full matrix

## Phase 6 — Engines

- [x] `bot` engine ported to `analyze(doc) -> dataclass` + store call —
      landed as `analysis/src/engine/bot_detection.py` (deterministic
      stylometric/account signal battery always runs and feeds the LLM
      prompt; a successful call is `hybrid`, no heuristic-only fallback --
      an LLM failure or unavailable backend records a failed run,
      identical to text.py's contract); `refresh_author_bot_scores()` is
      the bot rollup, a plain SQL aggregate over `bot_signals` (below) —
      see `docs/audit-trail/analysis/2026-07-23-pg-engines-wave3.md`
- [x] `analyzer` (text -> sentiment + favorability) ported — landed as
      `analysis/src/engine/text.py` (pure `analyze()` + thin `process()`,
      injected `LLMClient` + `EntityResolver`; no heuristic fallback, a
      failed/unavailable LLM call is a recorded failed run) — see
      `docs/audit-trail/analysis/2026-07-23-pg-engines-wave1.md`. Owner
      decision 2026-07-23: the trivial-content short-circuit is a `done`
      deterministic run with NO `sentiment_results` row (replacing the
      ported neutral-at-0.5 placeholder; `TRIVIAL_CONTENT_CONFIDENCE`
      deleted) -- unanalyzable is not neutral. See wave 3 entry.
- [x] `target_extractor` ported — landed as `analysis/src/engine/targets.py`
      (pure `analyze()` + thin `process()`; `target_mentions.entity_id` is
      NULLABLE, so unresolved targets are kept, not dropped, unlike
      favorability_stances) — see
      `docs/audit-trail/analysis/2026-07-23-pg-engines-wave2.md`
- [x] `propaganda_detector` ported — landed as `analysis/src/engine/
      propaganda.py` (loaded-language pre-filter as a deterministic run;
      `propaganda_results` gains `techniques_validated`/`techniques_dropped`
      restored end to end — DDL, store, engine, tests); see the audit-trail
      entry above
- [x] `claim_extractor` ported — landed as `analysis/src/engine/claims.py`
      (evidence failure drops the claim entirely, no confidence-cap path,
      because `claims` anchors narratives) — see the audit-trail entry above
- [x] `citation_extractor` ported (now emits a run row) — landed as
      `analysis/src/engine/citations.py` (pure `extract()`/`resolve_candidates()`
      + thin `process()`; deterministic, confidence 1.0); reference-column
      restoration (`corpus.x_posts.referenced_tweet_id`/`referenced_tweet_type`)
      closed the same day — see the audit-trail entry above
- [x] `account_classifier` ported — landed as
      `analysis/src/engine/account_tier.py`: deterministic
      `classify_authors()`, no LLM, no `analysis.runs` row (`author_profiles`
      is a `corpus` table, not an analysis result). Elected/affiliated fix:
      `corpus.entities.elected` (added 2026-07-23) is the curated truth
      the tier derives from -- TRUE -> `elected_official`, FALSE ->
      `affiliated` (cabinet secretaries, agency heads, party chairs), NULL
      defaults cautiously to `affiliated` with a warning log. Seeded 2026-07-23:
      539 elected / 12 affiliated / 36 NULL (outlet/subreddit, not
      applicable) — see `docs/audit-trail/analysis/2026-07-23-pg-engines-wave3.md`
- [x] `narrative_clusterer` ported (`clustering_runs` provenance; revisit
      fragmentation thresholds — 8,477 narratives / 7,116 docs today) —
      landed as `analysis/src/engine/narrative_clustering.py`: fragmentation
      fix (`MIN_NARRATIVE_SUPPORT = 2`, a claim matching nothing is no
      longer materialized as a one-doc narrative); `narrative_docs.
      added_by_run` (added 2026-07-23) gives run-precise extension
      provenance — see the wave 3 entry
- [x] Bot rollup becomes a plain SQL aggregate over `bot_signals` — landed
      as `engine/bot_detection.py::refresh_author_bot_scores()` (above)
- [x] New deterministic `analysis/src/engine/lean_derivation.py` stage
      (writes `analysis.author_leans` + `analysis.narrative_leans`) — see
      `docs/audit-trail/analysis/2026-07-23-pg-scheduler-wave4.md`
- [x] `analysis/src/common/entity_resolver.py` — DB-backed `EntityResolver`
      replacing YAML `entity_registry` resolution for the new stack (loads
      `corpus.entities`/`corpus.entity_aliases` once per construction into
      an in-memory map; `resolve()` is a pure lookup) — see
      `docs/audit-trail/analysis/2026-07-23-pg-engines-wave1.md`
- [ ] Test fixtures moved from sqlite tempfiles to a Postgres test schema

## Phase 7+ — Approved additions (not yet scheduled)

- [ ] Officials backfill + admission_class workstream (approved
      2026-07-23, see plan Addition section): `corpus.documents.
      admission_class` enum ('sampled'/'official_record'); Go
      `civic-ingest backfill-officials` one-time historical fetch
      (editorial 16 -> N=100, promoted 533 -> N=25, own spend cap flag);
      `serving.entity_profiles` all-time per-entity rollup (Phase 9).

## Phase 7 — Scheduler

- [x] `analysis/src/scheduler/stages.py` (`StageSpec` dataclass, SKIP LOCKED
      claim loop, workers write via their own pooled connections) — see
      `docs/audit-trail/analysis/2026-07-23-pg-scheduler-wave4.md`
- [x] New `analysis/src/scheduler/pipeline.py` (registry + budget guard +
      `pipeline_runs` recording), invoked via `run.sh analyze-pg`; the old
      `scheduler/job_runner.py` (its `_map_llm_concurrent` serial-write-back
      machinery included) stays the live `run.sh analyze` entry point until
      Phase 11 cutover, not rewritten in place
- [x] Tests: claim/complete/fail/retry/stale-reclaim
- [ ] End-to-end `--limit 20` run against Ollama on dev

## Phase 8 prep — Officials backfill + admission_class (2026-07-23)

- [x] `civic-ingest backfill-officials --spend-cap-usd <N>` built and tested
      (`ingest/internal/runner/backfill_officials.go` +
      `backfill_officials_test.go` + `backfill_officials_integration_test.go`)
      — see `docs/audit-trail/ingestion/2026-07-23-backfill-officials.md`
- [x] `corpus.documents.admission_class` migration + ETL landed
      (`data/pg-migrations/0003_admission_class.sql`,
      `analysis/src/etl/documents.py`, `analysis/src/etl/constants.py`) —
      see `docs/audit-trail/analysis/2026-07-23-admission-class.md`
- [x] Cross-cutting fix: `ingest/internal/config/config.go` strict YAML
      decoding now tolerates `data/seeds.yaml`'s Python-only `domain_filter`
      section (was breaking every Go CLI command) — see the ingestion
      entry above
- [ ] Owner-run: actual production backfill via `civic-ingest
      backfill-officials`, invoked once with a real `--spend-cap-usd`
      chosen against the live X budget
- [ ] Owner decision: tune `OFFICIAL_RECORD_PER_AUTHOR_CAP` (currently 200,
      a placeholder) once real per-official `official_record` volume from
      the production backfill run above is visible

## Phase 8 — Recompute run

Live state 2026-07-24 (box-side, not verifiable from the repo): corpus at
2,758 docs (966 X + ~1,800 news). `bot`, `text`, `targets`, `propaganda`,
`citations` are all 2,758 done / 0 failed. `claims` is partial (~950 done)
because Gemini prepayment credits ran out for the third time. Every claims
failure is at `attempts=1`, so stage-start auto-requeue
(`MAX_TASK_ATTEMPTS=3`) picks them up — no manual `UPDATE` needed.

- [x] Pilot `--limit 200` on the box: measure per-doc latency, token cost,
      PG memory under `docker stats`
- [x] Kobe picks the backend (Gemini vs Ollama) with real numbers — Gemini
- [ ] Full recompute in resumable chunks via the queue (crash resumes free)
      — IN FLIGHT, blocked on a Gemini credit top-up sized for ~2k
      remaining claims calls plus 1-2 more ETL passes (news articles cost
      several times more per call than X posts). Probe with a single
      `curl generateContent` before relaunching; relaunch on each
      `pipeline complete` until ETL logs `inserted=0` (per-run candidate
      batch cap; expect a final news corpus of 2-4k of the ~12,977
      in-window candidates).
- [ ] Acceptance gate 1: queue drains, failure rate <5% per task
- [ ] Acceptance gate 2: 20-doc random traceability audit (doc -> is_current
      run -> typed result -> prompt_version -> raw_hash -> file on disk).
      `has_rows=false` is only a failure for `bot`/`propaganda`;
      `text`/`targets`/`claims`/`citations` may legitimately be empty
- [ ] Acceptance gate 3: whole-corpus contract-violation sweep (llm runs
      missing prompt/confidence/raw_response; done bot/propaganda runs with
      no result row; failed runs with no error) — every count must be 0
- [ ] Acceptance gate 4: every DISTINCT `corpus.documents.raw_hash` resolves
      to a file under `/var/lib/civic-lens/data/raw/sha256` (0 missing)

Paste-ready verification commands for all four gates:
`docs/deployment/phase8-acceptance-gates.md`.

## Phase 9 — Strictly-live API (serving schema dropped)

Owner decision 2026-07-24 reversed the original serving-rollup design this
section used to describe: no `serving.*` schema, no rebuild job. Panels
aggregate `corpus.*`/`analysis.*` directly at request time. See
`docs/audit-trail/analysis/2026-07-24-phase9-prewave.md` for the reasoning
and `docs/audit-trail/api/2026-07-24-phase9-wiring-review-docs.md` (plus
its cross-linked sibling entries) for the full wave.

- [x] `data/pg-migrations/0004_drop_serving.sql` — `serving` schema (never
      had a writer) dropped
- [x] `analysis/src/api/queries/` (`base.py`, `constants.py`) — shared
      read-side helpers (window cutoffs, admission-class predicates,
      evidence-sample builder) and the ported aggregation floors/caps
- [x] `analysis/src/api/models/` (`common.py`: `CamelModel`, `LeanLabel`,
      `RangeMeta`, `SampleDocModel`) — the response-shape contract every
      panel model builds on
- [x] Presentation invariant (owner decision 2026-07-22): fact/curated/derived
      lean labels visually+verbally distinct; derived lean always with
      `lean_share`/`confidence`/`sample_count`; lean never fed to LLM
      prompts — codified in `.agent/rules/media-analysis.md`'s Phase 9
      section
- [x] Seven panel query modules + routers query `corpus`/`analysis` directly
      (sentiment, entities, narratives, propaganda, bots, outlets, movers) —
      no `serving.*` anywhere
- [x] `analysis/src/review/` (`service.py`, `constants.py`) reads
      `analysis.runs`, writes `analysis.evals` + `analysis.golden_labels`
      (golden minted in the same transaction as the eval)
- [x] `GET /docs/{doc_id}` universal drill-down (`api/queries/docs.py`,
      `api/models/docs.py`, `api/routers/docs.py`) — core fields, subtype
      extras, every current analysis result, citations in/out; no time
      predicate, resolves regardless of document age
- [x] `GET /snapshot-status` reads the latest `ops.pipeline_runs` row
      (`serving.refreshes` no longer exists)
- [x] `GET /eval-accuracy` — live aggregate via the review service, public
      floor applied
- [x] `api/routers/admin.py` rewritten: run-trigger endpoints call
      `scheduler/pipeline.py`; the cache-status endpoint is replaced by
      `GET /pipeline-runs`
- [x] `api/routers/health.py` rewritten: Postgres pool `SELECT 1`, not
      sqlite3
- [x] `server.py` + `routers/__init__.py` mount every v1 router (the seven
      panels, admin, docs, review, sentiment already listed above, status)
      plus the unversioned health router; `api/routers/data.py` +
      `api/cache_utils.py` deleted
- [x] Fresh `CamelModel`-based pydantic response models throughout
- [x] API contract snapshot tests (`analysis/tests/contract/`) are the new
      UI contract
- [ ] Retire `common/cache.py`, `reporting/models/aggregator_models.py`
      (785 lines), `data/cache/` — deferred to Phase 11: the old
      `reporting/`/`scheduler/job_runner.py` stack still reads them and
      stays live until cutover

## Phase 10 — UI adaptation

Landed on `ui-data-contract-rewrite` (commit `db8c9ef`) — see
`docs/audit-trail/ui/2026-07-24-phase10-ui-adaptation.md` for the full
inventory, including the pre-redesign features removed rather than faked.

- [x] Regenerate `ui/src/types.ts` from the API contract
- [x] Update `services/api.ts` (tolerance shrinks — shapes are now
      trustworthy); `services/transformers.ts` deleted outright rather than
      updated, since the contract is trustworthy field-for-field
- [x] Page wiring updates where fields changed
- [x] Verify: `npm run typecheck` + `npm run build` — both clean
      2026-07-25 (only the pre-existing >500 kB chunk-size warning)
- [ ] Verify: manual pass of every tab against the live API — owner-run,
      blocked until the Phase 8 recompute finishes (empty panels are
      otherwise indistinguishable from broken ones)

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
- [ ] Repoint the four production entrypoints at `scheduler.pipeline` BEFORE
      deleting `job_runner.py` — `docker-compose.yml:133`, `run.sh:127`
      (`analyze`), `setup-cron.sh:59`, and
      `deploy/systemd/civic-lens-analyze.service` all still run the retired
      SQLite stack. Deleting the module first stops production analysis.
- [ ] Delete `settings.llm_enabled` (`analysis/src/common/settings.py`) and
      the `CIVIC_LLM_ENABLED` entry in `.env.example`. No engine in the
      Postgres pipeline reads it (2026-07-25 audit) and `/health` no longer
      reports it, but `job_runner.py` plus `engine/{analyzer,bot,
      propaganda_detector,target_extractor,claim_extractor}.py` still do —
      so it can only go when they do. See
      `docs/audit-trail/api/2026-07-25-drop-unhonored-llm-switch.md`.
- [ ] Delete the heuristic engines retired on 2026-07-25 along with the old
      stack: `engine/analyzer.py`, `engine/bot.py`, and the constants only
      they use (`PROXIMITY_WINDOW`, `POSITIVE_WORDS`, `NEGATORS`,
      `GOP_ENTITIES`, `FAVORABLE_INDICATORS`, `UNFAVORABLE_INDICATORS`, and
      `NEGATIVE_WORDS`/`INTENSIFIERS` once `propaganda_detector.py` goes).
      See `docs/audit-trail/analysis/2026-07-25-llm-only-judgments.md`.
- [ ] Rewrite CLAUDE.md data-flow + architecture docs
- [ ] Audit-trail entries per affected layer
- [ ] Drop `analysis.favorability_stances` (0001_north_star.sql): its writer
      was removed 2026-07-25 (`engine/text.py` sentiment-only rewrite,
      `results/store.py` has no `save_favorability_stances` path) and every
      reader repointed to `analysis.target_mentions`. Deliberately NOT
      dropped in that same change -- data loss is irreversible and the full
      recompute + side-by-side acceptance above is still ahead. Drop it in
      the same decommission migration as the rest of the retired surface;
      existing rows are Republican-only (the old prompt scoped favorability
      to GOP entities), so do not resurrect them as a general-purpose stance
      source. See `docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md`.
- [ ] Delete this todo file when every box above is checked
