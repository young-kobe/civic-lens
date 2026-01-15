---
description: Core python code instruction set
---

# Agent Instructions: Python + Streamlit AI Reporting (Traceable, Reproducible Analysis)

## Objective
Build an analysis and reporting pipeline that:
- Loads raw news + Reddit data produced by the C++ ingestor
- Extracts clean text in Python
- Uses AI to compute topic/stance/framing and “propaganda-technique” signals with explicit evidence spans
- Aggregates results into an interactive Streamlit report
- Maintains auditability: every output is traceable to raw sources and model/prompt versions

The system must clearly label reach and sentiment as **proxies** and must avoid claiming to represent all Americans. Reddit outputs must be labeled as “sampled Reddit discourse.”

---

## High-Level Architecture
1. **DB Loader**
   - Load from SQLite (or Postgres if upgraded)
2. **ETL: Raw -> Docs**
   - Convert raw HTML/JSON pointers to clean text
   - Build a canonical `docs` dataset
3. **Baseline Features**
   - readability, length, duplication, similarity
4. **Embeddings + Clustering**
   - Group articles into “story clusters”
5. **LLM Analysis**
   - topic classification
   - stance/framing labeling (bounded label set)
   - propaganda-technique detection (bounded technique set)
   - evidence spans + confidence required
6. **Aggregations**
   - outlet profiles, topic trends, cluster summaries
   - Reddit sentiment + themes by cluster/topic/subreddit
7. **Streamlit App**
   - overview + drilldown + audit panel
8. **Versioning**
   - store model name/version, prompt template version, code version, timestamps

---

## Minimum Data Model (Python)
### docs (core)
Fields:
- doc_id (stable id)
- source_type: `news` | `reddit_post` | `reddit_comment`
- url_canon or fullname
- domain or subreddit
- published_at, fetched_at
- title
- text (clean)
- raw_hash
- metadata_json (anything extra)
- etl_version

### ai_outputs (traceable)
- doc_id
- model_id
- prompt_version
- task_type (topic|stance|techniques|summary|sentiment)
- output_json
- confidence
- evidence_spans (list of {start,end,quote})
- created_at

### clusters
- cluster_id
- doc_id
- embedding_model
- clustering_version

---

## ETL Requirements (v1)
### News
- Load raw HTML by `raw_hash` pointer
- Extract main text using a robust library or readability approach
- Store:
  - canonical URL
  - title/published time if available (fallback to earlier metadata)
- Persist `docs` rows

### Reddit
- Prefer using ingested JSON payloads:
  - post title/selftext
  - comment body
- Persist `docs` rows

**Invariant**
- `docs.raw_hash` must always exist and correspond to raw bytes.
- ETL is deterministic under fixed library versions.

---

## AI Tasks (v1) and Guardrails
### Topic
- Use a bounded topic taxonomy (e.g., 15–25 topics).
- Output: {topic, secondary_topics, confidence}

### Stance/Framing (only when topic applicable)
- Output must include:
  - label set (e.g., pro/anti/neutral/unclear)
  - confidence
  - evidence spans (quotes)
- If unclear: label `unclear` and provide rationale.

### Propaganda-Technique Signals
Operationalize as technique flags, not a moral judgment:
- loaded language
- scapegoating/dehumanization cues
- cherry-picking / misleading statistics markers
- ad hominem / name-calling
- conspiracy framing
- urgency/call-to-action framing

Output:
- techniques: [{name, confidence, evidence_spans}]
- composite manipulation_risk_score (0–100) with explanation
**Every technique must have evidence spans.**

### “Reach” Proxies
Unless external traffic data is available, compute proxies:
- Reddit score/comment count references
- cross-posting frequency
- syndication/near-duplicate footprint
Label these explicitly as proxies.

### “How Americans feel”
Must be labeled as:
- “Reddit sample sentiment/themes”
Include:
- subreddit breakdown
- time window
- sample sizes
Avoid universal language.

---

## Embeddings + Clustering (v1)
- Compute embeddings for news docs (and optionally Reddit posts).
- Cluster into story clusters (same event coverage).
- Provide cluster summaries and compare outlet framing.

**Invariant**
- Store embedding model + clustering version.
- Clustering must be reproducible under fixed parameters.

---

## Streamlit Report (v1)
### Pages
1. **Overview**
   - ingestion counts
   - top domains/subreddits
   - topic distribution
2. **Story Clusters**
   - cluster list by date range
   - stance distribution across outlets
   - manipulation-technique prevalence
   - reach proxy metrics
3. **Outlet Profiles**
   - stance by topic over time
   - language intensity / technique rates
4. **Reddit Sentiment**
   - sentiment/themes per cluster/topic
   - subreddit segmentation + representative quotes
5. **Audit / Trace**
   - select any item -> show:
     - raw URL/hash
     - extracted text
     - AI outputs with evidence spans
     - model_id + prompt_version + timestamp

---

## Reproducibility Requirements
- All AI runs must record:
  - model_id
  - prompt_version
  - temperature/params
  - timestamp
- Store prompt templates in repo with semantic versioning.
- Provide a CLI:
  - `python -m pipeline.etl`
  - `python -m pipeline.analyze --tasks topic stance techniques`
  - `streamlit run app.py`

---

## Deliverables
1. ETL module producing `docs`
2. Analysis module producing `ai_outputs` and `clusters`
3. Streamlit app with drilldown audit trail
4. Basic tests:
   - ETL produces non-empty text for sample pages
   - AI output JSON schema validation
5. README with run instructions and configuration

---

## Acceptance Criteria (v1)
- Can run end-to-end on a small seed set (e.g., 10 RSS feeds + 5 subreddits).
- Report shows clusters, outlet comparisons, and Reddit sample sentiment.
- Every chart item is traceable to raw sources and AI evidence spans.
- Reach and sentiment are clearly labeled as proxies/samples.
