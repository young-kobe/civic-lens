# Collection

## Purpose

Collection fetches configured sources through a separate, bounded collector process. The application verifies and imports each observation, then runs one durable pipeline from a due source check to the private review queue. The Congress.gov bill stream runs on this pipeline; see [bills](bills.md). People, sources, and coverage are in [configuration](configuration.md).

## Decisions

- The application schedules bounded discovery, fetch, and watch jobs. The collector only fetches and writes captures. The application validates and imports the results idempotently.
- Wire DTOs stay separate from domain evidence and database entities. A change to the transport layout therefore does not change persistence semantics.
- Every fetch attempt is an observation, also a failure or a 304. Only changed content creates a new document version.
- A failed fetch does not mean deletion. A parser change does not mean a source edit. Raw and normalized hashes stay separate.
- HTTP response metadata belongs to the observation, not to the capture. The same capture can occur with other content types, encodings, or validators, and must not erase those observations.
- A raw hash alone does not establish a semantic edit or a parser version. A document version is a later domain concept.
- Attempt results are separate sealed types. Each type exposes only the data it can carry, so contradictory combinations are unavailable through the public API. This replaces an outcome enum with nullable fields.
- A retry produces a new attempt. It never changes a failed result into a captured result.
- The import policy is a pure decision function, not a durable lifecycle state machine. It does no I/O.
- Application imports the receipt evidence, not the requested manifest values.
- All collection imports serialize at one advisory lock. A later change to the concurrency strategy should follow measured contention, not only the addition of more collectors.
- PostgreSQL notifications only wake the engine. Durable rows decide the runnable work.
- Network execution is not exactly once. An expired lease cannot prove that a stalled process has stopped its HTTP requests.
- A lost import acknowledgment never authorizes another fetch before reconciliation.
- Timeout reservations stay fully charged, because the receipt does not report elapsed time.
- Large crawl delays are not shortened to fit budgets. Repeated attempts can exhaust the bounded allowance of a job, and an operator then adjusts it.
- A robots rule that exceeds a limit blocks collection. The collector never accepts partial rules.
- Pacing groups bound the total load on shared infrastructure, such as all `*.house.gov` origins.
- A new source gets a deterministic first-check offset, so a fresh database does not send every check at once.
- Each configuration revision is stored once and referenced by ID. Otherwise each job and schedule would copy the whole snapshot. A [bill list page](bills.md#admission) of 251 jobs would then write hundreds of megabytes inside the job lock.
- `PostgresCollectionJobStore.SettleAttemptAsync` is the only path that sets a job to Succeeded. It adds the preparation row in the same transaction, so no recurring or startup registration scan is needed.
- Article jobs clear the validators of the discovery source and use page mode. This prevents recursive discovery.
- Batch replay imports captures before 304s. Random attempt-ID order therefore cannot decide permanent evidence links.
- Source-configured limits and deduplication run before any AI work.
- Collection makes no editorial decision. Changed evidence enters review, and approval and publication stay explicit.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- A capture hash keeps one byte length. Enforced by the primary key of `collection_captures` and `PostgresCollectionAttemptStore`. Test: `PostgresCollectionAttemptStoreTests.SerializesConcurrentSameAndDistinctAttemptsAndRejectsHashLengthConflict`.
- An attempt import is idempotent, and a conflicting reuse of an attempt ID fails. Enforced by `CollectionImportPolicy` under the `PostgresAdvisoryLock.CollectionImport` key. Test: `PostgresCollectionAttemptStoreTests.PersistsDuplicateAndConflictingReplayAcrossAdapterInstances`.
- Only a 304 links to a prior capture, and only to one unambiguous capture. Enforced by `CollectionImportPolicy`, `ck_collection_attempts_prior_outcome`, and `ck_collection_attempts_prior_pair`. Test: `PostgresCollectionAttemptStoreTests.LinksUnambiguous304AndPreservesUnresolvedAmbiguityOnReplay`.
- An enqueue key returns one job, and its reuse for another definition fails. Enforced by `IX_collection_jobs_idempotency_key`. Test: `PostgresCollectionJobStoreTests.EnqueueIsIdempotentAndConflictingKeysCannotReplaceSnapshots`.
- One managed collector runs at a time. Enforced by `ck_collector_singleton` and the slot lease in `PostgresCollectionJobStore`. Test: `PostgresCollectionJobStoreTests.SingletonAdmissionSurvivesDifferentRootsAndOriginBarrierOutlivesCollectorRelease`.
- Each origin and each job has at most one unresolved attempt. Enforced by `collection_job_origins.unresolved_attempt_id` and `IX_collection_job_attempts_job_id`. Test: `PostgresCollectionJobStoreTests.SingletonAdmissionSurvivesDifferentRootsAndOriginBarrierOutlivesCollectorRelease`.
- A stale job owner cannot renew, settle, or release. Enforced by the token, fence, and expiry checks in `PostgresCollectionJobStore` and `ck_job_lease`. Test: `PostgresCollectionJobStoreTests.ExpiredOwnerCannotRenewSettleOrReleaseNewOwnershipAndReservationsSurvive`.
- A settlement must match the imported evidence. Enforced by `PostgresCollectionJobStore.SettleAttemptAsync`. Test: `PostgresCollectionJobStoreTests.SettlementRejectsDifferentEvidenceBeforeRefundingBudget`.
- A succeeded job gets exactly one preparation row, in the settling transaction. Enforced by `PostgresCollectionJobStore` with `ON CONFLICT DO NOTHING`. Test: `PostgresCollectionJobStoreTests.SettlingACapturedJobQueuesOnePreparationRowAndReplayDoesNotAddAnother`.
- A scheduler admits no second active root job for one source and URL. Enforced by a check under the `PostgresAdvisoryLock.CollectionJobs` key, not by a unique index. Test: `PostgresCollectionScheduleStoreTests.ConcurrentSchedulersAdmitOnlyOneRootJob`.
- Discovery admits one job for each exact source and URL. Enforced by the primary key of `collection_candidate_jobs` and the `PostgresAdvisoryLock.DiscoveryCandidate` keys. Test: `PostgresDiscoveryAdmissionTests.ConcurrentFeedAndHtmlAttemptsCreateOnlyOneJobForAnExactSourceUrlPair`.
- A processing checkpoint needs the current lease and fence. Enforced by `EvidenceProcessingLifecycle` and `ck_evidence_processing_lease`. Test: `PostgresEvidenceProcessingStoreTests.EnsureIsIdempotentAndCheckpointFencesReclaimedLease`.

## Behavior

### Collection boundary

The collector receives a versioned JSON manifest. It writes capture artifacts under its assigned directory and emits exactly one JSONL receipt on stdout for a valid request. Diagnostics go to stderr. One durable job queue feeds the notification-driven evidence pipeline. Discovered URLs return as candidates for bounded admission into that queue.

Requests carry the source identity, the allowed host and path, the validators, the request, byte, and time budgets, and the job identity. Results carry the requested and final URLs, the status, timestamps, validators, content hashes, artifact references, discovered links, resource use, and structured failures. Credentials are resolved separately and never appear in manifests or logs.

The connectors are HTTP pages, RSS and Atom feeds, bounded HTML discovery, and JSON APIs. Required behavior includes robots checks, redirect validation, per-host pacing, conditional requests, correct 304 and 429 handling, and cancellation. Oversized or incomplete responses fail explicitly.

### Collector protocol

`Collection.Contracts` owns JSON protocol version 10. The collector and `CollectionRequest.Validate` accept only version 10 for new requests. `CollectionRequest.ValidateReadable` also accepts versions 7 and 9. Stored attempts, discoveries, and handoffs use it. `CollectionResult.ValidateAgainst` and `DiscoveryResult.ValidateAgainst` call it. Version 7 stored data has robots accounting and no `outOfScopeCount`. Other versions are unsupported. Jobs queued before an upgrade run at version 10. Configuration version 2 is independent of the collector protocol.

The collector accepts `collect <manifest.json>`. Both sides reject unsupported or missing versions, unknown fields, duplicate fields, and null required fields. Wire names are case-sensitive camelCase. Exit codes are 0 for `captured` or `notModified`, 1 for `failed` or `deferred`, and 2 for invalid input.

`CollectionResult.ValidateAgainst` owns receipt validity. The collector, the application, and the process adapter reuse it. It checks the result identity, the outcome, the destination, and the budgets. Structural validity does not prove that an artifact exists or matches its hash. Infrastructure therefore also verifies the exact decompressed artifact length and SHA-256 before it returns the receipt.

**Request.** A request names one job, one source, one URL, and one exact allowed origin (scheme, host, port). It also names a path or descendant prefix, an absolute artifact directory, and request, aggregate byte, time, and pacing limits. Defaults are 5 requests, 2 MB, 30 seconds, and 1 second between requests. Optional ETag and Last-Modified validators support explicit conditional requests.

A request has an explicit `mode`: `page` (the default), `feed`, `html`, or `api`. `maxCandidates` defaults to 100, with a maximum of 1,000. Feed and HTML modes return discovery results. Manifests are bounded to 64 KiB, both when read and when canonically serialized. Shared request validation rejects oversized work before collection or managed budget reservation.

**Receipt.** A receipt is `captured`, `notModified`, `deferred`, or `failed`. It carries the requested and final URLs, the observation time, the request count, and the aggregate received body bytes. It also carries a closed failure code and optional Retry-After seconds.

- `finalUrl` is the last attempted in-scope content URL, or the requested URL when no content request was made.
- `observedAt` is the completion time of this attempt, not a publisher or event time.
- Page receipts have a 1 MiB character bound, because repeated URLs and response metadata can exceed the input size. Discovery receipts have a 32 MiB bound.

**Sent validators.** `sentValidators` is required. It is null when the last attempted content request had no conditional headers, also for failures before any content request. Otherwise it holds `eTag` and `lastModified`, read from the formatted request headers just before the content request.

ETags use their serialized form. HTTP dates use whole seconds in UTC. The field describes headers applied to the request, not proof that the server received them. A redirect clears the values, unless it returns to the original URL. Contract validation checks the field against the request, the last attempted URL, and the request count.

**Response metadata.** `response` holds the content response status, ETag, Last-Modified, the full Content-Type with charset parameters, and the ordered Content-Encoding tokens. It is null when no valid content response metadata arrived, also for failures during the robots check. Headers from an earlier redirect never attach to a later failed request.

An absent Content-Type stays null. An absent Content-Encoding becomes an empty array. Codings keep their application order, also unknown tokens. Malformed or oversized metadata gives `failed` with `invalidResponse` before a capture is written. Content-Type and ETag are bounded to 4,096 characters. At most 16 coding tokens of at most 64 characters are accepted.

**Capture.** Only a `captured` receipt has `capture`: a lowercase `sha256`, a relative `<sha256>.gz` `relativePath`, and the exact `byteLength`. The length and hash describe the HTTP body bytes with content encodings still applied, not the gzip storage envelope. Aggregate `bytesReceived` includes robots requests. `capture.byteLength` counts only the content body. Headers and transport framing are not counted.

An overflow probe may read one extra byte to detect an oversized body of unknown length. The collector discards that byte. Captures are promoted atomically after a complete download, and existing content is checked before reuse. Unchanged bodies share an artifact. This is file deduplication, not an idempotent import. Failures, 304s, and 429s have no artifact. Stored hash-named gzip files keep their byte format and stay reusable after verification.

A 429 returns `deferred`, with Retry-After when the server supplies it. Host cancellation kills the child and accepts no receipt, so a killed process can leave an unreferenced temporary file.

**Scheme upgrade.** The collector can fetch a discovered `http` link over `https`. This happens when the allowed origin is `https` and the link has the same host and the default port. `CollectionRequest.ResolveCandidate` owns this rule.

**API mode.** The collector fetches one JSON resource with HTTP GET and sends `Accept: application/json`. It returns a capture with no discovery result. It knows nothing about any specific API; an Application parser reads the capture later. API mode makes no robots request, so `robotsRequestCount` is 0 and no crawl delay is declared.

API mode still honors 429 with Retry-After, `minDelayMilliseconds` pacing, the budgets, scope checks on redirects, and conditional validators. Its receipt limit is the page receipt limit. An API job with no `apiParser` completes at preparation, with no extraction or comparison stage.

**Credentials.** An API request may name a `credential` from the closed list in `CollectionCredentials`. The only name is `congressGov`, which maps to the environment variable `CIVIC_LENS_CONGRESS_API_KEY`. The manifest, receipt, capture metadata, and diagnostics carry the name only, never the key.

`CollectorProcess` keeps its strict environment allowlist and adds only the variable that the request names. The collector sends the value as the `X-Api-Key` header, only to the allowed origin. A missing or blank variable fails the attempt with `credentialUnavailable` before any request. Source configuration version 2 accepts `credential` only with mode `api`. The process adapter also removes `CIVIC_LENS_DATABASE` from the child environment.

### Robots and pacing

Except in API mode, the collector requests `/robots.txt` on the same origin first, outside the content path restriction. Robots redirects may use other paths on that origin. Content redirects must stay within the declared content scope. Cross-origin redirects are unsupported.

**Rules.** Robots rules select and merge the case-insensitive `CivicLens` product-token groups. Wildcard groups apply only when no matching product group exists. Allow and Disallow use the most specific matching normalized octets, and Allow wins equal ties.

- Matching supports `*`, a terminal `$`, UTF-8 paths, and percent-encoding normalization.
- Blank and comment lines do not end a group. Extension directives do not split consecutive user-agent lines.
- Unknown directives and malformed non-path rules are ignored.
- Limits are 2,048 characters for each rule, 2,304 for each line, and 10,000 rules, groups, and user-agent entries.
- Each regex match has 50 ms, and each path evaluation has 250 ms.

**Fetch.** Robots redirects stay on the configured origin, without credentials or fragments, with configured pacing between hops. Every hop uses the shared request budget and timeout. `robotsRequestCount` counts each robots hop inside the total `requestCount`. Several robots hops therefore cannot imply that a conditional content request occurred.

A robots 404 permits collection. Other ambiguous or unreadable responses block content fetching. A robots 429, a server error, or a network failure gives a deferred observation with no content response metadata. Retry-After is kept when supplied. A matching denial is a permanent failure.

**Decoding.** Identity, gzip, deflate, and Brotli responses are supported, with strict UTF-8 decoding for robots. Wire bytes use the shared byte budget. Each decoding stage is bounded to 10 MB and drained to completion. At most 16 encoding tokens are accepted. Executables and tests enable the strict compression validation of the pinned runtime.

**Cache.** The collector caches robots rules as files in the artifact directory of the request. The name is `robots-<sha256 of the normalized origin>.json`. The file holds the decoded robots text, the fetch time, and whether the entry was a 404. `RobotsCache` owns the files.

- A copy younger than 24 hours replaces the robots request. The collector then reports `robotsRequestCount` 0, `robotsCacheHit` true, and the cached crawl delay.
- On a cache hit, the collector does not wait before its first content request, because Application origin pacing already spaced the jobs.
- Only 200 and 404 responses are cached. Other responses and rules that fail to parse are never cached.
- A corrupt, unreadable, or future-dated file is ignored and refetched. A failed cache write does not fail collection.
- When a refetch gets a 5xx, the collector obeys a cached 200 copy up to 7 days old, also its Disallow rules. It reports one robots request and `robotsCacheHit` true.
- A cached 404 is never used that way. A robots 5xx then behaves as with no cache.
- A robots 429 is never masked. It always defers with `rateLimited`, so the application applies the group backoff.
- A timeout before any request, for example while the collector reads the cache, is a `timeout` failure with no requests.

The cache write uses a temporary file and moves it over the old entry. Unlike capture promotion, it does not flush first and it replaces an existing file. A lost or torn entry only causes a refetch. The collector holds no other state and still enforces robots itself.

Version 10 adds the result field `robotsCacheHit`, which the collector omits when false. Every non-API result that made requests needs a robots request or a cache hit. A crawl delay or a `crawlDelay` deferral also needs one of them. Version 9 results always need a robots request and cannot set the field. API results cannot set it.

**Crawl delay.** Crawl-delay is a supported extension. Nonnegative decimal seconds round up to milliseconds. Repeated declarations in the selected groups use the maximum. A zero declaration is distinct from an absent declaration. Receipts carry the declaration as `robotsCrawlDelayMilliseconds`.

Invalid declarations or delays above 922,337,203,685 seconds block collection. This maximum keeps the rounded retry seconds representable during evidence import. The collector waits for the greater of configured pacing and the declared delay between requests. A wait that cannot fit the remaining timeout gives `deferred` with the failure code `crawlDelay` and a rounded-up retry delay.

Application keeps the declared delay with the imported attempt evidence, also zero, and rejects conflicting replays. Old evidence without the column keeps an unknown declaration. Job resolutions keep the delay, also when they recover only from imported evidence after a lost handoff.

Settlement extends the origin deadline by the greater of configured and declared pacing, measured from database settlement time. Deferred observations also apply the shared retry backoff. Deadline arithmetic saturates at the maximum supported date.

**Pacing groups.** Configuration v2 can define top-level `pacingGroups`. Each group has a lowercase `hostSuffix` of at least two labels and a `minDelayMilliseconds` from 1 to 3,600,000. Omitted groups leave the canonical JSON and the revision IDs unchanged. Duplicate suffixes are invalid.

`CollectionConfiguration.PacingGroupOf` resolves an origin. A host matches when it equals the suffix or ends with a dot and the suffix, so `evilhouse.gov` does not match `house.gov`. The longest matching suffix wins, so each origin has at most one group.

- Each group has one row in `collection_job_origins` with the key `group:<suffix>` and no unresolved attempt.
- Admission of any job checks the origin row and then the group row through `ReadAdmissionAvailabilityAsync`. A closed group returns the existing origin backoff block.
- Every settlement moves the group deadline to the later of its old value and database time plus the group delay.
- Only a 429 (`RateLimited`, also a robots 429) applies the Retry-After backoff of the job to the group. A 5xx, `RobotsUnavailable`, or `CrawlDelay` leaves the group at its normal delay.
- The delay applies to each job. With a cached robots file, each job makes one request.

**Pacing keys.** Each new job row stores its pacing key in `collection_jobs.pacing_key`. `JobRowWriter` writes it in the transaction that creates the job. The key is the group key when the configuration revision of the job puts its origin in a group. Otherwise it is the normalized origin key.

`PostgresCollectionJobStore.WherePacingOpen` is the one filter. The worker queue uses it to leave out jobs whose key row has a future `not_before`. `TryClaimAsync` applies it again in the claim statement, under the job lock and database time. A pass therefore does not claim, start, and release each blocked job.

When a claim returns nothing, `RunCollectionJob` makes one more read, `ReadPacingBlockAsync`, with the same filter. A closed key then gives the `originBackoff` block with its deadline. The worker queue leaves out closed keys, so the worker only makes this read when a claim loses a race.

A `Running` job holds an unresolved attempt, so it stays claimable for recovery while its key is closed. A grouped job's key is only its group. A closed origin row inside an open group therefore still blocks it at attempt start. The existing wakeup query already includes every `not_before`.

Jobs created before the `JobPacingKeys` migration have a null key. The migration does not backfill it, because the key needs .NET origin normalization and group matching that SQL cannot repeat exactly. The worker claims those jobs while their origin waits, and attempt start blocks them. A large old backlog therefore still costs one claim, start, and release for each pass.

The checked-in federal configurations set 3,000 ms for `house.gov` and `senate.gov`, as separate groups. At about 538 House checks an hour, a 3-second delay uses about 45 percent of the group capacity. Jobs created under an older configuration revision have no pacing group until they finish.

### Discovery and admission

**Feeds.** Feed mode captures the response through the same HTTP, robots, scope, verification, and handoff boundaries as page mode. RSS 2.0 and Atom parsing returns only bounded, resolved, in-scope article URLs. Parsing decodes identity, gzip, deflate, and Brotli without changing the stored bytes.

Each HTTP decoding stage is bounded to 10 MB and drained before parsing. An inner decoder therefore cannot hide an incomplete outer trailer or excessive intermediate expansion.

- XML character decoding follows [RFC 7303](https://www.rfc-editor.org/rfc/rfc7303.html#section-3.2): BOM, HTTP charset, then the XML declaration or the default.
- Parsing limits decoded input to 10 MB, XML nodes to 100,000, and depth to 64, before it builds a document tree. DTDs and external resolution are off.
- Relative links use the final response URL. They resolve against each `xml:base` from the root element inward. An invalid base makes the discovery `invalid`.
- Nested markup in RSS link text is rejected, not joined into a URL.
- Unknown feed formats and unsupported charsets report `unsupported`, and the raw capture stays.

**HTML.** HTML mode extracts HTML-namespace anchor `href` values from the saved capture with AngleSharp 1.8.3. It loads no resources and runs no scripts. Relative URLs use the final response URL or the first valid HTTP(S) `<base href>`.

- Fragments are removed before scope validation and exact deduplication. Queries stay significant.
- Decoded input is limited to 10 MB. Parser tokens and created elements are limited to 100,000. Tracked open tags and tree depth are limited to 64.
- The parser recovers optional paragraph closures and mismatched end tags, within the DOM depth limit.
- Self-closing SVG and MathML elements add no lexical depth. Deep foreign content stays bounded by the DOM check.
- Cancellation is checked during decoding and token processing. Cancellation and timeout keep the collection-failure semantics and can leave a completed artifact with no capture reference in a receipt.

Character decoding uses the BOM, the HTTP charset, a real meta charset declaration in the first 1,024 bytes, and then Windows-1252. Labels use the HTML mappings, so ISO-8859-1 and ASCII aliases map to Windows-1252. HTTP `x-user-defined`, UTF-32 BOMs, and labels of the HTML replacement encoding are unsupported. An `x-user-defined` meta declaration uses the required Windows-1252 mapping.

**Discovery result.** A captured feed or HTML page has a separate discovery status: `parsed`, `invalid`, `unsupported`, or `limitExceeded`. A parsing failure keeps the raw capture and returns no partial candidate list. A successful collection job therefore does not prove a successful parse.

Discovery results carry a required `outOfScopeCount`: the number of distinct resolved links excluded by scope. A failed discovery reports 0. Stored version 7 results omit the count, so `DiscoveryResult.ValidateAgainst` requires it to be absent for them. The check history in the review workspace shows a nonzero count.

A 304 has no new discovery result. Retained candidates stay bound to the earlier captured attempt. Failed and deferred outcomes create no candidate list.

**Evidence.** Application snapshots discovery evidence immutably, with the original request scope. Infrastructure stores it in `collection_discoveries` in the same transaction as the attempt and capture identity. A conflicting replay of candidate URLs, status, or request snapshot is rejected. Receipt replay rebuilds this evidence without another fetch. Discovery creates no document version or editorial claim.

**Admission.** `discovery admit` turns one persisted parsed feed or HTML discovery into page jobs. `feeds admit` is an alias. The current enabled source configuration supplies the article job settings and the admission policy. Its source ID, discovery mode, and scope must match the retained discovery.

A batch reserves the full aggregate retry allowance of each admitted job against the request, byte, timeout, and job-count caps. Defaults allow at most 10 jobs, 150 requests, 60 MB, and 900 timeout seconds over their full lifetimes. These limits apply to one admission; another explicit batch authorizes more work. Unused allowance is not redistributed within a committed batch.

`DiscoveryAdmissionPolicy.OpenBudget` computes the reservation of one job once for the template. It returns a `DiscoveryAdmissionBudget` that owns the running totals and checked sums. Article admission and bill stream admission both use it.

Admission uses the job coordination transaction and lock. It reads the candidate bindings in one bounded hash lookup. Collision checks and budget reservations then follow the retained URL order. The batch input, the result, the URL bindings, and the new jobs commit together.

- The same key and inputs return the saved result. Conflicting reuse of a key fails.
- Source plus exact resolved URL identifies an initial article admission, across feed and HTML discoveries from that source.
- URL hashes bound the index size. Exact value comparisons guard against collisions.
- Manual admission never admits an existing binding again, also when its job was cancelled or failed.
- Automatic source checks may recheck a known URL after its latest job is terminal, within the same batch limits. Active jobs are skipped.
- Unadmitted candidates stay available for another batch with a new key.

Admission only enqueues jobs. The worker runs them under the existing retry, pacing, cancellation, and recovery rules. `jobs run` stays a manual diagnostic action.

### Collection domain and import rules

A collection attempt result records what happened during one completed source check. The collection protocol also calls it an observation. A capture identifies exact response body bytes by SHA-256, after removal of the storage gzip envelope and before decoding of HTTP content encodings.

Core/Collection implements these rules as immutable domain values and the pure `CollectionImportPolicy`. Attempt identity is independent of job identity. A new attempt with identical bytes stays a separate result. Capture identity is the hash and the byte length, without a storage path.

**Result types.** `CapturedAttemptResult`, `NotModifiedAttemptResult`, `FailedAttemptResult`, and `DeferredAttemptResult` are sealed types on the abstract `CollectionAttemptResult` base. A capture belongs to the captured type. Failure information belongs to the failed and deferred types. A retry delay belongs to the deferred type. Constructors validate the required values. Duplicate comparison includes the concrete type and its data.

`CollectionResponse` builds an `ImmutableArray<string>` of ordered content encodings. It copies mutable inputs, accepts empty collections, and rejects uninitialized arrays. Equality compares the values in order. Wire records keep their JSON-friendly arrays and validation, and domain constructors own the immutable evidence. Wire parsing, header syntax, scope, budgets, and file verification stay at their existing boundaries.

**Import decision.** `CollectionImportPolicy.Decide` returns an immutable `CollectionImportDecision`. Its `ImportDisposition` is `NewAttempt` or `DuplicateAttempt`, which classifies the import, not a lifecycle move. A reimport of identical attempt data and sent validators keeps the original result and capture link. Conflicting reuse of an attempt ID is rejected.

**304 link.** `PriorCaptureLinkStatus` is `NotApplicable` for other outcomes, and `Unresolved` or `Linked` for a 304. `PriorCapturedAttempt` names the linked evidence, separate from the current result and its metadata.

- A link needs a distinct, earlier or simultaneous captured observation of the same source and exact URLs.
- The conditional attempt must have no redirect.
- The sent ETag takes precedence over Last-Modified and must match exactly. A conflicting returned ETag leaves the link unresolved.
- Without an ETag, a matching sent Last-Modified can establish the link.
- Missing proof keeps the 304 without a capture link.
- Failed and deferred attempts carry no capture and cannot replace prior evidence.

For a new 304, Infrastructure loads the possible captures of the same source and exact requested and final URLs, with no later observation time. Core `DecideFromCandidates` owns final eligibility and ambiguity. Different eligible capture identities leave the result unresolved. One shared capture selects the newest observation, with the ordinal attempt ID as the tie-break. A replay keeps its original resolved or unresolved link.

**Import handler.** `CollectAndImportCollectionAttempt` maps a verified receipt under an application-owned attempt ID, independent of the wire job ID. `ICollectionAttemptStore.ImportAtomicallyAsync` loads existing and prior evidence, applies the Core policy, and commits in one atomic operation. `StoredCollectionAttempt` holds the result, the sent validators, and the prior capture link. The new-or-replay disposition belongs only to the current call.

Collection runs through `ICollectorProcess`, which verifies capture bytes before it returns. Receipt validation runs again before mapping. Collector errors, verification errors, invalid receipts, and cancellation prevent import. Failed and deferred outcomes are valid evidence to import. Retry-After values beyond the `TimeSpan` range are rejected before persistence, not truncated.

Each fresh run collects and saves a handoff before it imports. The recovery handler replays saved receipts without the collector. Managed jobs compose the same import and handoff boundaries.

### Receipt handoff and replay

Application owns `PendingCollectionHandoff`, the handoff store and capture verifier ports, and the fresh and replay use cases. A handoff has its own version 1 envelope. It holds the original application attempt ID, the original collection request, and the receipt. Fresh collection and replay share the evidence mapping and the atomic import rules.

**Spool.** Infrastructure stores pending envelopes at `<artifact-root>/.pending/<attempt-id>.json`. A file is written to a temporary file, flushed, and promoted without overwriting an existing handoff. A spool lease serializes fresh collection and recovery on the same root, with a cancellable wait of up to 30 seconds. The spool is independent of managed job leases, so unmanaged commands can use it. A handoff can be saved while Postgres is unavailable.

Recovery is possible only after the complete verified handoff is saved. An interruption before that save can lose the receipt, even when capture bytes exist. Temporary handoff files are never replayed. The checkpoint covers process interruption and restart, not machine power loss, and it does not replace backups.

**Replay.** Replay validates the envelope version, the filename and attempt identity, the original request, and the receipt. Infrastructure checks the capture length and SHA-256 again when a capture is present. Capture lookup uses the selected runtime root and the validated hash-based filename, so relocated storage works.

Replay needs neither the current source configuration nor the original collector executable. Missing or corrupt artifacts and invalid or unsupported envelopes prevent import and keep the pending file. A deserialized receipt is never accepted as proof of capture verification.

**Cleanup.** Only a confirmed import permits handoff removal. An uncertain commit keeps the handoff and replays the same attempt ID under the duplicate and conflict rules. A cleanup failure keeps the confirmed import result, and a later replay is safe. Cleanup never deletes captures.

**Batch replay.** `receipts replay <artifact-directory> <attempt-id|--all>` needs the database. `receipts list <artifact-directory>` does not. Listing reports pending identifiers without parsing them, so malformed envelopes stay visible.

Batch replay imports captured observations before 304s. Any unconfirmed captured import or unreadable envelope blocks the 304 imports of the batch with `PrerequisiteUnconfirmed`. Unrelated valid captures and failed or deferred observations still proceed. Confirmed captures with failed cleanup stay eligible evidence.

This conservative gate can delay unrelated 304s, but it avoids a second copy of the Core eligibility rules. It covers pending evidence in the selected spool only. It never rewrites stored 304 links. A single replay processes only its selected attempt. Cancellation stops further work and keeps the completed reports.

### Managed collection jobs

Application `Collection/Jobs` owns the job lifecycle and runner. `ICollectionJobStore` is the atomic persistence boundary, and Postgres implements it. Jobs move through Pending, Running, WaitingToRetry, Succeeded, Failed, and Cancelled.

A completed HTTP observation stays immutable. A retry creates a new attempt ID and a new collector wire request ID. An interrupted run without a complete receipt is job history, not invented HTTP evidence.

**Enqueue.** Enqueue snapshots the settings, explicit validators, and retry and budget policy of one source. An explicit idempotency key returns the same job for an equal definition and rejects conflicting reuse. Later runs need neither the original configuration nor an enabled entry in a changed configuration. Artifact roots come at run time. One manual run reconciles an unresolved attempt first and makes at most one new collection.

**Coordination.** Short Postgres transactions serialize admission and settlement with a transaction advisory lock and database time. Job listing and inspection read jobs and attempts in two queries in a repeatable-read snapshot, without the lock.

- The worker claim scan reads Pending, Running, and WaitingToRetry jobs in `(created_at, job_id)` order. The partial index `ix_collection_jobs_active_created_at_job_id` holds only those rows, so finished job history does not slow the scan. Job listing and keyset paging keep the full `(created_at, job_id)` index.
- Each job has an expiring token and an increasing fence. Expired ownership rejects stale database updates and signals the local child to stop.
- A renewable singleton collector slot admits one managed collector across all artifact roots. Recovery and waiting jobs do not hold that slot.
- A per-origin unresolved-attempt barrier survives lease expiry until reconciliation. An unprocessed 429 receipt therefore cannot be bypassed.
- Origin deadlines include minimum pacing and server Retry-After, also values beyond the backoff cap.
- Direct `collect`, `collect-import`, and standalone collector runs are outside this coordination.

**Budgets.** Admission reserves the maximum request, byte, and timeout allowance of each attempt before launch. A verified receipt refunds unused requests and bytes. Unknown use stays fully charged. Aggregate budgets and attempt caps are checked again before a retry.

The default policy allows three attempts and exponential delays from 30 seconds, capped at 3,600 seconds. Aggregate limits default to three full attempts. Policy overrides live on the source.

**Recovery.** Recovery checks imported evidence before the saved handoff. Managed collection saves its receipt atomically before it waits for the spool lease, so contention cannot discard the receipt.

1. A valid saved receipt is verified again and imported idempotently.
2. The job is settled.
3. The handoff is removed.

Imported evidence without a usable receipt can settle with conservative full accounting. Invalid or missing capture evidence blocks receipt recovery. With neither evidence nor handoff, the earlier run is recorded as interrupted, with the full reservation charged. Jobs and evidence use separate transactions, so replay repairs a partial completion.

A stale runner can save a late receipt after its attempt was already settled as interrupted. That handoff stays available to `receipts replay`. Later managed runs do not revisit settled attempts.

**Cancellation.** The CLI uses a 60-second renewable database-time lease. A persisted cancellation is seen on renewal, normally within 20 seconds. Job cancellation prevents future admission and signals an active collector. Complete receipts stay available for import. Ctrl+C interrupts only the runner. Ownership renews during collection and recovery.

### Autonomous collection engine

The worker owns one durable flow: due source, admitted check, collection and verified import, preparation, extraction, comparison, and the private review queue. Application owns the scheduling contract and the progression rules in `Application/Collection/Jobs` and `Application/Collection/Processing`. Infrastructure persists checkpoints and wakeups transactionally.

Collection state keeps its meaning: success means that verified collection evidence was imported. Separate processing records keep the stage, the execution status, the ownership fence, the retry deadline, and exact extraction and comparison references.

**Worker.** `CollectionJobWorker` runs the pipeline in a separately launched process. It interleaves bounded collection pages with evidence processing through the same fenced handlers. The default page size is 20, with a maximum of 100. It drains runnable work before it waits for notifications or persisted retry, pacing, and ownership deadlines.

`worker --once` admits due checks and drains the runnable stages, also downstream work released during the run. The worker requires `CIVIC_LENS_COLLECTION_CONFIG` and validates that configuration before it starts.

**Leases.** Evidence processing shares Application-owned lease bounds of one second to ten minutes, for pipeline startup, processing startup, claims, and renewals. Renewal runs every third of the lease duration. Retry checkpoints carry a nonnegative delay of at most five minutes.

A checkpoint reads the database clock once in its transaction. `EvidenceProcessingLifecycle` computes the attempt count and the retry deadline from that reading. The fenced update checks the lease against the same reading. A claim, a renewal, and a release each stay one atomic statement that reads the database clock as it runs.

**Schedules.** At startup the worker synchronizes durable source schedules and keeps the exact configuration revision for future admission. Configuration changes take effect on restart.

- Removed or disabled sources pause. Their retained jobs and evidence stay inspectable. A re-enabled source is due at once.
- A new source is due after a fixed offset. `WatchedSourceConfiguration.InitialDueOffset` takes the SHA-256 of the source ID as a big-endian 64-bit number, modulo the smaller of the interval and one hour in seconds.
- The offset applies only to new schedule rows. Later due times are database time plus the interval, so the spread persists.
- Unchanged schedules keep their next due time across restarts.
- `checkIntervalSeconds` overrides the hourly default, from 60 seconds to seven days. A changed interval starts the next interval from synchronization time.
- Omitted intervals stay out of the canonical JSON, so older revision IDs do not change.

**Due admission.** Admission of a due check and the move of the next due time commit in one transaction. Each due source admits at most one root job, then its next due time becomes database time plus its interval. Missed intervals coalesce into one check.

An active job for the same source and exact root URL suppresses the scheduled admission and uses that due slot. A manually admitted job counts too. Coverage eligibility uses the current database UTC date, and an ineligible slot advances without collecting. Cancellation stops one job; pausing a source in configuration stops its future checks.

**Configuration revisions.** Each revision is stored once in `collection_configuration_revisions`, keyed by its hash ID. Job, admission batch, and source schedule rows reference it through `configuration_revision_id` with a restricting foreign key. Postgres therefore refuses to delete a revision that a row needs. Their stored JSON never embeds the revision.

- A due tick loads each distinct schedule revision once.
- A write inserts the revision when it is absent, in the transaction of the job rows, once for each operation.
- A read loads the distinct revisions of its rows in one query. It builds each `CollectionConfigurationRevision`, which verifies the hash. An altered revision is an invalid stored definition.
- `StoredJobDefinition` owns this mapping. Application code still sees a full revision on `CollectionJobDefinition`.
- `JobRowWriter` owns the creation of job rows and the single write of each revision.
- Admission replay compares stored input as JSON values, because Postgres may reorder keys.

| Engine concern | Implemented policy |
|---|---|
| Source scheduling | Fixed interval for each source, hourly default; due-time order with source ID tie-break; one catch-up after downtime |
| Work selection | Bounded batches, oldest eligible collection first; observation order for evidence; prerequisite waits leave runnable capacity |
| Admission | Enabled source and current coverage, no overlapping active root check, exact-URL deduplication, job and discovery budget reservation |
| Collection concurrency | One managed collector slot for each store, origin pacing and backoff, unresolved-attempt barrier, renewable fenced leases |
| Stage handoff | Preparation registration in the settlement transaction; idempotent child admission and evidence saves; fenced checkpoints |
| Recovery | Startup drain of durable work, upgrade backfill, persisted retry and lease deadlines, connection resubscription, durable predecessor release |
| Human boundary | Changed evidence enters review; approval and publication stay explicit |

**Wakeups.** The listener subscribes before it drains. It combines schedule, retry, pacing, and ownership deadlines in database time. The release of a runnable collection claim signals work. A release behind a timed barrier carries its deadline. A commit after that deadline therefore cannot strand work or make blocked jobs wake themselves again and again.

### Preparation

`JobPreparation` in `Application/Collection/Processing` owns the preparation stage. It reads a succeeded job and its attempts. It admits article batches or bill stream pages and returns a `JobPreparationResult`. `EvidenceProcessingWorker` checkpoints that result and does not know the preparation rules. `ApiResponseReader` reads the saved capture of an API job. Bill stream preparation is in [bills](bills.md).

Each successful configured discovery check admits one idempotent article batch, with its retained configuration and coverage date. New candidates and rechecks share the configured batch limits. A replay of a parent check cannot create another batch. Later passes do not drain every deferred candidate. A preparation failure shows as such, not as a collection failure.

**Extraction and comparison.** Captured pages use retained content profiles for readable text; see [documents](documents.md). A comparison that waits for an earlier extraction parks as a durable dependency and leaves capacity for preparation. Dependency registration checks readiness again atomically with release, so a concurrent completion cannot lose the wakeup.

Comparison reads the predecessor evidence again after the readiness check, because history and processing status are separate reads. It uses the preceding compatible observation and never skips across failed or unprocessed history. A first observation or a processing gap sets a baseline. Identical text completes without a review candidate.

Complete changed comparisons enter the private review inbox. Missing profiles, invalid evidence, and processing limits produce visible attention states. Transient failures retry within bounded limits. Immutable evidence saves and admission keys make replay safe. Leased checkpoints stop a stale worker from advancing the progress of another worker.

### Job and processing states

`ck_job_state` lists the job states. `CollectionJobLifecycle` decides settlement moves. `PostgresCollectionJobStore` also sets Running, Failed, and Cancelled directly.

| From | To | Write |
|---|---|---|
| (new) | Pending | `JobRowWriter` creates the job |
| Pending, WaitingToRetry | Running | `TryStartAttemptAsync` starts an attempt |
| Pending, WaitingToRetry | Failed | `TryStartAttemptAsync` when the budget or attempt cap refuses a new attempt |
| Running | Succeeded, Failed, WaitingToRetry, Cancelled | `SettleAttemptAsync`; a cancellation request wins |
| Pending, WaitingToRetry | Cancelled | `CancelAsync` when no attempt is unresolved |

A claim changes only the lease, never the state. Job leases last 3 to 600 seconds.

`ck_evidence_processing_stage` lists the stages: Preparation, Extraction, Comparison, and Complete. `ck_evidence_processing_status` lists the statuses. `EvidenceProcessingLifecycle` decides most moves, and `PostgresEvidenceProcessingStore` applies them. `ReleaseAsync` and `ParkOrRequeueAsync` bypass the lifecycle. They write fenced raw SQL that matches the status, lease token, fence, and an unexpired lease.

| From | To | Write |
|---|---|---|
| (new) | Preparation, Pending | Settlement of a succeeded job |
| (new) | Extraction, Pending | Preparation of a captured attempt |
| Pending, due RetryWaiting, expired Running | Running | Claim, with a higher fence |
| Running | Pending | Release on shutdown |
| Preparation, Running | Complete, Succeeded or Blocked | Preparation checkpoint |
| Extraction, Running | Comparison, Pending, or Complete, Blocked | Extraction checkpoint |
| Comparison, Running | Complete, Succeeded or Blocked | Comparison checkpoint |
| Comparison, Running | WaitingForPredecessor | The earlier extraction is not ready |
| Comparison, Running | Comparison, Pending | The predecessor is already ready |
| WaitingForPredecessor | Pending | The trigger function `civic_lens_release_predecessor_waiters` |
| Any stage, Running | Same stage, RetryWaiting or Failed | Retry or failure checkpoint |

The release of predecessor waiters is the one move in SQL. It needs the predecessor lock in the trigger; see [advisory locks](../architecture.md#advisory-locks).

### Sources page

The review workspace has an owner-only Sources page when `CIVIC_LENS_COLLECTION_CONFIG` names an absolute configuration path. The owner can enqueue a source check (Check now), cancel a job (Stop), and start a draft from a found change.

- The page lists configured sources with the health of their latest check, and the recent checks.
- Check details open in a side panel. It shows each attempt, the retry timing, Check again for a tracked page URL, and a link to all saved versions.
- The page refreshes every 5 seconds while a check is active and every 30 seconds otherwise. A connection failure shows a reconnect notice.
- It accepts configured sources and retained article URLs only. It holds no demonstration records and does not claim worker liveness.
- A baseline or unchanged observation does not enter review.

The owner can also collect a retained article URL again. That uses the current coverage, limits, and profile, and keeps the retained URL. It never reruns a listing or creates work from an arbitrary URL. The page has no manual admission, extraction, or comparison controls; only the worker and the CLI run them.

Browser actions call Application handlers. Only the worker runs queued HTTP collection, and only the worker reads and writes captures. The page reuses the newest page of recent jobs to find the latest check of each source, and reads only the missing sources. It reuses a selected job from the listed page, or loads an older job once. It reads processing progress in bounded batches by job ID.

### CLI composition

Host owns command parsing and output. `collect` and `collect-import` prepare the attempt through `PreparedCollectionAttempt` before execution. `PreparedCollectionAttempt` is built from a `ConfiguredCollectionSource`, so only a validated configuration revision and an eligible source reach the handlers.

- `ConfiguredCollectionSource` owns request and job creation for one source. `CollectionConfiguration.EligibleSource` owns the enabled and coverage check.
- `collect` needs no database. `collect-import` then calls the collection and import handler.
- Application creates a fresh attempt ID and a separate wire job ID before collection. Neither is a durable job record. The CLI accepts no attempt-ID override.
- Every `collect-import` collects again. `receipts replay` keeps the saved attempt identity and collects nothing.

`db migrate`, `collect-import`, and `receipts replay` need `CIVIC_LENS_DATABASE` with explicit Host and Database values. Runtime commands have no fallback database. `PostgresCollectionAttemptStore.FromConnectionString` owns the provider configuration and the context factory, so Host has no EF or Npgsql package dependency. Import never applies schema changes.

Host writes the attempt ID to stderr before collection. After import it writes a JSON summary with `attemptId`, `outcome`, `importDisposition`, `priorCaptureLinkStatus`, and `handoffRemoved`.

Exit codes are in [operations](../operations.md#postgres-adapter-and-migrations).

Provider exception details are not printed, because they can hold connection values. Cancellation before execution reports a generic cancellation. An interrupted or failed import reports that persistence is not confirmed. The commit may still have succeeded with a lost acknowledgment. Pending handoffs stay available for replay.

## Storage and contracts

- The generated [collection schema](schema/collection.md) lists every table, column, key, check, index, and trigger.
- `PostgresCollectionAttemptStore` uses a fresh `CollectionAttemptDbContext` for each call. It starts a READ COMMITTED transaction, takes the `civic-lens-collection-import` advisory lock shared by all collection imports, and then reads existing evidence. The lock order is in [advisory locks](../architecture.md#advisory-locks).
- All import writers must use that lock. HTTP collection and capture verification finish before the operation. Reads and unrelated work do not take the import lock.
- `collection_captures` has one row for each SHA-256 with its immutable byte length.
- `collection_attempts` keeps each outcome, the source and URLs, the response metadata, the applied validators, the declared crawl delay, and an optional prior-capture reference.
- Exact timestamps use UTC ticks in `bigint`. Retry durations use ticks. This keeps .NET precision for replay comparisons.
- Ordered content encodings use PostgreSQL `text[]`. Presence flags tell an absent response or validator set from a present one whose optional fields are null.
- Raw capture files stay outside Postgres. No file bytes or storage paths enter domain identities.
- Unique keys, foreign keys, and check constraints protect capture identity and outcome shape. The adapter inserts evidence and never updates an observation. This is an application write invariant, not protection from a database administrator.
- Conflicting attempt replays and reuse of a capture hash with another length fail without partial writes.
- The migration adds the prior-link composite foreign key explicitly. EF alternate keys would make the capture column non-nullable for every outcome. The model keeps a unique index on the referenced pair.
- `collection_discoveries` holds discovery evidence. `collection_admission_batches` holds batch inputs and results. `collection_candidate_jobs` holds the URL bindings of admitted candidates.
- A migration renamed the old feed tables without rewriting evidence. Legacy feed batch input serialization stays, so old batches replay idempotently.
- `collection_jobs`, `collection_job_attempts`, `collection_job_origins`, `collection_collector_slot`, and `collection_source_schedules` hold managed job state.
- `collection_configuration_revisions` holds each configuration revision once.
- `evidence_processing` is keyed by job and attempt ID. Each succeeded job gets exactly one Preparation row with the attempt ID `prepare`. Each captured attempt that preparation registers gets one Extraction row.
- Schema changes go only through checked-in EF migrations and `MigrateAsync`. `db migrate` applies them. Generated designers and snapshots keep the EF layout.
- Integration tests start one Testcontainers Postgres for each test run and migrate one template database in it. Each test copies the template. The migration tests migrate an empty database and check for model drift. Full CI needs Docker. A category filter runs the database-free tests.
- These tests verify exact evidence round trips, duplicate, conflicting, and concurrent imports, capture-length conflicts, 304 ambiguity, and transaction cancellation and rollback.
- Receipt tests also cover failed-import recovery, relocation, revalidation, uncertain commit replay, and cleanup failures. No test establishes machine power-loss durability or a production restore.
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Wire protocol and validation | `src/CivicLens.Collection.Contracts/CollectionProtocol.cs`, `src/CivicLens.Collection.Contracts/CollectionRequest.cs`, `src/CivicLens.Collection.Contracts/CollectionResult.cs`, `src/CivicLens.Collection.Contracts/DiscoveryResult.cs`, `src/CivicLens.Collection.Contracts/CollectionCredentials.cs` |
| Collector | `src/CivicLens.Collector/Program.cs`, `src/CivicLens.Collector/Http/HttpCollector.cs`, `src/CivicLens.Collector/Http/CaptureDecoder.cs` |
| Robots | `src/CivicLens.Collector/Http/RobotsRules.cs`, `src/CivicLens.Collector/Http/RobotsCache.cs` |
| Feed and HTML discovery | `src/CivicLens.Collector/Http/FeedParser.cs`, `src/CivicLens.Collector/Http/HtmlParser.cs`, `src/CivicLens.Collector/Http/DiscoveryCandidates.cs` |
| Domain results and import policy | `src/CivicLens.Core/Collection/CollectionAttemptResult.cs`, `src/CivicLens.Core/Collection/CollectionImportPolicy.cs`, `src/CivicLens.Core/Collection/CollectionImportDecision.cs`, `src/CivicLens.Core/Collection/CollectionResponse.cs` |
| Import handler and ports | `src/CivicLens.Application/Collection/CollectAndImportCollectionAttempt.cs`, `src/CivicLens.Application/Collection/ICollectorProcess.cs`, `src/CivicLens.Application/Collection/ICollectionAttemptStore.cs`, `src/CivicLens.Application/Collection/StoredCollectionAttempt.cs` |
| Handoff and replay | `src/CivicLens.Application/Collection/PendingCollectionHandoff.cs`, `src/CivicLens.Application/Collection/RecoverCollectionAttempts.cs`, `src/CivicLens.Infrastructure/Collection/FileCollectionReceiptHandoffStore.cs` |
| Prepared attempts | `src/CivicLens.Application/Collection/PreparedCollectionAttempt.cs`, `src/CivicLens.Application/Collection/ConfiguredCollectionSource.cs` |
| Pacing groups | `src/CivicLens.Application/Collection/PacingGroupConfiguration.cs`, `src/CivicLens.Application/Collection/CollectionConfiguration.cs` |
| Discovery admission | `src/CivicLens.Application/Collection/Discovery/DiscoveryAdmissionPolicy.cs`, `src/CivicLens.Application/Collection/Discovery/DiscoveryAdmissionBudget.cs`, `src/CivicLens.Application/Collection/Discovery/IDiscoveryAdmissionStore.cs` |
| Managed jobs | `src/CivicLens.Application/Collection/Jobs/RunCollectionJob.cs`, `src/CivicLens.Application/Collection/Jobs/CollectionJobLifecycle.cs`, `src/CivicLens.Application/Collection/Jobs/CollectionJobPolicy.cs`, `src/CivicLens.Application/Collection/Jobs/ICollectionJobStore.cs` |
| Engine and preparation | `src/CivicLens.Application/Collection/Jobs/CollectionJobWorker.cs`, `src/CivicLens.Application/Collection/Processing/EvidenceProcessingWorker.cs`, `src/CivicLens.Application/Collection/Processing/EvidenceProcessingLifecycle.cs`, `src/CivicLens.Application/Collection/Processing/EvidenceProcessingPolicy.cs`, `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Schedules | `src/CivicLens.Application/Collection/WatchedSourceConfiguration.cs`, `src/CivicLens.Application/Collection/Jobs/ICollectionScheduleStore.cs` |
| Process adapter and verification | `src/CivicLens.Infrastructure/Collection/CollectorProcess.cs`, `src/CivicLens.Infrastructure/Collection/CaptureArtifactVerifier.cs` |
| Postgres evidence | `src/CivicLens.Infrastructure/Collection/PostgresCollectionAttemptStore.cs`, `src/CivicLens.Infrastructure/Collection/CollectionAttemptDbContext.cs` |
| Postgres jobs | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/JobRowWriter.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/StoredJobDefinition.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionWorkerQueue.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionPipelineWakeup.cs` |
| Postgres processing | `src/CivicLens.Infrastructure/Collection/Processing/PostgresEvidenceProcessingStore.cs` |
| CLI and pages | `src/CivicLens.Host/Collection/CollectionWorkerCommand.cs`, `src/CivicLens.Host/Collection/CollectionJobCommand.cs`, `src/CivicLens.Host/Collection/CollectionRecoveryCommand.cs`, `src/CivicLens.Host/Collection/DiscoveryCommand.cs`, `src/CivicLens.Host/Collection/SourcesPage.razor` |
| Parser parity test | `tests/CivicLens.Tests/Infrastructure/Documents/HtmlParseParityTests.cs` |

The shared wakeup and lease mechanics are in [Shared worker mechanics](../architecture.md#shared-worker-mechanics). Commands are in [operations](../operations.md#managed-collection-jobs).

## Limits and planned work

- Unmanaged commands need operator coordination. The host serializes processes that share an artifact directory, but separate directories and standalone collectors are not coordinated.
- Candidate loading for a new 304 has no cutoff, so memory use can grow with the history of a source. Pagination or streaming must keep the all-candidates ambiguity rule.
- Orphan capture cleanup is future work. Successful handoffs are removed with no completed archive and no capture retention policy.
- The host does not replay stored validators automatically. Validators are explicit on the source or the manifest.
- Recovery does not establish machine power-loss durability.
- Unsupported handoff versions need an explicit future migration. They are never converted silently.
- Future wire changes need an explicit compatibility decision.
- Browser rendering and OCR are deferred until a selected source needs them.
- Planned engine policies: engine-wide spending ceilings, global backlog admission limits, weighted fairness for each source, live configuration reload, and a unified engine-health view. The current scheduler does not guarantee them. Broad coverage makes them MVP requirements.
- Agreed upgrade ([R-06](../roadmap.md#now)): stored generated columns for the source ID and URL of the job definition. Add an index for the latest job of each source, and one batched active-root check for each scheduling pass.
- Agreed upgrade ([R-08](../roadmap.md#now)): parallel evidence processing with a configured degree. It needs the lock change for comparison and extraction saves in [documents](documents.md#limits-and-planned-work) first.
- Agreed upgrade ([R-22](../roadmap.md#later)): ordered row locks in place of the single global lock of the job store. Measure lock waits on a bills-only collection run before the design.
- Fix the remaining format and parser limits that the federal coverage found ([R-30](../roadmap.md#next)).
- Planned: automatic discovery of official sources for a configured official, so that a new official needs no source entered by hand. Use the `fromDateTime` change check of the Congress.gov member list for it.
- Planned: estimate the change rate of each page from check history with a Poisson model, after Cho and Garcia-Molina. Set the check cadence from it within the collection budget. Read the original work before choosing the refresh policy.
- Storage must stay bounded at scale. The storage design is pending decision [D-04](../roadmap.md#pending-decisions).

Collection acceptance:

- [ ] Add an official through configuration only.
- [ ] Collect one feed and one watched page.
- [ ] Show cancellation and restart without duplicate imports.
- [ ] Show conditional 304 handling, 429 backoff, bounded discovery, and explicit incomplete-download errors.
- [ ] Validate registry behavior with hundreds of fixture identities, without hundreds of live requests.
- Live Auth0 tenant login for the Sources page still needs deployment verification.
