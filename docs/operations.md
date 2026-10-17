# Operations

This page holds commands and procedures. Design rules are in the [architecture](architecture.md) lookup table.

## Verification

Prerequisites: the .NET SDK selected by global.json and a working Docker daemon for the full test suite. Python 3 runs the development tools in `tools/`. Testcontainers creates disposable Postgres instances. No existing database, model account, Node installation, or deployment credentials are needed.

```sh
dotnet restore --locked-mode
dotnet build CivicLens.slnx --configuration Release --no-restore
docker version
dotnet test CivicLens.slnx --configuration Release --no-build
dotnet format CivicLens.slnx --verify-no-changes --no-restore
python3 tools/render-architecture.py --check
python3 tools/check-docs.py --check
```

Tests never call the paid Claude API. They use a fake model at each drafting port. The live tests, in category `LiveAi`, are skipped unless you set `CIVIC_LENS_LIVE_AI=1` and `ANTHROPIC_API_KEY`. CI does not set them. Each live test sends paid requests, so run them on request only:

```sh
CIVIC_LENS_LIVE_AI=1 ANTHROPIC_API_KEY=... dotnet test CivicLens.slnx --configuration Release --no-build --filter 'Category=LiveAi'
```

For a database-free test run, use `dotnet test CivicLens.slnx --configuration Release --no-build --filter 'Category!=Postgres'`. This leaves out the Postgres adapter and CLI database integration tests. It does not replace the full required check. Testcontainers needs Docker socket access, so an agent sandbox may need an execution override for Docker and local test sockets. Missing Docker fails the full suite; it never skips database checks silently. The test fixture pins the Postgres image by tag and digest. Its first run and the Testcontainers cleanup container may download images.

`dotnet run --project src/CivicLens.Host -- status` describes implemented capabilities. `dotnet run --project src/CivicLens.Collector -- --help` describes the manifest-driven collector. Unsupported commands return nonzero exit codes.

Generate the self-contained architecture explorer with `python3 tools/render-architecture.py`, then open artifacts/architecture.html in a browser. The HTML needs no remote assets or network access. The same script updates the Mermaid blocks in architecture.md. `--check` detects diagram drift without changing files. `python3 tools/check-docs.py --check` checks repository paths, local links, and sentence length in the core docs.

## Local development

The Makefile wraps the local commands. Run `make help` to list them. It reads `.runtime/database/connection.env`, `.runtime/database/postgres.env`, and `.runtime/review/connection.env`. Only the worker reads `.runtime/congress/api.env`, which sets `CIVIC_LENS_CONGRESS_API_KEY`, when that file exists. The Makefile uses the Caddy binary in `.runtime/tools/caddy`. These files are local and ignored by Git. `make review` builds, starts Postgres, applies migrations, and runs the HTTPS proxy, review workspace, and worker. Set `DOTNET` when the SDK is not on `PATH`.

## Dependency changes

Update explicit package versions, run `dotnet restore --force-evaluate`, review and commit packages.lock.json changes, then run the checks above. CI restores in locked mode. global.json pins the exact SDK with no roll forward, so CI and local builds use the same SDK. Update global.json deliberately when you adopt an SDK patch or feature band. An SDK update can change implicit package versions, so run `dotnet restore --force-evaluate` after it and commit the lock file changes.

## Configuration

Validate a registry without making requests:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- validate config/example.json
```

Edit configuration to add people or shared sources. A source URL occurs once, with several `coverage` entries when needed. `enabled: false` prevents collection without removing the configuration. `allowedOrigin` includes the scheme and port. `allowedPathPrefix` permits that exact path and its descendants. The local example permits loopback explicitly. This tool is for trusted local configuration, not arbitrary public fetch requests. Keep credentials out of source URLs and manifests.

Configuration version 2 is the only supported version. For example:

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

`startsOn` is inclusive and `endsBefore` is exclusive. Either can be left out. A person's optional `names` array holds entries with `name` and the same optional date fields. A person may also have a `bioguideId`, the Congress.gov member ID, for example `W000817`. The field rules are in [configuration](architecture/configuration.md#people-and-sources).

`collect`, `collect-import`, `jobs enqueue`, and `discovery admit` accept a trailing `--as-of yyyy-MM-dd`. Selecting a past date still fetches the current source; it does not request an archived page. For a reproducible managed admission:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/example.json example-local-source dated-check-1 --as-of 2026-07-01
```

