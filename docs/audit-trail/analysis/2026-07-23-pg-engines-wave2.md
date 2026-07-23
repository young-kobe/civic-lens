# 2026-07-23 — Postgres redesign Phase 6 Wave 2: targets, propaganda, claims engines

`analysis/src/engine/targets.py`, `analysis/src/engine/propaganda.py`, and
`analysis/src/engine/claims.py` are new — the second batch of Phase 6 engine
ports (plan `has-our-aggregate-method-async-frog`, checklist
`docs/todos/pg-redesign.md`), built to the pure `analyze()` + thin
`process()` contract Wave 1 set (`2026-07-23-pg-engines-wave1.md`: `text.py`,
`citations.py`, `entity_resolver.py`). This entry also restores
`propaganda_results`' count columns end to end and consolidates the three
engines' constants into `engine/constants.py`.

## What shipped

### `engine/targets.py` — per-target stance, unresolved kept not dropped

- `TargetMentionOutcome.entity_id` is `Optional[int]`: `target_mentions.
  entity_id` is NULLABLE, so an unresolved raw target is stored with
  `entity_id NULL` rather than dropped. This is the opposite convention
  from `text.py`'s favorability stances (`favorability_stances.entity_id`
  is NOT NULL, so an unresolved stance there is dropped and counted). Same
  `EntityResolver`, opposite table constraint, opposite behavior.
- A stance value outside `analysis.sentiment_label` (`_STANCE_MAP`) is
  dropped and counted (`dropped_unmappable_stance`) — a different failure
  mode from the unresolved-entity case above, tracked separately.
- De-dup by lowercased target name, first valid mention wins (schema caps
  at 4 targets; a repeat is a model slip, not a genuine second stance).
- Run confidence: mean of every mention's own confidence; zero mentions is
  legitimate (no stance taken toward any target) and stores `None`, not a
  guessed value — the only one of the three Wave 2 engines that can leave
  run confidence unset.

### `engine/propaganda.py` — pre-filter as a deterministic run, counts restored

- Loaded-language pre-filter (`_has_loaded_language`, ported from
  `propaganda_detector.py`) runs before any LLM call: if the first
  `PROPAGANDA_PRE_FILTER_SCAN_CHARS` of (title + text) contain no token
  from `PROPAGANDA_LOADED_LEXICON`, the LLM call is skipped and a
  `deterministic` zero-technique result is recorded as `done` — never a
  silent skip that would leave the doc unqueued. Same shape as `text.py`'s
  trivial-content short-circuit and `targets.py`'s.
- `_DDL_PROPAGANDA_TECHNIQUES` is the INSERT-time vocabulary gate,
  deliberately duplicated against `llm/schemas.py`'s
  `PROPAGANDA_TECHNIQUE_ENUM` (the LLM-facing contract) so a future
  prompt/schema addition can't punch through to a failed enum cast without
  a human first running the `ALTER TYPE ... ADD VALUE` migration.
- Run confidence: `density` IS the run confidence (the old job_runner's
  `run_propaganda_detection` passed `overall_propaganda_score` straight
  through as `ai_outputs.confidence`) — not a mean over per-technique
  confidences, unlike `text.py`'s unified run. Three density outcomes
  (ported from the old detector, cap value 0.2 -> 0.3 per the unified
  `UNVERIFIED_EVIDENCE_CONFIDENCE_CAP`): >=1 technique validated trusts the
  reported density; techniques flagged but none validated caps density;
  zero techniques flagged forces density to 0.0.

