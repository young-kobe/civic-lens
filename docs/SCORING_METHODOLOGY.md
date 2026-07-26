# Civic Lens Scoring Methodology

> **Version**: 3.0 (Postgres redesign)
> **Last Updated**: 2026-07-26

---

## Overview

Every engine writes through `analysis/src/results/store.py`, the only writer of run-anchored `analysis.*` result tables. Each analysis attempt is one `analysis.runs` row: `task`, `model_id`, `prompt_version_id` (nullable for deterministic runs), `inference_method` (`llm`/`deterministic`/`hybrid`), `confidence`, `created_at`, `is_current`. All scores include confidence values and are traceable through this chain — see "Traceability" below.

---

## Bot Detection Scoring

Identifies potential automated accounts or coordinated inauthentic behavior. `engine/bot_detection.py`.

### Labels

`label` (`analysis.bot_label`: `human`/`suspicious`/`bot`/`unknown`) comes directly from the LLM's own judgment, not from a blended numeric score — the hand-tuned additive score formula was retired (`0005_drop_bot_score.sql`). `unknown` is reserved for the one deterministic case: empty doc text, no signal battery possible.

### Signals

A deterministic stylometric/account battery always runs first and feeds the LLM prompt as context; the LLM's own classification (`label`, `confidence`, `llm_text_likelihood`, `indicators`, `reasoning`) is what gets persisted. A successful call is `inference_method = 'hybrid'` — both the battery and the model's judgment contributed.

- **Text / stylometric**: sentence-length variance (`burstiness`), unique-word ratio (`type_token_ratio`), LLM hedge-phrase rate (`template_score`), typographic-purity score, spam-keyword hits, URL/hashtag counts.
- **Account / behavioral** (X posts only): new account (< `X_NEW_ACCOUNT_AGE_DAYS` days), low follower count (< `X_LOW_FOLLOWERS_THRESHOLD`), follow-ratio anomaly. Reddit posts carry no author snapshot yet and fall through to the text-only signal subset.

There is no government-verified-account de-bias override and no non-US-geotag signal — both were retired with the additive score formula; `verified_type` still reaches the prompt but nothing derived from it overrides the model's label.

### Output Schema

```json
{
  "label": "bot",
  "confidence": 0.85,
  "llm_text_likelihood": 0.7,
  "indicators": ["New account (3 days)", "High posting rate (120/day)"],
  "reasoning": "Account shows automated posting patterns",
  "inference_method": "hybrid"
}
```

> [!IMPORTANT]
> Classification is a probabilistic lead, not a verdict, and may include false positives. Bot/suspicious-flagged content is excluded from public aggregations above a per-author flagged-post share (`BOT_FLAGGED_SHARE_EXCLUSION = 0.5` of confidence-floored analyzed posts, `api/queries/constants.py`) but retained for audit.

---

## Sentiment Analysis Scoring

Classifies content emotional tone. `engine/text.py` — sentiment-only as of 2026-07-25; the favorability half (GOP stance) was retired. `analysis.sentiment_label`: `positive`/`negative`/`neutral`/`mixed`.

One LLM call per doc. A trivial-content doc (mentions/links/hashtags only) short-circuits to a `done` deterministic run with no `sentiment_results` row — unanalyzable is not neutral. A failed or unavailable LLM call is recorded as a `failed` run; there is no heuristic fallback.

### Output Schema

```json
{
  "label": "negative",
  "confidence": 0.78,
  "sarcasm_detected": false,
  "evidence_spans": ["criticized the administration's handling of..."]
}
```

> [!NOTE]
> Sentiment represents content tone, not author intent. Results are labeled "sampled political discourse" in the UI; net tone renders in points ("pts") on a -100 to +100 scale, not as a percentage.

---

## Per-Entity Target Stance

Party-neutral, topic-tagged stance toward named entities. `engine/targets.py` writes `analysis.target_mentions` (`raw_target` always kept for audit; `entity_id` nullable — unresolved targets persist rather than being dropped). This is the sole source of directional stance evidence: `favorability_stances` (the old GOP-only text-task output) has no writer as of 2026-07-25 and is superseded here, symmetric across parties.

Party stance for a document, author, or narrative is read by joining `target_mentions.entity_id -> corpus.entities.lean` at request time (`api/queries/`) or by `engine/lean_derivation.py` for the author/narrative-level rollups below — never by feeding lean into an LLM prompt.

---

## Political Lean Derivation

Deterministic, `engine/lean_derivation.py`. Pools directional (`positive`/`negative` stance toward a `democrat`/`republican`-leaning entity) `target_mentions` evidence per author and per narrative, full rebuild every invocation, into `analysis.author_leans`/`narrative_leans`.

- **Minimum sample**: `LEAN_MIN_SAMPLE_COUNT = 5` directional stance samples required before a lean is computed at all.
- **Share threshold**: `LEAN_SHARE_THRESHOLD = 0.7` — the majority lean must hold at least this share of directional samples to be assigned; below it, the subject is `mixed`.
- **Confidence saturation**: `LEAN_CONFIDENCE_SATURATION_SAMPLES = 20` — confidence scales with sample count up to this ceiling, beyond which more samples add no further confidence.

Curated lean (`corpus.entities.lean`) is never fed into an LLM prompt (bias/priming risk); derived lean is always accompanied by its `lean_share`/`confidence`/`stance_sample_count` so a reader can see the sample size behind the label.

---

