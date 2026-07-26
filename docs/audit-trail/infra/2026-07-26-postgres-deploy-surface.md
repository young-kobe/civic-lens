# Deploy surface points at Postgres

**Date:** 2026-07-26
**Layer:** infra (cross-link: [ingestion](../ingestion/2026-07-26-pg-migrations-resolution.md))
**Todo:** docs/todos/post-rewrite-cutover.md

The production deploy path now targets the Postgres stack end to end. Before
this, `deploy/` and the workflows were unchanged from the SQLite era: a push
to main would have migrated SQLite, reinstalled SQLite-era systemd units, and
restarted the retired `scheduler/job_runner.py` pipeline.

## The system as it is now

- `docker-compose.yml`: the `analyze` one-shot runs
  `analysis.src.scheduler.pipeline`; `api`, `ingest`, and `analyze` carry
  `depends_on: postgres (service_healthy)`. Header comments describe postgres
  as the primary datastore and the two DSN vantage points
  (`@postgres:5432` in-container, `@127.0.0.1:5432` on host).
- `deploy/deploy.sh`: the migration step is `docker compose run --rm ingest
  migrate` with no `--db` flag — the CLI defaults to `CIVIC_DATABASE_URL`
  from the compose env_file, which selects the Postgres backend and applies
  `data/pg-migrations/`.
- systemd units: `civic-lens-analyze.service` ExecStartPre/ExecStart both go
  through the Postgres path; `civic-lens-crawl.service` and
  `civic-lens-x.service` drop `--db ${CIVIC_DB_PATH}` for the same env-driven
  default. `civic-lens-backup.service` is the Postgres backup.
- `deploy/backup.sh`: nightly `pg_dump -Fc` over the loopback publish using
  `POSTGRES_USER`/`POSTGRES_PASSWORD` (the sandboxed unit has no docker
  access), age-encrypted, rclone-pushed, 14-day retention. Replaces the
  sqlite3 `.backup` script.
- `deploy/install.sh`: installs `postgresql-client` instead of `sqlite3`;
  no longer pre-creates `data/cache` or manages the SQLite migrations
  symlink; next-steps text names the required Postgres env vars.
- `.env.example`: retired `CIVIC_LLM_ENABLED`, `CIVIC_LLM_CONCURRENCY`,
  `CIVIC_DB_PATH`, `CIVIC_CACHE_DIR`; documents both `CIVIC_DATABASE_URL`
  forms and why the loopback form fails inside containers.
- `run.sh`: `analyze` dispatches `scheduler.pipeline`; the `analyze-pg`
  alias is gone. `setup-cron.sh` (dev cron for the retired stack) and
  `deploy/scripts/seed-initial.sh` (pre-container: repo-root binary +
  `.venv`, neither exists on the box) are deleted.
- `deploy/README.md`: rewritten for the Postgres stack, including restore
  via `pg_restore` and the env checklist.

Litestream (frozen-SQLite replication) intentionally survives until the
final cold artifact is archived post-cutover; its removal is tracked in the
todo alongside the Go SQLite backend and `data/migrations/`.

`CIVIC_BUDGET_SECONDS` stays on the compose `analyze` service — verified
honored by `scheduler/pipeline.py` (soft budget: stop claiming new queue
work, let in-flight finish; the timer's `TimeoutStartSec=2h` remains the
hard ceiling).