**Count-column restoration** (this wave's substantive change): the plan's
`propaganda_results` sketch specified validated/dropped counts; the DDL that
actually shipped in `0001_north_star.sql` carried only `(run_id, density,
summary)`. The engine already computed `techniques_validated`/
`techniques_dropped` (evidence-verified counts vs. LLM-flagged-and-dropped
counts) but had nowhere to persist them — closed end to end:

- `data/pg-migrations/0001_north_star.sql`: `analysis.propaganda_results`
  gains `techniques_validated INT NOT NULL DEFAULT 0` and
  `techniques_dropped INT NOT NULL DEFAULT 0`, with column comments.
  Greenfield migration, deployed nowhere — in-place DDL edit.
- `docs/DATABASE_SCHEMA.md`: `propaganda_results` row updated.
- `analysis/src/results/store.py`: `PropagandaResultRow` gains both fields
  (default 0); the `propaganda_results` INSERT carries them through.
- `analysis/src/engine/propaganda.py`: `process()` passes
  `result.techniques_validated`/`result.techniques_dropped` into the
  `PropagandaResultRow` it hands to `save_propaganda()`.
- Tests: `test_result_store.py::test_propaganda_round_trip` asserts both
  columns round-trip; `test_engine_propaganda.py`'s two existing
  integration tests assert the counts on both the LLM path (1
  validated/0 dropped) and the pre-filter path (0/0); a new
  `test_mixed_validated_and_dropped_techniques_persist_both_counts` asserts
  a genuinely mixed case (1 validated, 2 dropped) lands correctly — the
  gap the two pre-existing tests (0-dropped only) didn't cover.

### `engine/claims.py` — drop-not-cap, tied to narrative anchoring

- A claim whose `evidence_span` fails `validation.validate_evidence_span`
  is **dropped entirely**, not kept with confidence capped — the opposite
  of `text.py`/`targets.py`'s span handling (invalid spans there are
  filtered out of the list but the sentiment/stance itself survives with
  confidence capped at `UNVERIFIED_EVIDENCE_CONFIDENCE_CAP`). Rationale
  ported unchanged from the old `claim_extractor.py`: `analysis.claims` has
  no evidence_span column to persist a capped-but-kept row against, and
  `analysis.narratives.anchor_claim_id` anchors narrative clustering on
  claim text directly — a hallucinated claim that survived with merely
  lower confidence could still seed or join a narrative cluster. Dropping
  is the only option that can't leak a fabricated claim downstream.
- Run confidence: mean of every claim's own confidence; zero surviving
  claims stores `ZERO_CLAIMS_CONFIDENCE = 0.0`, not `None` — old
  `job_runner.run_claim_extraction` convention. Contrast `targets.py`,
  which stores `None` for the same zero-result case: both ported unchanged
  from their respective old-stack conventions rather than unified, because
  the old code itself disagreed and nothing forced a reconciliation.

### Run-confidence conventions across the wave (plainly stated)

| Engine | Zero-result confidence | Non-zero convention |
| --- | --- | --- |
| `targets.py` | `None` (no mentions, nothing to average) | mean of per-mention confidence |
| `propaganda.py` | `0.0` (pre-filter or LLM-zero-techniques path) | `density` passthrough (self-reported score, not a mean) |
| `claims.py` | `0.0` (`ZERO_CLAIMS_CONFIDENCE`) | mean of per-claim confidence |

## Constants consolidation

All three engines originally kept a "Wave 2 constants" block module-local,
each with an identical comment explaining why: touching `engine/
constants.py` concurrently with a sibling agent risked a merge conflict.
That pressure is gone now that all three have landed — consolidated into
one clearly-sectioned append to `engine/constants.py`:

**Moved**: `TARGETS_TASK`/`PROPAGANDA_TASK`/`CLAIMS_TASK` (task-identifying
strings); `MAX_TARGETS`, `TARGET_TEXT_MAX_CHARS`, `PROPAGANDA_TEXT_MAX_CHARS`,
`MAX_PROPAGANDA_TECHNIQUES` (renamed from `_MAX_TECHNIQUES`, no longer
private once shared), `PROPAGANDA_LOADED_LEXICON` (renamed from
`_LOADED_LEXICON`, the union of the already-shared `NEGATIVE_WORDS`/
`INTENSIFIERS`), `PROPAGANDA_PRE_FILTER_SCAN_CHARS` (renamed from
`_PRE_FILTER_SCAN_CHARS`), `CLAIM_TEXT_MAX_CHARS`, `MAX_CLAIMS_PER_DOC`,
`ZERO_CLAIMS_CONFIDENCE`. Matches the precedent `TEXT_ANALYSIS_MAX_CHARS`/
`TRIVIAL_CONTENT_CONFIDENCE` already set by Wave 1's `text.py`: numeric
budgets, caps, and default-confidence conventions shared or shareable
across engines and tests.

**Stayed module-local, with the rule applied**:
- `targets.py`'s `_STANCE_MAP` and `propaganda.py`'s
  `_DDL_PROPAGANDA_TECHNIQUES` — explicitly named in the owner's
  instruction as the example case: private lookup/gate tables read by
  exactly one function each. Moving a two-branch dict or a six-value
  enum-gate frozenset into a shared constants module doesn't make it more
  shared — it just separates the table from its only reader, which is a
  readability loss, not a gain.
- `propaganda.py`'s `_WORD_RE` (compiled regex) — an implementation detail
  of `_has_loaded_language()`, not a data constant of the kind
  `engine/constants.py` holds (frozensets/tuples/ints, no compiled
  patterns anywhere in that module today).
