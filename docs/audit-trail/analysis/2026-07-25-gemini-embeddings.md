# 2026-07-25 — Gemini embeddings implemented; silent Jaccard downgrade removed

`GeminiClient` now implements `embed()`, and `narrative_clustering.py` no
longer silently downgrades `mode='embedding'` to `mode='jaccard'` when the
configured backend cannot embed. Both close the same gap: every narrative in
the production database was clustered by word overlap, not meaning, because
Gemini (the default backend) had no `embed()` and the clusterer treated that
as a quiet, unlogged-at-warning-level-only fallback rather than a
configuration error.

## What shipped

**`analysis/src/llm/gemini.py`** — `GeminiClient.embed(text, model=None)`,
matching the return-`None`-on-any-failure convention already used by
`ollama.py`/`openai_compat.py`'s `embed()`. Calls the installed `google-genai`
SDK's `client.models.embed_content(model=..., contents=[text[:2000]])` and
reads `response.embeddings[0].values`. Truncation length and the
try/except-log-warning-return-None shape are copied verbatim from the sibling
clients so the three backends stay behaviorally interchangeable from
`narrative_clustering.py`'s point of view. A private module constant,
`_DEFAULT_EMBEDDING_MODEL = "text-embedding-004"`, is used only when a caller
doesn't pass `model` explicitly — production always does, sourced from
`settings.narrative_embedding_model`.

This alone flips `LLMClient.supports_embedding` (`analysis/src/llm/client.py`)
to `True` for Gemini, with **no change to client.py**: that property is an
identity check against `BaseLLMClient.embed`'s no-op default, so implementing
a real `embed()` is the entire fix.

**`analysis/src/engine/narrative_clustering.py`** — `run()`'s embedding-mode
resolution used to be:

```python
if resolved_mode == "embedding":
    if embed_fn is None:
        embed_fn = _resolve_embed_fn(embedding_model)
    if embed_fn is None:
        resolved_mode = "jaccard"
```

A backend whose class never overrides `embed()` (any future regression, or
Gemini before this change) made `_resolve_embed_fn` return `None`, and the
third line silently rewrote `resolved_mode` — the run proceeded, wrote a
`clustering_runs` row stamped `mode='jaccard'`, and nothing downstream could
tell the difference between "jaccard was requested" and "embedding was
requested and quietly failed to start." Now:

```python
if resolved_mode == "embedding" and embed_fn is None:
    embed_fn = _resolve_embed_fn(embedding_model)
    if embed_fn is None:
        raise RuntimeError(...)
```

The raise happens before `_open_clustering_run()` — no `clustering_runs` or
`narratives` rows are written for a run that should never have started.

**What did not change**: a per-text `embed()` call failing mid-run (a live,
embed-capable backend returning `None` for one claim — a down server, a rate
limit, a timeout) still degrades only that claim to jaccard and is counted in
`embedding_fallbacks`, exactly as before. That is a transient runtime
condition, not a configuration error, and conflating the two was explicitly
out of scope for this fix. Explicitly choosing `CIVIC_NARRATIVE_SIMILARITY_
MODE=jaccard` is unaffected either way — the new check only evaluates when
`resolved_mode == "embedding"`.

## Where the check lives, and why

Narrative clustering has no dedicated startup/config-validation entry point —
`scheduler/pipeline.py` calls `narrative_clustering.run()` directly, with no
preflight step, and adding one there is out of this change's file ownership
(pipeline/scheduler wiring is a separate workstream). Given that, the check
is placed as early as this module can put it: at the top of `run()`, before
any DB I/O (`_open_clustering_run`, `_load_existing_anchors`, etc.). A
misconfigured deployment fails on the very first clustering invocation —
loudly, and before writing anything — rather than on some later run once
data has already been silently degraded. This is the closest approximation
to "startup-time" available without expanding scope into
`scheduler/pipeline.py`; a true preflight check (run once at process start,
independent of any particular clustering pass) would be a better long-term
home and is listed as a follow-up.

Settings-level validation (a pydantic validator on `Settings` asserting the
configured backend supports embedding) was considered and rejected:
`settings.py` would need to import and instantiate the live LLM client to
check `supports_embedding`, which both inverts the dependency direction
(`llm/*` already imports `settings`) and gives loading configuration a live
network/SDK side effect. Keeping the check in `narrative_clustering.py`,
where the resolved client is already being asked to do real work, avoids
both problems.

## Known gap: the default embedding model name doesn't match the default backend

