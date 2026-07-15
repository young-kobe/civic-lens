# 2026-07-15 — Harden LLM analysis stages against SQLite lock contention

After Litestream began replicating the SQLite WAL continuously, every
LLM-driven pipeline stage (`text`, `targets`, `propaganda`, `claims`) started
failing a whole batch with `sqlite3.OperationalError: database is locked`, while
the fast deterministic stages (`bot`, `citations`, `narratives`, `accounts`,
`snapshots`) passed. Two independent weaknesses combined: a `busy_timeout` too
short to ride out Litestream's periodic WAL checkpoints, and stage loops that let
a single write failure abort the entire batch. The pipeline now absorbs the
contention and isolates per-doc failures so one locked write can no longer strand
a stage's whole backlog.

## What shipped

- `etl/loader.py` — `SQLITE_BUSY_TIMEOUT_MS` raised 5000 -> 15000. Litestream
  holds brief write locks while checkpointing; the application must wait them
  out (Litestream's documented requirement). 5s was set before any concurrent
  writer existed. The slow (~10s/Gemini-call) LLM stages span many checkpoint
  cycles, so they were the ones that hit the wall.
- `etl/loader.py` — silenced the `trafilatura` logger
  (`setLevel(CRITICAL)`). It emitted an ERROR+WARNING per unparseable page
  (index pages, paywalls, malformed HTML) — hundreds of lines per run — for
  outcomes ETL already counts in its skip totals.
- `scheduler/job_runner.py` — `run_text_analysis`, `run_target_extraction`,
  `run_propaganda_detection`, and `run_claim_extraction` now wrap each
  doc/group's `save_ai_output` in try/except. On failure they call
  `mark_task_failed(...)` so the doc re-queues next run and `continue`, matching
  the existing transport-failure handling (audit A-3). Completion logs report
  `processed` vs `re-queued` counts.
- `tests/test_loader.py` — `test_busy_timeout_pragma` now asserts against the
  `SQLITE_BUSY_TIMEOUT_MS` constant so a regression to a too-short timeout is
  caught.

## Why

- Litestream (added in the Docker-compose stack, see
  `infra/2026-07-09-docker-compose-stack.md`) introduced a second writer that
  contends with analyze for the WAL. The 5s timeout was the direct trigger; the
  non-resilient stage loops turned a transient lock into a lost batch.
- `save_ai_output` writes the output row and the `'done'` `doc_task_state` in one
  transaction, so a locked write rolls back cleanly — no partial/corrupt state.
  The only damage was the aborted batch, which the per-doc isolation now
  prevents.

## Follow-ups

- Confirm on the box that stopping Litestream during a manual analyze run clears
  the failures (isolates Litestream as the contended writer). If contention
  persists at 15s under real load, tune the timeout up further.
- Docs that received heuristic-fallback outputs during the Gemini credit outage
  are marked `done` and will not re-score automatically; re-queue by deleting
  their `doc_task_state` rows for the affected window (see incident notes).
- Sequential ~10s/call Gemini requests make the LLM stages slow; batching via
  `batchGenerateContent` or bounded concurrency is a separate performance
  initiative, not part of this fix.
