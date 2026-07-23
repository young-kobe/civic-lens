# 2026-07-23 — Postgres redesign Phase 7: scheduler pipeline + lean derivation (integration wave)

`analysis/src/scheduler/{constants,stages,pipeline}.py` and
`analysis/src/engine/lean_derivation.py` are new — the first working
new-stack pipeline runner over the six Phase 6 engines
(`2026-07-23-pg-engines-wave1.md`..`wave3.md`), plus the deterministic
political-lean derivation Phase 6 left open. This entry also lands the
`RunOutcome` contract every engine's `process()` now returns, and closes
out the Phase 7 scheduler checklist.

## What shipped

### `scheduler/pipeline.py` + `scheduler/stages.py` — the new-stack runner

`pipeline.run_pipeline(tasks=None, limit=None, budget_seconds=None)` builds
a concrete `StageSpec` registry over the real engines and drives it via
`stages.py`'s two runners: `run_queue_stage` (SKIP LOCKED claim loop against
`ops.task_queue`, `concurrency` worker threads, requeue-under-max-attempts
at stage start) for the six doc-scoped tasks, and `run_global_stage`
(call once) for `etl`/`account_tier`/`bot_rollup`/`narratives`/`leans`.
Stages always run in `STAGE_ORDER`
(`scheduler/constants.py`) regardless of caller-supplied order — narratives
after claims, leans after text/targets/narratives, bot_rollup after bot.
One `ops.pipeline_runs` row per invocation: `'running'` at start,
`'done'`/`'failed'` + a per-stage `stage_summary` JSON in a `finally` block.
A stage's ordinary per-doc `failed` count (retryable via the queue's
`attempts` mechanism) does **not** fail the pipeline_runs row — only a
stage-level `error` (a worker thread aborting, or a `kind='global'`
callable raising) does.

CLI: `python -m analysis.src.scheduler.pipeline [--tasks ...] [--limit N]
[--budget-seconds N]`, wired into `run.sh analyze-pg` (new subcommand;
`run.sh analyze` is untouched and keeps invoking old-stack
`scheduler/job_runner.py`). New setting: `Settings.analyze_concurrency`
(default 4) — worker thread count for `run_queue_stage`.

**Replaces old `job_runner.py`'s `_map_llm_concurrent`, not job_runner.py
itself.** `job_runner.py` is untouched, byte-for-byte, and stays the live
`run.sh analyze` entry point (SQLite stack) until Phase 11 cutover — this
wave adds a second, parallel entry point rather than rewriting the first.
`_map_llm_concurrent`'s serial-write-back-after-concurrent-fetch machinery
has no successor in the new stack: `run_queue_stage`'s per-thread SKIP
LOCKED claim + immediate write (via each engine's own `process()` ->
`results/store.py`) replaces the whole fetch/compute/write-back shape.

**Per-worker-thread LLM client**: `stages._new_llm_client()` builds a fresh
backend + `LLMClient` per thread rather than reusing `llm/factory.py`'s
cached singleton, because every backend's `complete_once()` does an
unlocked `self.total_tokens_used += ...` — a genuine data race under
concurrent worker threads sharing one instance. Left as documented,
not "fixed": the counter has no reader in this pipeline today.

**Tracked-targets-from-registry (owner decision, 2026-07-23)**: the
`targets` stage's `tracked_targets` list is built once per stage run from
`corpus.entities` (`kind='official' AND editorial AND active`), not per
doc — mirrors old `job_runner.py`'s curated-registry convention, minus its
two hardcoded party-collective strings. `corpus.entities` has no populated
`kind='collective'` rows yet (tracked in `docs/todos/pg-redesign.md`'s
Phase 4 "Future decision" item); fabricating them here would violate the
never-fabricate-values rule, so the block is shorter than old
job_runner's until that data exists.

### `engine/lean_derivation.py` — the one political-lean derivation every surface joins

