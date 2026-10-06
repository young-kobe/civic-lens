# Architecture

## Implementation status

Implemented: project boundaries, pinned SDK and package locks, CI, diagrams, and the first watched-page tracer. Application validates people and shared watched-source membership and creates a bounded request. Infrastructure invokes the independent collector and verifies its receipt and capture bytes. Collector performs scoped HTTP collection and writes immutable content-addressed gzip artifacts. No durable jobs, imports, database, feeds/discovery, AI, review, publication, or MCP are implemented yet. The remaining workflow descriptions below are target design.

The tracer crosses only the configuration, application, process adapter, and collection layers. It deliberately ends at a verified receipt, before operational evidence persistence. A capture is not a document version, approval, or publication. Configuration currently supports people and watched-source membership; dated relationships and other registry concepts remain planned.

## Product and deployment

One repository, a local C# application, a separate C# collector executable, and a static public publication. Three views share evidence: officials' statements/actions, tracked-document changes, and weekly media briefs. Registry size is not hardcoded. New officials using existing source types require configuration only; new source formats may need connectors.

## Chosen stack

| Responsibility | Choice |
|---|---|
| Application and collector | .NET 10 LTS / C#; SDK pinned in global.json |
| Local review | ASP.NET Core Razor Pages, added when implemented |
| Operational database | Local Postgres; EF Core/Npgsql; application-owned durable jobs |
| Captures | Compressed content-addressed files; immutable source and extraction versions |
| Classification | Task-specific Clef/Clef-flash adapters after evaluation |
| Extraction and drafting | Separate structured-generation adapter; model selected by evaluation |
| Public website | C# release builder with Scriban HTML templates and small JS modules |
| Local MCP | Official C# SDK; separate local development/review registration |
| Public MCP | Thin TypeScript Cloudflare Worker over published artifacts/index |
| Hosting | Cloudflare Pages and Worker; public operation independent of local machine |
| Tests/CI | xUnit, architecture/contract/integration tests, GitHub Actions |

Only the SDK and test dependencies are installed. Introduce the other packages when their phase begins and pin them. No Rust, Go, Python analysis service, or React application is planned. Python's standard library is used only for the development diagram generator.

## Dependency rules

Core contains domain invariants; Application contains use cases and interfaces; Infrastructure implements external adapters; Host wires the application. Collection.Contracts and Publication.Contracts remain dependency-free. The collector references Collection.Contracts only. Public MCP consumes the publication contract, never local application internals.

<!-- diagrams:start -->
### Collection to publication (target)

Target workflow. Configuration, bounded watched-page collection, and verified file captures are implemented; durable evidence storage and downstream stages remain planned.

```mermaid
flowchart LR
    config["Coverage configuration"]
    app["C# application"]
    collector["C# collector"]
    evidence["Evidence store"]
    analysis["Selective analysis"]
    review["Local review and MCP"]
    release["Versioned release"]
    public["Website and public MCP"]
    config --> app
    app --> collector
    collector --> evidence
    evidence --> analysis
    analysis --> review
    review --> release
    release --> public
```

### Project dependencies (implemented)

Arrows mean references. These project boundaries are checked by xUnit tests.

```mermaid
flowchart LR
    host["Host"]
    infra["Infrastructure"]
    app["Application"]
    core["Core"]
    cc["Collection.Contracts"]
    pc["Publication.Contracts"]
    collector["Collector"]
    host --> app
    host --> infra
    infra --> app
    app --> core
    app --> cc
    app --> pc
    collector --> cc
```

<!-- diagrams:end -->

The HTML explorer is generated from the same diagram definitions: `python3 tools/render-architecture.py`, then open `artifacts/architecture.html`. Planned components are explicitly labeled there; it is not a claim that the pipeline exists.

## Configuration and identity

Version-controlled configuration is authoritative for people, organizations/offices, dated terms/affiliations, dated source relationships, issues, coverage membership, and collection/analysis policies. The database imports validated configuration revisions; it is not a second editable registry. Runtime cursors and job state live in the database.

Stable person IDs outlive office/account changes. An institutional account is not a person's alias. Removing coverage stops future dedicated collection but preserves history and identity resolution; public visibility is a separate control. Shared sources are collected once, regardless of how many officials reference them. Configuration changes must show estimated work and bounded backfill before execution.

## Collection boundary

C# schedules bounded discovery, fetch, and watch jobs. The collector receives versioned JSON manifests, emits JSONL result records on stdout and diagnostics on stderr, and writes capture artifacts under an assigned directory. C# validates and imports results idempotently. There is one durable scheduler. Newly discovered URLs return as candidates for that scheduler.

Requests carry source identity, allowed hosts/paths, cursors/validators, request/byte/time budgets, and job identity. Results carry requested/final URLs, status, timestamps, validators, content hashes, artifact references, discovered links, resource usage, and structured failures. Credentials are resolved separately and never logged in manifests.

Initial connectors: HTTP, feeds, and bounded HTML discovery. Required behavior includes robots checks, redirect/destination validation, per-host pacing, conditional requests, correct 304 and 429 handling, cancellation, and explicit oversized/incomplete response failures. Coordinate host pacing across collectors; initially avoid concurrent collector processes for the same host. Browser rendering/OCR are deferred until a selected source needs them.

Every fetch attempt is an observation. Only changed content creates new versions. Raw and normalized hashes are separate. Failed fetches do not mean deletion; parser changes do not mean source edits. Source-configured limits and deduplication run before AI.

### Implemented watched-page protocol

