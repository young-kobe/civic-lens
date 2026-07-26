# Postgres migrations resolve CWD-independently and fail loudly

**Date:** 2026-07-26
**Layer:** ingestion (cross-link: [infra](../infra/2026-07-26-postgres-deploy-surface.md))
**Todo:** docs/todos/post-rewrite-cutover.md

`civic-ingest migrate` against a Postgres DSN previously looked for
`data/pg-migrations/` relative to the process working directory only. In the
container, compose sets `working_dir: /var/lib/civic-lens` while the
migrations ship in the image at `/app/data/pg-migrations` — so the lookup
found nothing, and an empty result was treated as "nothing to apply". A
fresh database would have been left schemaless with no error.

## The system as it is now

- `resolvePgMigrationsDir()` (`ingest/internal/storage/db/db_postgres.go`)
  tries the CWD-relative dir (repo checkouts, tests), then the dir beside
  the executable (container image), and errors when neither exists.
- `migratePostgresDir` errors when zero `.sql` files are discovered — an
  empty migrations dir is always a packaging bug, never a valid no-op.
- `DB.Migrate` (`db.go`) calls the resolver instead of the raw constant.

The SQLite migration path (`data/migrations/`, entrypoint materialization)
is untouched and retires wholesale with the Go SQLite backend post-cutover.
