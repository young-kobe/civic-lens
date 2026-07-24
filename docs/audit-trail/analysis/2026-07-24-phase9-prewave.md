# 2026-07-24 — Phase 9 pre-wave: strictly-live API queries, `serving` schema dropped

Phase 9 (see `docs/todos/pg-redesign.md`, plan `has-our-aggregate-method-async-frog`) goes **strictly-live**: dashboard panels aggregate `corpus.*`/`analysis.*` directly at request time — there is no precomputed rollup layer, no per-window rebuild job, no `serving.*` table. This entry records the reversal (an earlier pass of this same pre-wave built a `serving`-schema-based rollup design, described below only as the discarded alternative) and the pre-wave scaffolding that survives it: the shared read-side query helpers, the API response-model contract (camelCase + the lean presentation invariant), and the contract-test harness.

## What shipped

- **`data/pg-migrations/0004_drop_serving.sql`**: `DROP SCHEMA serving CASCADE`. The `serving` schema (created by `0001_north_star.sql`) had no writer — no rollup builder was ever shipped against it, confirmed empty — so this is a clean drop, not a data migration. Applies cleanly on top of `0001`-`0003`; not self-idempotent (same one-shot-apply convention as every other file in this directory).
- **`analysis/src/api/queries/`** (`__init__.py`, `constants.py`, `base.py`) — the read-side helpers every panel query will share:
  - `constants.py` ports the aggregation floors/caps from the pre-redesign `reporting/aggregators/` modules verbatim (see table below).
  - `base.py`: `window_cutoff(window)` (window key -> `TIMESTAMPTZ` lower bound, as a Python `timedelta` subtracted from `now()`); `admission_label()` / `split_admission_counts()` (the `sampled`/`official_record` wording and counting every panel needs — `corpus.documents.admission_class`, `0003_admission_class.sql`); `build_sample_doc(row, admission_class=...)` (assembles one evidence-sample dict from a query row, raising if `source_url` or `confidence` is missing — feeds directly into `api.models.common.SampleDocModel`). No caching layer here — deferred, see Follow-ups.
- **`analysis/src/api/models/`** (`__init__.py`, `common.py`) — the response-shape contract, unaffected by the strictly-live reversal:
  - `CamelModel`: pydantic v2 base, `alias_generator=to_camel`, `populate_by_name=True`.
  - `LeanLabel`: the single place the three-epistemic-kinds lean invariant (owner decision 2026-07-22) is encoded. `kind: Literal['fact','curated','derived']`; a `model_validator` requires `lean_share`/`confidence`/`sample_count` all present when `kind='derived'` and forbids all three otherwise.
  - `SampleDocModel`: `doc_id`, `source_url` (required, non-empty), `snippet`, `confidence` (required), `admission_class: Literal['sampled','official_record']`, `published_at`.
- **`analysis/tests/contract/`** (`__init__.py`, `conftest.py`, `snapshots/.gitkeep`) — the contract-test harness. `conftest.py` re-exports `analysis/tests/pg_fixture.py` (same `CIVIC_TEST_DATABASE_URL`-gated convention as every other integration `TestCase` in this repo) and provides `assert_snapshot_match(name, payload)`: writes `snapshots/<name>.json` when absent, otherwise asserts byte-identical JSON and raises `AssertionError` with a unified diff on mismatch. Named `conftest.py` per the task, but this is a plain importable module, not a pytest fixture file — the repo's test runner is `unittest` (`pytest` is not a dependency); tests import its names directly.
- **`analysis/tests/pg_fixture.py`**: `reset_schema()` now always applies `0004_drop_serving.sql` (after the optional `0002` seed, after `0003`) — every gated module's baseline schema tracks the latest migration.
- **`docs/DATABASE_SCHEMA.md`**: the `serving` schema section replaced with "API query layer — strictly live"; the schema-count overview corrected (six -> five, with a note that `0001` created a sixth that `0004` dropped); the `corpus.political_lean` enum's usage list no longer cites the three now-gone `serving.*.lean` columns.
- **Test coverage**: `analysis/tests/test_api_queries_base.py` (window cutoff, admission helpers, `build_sample_doc` — pure plus one integration test against a real `corpus.documents` row), `analysis/tests/test_api_models.py` (`LeanLabel`/`SampleDocModel` validators, pure), `analysis/tests/contract/test_snapshot_helper.py` (write-then-match round trip, pure).

## Ported constants (verbatim — see `analysis/src/api/queries/constants.py`)

