# Operations

## Verification

Prerequisites: .NET SDK selected by global.json and a working Docker daemon for the full test suite. Python 3 is needed only to regenerate the architecture visual. Testcontainers creates disposable Postgres instances; no pre-existing database, model account, Node installation, or deployment credentials are needed.

```sh
dotnet restore --locked-mode
dotnet build CivicLens.slnx --configuration Release --no-restore
docker version
dotnet test CivicLens.slnx --configuration Release --no-build
dotnet format CivicLens.slnx --verify-no-changes --no-restore
python3 tools/render-architecture.py --check
```

For a database-free test run, use `dotnet test CivicLens.slnx --configuration Release --no-build --filter 'Category!=Postgres'`. This excludes the Postgres adapter and CLI database integration tests and is not a substitute for the full required check. Testcontainers requires Docker socket access; an agent sandbox may need an execution override for Docker and local test sockets. Missing Docker fails the full suite rather than silently skipping database checks. The Postgres image is pinned by tag and digest in the test fixture; its first run and the Testcontainers cleanup container may require image downloads.

`dotnet run --project src/CivicLens.Host -- status` describes implemented capabilities. `dotnet run --project src/CivicLens.Collector -- --help` describes the manifest-driven collector. Unsupported commands return nonzero exit codes.

Generate the self-contained architecture explorer with `python3 tools/render-architecture.py`; open artifacts/architecture.html in a browser. The HTML requires no remote assets or network access. The same script updates the Mermaid blocks in architecture.md. `--check` detects diagram drift without changing files.

## Dependency changes

Update explicit package versions, run `dotnet restore --force-evaluate`, review and commit packages.lock.json changes, then run the checks above. CI restores in locked mode. Update global.json deliberately when adopting an SDK patch or feature band.

## Watched-page tracer

Use the installed SDK on `PATH`. Build Release first. Validate the example registry without making requests:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- validate config/example.json
```

The example contains a fictitious official and one localhost watched page. Start a local source in one terminal:

```sh
mkdir -p .runtime/example-source
printf 'User-agent: *\nDisallow:\n' > .runtime/example-source/robots.txt
printf '<html><body>A local evidence example.</body></html>\n' > .runtime/example-source/page.html
python3 -m http.server 8765 --bind 127.0.0.1 --directory .runtime/example-source
```

In another terminal, collect through the real process boundary:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- collect config/example.json example-local-source src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

The host prints a verified JSON receipt. A captured result points to `capture.relativePath` (`<sha256>.gz`) relative to `.runtime/captures`; `gzip -dc .runtime/captures/<sha256>.gz` inspects the original response bytes. Repeating the command produces a new observation receipt but reuses an identical artifact. Receipts separate `capture` (hash, relative path and byte length) from `response` (status, validators, content type and ordered content encodings). The storage gzip envelope is separate from HTTP content encoding; decoding a gzip-encoded HTTP response requires both layers. The `collect` command does not import or persist receipts. Save stdout if needed for inspection; do not treat it as a durable job store. No database is needed for this command.

Edit configuration to add people or shared sources. A source URL occurs once, with multiple `personIds` when appropriate. `enabled: false` prevents collection without removing the configuration. `allowedOrigin` includes scheme and port; `allowedPathPrefix` permits that exact path and descendants. The local example explicitly permits loopback; this tool is for trusted local configuration, not arbitrary public fetch requests. Keep credentials out of source URLs and manifests.

Version 1 remains supported. For dated coverage use version 2, replace each source's `personIds` with `coverage`, and optionally add dated names to people. For example:

```json
{
  "version": 2,
  "people": [{ "id": "example-official", "name": "Example Official" }],
  "sources": [{
    "id": "example-local-source",
    "coverage": [{ "personId": "example-official", "startsOn": "2026-07-01", "endsBefore": "2027-07-01" }],
    "url": "http://127.0.0.1:8765/page.html",
    "allowedOrigin": "http://127.0.0.1:8765",
    "allowedPathPrefix": "/"
  }]
}
```

`startsOn` is inclusive and `endsBefore` exclusive; either may be omitted to leave that limit unspecified. A person's optional `names` array contains entries with `name` and the same optional date fields. Different names may overlap. Duplicate overlapping intervals for the same exact name or person/source are invalid; adjacent intervals are allowed. Coverage is our monitoring policy, not a claim about authorship, officeholding, or source ownership.

`collect`, `collect-import`, `jobs enqueue`, and `discovery admit` accept a trailing `--as-of yyyy-MM-dd`. Direct collection defaults to today's UTC date. New managed admissions default to the UTC database date inside their transaction. An enabled source must have coverage on the resolved date for new work. Repeating an existing key without `--as-of` returns its saved decision and date, even after coverage expires. Selecting a past date still fetches the current source; it does not request an archived page. For a reproducible managed admission:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/example.json example-local-source dated-check-1 --as-of 2026-07-01
```

