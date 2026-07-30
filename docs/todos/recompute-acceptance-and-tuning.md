# Recompute acceptance and post-cutover tuning

Open items carried over from the retired pg-redesign / post-rewrite-cutover
todos (Phase 7 close-out, 2026-07-28). None of these block anything; each is
independently tickable.

## Recompute acceptance (gates defined in the pg-redesign plan)

- [ ] Full recompute completes -> acceptance gates 1-4: queue drains with
      <5% failure rate; 20-doc traceability audit (doc -> run -> result ->
      panel); contract-violation sweep; raw_hash resolution check
- [ ] Manual pass of every UI tab against recomputed prod data (blocked on
      the recompute)

## Ingestion decisions

- [ ] Reddit fetcher placement decision (prod reddit capture was empty at
      cutover planning; decide where/whether the fetcher runs in prod)
- [ ] Empty-tweet capture filter (deferred 2026-07-23): X capture can store
      posts with empty text; decide filter at capture vs ETL
- [ ] Officials backfill prod run (`civic-ingest backfill-officials
      --spend-cap-usd ...`) + `OFFICIAL_RECORD_PER_AUTHOR_CAP` tuning
      (analysis/src/etl/constants.py)

## Analysis calibration

- [ ] `corpus.entity_kind = 'collective'` decision: keep as a distinct kind
      or fold into 'official'; affects account_tier derivation and party
      rollups
- [ ] `BOT_FLAGGED_SHARE_EXCLUSION = 0.5` recalibration
      (analysis/src/api/queries/constants.py) once recomputed
      author_bot_scores exist at prod scale
- [ ] End-to-end `--limit 20` Ollama run on dev (backend parity check:
      every stage completes on `CIVIC_LLM_BACKEND=ollama`)

## Test coverage

- [ ] Rebuild the officials-pass integration coverage lost with the SQLite
      test fixture (cache reuse, budget-exhaustion skip, per-account
      failure isolation, backfill walk): needs a
      `CIVIC_TEST_POSTGRES_DSN`-gated fixture over
      `raw.x_users`/`raw.x_posts`/`ops.x_api_budget` following
      `frontier_postgres_test.go`'s cleanup-by-domain pattern (see
      docs/audit-trail/ingestion/2026-07-28-delete-go-sqlite-backend.md)