| Name | Value | Origin |
| --- | --- | --- |
| `WINDOWS` | `24h/7d/30d/90d` as `timedelta`s | `reporting/aggregators/base.py::TIME_WINDOWS` (minus its `'all'` key — an all-time view is "no cutoff", not a fifth window key) |
| `STRONG_CONFIDENCE_THRESHOLD` | `0.7` | `reporting/aggregators/constants.py` |
| `SNIPPET_MAX_CHARS` | `120` | `reporting/aggregators/evidence.py` |
| `MAX_EVIDENCE_PER_SAMPLE` | `5` | `reporting/aggregators/evidence.py` |
| `MAX_DISTRIBUTION_SAMPLES_PER_BUCKET` | `15` | `reporting/aggregators/sentiment/samples.py` |
| `MAX_SAMPLES_PER_TOPIC` | `5` | `reporting/aggregators/sentiment/samples.py` |
| `MAX_SAMPLES_PER_ENTITY` | `10` | `reporting/aggregators/sentiment/samples.py` |
| `MAX_SAMPLES_PER_TARGET` | `5` | `reporting/aggregators/sentiment/samples.py` |
| `MIN_TARGET_SAMPLE_N` | `5` | `reporting/aggregators/sentiment/target_tone.py` |
| `BOT_SCORE_AUTHOR_EXCLUSION` | `0.5` | `reporting/aggregators/sentiment/target_tone.py` |
| `MIN_SAMPLED_AUTHOR_POSTS` | `3` | `reporting/aggregators/sentiment/entities.py` |
| `MIN_SAMPLED_AUTHOR_FOLLOWERS` | `1000` | `reporting/aggregators/sentiment/entities.py` |
| `MAX_SAMPLED_AUTHOR_CARDS` | `12` | `reporting/aggregators/sentiment/entities.py` |

`get_settings().aggregation_min_confidence` (default `0.5`) is deliberately **not** duplicated as a constant here — it is a runtime `CIVIC_`-prefixed setting, and copying its value into a second constant would recreate exactly the drift risk this porting exercise exists to avoid. Query modules should read it live via `get_settings()`, matching `reporting/aggregators/base.py::get_aggregation_min_confidence()`'s existing pattern.

## Why (the strictly-live reversal)

- **Single source of truth per number.** A precomputed `serving.*` row is a second copy of a count/mean already derivable from `corpus.*`/`analysis.*` — every rollup table this pre-wave drafted (`entity_profiles`, the reshaped `propaganda_rollups`, `bot_rollups`' lean columns) denormalized values that already live elsewhere. Reading live removes the copy, and the class of bug where the copy and the source disagree.
- **Data only changes at pipeline completion.** The corpus updates once per scheduled pipeline run (`ops.pipeline_runs`), not continuously — so a live aggregate computed at request time is exactly as fresh as a rebuilt rollup would have been, for a fraction of the code (no rebuild scheduler, no rebuild-transaction contract, no half-built-window race to guard against).
- **Corpus is small.** The redesign's own sizing note (Phase 1 audit-trail: a 2GB box, a corpus far short of a scale where aggregate queries would need precomputation) means request-time `GROUP BY`/`COUNT` over `corpus.documents`/`analysis.*` is cheap enough that the rollup layer would have been complexity paid for headroom nobody needs yet.
- **Freshness signal already exists.** `ops.pipeline_runs` (populated by the scheduler, Phase 7) already answers "when did the corpus last change" — `serving.refreshes` would have been a second, parallel answer to the same question.

## LeanLabel invariant (owner decision 2026-07-22, unaffected by the reversal)

A lean is one of three epistemic kinds, and the UI must never blur them:

- **`fact`** — an official's party registration (stated, not inferred).
- **`curated`** — an outlet/subreddit's editorial lean (a human judgment call recorded in `corpus.entities.lean`/`lean_source`).
- **`derived`** — a statistical estimate (`analysis.author_leans`/`analysis.narrative_leans`), which must never render without the evidence backing it: `lean_share`, `confidence`, `sample_count`.

`LeanLabel`'s `model_validator` is the single enforcement point: `kind='derived'` requires all three evidence fields; `kind` in (`fact`, `curated`) forbids all three. Every API response surfacing a lean should use this shape rather than a bare string.

## Validation performed this task

- Fresh throwaway `postgres:17-alpine` (unique container name, random host port, no repo volumes): `0001` -> `0002` (seeded) -> `0003` -> `0004` applies cleanly; confirmed the `serving` schema and `serving.window` type are gone afterward.
- Full gated Python suite (`CIVIC_TEST_DATABASE_URL` pointed at the throwaway container) via one `unittest discover analysis/tests` process: 900 tests, `OK`.
- Full ungated suite (same command, no DB env var): 900 tests, `OK` (113 skipped — the gated cases).
- Throwaway container and its anonymous volume torn down after validation.

## Follow-ups

- [ ] A caching layer keyed on `ops.pipeline_runs.pipeline_run_id` may be worth adding once real request latency against a live corpus is measured — explicitly deferred, not designed here.
- [ ] Codify the fact/curated/derived presentation invariant in `.agent/rules/media-analysis.md` (named in the Phase 9 todo; out of scope for this pre-wave — `.agent/rules/` was not in this task's assigned files).
- [ ] Actual panel query modules (sentiment, bot, propaganda, narrative, entity-profile) that call these helpers are the next wave, not this pre-wave.
