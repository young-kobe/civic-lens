# 045 — Analysis + API Layer Audit Remediation (2026-04-20)

Lands §§ 2, 3, 8, 9 of the 2026-04-19 non-security audit (analysis layer,
API layer, narrative deep-dive, bot deep-dive). Companion to walkthrough
044 which landed § 1 (ingestion). UI-layer findings (§ 4) are not addressed
here.

## Scope

- § 2 Analysis layer — the LLM 0-100 confidence bug, DRY / perf wins in
  aggregators and ETL, broad-except narrowing, analyzer hot-path fix,
  retry-policy unification.
- § 3 API layer — versioning under `/api/v1`, decorator-fied cache helper,
  deep health check, rate-limited triggers, typed query params,
  modularized routers.
- § 8 Narrative deep-dive — clusterer audit metadata, anchor warm-up,
  per-claim pooling, embedding-fallback observability, enum cleanup,
  propagation naming.
- § 9 Bot deep-dive — prompt guardrail, parse-time coercion, fallback
  reason plumbing.

Items deliberately deferred (still OPEN on the audit): confidence
calibration (blocks on a golden set), narrative/claim LLM-plumbing
consolidation, Pydantic response models, `loader.py` responsibility split,
settings nesting, the O(N·M) cosine loop, per-aggregator JSON reparsing
outside sentiment/geo. These are flagged as **OPEN (deferred)** in the
audit with rationale.

## Analysis layer

### LLM confidence 0-100 bug (live)

- `llm/base.py`: new `_coerce_numeric_scales` walks the parsed response
  against the schema before validation. Numeric fields whose schema has
  `maximum=1` and `minimum>=0` are divided by 100 when the value lands in
  `(1, 100]`, with a warning logged. Values outside that band are left for
  the validator to reject.
- `llm/prompts.py` (moved from `engine/prompts.py`): `BOT_SYSTEM_PROMPT`,
  `TEXT_ANALYSIS_SYSTEM_PROMPT`, and `CLAIM_EXTRACTION_SYSTEM_PROMPT` each
  gained a rules line specifying "decimals in [0.0, 1.0], never a
  percentage" with a worked example (`0.85`, not `85`). Claim prompt also
  carries a full input→output example. Prompt versions bumped
  (`bot-v2`, `text-analysis-v3`, `claim-extraction-v2`).

### Perf / DRY wins

- `aggregators/base.py`: new `fetch_task_rows(cursor, select_clause,
  task_type, cutoff, min_confidence, ...)` helper consolidates the
  cutoff-branching every aggregator was copy-pasting.
- `aggregators/sentiment.py`: the two SELECT branches collapsed into
  `fetch_task_rows` calls. Sentiment now accepts an optional `bot_docs`
  set so job_runner can compute bot flags once and share.
- `aggregators/geo.py`: same `bot_docs` injection hook.
- `scheduler/job_runner.save_snapshots`: computes `bot_docs` once per
  refresh and passes it to every sentiment + geo aggregation across the
  four time windows. Cuts the repeated `get_bot_flagged_doc_ids` query
  from 12x to 1x per run.
- `etl/loader.py`: each `_load_*_batch` now preloads its source-type's
  `ident` set once per batch instead of per-doc SELECTs. On a 1k-doc
  batch that eliminates 1k round-trips.

### analyzer.py hot path

- New `_word_offsets(text)` builds a `char-index → word-index` map once
  per inference. `_find_entity_positions` and `_is_keyword_near_entity`
  take the map as a parameter and do O(1) lookups, eliminating the
  `text_lower[:pos].split()` repeated up to 100x per GOP-keyword scan
  (04-16 audit carry-over).

### Retry / error-handling

- `llm/ollama.py`: now uses `(2 ** attempt) + 0.5` backoff to match
  `gemini.py`. One shared policy.
- Narrowed broad `except Exception` in `analyzer.py` (init + LLM call),
  `bot.py` (LLM call), and `loader.py` (trafilatura) to the specific
  classes they're meant to catch, with a comment on each.
- `STRONG_CONFIDENCE_THRESHOLD` duplicate in `engine/constants.py`
  removed; `aggregators/constants.py` is the sole source of truth.

### Engine reorg

- `engine/prompts.py` → `llm/prompts.py`. Prompts are an LLM-contract
  artifact (versioned alongside schemas), not an engine internal. Every
  importer updated.

## API layer

### Versioning

- Canonical prefix is now `/api/v1` (defined as `V1_PREFIX =
  f"/api/{API_VERSION}"` in `api/server.py`). The legacy `/api/*` prefix
  is not accepted — clients must migrate. UI's `services/api.ts`
  `API_BASE` updated.
- `/health` stays unversioned at the app root. Infra probes shouldn't
  have to track the API version.

### Server modularization

Before: 380-LoC `server.py` holding app wiring + all handlers + the
cache-or-fallback helper + the admin-token dep + the legacy analysis
queue. After:

```
analysis/src/api/
├── server.py              # FastAPI app, include_router wiring, prefix constants
├── dependencies.py        # require_admin_token, enforce_trigger_cooldown
├── cache_utils.py         # get_cached_or_fallback + WindowLiteral type
└── routers/
    ├── __init__.py
    ├── health.py          # unversioned
    ├── admin.py           # /cache-status, /run/etl, /run/full-pipeline
    ├── data.py            # /sentiment, /bot-activity, /narratives, /propaganda, /geo-sentiment
    └── review.py          # /review/queue, /review/submit, /review/stats
```

Each router declares its own admin-token dependency where appropriate
(`admin`, `review`) and stays free of it where not (`data`, `health`).

### Other API fixes

