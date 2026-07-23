# 2026-07-22 — Postgres redesign Phase 5: analysis plumbing

`analysis/src/llm/client.py`, `analysis/src/results/store.py` +
`analysis/src/results/__init__.py`, and `analysis/src/engine/validation.py`
are new — the three "code design principle" modules Phase 5 of the plan
(`has-our-aggregate-method-async-frog`, checklist `docs/todos/pg-redesign.md`)
promised: one LLM retry/backoff layer, one result store, one evidence-span
validator. All three compose against the north-star schema
(`data/pg-migrations/0001_north_star.sql`, Phases 1-4). Cross-links: Phase 1
`2026-07-22-pg-connection-pool.md` (the `common/db.py` pool these all use),
Phase 4 `2026-07-22-pg-etl-authors-documents-queue.md` (the `corpus.documents`
rows these results attach to).

## What shipped

### `llm/client.py` — unified retry/backoff/confidence layer

- `LLMClient.complete()` wraps a `TransportBackend` (structural `Protocol`:
  `is_available`, `complete_once()`, `get_token_usage()`) with one
  retry/backoff loop (`DEFAULT_MAX_RETRIES = 3`, `(2**attempt) + 0.5`s
  backoff, no sleep after the final attempt) and one confidence-coercion
  pass (`_coerce_confidence_fields`, recursive over the response dict for
  every key named or suffixed `_confidence`).
