# Parallelize the LLM analysis stages

The four LLM stages (`text`, `targets`, `propaganda`, `claims` in
`analysis/src/scheduler/job_runner.py`) call Gemini **one doc at a time,
synchronously** — each `generateContent` is ~10s, so a 200-doc batch is ~30+
min per stage and the full pipeline is dominated by LLM wall-clock. The calls
are network-bound and independent, so concurrency is the obvious win. Surfaced
during the 2026-07-15 recovery ("gemini is mostly working now, it is still
slow").

## Design constraints (learned this incident)

- **Do NOT parallelize the DB writes.** We just fixed `database is locked`
  contention (see `docs/audit-trail/analysis/2026-07-15-llm-stage-lock-resilience.md`).
  Concurrent `save_ai_output` calls would reintroduce it. Parallelize the
  **LLM calls**; keep writes serial on the main thread.
- Preserve the existing per-doc resilience: a failed call still routes through
  `mark_task_failed` so the doc re-queues; it must not abort the batch.
- Respect the AI output contract (`confidence`, `model_id`, `prompt_version`,
  `inference_method`) and the propaganda stage's raw_hash dedup (score the
  primary of each group once, fan the result to siblings) — parallelize across
  *groups*, not within them.
- Bound concurrency so we don't trip Gemini rate limits (`AFC ... max remote
  calls: 10` already appears in the logs) or the tier's RPM cap.

## Approaches to evaluate

- [ ] **Bounded thread pool (likely simplest):** a `ThreadPoolExecutor` of size
      N (config, e.g. `CIVIC_LLM_CONCURRENCY`, default ~5) runs the LLM calls;
      results come back to the main loop which writes them serially. Pattern
      generalizes across all four stages — consider a shared helper so it isn't
      copy-pasted four times.
- [ ] **Gemini `batchGenerateContent`:** the model supports it
      (seen in the ListModels `supportedGenerationMethods`). Fewer round-trips,
      but a bigger change to the `llm/` client abstraction and both backends
      (`get_llm_client()` factory must keep Gemini/Ollama parity — Ollama has no
      batch endpoint, so this may need a fan-out shim there).

## Checklist

- [ ] Add `CIVIC_LLM_CONCURRENCY` to `common/settings.py` + `.env.example`.
- [ ] Introduce a shared "map LLM over docs with bounded concurrency, write
      serially" helper and adopt it in all four stages.
- [ ] Verify rate-limit behavior against the Gemini tier cap; back off on 429.
- [ ] Confirm no regression in lock contention (writes stay single-threaded).
- [ ] Measure before/after wall-clock on a representative batch; record numbers.
- [ ] Record in `docs/audit-trail/analysis/` and delete this todo.

## Out of scope

- The lock-contention hardening and busy_timeout bump — already shipped.
- Switching backends or models for speed; this is about concurrency, not model
  choice.
