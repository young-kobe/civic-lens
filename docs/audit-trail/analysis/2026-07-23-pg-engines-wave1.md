# 2026-07-23 — Postgres redesign Phase 6 Wave 1: text engine, citations engine, entity resolver

`analysis/src/engine/text.py`, `analysis/src/engine/citations.py`, and
`analysis/src/common/entity_resolver.py` are new — the first three Phase 6
engine ports (plan `has-our-aggregate-method-async-frog`, checklist
`docs/todos/pg-redesign.md`). Both engines compose against the Phase 5
modules (`llm/client.py`, `results/store.py`, `engine/validation.py`; see
`2026-07-22-pg-analysis-plumbing.md`) and Phase 4 ETL output
(`2026-07-22-pg-etl-authors-documents-queue.md`). This entry also closes a
Wave 1 discrepancy: `corpus.x_posts` gains the two reference columns the
plan's sketch specified but the first-landed `0001_north_star.sql`
omitted. `documents.py`'s admission gate was decomposed the same wave;
recorded here since it lands in the same commit set.

## What shipped

### `engine/text.py` — the design contract for every remaining Phase 6 engine

- Pure `analyze(doc, client, resolver) -> TextAnalysis` + thin
  `process(doc, client, resolver) -> run_id`; `analyze()` does no I/O — the
  `LLMClient` and `EntityResolver` are both injected, not constructed
  inside. `process()` is the only place that touches `results/store.py`.
- **No heuristic fallback** (deliberate behavior change from
  `engine/analyzer.py`): a failed/unavailable LLM call is a recorded
  `status='failed'` run, re-queued, never silently replaced by a keyword-
  proximity guess. A wrong-but-confident heuristic label is worse than an
  honestly-absent one when every output is confidence-scored and traceable
  (`.agent/rules/invariants.md`); Phase 7's queue retry makes "failed, try
  again" cheap, removing the old fallback's reason to exist. Trivial
  content (mentions/links only) still short-circuits deterministically
  (`is_trivial_content()`) — that's a shape check, not a substitute guess.
