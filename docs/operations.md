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

Standalone collection accepts `CivicLens.Collector collect <manifest.json>` (or `dotnet <collector.dll> collect <manifest.json>`). Version 6 request fields are defined by `CollectionRequest` in Collection.Contracts: `version`, `jobId`, `sourceId`, `url`, `allowedOrigin`, `allowedPathPrefix`, and absolute `artifactDirectory`, plus optional bounded `maxRequests`, `maxBytes`, `timeoutSeconds`, `minDelayMilliseconds`, `eTag`, and `lastModified`. JSON names are case-sensitive. Use version 6 in new standalone manifests and rebuild host and collector together; version 1 and 2 collection messages are rejected. Registry configuration supports versions 1 and 2. Receipts report applied conditional headers in `sentValidators`, using serialized ETags and whole-second UTC dates; the field is null when no conditional headers were applied to the last attempted content request. Validators are explicit in standalone manifests; the host does not yet automatically replay stored validators. A 304 emits `notModified` without a capture. A 429 emits `deferred`, with optional `retryAfterSeconds`; wait before rerunning. HTTP failures, robots denial/unavailability, budget exhaustion, incomplete bodies, and cancellation are explicit failures rather than source deletion.

Do not run concurrent collectors for the same origin. Host invocations sharing a capture directory are mutually exclusive, but separate directories and standalone invocations are not coordinated. Robots support and protocol limits are documented in [architecture](architecture.md#robots-policy-and-pacing). Version 6 supports Allow exceptions, bounded same-origin robots redirects, compressed robots files, and crawl-delay. Cross-origin redirects and persistent robots caching are unsupported. Versions 3-5 remain executable with their legacy robots redirect and pacing behavior. A killed child can leave `.capture-*.tmp`; these files are never valid captures and can be removed when no collector is running. Do not remove hash-named captures as temporary files. Use `collect-import` below for atomic evidence persistence. Use the receipt recovery commands below after an interrupted import. Use managed jobs below for cross-run admission, retry budgets, and origin pacing.

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

Apply `db migrate` first, including the additive `RobotsCrawlDelay` migration when upgrading. Rebuild host and collector together to use version 6. Job commands require `CIVIC_LENS_DATABASE`. Enqueue snapshots the selected source and policy; later runs need neither the original configuration nor an enabled entry in a changed configuration.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/example.json example-local-source check-2026-10-06
# Use the jobId returned above:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs get <job-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs list 20
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs cancel <job-id>
```

Reuse an enqueue key only for the same configuration revision, source settings, policy, and any explicitly supplied coverage date; use a new key for a new logical check. `jobs run` reconciles old execution before making at most one new attempt. It returns immediately when a lease, global collector slot, origin barrier, origin deadline, or retry deadline blocks admission. Inspect `status`, `blockReason`, `retryAt`, and `job` in the JSON result; rerun when eligible. Run exits 0 only for a succeeded job, otherwise 1. Other job commands return 0 on success, 2 for invalid input or missing inspected jobs, and 1 for operational failures. Listing defaults to 20 jobs, maximum 100.

Optional source `jobPolicy` fields are `maxAttempts` (default 3), `initialRetryDelaySeconds` (30), `maximumRetryDelaySeconds` (3600), `maxTotalRequests`, `maxTotalBytes`, and `maxTotalTimeoutSeconds`. Omitted aggregate limits equal the corresponding per-attempt limit times `maxAttempts`. Limits must reserve at least one complete attempt. Source `eTag` and `lastModified` are optional explicit validators; automatic stored-validator lookup is not implemented. A server Retry-After can exceed the exponential delay cap. Version 6 robots 429/server/transport failures are deferred without attaching robots headers as content evidence. Declared crawl-delay extends shared origin pacing even after successful collection or recovery without a saved receipt. If the required wait cannot fit the attempt timeout, the collector defers with `crawlDelay`; it does not shorten the delay. Attempts remain budgeted, so a delay longer than every attempt timeout can exhaust retries. Inspect the delay and adjust the explicit work budget or source policy before admitting new work. Inspection includes immutable attempts and charged totals; the `reserved*` totals retain timeout reservations and unknown usage conservatively.

The CLI uses a 60-second renewable database-time lease. Persisted cancellation is observed on renewal (normally within 20 seconds) and stops future collection. Complete receipts remain recoverable. Ctrl+C does not permanently cancel the job. After interruption, rerun the same job with the complete artifact root, including `.pending`, or its relocated copy. A same-origin unresolved attempt blocks other managed jobs until reconciled. Use `jobs run` to reconcile its job; standalone receipt replay imports evidence but does not settle jobs. No receipt and no imported evidence means unknown execution, charged at the full reserved budget. There is no automatic polling worker.

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

Run `db migrate` to apply `DocumentComparisons`, then use:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents history <source-id> <exact-requested-url>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents compare <before-extraction-id> <after-extraction-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents comparison <comparison-id>
```

History retains checks and extraction revisions, groups consecutive equal text within each processing stream, and preserves reversions. Failed/missing evidence breaks grouping. Resolved 304 checks reuse the earlier captured extraction. Histories exceeding the documented observation, extraction, or text budgets fail explicitly; pagination is not yet implemented.

`compare` stores a deterministic result bound to both extractions and the algorithm/settings versions. It returns exit 0 for a complete comparison and exit 1 with a retained `incompatible` or `limitExceeded` result otherwise. `comparison` inspects that retained result and returns exit 0 when found. No hunks means unchanged only when status is `complete`. Text ranges use UTF-16 offsets; use `documents cite` with the corresponding extraction ID and positive range length to inspect exact quotations. A zero-length range denotes an insertion/deletion point, not a quotation.

When processing versions or profiles differ, explicitly rerun `documents extract` on both original captured attempt IDs using the same current settings/configuration, then compare those new extraction IDs. Earlier extractions, citations, and comparisons remain retained. These commands do not judge substantive significance, approve evidence, or publish anything.

### Remaining operations

Runtime data belongs in ignored .runtime/; generated publication and evaluation artifacts in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. There is currently no deploy command and no public endpoint.

Core collection-import decisions and the Application fresh/replay handlers share evidence mapping and atomic Postgres imports. The host exposes receipt-only `collect`, database-backed `collect-import`, and explicit receipt recovery. Managed jobs add explicit lifecycle execution; background scheduling remains planned.

Review, backup/restore, publication, rollback, and MCP runbooks will be added here when implemented. Do not treat target architecture descriptions as executable procedures.
