# 033 — Narrative Reader Layer + Embedding Clustering

## Context

Walkthrough 032 wired the narrative-propagation writers (citations, claim extraction, narrative clustering) but left two follow-ups open: the data was only visible via SQL, and the lexical Jaccard clusterer split semantic synonyms into separate narratives. This walkthrough closes both.

## Changes

### Reader layer (Narratives surface to UI)

- `analysis/src/reporting/aggregators/narrative.py` — new `NarrativeAggregator`. Returns the top-N narratives in a time window with: name, origin (source + domain), supporting doc count, per-source breakdown, daily timeline, net sentiment over supporting docs, and inbound citation count.
- `analysis/src/reporting/models/aggregator_models.py` — new `NarrativeSummary` dataclass (exported via `models/__init__.py`).
- `analysis/src/reporting/aggregators/__init__.py` — export `NarrativeAggregator`.
- `analysis/src/api/server.py` — new `GET /api/narratives?window=...&limit=20` endpoint, served from cache via `_get_cached_or_fallback` like every other read endpoint.
- `analysis/src/scheduler/job_runner.py` — `save_snapshots()` now caches `narratives_<window>_20` for 24h/7d/30d/90d.
- `ui/src/types.ts` — `NarrativeSummary`, `NarrativeSourceBreakdownItem`, `NarrativeTimelinePoint`.
- `ui/src/services/api.ts` — `fetchNarratives(window, limit)`.
- `ui/src/pages/Narratives.tsx` — new page: top-narratives list with one row per narrative showing claim text, origin, source-mix bar, sparkline of daily count, total docs, net sentiment, inbound citation count. Includes the same amber sampling disclaimer as the sentiment page (narratives are claim-clusters, not opinion polls; origin is "first ingested" not "first in the world").
- `ui/src/pages/index.ts`, `ui/src/App.tsx` — wire the Narratives tab between Public Sentiment and Bot Activity.

### Embedding-mode narrative clustering

- `analysis/src/llm/base.py` — adds default `embed(text, model=None) → Optional[list]` (returns None) on `BaseLLMClient` so any backend can opt in.
- `analysis/src/llm/ollama.py` — implements `embed()` against Ollama's `/api/embeddings` endpoint. Returns None on any failure so the caller falls back without crashing.
- `analysis/src/common/settings.py` — `narrative_similarity_mode` ("jaccard" | "embedding"), `narrative_embedding_model` (default `nomic-embed-text`), and per-mode thresholds (`narrative_jaccard_threshold=0.3`, `narrative_embedding_threshold=0.65`).
- `data/migrations/008_narrative_anchor_embedding.sql` — adds `narratives.anchor_embedding_json` (nullable TEXT). Caches the anchor's embedding so subsequent runs don't re-embed every existing narrative.
- `analysis/src/engine/narrative_clusterer.py` — refactored. Same storage, two comparators:
  - **jaccard**: lexical token Jaccard against anchor token set (unchanged behavior).
  - **embedding**: cosine similarity against anchor embedding. Per-claim fallback: if `embedding_client.embed()` returns None for a given claim, that claim falls back to Jaccard for that one comparison.
  - Anchor embeddings are computed lazily and persisted via UPDATE on first use.
- `analysis/src/scheduler/job_runner.py` — `_build_narrative_clusterer()` reads the mode from settings, lazily inits an Ollama client only when `mode='embedding'`, and degrades gracefully if the client init fails.

### Tests

- `analysis/tests/test_propagation.py` — 6 new tests:
  - `TestCosine` (4): orthogonal, identical, empty, mismatched length.
  - `TestNarrativeClustererEmbeddingMode` (2):
    - With a fake embedding client that returns vectors for synonymous claims → "Trump won Pennsylvania" and "Trump victory in Keystone State" merge into one narrative; orthogonal Senate claim is its own. Verifies persisted `anchor_embedding_json`.
    - With a fake client that returns None → exercises the per-claim fallback path; clusters as Jaccard would (synonymous claims split because their tokens don't overlap).
- All 23 propagation tests pass; full suite 35/35.

## How to enable embedding mode

Pull the embedding model on the Ollama box once:

```
ollama pull nomic-embed-text
```

Then add to `.env`:

```
CIVIC_NARRATIVE_SIMILARITY_MODE=embedding
```

The next `analyze` run will start embedding new claims; existing narratives' anchor embeddings get computed lazily on first compare. To roll back, set the var back to `jaccard`.

Default remains `jaccard` so this change is non-breaking — embedding mode is strictly opt-in.

## What this enables that wasn't possible before

- **Narratives have a UI.** The Narratives tab is the first surface to show propagation data — claim text, where it started, how many docs are repeating it, cross-source mix, daily spread sparkline, sentiment, inbound citations — all in one row per narrative.
- **Synonym-tolerant clustering.** With embedding mode on a real model, "Trump won Pennsylvania" and "Trump victory in Keystone State" merge instead of splitting. Lexical near-duplicates still work the same.
- **Stable cross-mode behavior.** Switching modes does not silently rewrite history — narratives created under jaccard keep their (NULL) embeddings and continue to cluster lexically. Embedding mode only takes effect for new narratives and for any anchor it can backfill.

## What's still open

- `ai_output_evals` writer (golden-set / human-override workflow) — schema-only, requires a separate review-UI design.
- Per-narrative detail page (list of supporting docs with excerpts) — the list view ships now; deep-dive can follow.
- Embedding cache for individual claims — currently we re-embed every pending claim each run. Negligible at MVP volumes; revisit if claim throughput grows.

## Deploy note

Run `.\run.ps1 migrate` to apply migration 008 before the next pipeline run. To use embedding mode also: `ollama pull nomic-embed-text` and set `CIVIC_NARRATIVE_SIMILARITY_MODE=embedding` in `.env`.