- One `analysis.runs('text')` row feeds both `sentiment_results` and
  `favorability_stances` (the plan's unification); run confidence is the
  mean of the sentiment score and every stance's own confidence.
- Unresolved favorability targets are dropped and counted
  (`dropped_unresolved`), not stored — `favorability_stances.entity_id` is
  NOT NULL, unlike the (not-yet-ported) targets engine's `target_mentions`,
  which keeps `raw_target` for an unresolved subject.

### `common/entity_resolver.py` — DB-backed resolver replacing YAML lookup

- `EntityResolver(conn=None)` loads active `corpus.entities` +
  `corpus.entity_aliases` once per construction (587 rows today) into an
  in-memory map; `resolve(raw_name) -> Optional[entity_id]` is a pure dict
  lookup after that — no further DB access.
- `canonicalize_entity_name()` and the last-name-ambiguity guard (a derived
  last-name alias is indexed only when exactly one active entity owns that
  last name) are ported from `reporting/entity_registry.py`'s
  `TargetResolver`/`normalize_target_name` — copied, not imported, since
  that module is YAML-tied and retires in Phase 9. Same rules, different
  backing store.
- Second independent instance this wave of "load once, resolve in memory"
  (the first being `authors.py`'s author-id lookup) — worth a shared
  helper if a third engine repeats the shape, not abstracted preemptively.

### `engine/citations.py` — deterministic port, now emits a run row

- Pure `extract(doc) -> list[CitationCandidate]` (regex URL scan +
  x-reference-type mapping, no DB) + `resolve_candidates()` (pure, given a
  pre-fetched natural-key lookup) + thin `process(doc) -> run_id`.
- Deterministic extraction gets `analysis.runs.confidence = 1.0`
  (`DETERMINISTIC_CONFIDENCE`) — the old extractor computed no confidence
  at all; 1.0 is the correct "not probabilistic" reading now that every run
  needs one, not a new uncertainty claim.
- Self-citations are dropped; unresolved x-references are dropped (a
  reply/quote/retweet edge with no known target URL is worse than no edge —
  no re-fetch path exists); unresolved URL citations keep `target_url`.
  Zero candidates is a legitimate `done` run with zero rows, not a skip.
- Module-local constants stay in the module rather than joining
  `engine/constants.py` (a sibling agent was concurrently appending there
  for the text engine this same wave; none of citations' constants are
  genuinely shared yet).

## The `corpus.x_posts` reference-column restoration (this closure)

The plan's `corpus.x_posts` sketch listed `referenced_tweet_id TEXT` and
`referenced_tweet_type TEXT` alongside the engagement counts and
`is_official_tier`. The first-landed `0001_north_star.sql` put both
columns only on `raw.x_posts` (capture layer), which would have forced
`engine/citations.py`'s caller to join `raw.x_posts` at analysis time,
breaking the raw/corpus layering the redesign enforces (analysis reads
`corpus.*`, never `raw.*`). Closed end to end:

- `data/pg-migrations/0001_north_star.sql`: `corpus.x_posts` gains
  `referenced_tweet_id TEXT` / `referenced_tweet_type TEXT` (nullable — an
  original post has neither). Column comment documents the snapshot
  contract. `0001` is greenfield (deployed nowhere), so this was an
  in-place DDL edit, not a new migration.
- `docs/DATABASE_SCHEMA.md`: `corpus.x_posts` bullet updated with the two
  columns and the "snapshotted at ETL load time, no raw.* join needed at
  analysis time" note.
- `analysis/src/etl/documents.py`: `_XCandidate` gains
  `referenced_tweet_id`/`referenced_tweet_type`; `_gather_x_candidates`'s
  SELECT and construction copy both from `raw.x_posts`;
  `_insert_x_candidates`'s `INSERT INTO corpus.x_posts` carries them
  through — identical treatment to the four engagement-count columns
  already on this path. One correction to the task brief as given: the
  engagement-count columns are **not** actually refreshed on conflict
  today — `_gather_x_candidates`'s `WHERE NOT EXISTS (... corpus.documents ...)`
  already excludes any tweet with an existing doc row before the INSERT is
  reached, so `corpus.x_posts`'s `ON CONFLICT (doc_id) DO NOTHING` is
  unreachable in current practice, not a live refresh path. The reference
  columns get the same (currently inert) `DO NOTHING` for consistency with
  every other subtype-table INSERT in this file, rather than inventing a
  `DO UPDATE` this table has never had (`authors.py`'s `corpus.authors`
  upsert does have a live `DO UPDATE` — a different table, not a precedent
  to import here without a reason).
- `analysis/src/engine/citations.py`: module docstring's caller contract
  updated to "the caller reads them off `corpus.x_posts`"; no engine code
  change needed — `CitationDocInput` was already caller-assembled and
  never queried `raw.*` directly.
- Tests: `test_etl_documents.py` gains
  `test_x_post_copies_referenced_tweet_columns_onto_corpus` (seeds
  `raw.x_posts` with both columns, runs `load_new_documents()`, asserts
  `corpus.x_posts` carries the same values). `test_engine_citations.py`'s
  fixture (`_seed_x_doc`) now writes the reference columns onto
  `corpus.x_posts` directly (previously only `raw.x_posts` had them);
  `_doc_input()` now reads them off `corpus.x_posts` via a live query
  instead of a hardcoded pass-through, proving the caller-reads-from-corpus
  contract rather than just asserting it in a comment.

## The `documents.py` admission-gate decomposition

Landed the same wave (not a Phase 6 item, but part of this commit set):
`is_index_page`'s ad hoc inline scoring became a shared `_TextStats`
dataclass (word count, punctuation density, titlecase share, chrome-term
hits, hub-URL shape — computed once) plus four named predicate functions
(`_hub_url_with_thin_prose`, `_unpunctuated_link_list`, `_headline_list`,
`_nav_chrome_heavy`). The per-source deny/recency/index/political checks
became `AdmissionVerdict`-returning `_admit_news_pretext`/
`_admit_news_posttext`/`_admit_reddit`/`_admit_x` functions — one per
source because each applies a different check subset (reddit skips the
index-page check; x has no deny check; news alone splits into pre-/post-
text-extraction phases since extraction is a disk read that must not run
for an already-domain-rejected row). `DocLoadResult` gains a
`rejections: dict[str, int]` reason-keyed tally alongside the pre-existing
named counters — same information, queryable by reason string, logged in
`load_new_documents()`'s summary line.

## Why

- Phase 6's stated design contract (plan, "6. Engines"): "each becomes pure
  `analyze(doc) -> dataclass` + store call." `text.py` and `citations.py`
  are the first two engines built to that contract and set the pattern the
  remaining six will follow.
- No-heuristic-fallback follows from the traceability invariant: a silent
  guess with no model/prompt/evidence cannot be labeled honestly, and the
  new queue-based retry (Phase 7) removes the old fallback's only
  justification (never leaving a doc unanalyzed).
- The reference-column gap was caught during Wave 1 closure review — same
  class of DDL-vs-plan drift as the Phase 4 `outlet_entity_id`/
  `subreddit_entity_id` FK gap and the Phase 5 `runs.error` gap, each
  caught and corrected at zero cost while the schema is still greenfield.
- The admission-gate decomposition responds to `is_index_page` and the
  per-source loops having grown enough inline branching that a change to
  one source's check order risked silently changing another's; naming
  each check as its own function makes that impossible by construction and
  gives rejection reasons a stable string identity for future observability
  (a per-domain composition report is already on the Phase 11 acceptance
  battery).

## Validation performed this task

- Full unit suite (no DB): all pure-core tests for `text.py`, `citations.py`,
  `entity_resolver.py`, and the `documents.py` admission-gate predicates,
  all green.
- Clean-room verification against a fresh throwaway `postgres:17-alpine`
  container (real `civic-ingest` binary, rebuilt from current source):
  `civic-ingest migrate` applies `0001_north_star.sql` +
  `0002_entity_registry_seed.sql` cleanly; re-run is a no-op (idempotent);
  `\d corpus.x_posts` confirms `referenced_tweet_id`/`referenced_tweet_type`
  present and nullable. Full Python suite gated on
  `CIVIC_TEST_DATABASE_URL` pointed at the container: 672 tests, 0 skips,
  all pass; ungated (no DB): 672 tests, 48 skipped, all pass. No failures
  either way this session — `test_api.py` uses an in-process `TestClient`,
  not a live `:8000` server, so the project-memory note about 6 `test_api`
  failures needing one did not apply here.
- Throwaway cross-engine smoke script (not committed): seeded `raw.x_users`
  + two `raw.x_posts` (one original, one reply with
  `referenced_tweet_id`/`referenced_tweet_type` set) + one `raw.articles`
  row; ran the real ETL chain (`authors.sync_x_authors()` ->
  `documents.load_new_documents()` -> `queue.seed_pending_tasks()`);
  asserted `corpus.x_posts` carried the reference columns without touching
  `raw.*` again; ran `engine.text.process()` against the news doc with a
  fake `TransportBackend` (no real LLM call) — confirmed a `done`/
  `is_current` run plus one sentiment row and one favorability-stance row;
  ran `engine.citations.process()` twice — reply doc (one `reply` edge,
  `target_doc_id` resolved to the original tweet) and news doc augmented
  with an un-ingested external URL (one `url_citation` edge, `target_url`
  set, `target_doc_id` NULL) — confirmed all three runs are `is_current`
  and `ops.task_queue` rows exist for every loaded doc. Proves ETL output
  feeds both engines without either reading `raw.*` at analysis time.
- `cd ingest && go test ./... -count=1`: all packages pass (regression
  check only — no Go-side change this task).
- Container hygiene: `docker ps -a` confirms no leftover container; the
  anonymous data volume for the throwaway container's `postgres` image
  (identified by creation timestamp, since the box also runs unrelated
  pre-existing containers/volumes from earlier phases) was individually
  removed; all pre-existing containers/volumes were left untouched.

## Follow-ups (tracked in `docs/todos/pg-redesign.md`)

- Remaining Phase 6 engines (bot, targets, propaganda, claims,
  account_classifier, narrative_clusterer) are not started; each should
  follow the `analyze()`/`process()` split this wave establishes.
- Bot rollup -> plain SQL aggregate, and `lean_derivation.py`, both wait on
  their upstream engines landing first.
- Test fixtures are still per-module hand-rolled Postgres setup/teardown,
  not the shared "Postgres test schema" fixture the plan names for Phase 6
  — worth factoring once a third or fourth engine's test file repeats it.
