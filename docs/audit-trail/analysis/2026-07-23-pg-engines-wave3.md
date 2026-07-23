# 2026-07-23 — Postgres redesign Phase 6 Wave 3: bot, account_tier, narrative_clustering engines

`analysis/src/engine/bot_detection.py`, `account_tier.py`, and
`narrative_clustering.py` are new — the third and final batch of Phase 6
engine ports (plan `has-our-aggregate-method-async-frog`), built to the
pure `analyze()` + thin `process()` contract Wave 1 set
(`2026-07-23-pg-engines-wave1.md`: `text.py`, `citations.py`,
`entity_resolver.py`) and Wave 2 extended (`2026-07-23-pg-engines-wave2.md`:
`targets.py`, `propaganda.py`, `claims.py`). This closure entry also fixes
a gated-test-suite fragility, lifts `embed()` onto the shared LLM client,
consolidates Wave 3's constants, and reconciles `analysis.*` write
ownership in `DATABASE_SCHEMA.md`.

## What shipped

### `engine/bot_detection.py` — LLM-owned labels, failed-run contract, narrowed de-bias

Ports old `engine/bot.py`'s `HybridBotDetector` onto `analyze()`/
`process()`. The deterministic signal battery (spam keywords, repetition,
hedge-phrase rate, typographic tells, account-age/follower/follow-ratio
flags) always runs and feeds the LLM prompt; a successful call is `hybrid`.
Unlike old `bot.py`, an LLM failure/unavailable backend no longer degrades
to heuristic-only — it raises, and `process()` records a **failed run**
(text.py's contract). The battery alone only classifies the empty-text
`unknown` case.

- **Dropped, not ported**: `sustained_tweet_rate_flag` (+0.15) /
  `unlisted_active_flag` (+0.08) needed `tweet_count`/`listed_count`, which
  `corpus.authors` does not carry (see post_count verdict below) — score
  ceiling for an X account is lower than old `bot.py`'s by up to ~0.23.
- **De-bias scope change (owner-confirmed 2026-07-23)**: government/
  business de-bias applies to the deterministic `aggregated_score` only.
  `label`/`confidence` on a `hybrid` run are LLM-owned; no hard label
  override is restored — narrower than old `bot.py`'s forced
  `is_bot=False`, a deliberate scope change.
- Author fields come from a caller-side `corpus.authors` join; Reddit has
  no author snapshot yet (`sync_reddit_authors` is a no-op).
- **Bot rollup**: `refresh_author_bot_scores()` is a plain SQL aggregate
  (`AVG`/`VAR_POP`/`COUNT` over current `bot_signals`, joined through
  `is_current`) replacing `job_runner.run_account_bot_rollup` — full
  recompute every call, reprocess supersession falls out of the join.

**post_count verification (verdict: stays dropped)**: checked whether
`corpus.authors` carries a `post_count` fed from `raw.x_users.tweet_count`.
It does not — `0001`'s `corpus.authors` has no such column at all (the
`post_count` in the schema belongs to `ops.x_api_budget`, unrelated), and
`etl/authors.py::sync_x_authors` never reads `tweet_count`. Restoring the
signal needs a DDL + ETL change, out of scope here; the module's docstring
already documented this and now cites the confirmation.

### `engine/account_tier.py` — elected-flag derivation

Deterministic `classify_authors()`: no LLM, no `analysis.runs` row
(`author_profiles` is corpus, not analysis). Closes the seeding gap left
when `registry_sync.py` retired, mirroring its handle-vs-`entity_key`/
`entity_aliases` matching exactly.

`corpus.entities.elected` (added this wave) is the curated truth tier
derives from: `TRUE` -> `elected_official`, `FALSE` -> `affiliated`
(cabinet secretaries, agency heads, party chairs). `NULL` defaults
cautiously to `affiliated` with a warning log. Seed counts: 539 elected /
12 affiliated / 36 NULL (outlet/subreddit, n/a) — verified live.
Idempotent: `LEFT JOIN author_profiles` never re-selects an already-
profiled (incl. hand-edited) author.

