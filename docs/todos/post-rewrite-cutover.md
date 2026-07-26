# Post-rewrite cleanup and Postgres cutover

Endgame of the pg-redesign: fix the deploy surface, delete the legacy SQLite
analysis stack, consolidate docs, rewrite git history into clean milestones,
and cut production over by pushing the rewritten main. Plan of record:
`~/.claude/plans/toasty-munching-conway.md` (session plan); supersedes the
Phase 11 block in `pg-redesign.md`.

Roles: Claude edits files and generates command blocks; Kobe runs every git
commit/branch/push and every prod-box command. Nothing touches origin/main
until the single Phase 6 push, which is the cutover.

## Phase 0 — Safety snapshot + baseline (Kobe)

- [ ] Tag `pre-cutover-main` (origin/main 7f4ce6e) and `pre-rewrite-branch`
      (post-rewrite-repo-cleanup 827f495); push both tags
- [ ] `git bundle create ~/civic-lens-pre-rewrite.bundle --all`
- [ ] Baseline green: Python suite (with throwaway PG on 55434), go test,
      ui typecheck+build, eval gate

## Phase 1 — Deploy-surface fixes (blocking for merge)

- [x] `docker-compose.yml`: analyze command -> `scheduler.pipeline`;
      `depends_on: postgres (service_healthy)` on api/analyze/ingest;
      refresh stale header comments
- [x] `deploy/deploy.sh`: SQLite migrate -> PG migrations (`data/pg-migrations/`)
- [x] `ingest/docker-entrypoint.sh` + `db_postgres.go`: pg-migrations path
      CWD-independent; fail loudly when the dir is missing/empty
- [x] systemd units: analyze (ExecStartPre + ExecStart), crawl, x -> PG DSN
- [x] `.env.example`: document `@postgres:5432` (container) vs
      `@127.0.0.1:5432` (host) DSN forms
- [x] `deploy/backup.sh` -> `pg_dump -Fc` + age + rclone
- [x] `deploy/install.sh`: timer list, drop sqlite3 apt dep
- [x] `run.sh`: `analyze` dispatches `scheduler.pipeline`; retire `analyze-pg` alias
- [x] Delete `setup-cron.sh`
- [x] Fix `deploy/scripts/seed-initial.sh` job_runner reference
- [x] Rewrite `deploy/README.md` (incl. monitoring off dead `/api/v1/cache-status`)
- [x] `CIVIC_BUDGET_SECONDS`: verify pipeline honors it or drop from compose/unit
- [x] Gate: `docker compose config` passes; `bash -n` all scripts; tests green

## Phase 2 — Dead-code deletion

- [x] Repoint `analysis/evals/run_eval.py` + `tests/test_eval_runner.py`
      from `engine.claim_extractor` to `engine/claims.py` (CI gate)
- [x] Delete legacy island: `scheduler/job_runner.py`; engine
      {analyzer,bot,propaganda_detector,claim_extractor,citation_extractor,
      narrative_clusterer,account_classifier,target_extractor}.py +
      `engine/models/`; `etl/{loader,polling}.py`;
      `common/{cache,alerts,schema_guard}.py`; all of `src/reporting/`
- [x] Delete 39 legacy-only test files + `test_workflow.py`;
      split `test_text_prep.py` (keep trivial-content/truncation cases)
- [x] Delete `ingest/cmd/stats/`, `data/civic_prod_copy.db*`, `data/cache/`
- [x] `common/settings.py` + `.env.example`: remove `llm_enabled`,
      `llm_concurrency`, `db_path`, `cache_dir`; trim comment bulk
- [ ] Defer: Go SQLite backend, `data/migrations/*.sql`, litestream,
      `tools/migrate_sqlite_to_pg.py` (Phase 7)
- [x] Gate: full battery green; no stray references outside audit-trail

## Phase 3 — Docs consolidation

- [ ] Delete todos (salvage first): backend-aggregator-audit,
      cross-tier-narrative-clustering, news-visibility-prod, containerization
      (Flash-Lite item -> eval-expansion), bot-propaganda-entity-signals
      (2 live questions -> fresh todo), ui-rework (R-3 -> ui-feature-restoration)
- [ ] Superseded banners: `docs/proposals/scale-out-and-targeted-classification.md`,
      `docs/deployment/plan.md`
