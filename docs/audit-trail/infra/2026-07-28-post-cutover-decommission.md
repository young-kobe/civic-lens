# Phase 7 post-cutover decommission (litestream, branches, todos)

**Date**: 2026-07-28
**Layer**: infra
**Cross-links**: analysis: `2026-07-28-drop-favorability-stances.md`,
ingestion: `2026-07-28-delete-go-sqlite-backend.md`

## What the system does now

The deploy surface is Postgres-only with no transitional pieces:

- **Litestream is gone.** `deploy/litestream.yml`, the `litestream` compose
  service, the `LITESTREAM_*` block in `.env.example`, and the dev-override
  disable stanza are deleted; `civic-lens-stack.service` describes the
  stack as caddy + api + postgres. Durability is `deploy/backup.sh`
  (`pg_dump -Fc` + age + rclone to R2) on its existing timer; the final
  frozen-SQLite replica and cold artifact remain in R2 untouched.
- **`data/migrations/` (SQLite) is deleted**; `data/pg-migrations/` is the
  only migration directory. 0008 (favorability drop) is the newest file.
- **Docs describe only the live system**: `analysis/README.md` rewritten
  off the retired loader/reporting/snapshot stack,
  `.agent/workflows/go-ingestion.md` no longer lists a deprecated backend,
  `docs/DATABASE_SCHEMA.md` bumped to 3.3, stale SQLite-era comments in
  `data/seeds.yaml` / compose files reworded.

## Branch prune

All pre-rewrite history is preserved by tags `pre-cutover-main` (7f4ce6e),
`pre-rewrite-branch` (827f495), and the off-box bundle. Verified by
containment before deletion (ancestry against the OLD tags, valid because
both sides predate the history rewrite; tree-compare for the two stragglers):

- 22 numbered workstream branches: ancestors of `pre-cutover-main`.
- 10 rewrite-phase branches (etl-rewrite, engine-rewrtie, go-ingestion-port,
  scheduler-rewrite, serving-api-rewrite, migration-script,
  infrastructure-setup, recompute-pilot, unified-llm-client-and-result-store,
  llm-analysis-pipeline-refactor): ancestors of `pre-rewrite-branch`.
- `114-ui-cleanup-after-repo-rewrite`, `ui-restoration`,
  `full-fidelity-restoration`, `main-rewritten`: contained in the new main.
- `post-rewrite-repo-cleanup` (tip 52151f8, past the tag): its extra
  commits are the phase 1-3 cleanup (replayed into the rewritten main) plus
  the geometry restoration pass, explicitly superseded by the
  full-fidelity restoration that landed on main 2026-07-28.
- `ui-data-contract-rewrite`: one commit past the tag (d52ae51,
  raw_store_dir config + ETL extraction-failure handling), functionality
  present in main.

## Todo close-out

`docs/todos/pg-redesign.md` and `docs/todos/post-rewrite-cutover.md`
deleted — the redesign initiative is complete and this entry plus its
cross-links are the permanent record. Open carried-over items moved to
`docs/todos/recompute-acceptance-and-tuning.md` (recompute acceptance
gates, UI tab pass, Reddit fetcher placement, empty-tweet filter, officials
backfill + cap tuning, collective-kind decision,
BOT_FLAGGED_SHARE_EXCLUSION recalibration, Ollama e2e).

## Backup restore test

Phase 7 gate: the latest `backup.sh` pg_dump is test-restored into a
throwaway `postgres:17-alpine` container on the box before this branch
merges. A restore that errors or reports zero `corpus.documents` rows
fails the gate.