`settings.narrative_embedding_model` defaults to `"nomic-embed-text"` — an
Ollama model tag — while `.env.example` ships `CIVIC_LLM_BACKEND=gemini` as
the recommended production backend. With this change alone, a fresh
production deploy now has a backend that *can* embed (Gemini,
`supports_embedding=True`), but every `embed_content` call will be made with
`model="nomic-embed-text"`, which does not exist in Gemini's API — so every
call still fails, just per-text instead of at the configuration-check level,
degrading through the legitimate `embedding_fallbacks` path this change
deliberately preserves (see "What did not change" above). The net effect for
an unconfigured Gemini deployment is unchanged from before this fix: still
effectively all-jaccard, just for a different, harder-to-notice reason (a
climbing `embedding_fallbacks` counter in the logs rather than
`mode='jaccard'` in `clustering_runs`).

This was left alone rather than silently fixed, because picking the "right"
default is an operational/deployment decision outside this change's
instructed scope (implement `embed()` and stop the silent mode-fallback).
`docs/deployment/plan.md` §4 is the only in-repo source naming a specific
Gemini embedding model (`text-embedding-004`, matching the constant added to
`gemini.py`), so operators running `CIVIC_LLM_BACKEND=gemini` with
`CIVIC_NARRATIVE_SIMILARITY_MODE=embedding` (the default) must set
`CIVIC_NARRATIVE_EMBEDDING_MODEL=text-embedding-004` (or whatever Gemini's
current recommended embedding model is at deploy time) explicitly. Flagged
here rather than changed because it touches a settings default two other
concurrent workstreams (`.env.example`, `scheduler/pipeline.py`) also read.

## Re-clustering existing data

Every narrative currently in the database was built under `mode='jaccard'`
(whether by explicit config or, before today, by the silent downgrade this
change removes). Getting semantic narratives requires an actual re-cluster
run with `CIVIC_NARRATIVE_SIMILARITY_MODE=embedding` and a valid
`CIVIC_NARRATIVE_EMBEDDING_MODEL` in place — flipping the setting alone does
not retroactively re-embed anything already written. A re-cluster consumes
one embedding call per already-extracted claim/anchor
(`_embed_pending_claims`/`_warm_anchor_embeddings`), which is cheap relative
to a generation call (no output tokens, small input) — this does not require
re-running claim extraction or any other LLM generation stage, only the
clustering pass itself.

## Tests

`analysis/tests/test_gemini_client.py` — new `TestGeminiEmbed`: a successful
`embed()` call returns the vector from `response.embeddings[0].values`; the
model kwarg passed to `embed_content` is the caller's `model` when given, or
`_DEFAULT_EMBEDDING_MODEL` when not; an API exception, an empty
`embeddings` list, an unavailable client, and empty input text all return
`None` rather than raising or fabricating a vector.
`test_supports_embedding_flips_true_now_gemini_implements_embed` pins the
regression this whole change targets: it asserts `LLMClient(GeminiClient(...)
).supports_embedding is True`, with `llm/client.py` untouched.

`analysis/tests/test_engine_narratives.py` — new
`MisconfiguredEmbeddingModeTests`:
`test_unsupported_backend_raises_instead_of_silently_falling_back` patches
`analysis.src.llm.client.get_client` to return a fake backend with
`supports_embedding = False` and asserts `nc.run(mode="embedding")` raises
`RuntimeError` — the exact regression guard for the bug this change fixes.
`test_check_is_bypassed_when_caller_injects_embed_fn_directly` asserts the
resolver (and thus the check) is never reached when a caller supplies
`embed_fn` directly, since `_resolve_embed_fn`/`get_client` must not run in
that path. Both are Tier-1 (no DB): `run()` raises before any DB connection
is opened, so neither test needs `CIVIC_TEST_DATABASE_URL`.

No live network calls are made in any test — the `google-genai` SDK is
mocked via `sys.modules` (existing convention in this file), and the
narrative tests mock `get_client` directly.

Full suite: 1109 tests, 0 failures (was 1099 before this change; +10 new
tests — 8 in `test_gemini_client.py`, 2 in `test_engine_narratives.py`).
Skip count moved from 242 to 243 in the same run, entirely from unrelated
concurrent work on this branch (retry/backoff error classification in
`llm/client.py`), not from anything in this change.

## Follow-ups

- Give narrative clustering (or the scheduler generally) a real startup/
  config-validation entry point, and move this check there instead of inside
  `run()`. Out of scope here: that file is `scheduler/pipeline.py`, owned by
  a concurrent workstream.
- Decide and set `CIVIC_NARRATIVE_EMBEDDING_MODEL` for the Gemini backend in
  `.env.example` (see "Known gap" above) — otherwise the misconfiguration
  check this change adds cannot catch the mismatch, since Gemini *does*
  support embedding in the abstract; only the specific model name is wrong.
- Re-cluster existing narratives once the above is set, to replace the
  all-jaccard data currently in `analysis.narratives` with semantic
  clusters (see "Re-clustering existing data" above).