`jobs get` shows `definition.configurationRevision` (the hash ID and the validated JSON snapshot) and `definition.coverageAsOf`. To repeat a job or batch key, use the same configuration revision. Leave out `--as-of` to reuse the saved date, or give the original date; another date conflicts. The replay rules are in [configuration](architecture/configuration.md#admission-replay).

Changing coverage stops new admissions but does not cancel admitted jobs. Use `jobs cancel` for that. Keep stable people in configuration when coverage ends. Use managed jobs when the retained coverage decision matters, because direct collection keeps no revision. Snapshots hold the full registry and source settings. Keep secrets out of configuration, and treat job inspection output as local operational data.

## Watched-page tracer

Use the installed SDK on `PATH`. Build Release first. The example holds a fictitious official and one localhost watched page. Start a local source in one terminal:

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

The host prints a verified JSON receipt. A captured result points to `capture.relativePath` (`<sha256>.gz`) relative to `.runtime/captures`. `gzip -dc .runtime/captures/<sha256>.gz` shows the original response bytes. To decode a gzip-encoded HTTP response, remove both layers. A repeated command gives a new receipt and reuses an identical artifact. `collect` needs no database and persists nothing. Save stdout if you need it, but do not treat it as a durable job store.

## Standalone collector

Run `CivicLens.Collector collect <manifest.json>` or `dotnet <collector.dll> collect <manifest.json>`. `CollectionRequest` in Collection.Contracts defines the version 10 fields:

- required: `version`, `jobId`, `sourceId`, `url`, `allowedOrigin`, `allowedPathPrefix`, and an absolute `artifactDirectory`
- optional, bounded: `maxRequests`, `maxBytes`, `timeoutSeconds`, `minDelayMilliseconds`, `eTag`, `lastModified`
- for `api` mode only: `credential`

JSON names are case-sensitive. Use version 10 in new manifests, and rebuild the host and collector together. The collector rejects every other version. Validators are explicit in standalone manifests. A 429 gives `deferred` with an optional `retryAfterSeconds`; wait before you run again. The protocol rules are in [collection](architecture/collection.md#collector-protocol).

Do not run concurrent collectors for the same origin. Host runs that share a capture directory exclude each other, but separate directories and standalone runs are not coordinated. Use managed jobs for cross-run admission, retry budgets, and origin pacing.

The collector caches robots rules for 24 hours in `robots-*.json` files in the artifact directory. To force a refetch, delete the file for that origin. Do not delete other files. A killed child can leave `.capture-*.tmp` or `.robots-*.tmp` files. They are never valid captures or cache entries. Remove them only when no collector runs. Never remove hash-named captures as temporary files.

### API credential

A source with mode `api` may name a `credential`. The only name is `congressGov`, and its key is in `CIVIC_LENS_CONGRESS_API_KEY`. Get a free key at api.congress.gov/sign-up. Set the variable in the protected environment of the process that starts the collector, such as the worker. Never put the key in configuration, manifests, command arguments, or committed files. A missing or blank variable fails the check with `credentialUnavailable`.

### Congress.gov bill stream

The source `congress-bills` in `config/federal-coverage.json` and `config/federal-pilot.json` follows the Congress.gov bill list. It ships with `enabled: false`. To enable it:

1. Get a key at api.congress.gov/sign-up.
2. Set `CIVIC_LENS_CONGRESS_API_KEY` in the protected environment of the worker. Do not put the key in configuration.
3. Set `CIVIC_LENS_ISSUES_CONFIG` for the worker. With an enabled bill source and no issue file, the worker refuses to start and exits with code 2.
4. Set `enabled` to `true` for `congress-bills` and restart the worker. A restart reads the configuration again.

The backfill sends many requests to Congress.gov, paced by `minDelayMilliseconds`, so it takes some time. [Bills](architecture/bills.md) holds the window, cursor, and gate rules.

Configuration loading rejects a bill list source whose `admissionPolicy` cannot hold the [251-job rule](architecture/bills.md#admission). Set `maxJobs` and the request, byte, and time totals to fit it.

To list bills whose gate is still pending because a cosponsor page job is blocked, run this query:

```sql
SELECT g.detail_job_id, g.source_id, g.congress, g.bill_type, g.bill_number,
       e.job_id AS blocked_job_id, e.error_code
FROM bill_gates g
JOIN bill_follow_on_jobs l ON l.detail_job_id = g.detail_job_id
JOIN collection_jobs j ON j.job_id = l.job_id
JOIN evidence_processing e ON e.job_id = l.job_id
WHERE g.decision = 'Pending'
  AND j.definition_json::jsonb ->> 'apiResource' = 'billCosponsors'
  AND e.status = 'Blocked'
ORDER BY g.congress, g.bill_type, g.bill_number;
```

To list passed bills that have no action candidate because a cosponsor or actions page job failed or is blocked, run this query. A failed summaries page does not block a candidate, so the query skips it.

```sql
SELECT g.detail_job_id, g.source_id, g.congress, g.bill_type, g.bill_number,
       j.job_id AS stuck_job_id, j.definition_json::jsonb ->> 'apiResource' AS resource,
       j.state, e.status AS processing_status, e.error_code
FROM bill_gates g
JOIN bill_follow_on_jobs l ON l.detail_job_id = g.detail_job_id
JOIN collection_jobs j ON j.job_id = l.job_id
LEFT JOIN action_candidates c ON c.detail_job_id = g.detail_job_id
LEFT JOIN bill_pages p ON p.job_id = l.job_id
LEFT JOIN evidence_processing e ON e.job_id = l.job_id
WHERE g.decision = 'Passed'
  AND c.candidate_id IS NULL
  AND p.job_id IS NULL
  AND j.definition_json::jsonb ->> 'apiResource' IN ('billCosponsors', 'billActions')
  AND (j.state IN ('Failed', 'Cancelled') OR e.status IN ('Blocked', 'Failed'))
ORDER BY g.congress, g.bill_type, g.bill_number;
```

### Congress.gov House vote stream

The source `congress-house-votes` in `config/federal-coverage.json` and `config/federal-pilot.json` follows the House roll-call votes of the Congress.gov `house-vote` API. It ships with `enabled: false`. To enable it:

1. Get a key at api.congress.gov/sign-up.
2. Set `CIVIC_LENS_CONGRESS_API_KEY` in the protected environment of the worker. Do not put the key in configuration.
3. Set `enabled` to `true` for `congress-house-votes` and restart the worker. A restart reads the configuration again.

The source needs no issue file. Votes on a bill draft only when the `congress-bills` source is enabled and has saved the detail of that bill. Each day it reads the whole vote list of every session from `backfillFrom` on, about 4 list pages a day today. The first day admits one detail job and one members job for every roll call. [Votes](architecture/votes.md) holds the sweep and candidate rules.

Configuration loading rejects a vote source whose `admissionPolicy` cannot hold the [251-job rule](architecture/votes.md#admission). Set `maxJobs` and the request, byte, and time totals to fit it.

To list vote versions that have a detail but no candidate yet, run this query. A row can be a members job that is still pending, failed, or blocked:

```sql
SELECT v.detail_job_id, v.congress, v.session_number, v.roll_call_number
FROM house_votes v
LEFT JOIN house_vote_candidates c ON c.detail_job_id = v.detail_job_id
WHERE c.detail_job_id IS NULL
ORDER BY v.congress, v.session_number, v.roll_call_number;
```

## Postgres adapter and migrations

Set `CIVIC_LENS_DATABASE` in your local environment to a Postgres connection string with Host and Database, and with authentication for your local server. Runtime commands need this variable and have no default database. Keep credentials out of committed files, command arguments, and captured output. The commands need a reachable Postgres server and enough database permissions. They do not install or start Postgres.

After you set the variable, run:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- db migrate
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- collect-import config/example.json example-local-source src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

Use the local HTTP source from the tracer example. `db migrate` applies all pending checked-in EF migrations and can be repeated. Imports never apply migrations. Capture files stay in the artifact directory.

Before collection, stderr shows the application-generated attempt ID. After a successful import, stdout holds one JSON summary, for example:

```json
{"attemptId":"<generated-id>","outcome":"captured","importDisposition":"newAttempt","priorCaptureLinkStatus":"notApplicable","handoffRemoved":true}
```

Exit codes are 0 for imported captured or not-modified outcomes. They are 1 for failed or deferred collection, an operational failure, a cancellation, or a confirmed import whose handoff cleanup failed. They are 2 for invalid input or configuration. Only a returned import prints the JSON summary. A confirmed import with failed cleanup prints `handoffRemoved: false`; replay stays safe.

On an import failure or interruption, stderr reports that persistence was not confirmed. Evidence may still be stored. Keep the attempt ID for investigation. Running `collect-import` again collects again with a new ID. Use `receipts replay` to keep the original attempt identity and import its saved receipt without another fetch.

For other application composition, build `PostgresCollectionAttemptStore` with `FromConnectionString`, or supply an `IDbContextFactory<CollectionAttemptDbContext>`. Call `MigrateAsync` explicitly before you import.

The design-time context factory reads `CIVIC_LENS_DATABASE` when it is set. Otherwise it uses a local design database name for offline migration generation. Use EF tooling version 10.0.12 to generate migrations for `src/CivicLens.Infrastructure`, with output under `Collection/Migrations`. Review the generated operations and the snapshot together. Keep and test the explicit composite prior-capture foreign key in later schema changes.

After a schema change, regenerate the schema reference in `docs/architecture/schema/` and commit it. Place each new table in the feature map of `SchemaReferenceTests` first.

```sh
CIVIC_LENS_WRITE_SCHEMA_DOCS=1 dotnet test CivicLens.slnx --configuration Release --no-build --filter FullyQualifiedName~SchemaReferenceTests
```

## Managed collection jobs

Apply `db migrate` first, also when you upgrade. Rebuild the host and collector together. Job commands need `CIVIC_LENS_DATABASE`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/example.json example-local-source check-2026-10-06
# Use the jobId returned above:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs get <job-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs list 20
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs cancel <job-id>
CIVIC_LENS_COLLECTION_CONFIG="$PWD/config/example.json" dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

Reuse an enqueue key only for the same configuration revision, source settings, policy, and any explicit coverage date. Use a new key for a new logical check.

`jobs run` returns at once when a lease, the global collector slot, an origin barrier, an origin deadline, or a retry deadline blocks admission. Inspect `status`, `blockReason`, `retryAt`, and `job` in the JSON result, and run again when eligible. `jobs run` exits 0 only for a succeeded job, otherwise 1. Other job commands return 0 on success, 2 for invalid input or a missing job, and 1 for operational failures. `jobs list` defaults to 20 jobs, with a maximum of 100.

Optional source `jobPolicy` fields are `maxAttempts` (default 3), `initialRetryDelaySeconds` (30), `maximumRetryDelaySeconds` (3600), `maxTotalRequests`, `maxTotalBytes`, and `maxTotalTimeoutSeconds`. Omitted aggregate limits equal the per-attempt limit times `maxAttempts`. Limits must reserve at least one complete attempt. Source `eTag` and `lastModified` are optional explicit validators.

A crawl delay longer than every attempt timeout can exhaust the retries. Inspect the delay, then adjust the work budget or the source policy before you admit new work. Inspection shows immutable attempts and charged totals. The `reserved*` totals keep timeout reservations and unknown use conservatively.

**Interruption.** Ctrl+C does not cancel the job permanently. After an interruption, run the same job again with the complete artifact root, including `.pending`, or its relocated copy. A same-origin unresolved attempt blocks other managed jobs until it is reconciled. `jobs run` and the worker reconcile imported evidence even when its handoff is missing. Otherwise `receipts replay` imports without a fetch.

**Worker.** `worker <collector.dll> <artifact-directory>` needs `CIVIC_LENS_COLLECTION_CONFIG` with an absolute configuration path. It runs the durable collection-to-review engine. `--once` admits due checks, drains the runnable work, and exits without waiting for future deadlines. Ctrl+C stops the worker. Change the configuration and restart the worker to apply it. Cancelling one job does not disable its source schedule. Keep one worker for each operational store. Leases and fenced checkpoints protect ownership if another worker runs.

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_EVIDENCE_CONCURRENCY` | Optional number of captured pages processed at the same time (default 2, 1 to 4). Any other value stops the worker with exit code 2 |

Enabled sources are checked every hour unless `checkIntervalSeconds` sets another interval from 60 to 604800 seconds. The engine rules are in [collection](architecture/collection.md#autonomous-collection-engine).

**Pacing groups.** Optional top-level `pacingGroups` limit the total load on shared hosts. For example, `{"hostSuffix": "house.gov", "minDelayMilliseconds": 3000}` spaces all managed jobs for any `*.house.gov` origin by at least 3 seconds. This adds to the pacing of each origin. If the queue keeps growing, lower the delay. The group rules are in [collection](architecture/collection.md#robots-and-pacing).

Managed jobs share one collector slot for each operational store. Direct `collect`, `collect-import`, and standalone collector commands are outside this guarantee. Do not treat collection as exactly once. Back up the database and the complete artifact roots together.

## Feed and HTML discovery

Set a source's `mode` to `feed` to collect RSS 2.0 or Atom, or to `html` to extract scoped anchor links from a listing page. An omitted mode stays `page`. `maxCandidates` defaults to 100 and accepts 1 to 1,000. The allowed origin and path prefix must cover the feed and the article URLs. Add this synthetic source to `config/example.json` for the local HTTP server above:

```json
{
  "id": "example-local-feed",
  "coverage": [{ "personId": "example-official" }],
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

Create the feed in the same local server directory. This fixture is synthetic and holds no claims about a real official:

```sh
printf '<rss version="2.0"><channel><title>Local fixture</title><item><link>/page.html</link></item></channel></rss>' > .runtime/example-source/feed.xml
```

Apply migrations, collect and import the feed, inspect the returned attempt ID, and admit its candidates:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- db migrate
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- collect-import config/example.json example-local-feed src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery get <attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery admit config/example.json example-local-feed <attempt-id> local-feed-batch-1
# Use a job ID from the admission result:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

`feeds get` and `feeds admit` are aliases of `discovery get` and `discovery admit`. For HTML, use the same commands with a source in `mode: "html"` and a listing-page URL.

Discovery collection also works through `jobs enqueue` and `jobs run`. Inspect the job's attempts to find the evidence attempt ID. `discovery get` reports `parsed`, `invalid`, `unsupported`, or `limitExceeded`, with URLs only after complete parsing. It also prints `outOfScopeCount`. A successful capture does not prove a valid discovery. For a 304, inspect the earlier captured discovery attempt.

`discovery admit` enqueues jobs and does not fetch them. The response reports the jobs created, the duplicate count, and the deferred count. The same key and inputs return the original result; changed inputs with the same key fail. Use a new key for another bounded batch of the remaining candidates. A source and URL already admitted stays deduplicated, even when its job failed or was cancelled; inspect or recover that job instead.

If an import fails, use `receipts replay`; no new fetch is needed. If an admission acknowledgment is lost, repeat its exact key and configuration. Leave out the date to reuse the saved one, or give that same date. Schema changes still need an explicit `db migrate`.

## Federal source pilot

`config/federal-pilot.json` holds a small live-source configuration for Elizabeth Warren and Ted Cruz. It also tracks Hakeem Jeffries, a House member with no sources, so that the House vote stream has a tracked member. It is a collection pilot, not representative national coverage. The coverage start date records when this project began to monitor these sources, not when either senator took office. Office press releases show what an office published. Claims about enacted legislation or completed government actions need corroborating authoritative records before publication. The pilot verified captures of both newsroom pages, and a worker pass parsed 16 candidate links from the Warren listing.

| Source ID | Purpose |
|---|---|
| `elizabeth-warren-press` | Official newsroom listing; HTML discovery applies tolerant recovery to its retained markup; refresh the capture and inspect candidates before admission |
| `ted-cruz-press` | Official newsroom listing; parsed candidates include navigation and pagination, so prefer the RSS source for article admission |
| `ted-cruz-press-feed` | RSS endpoint that the official newsroom advertises; bounded admission of linked article jobs |
| `elizabeth-warren-pilot-article-1` | Explicit education-oversight release; `capture-text-v2` with the `warren-article` profile extracts its retained capture |
| `elizabeth-warren-pilot-article-2` | Explicit heating-assistance release; `capture-text-v2` with the `warren-article` profile extracts its retained capture |

Each job allows at most two attempts, five requests and 2 MB for each attempt, a 60-second attempt timeout, and at least 1.5 seconds between content requests. A discovery admission allows at most two article jobs and reserves their full retry allowances: 20 requests, 8 MB, and 240 seconds. Another explicit admission authorizes another batch. The worker is a separate command and does not start with the Host.

Build both executables and set `CIVIC_LENS_DATABASE` as above. For the configured local development database, source the ignored `.runtime/database/connection.env` file. Start with the RSS source:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- validate config/federal-pilot.json
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/federal-pilot.json ted-cruz-press-feed <new-check-key>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery get <captured-attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery admit config/federal-pilot.json ted-cruz-press-feed <captured-attempt-id> <new-admission-key>
```

Inspect discovery before admission. Run each admitted job, then extract its captured article with the configured content profile:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs run <article-job-id> src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents extract config/federal-pilot.json <article-attempt-id> .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents get <extraction-id>
```

The `senate-main` profile keeps the main article region and excludes navigation, modal, sharing, and previous and next controls. The `warren-article` profile selects the article template. Inspect the selected text before you rely on a profile.

First captures are baselines. Do not compare different articles to make a change, and do not mistake a changed profile for a source edit. A document-change candidate needs two compatible extractions of the same source and exact requested URL, and a complete comparison with changed text. The review queue can therefore stay empty while real evidence is stored. Use the evidence pages or `documents history` to find extraction IDs. Failed processing does not mean no activity.

## Recover saved receipts

`collect-import` saves a versioned handoff in `<artifact-directory>/.pending/` after it verifies the receipt and capture, and before it imports. The directory also holds the hash-named gzip captures. A database outage or a missing migration leaves the handoff for replay. Missing or invalid database configuration is still rejected before collection.

List pending handoffs without a database. Then set `CIVIC_LENS_DATABASE`, apply migrations, and replay one attempt or all of them:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts list .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts replay .runtime/captures <attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts replay .runtime/captures --all
```

Listing returns a JSON array of pending identifiers, also for entries whose contents may be invalid. A single replay prints the same import summary as `collect-import`. A batch replay writes one JSON result for each entry, with `handoffId`, `status`, an optional `import` summary, and a sanitized `error`. It continues past single failures. It returns 1 if any recovery fails, any cleanup fails, or a saved outcome is failed or deferred. An empty batch returns 0. Cancellation stops the batch and reports the completed imports and the interrupted entry. Use listing to see the remaining work.

In a batch, a 304 waits with `prerequisiteUnconfirmed` when a captured import is unconfirmed or an envelope is unreadable. Prefer `--all` when you recover related captures and 304s, because a single replay cannot account for other pending evidence. Invalid receipts and missing or corrupt captures stay pending for inspection. Fresh collection and recovery in one spool wait up to 30 seconds for access, then report a failure.

To relocate runtime storage, move the complete capture directory, including `.pending`, and pass its new path to listing and replay. Back up pending handoffs together with their captures. Do not edit envelope contents to relocate them, and never copy credentials into them.

A crash or a cancellation before the handoff save may leave captures without a replayable receipt. Temporary `.tmp-*` handoff files are not replayable. Remove them only when no collection or recovery runs.

## Documents

### Extract and cite document text

Apply `db migrate`, then use an imported captured attempt and its artifact directory:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents extract <captured-attempt-id> .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents get <extraction-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents cite <extraction-id> <start> <length>
```

Extraction returns the extraction ID, the source attempt ID, the processing versions, the text hash, and the text length. `get` returns the exact stored text and provenance. Citation offsets count UTF-16 units, so an emoji stored as a surrogate pair counts as two. The command rejects a range that splits that pair.

Only captured HTML and plain-text attempts are supported. For a linked 304, select the original captured attempt. Repeating an extraction verifies the artifact again and returns the same record for identical output. A relocated artifact directory works. Inspection and citation can use retained text while a capture is unavailable, but back up the raw captures anyway. The rules are in [documents](architecture/documents.md#extraction).

### Configure versioned HTML content selection

In configuration v2, declare reusable profiles and name them from sources. This example targets synthetic local HTML with `<main>` and a `.related-news` subtree:

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

This command uses the profile of the attempt's source ID. It reports the profile ID and revision with the extraction ID. Check the output with `documents get` before you rely on a profile. The selector rules are in [documents](architecture/documents.md#content-profiles).

### Inspect history and compare text

Run `db migrate`, then use:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents history <source-id> <exact-requested-url>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents compare <before-extraction-id> <after-extraction-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents comparison <comparison-id>
```

`compare` returns exit 0 for a complete comparison. It prints an `incompatible` or `limitExceeded` result with exit 1 and does not save it, so `comparison` cannot find it. `comparison` shows a saved result and returns exit 0 when found. An unknown or malformed extraction ID returns exit 2. No hunks means unchanged only when the status is `complete`. To inspect an exact quotation, use `documents cite` with the extraction ID and a positive range length.

When processing versions or profiles differ, run `documents extract` again on both original captured attempt IDs with the same current settings. Then compare the new extraction IDs. These commands do not judge significance, approve evidence, or publish anything.

### Inspect evidence in the review workspace

The pages under `/Documents` show a saved comparison, saved text, a cited passage, and saved versions. Open them from links in Review and Sources. They read existing evidence only. Use the CLI or the worker for migrations, collection, extraction, and comparisons. Empty histories do not show source inactivity.

## Review workspace

`review [port]` starts the editorial workspace on IPv4 loopback (default 5081). It needs Auth0 configuration. It does not collect, migrate, or manage access accounts. Apply `db migrate` before you start it.

Set these variables through the protected environment or secret mechanism of the deployment, never through command-line arguments or committed files:

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_DATABASE` | Existing operational PostgreSQL connection string |
| `CIVIC_LENS_REVIEW_ORIGIN` | Exact external HTTPS origin, without a path, query, or credentials |
| `CIVIC_LENS_AUTH0_AUTHORITY` | Auth0 tenant or custom-domain HTTPS origin |
| `CIVIC_LENS_AUTH0_CLIENT_ID` | Regular Web Application client ID |
| `CIVIC_LENS_AUTH0_CLIENT_SECRET` | Auth0 client secret |
| `CIVIC_LENS_REVIEW_OWNER` | Exact Auth0 `sub` of the owner |
| `CIVIC_LENS_REVIEW_REVIEWERS` | Optional comma-separated additional reviewer subject IDs |
| `CIVIC_LENS_REVIEW_KEY_DIRECTORY` | Absolute persistent directory for ASP.NET data-protection keys |
| `CIVIC_LENS_ISSUES_CONFIG` | Optional absolute path to the tracked-issue file, at most 1 MB. Its issue IDs are the configured issues. Unset means no configured issues |
| `CIVIC_LENS_REVIEW_OFFICIALS` | Optional comma-separated configured official IDs |
| `CIVIC_LENS_COLLECTION_CONFIG` | Optional absolute path to the collection configuration for the owner Sources page |
| `CIVIC_LENS_RELEASE_DIRECTORY` | Optional absolute path to the release root. With the collection configuration, it turns on the owner publish section |

Without the collection configuration, the Sources page reports that source checks are not set up. The publish section needs the collection configuration, for the officials' display names, and the release directory. Without them, the owner sees a notice that publishing is off. Set `CIVIC_LENS_ISSUES_CONFIG` to offer issues and policy areas as choices on the statement pages.

**Auth0 and proxy.** Set the Auth0 allowed callback URL to `<CIVIC_LENS_REVIEW_ORIGIN>/signin-oidc`. Configure an HTTPS reverse proxy on the same host. It forwards to loopback and keeps the configured external Host header, with its port if it is not standard. Public access must end HTTPS at the proxy. Local workstation testing also needs a local HTTPS proxy and an allowed Auth0 callback; there is no unauthenticated review mode.

**Access.** To change access, change the server subject configuration and restart. The data-protection keys are not encrypted at rest by the application. Protect the persistent key volume and its backups as credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- review 5081
```

Open `/Review` through the configured HTTPS origin. Keep the separate `worker` running, because it collects, extracts, and compares automatically. `/Review/Sources` is the owner Sources page. `/Review/GateLabels` is the owner page for gate labels. `/Review/Statements` lists statements, and `/Review/Statements/{candidate-id}` shows one statement for editing and decisions. `/Review/Actions` lists action records and approves sponsorship-only revisions in bulk, and `/Review/Actions/{record-id}` shows one record.

A record allows at most 64 open concerns. An approval resolves them and starts a new cycle. When a limit rejects a write, do not discard history to continue. Browsing comparisons may return an empty batch with a continuation when the batch holds only unchanged, incompatible, or limited results. Use the continuation; the empty batch does not prove that no changes exist.

An AI draft is revision 1 by the AI drafter. Check every sentence and citation against the evidence. If the editor flags an unverified change date, check it, tick Date checked, and save a revision before you approve. Approved accounts stay private until the owner publishes them.

Auth0 tenant login and logout and the production reverse proxy still need live verification.

## AI drafting of document-change accounts

The `worker` command drafts AI accounts of changed comparisons. [Analysis](architecture/analysis.md) holds the rules. Set both required variables below in the worker's protected environment to turn drafting on. If you set only one, the worker stops with exit code 2.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key |
| `CIVIC_LENS_ANALYSIS_DAILY_TOKENS` | Daily token limit for document-change drafting calls, per UTC day. Each other drafting stage has its own limit. |
| `CIVIC_LENS_ANALYSIS_RUN_TOKENS` | Optional limit for one call's reservation (default 120,000 or the daily limit, whichever is smaller; at most the daily limit) |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time (default 2, 1 to 4) |
| `CIVIC_LENS_ISSUES_CONFIG`, `CIVIC_LENS_REVIEW_OFFICIALS` | The same issue file and official IDs that the review host reads |

Tracked issues live in `config/issues.json`. The file format is in [configuration](architecture/configuration.md#tracked-issues). An invalid file stops startup.

`db migrate` queues every earlier changed comparison that has no draft. The first worker run can spend up to the daily limit on those changes. Start with a low daily limit. Check the first drafts, and then raise the limit.

With `--once`, the worker drains collection first and then drafting. A second JSON line reports the drafting result. Drafting failures give exit code 1.

The New changes panel shows whether drafting is on, off, or paused, and the state of each new change.

| Shown state | Action |
|---|---|
| Waiting for budget | Wait for the next UTC day, or raise the limit and restart the worker. |
| Too large for AI | Start the draft by hand. |
| AI declined, Citations rejected, Output rejected, Output too long, Request rejected, Retries used up | Start the draft by hand, or requeue the entry. |
| Paused: key rejected, billing problem, or model not available | Fix the key, the credit, or the model access. Then restart the worker. |
| Paused: the Claude API is failing | Wait. The worker tries again by itself. |

To requeue failed entries, run one of these commands as the owner. They need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- analyses requeue <comparison-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- analyses requeue --all
```

To see how reviewers treat AI drafts, run this command as the owner. It makes no model calls and prints one JSON document.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- analyses metrics
```

The document lists one entry for each prompt version. Drafts that a person started by hand are not counted.

| Field | Meaning |
|---|---|
| drafts | AI drafts written with this prompt version. |
| approvedUnchanged | Approved, and the approved revision holds the same values as revision 1. A re-save with the same values counts as unchanged. |
| approvedEdited | Approved, and at least one field differs from revision 1. |
| changesRequested, approvalWithdrawn | Drafts whose current status is that status. |
| awaitingReview | Drafts that await review. This includes drafts that carry an unresolved concern from an earlier revision. |
| fieldEdits | For approved drafts, how many differ from revision 1 in each field: headline, summary, significance, limits, institution, changeDate (date or its evidence), officialIds, issueIds, citations. Selections and citations compare as sets, so order does not matter. A citation is its extraction, start, and length. |

Each draft is counted once, by its current status. A draft with a newer revision is judged on that revision.

The [live test](#verification) for this adapter sends one paid call. It refuses to call if the reservation is over 25,000 tokens.

## Statement issue gate

The `worker` command gates statement candidates. [Statements](architecture/statements.md#issue-gate) holds the rules. The gate runs when all of these variables are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. Without `CIVIC_LENS_ISSUES_CONFIG`, the gate stage does not run, and `statements status` shows the pause reason `issuesConfigurationUnset`.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with drafting |
| `CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS` | Daily token limit for gate calls, per UTC day. It is separate from the drafting limit. |
| `CIVIC_LENS_STATEMENT_GATE_RUN_TOKENS` | Optional limit for one call's reservation (default 120,000 or the daily limit, whichever is smaller). It also sets the article byte cap. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with drafting (default 2, 1 to 4) |
| `CIVIC_LENS_ISSUES_CONFIG` | The issue file. Its revision ID binds each gate run. |

The migration queued a gate row for every earlier candidate. The first run can spend up to the daily limit on them. Start with a low limit. With `--once`, the worker drains the stages in the [order in statements](architecture/statements.md#issue-gate) and prints one more JSON line for each stage that runs.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements list skipped 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements run <run-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements requeue <candidate-id|--all>
CIVIC_LENS_ISSUES_CONFIG=/abs/config/issues.json dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements regate --since 2026-01-01
```

`status` prints the daily limit, any pause, the tokens charged today, and the count of candidates in each outcome. `list` takes `passed`, `skipped`, `failed-open`, `over-cap`, `pending`, or `failed`, and a limit up to 500 (default 100). `run` prints one run with its output, rubric revision, reason, and verified quote spans. `requeue` moves Failed gate rows back to Pending. `regate` moves skipped candidates back to Pending when their latest run used another issue revision or another gate prompt version. To apply a rubric change, edit the issue file, run `regate`, and let the worker run.

The [live test](#verification) for the gate sends two paid requests. It fails unless the second request reads the prompt cache.

## Gate labels

The owner labels releases by hand to measure the gate. [Statements](architecture/statements.md#gate-labels) holds the rules. Open `/Review/GateLabels` as the owner. Held-out releases come first. Tick the tracked issues of a release, choose yes or no for a named bill or vote, and save. The page then opens the next release.

The export needs `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`. Run it from the repository root. The path must be inside `.runtime`, or the command stops with exit code 2.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements labels export .runtime/benchmark/gate-labels.jsonl
```

The file has one JSON line for each release that has a human label. Each line holds the candidate ID, the extraction ID, the held-out flag, the issue IDs, `namesBillOrVote`, and the public release text. It never holds the author, a review note, or a credential. Only this text and these labels may go to Cloudflare.

## Statement drafting

The `worker` command drafts a statement record for each candidate that the gate passed. [Statements](architecture/statements.md#statement-drafting) holds the rules. Statement drafting runs when both variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. The stage does not need the issue file or the current collection configuration.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS` | Daily token limit for statement drafting calls, per UTC day. It is separate from the gate and the document-change limits. |
| `CIVIC_LENS_STATEMENT_DRAFTING_RUN_TOKENS` | Optional limit for one call's reservation (default 120,000 or the daily limit, whichever is smaller). It also sets the article byte cap. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Sonnet 5.5, which costs more than the gate model. Gate rows that passed before you set the limit become draft rows, and the first run can spend up to the daily limit on them. Start with a low limit. A candidate whose article is over the byte cap gets no model call. It gets a draft with no revision, and a person writes the record. An entry blocked as `speakersUnavailable` stays blocked after a coverage change; the change applies only to articles admitted after it.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements draft status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements draft list over-cap 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements draft run <run-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements draft requeue <candidate-id|--all>
```

`status` prints the daily limit, any pause, the tokens charged today, and the count of drafts in each outcome. `list` takes `drafted`, `over-cap`, `existing`, `pending`, or `failed`, and a limit up to 500 (default 100). `run` prints one run with its output, reason, and validation errors. `requeue` moves Failed and no other draft rows back to Pending. It never changes a gate row. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

The [live test](#verification) for statement drafting sends one paid request.

## Action drafting

The `worker` command drafts one action record for each bill that has a candidate. [Actions](architecture/actions.md) holds the rules. Action drafting runs when all three required variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` or without `CIVIC_LENS_ISSUES_CONFIG` stops the worker with exit code 2. The tracked officials come from the configuration revision of the candidate, so the stage does not read the current collection configuration. The issue file gives the tracked issues of each bill.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_ISSUES_CONFIG` | Absolute path of the issue configuration, shared with the Congress.gov bill stream |
| `CIVIC_LENS_ACTION_DRAFTING_DAILY_TOKENS` | Daily token limit for action drafting calls, per UTC day (about 2,000,000 is a good start). It is separate from the other limits. |
| `CIVIC_LENS_ACTION_DRAFTING_RUN_TOKENS` | Optional limit for one call's reservation (default 120,000 or the daily limit, whichever is smaller). It also sets the prompt byte cap. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Haiku 5.5. The migration queued every candidate that existed when it ran, and the first run can spend up to the daily limit on them. Start with a low limit. A bill version whose summaries had already closed has two queued candidates. The older one settles as `supersededByNewerCandidate` with no call, also when the candidate with summaries blocks. The [outcomes](architecture/actions.md#outcomes) table lists every other code.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft list unchanged 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft requeue <candidate-id|--all>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft requeue --blocked --code titleTooLong
```

`status` prints the daily limit, any pause, the tokens charged today, and the count of entries in each outcome. `list` takes `drafted`, `unchanged`, `superseded`, `no-official`, `pending`, or `failed`, and a limit up to 500 (default 100). `requeue` moves Failed and no other rows back to Pending. `requeue --blocked --code <block-code>` moves Blocked rows with that code back to Pending. Fix the cause first, or the entry blocks again. The code is required, so one command cannot requeue every Blocked entry. Use a code from the [outcomes](architecture/actions.md#outcomes) table, or the exception type name that `list failed` shows for an unexpected error. A mistyped code requeues nothing and prints `{"requeued":0}`. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

The [live test](#verification) for action drafting sends one paid request.

## Vote drafting

The `worker` command drafts one vote record for each House roll call that has a candidate. [Votes](architecture/votes.md#drafting) holds the rules. Vote drafting runs when all three required variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` or without `CIVIC_LENS_ISSUES_CONFIG` stops the worker with exit code 2. The tracked members come from the configuration revision of the candidate. The issue file gives the tracked issues of the voted bill. Votes on a bill draft only when the `congress-bills` source is enabled and has saved the detail of that bill.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_ISSUES_CONFIG` | Absolute path of the issue configuration, shared with the Congress.gov bill stream |
| `CIVIC_LENS_VOTE_DRAFTING_DAILY_TOKENS` | Daily token limit for vote drafting calls, per UTC day. It is separate from the other limits. Start low. |
| `CIVIC_LENS_VOTE_DRAFTING_RUN_TOKENS` | Optional limit for one call's reservation (default 120,000 or the daily limit, whichever is smaller). It also sets the prompt byte cap. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Haiku 5.5. The migration queued every vote candidate that existed when it ran, and the first run can spend up to the daily limit on them. The stage runs after action drafting and before brief drafting. A roll call with no tracked member, and a re-collected vote with the same facts, settle without a call. The [outcomes](architecture/votes.md#outcomes) table lists every code.

No owner command exists for vote drafting yet. The status row of the task `vote-draft` shows the daily limit and any pause. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

## Brief drafting

The `worker` command drafts one weekly brief for each closed week. [Briefs](architecture/briefs.md) holds the rules. A week runs from Monday 00:00 to the next Monday 00:00 in New York time. The brief covers the records that releases published in that week. Brief drafting runs when both required variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. The names of the officials come from the collection configuration of the worker.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_BRIEF_DRAFTING_DAILY_TOKENS` | Daily token limit for brief drafting calls, per UTC day. It is separate from the other limits. |
| `CIVIC_LENS_BRIEF_DRAFTING_RUN_TOKENS` | Optional limit for one call's reservation (default 120,000 or the daily limit, whichever is smaller). It also sets the prompt byte cap. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Sonnet 5.5. The first run with the limit set stores its Monday as the first brief week. The worker queues each closed week from then on, and a wakeup at each Monday boundary queues the week that just closed. The worker never queues a week before the first brief week. A week with no new records makes no call and no brief.

The command below needs `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- brief redraft --week 2026-10-05
```

`redraft` queues the week if it has no entry. It moves a Succeeded, Blocked, or Failed entry back to Pending. It also works for a week before the first brief week. The date must be a Monday, and the week must have ended. The command prints `{"requeued":1}` when it queued or moved the entry, and `{"requeued":0}` when the entry is already Pending, Running, or waiting. The step settles as `unchanged` when the records of the week and their revisions in the active release have not changed since the last brief; see [staleness](architecture/briefs.md#staleness). A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

The [live test](#verification) for brief drafting sends one paid request.

## Publish a static release

The owner can publish from the Review home page. Select records in the Publish section and choose Review selection. The list shows each record as a document change, a statement, or an action. The summary lists each headline with its kind, as new or a replacement, and shows the release number. Choose Publish to confirm. The page lists recent releases read-only. Rollback stays in the CLI.

The CLI is a local owner command. Set `CIVIC_LENS_DATABASE`, `CIVIC_LENS_REVIEW_OWNER` (the owner subject recorded on the release), and `CIVIC_LENS_RELEASE_DIRECTORY` (an absolute path, for example `$PWD/.runtime/releases`). Publishing also needs `CIVIC_LENS_COLLECTION_CONFIG`, an absolute configuration path that supplies the officials' display names. The command trusts the local operator, who already holds database credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases publish <idempotency-key> <kind>:<id> [<kind>:<id>...]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases list [limit]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases activate <release-number>
```

Write each record as `<kind>:<id>`. The kind is `documentChange`, `statement`, or `action`. For example, use `documentChange:` and a 32-character draft ID, `statement:` and a 64-character candidate ID, or `action:` and a 64-character action record ID. The command never guesses the kind from the ID. An unknown kind or a bad shape exits with code 2. The revision must be approved with no unresolved concerns.

A concurrent review decision, draft save, other release, or rollback makes the commit fail with exit code 1. The built directory is deleted and nothing is activated. Invalid input or a non-owner actor exits with code 2 and prints the rule that failed. The publish rules are in [publication](architecture/publication.md#publish).

Repeat the same key and records to receive the original result. The repeat also repairs a link swap that failed after the commit. To roll back, activate an earlier release number. The next `publish` builds on the active release, so records added after it are not carried forward. `list` shows `activeReleaseNumber`, the release that Postgres records as active, and `servedDirectoryName`, the release that the link serves. If they differ, run `releases activate` with the active release number. Releases are never deleted automatically.

To check that the public pages work without the operational application, stop the review workspace, the worker, and Postgres. Then serve the active release with a plain static server:

```sh
python3 -m http.server 8090 --bind 127.0.0.1 --directory "$CIVIC_LENS_RELEASE_DIRECTORY/current"
```

Open `http://127.0.0.1:8090/`. The index shows 50 records for each page, with links to the previous and next pages, and a kind label next to each headline. A document change page shows the account, the changes, the citations, and both full document versions. A statement page shows the speakers, the statement date with its evidence, each quotation with its kind, and the full source text with the quotations marked. An action page shows the bill, a sponsorship table with cited quotes, the policy area and CRS summary citations, and its Congress.gov sources. The production static server is still a deployment decision.

## Runtime data

Runtime data belongs in ignored .runtime/. Generated publication and evaluation artifacts belong in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. There is no deploy command and no public endpoint yet. Published releases live under the configured release directory, not in the repository. Production backup and restore and MCP runbooks will come here when they exist.