`run(now=None)` pools directional `favorability_stances`/`target_mentions`
evidence (current, `status='done'` runs only) per author and per
narrative, then full-rebuilds `analysis.author_leans`/`narrative_leans` —
DELETE + INSERT inside its own transaction per table, no `analysis.runs`
row (deterministic, not run-anchored — same documented-exception class as
`narrative_clustering.py`'s tables). A subject with zero directional
samples gets no row at all, not a zero-evidence placeholder.

**Signal direction** (`_signal_for`): favorable-or-positive toward a
democrat entity, or unfavorable-or-negative toward a republican entity, is
one pro-democrat sample; the mirror is one pro-republican sample.
Non-partisan curated leans (`independent`/`mixed`/`unknown`) and
non-directional stances (`neutral`/`mixed`) carry no signal.

**Three-way gate (owner decision, 2026-07-23 — mixed-vs-unknown
refinement)**: below `LEAN_MIN_SAMPLE_COUNT` (5) pooled samples ->
`'unknown'` (insufficient evidence). At/above it, majority share <
`LEAN_SHARE_THRESHOLD` (0.7) -> `'mixed'` — balanced evidence is itself a
finding, not the same as not having enough evidence to say anything, so it
gets its own label rather than collapsing into `'unknown'`. At/above both
-> the majority side. `lean_share` always records the true majority share
regardless of gate outcome (auditable even when `'unknown'`/`'mixed'`).
`lean_confidence`/`confidence` scale `lean_share` down for thin evidence,
saturating at `LEAN_CONFIDENCE_SATURATION_SAMPLES` (20). All three
constants live in `engine/constants.py`.

Column names differ by table on purpose: `author_leans.lean_confidence`
vs. `narrative_leans.confidence` — both are the same derivation, tested
explicitly to catch a copy-paste rename.

### `RunOutcome` contract (`results/store.py`)

`RunHandle.finish()` now returns `RunOutcome(run_id: int, status: str,
error: Optional[str])` (frozen dataclass) instead of a bare `run_id` int.
All six doc engines' `process()` (`text`, `targets`, `propaganda`,
`claims`, `bot_detection`, `citations`) pass it through unchanged —
mechanical, their bodies already did `return handle.finish(...)`; only
signatures/docstrings were updated (`-> int` -> `-> store.RunOutcome`).
`scheduler/stages.py`'s claim loop already consumed `outcome.run_id`/
`status`/`error` duck-typed against this exact shape and needed no change.
Every existing test asserting on `process()`/`finish()` returning a bare
int (`test_result_store.py`, `test_engine_{text,targets,propaganda,claims,
bot,citations}.py`) now unwraps `.run_id` where an int is actually needed
(SQL param, dict key, equality against another run's id); two new
`test_result_store.py` cases assert the `RunOutcome` shape itself
(`run_id`/`status`/`error` fields, `isinstance` check).

Checked for other consumers of `finish()`/`process()`'s return value:
`account_tier.classify_authors()` and `narrative_clustering.run()` never
call `finish()` (both are documented exceptions to `results/store.py`
being the run-anchored sole writer — see their own module docs and
`DATABASE_SCHEMA.md`'s "Result-store write semantics"), so neither needed
a change. `bot_detection.refresh_author_bot_scores()` returns a plain SQL
rowcount, an unrelated contract, also untouched.

## Docstring trim (owner-directed)

Module docstrings over 3 lines in the engine modules this wave's
integration touched were trimmed to 1-3 lines, pointing at
`DATABASE_SCHEMA.md`/audit-trail entries instead of restating them:
`engine/account_tier.py`, `engine/targets.py`, `engine/narrative_clustering.py`,
`engine/lean_derivation.py`, `scheduler/stages.py`, `scheduler/pipeline.py`.
No load-bearing content was lost — everything trimmed was already recorded
in `DATABASE_SCHEMA.md`'s "Narratives" section or the `wave1`-`wave3`
entries; the mixed-vs-unknown gate and tracked-targets decisions this wave
introduced are recorded above instead of in the module docstring.
Other pre-existing modules with long docstrings (`text.py`, `claims.py`,
`bot_detection.py`, `citations.py`, `propaganda.py`, everything outside
`analysis/src/engine/`, and every old-stack module still live behind
`job_runner.py`) were left as found — out of this wave's designated scope,
reported to the owner rather than trimmed silently.

## Why

Phase 7's scheduler had to prove the new stack can actually drive real
work end-to-end (claim/complete/fail/retry/stale-reclaim) before Phase 8's
recompute pilot commits real LLM spend to it. Lean derivation was the last
Phase 6 engine gap — every rollup surface downstream needs one
politically-labeled row per author/narrative, computed one way. The
`RunOutcome` contract exists because the scheduler's claim loop needs
`status`/`error` off every engine call, and threading a bare int through
that shape would have meant re-deriving them from a second DB read or
smuggling them through the doc-input/output tuple.

## Validation performed this task

Fresh throwaway `postgres:17-alpine`, freshly rebuilt `civic-ingest`
binary: `migrate` applies `0001`+`0002` cleanly and idempotently (re-run
is a clean no-op). Full gated Python suite as one `unittest discover`
process against that database: **863 tests, 0 failures, 3 consecutive
clean runs**. Ungated run (no `CIVIC_TEST_DATABASE_URL`): 863 tests, 104
skipped (the gated classes), 0 failures. `cd ingest && go test ./...
-count=1`: all packages pass (regression check; no Go-side change this
task).

Composition smoke (throwaway script, not committed): seeded 2 entities
(one democrat, one republican, one editorial/elected for the
`account_tier` real-registry match), 2 authors, and 9 documents across all
three source types, then drove three `pipeline.run_pipeline()` calls with
`stages._new_llm_client` monkeypatched to a content-keyed stub (deterministic
stages — `citations`, `account_tier`, `bot_rollup`, propaganda's
loaded-language pre-filter skip path — ran for real; LLM-backed stages ran
against the stub). Proved, all 24 assertions green: `ops.task_queue` seeds
53 rows and drains to `done`/`failed` correctly; an injected LLM failure on
one doc's `bot` task records a real failed run (`RunOutcome` wired through
`results/store.py`, not a stub) and leaves the queue row `failed` with
`attempts=1`; a second `run_pipeline(tasks=["bot"])` call requeues and
recovers it to `done`; three `ops.pipeline_runs` rows each carry a
`stage_summary` (one shows `bot: {failed: 1}}` without flipping the run's
own status to `'failed'`, per the per-doc-vs-stage-level-error rule);
`claims` -> `narratives` -> `leans` clusters 3 matching-claim docs into one
narrative and derives a `'democrat'`/1.0-share/3-doc-count narrative lean;
one author with 6 pooled pro-democrat samples gets a decided `'democrat'`
author lean, another with a 4/4 split gets `'mixed'` at 0.5 share — both
exactly matching `_derive_lean`'s known arithmetic.

Container + its anonymous `postgres:17-alpine` data volume were both
removed after the final verification pass; confirmed via `docker volume
ls -f dangling=true` that no volume created this session remains (the
~34 dangling volumes still present pre-date this session and were not
touched).

## Follow-ups

`docs/todos/pg-redesign.md`'s "End-to-end `--limit 20` run against Ollama
on dev" Phase 7 box is intentionally left unticked — an owner-run action
against a live backend, not something a clean-room verification proves.
Phase 8 (recompute pilot) is next. The docstring-length audit surfaced
several pre-existing modules outside this wave's scope (see above) —
flagged for whoever picks up a dedicated docstring-trim pass, not fixed
here.
