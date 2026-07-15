# 2026-07-15 — Parallelize the LLM analysis stages

The four LLM stages (`text`, `targets`, `propaganda`, `claims` in
`scheduler/job_runner.py`) called Gemini one doc at a time, synchronously — at
~10s per `generateContent`, a 200-doc batch took ~30+ min per stage and the full
pipeline was dominated by LLM wall-clock. The calls are network-bound and
independent, so each stage now issues them through a bounded thread pool while
keeping all DB writes serial. Stage wall-clock drops roughly linearly with the
concurrency setting.

## What shipped

- `common/settings.py` + `.env.example` — new `CIVIC_LLM_CONCURRENCY` (default
  5). Sized to stay under Gemini's in-flight remote-call cap (~10).
- `scheduler/job_runner.py` — new `_map_llm_concurrent(items, call)` helper:
  runs `call` over items in a `ThreadPoolExecutor`, returns
  `[(item, result, error), ...]` in input order, capturing per-item exceptions
  instead of raising. Falls back to a serial path when concurrency is 1.
- All four LLM stages refactored to the same shape: fan the LLM call out
  concurrently, then iterate results and write serially on the main thread. The
  per-doc/per-group resilience (mark_task_failed + continue on LLM error OR
  write error) is preserved.
- `tests/test_llm_concurrency.py` — asserts the helper's contract: input-order
  preservation despite out-of-order completion, per-item error capture without
  aborting the batch, actual concurrency, and the serial path at concurrency 1.

## Why

- Sequential LLM calls made a full analyze run take hours; the stages were
  latency-bound on the network, not CPU. Concurrency is the direct lever.
- **Writes stay serial by design.** The 2026-07-15 lock-resilience work
  (`analysis/2026-07-15-llm-stage-lock-resilience.md`) fixed `database is
  locked` contention; parallelizing `save_ai_output` would reintroduce it. Only
  the network calls are parallelized — the single-writer SQLite discipline is
  intact.

## Follow-ups

- Measure before/after wall-clock on a representative batch on the box and tune
  `CIVIC_LLM_CONCURRENCY` against the actual Gemini tier RPM cap; back off if
  429s appear.
- Gemini `batchGenerateContent` (fewer round-trips) was considered and deferred:
  it needs changes to the `llm/` client abstraction and an Ollama fan-out shim
  to keep backend parity. The thread-pool approach ships the win now with no
  client changes; revisit batch if rate limits, not concurrency, become the
  ceiling.
