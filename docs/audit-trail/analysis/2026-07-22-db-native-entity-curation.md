# 2026-07-22 — Owner reversal: entity registry curated in the database, registry_sync retired

Addendum to `docs/audit-trail/analysis/2026-07-22-pg-lean-unification-registry-sync.md`
(that file is at its ~200-line split point; this is a new entry, not an edit
to it). Authority: owner decision, same day as the entry above, superseding
it on this one point. Checklist: `docs/todos/pg-redesign.md`.

## The reversal

The entity registry's source of truth moves from YAML-in-git to the
database itself, for readability of hands-on curation. `registry_sync.py`
(YAML -> `corpus.entities`/`entity_aliases`/`author_profiles`, described in
full in the entry above) is retired. A one-time seed migration,
`data/pg-migrations/0002_entity_registry_seed.sql`, replaces it.

## What replaced what

- **Seed migration**: `0002_entity_registry_seed.sql` is the final output of
  running the (now-deleted) `registry_sync.py` once more against a virgin
  `postgres:17-alpine` container (`0001` applied) with the real
  `data/*.yaml` files, then exporting `corpus.entities` /
  `corpus.entity_aliases` as deterministic, human-readable `INSERT`
  statements (explicit column lists, `entity_id` assigned fresh by the
  IDENTITY column, alias rows FK via a subselect on `entity_key` — no
  `OVERRIDING SYSTEM VALUE`). Verified counts: 21 outlets, 16 editorial
  officials, 15 subreddits, 535 promoted officials (549 person-groups, 14
  already covered by an editorial entity) = 587 entities, 30 aliases.
  `corpus.author_profiles` seeds zero rows — see below.
- **Curation history**: from here, edits to `corpus.entities`/
  `entity_aliases` happen directly against the database. `pg_dump` backups
  (once `deploy/backup.sh` is ported, Phase 11) are the audit trail — there
  is no more YAML diff to read. This is a real regeneration caveat: a
  post-seed curation edit exists only in the live DB and its backups, not
  in git.
- **`corpus.entities.synced_at` -> `updated_at`**: renamed in `0001` (still
  unreleased/greenfield) with `DEFAULT now()`, since the column no longer
  records a sync pass — it's the last direct-curation timestamp. Seeded to
  a fixed `2026-07-22` value by `0002` for a reproducible file.
- **`analysis/src/common/registry.py`** slimmed from 300 to ~35 lines: kept
  only `canonicalize_news_domain`/`canonicalize_subreddit` (the two
  functions `analysis/src/etl/documents.py` still calls for
  `outlet_entity_id`/`subreddit_entity_id` FK-backfill matching). Deleted:
  `canonicalize_handle` (no remaining caller — `entity_posts.py` and the
  sentiment aggregators import their own copy from the old-stack
  `reporting/entity_registry.py`, not this module), the YAML-loading
  dataclasses (`OutletRecord`/`OfficialRecord`/`SubredditRecord`/
  `PoliticalAccountRecord`), `load_outlets`/`load_officials`/
  `load_subreddits`/`load_political_accounts`, and the lean-flattening
  constants (`LEAN_SCALE_TO_POLITICAL_LEAN`, `PARTY_CODE_TO_POLITICAL_LEAN`,
  `flatten_lean_scale`, `flatten_party_code`) — the flattening they did is
  now baked once into `0002`'s literal `lean`/`lean_source` values.
- **Deleted outright** (no deprecation stub): `analysis/src/etl/registry_sync.py`
  (392 lines), `analysis/tests/test_registry_sync.py` (512 lines).
- **`analysis/tests/test_etl_documents.py`**: the two helpers that seeded a
  test outlet/subreddit via a real `registry_sync.sync_registry()` pass over
  a fixture YAML dir (`_sync_outlet_registry`/`_sync_subreddit_registry`)
  are replaced with `_seed_outlet_entity`/`_seed_subreddit_entity`, which
  `INSERT` the `corpus.entities` row directly — matching how curation
  actually happens now. One test renamed
  (`test_outlet_entity_id_backfills_after_later_registry_sync` ->
  `..._after_later_curation`) to match.