## Propaganda Technique Detection

LLM-driven classifier flagging one or more of six rhetorical techniques. `engine/propaganda.py`. A flag measures rhetorical *style* — not truth, intent, or whether a post is "propaganda" in the everyday sense.

### Techniques

`loaded_language`, `name_calling`, `ad_hominem`, `appeal_to_fear`, `whataboutism`, `doubt_casting` (`analysis.propaganda_technique`).

### Method

1. Trivial-content docs (mentions/links/hashtags only) are declined — a `done` deterministic run with no `propaganda_results` row, not a call spent on a guaranteed-empty result.
2. Every other doc with substantive content gets one LLM call. The LLM returns candidate techniques, each with a verbatim `evidence_span`.
3. **Validation** (`engine/validation.py`): a technique whose evidence span is under `MIN_EVIDENCE_WORDS = 4` words, or is not a case-insensitive substring of the source text, is dropped. If the LLM returned techniques but none validate, `density` is capped at `UNVERIFIED_EVIDENCE_CONFIDENCE_CAP = 0.3`. If the LLM flagged zero techniques, `density` is forced to 0.0.

There is intentionally no deterministic fallback — a technique claim without a verifiable quote is not surfaced. The run's `confidence` is `density` itself (the model's self-reported overall score), not a mean over per-technique confidences.

### Output Schema

```json
{
  "techniques": [
    {"technique": "loaded_language", "confidence": 0.85, "evidence_span": "radical extremist agenda"}
  ],
  "density": 0.62,
  "summary": "..."
}
```

> [!NOTE]
> `density` runs 0 (none) to 1 (saturated). It is not a truth or intent score.

---

## Claim Extraction

`engine/claims.py`. Trivial-content docs short-circuit to zero claims (deterministic, no LLM call). Otherwise one LLM call extracts candidate claims; each is validated:

- Length: `MIN_CLAIM_WORDS = 4` to `MAX_CLAIM_WORDS = 20` words.
- Evidence: the claim's `evidence_span` must be a verbatim substring of the source text (same `MIN_EVIDENCE_WORDS = 4`-word floor as propaganda).

Unlike sentiment/propaganda evidence, a claim that fails the evidence check is **dropped entirely**, not confidence-capped — `analysis.claims` anchors narratives via `narratives.anchor_claim_id`, and a model that hallucinates a claim must not silently populate the narrative layer. Run-level `confidence` is the mean of every surviving claim's own confidence, or `ZERO_CLAIMS_CONFIDENCE` when none survived.

---

## Narrative Clustering

`engine/narrative_clustering.py`. Embedding-only (the lexical Jaccard comparator was retired 2026-07-26) — groups current `analysis.claims` into `analysis.narratives` by cosine similarity of embeddings.

- **Match threshold**: `CIVIC_NARRATIVE_EMBEDDING_THRESHOLD` (default 0.65) — tuned for `nomic-embed-text` and not portable across embedding models without re-checking against a real claim sample.
- **Minimum support**: `MIN_NARRATIVE_SUPPORT = 2` distinct documents must support a provisional group before it becomes a persisted narrative; below this, matched claims stay in memory for the run and are reconsidered next run.
- **Lookback**: `CLAIM_LOOKBACK_DAYS = 30` for pending (unclustered) claims.

`CIVIC_NARRATIVE_EMBEDDING_MODEL` is required with no default — a blank value would leave `clustering_runs` unable to say which model produced its vectors, so the stage refuses to start. A claim whose `embed()` call fails is left unclustered (never measured some other way) and counted in `clustering_runs.embedding_failures`; if every embed call in a run fails, the run raises rather than writing an empty result that reads as "no narratives found".

Narrative "first seen" is the earliest doc **we ingested** carrying a linked claim, not the claim's origin in the world.

---

## Confidence Scoring

All analyses include confidence scores indicating certainty.

| Confidence | Level | Interpretation |
|------------|-------|----------------|
| 0.0 - 0.3 | Low | Uncertain, may need human review |
| 0.3 - 0.6 | Medium | Reasonable certainty |
| 0.6 - 0.8 | High | Strong certainty |
| 0.8 - 1.0 | Very High | Near-certain classification |

`aggregation_min_confidence` (`CIVIC_` setting, default 0.5) is the floor below which a run is dropped from sentiment/narrative aggregations at request time.

---

## Time-Based Filtering

API panels support filtering by time window against `corpus.documents.published_at`:

| Window | Duration |
|--------|----------|
| 24h | 24 hours |
| 7d | 7 days |
| 30d | 30 days (default) |
| 90d | 90 days |

`api/queries/constants.py::WINDOWS` is the single source of truth for these four keys; there is no "all-time" fifth window — an all-time view reads `corpus.*`/`analysis.*` with no cutoff at all.

---

## Traceability

All scores are traceable to:

1. **Source document or author**: `analysis.runs.doc_id` XOR `author_id` -> `corpus.documents`/`corpus.authors`.
2. **Model used**: `analysis.runs.model_id`.
3. **Prompt version**: `analysis.runs.prompt_version_id` -> `analysis.prompt_versions` (nullable for deterministic runs).
4. **Timestamp**: `analysis.runs.created_at`.
5. **Raw evidence**: `analysis.runs.raw_response` (verbatim LLM payload) and the typed result table's `evidence_spans`/`evidence_span` columns.

This ensures reproducibility and audit capability per project invariants (`docs/INVARIANTS.md`).
