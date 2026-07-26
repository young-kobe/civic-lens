# Civic Lens - System Invariants & Correctness Checklist

Civic Lens measures **sampled political discourse** across news, Reddit, and X, with a **narrative overlay** that clusters recurring claims and a **partial citation overlay** between owned sources. This document defines the non-negotiable invariants for the system. All code changes must preserve these properties.

See `docs/audit-trail/` (analysis and infra buckets) for the rationale behind the current goal framing and its evolution.

## Part A: Ingestion & Storage (Go)

### A1. Seeds
- [ ] **Reproducibility**: The seed list is versioned. The same seed list must always produce the same initial crawl frontier.

### A2. URL Canonicalization
- [ ] **Determinism**: `Canonicalize(URL)` must be a pure function. Same input URL -> Same output string.
- [ ] **Ubiquity**: The canonical URL is used as the Primary Key in the DB and determining deduplication.

### A3. Frontier State Machine
- [ ] **Uniqueness**: Exactly one row per canonical URL in the `pages` table.
- [ ] **Exclusivity**: A URL cannot be both `QUEUED` and `INFLIGHT`.
- [ ] **Crash Safety**: On system restart, any `INFLIGHT` URLs must be reset to `QUEUED` (fetch never completed).

### A4. Fetcher
- [ ] **Politeness**: Request rate per domain strictly never exceeds the configured token bucket limit. Redirect targets take a token against the target domain too, so a chain of source domains cannot multiply one host's request rate.
- [ ] **Failure accounting**: Every fetch outcome updates the page's frontier row: a success transitions it to `DONE`; a failure records `pages.last_error` and either re-queues it with an incremented `retries` and backoff or marks it `FAILED`. There is no per-attempt `fetch_event` ledger — only the latest error and the retry count survive per page. This is the audit surface the system actually maintains; API-fetch history (robots.txt, Reddit/X calls) is captured only via the content-addressed raw blobs those calls persist, not as fetch events.

### A5. Content Capture
- [ ] **Integrity**: `Hash(StoredBytes) == FilenameHash`.
- [ ] **Immutability**: Raw content files are content-addressed and never modified after write. Metadata tables point to these hashes.

### A6/A7. Parsing & Extraction
- [ ] **Resilience**: Parser failures do not crash the application; they are logged as errors.
- [ ] **Traceability**: Every parsed record (`articles_raw`, `reddit_posts_raw`) contains the `raw_hash` of the source blob.

## Part B: Analysis API (Python)

### B1. ETL / Dataset Builder
- [x] **Traceability**: Every row in `corpus.documents` links to a `raw_hash`.
- [x] **Versioning**: ETL jobs stamp the logic version onto every `corpus.documents` row via the `etl_version` column (`etl/constants.py::ETL_VERSION`, stamped by `etl/documents.py`) and log it per run, so docs produced by different filter/extraction logic are distinguishable (audit A-9). Bump `ETL_VERSION` when the admission rules, recency window, or extraction changes.

### B2. AI Analysis
- [ ] **Evidence**: AI classifications (topic, stance, propaganda) must cite specific spans/quotes from the text as evidence.
- [ ] **Uncertainty**: All AI outputs include a confidence score.
- [ ] **No Hallucination**: The API must not invent data. If a field is missing, it returns null/error, not a guess.

### B3. Propaganda Detection
- [ ] **Operational Definition**: "Propaganda" is the presence of measurable rhetorical techniques (loaded language, name-calling, ad hominem, appeal to fear, whataboutism, doubt-casting), not a subjective label. A flag measures rhetorical style — not truth, intent, or whether a post is "propaganda" in the everyday sense.
- [ ] **Auditability**: Every flagged technique must point to a verbatim text span that triggered the flag. A technique whose evidence span is under four words, or is not a substring of the source text, is dropped.

> **Status:** Implemented and live. `analysis/src/engine/propaganda.py` runs as a first-class pipeline stage, writing `analysis.propaganda_results`/`propaganda_techniques` rows via `results/store.py`, surfaced on the Propaganda tab via `api/queries/propaganda.py`. It is LLM-only (no deterministic fallback); when the LLM returns techniques but none validate against the source text, `density` is capped at 0.3.

## Part C: Frontend / Presentation

### C1. Data Fidelity
- [ ] **Honesty**: The UI must display confidence scores alongside AI predictions.
- [ ] **Proxy Labeling**: "Reach" and "Influence" metrics must be explicitly labeled as proxies (e.g., "Reddit Score") unless verified traffic data exists.
- [ ] **Source attribution on evidence**: Any UI surface that shows an individual doc as evidence — flagged example, classification sample, supporting doc, narrative citation — MUST link back to the original source (news article URL, X tweet permalink, or Reddit post link). The aggregator is responsible for synthesizing the URL when it isn't stored literally. A doc row without a link is a bug, not a layout choice: without the link the reader can't audit the claim and the system is no longer traceable end-to-end.

### C2. User Experience
- [ ] **Responsiveness**: The UI should never freeze during data loading; use skeletons or spinners.
- [ ] **Aesthetics**: Design must use high-quality typography and spacing (e.g., Inter, 8px grid). No default browser styles.