New managed jobs retain `definition.configurationRevision` (hash ID and complete validated JSON snapshot) and `definition.coverageAsOf`, visible through `jobs get`. Discovery children retain the admission configuration revision. Repeating a job/batch key requires the same configuration revision. Omit `--as-of` to reuse its saved date, or supply the original date explicitly; a different explicit date conflicts. Replay checks the saved decision before current eligibility, while a new key still requires eligible coverage. Corrections or even array reorderings produce another revision. No database migration is needed for these additions. Pre-upgrade keys can replay matching execution settings even after current coverage expires or the source is disabled. They retain unknown provenance and do not acquire a later revision. Because legacy keys have no saved date, an explicit date is ignored on their replay and does not establish historical coverage.

Changing coverage prevents new admissions but does not cancel jobs already admitted under an earlier snapshot. Use `jobs cancel` for that. Keep stable people in configuration when ending coverage. Direct collection/import checks eligibility but retains no registry revision with its receipt; use managed jobs to retain that decision. Configuration snapshots include the full registry and source settings, so keep secrets out of configuration and treat job inspection output as local operational data.

Standalone collection accepts `CivicLens.Collector collect <manifest.json>` (or `dotnet <collector.dll> collect <manifest.json>`). Version 7 request fields are defined by `CollectionRequest` in Collection.Contracts: `version`, `jobId`, `sourceId`, `url`, `allowedOrigin`, `allowedPathPrefix`, and absolute `artifactDirectory`, plus optional bounded `maxRequests`, `maxBytes`, `timeoutSeconds`, `minDelayMilliseconds`, `eTag`, and `lastModified`. JSON names are case-sensitive. Use version 7 in new standalone manifests and rebuild host and collector together; version 1 and 2 collection messages are rejected. Registry configuration supports versions 1 and 2. Receipts report applied conditional headers in `sentValidators`, using serialized ETags and whole-second UTC dates; the field is null when no conditional headers were applied to the last attempted content request. Validators are explicit in standalone manifests; the host does not yet automatically replay stored validators. A 304 emits `notModified` without a capture. A 429 emits `deferred`, with optional `retryAfterSeconds`; wait before rerunning. HTTP failures, robots denial/unavailability, budget exhaustion, incomplete bodies, and cancellation are explicit failures rather than source deletion.