`Collection.Contracts` owns JSON protocol version 1. The collector accepts `collect <manifest.json>` and emits exactly one JSONL receipt for a valid request. Both sides reject unsupported/missing versions, unknown fields, duplicate fields, and null required fields. Wire names are case-sensitive camelCase. Exit codes are 0 for captured/notModified, 1 for failed/deferred, and 2 for invalid input. Diagnostics go to stderr. Application validates result identity, outcome, destination, and budgets; Infrastructure verifies the exact decompressed artifact length and SHA-256 before returning the receipt.

A request identifies one job and source, one URL, one exact allowed origin (scheme, host, port), a path or descendant prefix, an absolute artifact directory, and request/aggregate-byte/time/pacing limits. Optional ETag and Last-Modified validators support explicit conditional requests. There is no credential field. Defaults are 5 requests, 2 MB, 30 seconds, and 1 second between requests. The collector always requests `/robots.txt` first on the same origin, outside the content path restriction. Redirects must remain within the declared content scope.

Receipts distinguish `captured`, `notModified`, `deferred`, and `failed`. They carry requested/final URLs, observation time, HTTP status when available, validators, request count, aggregate received body bytes, failure code, and optional Retry-After seconds. Captured receipts additionally require a lowercase SHA-256, relative `<sha256>.gz` artifact name, and exact uncompressed artifact byte length. Aggregate body bytes include robots; artifact bytes describe only the captured response body. Headers and transport framing are not counted. An overflow probe may read one extra byte to detect an oversized unknown-length body; that byte is discarded. Compressed HTTP content is preserved as received; gzip is the storage envelope, and no normalized text is produced yet.

Captures are atomically promoted after a complete download, with existing content checked before reuse. Unchanged bodies share an artifact; this is file deduplication, not a durable idempotent import. Failures and 304/429 responses have no artifact. A 429 is returned as deferred with Retry-After when supplied; no retry is scheduled in this tracer. Host cancellation kills the child and accepts no receipt, so a killed process can leave an unreferenced temporary file. Durable recovery and orphan cleanup remain scheduler work.

Robots support is conservative: matching wildcard and CivicLens groups contribute Disallow rules, including `*` and terminal `$`; Allow exceptions and crawl-delay are not implemented. A 404 permits fetching. Other unavailable responses, nonidentity content encodings, malformed UTF-8, and rules beyond supported limits fail closed; robots redirects are not followed. Request budgets include robots and content redirects. Pacing applies within a run. The host serializes processes sharing an artifact directory; operators must avoid concurrent runs for the same origin across different directories or standalone collectors. Cross-run pacing and durable host backoff remain scheduler work.

## Evidence and AI

The data model will distinguish capture observations, immutable document versions, changes, statements, authoritative actions, proposed relationships, analysis runs, reviews, briefs, and publication releases. Evidence points to exact versioned text spans/page locations. Event time, publisher time, observed time, and last-checked time remain distinct.

Structured actions bypass relevance models. Explicitly watched documents retain versions regardless of relevance decisions. Classification of unstructured material begins in observation mode. Later routing supports process, defer, and skip-expensive-analysis, with reasons and retained input references. Audit a sample of skipped records. Do not silently truncate long documents; record chunk coverage and extraction failures.

Clef is a decision classifier, not the quotation extractor or brief writer. Extraction returns validated source evidence. Match statements/actions using identifiers, people, dates, jurisdiction, and issue candidates before any model comparison. Relationship assessments require editorial context. Briefs use bounded evidence selections and citation IDs.

Cache keys include input hashes, task/model/schema versions, and relevant configuration/context. Store model responses and provider metadata; hosted aliases may change. Enforce per-task and per-run limits. Budget exhaustion leaves visible pending work. Measure false negatives, precision, calibration, latency, downstream savings, and review workload on held-out examples, grouped to prevent document-version leakage.

## Review and MCP

Local development MCP will inspect jobs, explain processing, inspect source health, compare analysis runs, and run explicitly budgeted evaluations. Review tools will list/read cases, propose corrections, and record explicit decisions. Mutations use expected versions and idempotency keys. AI suggestions, model predictions, and human decisions are distinct records. Initial local tooling is read-only; mutations arrive with the review workflow.

Public MCP will expose search_records, get_record, get_official_timeline, and compare_document_versions. It retrieves published data with IDs, citations, dates, review status, coverage caveats, and release ID. It cannot initiate collection, inference, or edits. Bound and paginate responses, limit requests, accept record IDs rather than arbitrary fetch URLs, and treat source passages as untrusted attributed content.

## Publication and operations

Build HTML, records, and a bounded search index from an explicit approved selection. Validate citations and approval versions before publishing. Upload a complete versioned release, then change the active release pointer. Website and MCP share that release; clients can pin it across calls. Previous releases support rollback. Keep operational storage and unpublished material private.

Local Postgres and capture files need coordinated backups and a tested restore. Retention must preserve evidence referenced by published releases; disposable captures follow explicit policy. Monitor freshness, skipped/deferred work, source failures, storage, and spend. Public MCP requires request compute, but never the local database or AI inference.

## Legacy reuse

Use the legacy crawler's fixtures and lessons about normalization, robots, pacing, redirects, retries, atomic capture, and recovery. Reimplement in C# against the new contract. Verify registries before selective import. Preserve provenance for any migrated records. Do not port bot/propaganda/sentiment pipelines, live dashboard aggregation, old storage schemas, duplicate registries, deployment secrets, or historical documentation.