- `GeminiClient` / `OllamaClient` / `OpenAICompatClient` each gained an
  additive `complete_once()` — the single-attempt request+parse body
  extracted out of their existing `complete()`, which now just calls
  `complete_once()` inside its own loop. This is a transport-hook layering,
  not yet the full "backends become transport-only" end state: each
  backend's own `complete()` retry loop stays live as a compatibility shim
  for the pre-Phase-6 engines that still import a backend directly and call
  `complete()`. `llm/client.py` is the only caller of `complete_once()`
  today. Full shrink (deleting each backend's own retry loop) lands
  per-engine as Phase 6 ports each engine onto `llm/client.py`.
- Schema-invalid responses (`SchemaValidationError`, raised inside
  `complete_once()`'s `parse_json_response`) retry exactly like a transport
  error — same loop, same attempt count, no special-casing.
- `get_client()` / `reset_client()` mirror `factory.py`'s lazy-singleton
  pattern, but there is exactly one `LLMClient` (not one per backend); the
  backend itself still resolves via the existing `CIVIC_LLM_BACKEND`/
  `get_llm_client()` factory.

### `results/store.py` — the sole writer of `analysis.*` results

- `open_run(task, *, doc_id=None, author_id=None, model_id, prompt_version=None,
  inference_method) -> RunHandle`. Validates the `doc_id` XOR `author_id`
  contract and non-empty `model_id` before any DB call — a caller bug
  surfaces immediately, not as a constraint violation after the engine did
  its work.
- `RunHandle.save_*()` methods (`save_sentiment`, `save_favorability_stances`,
  `save_target_mentions`, `save_propaganda`, `save_claims`,
  `save_bot_signals`, `save_citations`) accumulate typed, store-owned
  dataclasses in memory. Nothing reaches Postgres until `finish()`.
  `save_bot_signals`/`save_citations` reject an author-scoped handle
  immediately (both tables are doc-scoped only).
- `finish(status, confidence=None, raw_response=None, error=None) -> run_id`
  commits the run row + all accumulated result rows in one transaction:
  1. **Advisory lock**: `pg_advisory_xact_lock(hashtext(lock_key))`, keyed
     `task:doc_id|author_id:subject_id`. Transaction-scoped, so it releases
     automatically on commit or crash — serializes concurrent `finish()`
     calls for the same (subject, task) into ordinary sequential execution
     instead of racing the partial unique index.
  2. **Flip-before-insert**: if `status == 'done'`, the predecessor's
     `is_current` is UPDATEd to `false` *before* the new row is INSERTed.
     Insert-then-flip was rejected: Postgres checks a plain (non-deferred)
     unique index per-statement, so both rows would briefly satisfy the
     partial unique index between the two statements — a real violation,
     not just a race. Flip-then-insert never lets that state exist.
  3. **Failed-run rule**: a `status='failed'` run is inserted with
     `is_current=false` unconditionally, never flips a predecessor, and
     discards all accumulated `save_*()` results (nothing is written to the
     typed result tables). Rationale: stale-but-valid beats broken — a
     prior succeeded run keeps serving as `is_current` until a new succeeded
     run replaces it, and a failed run had no trustworthy result to persist
     in the first place. A future deliberate extension (partial results on
     failure) is not today's default.
  4. **Traceability contract**: `model_id` is required unconditionally
     (mirrors the NOT NULL DDL constraint). `prompt_version` is required
     only when `status == 'done'` and `inference_method` is `llm`/`hybrid` —
     deterministic runs never need one; a *failed* llm/hybrid run is exempt
     too (it never got far enough to produce a prompted result, but still
     needs to be recorded as failed).
- `register_prompt_version()` is an idempotent upsert into
  `analysis.prompt_versions`, safe to call on every engine invocation.
  `task` is deliberately excluded from the `ON CONFLICT` update (sentiment
  and favorability share one prompt_version under the unified `text` task;
  whichever calls first shouldn't make the audit column flip-flop).

### `engine/validation.py` — the one evidence-span validator

Consolidates four drifted per-engine validators (`analyzer.py`,
`claim_extractor.py`, `propaganda_detector.py`, `target_extractor.py`) into
one set of pure functions. Constants chosen by majority precedent across the
four originals, not picked arbitrarily:

- `MIN_EVIDENCE_WORDS = 4` (three of four already used 4; `claim_extractor`'s
  3-word floor tightens up).
- `UNVERIFIED_EVIDENCE_CONFIDENCE_CAP = 0.3` (three of four already used 0.3;
  `propaganda_detector`'s 0.2 cap loosens up).

Because Phase 8 recomputes every result fresh under the new engines, there is
no drift-vs-history problem in changing these constants now — nothing
downstream depends on the old per-engine values surviving.

Four recorded disagreement resolutions (old behavior -> unified, all in the
module docstring):

1. **Containment check**: all four already agreed (case-insensitive verbatim
   substring, not word-boundary) — kept as-is, no change.
2. **De-duplication**: `target_extractor` de-duped spans in place; `analyzer`
   did not. `validate_spans` does neither — de-duplication is a caller-level
   concern (what to do with valid evidence), not a span-validity question.
3. **`had_invalid` signal**: `analyzer`'s loose "at least one input span
   failed" flag, only acted on by callers when *no* valid spans survived
   either. `validate_spans` returns that combined condition directly
   (`had_invalid = spans given but none survived`) so callers drop the
   redundant second check.
4. **Confidence clamp/cap order**: `target_extractor`/`propaganda_detector`
   both clamped to [0,1] before applying the unverified cap;
   `cap_confidence_if_unverified` matches that order. `claim_extractor`
   never capped (it drops the whole claim instead) — that "drop" behavior
   has no equivalent helper here; it stays an engine-level Phase 6 decision.

Raw-value coercion (`raw.get("confidence", ...)` parsing with a
try/except-reject) stays a caller concern — `cap_confidence_if_unverified`
takes an already-parsed float.

### The `runs.error` column restoration

The plan's `analysis.runs` sketch included `error TEXT`; the committed
0001 DDL omitted it, and `store.py` originally worked around this by folding
`error` into `raw_response` under an `"error"` key. Owner decision
2026-07-22 (pgAdmin readability: an explicit column beats a JSONB key for
hands-on inspection) reversed that workaround:

- `data/pg-migrations/0001_north_star.sql`: `analysis.runs` gains
  `error TEXT`, with column comments on both `error` and `raw_response`
  cross-referencing the no-mixing rule (`raw_response` is purely the
  verbatim LLM payload; `error` never appears inside it).
- `results/store.py::finish()` writes `error` straight to the column;
  `raw_response` is passed through untouched — no folding, no merging.
- `docs/DATABASE_SCHEMA.md`'s `analysis.runs` section documents the new
  column.
- Tests: `test_result_store.py`'s failed-run tests assert `error` lands in
  the column and `raw_response` is untouched; a new
  `test_error_and_raw_response_do_not_mix` passes both to one `finish()`
  call and asserts neither leaks into the other.

`0001_north_star.sql` is greenfield (deployed nowhere yet), so this was an
in-place edit, not a new migration.

## Why

- The plan's "code design principles" section named these three modules as
  ending specific, named drift: 4 LLM init patterns + 3 copy-pasted retry
  loops (client.py); the correlated-MAX `is_current` view pattern (store.py);
  the 3-vs-4-word / 0.2-vs-0.3-cap validator drift (validation.py).
- The `error` column restoration is DDL drifting from the plan's own sketch
  during Phase 1 authoring, caught during Phase 5 closure review, and
  corrected in favor of the owner's stated priority (pgAdmin-native
  readability over JSONB-key indirection) before any data existed to migrate.

## Validation performed this task

- Full unit suite (no DB): 31 `test_result_store.py` validation-tier tests,
  all green.
- Clean-room verification against a fresh throwaway `postgres:17-alpine`
  container (real `civic-ingest` binary, rebuilt from current source):
  `civic-ingest migrate` applies `0001_north_star.sql` +
  `0002_entity_registry_seed.sql` cleanly; re-run is a no-op (idempotent);
  `\d analysis.runs` confirms `error TEXT` is present alongside
  `raw_response JSONB`. Full Python suite gated on `CIVIC_TEST_DATABASE_URL`
  pointed at the container: 598 tests, 0 skips, all pass; ungated (no DB):
  598 tests, 37 skipped, all pass.
- Throwaway cross-component smoke script (not a committed test): registered
  a prompt version; seeded minimal `raw.pages` -> `raw.articles` ->
  `corpus.documents` fixtures (full traceability chain, not just the
  documents row); ran a fake `TransportBackend` through `LLMClient.complete()`
  (confirmed the 0-100 -> 0-1 confidence coercion fires); validated its
  evidence span through `engine/validation.py`; opened an `analysis.runs`
  (`task='text'`) run via `results/store.py`, saved a validated
  `SentimentRow`, called `finish('done', ...)`, and confirmed the row is
  queryable and `is_current`; opened a second run for the same doc+task and
  confirmed it flips the first to `is_current=false` while becoming the new
  current row. Proves the three workstreams compose end to end.
- `cd ingest && go test ./... -count=1`: all packages pass (regression check
  only — no Go-side change this task).
- Container hygiene: `docker ps -a` confirms no leftover container; the
  anonymous data volume for the throwaway container's `postgres` image
  (identified by creation timestamp, since the box also runs unrelated
  pre-existing containers/volumes) was individually removed; all
  pre-existing containers/volumes were left untouched.

## Follow-ups (tracked in `docs/todos/pg-redesign.md`)

- Backends' own `complete()` retry loops are not deleted yet — happens
  per-engine as Phase 6 ports each engine onto `llm/client.py`.
- `claim_extractor`'s "drop the claim" behavior for unverifiable evidence has
  no equivalent here — an explicit Phase 6 decision for that engine's port.
- Phases 6-8 (engines, scheduler, recompute) depend on these three modules
  and are not started.