### `engine/narrative_clustering.py` — deferred-materialization fragmentation fix

Groups current `analysis.claims` into narratives, carrying forward the old
clusterer's anchor-on-first-claim comparator (jaccard/embedding,
`CIVIC_NARRATIVE_SIMILARITY_MODE`) with one fix: a claim matching nothing
is no longer materialized as a one-doc narrative (`MIN_NARRATIVE_SUPPORT =
2` distinct docs) — production data showed the old rule fragmenting into
8,477 near-singletons over 7,116 docs. Sub-threshold claims stay
unclustered this run, reconsidered next run against a larger anchor pool.
`plan_clustering()` is pure; `run()` composes it with loading/embedding/
writing, a documented exception to `results/store.py`'s sole-writer rule
(no narrative-shaped `RunHandle` method; batch job, not one run per doc).

**`narrative_docs.added_by_run`**: the DDL as first landed had no
run-reference column, so which later run extended a narrative was only
approximate (`discovered_at` vs. a `clustering_runs` window). Closed:
`added_by_run BIGINT REFERENCES clustering_runs` (nullable), populated on
every insert, founding and extending. Gated test extended to assert
extension rows carry the extending run's id, not the founding one's.

`narrative_docs.confidence` documented: it stores the linked claim's own
`analysis.claims.confidence`, copied at insert — **not** the jaccard/cosine
similarity (computed in-memory, never persisted). Matches the old
clusterer's convention; the task brief's premise that the column stores
"the similarity value" didn't match the shipped code or the pre-redesign
precedent, so it's documented as it actually behaves (Rule 7: surface the
conflict, don't write false documentation) — flagged for whoever revisits
comparator-strength persistence.

## `LLMClient.embed()` lift

`llm/client.py` gains `embed()` + `supports_embedding`: passthrough to the
backend's `embed()` when its class overrides `BaseLLMClient.embed`'s no-op
default (every backend "has" the attribute via the base class, so a plain
`hasattr` — Wave 3's original workaround — can't tell real support from
the default). Raises naming the backend when unsupported.
`narrative_clustering._resolve_embed_fn` now goes through
`llm.client.get_client()` instead of the raw backend; the `hasattr`
workaround comment is gone. Tested both ways with fake transports.

## Trivial-content decision (`engine/text.py`)

Owner decision: the trivial-content short-circuit is now a `done`
deterministic run with **no `sentiment_results` row**, replacing the
neutral-at-0.5 placeholder. `TRIVIAL_CONTENT_CONFIDENCE` deleted.
`TextAnalysis.sentiment` is `Optional`; `process()` skips the save calls
and finishes with `confidence=None` when it's `None` — the store already
supported empty finishes (`citations.py` proved it first). Test updated to
assert no sentiment row and `confidence`/`raw_response`/`prompt_version_id`
all `NULL`.

## Constants consolidation

Moved (task strings + numeric budgets/caps, per the Wave 2 rule):
`BOT_TASK`, `BOT_PROMPT_TEXT_MAX_CHARS`, the account-age/follower/
follow-ratio thresholds, `ELECTED_OFFICIAL_TIER`/`AFFILIATED_TIER`/
`CURATED_LIST_METHOD`, `MIN_NARRATIVE_SUPPORT`, `CLAIM_LOOKBACK_DAYS`,
`NARRATIVE_NAME_MAX_CHARS`. Stayed local: `_GOVERNMENT_VERIFIED_TYPE`/
`_BUSINESS_VERIFIED_TYPE` (de-bias gate values, one reader — the
`_STANCE_MAP` precedent) and compiled regexes; `account_tier.py`'s SQL
strings; `_STOPWORDS`/`_TOKEN_RE`/`EmbedFn` (private lookup, pattern, type
alias).

