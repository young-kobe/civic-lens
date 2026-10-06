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

Standalone collection accepts `CivicLens.Collector collect <manifest.json>` (or `dotnet <collector.dll> collect <manifest.json>`). Version 3 request fields are defined by `CollectionRequest` in Collection.Contracts: `version`, `jobId`, `sourceId`, `url`, `allowedOrigin`, `allowedPathPrefix`, and absolute `artifactDirectory`, plus optional bounded `maxRequests`, `maxBytes`, `timeoutSeconds`, `minDelayMilliseconds`, `eTag`, and `lastModified`. JSON names are case-sensitive. Use version 3 in standalone manifests and rebuild host and collector together; version 1 and 2 collection messages are rejected. Registry configuration remains version 1. Receipts report applied conditional headers in `sentValidators`, using serialized ETags and whole-second UTC dates; the field is null when no conditional headers were applied to the last attempted content request. Validators are explicit in standalone manifests; the host does not yet automatically replay stored validators. A 304 emits `notModified` without a capture. A 429 emits `deferred`, with optional `retryAfterSeconds`; wait before rerunning. HTTP failures, robots denial/unavailability, budget exhaustion, incomplete bodies, and cancellation are explicit failures rather than source deletion.

Do not run concurrent collectors for the same origin. Host invocations sharing a capture directory are mutually exclusive, but separate directories and standalone invocations are not coordinated. Robots support and protocol limits are documented in [architecture](architecture.md). A killed child can leave `.capture-*.tmp`; these files are never valid captures and can be removed when no collector is running. Do not remove hash-named captures as temporary files. Use `collect-import` below for atomic evidence persistence. Use the receipt recovery commands below after an interrupted import. Durable jobs, retry scheduling, and cross-run pacing remain planned.

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

Every invocation creates a fresh attempt ID independent of its collector wire job ID. There is no attempt-ID override. Repeated identical content retains separate attempts and reuses the capture. Failed and deferred outcomes are imported too. The command does not automatically load prior validators or schedule retries.

Exit codes are 0 for imported captured/not-modified outcomes, 1 for failed/deferred collection or operational failure/cancellation, and 2 for invalid input/configuration. Only a returned import produces the JSON summary. On import failure or interruption, stderr reports that persistence was not confirmed: a lost commit acknowledgment can mean evidence was stored even though no success was printed. Retain the attempt ID for investigation. Re-running `collect-import` collects again with a new ID. Use `receipts replay` to retain the original attempt identity and import its saved receipt without another fetch. A confirmed import with failed handoff cleanup prints its summary with `handoffRemoved: false` and returns 1; replay remains safe. Provider error details and connection strings are not printed.

For other application composition, construct `PostgresCollectionAttemptStore` with `FromConnectionString`, or supply an `IDbContextFactory<CollectionAttemptDbContext>` directly. Call `MigrateAsync` explicitly before importing.

The design-time context factory reads `CIVIC_LENS_DATABASE` when supplied and otherwise uses a local design database name for offline migration generation. Keep credentials out of committed files and command output. Use EF tooling version 10.0.12 when generating future migrations for `src/CivicLens.Infrastructure`, with output under `Collection/Migrations`. Review generated migration operations and the snapshot together. The initial migration includes an explicit composite prior-capture foreign key that EF cannot represent without incorrectly making the principal capture field required for all outcomes; preserve and test that constraint in later schema changes.

Integration tests apply migrations against disposable Postgres instances and check for model drift. They verify exact evidence round trips, duplicate/conflicting and concurrent imports, capture-length conflicts, 304 ambiguity, and transaction cancellation/rollback. Receipt tests additionally exercise failed-import recovery, relocation, revalidation, uncertain commit replay, and cleanup failures. They do not establish machine power-loss durability or a production backup/restore procedure.

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

Runtime data belongs in ignored .runtime/; generated publication and evaluation artifacts in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. There is currently no deploy command and no public endpoint.

Core collection-import decisions and the Application fresh/replay handlers share evidence mapping and atomic Postgres imports. The host exposes receipt-only `collect`, database-backed `collect-import`, and explicit receipt recovery. Durable job execution and scheduling are the next checkpoint.

Durable jobs, review, backup/restore, publication, rollback, and MCP runbooks will be added here when implemented. Do not treat target architecture descriptions as executable procedures.
