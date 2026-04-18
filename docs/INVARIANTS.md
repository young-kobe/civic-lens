# Civic Lens - System Invariants & Correctness Checklist

This document defines the non-negotiable invariants for the system. All code changes must preserve these properties.

## Part A: C++ Ingestion & Storage

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
- [ ] **Politeness**: Request rate per domain strictly never exceeds the configured token bucket limit.
- [ ] **Completeness**: Every fetch attempt results in a recorded `fetch_event` (success or failure). Data is never silently dropped.

### A5. Content Capture
- [ ] **Integrity**: `Hash(StoredBytes) == FilenameHash`.
- [ ] **Immutability**: Raw content files are content-addressed and never modified after write. Metadata tables point to these hashes.

### A6/A7. Parsing & Extraction
- [ ] **Resilience**: Parser failures do not crash the application; they are logged as errors.
- [ ] **Traceability**: Every parsed record (`articles_raw`, `reddit_posts_raw`) contains the `raw_hash` of the source blob.

## Part B: Analysis API (Python)

### B1. ETL / Dataset Builder
- [ ] **Traceability**: Every row in the `docs` table links to a `raw_hash`.
- [ ] **Versioning**: ETL jobs log the version of the code/logic used to produce the output.

### B2. AI Analysis
- [ ] **Evidence**: AI classifications (topic, stance, propaganda) must cite specific spans/quotes from the text as evidence.
- [ ] **Uncertainty**: All AI outputs include a confidence score.
- [ ] **No Hallucination**: The API must not invent data. If a field is missing, it returns null/error, not a guess.

### B3. Propaganda Detection
- [ ] **Operational Definition**: "Propaganda" is defined as the presence of measurable techniques (loaded language, ad hominem), not a subjective label.
- [ ] **Auditability**: Flagged techniques must point to the specific text span that triggered the flag.

## Part C: Frontend / Presentation

### C1. Data Fidelity
- [ ] **Honesty**: The UI must display confidence scores alongside AI predictions.
- [ ] **Proxy Labeling**: "Reach" and "Influence" metrics must be explicitly labeled as proxies (e.g., "Reddit Score") unless verified traffic data exists.

### C2. User Experience
- [ ] **Responsiveness**: The UI should never freeze during data loading; use skeletons or spinners.
- [ ] **Aesthetics**: Design must use high-quality typography and spacing (e.g., Inter, 8px grid). No default browser styles.
