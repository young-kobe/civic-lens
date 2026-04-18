# 032 — Narrative Propagation Pipeline

## Context

Walkthrough 030 landed the audit + narrative schema (`prompt_versions`, `ai_output_evals`, `narratives`, `narrative_docs`, `narrative_citations`) as empty tables. This walkthrough wires three of the five: citation edges and narrative clustering now populate during the normal analysis pipeline. `ai_output_evals` remains schema-only — that's the golden-set/review workflow, a separate design.

The pipeline now has two propagation surfaces that together answer "where did this claim come from and where did it spread":

1. **Citation edges** (deterministic, no LLM): URLs in doc text → matched against owned docs; X referenced tweets → resolved via `x_posts_raw`. Writes to `narrative_citations`.
2. **Claim-based narrative clustering** (LLM-driven, then lexical clustering): each doc gets 0–3 canonical claim statements via a new LLM task; claims are grouped by Jaccard-over-tokens into `narratives` rows; each supporting doc gets a `narrative_docs` edge.

## Changes

### Analysis engine

- `analysis/src/engine/citation_extractor.py` — new deterministic extractor. Canonicalizes URLs (lowercases scheme/host, strips `www.`, trailing slashes, fragments, and trailing punctuation), resolves `x_posts_raw.referenced_tweet_id` to `docs.ident` for quote/reply/retweet edges. Per-doc idempotency via an `ai_outputs` row with `task_type='citations'`.
- `analysis/src/engine/claim_extractor.py` — new LLM extractor. Returns at most 3 claims per doc, each with a `claim` (4–20 words, paraphrased), `confidence` (0–1), and `evidence_span` that is ≥3 verbatim words from the source. Fabricated or off-source claims are dropped at validation time.
- `analysis/src/engine/narrative_clusterer.py` — new lexical clusterer. Reads claims from `ai_outputs` (task_type='claims', last 30 days), tokenizes with a small stop-word list, computes Jaccard against each existing narrative's anchor-claim tokens. Match threshold 0.3; above → assign to existing narrative, below → mint a new one using the first claim as anchor. Anchor-based matching (rather than drifting centroids) keeps narrative identity stable across runs.
- `analysis/src/engine/prompts.py` — adds `CLAIM_EXTRACTION_SYSTEM_PROMPT`, `CLAIM_EXTRACTION_USER_PROMPT_TEMPLATE`, and `CLAIM_EXTRACTION_PROMPT_VERSION = "claim-extraction-v1"`.
- `analysis/src/llm/schemas.py` — adds `CLAIM_EXTRACTION_SCHEMA` with a required `claims` array and a `CLAIM_SCHEMA` object (claim / confidence / evidence_span, all required).

### ETL / orchestration

- `analysis/src/etl/loader.py:get_unprocessed_docs` — now also returns `ident`, which the citation extractor uses to resolve X references.
- `analysis/src/scheduler/job_runner.py` — adds `run_citation_extraction`, `run_claim_extraction`, `run_narrative_clustering` steps between text analysis and snapshot save. Pipeline is now seven steps: ETL → bot → text → citations → claims → narratives → snapshots. New CLI `--tasks` values: `citations`, `claims`, `narratives`.

### Tests

- `analysis/tests/test_propagation.py` — 17 tests: URL canonicalization, Jaccard, claim validator (including fabricated-evidence rejection), citation extractor (writes reply + url_citation edges, idempotent marker), narrative clusterer (near-duplicate claims collapse to one narrative, rerun doesn't duplicate assignments).

## Known limitations

- **Synonym-blind.** Jaccard on token sets clusters near-duplicates (same entity + same event) but misses synonymy ("Trump won PA" vs "Trump victory in Pennsylvania" share only 2 tokens). A future pass can swap the anchor comparator for embeddings without changing the storage schema.
- **First claim becomes narrative name.** Simple but not always the best label; the anchor-claim text is stored in `narratives.description` so a future renaming pass (LLM-summarization over all member claims) can update names without reshuffling members.
- **Claim quality depends on model.** Qwen 0.5B won't produce useful claims — the extractor falls back to an empty list on any LLM failure. Run with `CIVIC_LLM_BACKEND=gemini` for the narrative pipeline to be meaningful.
- **ai_output_evals has no writer yet.** Golden-set import / human-override workflow is deferred — it's the counterpart to the layer-1 accuracy harness, which is a separate workstream.

## Verification

- 29 Python tests pass (17 new in `test_propagation.py` plus the pre-existing suites).
- Smoke import: all new modules load cleanly.

## Deploy note

Migrations 005–007 from walkthrough 030 are prerequisites — the user's `analyze` run failed with `no such table: prompt_versions` before this walkthrough because migrations hadn't been applied. Run `.\run.ps1 migrate` once before the first pipeline run that uses this code.
