# Drop analysis.favorability_stances (Phase 7 decommission)

**Date**: 2026-07-28
**Layer**: analysis
**Cross-links**: infra: `2026-07-28-post-cutover-decommission.md`,
ingestion: `2026-07-28-delete-go-sqlite-backend.md`

## What the system does now

Migration `data/pg-migrations/0008_drop_favorability_stances.sql` drops
`analysis.favorability_stances` and the `analysis.favorability_label` enum.
Party stance is served exclusively by `analysis.target_mentions` joined to
`corpus.entities.lean` — that has been the only live path since the
2026-07-25 sentiment-only rewrite of `engine/text.py`; this migration
removes the last physical remnant.

The one-time SQLite-to-Postgres import tool
(`tools/migrate_sqlite_to_pg.py` + its test) is deleted. The `archive.*`
schema it populated remains, read-only, as the permanent record of the old
derived data; the original bytes stay in the age-encrypted SQLite cold
artifact in R2.

## Why now and not earlier

The drop was deliberately deferred (see
`2026-07-25-text-sentiment-only.md`) because data loss is irreversible and
the full recompute plus the side-by-side acceptance pass were still ahead.
Cutover executed 2026-07-26 and held stable through the 48h watch; the
pre-cutover pg_dump and the cold artifact both retain the rows (which were
Republican-only — the old prompt scoped favorability to GOP entities — and
must not be resurrected as a general-purpose stance source).

## Consumers

No live reader existed: `api/queries/`, `engine/lean_derivation.py`, and
the movers ticker were all repointed to `target_mentions` on 2026-07-25.
The only code change needed was removing the table from two PG-gated test
fixtures' TRUNCATE lists (`test_api_queries_docs.py`,
`test_result_store.py`); `test_result_store.py`'s guard asserting
`RunHandle` has no `save_favorability_stances` path stays.