- **Frozen YAMLs**: the four registry YAMLs (`news_outlets.yaml`,
  `verified_officials.yaml`, `major_subreddits.yaml`,
  `known_political_x_accounts.yaml`) each get a one-line header comment
  (`frozen 2026-07-22 — superseded by 0002_entity_registry_seed.sql; edits
  here no longer reach the new system`) and are otherwise untouched. They
  stay in git, read-only, only because the old SQLite-stack's
  `analysis/src/reporting/entity_registry.py` still reads them; that module
  dies with the rest of the old stack in Phase 9, at which point the YAMLs
  can go too.

## `corpus.author_profiles`: seeded empty, not a shortcut

`author_profiles.author_id` is `NOT NULL UNIQUE REFERENCES corpus.authors
(author_id)`, and `corpus.authors` (populated by `analysis/src/etl/authors.py`
from `raw.x_users`) is empty on a virgin database — there is no `author_id`
to reference at seed time. Running the final `registry_sync.py` pass against
the identical virgin container confirmed this isn't a gap introduced by
skipping the table: its own `_sync_author_profiles` logic produced
`author_profiles_synced=0, author_profiles_skipped=568` (one skip per
curated account whose author hadn't been ETL'd yet) against that same
empty-`corpus.authors` state. A faithful snapshot of that run already
contains zero linkage rows, so `0002` seeds none, by design, not omission.

Going forward, nothing automated re-derives this linkage — `registry_sync.py`
was the only writer and is deleted. Populating `author_profiles` for a given
author is either a direct DB-curation edit once that author exists, or
future work for Phase 6's `account_classifier` (tracked in
`docs/todos/pg-redesign.md`).

## Verification

- `0001` (with the `synced_at`->`updated_at` rename) then `0002` applied
  clean on a virgin `postgres:17-alpine` container, via both raw `psql` and
  the real `civic-ingest migrate` binary; re-running `civic-ingest migrate`
  is a true no-op (`ops.schema_migrations` tracks both files as applied).
- Counts matched the header exactly: `corpus.entities` 587 (535/16 official,
  21 outlet, 15 subreddit), `corpus.entity_aliases` 30, `corpus.author_profiles`
  0. Spot-checked `entity_key='potus'` (`republican`, `lean_source='R'`,
  `editorial=true`) and the `washingtonpost.com` alias rows (`wapo.st`,
  `washpost.com`) against the source YAML. Zero orphaned `entity_aliases`
  rows (FK join to `entities` complete).
- End-to-end sync-less chain against a DB seeded only by `0001`+`0002` (no
  `registry_sync` involved at any point): `authors.sync_x_authors()` ->
  `documents.load_new_documents()` on a fixture `raw.articles` row for
  `nytimes.com` (a real seeded outlet) -> `queue.seed_pending_tasks()`.
  `outlet_entity_id` resolved to the seeded `nytimes.com` entity
  (`editorial=true`); 5 task rows queued. Re-running the same three calls
  was a zero-delta no-op (0 new docs, 0 new queue rows) — idempotency holds
  without `registry_sync` in the loop.
- `analysis/tests/test_etl_documents.py` (with the rewritten seed helpers)
  passes gated against real Postgres: 47/47.
- Full suite: `PYTHONPATH=$PWD analysis/.venv/bin/python -m unittest
  discover analysis/tests` — 526 tests, 0 failures, both ungated
  (21 skipped, no `CIVIC_TEST_DATABASE_URL`) and fully gated
  (`CIVIC_TEST_DATABASE_URL` set, 0 skipped) runs.
- `cd ingest && go test ./...` — all packages pass, unaffected (the runner's
  migration discovery is directory-generic; no test hardcodes a count
  against the real `data/pg-migrations/` directory).
- Container hygiene: every throwaway `postgres:17-alpine` container used for
  this verification was removed; `docker ps -a` afterward shows only the
  two pre-existing, unrelated containers.

## Follow-ups (tracked in `docs/todos/pg-redesign.md`)

- `corpus.author_profiles` has no writer until a DB-curation habit forms or
  Phase 6's `account_classifier` lands — not a blocker today since nothing
  in the live pipeline reads the table yet (Phase 6 is still unchecked).
- The frozen YAMLs and `analysis/src/reporting/entity_registry.py` die
  together in Phase 9, per the existing plan — no separate follow-up needed.