- `/api/profiles` endpoint + `OutletAggregator` wiring in job_runner
  deleted (no UI caller since walkthrough 029-ish). Aggregator class and
  its tests kept for now.
- `_get_cached_or_fallback` lifted into `api/cache_utils.get_cached_or_fallback`
  so every data router calls through the same helper.
- All data endpoints now accept `window: Literal["24h","7d","30d","90d"]`
  so invalid values 422 instead of silently computing live.
- `/health` probes DB (`SELECT 1`) and cache-dir existence; returns
  `status: degraded` on failure instead of unconditional `ok`.
- Pipeline triggers go through `enforce_trigger_cooldown(endpoint)`. Per-
  endpoint in-memory last-fire timestamps; 429 with `Retry-After` when
  a request comes in inside the cooldown. Configurable via
  `CIVIC_PIPELINE_TRIGGER_COOLDOWN_SECONDS` (default 60s).
- `STALE_CACHE_WARN_SECONDS` moved to `settings.stale_cache_warn_seconds`.
- Legacy `process_analysis_queue` / `/api/run/analysis` deleted —
  `job_runner` is the only path.
- `test_api.py` updated for v1 routes, asserts legacy prefix 404s, and
  asserts admin endpoints return 503 without a token (loud
  misconfiguration).

## Narrative layer

- Migration 015 adds `clustering_mode`, `clustering_threshold`, and
  `embedding_model` columns on `narratives`. `_create_narrative` now
  populates them so a future threshold change doesn't silently re-
  interpret old anchors.
- `_warm_anchor_embeddings` runs once at the top of an embedding-mode
  cluster run, materializing any missing anchor embeddings before the
  per-claim loop. The lazy `_anchor_embedding` path remains as a safety
  net for concurrent narratives created mid-run.
- `job_runner.run_claim_extraction`: row-level confidence is the mean of
  per-claim confidences, not `max`. Per-claim array is still in
  `output_json` as the source of truth; aggregators that want stronger
  signal can drill in.
- Clusterer `run()` summary now includes `embedding_fallbacks` so an
  Ollama regression shows up in `job_runner` logs instead of silent.
- Migration 016 rebuilds `narrative_citations` with a tightened
  `link_type` CHECK that drops the unused `repost` value; any stray
  `repost` rows migrate to `retweet`.
- Naming cleanup: `narrative.py::NarrativeAggregator` docstring switched
  from "propagation data" to "coverage data" (walkthrough 035
  compliance). `test_propagation.py` renamed to
  `test_narrative_pipeline.py`.

## Bot layer

- `BotResult.fallback_reason: Optional[str]` added. Populated in the
  `_llm_classify` except branch with `"{exc_type}: {message}"`; rides
  into `ai_outputs.output_json` via `to_dict()`. The `inference_method`
  column still tells you a heuristic fired; `fallback_reason` now tells
  you *why*.

## Tests

- `analysis.tests.test_engines` — 6 tests pass.
- `analysis.tests.test_narrative_pipeline` (renamed) — pass.
- `analysis.tests.test_loader`, `test_bot_rework`, `test_rich_aggregators`,
  `test_aggregation_confidence_filter`, `test_propaganda`,
  `test_propaganda_surfaces` — pass.
- `test_api.py` updated to test versioned routes; requires the server to
  boot (not exercised in the unit-test batch).

## Files touched

```
analysis/src/api/cache_utils.py          (new)
analysis/src/api/dependencies.py         (new)
analysis/src/api/routers/__init__.py     (new)
analysis/src/api/routers/admin.py        (new)
analysis/src/api/routers/data.py         (new)
analysis/src/api/routers/health.py       (new)
analysis/src/api/routers/review.py       (new)
analysis/src/api/server.py               (slimmed to wiring only)
analysis/src/common/settings.py          (stale_cache_warn_seconds, pipeline_trigger_cooldown_seconds)
analysis/src/engine/analyzer.py          (hot-path fix, narrowed except, import move)
analysis/src/engine/bot.py               (fallback_reason wiring, narrowed except, import move)
analysis/src/engine/account_classifier.py (import move)
analysis/src/engine/claim_extractor.py   (import move)
analysis/src/engine/propaganda_detector.py (import move)
analysis/src/engine/constants.py         (dropped duplicate threshold)
analysis/src/engine/models/engine_models.py (fallback_reason field on BotResult)
analysis/src/engine/narrative_clusterer.py (audit metadata, anchor warm-up, embedding_fallbacks)
analysis/src/etl/loader.py               (N+1 fix, narrowed except)
analysis/src/llm/base.py                 (_coerce_numeric_scales)
analysis/src/llm/ollama.py               (unified backoff)
analysis/src/llm/prompts.py              (moved from engine/; added guardrails + worked examples)
analysis/src/reporting/aggregators/base.py (fetch_task_rows helper)
analysis/src/reporting/aggregators/geo.py (bot_docs injection)
analysis/src/reporting/aggregators/narrative.py (propagation → coverage naming)
analysis/src/reporting/aggregators/sentiment.py (fetch_task_rows, bot_docs injection)
analysis/src/scheduler/job_runner.py     (bot_docs share, dropped outlet, mean claim confidence)
analysis/tests/test_api.py               (v1 routes, legacy 404 assertion)
analysis/tests/test_narrative_pipeline.py (renamed from test_propagation.py)
data/migrations/015_narrative_cluster_audit.sql        (new)
data/migrations/016_narrative_citations_drop_repost.sql (new)
ui/src/services/api.ts                   (API_BASE → /api/v1)
docs/audits/04_19_2026.md                (OPEN → REMEDIATED tagging for landed items; 044 items tagged too)
```