## Gated-suite fragility fix (must-fix)

**Symptom**: the full gated suite as one `unittest discover` process was
reported to trip cross-module DB-fixture interference; each module passed
alone.

**Root cause**: every gated `setUpClass` dropped/recreated the schema via
a raw `psycopg` connection WITHOUT first closing the shared `common/db.py`
`ConnectionPool` — only per-test `setUp`/`tearDown` did. A pool left open
(or mid async-reconnect) by the previous class holds connections whose
session state (prepared plans, catalog caches) is keyed to the OLD
schema's relation OIDs; dropping while that pool could still be handing
out/reconnecting a connection races the next class's fresh pool against
stale catalog state — intermittent by nature, explaining "passes alone,
fails together."

**Fix**: `analysis/tests/pg_fixture.py` (new) — `reset_schema(dsn,
seed=bool)` closes the pool FIRST, always, before dropping;
`begin_test(dsn)`/`end_test(prev_url)` wrap the per-test dance. All twelve
gated modules (bot, narratives, targets, claims, propaganda, etl_documents,
citations, etl_queue, text, account_tier, result_store, etl_authors, plus
`test_pg_db`'s round-trip class) now call the shared helper instead of
copy-pasting drop/apply SQL and pool lifecycle — three-plus consumers
justifies the same consolidation rule already applied to constants.
Per-module truncation logic is unchanged (genuinely module-specific).

## Ownership reconciliation

`DATABASE_SCHEMA.md` said `results/store.py` is the only writer of
`analysis.*` — inaccurate: `author_bot_scores` (bot rollup) and
`clustering_runs`/`narratives`/`narrative_docs` (narrative_clustering) are
written directly by their computing modules. Amended: `results/store.py`
owns every **run-anchored typed result** table (everything `RunHandle.
save_*()` writes, plus `runs`/`prompt_versions`) — still the only inserter,
still the traceability-contract enforcement point. Named aggregate/derived
tables that are NOT run-anchored are owned by the module that computes
them, documented as an explicit exception at that module's own docstring.
Writer table, `## analysis` intro, and "Result-store write semantics" all
updated consistently.

## Why

Same no-heuristic-fallback contract Waves 1-2 established, extended to bot
and to the two batch modules the contract doesn't literally fit (documented
as explicit exceptions rather than forced into a bad shape). `elected` and
`added_by_run` are the same class of DDL-vs-plan drift Waves 1-2 caught and
closed at zero cost while the schema is greenfield. The gated-suite
fragility blocks Phase 6 closure's own verification requirement and would
worsen as Phase 7 adds more gated modules.

## Validation performed this task

Full unit suite (no DB): 803 tests, 80 skipped, 0 failures. Clean-room
(fresh `postgres:17-alpine`, real `civic-ingest` binary): virgin
`0001`+`0002` apply clean; `elected` counts 539/12/36-NULL confirmed; `\d
analysis.narrative_docs` shows `added_by_run`. Full gated suite via one
`unittest discover` process: **3 consecutive clean runs**, 803 tests each,
0 skipped, 0 failures — the fragility fix proven stable, not a one-off.
Ungated run also clean. `cd ingest && go test ./... -count=1`: all packages
pass (regression check, no Go-side change this task). Container hygiene:
throwaway container + its anonymous pgdata volume removed after the final
run; no leftovers from this session.

## Follow-ups

`engine/lean_derivation.py` remains unported — the last open Phase 6 item
besides the sqlite-to-Postgres test-fixture box (effectively satisfied for
every landed engine already; tracks `lean_derivation.py`'s own tests once
it lands). Bot score ceiling gap (~0.23) stays open pending a
`corpus.authors` schema addition or a golden-set recalibration.
`narrative_docs`'s comparator similarity is computed but never persisted —
flagged, not fixed, this task. Phase 7 (scheduler) and Phase 8 (recompute
pilot) are next.