- `propaganda.py`'s `_PRE_FILTER_REASONING` — a presentational string
  returned as `PropagandaAnalysis.summary`, not a threshold or config
  value; `engine/constants.py` holds behavior-governing constants, not
  narrative text.

**Rule applied, stated once**: move a constant if it is a task-identifying
string, or a numeric budget/cap/confidence-default of the kind already
living in `engine/constants.py`; keep it local if moving it would separate
a private lookup/gate table, a compiled pattern, or a user-facing string
from the single function that owns it, since none of those are "shared" in
the sense this file exists to serve. All three engines' imports and test
files were updated; the only test references to the old private names
(`propaganda._DDL_PROPAGANDA_TECHNIQUES`, `propaganda._PRE_FILTER_REASONING`)
target names that stayed put and needed no change.

## Why

- Same "pure `analyze()` + thin `process()`, no heuristic fallback" design
  contract Wave 1 established — a failed/unavailable LLM call is a
  recorded `failed` run in all three engines, never a degraded guess.
- The count-column gap is the same class of DDL-vs-plan drift Wave 1 caught
  twice (`corpus.x_posts` reference columns, `analysis.runs.error`): caught
  and closed at zero cost while the schema is still greenfield, before any
  data depends on the narrower shape.
- Constants consolidation follows the owner's explicit rule (deconflict now
  that the concurrency risk that justified staying local is gone) rather
  than leaving three near-identical "not yet consolidated" comments to rot.

## Validation performed this task

- Full unit suite (no DB): all pure-core tests for `targets.py`,
  `propaganda.py`, `claims.py` green, including the new mixed-count test.
- Clean-room verification against a fresh throwaway `postgres:17-alpine`
  container (real `civic-ingest` binary, rebuilt from current source):
  `civic-ingest migrate` applies `0001_north_star.sql` +
  `0002_entity_registry_seed.sql` cleanly on a virgin database; re-run is a
  no-op (idempotent); `\d analysis.propaganda_results` confirms
  `techniques_validated`/`techniques_dropped` present, `INT NOT NULL
  DEFAULT 0`. Full Python suite gated on `CIVIC_TEST_DATABASE_URL` pointed
  at the container: 735 tests, 0 skips, all pass; ungated (no DB): 735
  tests, 62 skipped, all pass.
- Throwaway cross-engine smoke script (not committed): seeded one raw news
  article, one `raw.reddit_posts` row, one `raw.x_users` + `raw.x_posts`
  row (all sharing a common evidence phrase so one fixed propaganda
  response validates against every doc); ran the real ETL chain
  (`authors.sync_x_authors()` -> `documents.load_new_documents()` ->
  `queue.seed_pending_tasks()`, 3 docs / 17 queue rows); ran all FIVE
  landed engines' `process()` (`text`, `citations`, `targets`,
  `propaganda`, `claims`) over the 3 loaded docs with a fake
  `TransportBackend` for the four LLM-backed engines. First all-engines
  composition proof:
  - exactly one `is_current` run per (doc, task) for all 5 applicable
    tasks x 3 docs = 15 rows, no more, no fewer;
  - 3 `sentiment_results` rows, 3 `propaganda_results` rows each with
    `techniques_validated=1`/`techniques_dropped=0`/`density=0.6`, 3
    `propaganda_techniques` rows;
  - `ops.task_queue` carries 17 rows (5 tasks x 3 docs + `bot` x 2 social
    docs, `account_tier` excluded as author-scoped, `bot` excluded for the
    1 news doc) confirming the task-applicability matrix from
    `etl/queue.py` matches what actually got queued.
- `cd ingest && go test ./... -count=1`: all packages pass (regression
  check only — no Go-side change this task).
- Container hygiene: `docker stop`/`docker rm -v` removed the throwaway
  container and its anonymous pgdata volume; `docker ps -a` and a
  volume-creation-timestamp check confirm no leftover container and no
  orphaned volume from this session; all pre-existing containers/volumes
  from earlier, unrelated work were left untouched.

## Follow-ups

- `bot`, `account_classifier`, `narrative_clusterer`, the bot-rollup SQL
  aggregate, and `lean_derivation.py` remain unported (`docs/todos/
  pg-redesign.md`, Phase 6).
- Test fixtures moving from sqlite tempfiles to a Postgres test schema
  stays open until every Phase 6 engine lands (todo checklist item).