Do not run concurrent collectors for the same origin. Host invocations sharing a capture directory are mutually exclusive, but separate directories and standalone invocations are not coordinated. Robots support and protocol limits are documented in [architecture](architecture.md#robots-policy-and-pacing). Version 6 and 7 support Allow exceptions, bounded same-origin robots redirects, compressed robots files, and crawl-delay. Version 7 adds tolerant HTML recovery; version 6 preserves its earlier HTML parsing behavior. Cross-origin redirects and persistent robots caching are unsupported. Versions 3-5 remain executable with their legacy robots redirect and pacing behavior. A killed child can leave `.capture-*.tmp`; these files are never valid captures and can be removed when no collector is running. Do not remove hash-named captures as temporary files. Use `collect-import` below for atomic evidence persistence. Use the receipt recovery commands below after an interrupted import. Use managed jobs below for cross-run admission, retry budgets, and origin pacing.

## Postgres adapter and migrations

The Host exposes explicit schema migration and watched-page collection/import. Set `CIVIC_LENS_DATABASE` in your local environment to a Postgres connection string containing Host and Database, with authentication appropriate to your local server. Runtime commands require this variable and have no default database. Keep credentials out of committed files, command arguments, and captured output. The commands require a reachable Postgres server and sufficient database permissions; they do not install or start Postgres.

After setting the environment variable, run:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- db migrate
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- collect-import config/example.json example-local-source src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

Use the local HTTP source from the tracer example above. `db migrate` applies pending checked-in EF migrations and can be repeated. Imports never apply migrations. Capture files remain in the artifact directory; Postgres stores capture identities and immutable attempt evidence, not file bodies.

Before collection, stderr identifies the application-generated attempt ID. After successful import, stdout contains one JSON summary, for example:

```json
{"attemptId":"<generated-id>","outcome":"captured","importDisposition":"newAttempt","priorCaptureLinkStatus":"notApplicable","handoffRemoved":true}
```

Every invocation creates a fresh attempt ID independent of its collector wire job ID. There is no attempt-ID override. Repeated identical content retains separate attempts and reuses the capture. Failed and deferred outcomes are imported too. The command does not automatically load prior validators or schedule retries. Feed and HTML discovery sources also retain their discovery result atomically with the captured attempt.

Exit codes are 0 for imported captured/not-modified outcomes, 1 for failed/deferred collection or operational failure/cancellation, and 2 for invalid input/configuration. Only a returned import produces the JSON summary. On import failure or interruption, stderr reports that persistence was not confirmed: a lost commit acknowledgment can mean evidence was stored even though no success was printed. Retain the attempt ID for investigation. Re-running `collect-import` collects again with a new ID. Use `receipts replay` to retain the original attempt identity and import its saved receipt without another fetch. A confirmed import with failed handoff cleanup prints its summary with `handoffRemoved: false` and returns 1; replay remains safe. Provider error details and connection strings are not printed.

For other application composition, construct `PostgresCollectionAttemptStore` with `FromConnectionString`, or supply an `IDbContextFactory<CollectionAttemptDbContext>` directly. Call `MigrateAsync` explicitly before importing.

The design-time context factory reads `CIVIC_LENS_DATABASE` when supplied and otherwise uses a local design database name for offline migration generation. Keep credentials out of committed files and command output. Use EF tooling version 10.0.12 when generating future migrations for `src/CivicLens.Infrastructure`, with output under `Collection/Migrations`. Review generated migration operations and the snapshot together. The initial migration includes an explicit composite prior-capture foreign key that EF cannot represent without incorrectly making the principal capture field required for all outcomes; preserve and test that constraint in later schema changes.

Integration tests apply migrations against disposable Postgres instances and check for model drift. They verify exact evidence round trips, duplicate/conflicting and concurrent imports, capture-length conflicts, 304 ambiguity, and transaction cancellation/rollback. Receipt tests additionally exercise failed-import recovery, relocation, revalidation, uncertain commit replay, and cleanup failures. They do not establish machine power-loss durability or a production backup/restore procedure.

## Managed collection jobs

Apply `db migrate` first, including the additive `RobotsCrawlDelay` migration when upgrading. Rebuild host and collector together to use version 7. Version 6 retains its robots and pacing behavior, and versions 3-6 remain executable and recoverable. Job commands require `CIVIC_LENS_DATABASE`. Enqueue snapshots the selected source and policy; later runs need neither the original configuration nor an enabled entry in a changed configuration.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/example.json example-local-source check-2026-10-06
# Use the jobId returned above:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs get <job-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs list 20
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs cancel <job-id>
CIVIC_LENS_COLLECTION_CONFIG="$PWD/config/example.json" dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

Reuse an enqueue key only for the same configuration revision, source settings, policy, and any explicitly supplied coverage date; use a new key for a new logical check. `jobs run` reconciles old execution before making at most one new attempt. It returns immediately when a lease, global collector slot, origin barrier, origin deadline, or retry deadline blocks admission. Inspect `status`, `blockReason`, `retryAt`, and `job` in the JSON result; rerun when eligible. Run exits 0 only for a succeeded job, otherwise 1. Other job commands return 0 on success, 2 for invalid input or missing inspected jobs, and 1 for operational failures. Listing defaults to 20 jobs, maximum 100.

Optional source `jobPolicy` fields are `maxAttempts` (default 3), `initialRetryDelaySeconds` (30), `maximumRetryDelaySeconds` (3600), `maxTotalRequests`, `maxTotalBytes`, and `maxTotalTimeoutSeconds`. Omitted aggregate limits equal the corresponding per-attempt limit times `maxAttempts`. Limits must reserve at least one complete attempt. Source `eTag` and `lastModified` are optional explicit validators; automatic stored-validator lookup is not implemented. A server Retry-After can exceed the exponential delay cap. Version 6 robots 429/server/transport failures are deferred without attaching robots headers as content evidence. Declared crawl-delay extends shared origin pacing even after successful collection or recovery without a saved receipt. If the required wait cannot fit the attempt timeout, the collector defers with `crawlDelay`; it does not shorten the delay. Attempts remain budgeted, so a delay longer than every attempt timeout can exhaust retries. Inspect the delay and adjust the explicit work budget or source policy before admitting new work. Inspection includes immutable attempts and charged totals; the `reserved*` totals retain timeout reservations and unknown usage conservatively.

The CLI uses a 60-second renewable database-time lease. Persisted cancellation is observed on renewal (normally within 20 seconds) and stops future collection. Complete receipts remain recoverable. Ctrl+C does not permanently cancel the job. After interruption, rerun the same job with the complete artifact root, including `.pending`, or its relocated copy. A same-origin unresolved attempt blocks other managed jobs until reconciled. `jobs run` and the worker reconcile imported evidence even when its receipt handoff is missing; otherwise receipt replay remains available without fetching. No receipt and no imported evidence means unknown execution, charged at the full reserved budget.

`worker <collector.dll> <artifact-directory>` requires `CIVIC_LENS_COLLECTION_CONFIG` to name an absolute configuration path and runs the durable collection-to-review engine. It drains bounded batches, default 20 and maximum 100, then waits for committed work notifications or persisted source-check, retry, pacing, and lease deadlines. `--once` admits due configured checks and drains currently runnable work through downstream preparation, extraction, and comparison; it exits without waiting for future deadlines. Ctrl+C stops the worker. Successful configured source checks automatically queue one bounded article batch, extract readable text using retained profiles, and compare compatible consecutive observations. Repeated source checks can recheck known articles within that same batch budget. The worker resumes durable pending stages after restart. Enabled sources are checked every hour unless `checkIntervalSeconds` specifies another interval from 60 through 604800 seconds. On restart, unchanged schedules preserve their next due time, and missed intervals coalesce into one catch-up check. Active checks for the same source and exact root URL suppress another scheduled check. Disabled/removed sources pause future scheduling; coverage is checked at each due admission. Change configuration and restart the worker to apply it. Cancelling one job does not disable its source schedule. Keep one worker for an operational store; leases and fenced checkpoints protect ownership if another worker runs.

Managed jobs share one collector slot per operational store across artifact roots. Direct `collect`, `collect-import`, and standalone collector commands are outside this guarantee. A lost lease cannot guarantee that an old process has physically ceased HTTP; do not treat collection as exactly once. A stale runner can save a late receipt after its attempt was already settled as interrupted. Such a handoff remains available to `receipts replay`; later managed runs do not revisit already settled attempts. Back up the database and complete artifact roots together.

## Feed/HTML discovery and bounded article admission

Set a source's `mode` to `feed` to collect RSS 2.0 or Atom, or `html` to extract scoped anchor links from a listing page; omitted mode remains `page`. `maxCandidates` defaults to 100 and accepts 1 through 1,000. The allowed origin and path prefix must cover both the feed and intended article URLs. Add this synthetic source to `config/example.json` for the local HTTP server described above:

```json
{
  "id": "example-local-feed",
  "personIds": ["example-official"],
  "url": "http://127.0.0.1:8765/feed.xml",
  "allowedOrigin": "http://127.0.0.1:8765",
  "allowedPathPrefix": "/",
  "mode": "feed",
  "maxCandidates": 100,
  "admissionPolicy": {
    "maxJobs": 10,
    "maxTotalRequests": 150,
    "maxTotalBytes": 60000000,
    "maxTotalTimeoutSeconds": 900
  }
}
```

Create the feed in the same local server directory. This fixture is synthetic and contains no claims about an actual official:

```sh
printf '<rss version="2.0"><channel><title>Local fixture</title><item><link>/page.html</link></item></channel></rss>' > .runtime/example-source/feed.xml
```

Apply migrations, collect/import the feed, inspect the returned attempt ID, and explicitly admit its candidates:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- db migrate
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- collect-import config/example.json example-local-feed src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery get <attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery admit config/example.json example-local-feed <attempt-id> local-feed-batch-1
# Use a job ID from the admission result:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

`feeds get/admit` remain aliases for `discovery get/admit`. For HTML, use the same commands with a source configured as `mode: "html"` and a listing-page URL. Admission creates ordinary page jobs; fetching those pages does not generate more candidates.

Discovery collection also works through `jobs enqueue` and `jobs run`; inspect the job's attempts to obtain the evidence attempt ID. Successful HTTP capture does not guarantee valid discovery. `discovery get` reports `parsed`, `invalid`, `unsupported`, or `limitExceeded`, with URLs only after complete parsing. The raw capture remains inspectable in every captured case. Failed/deferred HTTP outcomes and 304s do not create candidate lists. For a 304, inspect the earlier captured discovery attempt.

`discovery admit` snapshots the current enabled source's page-job settings, clears its conditional validators, and checks that source, discovery mode, and scope match the retained discovery. Its limits reserve the full retry budget of every article job. Admission enqueues jobs and does not fetch them. The response reports jobs created, duplicate count, and deferred count. Repeating the same key and inputs returns the original result without creating jobs; changing inputs with the same key fails. A new key authorizes another bounded batch for remaining candidates. A source/URL already admitted stays deduplicated even if its job failed or was cancelled; inspect or recover that job instead. Existing admitted jobs run from their snapshot after configuration changes.

Candidates, capture identity, and attempt evidence commit atomically. If import fails, use the existing `receipts replay` commands; no new fetch is required. If admission acknowledgment is lost, repeat its exact key and configuration; omit the date to reuse the saved one, or supply that same date explicitly. Older v3 page, v4 page/feed, and v5 page/feed/HTML handoffs remain recoverable after upgrading; HTML discovery requires v5 or later. The migration renames existing discovery/admission tables while preserving their evidence, URL bindings, and batch replay records. Schema changes still require explicit `db migrate`.

## Federal source pilot

`config/federal-pilot.json` contains a deliberately small live-source configuration for Elizabeth Warren and Ted Cruz. It is a collection pilot, not representative national coverage. The coverage start date records when this project began monitoring these sources, not when either senator took office. Office press releases establish what an office published; claims about enacted legislation or completed government actions require corroborating authoritative records before publication.

| Source ID | Purpose |
|---|---|
| `elizabeth-warren-press` | Official newsroom listing; version 7 applies tolerant HTML recovery to its retained markup; refresh the capture and inspect candidates before admission |
| `ted-cruz-press` | Official newsroom listing; parsed candidates include navigation and pagination, so prefer the RSS source for article admission |
| `ted-cruz-press-feed` | RSS endpoint advertised by the official newsroom; bounded admission of linked article jobs |
| `elizabeth-warren-pilot-article-1` | Explicit education-oversight release; `capture-text-v2` with the `warren-article` profile extracts its retained capture |
| `elizabeth-warren-pilot-article-2` | Explicit heating-assistance release; `capture-text-v2` with the `warren-article` profile extracts its retained capture |

Each job permits at most two attempts, five requests and 2 MB per attempt, with a 60-second attempt timeout and at least 1.5 seconds between content requests. A discovery admission permits at most two article jobs and reserves their complete retry allowances: 20 requests, 8 MB, and 240 seconds. Another explicit admission authorizes another batch; these are not lifetime coverage caps. The worker is a separate explicit command and does not start with the Host.

Build both executables and configure `CIVIC_LENS_DATABASE` as above. For the already configured local development database, source the ignored `.runtime/database/connection.env` file. Start with the RSS source:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- validate config/federal-pilot.json
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/federal-pilot.json ted-cruz-press-feed <new-check-key>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery get <captured-attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery admit config/federal-pilot.json ted-cruz-press-feed <captured-attempt-id> <new-admission-key>
```

Inspect discovery before admission. Run each admitted job explicitly, then extract its captured article with the configured content profile:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <article-job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents extract config/federal-pilot.json <article-attempt-id> .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents get <extraction-id>
```

The `senate-main` profile retains the main article region and excludes navigation, modal, sharing, and previous/next controls. Inspect selected text before relying on a profile. Its exact revision is retained with each extraction; changing the profile does not rewrite old text. The `warren-article` profile selects the article template. Version 7 collection and `capture-text-v2` extraction use tolerant HTML recovery while retaining token, decoded-input, and DOM-depth limits.

The bounded pilot verified captures of both newsroom pages, the RSS feed, and two articles per office, with readable text retained for both offices. A version 7 worker pass captured the Warren listing and parsed 16 candidate links. Both retained Warren articles also extract successfully under `capture-text-v2`. These checks establish compatibility with those responses, not future source availability. Failed processing must not be interpreted as no activity.

First captures are baselines. Do not compare different articles to fabricate a change, or mistake a changed extraction profile for a source edit. A document-change candidate requires two compatible extractions of the same source and exact requested URL and a complete comparison containing changed text. The review queue can therefore remain empty while real evidence is already stored. Use the authenticated evidence viewer to inspect extraction IDs. No evidence is approved or published by collection.

## Recover saved receipts

`collect-import` saves a versioned handoff in `<artifact-directory>/.pending/` after the collector receipt and capture have been verified and before importing into Postgres. The directory also contains the hash-named gzip captures. Database outages and missing migrations leave the saved handoff available for replay. Missing or invalid database configuration is still rejected before collection.

List pending handoffs without a database, then replay one attempt or all pending attempts after setting `CIVIC_LENS_DATABASE` and applying migrations:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts list .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts replay .runtime/captures <attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts replay .runtime/captures --all
```

Listing returns a JSON array of pending identifiers, including entries whose contents may be invalid. Single replay prints the same import summary as `collect-import`. Batch replay writes one JSON result per entry, with `handoffId`, `status`, an optional `import` summary, and a sanitized `error`. It continues past individual failures and returns 1 if any recovery fails, cleanup fails, or a saved outcome is failed/deferred. Empty batches return 0. Cancellation stops the batch and returns reports for completed imports plus the interrupted entry when applicable; use listing to see remaining work.

Batch replay imports captured observations before 304s. If a captured import is unconfirmed or an envelope is unreadable, 304s stay pending with `prerequisiteUnconfirmed` until the missing evidence can be recovered. Other valid entries still proceed. A confirmed capture whose cleanup failed does not block 304s. This deliberately conservative rule can delay unrelated 304s in the same spool. Prefer `--all` when recovering related captures and 304s; single replay processes only the selected receipt and cannot account for other pending evidence. Existing stored 304 links are never rewritten.

Replay reads the saved request and receipt, validates their identity and supported versions, and rechecks referenced capture bytes. It never fetches a source or reads current coverage configuration. The saved attempt ID is reused, so a commit that succeeded before acknowledgment was lost becomes a duplicate import. Invalid receipts and missing or corrupted captures stay pending for inspection; no evidence is imported from them. Provider exception details are not printed.

After a confirmed import, the handoff is removed. If removal fails, the confirmed import remains valid and the handoff can be replayed safely. Capture files are never deleted by receipt cleanup. Fresh collection/import and recovery serialize within one spool, waiting up to 30 seconds for access before reporting failure; this does not implement cross-root host pacing.

To relocate runtime storage, move the complete capture directory including `.pending`, then pass its new path to listing/replay. The historical request retains its original artifact directory, while verification resolves hash-named captures beneath the selected new root. Back up pending handoffs together with their captures; do not edit envelope contents to relocate them or copy credentials into them.

Recovery begins only once a complete handoff has been saved. A crash or cancellation before that point may leave captures without a replayable receipt. Temporary `.tmp-*` handoff files are not replayable; remove them only when no collection or recovery operation is running. Process restart recovery does not establish machine power-loss durability. Unsupported handoff versions require an explicit future migration; they are not silently converted.

## Runtime and future operations

### Extract and cite document text

Apply `db migrate` for the additive `document_extractions` table, then use an imported captured attempt and its artifact directory:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents extract <captured-attempt-id> .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents get <extraction-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents cite <extraction-id> <start> <length>
```

Extraction returns the extraction ID, source attempt ID, processing versions, text hash, and text length. `get` returns the exact stored text and provenance. Citation offsets count UTF-16 units, matching .NET strings; an emoji represented by a surrogate pair counts as two units. The command rejects ranges that split that pair. Citation output is local evidence, not publication or approval.

Only captured HTML/plain-text attempts are supported. For a linked 304, explicitly select the original captured attempt. Repeating extraction verifies the artifact again and returns the same record for identical output; different output under the same processing versions and profile revision is rejected. A relocated artifact directory is supported. Missing/corrupt files, unsupported content, and parser limits fail without replacing earlier text. Inspection and citation can still use retained text if the capture is temporarily unavailable, but this does not replace backing up raw captures. Static HTML extraction may retain navigation or hidden text; it does not establish a substantive source change. See architecture for exact bounds and encoding rules.

### Configure versioned HTML content selection

Apply `db migrate` to add the nullable profile snapshot column. In configuration v2, declare reusable profiles and reference them from sources. This example targets synthetic local HTML containing `<main>` and a `.related-news` subtree:

```json
{
  "version": 2,
  "people": [{ "id": "example-official", "name": "Example Official" }],
  "documentProfiles": [{ "id": "policy", "selector": "main", "excludedSelectors": [".related-news"] }],
  "sources": [{
    "id": "example-local-source",
    "coverage": [{ "personId": "example-official" }],
    "url": "http://127.0.0.1:8765/page.html",
    "allowedOrigin": "http://127.0.0.1:8765",
    "allowedPathPrefix": "/",
    "documentProfileId": "policy"
  }]
}
```

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents extract <config.json> <captured-attempt-id> .runtime/captures
```

This command uses the profile assigned to the imported attempt's source ID. It reports the profile ID and revision alongside the extraction ID. Missing assignments, missing/ambiguous content regions, a profile that excludes its own root, and non-HTML content fail without falling back to whole-body extraction. Selectors support one tag name, `#id`, or `.class`; they do not support combinations, attributes, or pseudo-classes. Inspect output with `documents get` to verify the selected content before relying on a profile.

Changing selectors or exclusions produces a new immutable extraction identity even for the same capture. Earlier text, profiles, and citations remain readable; profiles are never retroactively attached to older records. Processing an already captured source is allowed after its coverage ends or it is disabled. Selection does not prove substantive change or grant publication approval. Version comparisons require matching processing settings on both sides.

### Inspect history and compare text

Run `db migrate` to apply `DocumentComparisons` and `DocumentHistoryLookupIndex`, then use:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents history <source-id> <exact-requested-url>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents compare <before-extraction-id> <after-extraction-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents comparison <comparison-id>
```

History retains checks and extraction revisions, groups consecutive equal text within each processing stream, and preserves reversions. Failed/missing evidence breaks grouping. Resolved 304 checks reuse the earlier captured extraction. Histories exceeding the documented observation, extraction, or text budgets fail explicitly; pagination is not yet implemented.

`compare` stores a deterministic result bound to both extractions and the algorithm/settings versions. It returns exit 0 for a complete comparison and exit 1 with a retained `incompatible` or `limitExceeded` result otherwise. `comparison` inspects that retained result and returns exit 0 when found. No hunks means unchanged only when status is `complete`. Text ranges use UTF-16 offsets; use `documents cite` with the corresponding extraction ID and positive range length to inspect exact quotations. A zero-length range denotes an insertion/deletion point, not a quotation.

When processing versions or profiles differ, explicitly rerun `documents extract` on both original captured attempt IDs using the same current settings/configuration, then compare those new extraction IDs. Earlier extractions, citations, and comparisons remain retained. These commands do not judge substantive significance, approve evidence, or publish anything.

### Inspect evidence in the local viewer

After configuring `CIVIC_LENS_DATABASE` and explicitly applying migrations, start:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- viewer
# Optional alternative port:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- viewer 5081
```

Open `http://127.0.0.1:5080/` (or the selected port). Use a source ID and exact requested URL to open history, an extraction ID from `documents extract` to inspect text, or a comparison ID from `documents compare` to inspect an already saved result. History links its extractions; comparisons link both extractions and exact changed-passage citations. You can also enter a UTF-16 start offset and positive length on an extraction page. Text selection alone does not calculate offsets in this initial viewer.

The viewer reads existing evidence only. Use the CLI for migrations, collection, extraction, and creation of comparisons. The viewer does not require a capture-directory argument because it reads retained extractions from Postgres. It preserves failed checks and missing evidence and reports history/comparison limits explicitly. Empty comparisons mean unchanged only when the saved status is complete. Empty histories do not establish source inactivity.

The listener binds only to `127.0.0.1`; ambient ASP.NET URL and Kestrel endpoint settings do not widen it. The process serves its own compiled pages and copied static assets even when launched from another working directory. Stop it with Ctrl+C. It is a local operational tool, not a public website or a multi-user authenticated service. No approvals or publication actions are available. Database outages and corrupt retained evidence produce a sanitized unavailable response; inspect database connectivity and applied migrations locally rather than exposing provider details in the browser.

### Authenticated document-change review

`review [port]` starts the separate editorial workspace on IPv4 loopback (default 5081). Unlike `viewer`, it supports draft and decision mutations and requires Auth0 configuration. It does not collect, migrate, publish, or manage access accounts through the UI. Apply `db migrate` explicitly before starting it; the additive `DocumentChangeReview` migration retains draft pointers, immutable revisions, decisions, and replay receipts.

Configure these environment variables through the deployment's protected environment/secret mechanism, not command-line arguments or committed files:

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_DATABASE` | Existing operational PostgreSQL connection string |
| `CIVIC_LENS_REVIEW_ORIGIN` | Exact external HTTPS origin, without a path, query, or credentials |
| `CIVIC_LENS_AUTH0_AUTHORITY` | Auth0 tenant/custom-domain HTTPS origin |
| `CIVIC_LENS_AUTH0_CLIENT_ID` | Regular Web Application client ID |
| `CIVIC_LENS_AUTH0_CLIENT_SECRET` | Auth0 client secret |
| `CIVIC_LENS_REVIEW_OWNER` | Exact Auth0 `sub` of the owner |
| `CIVIC_LENS_REVIEW_REVIEWERS` | Optional comma-separated additional reviewer subject IDs |
| `CIVIC_LENS_REVIEW_KEY_DIRECTORY` | Absolute persistent directory for ASP.NET data-protection keys |
| `CIVIC_LENS_REVIEW_ISSUES` | Optional comma-separated configured issue IDs |
| `CIVIC_LENS_REVIEW_OFFICIALS` | Optional comma-separated configured official IDs |
| `CIVIC_LENS_COLLECTION_CONFIG` | Optional absolute path to the collection configuration for the owner Sources page |
| `CIVIC_LENS_CAPTURE_DIRECTORY` | Optional absolute artifact root for collection and extraction in the Sources page; configure with the collection config |

Set both collection variables or leave both unset. Setting only one prevents the authenticated application from starting because it cannot safely configure the Sources page.

Set the Auth0 allowed callback URL to `<CIVIC_LENS_REVIEW_ORIGIN>/signin-oidc`. Configure an HTTPS reverse proxy on the same host to forward to loopback and preserve the configured external Host header, including its port if nonstandard. The workspace rejects other Host values, pins its effective scheme to HTTPS, ignores ambient listener configuration, and does not trust forwarded headers. The initial listener expects a same-host proxy; isolated container networking requires an explicitly reviewed deployment configuration. Public access must terminate HTTPS at the proxy. Local workstation testing also requires a local HTTPS proxy and an allowed Auth0 callback; there is no unauthenticated review mode.

The application accepts only configured subjects, even if other users can authenticate at Auth0. Both owner and reviewer may draft, self-approve, request changes, and withdraw an effective approval. Publishing and access-management UI are not implemented. Change the server subject configuration and restart to change access; previously issued cookies are rechecked against that configuration. Signing out clears the application session; it does not end the Auth0 identity-provider session. Cookies are secure, HttpOnly, non-sliding, and expire after eight hours. Keys persist across restarts; on Unix the application restricts the key directory to its owner. Keys are not encrypted by the application at rest, so protect the persistent volume and its backups as credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- review 5081
```

Open `/Review` through the configured HTTPS origin. The inbox lists saved changed comparisons and drafts. With collection settings configured, `/Review/Sources` offers the owner Check, recent activity, and Review for prepared changes. Keep the separate `worker` running: it collects articles, extracts readable text, and prepares comparisons automatically. Activity refreshes while the page is visible; connection failures show a reconnect notice. A baseline or unchanged observation does not enter review. Manual cancellation, admission, extraction, comparison, and article recollection remain under troubleshooting. The page accepts configured sources and retained article URLs only, contains no demonstration records, and does not claim worker liveness. `/Evidence` opens the authenticated supporting lookup. The old `viewer` command cannot access review routes.

Creating a draft explicitly selects a complete changed comparison. Saves retain immutable authored revisions. Actual change dates require a selected exact citation; dates of observation are displayed separately. Approval requires headline, summary, institution, at least one valid citation, and explicit resolution of all outstanding concerns. Concurrent saves/decisions return conflicts; stale unsaved edits remain visible, and decisions are disabled until the saved revision is reloaded or edits are saved. Review decisions do not publish anything.

The editor shows the saved document-change account and its exact selected passages as the approval target, separately from editable or recovered unsaved wording. Reviewers judge substantive change, wording, attribution, dates, interpretation, and evidence limits; automatic citation and revision checks establish integrity, not semantic correctness. Approved accounts remain private until the owner publishes them. Approval does not designate a golden label or a model training example.

History currently supports at most 64 revisions and 128 decisions per record, with 64 outstanding concerns and 64 citations per revision. Limit exhaustion rejects writes and requires future history-pagination work; do not discard history to continue. Browsing comparisons may return an empty batch with a continuation if the batch contains only unchanged/incompatible/limited results. Use the continuation rather than treating that batch as an exhaustive absence of changes.

Auth0 tenant login/logout and the production reverse proxy still require live verification. Offline tests validate subject authorization and HTTP workflows with isolated test signing keys; they do not exercise a live Auth0 tenant. Hetzner/Terraform provisioning, worker service management, production backup restoration, and a workspace publish action remain pending.

### Publish a static release

Publication is a local owner command. Set `CIVIC_LENS_DATABASE`, `CIVIC_LENS_REVIEW_OWNER` (the owner subject recorded on the release), and `CIVIC_LENS_RELEASE_DIRECTORY` (an absolute path, for example `$PWD/.runtime/releases`). Publishing also needs `CIVIC_LENS_COLLECTION_CONFIG`, an absolute configuration path that supplies officials' display names. The command trusts the local operator, who already holds database credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases publish <idempotency-key> <draft-id> [<draft-id>...]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases list [limit]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases activate <release-number>
```

`publish` adds or replaces each selected record's current revision. The revision must be approved with no unresolved concerns. The new release also keeps every record from the previous release as hard links to that release's files. Disk grows with published records, not with the number of releases. The command builds the release in `staging/`, moves it to a unique directory under `releases/`, records it, and then points the relative `current` link at it. A concurrent review decision, draft save, or other release makes the commit fail with exit code 1; the built directory is deleted and nothing is activated. Repeat the same key and drafts to receive the original result. If activation fails after the commit, run `releases activate` with the reported number. To roll back, activate an earlier release number. The next `publish` builds on the active release, so records added after it are not carried forward. Releases are never deleted automatically.

To check that the public pages work without the operational application, stop the review workspace, worker, and Postgres, then serve the active release with a plain static server:

```sh
python3 -m http.server 8090 --bind 127.0.0.1 --directory "$CIVIC_LENS_RELEASE_DIRECTORY/current"
```

Open `http://127.0.0.1:8090/`. The index lists the published records; each record page shows the account, the changes, the citations, and both full document versions. The production static server remains a deployment decision.

### Remaining operations

Runtime data belongs in ignored .runtime/; generated publication and evaluation artifacts in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. There is currently no deploy command and no public endpoint. Published releases live under the configured release directory, not in the repository.

Core collection-import decisions and the Application fresh/replay handlers share evidence mapping and atomic Postgres imports. The host exposes receipt-only `collect`, database-backed `collect-import`, and explicit receipt recovery. Managed jobs add explicit lifecycle execution; background scheduling remains planned.

Production backup/restore and MCP runbooks will be added here when implemented. Do not treat target architecture descriptions as executable procedures.