- [ ] Rewrite: CLAUDE.md, README.md, docs/ARCHITECTURE_DIAGRAM.md,
      .agent/workflows/{python-ai-reporting,global,go-ingestion}.md,
      docs/SCORING_METHODOLOGY.md, docs/DATABASE_SCHEMA.md, docs/INVARIANTS.md,
      .agent/rules/code-style.md, todos {eval-expansion,dead-code-cleanup,
      ui-consistency-audit}
- [ ] Collapse 67 walkthroughs into per-layer `docs/audit-trail/<layer>/timeline.md`;
      delete originals; repoint ~6 live-code refs + doc links
- [ ] Comment trim: engine/constants.py, engine/text.py, engine/targets.py,
      bot_detection.py, ui/src/services/dedupe.ts, ui/src/App.tsx
- [ ] Gate: no `walkthroughs/` refs; ui typecheck+build green

## Phase 4 — Full verification of the final tree (Kobe)

- [ ] Complete battery incl. deploy.yml gate steps (`pip-audit --strict`,
      `npm audit --audit-level=high`), `docker compose config` + `build`
- [ ] Push branch; ci.yml green on GitHub
- [ ] Record VERIFIED_SHA / VERIFIED_TREE

## Phase 5 — History rewrite (local only)

- [ ] Claude generates commit-tree replay script (~14 old-main milestones +
      ~12 branch phase commits + cleanup commits, plain imperative messages)
- [ ] Kobe builds `main-rewritten`; verify tree byte-identical to VERIFIED_TREE

## Phase 6 — Cutover (the one push; Kobe)

- [ ] Box pre-flight: rotate PG password; purge `/var/tmp/*.log`; verify
      `/etc/civic-lens.env` (POSTGRES_*, CIVIC_DATABASE_URL @postgres:5432,
      CIVIC_ANALYZE_CONCURRENCY, CIVIC_PG_POOL_MAX,
      CIVIC_NARRATIVE_EMBEDDING_MODEL=gemini-embedding-001, absolute
      CIVIC_RAW_STORE_DIR); final SQLite cold artifact; first manual pg_dump
- [ ] Same-day `pip-audit --strict` + `npm audit` re-run
- [ ] `git branch -f main main-rewritten`;
      `git push --force-with-lease=main:7f4ce6e origin main`
- [ ] Verify: Actions green; compose healthy; analyze unit runs pipeline;
      API/UI smoke; nothing holds the SQLite file
- [ ] Rollback ready: `git push --force origin pre-cutover-main:main`

## Phase 7 — Post-cutover (after 48h stable)

- [ ] Branch prune: verified merged/superseded table, then delete ~20 local +
      origin branches incl. llm-analysis-pipeline-refactor,
      post-rewrite-repo-cleanup
- [ ] Deferred deletions once PG data verified — EVERY transitional piece
      goes, none survives: Go SQLite backend (`db_sqlite.go`,
      `frontier_sqlite.go`, inline branches, `modernc.org/sqlite` dep,
      `defaultDBPath` sqlite fallback + DSN dispatch), `data/migrations/*.sql`,
      litestream + `deploy/litestream.yml`, `tools/migrate_sqlite_to_pg.py`
      + its test, sqlite materialization block in `ingest/docker-entrypoint.sh`
- [ ] Test-restore first `backup.sh` pg_dump into throwaway container
- [ ] Tick + delete this file and `pg-redesign.md`; final audit-trail entries

## Carried-over open items (survive doc deletion; not this initiative's work)

- [ ] Full recompute completes -> acceptance gates 1-4 (queue drains <5% fail,
      20-doc traceability audit, contract-violation sweep, raw_hash resolution)
- [ ] Manual pass of every UI tab (blocked on recompute)
- [ ] Reddit fetcher placement decision at cutover
- [ ] Empty-tweet capture filter (deferred 2026-07-23)
- [ ] Officials backfill prod run + `OFFICIAL_RECORD_PER_AUTHOR_CAP` tuning
- [ ] End-to-end `--limit 20` Ollama run on dev
- [ ] `corpus.entity_kind = 'collective'` decision
- [ ] `BOT_FLAGGED_SHARE_EXCLUSION = 0.5` recalibration
