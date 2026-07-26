# Narrative clustering is embedding-only

**Date:** 2026-07-26
**Layer:** analysis
**Migration:** `data/pg-migrations/0006_clustering_embedding_failures.sql`

## What it is now

`engine/narrative_clustering.py` groups claims by embedding cosine and by
nothing else. There is one comparator, one threshold, and one way a
narrative can come into being.

A claim whose `embed()` call returns no vector is left unclustered. An
existing narrative whose anchor has no vector is skipped as a match
target. Neither is compared by some other measure, and neither is
fabricated.

Three conditions raise `RuntimeError` instead of producing output:

- `CIVIC_NARRATIVE_EMBEDDING_MODEL` is blank. See Config below.
- The configured backend does not implement `embed()` at all. Raised
  before `_open_clustering_run()`, so no run row exists for an attempt
  that could never have worked.
- Every embed call in a run failed. That is a dead backend or a model
  name the backend does not serve, not a corpus with nothing to say —
  and "no narratives found" is a claim about the corpus that this run
  has not earned. The run row is stamped with its failure count and left
  with `completed_at` NULL.

Partial failures do not sink the run. They land in
`analysis.clustering_runs.embedding_failures` and log at ERROR.

## What it replaced

A lexical Jaccard comparator ran alongside the embedding one, selected by
`CIVIC_NARRATIVE_SIMILARITY_MODE`, and — the actual problem — served as a
per-claim runtime fallback. When an embed call failed, that claim was
silently re-measured by token overlap and clustered anyway.

That broke provenance. `clustering_runs.mode` is stamped once per run, so
a run recorded as `embedding` could contain rows produced lexically, with
nothing in the database distinguishing them. The fallback count existed
only in a log line.

It also conflated two different measurements. Jaccard clusters shared
vocabulary; embeddings cluster meaning. Claims making the same point in
different words do not group under the first and do under the second.
Both wrote to `analysis.narratives`, feeding one UI under one caption.

Deleted: `tokenize_claim()`, `jaccard()`, `_STOPWORDS`, `_TOKEN_RE`,
`PendingClaim.tokens`, `ExistingAnchor.tokens`, the `mode` and
`jaccard_threshold` parameters on `plan_clustering()` and `run()`, and
the `narrative_similarity_mode` / `narrative_jaccard_threshold` settings.

## Schema

`0006` adds `analysis.clustering_runs.embedding_failures INTEGER NOT NULL
DEFAULT 0`.

`clustering_runs.mode` stays in the DDL so historical `'jaccard'` rows
remain readable. Every new run writes `'embedding'`.

Existing rows default to 0. Accurate for historical `'jaccard'` runs,
which attempted no embeddings; an understatement for older `'embedding'`
runs, whose fallbacks were only ever logged. Read `mode` alongside the
count before trusting either.

## Config

`CIVIC_NARRATIVE_SIMILARITY_MODE` and `CIVIC_NARRATIVE_JACCARD_THRESHOLD`
are gone. Remove them from any deployed env file.

`CIVIC_NARRATIVE_EMBEDDING_MODEL` is now **required**. `run()` raises if
it is blank, before resolving a backend or opening a run row.

It previously defaulted to `nomic-embed-text` — an Ollama tag that reached
Gemini as a 404 on every call, which is how the silent fallback came to be
exercised in production. A blank default was the first replacement, letting
each backend pick its own; that removed the mismatch but left
`clustering_runs.embedding_model` recording a blank, unable to say which
model produced a run's vectors. Requiring the value fixes both without any
resolution plumbing: no default can mismatch a backend, and no run can be
recorded unaccountably.

The trade-off is that a fresh clone cannot run the narratives stage until
the variable is set. It gets an error naming the variable, and tests inject
`embed_fn` and so bypass the check.

**The threshold does not travel between embedding models.**
`narrative_embedding_threshold` is 0.65, tuned for `nomic-embed-text`.
Different models produce different cosine distributions. Recheck it
against a real claim sample after any model change.

## Retired stack

`scheduler/job_runner.py` read both deleted settings. Its two call sites
now inline the old defaults (`"embedding"`, `0.3`) so that stack runs
unchanged until Phase 11 deletes it. Do not reintroduce the settings to
serve it. `engine/narrative_clusterer.py` — the retired clusterer, which
still contains its own Jaccard path — was not touched.

## Tests

`analysis/tests/test_engine_narratives.py` is rewritten. Tier-1 fixtures
carry explicit vectors rather than derived tokens, so each test states
the similarity it asserts. New coverage: an unembedded claim is left
unclustered, an unembedded anchor is skipped, total embed failure raises
and writes no narratives, partial failure is persisted to
`embedding_failures`.

`analysis/tests/pg_fixture.py` now globs `data/pg-migrations/[0-9]*.sql`
in numeric order instead of naming each file. The old list had to be
edited for every migration and would have silently omitted `0006`.

Note that `_load_pending_claims()`'s dead `tokenize_claim` call survived
the ungated suite and was caught only by running the gated tier against a
real Postgres. Run gated before believing this module is green.
