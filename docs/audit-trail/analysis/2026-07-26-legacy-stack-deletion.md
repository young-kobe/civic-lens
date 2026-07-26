# Legacy SQLite analysis stack deleted

**Date:** 2026-07-26
**Layer:** analysis (cross-link: [infra](../infra/2026-07-26-postgres-deploy-surface.md))
**Todo:** docs/todos/post-rewrite-cutover.md

The retired SQLite-era analysis stack is gone. It was a closed island
reachable only through `scheduler/job_runner.py`; nothing in the Postgres
pipeline imported any of it.

## The system as it is now

- One pipeline: `scheduler/pipeline.py` + `scheduler/stages.py` over
  `ops.task_queue`, engines in `engine/{text,targets,propaganda,claims,
  citations,narrative_clustering,lean_derivation,bot_detection,account_tier}.py`,
  results via `results/store.py`, ETL in `etl/documents.py`.
- The API serves live Postgres queries from `api/queries/`; there is no
  cache layer and no aggregator tree.
- The eval gate (`analysis/evals/run_eval.py`, CI + deploy workflows) runs
  the recorded responses through `engine/claims.py` — the ported claims
  engine — mapping `ClaimOutcome` fields onto the golden-set dict shape.
  Behavior parity verified: same gate output before and after the repoint.

## Deleted

- `scheduler/job_runner.py` (the old orchestrator and sole root of the island)
- `engine/`: `analyzer.py`, `bot.py`, `propaganda_detector.py`,
  `claim_extractor.py`, `citation_extractor.py`, `narrative_clusterer.py`,
  `account_classifier.py`, `target_extractor.py`, `models/`
- `etl/loader.py` (740-line SQLite ETL), `etl/polling.py`
- `common/cache.py`, `common/alerts.py`, `common/schema_guard.py`
- all of `src/reporting/` (aggregators, models, entity_registry,
  entity_posts, review.py — superseded by `api/queries/` and
  `review/service.py`)
- `ingest/cmd/stats/` (orphan SQLite stats dumper)
- 37 legacy-only test files plus `test_workflow.py`; `test_text_prep.py`
  and `test_context_seeds.py` were split — their pure-function coverage
  (truncation, triviality gate, seed parsing/matching/injection) now
  drives `engine/claims.py` instead of the deleted classes.
- `common/settings.py` fields with no remaining reader: `db_path`,
  `cache_dir`, `llm_enabled`, `llm_concurrency`, `stale_cache_warn_seconds`,
  `polling_enabled`, `polling_cache_ttl`, `loader_batch_size`,
  `known_accounts_yaml`. (`.env.example` was cleaned in the same pass.)
- Untracked local artifacts: `data/civic_prod_copy.db*`, `data/cache/`.

## Deferred (deleted post-cutover, tracked in the todo)

Go SQLite backend + `data/migrations/*.sql`, litestream,
`tools/migrate_sqlite_to_pg.py` — still needed for the cutover itself.

Verification: full suite green including PG-gated tests (740 passed, 0
failed — the count drop from 1,115 is the deleted legacy tests), `go vet` +
`go test ./...` clean, UI typecheck + build clean, eval gate exit 0.
