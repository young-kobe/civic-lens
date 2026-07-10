# 2026-07-10 — Migrations are enforced, not assumed

Two layers now guarantee the analysis pipeline never runs against a
database older than its code (2026-07-10 prod incident: an image with
migration-022 code ran via manual `docker compose run analyze` against an
unmigrated DB and died on `no such table: ai_outputs_latest`).

## What shipped

- `deploy/systemd/civic-lens-analyze.service`: `ExecStartPre` runs
  `ingest migrate` (idempotent no-op when current) before every scheduled
  analyze; a migration failure aborts the unit before analysis touches
  the DB. deploy.sh keeps its own migrate step for image switches.
- `analysis/src/common/schema_guard.py` + a fail-fast check at the top of
  `job_runner.run_full_pipeline`: compares the DB's `schema_version`
  against the highest-numbered migration shipped in the image
  (`data/migrations/NNN_*.sql` — no version constant to maintain). A
  behind-schema DB aborts immediately with the exact migrate command in
  the error, exits non-zero, and fires the OnFailure alerter. A DB NEWER
  than the code is tolerated — deploys migrate before images switch, so
  DB-ahead is the normal ordering. This layer covers invocations that
  bypass systemd entirely (manual compose runs, dev shells).

## Rollout note

- The unit file change requires re-copying to /etc/systemd/system (or
  re-running deploy/install.sh) + `systemctl daemon-reload` on the host.
