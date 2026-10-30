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

Run `dotnet run --project src/CivicLens.Host -- --help` to list the host commands. `dotnet run --project src/CivicLens.Collector -- --help` describes the manifest-driven collector. Unsupported commands return nonzero exit codes.

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

Configuration version 1 is the only supported version. For example:

```json
{
  "version": 1,
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

`collect`, `jobs enqueue`, and `discovery admit` accept a trailing `--as-of yyyy-MM-dd`. Selecting a past date still fetches the current source; it does not request an archived page. For a reproducible managed admission:

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

Run `CivicLens.Collector collect <manifest.json>` or `dotnet <collector.dll> collect <manifest.json>`. `CollectionRequest` in Collection.Contracts defines the version 1 fields:

- required: `version`, `jobId`, `sourceId`, `url`, `allowedOrigin`, `allowedPathPrefix`, and an absolute `artifactDirectory`
- optional, bounded: `maxRequests`, `maxBytes`, `timeoutSeconds`, `minDelayMilliseconds`, `eTag`, `lastModified`
- for `api` mode only: `credential`

JSON names are case-sensitive. Use version 1 in new manifests, and rebuild the host and collector together. The collector rejects every other version. Validators are explicit in standalone manifests. A 429 gives `deferred` with an optional `retryAfterSeconds`; wait before you run again. The protocol rules are in [collection](architecture/collection.md#collector-protocol).

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

To list passed bills that have no action candidate because a cosponsor or actions page job failed or is blocked, run this query. A failed summaries page does not block a candidate, so the query skips it. A test runs this exact query.

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

### Federal Register stream

The source `federal-register-documents` in `config/federal-coverage.json` follows the presidential documents of the Federal Register. It ships with `enabled: false`. It needs no key and no roster. [Executive actions](architecture/executive-actions.md) holds the design. To enable it:

1. Check that the configuration holds a person for each President whose documents the window covers. The shipped file holds Joe Biden, from 2021-01-20 to 2025-01-20, and Donald Trump from 2025-01-20. A document with no configured President blocks its job.
2. Set `enabled` to `true` for `federal-register-documents` and restart the worker. A restart reads the configuration again.
3. Deploy the search service before the publisher. The search service of an older version refuses a release that holds an executive action.

To stop the source, set `enabled` to `false` and restart the worker.

The first run lists every document from `backfillFrom` 2024-10-29. It admits one job for each document, about 700 over two years. Each check after that runs every 6 hours and lists from 2 days before the newest stored publication date. Run `jobs list` and `jobs get` as in [managed collection jobs](#managed-collection-jobs) to see progress. A blocked job names its reason.

To withdraw a record, open `/Review/ExecutiveActions/{id}` and withdraw approval. Then run `releases publish <key> --withdraw executiveAction:<id> <text>` as in [publish a static release](#publish-a-static-release). The page alone does not change the public site.

### Congress.gov House vote stream

The source `congress-house-votes` in `config/federal-coverage.json` and `config/federal-pilot.json` follows the House roll-call votes of the Congress.gov `house-vote` API. It ships with `enabled: false`. To enable it:

1. Get a key at api.congress.gov/sign-up.
2. Set `CIVIC_LENS_CONGRESS_API_KEY` in the protected environment of the worker. Do not put the key in configuration.
3. Set `enabled` to `true` for `congress-house-votes` and restart the worker. A restart reads the configuration again.

The source needs no issue file, but [vote drafting](#vote-drafting) does. Each day it reads the whole vote list of every session from `backfillFrom` on, about 4 list pages a day today. The first day admits one detail job and one members job for every roll call. [Votes](architecture/votes.md) holds the sweep and candidate rules.

Configuration loading rejects a vote source whose `admissionPolicy` cannot hold the [251-job rule](architecture/votes.md#admission). Set `maxJobs` and the request, byte, and time totals to fit it.

To list vote versions that have a detail but no candidate yet, run this query. A row can be a members job that is still pending, failed, or blocked:

```sql
SELECT v.detail_job_id, v.congress, v.session_number, v.roll_call_number
FROM vote_versions v
LEFT JOIN vote_candidates c ON c.detail_job_id = v.detail_job_id
WHERE c.detail_job_id IS NULL
ORDER BY v.congress, v.session_number, v.roll_call_number;
```

### Voteview Senate vote stream

The Senate votes come from Voteview. senate.gov blocks the collector with a 403, so no other Senate vote source exists. The source `voteview-senate-votes` in `config/federal-coverage.json` ships with `enabled: false`. A configuration may enable only one Voteview source. The parser name `senateVoteList` still reads in a stored configuration, but the Host refuses a live configuration file that enables it.

Before you update a deploy that has the old `senate-votes` source, set `"enabled":false` on the `senate-votes` line in `/srv/civic-lens/config/collection.json`, or remove that line. Do this before you start the new release. The new release stops with an error while the line is enabled.

Do the steps on the machine that runs the worker. On production, this is `/srv/civic-lens/config/collection.json`. Production steps are for the owner to run.

1. Check that `congress-bills` is enabled. A Senate vote on a bill waits for the detail of that bill. Check that `congress-members` is enabled. The roster gives the party of a senator who still serves.
2. In the configuration file, set `enabled` to `true` for `voteview-senate-votes`. Keep the file compact, as the [first install](#first-install) says.
3. Stop the worker. On production, run `sudo deploy/scripts/compose.sh stop worker`.
4. Start the worker, because it reads the configuration only at start. On production, run `sudo deploy/scripts/compose.sh up --detach worker`.
5. Wait one day for the first full sweep. The sweep fetches the three files of the 119th Congress, about 3 MB, and then it makes a vote candidate for each roll call. Then run `jobs list` as in [managed collection jobs](#managed-collection-jobs) and `votes draft status` as in [vote drafting](#vote-drafting).

To stop the source, set `enabled` to `false` and restart the worker.

What to watch:

- A `jobs list` row for a Voteview file that is Failed or Blocked. A Blocked votes job with `voteviewJobMissing` means its parent job is missing. A job with `voteviewCountMismatch` succeeded, and it skipped roll calls whose counts differ. The worker log names the number skipped.
- A Voteview file that changes its columns or its codes. The parser refuses an unknown cast code, an unknown party code, and a member with no bioguide ID. The job fails loudly.
- The lag of Voteview behind the Senate. Compare the newest roll call date in the files with the roll call list on senate.gov. The sweep runs once a day, so one day of lag is normal. Report a longer lag.
- Drafts Blocked with `evidenceUnavailable`. Voteview matches senators by bioguide ID, so a roster gap does not cause them.
- Voteview lists no Vice President tie-break vote. A roll call that ended 50 to 50 shows no tie-break cast on its page. The page says so.

The first full run drafts every roll call that has a tracked senator. Drafting costs about $0.00046 for each draft. Code approves each draft after it checks the citations. A Senate vote from Voteview rests on Voteview data, and the record page names Voteview as the source.

## Postgres adapter and migrations

Set `CIVIC_LENS_DATABASE` in your local environment to a Postgres connection string with Host and Database, and with authentication for your local server. Runtime commands need this variable and have no default database. Keep credentials out of committed files, command arguments, and captured output. The commands need a reachable Postgres server and enough database permissions. They do not install or start Postgres.

After you set the variable, run:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- db migrate
```

`db migrate` applies all pending checked-in EF migrations and can be repeated. Each migration command fails after 10 minutes, for example when it waits on a lock. Imports never apply migrations. Capture files stay in the artifact directory. [Managed collection jobs](#managed-collection-jobs) collect and import evidence.

For other application composition, build `PostgresCollectionAttemptStore` with `FromConnectionString`, or supply an `IDbContextFactory<CollectionAttemptDbContext>`. Call `MigrateAsync` explicitly before you import.

The design-time context factory reads `CIVIC_LENS_DATABASE` when it is set. Otherwise it uses a local design database name for offline migration generation. The first migration, `InitialSchema`, holds the generated tables. After them, it holds the hand SQL for the notify functions and triggers, the composite prior-capture foreign key, and the descending and partial indexes. Add later schema changes as new migrations.

The EF tool version is pinned in `.config/dotnet-tools.json`. Restore it once, then add a migration for `src/CivicLens.Infrastructure`, with output under `Collection/Migrations`:

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/CivicLens.Infrastructure --output-dir Collection/Migrations --context CollectionAttemptDbContext
dotnet ef migrations has-pending-model-changes --project src/CivicLens.Infrastructure --context CollectionAttemptDbContext
```

Review the generated operations and the snapshot together. Remove the byte order mark from the generated files, because `dotnet format` rejects it. Keep the explicit composite prior-capture foreign key in later schema changes. `MigrationsCreateTheHandWrittenObjects` tests that it exists.

After a schema change, regenerate the schema reference in `docs/architecture/schema/` and commit it. Place each new table in the feature map of `SchemaReferenceTests` first.

```sh
CIVIC_LENS_WRITE_SCHEMA_DOCS=1 dotnet test CivicLens.slnx --configuration Release --no-build --filter FullyQualifiedName~SchemaReferenceTests
```

## Managed collection jobs

Apply `db migrate` first, also when you upgrade. Rebuild the host and collector together. Job commands need `CIVIC_LENS_DATABASE`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/example.json example-local-source check-2026-10-06
CIVIC_LENS_COLLECTION_CONFIG="$PWD/config/example.json" dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures --once
# Use the jobId returned by enqueue:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs get <job-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs list 20
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs cancel <job-id>
```

Reuse an enqueue key only for the same configuration revision, source settings, policy, and any explicit coverage date. Use a new key for a new logical check.

The worker skips a job while a lease, the collector slot of its pacing key, an origin barrier, an origin deadline, or a retry deadline blocks it. Use `jobs get` to see the state and `retryAt`. Job commands return 0 on success, 2 for invalid input or a missing job, and 1 for operational failures. `jobs list` defaults to 20 jobs, with a maximum of 100.

Optional source `jobPolicy` fields are `maxAttempts` (default 3), `initialRetryDelaySeconds` (30), `maximumRetryDelaySeconds` (3600), `maxTotalRequests`, `maxTotalBytes`, and `maxTotalTimeoutSeconds`. Omitted aggregate limits equal the per-attempt limit times `maxAttempts`. Limits must reserve at least one complete attempt. Source `eTag` and `lastModified` are optional explicit validators.

A crawl delay longer than every attempt timeout can exhaust the retries. Inspect the delay, then adjust the work budget or the source policy before you admit new work. Inspection shows immutable attempts and charged totals. The `reserved*` totals keep timeout reservations and unknown use conservatively.

**Interruption.** Ctrl+C does not cancel the job permanently. After an interruption, start the worker again with the complete artifact root, including `.pending`, or its relocated copy. A same-origin unresolved attempt blocks other managed jobs until it is reconciled. The worker reconciles imported evidence even when its handoff is missing. Otherwise `receipts replay` imports without a fetch.

**Worker.** `worker <collector.dll> <artifact-directory>` needs `CIVIC_LENS_COLLECTION_CONFIG` with an absolute configuration path. It runs the durable collection-to-review engine. `--once` admits due checks, drains the runnable work, and exits without waiting for future deadlines. Ctrl+C stops the worker. Change the configuration and restart the worker to apply it. Cancelling one job does not disable its source schedule. Keep one worker for each operational store. Leases and fenced checkpoints protect ownership if another worker runs.

**Worker health.** `worker health` needs `CIVIC_LENS_DATABASE`. It reads the heartbeat rows that the loops of the worker write. It prints one line and exits 0 when every loop beat is at most 10 minutes old. It exits 1 when one beat is older or when no beat exists. The line names the loop and its age. A model request times out after 5 minutes, and a collector attempt takes about 305 seconds plus the settlement timeouts. A single item, or a single release publish, that runs longer than 10 minutes fails health.

After a restart, the worker clears the rows. `worker health` can then report no heartbeat, or a partial list, until every loop has started. A container health check needs a start period of at least 10 minutes. The heartbeat assumes one worker process. `worker --once` does not clear the rows.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker health
```

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_EVIDENCE_CONCURRENCY` | Optional number of captured pages processed at the same time (default 2, 1 to 4). Any other value stops the worker with exit code 2 |
| `CIVIC_LENS_COLLECTION_CONCURRENCY` | Optional number of collection jobs run at the same time (default 1, 1 to 4). Jobs of one pacing key never run together. Any other value stops the worker with exit code 2. Lanes run keys of separate hosts at once. All Congress.gov jobs share one pacing key, so lanes never run them in parallel. Production uses 3, so house.gov, senate.gov, and the other hosts progress together |

Enabled sources are checked every hour unless `checkIntervalSeconds` sets another interval from 60 to 604800 seconds. The engine rules are in [collection](architecture/collection.md#autonomous-collection-engine).

**Pacing groups.** Optional top-level `pacingGroups` limit the total load on shared hosts. For example, `{"hostSuffix": "house.gov", "minDelayMilliseconds": 3000}` spaces all managed jobs for any `*.house.gov` origin by at least 3 seconds. This adds to the pacing of each origin. If the queue keeps growing, lower the delay. The group rules are in [collection](architecture/collection.md#robots-and-pacing).

Managed jobs share one collector slot for each pacing key. Direct `collect` and standalone collector commands are outside this guarantee. Do not treat collection as exactly once. Back up the database and the complete artifact roots together.

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

Apply migrations, enqueue and run the feed check, read the attempt ID from the job, and admit its candidates:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- db migrate
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/example.json example-local-feed local-feed-check-1
CIVIC_LENS_COLLECTION_CONFIG="$PWD/config/example.json" dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures --once
# Use the attempt ID from the job:
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs get <job-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery get <attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery admit config/example.json example-local-feed <attempt-id> local-feed-batch-1
CIVIC_LENS_COLLECTION_CONFIG="$PWD/config/example.json" dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures --once
```

For HTML, use the same commands with a source in `mode: "html"` and a listing-page URL.

Inspect the job's attempts to find the evidence attempt ID. `discovery get` reports `parsed`, `invalid`, `unsupported`, or `limitExceeded`, with URLs only after complete parsing. It also prints `outOfScopeCount`. A successful capture does not prove a valid discovery. For a 304, inspect the earlier captured discovery attempt.

`discovery admit` enqueues jobs and does not fetch them. The response reports the jobs created, the duplicate count, and the deferred count. The same key and inputs return the original result; changed inputs with the same key fail. Use a new key for another bounded batch of the remaining candidates. A source and URL already admitted stays deduplicated, even when its job failed or was cancelled; inspect or recover that job instead.

If the worker cannot import a saved receipt, use `receipts replay`; no new fetch is needed. If an admission acknowledgment is lost, repeat its exact key and configuration. Leave out the date to reuse the saved one, or give that same date. Schema changes still need an explicit `db migrate`.

## Federal source pilot

`config/federal-pilot.json` holds a small live-source configuration for Elizabeth Warren and Ted Cruz. It also tracks Hakeem Jeffries, a House member with no sources, so that the House vote stream has a tracked member. It is a collection pilot, not representative national coverage. The coverage start date records when this project began to monitor these sources, not when either senator took office. Office press releases show what an office published. Claims about enacted legislation or completed government actions need corroborating authoritative records before publication. The pilot verified captures of both newsroom pages, and a worker pass parsed 16 candidate links from the Warren listing.

| Source ID | Purpose |
|---|---|
| `elizabeth-warren-press` | Official newsroom listing; HTML discovery applies tolerant recovery to its retained markup; refresh the capture and inspect candidates before admission |
| `ted-cruz-press` | Official newsroom listing; parsed candidates include navigation and pagination, so prefer the RSS source for article admission |
| `ted-cruz-press-feed` | RSS endpoint that the official newsroom advertises; bounded admission of linked article jobs |
| `elizabeth-warren-pilot-article-1` | Explicit education-oversight release; `html-text-v1` with the `warren-article` profile extracts its retained capture |
| `elizabeth-warren-pilot-article-2` | Explicit heating-assistance release; `html-text-v1` with the `warren-article` profile extracts its retained capture |

Each job allows at most two attempts, five requests and 2 MB for each attempt, a 60-second attempt timeout, and at least 1.5 seconds between content requests. A discovery admission allows at most two article jobs and reserves their full retry allowances: 20 requests, 8 MB, and 240 seconds. Another explicit admission authorizes another batch. The worker is a separate command and does not start with the Host.

Build both executables and set `CIVIC_LENS_DATABASE` as above. For the configured local development database, source the ignored `.runtime/database/connection.env` file. Start with the RSS source:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- validate config/federal-pilot.json
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs enqueue config/federal-pilot.json ted-cruz-press-feed <new-check-key>
CIVIC_LENS_COLLECTION_CONFIG="$PWD/config/federal-pilot.json" dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures --once
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- jobs get <job-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery get <captured-attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- discovery admit config/federal-pilot.json ted-cruz-press-feed <captured-attempt-id> <new-admission-key>
```

Inspect discovery before admission. Run the worker for the admitted jobs, then extract its captured article with the configured content profile:

```sh
CIVIC_LENS_COLLECTION_CONFIG="$PWD/config/federal-pilot.json" dotnet run --no-build --configuration Release --project src/CivicLens.Host -- worker src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures --once
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents extract config/federal-pilot.json <article-attempt-id> .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- documents get <extraction-id>
```

The `senate-main` profile keeps the main article region and excludes navigation, modal, sharing, and previous and next controls. The `warren-article` profile selects the article template. Inspect the selected text before you rely on a profile.

First captures are baselines. Do not compare different articles to make a change, and do not mistake a changed profile for a source edit. A document-change candidate needs two compatible extractions of the same source and exact requested URL, and a complete comparison with changed text. The review queue can therefore stay empty while real evidence is stored. Use the evidence pages or `documents history` to find extraction IDs. Failed processing does not mean no activity.

## Recover saved receipts

A managed job saves a versioned handoff in `<artifact-directory>/.pending/` after it verifies the receipt and capture, and before it imports. The directory also holds the hash-named gzip captures. A database outage or a missing migration leaves the handoff. The worker imports the handoff of a job that it reconciles. `receipts replay` imports a handoff that the worker does not reach, for example a late receipt of a runner that lost its job.

List pending handoffs without a database. Then set `CIVIC_LENS_DATABASE`, apply migrations, and replay one attempt or all of them:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts list .runtime/captures
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts replay .runtime/captures <attempt-id>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- receipts replay .runtime/captures --all
```

Listing returns a JSON array of pending identifiers, also for entries whose contents may be invalid. A single replay prints one JSON summary, for example:

```json
{"attemptId":"<generated-id>","outcome":"captured","importDisposition":"newAttempt","priorCaptureLinkStatus":"notApplicable","handoffRemoved":true}
```

The exit code is 0 for an imported captured or not-modified outcome. It is 1 for a failed or deferred outcome, an operational failure, a cancellation, or a confirmed import whose cleanup failed. A confirmed import with failed cleanup prints `handoffRemoved: false`; replay stays safe. Stderr reports that persistence was not confirmed when an import fails or stops. A batch replay writes one JSON result for each entry, with `handoffId`, `status`, an optional `import` summary, and a sanitized `error`. It continues past single failures. It returns 1 if any recovery fails, any cleanup fails, or a saved outcome is failed or deferred. An empty batch returns 0. Cancellation stops the batch and reports the completed imports and the interrupted entry. Use listing to see the remaining work.

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

In the configuration, declare reusable profiles and name them from sources. This example targets synthetic local HTML with `<main>` and a `.related-news` subtree:

```json
{
  "version": 1,
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
| `CIVIC_LENS_ISSUES_CONFIG` | Absolute path to the tracked-issue file, at most 1 MB. Its issue IDs are the configured issues. Publishing needs it. Unset means no configured issues and no publishing |
| `CIVIC_LENS_COLLECTION_CONFIG` | Optional absolute path to the collection configuration for the owner Sources page and the official choices of the review pages. Without it no officials are listed |
| `CIVIC_LENS_RELEASE_DIRECTORY` | Optional absolute path to the release root. With the collection configuration, it turns on the owner publish section |
| `CIVIC_LENS_ARTIFACT_DIRECTORY` | The capture directory. Required when `CIVIC_LENS_RELEASE_DIRECTORY` is set, because a publish reads it for a new member photo. Without it the workspace stops at startup with exit code 2; see [Publish a static release](#publish-a-static-release) |

Without the collection configuration, the Sources page reports that source checks are not set up. The publish section needs the collection configuration, for the officials' display names, the issue file, for issue and policy area names, and the release directory. Without them, the owner sees a notice that publishing is off. The issue file also offers issues and policy areas as choices on the statement pages. The `releases publish` command needs the same two files.

**Auth0 and proxy.** Set the Auth0 allowed callback URL to `<CIVIC_LENS_REVIEW_ORIGIN>/signin-oidc`. Configure an HTTPS reverse proxy on the same host. It forwards to loopback and keeps the configured external Host header, with its port if it is not standard. Public access must end HTTPS at the proxy. Local workstation testing also needs a local HTTPS proxy and an allowed Auth0 callback; there is no unauthenticated review mode.

**Access.** To change access, change the server subject configuration and restart. The data-protection keys are not encrypted at rest by the application. Protect the persistent key volume and its backups as credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- review 5081
```

Open `/Review` through the configured HTTPS origin. Keep the separate `worker` running, because it collects, extracts, and compares automatically. `/Review/Sources` is the owner Sources page. `/Review/GateLabels` is the owner page for gate labels. `/Review/Statements` lists statements, and `/Review/Statements/{candidate-id}` shows one statement for editing and decisions. `/Review/Actions/{record-id}` and `/Review/Votes/{record-id}` show one action or vote. The Review home page links to the records that need action.

A record allows at most 64 open concerns. An approval resolves them and starts a new cycle. When a limit rejects a write, do not discard history to continue. Browsing comparisons may return an empty batch with a continuation when the batch holds only unchanged, incompatible, or limited results. Use the continuation; the empty batch does not prove that no changes exist.

An AI draft is revision 1 by the AI drafter. Check every sentence and citation against the evidence. If the editor flags an unverified change date, check it, tick Date checked, and save a revision before you approve. Approved accounts stay private until the owner publishes them.

Code approves a worker revision of an action or a vote after it checks the citations. The decision history shows the actor "Code check". A record shows as Needs action only if code did not approve it. A concern is open, a person wrote the text, or an approval check failed. A person can edit or withdraw any record. The owner still publishes each release.

Auth0 tenant login and logout and the production reverse proxy still need live verification.

## AI drafting of document-change accounts

The `worker` command drafts AI accounts of changed comparisons. [Analysis](architecture/analysis.md) holds the rules. Set both required variables below in the worker's protected environment to turn drafting on. If you set only one, the worker stops with exit code 2.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key |
| `CIVIC_LENS_ANALYSIS_DAILY_TOKENS` | Daily token limit for document-change drafting calls, per UTC day. Each other drafting stage has its own limit. The [call cap](architecture/analysis.md#budget) bounds one call. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time (default 2, 1 to 4) |
| `CIVIC_LENS_ISSUES_CONFIG` | The same issue file that the review host reads |

Tracked issues live in `config/issues.json`. The file format is in [configuration](architecture/configuration.md#tracked-issues). An invalid file stops startup.

The first worker run can spend up to the daily limit on queued changes. Start with a low daily limit. Check the first drafts, and then raise the limit.

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

The [live test](#verification) for this adapter sends one paid call. It refuses to call if the reservation is over 25,000 tokens.

## Statement issue gate

The `worker` command gates statement candidates. [Statements](architecture/statements.md#issue-gate) holds the rules. The gate runs when all of these variables are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. Without `CIVIC_LENS_ISSUES_CONFIG`, the gate stage does not run, and `statements status` shows the pause reason `issuesConfigurationUnset`.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with drafting |
| `CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS` | Daily token limit for gate calls, per UTC day. It is separate from the drafting limit. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with drafting (default 2, 1 to 4) |
| `CIVIC_LENS_ISSUES_CONFIG` | The issue file. Its revision ID binds each gate run. |

The first run can spend up to the daily limit on queued candidates. Start with a low limit. With `--once`, the worker drains the stages in the [order in statements](architecture/statements.md#issue-gate) and prints one more JSON line for each stage that runs.

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
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Haiku 5.5. The first run can spend up to the daily limit on queued candidates. Start with a low limit. A bill version whose summaries have closed has two queued candidates. The older one settles as `supersededByNewerCandidate` with no call, also when the candidate with summaries blocks. The [outcomes](architecture/actions.md#outcomes) table lists every other code.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft list unchanged 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft requeue <candidate-id|--all>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft requeue --blocked --code titleTooLong
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft redraft
```

`status` prints the daily limit, any pause, the tokens charged today, and the count of entries in each outcome. `list` takes `drafted`, `unchanged`, `superseded`, `no-official`, `pending`, or `failed`, and a limit up to 500 (default 100). `requeue` moves Failed and no other rows back to Pending. `requeue --blocked --code <block-code>` moves Blocked rows with that code back to Pending. Fix the cause first, or the entry blocks again. The code is required, so one command cannot requeue every Blocked entry. Use a code from the [outcomes](architecture/actions.md#outcomes) table, or the exception type name that `list failed` shows for an unexpected error. A mistyped code requeues nothing and prints `{"requeued":0}`. `redraft` moves drafted and unchanged rows of bills with an older prompt version back to Pending, and prints the count of entries, not bills. Older entries of a stale bill count too and settle as superseded. Use it for drafts that lack issue candidates. A prompt change alone draws no new draft, because the comparison ignores the prompt version. After the command, let the worker draft within its daily limit. The worker makes the paid calls, not the command. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

The [live test](#verification) for action drafting sends one paid request.

## Vote drafting

The `worker` command drafts one vote record for each House or Senate roll call that has a candidate. [Votes](architecture/votes.md#drafting) holds the rules. Vote drafting runs when all three required variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` or without `CIVIC_LENS_ISSUES_CONFIG` stops the worker with exit code 2. The tracked members come from the configuration revision of the candidate. The issue file gives the tracked issues of the voted bill. Votes on a bill draft only when the `congress-bills` source is enabled and has saved the detail of that bill.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_ISSUES_CONFIG` | Absolute path of the issue configuration, shared with the Congress.gov bill stream |
| `CIVIC_LENS_VOTE_DRAFTING_DAILY_TOKENS` | Daily token limit for vote drafting calls, per UTC day. It is separate from the other limits. Start low. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Haiku 5.5. The first run can spend up to the daily limit on queued vote candidates. The stage runs after action drafting and before brief drafting. A roll call with no tracked member, and a re-collected vote with the same facts, settle without a call. The [outcomes](architecture/votes.md#outcomes) table lists every code.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- votes draft status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- votes draft list unchanged 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- votes draft requeue <candidate-id|--all>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- votes draft requeue --blocked --code factTooLong
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- votes draft redraft
```

`status` prints one JSON object. Its `queue` field holds the daily limit, any pause, `chargedToday`, and `counts` for each outcome. Its `waitingForBill` field is the number of votes on a bill that wait for the bill detail and have no queue row yet. `list` takes `drafted`, `unchanged`, `superseded`, `no-member`, `pending`, or `failed`, and a limit up to 500 (default 100). `requeue` moves Failed and no other rows back to Pending. `requeue --blocked --code <block-code>` moves Blocked rows with that code back to Pending. Fix the cause first, or the entry blocks again. The code is required. Use a code from the [outcomes](architecture/votes.md#outcomes) table. A mistyped code requeues nothing and prints `{"requeued":0}`. `redraft` moves drafted and unchanged rows of votes on a bill with an older prompt version back to Pending. It prints the count of entries, not roll calls. Older entries of a stale roll call count too and settle as superseded. The newest drafted run of the roll call decides. A vote with no bill never moves. A redraft can write new revisions. Code approval of a new revision can queue relationship pairs, and a queued pair can cost a model call. An approved link that cites the old revision shows "cited records changed" and needs a person. A vote redraft requeues no brief, because briefs cannot cite votes. After the command, let the worker draft within its daily limit. The worker makes the paid calls, not the command. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

## Relationship drafting

The `worker` command drafts one relationship for each pair of an approved statement and an approved action or vote that share an official and a bill. [Statements](architecture/statements.md#relationship-drafting) holds the rules. Relationship drafting runs when both required variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. The names of the officials come from the collection configuration of the worker.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_RELATIONSHIP_DRAFTING_DAILY_TOKENS` | Daily token limit for relationship drafting calls, per UTC day. It is separate from the other limits. Start low. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Haiku 5.5. The stage runs after vote drafting and before brief drafting. A review decision on a statement, an action, or a vote queues a Pending row for each new pair of that record. Run `relationships draft refresh` once after the migration to queue the pairs of records that were approved earlier. Then the worker can spend up to the daily limit. A pair with a new approved revision on either side returns to Pending unless a person approved its relationship. A pair with the same input as the last settled run makes no call. The [outcomes](architecture/statements.md#relationship-drafting) table lists every code.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft list none 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft requeue <pair-id|--all>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft refresh
```

`status` prints one JSON object. Its `queue` field holds the daily limit, any pause, `chargedToday`, and `counts` for each outcome. Its `statementsWithoutCongress` field is the number of approved statements that name a bill number and have no date, or a date before 1935. Code skips their bill numbers, so set the date on the statement to pair it by number. `list` takes `drafted`, `unchanged`, `none`, `unmatched`, `pending`, or `failed`, and a limit up to 500 (default 100). `requeue` moves Failed and no other rows back to Pending. `refresh` prints `{"pairs":N}`, the number of pairs it found, and it is safe to repeat. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

## Brief drafting

The `worker` command drafts one weekly brief for each closed week. [Briefs](architecture/briefs.md) holds the rules. A week runs from Monday 00:00 to the next Monday 00:00 in New York time. The brief covers the records that releases published in that week. Brief drafting runs when both required variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. The names of the officials come from the collection configuration of the worker.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_BRIEF_DRAFTING_DAILY_TOKENS` | Daily token limit for brief drafting calls, per UTC day. It is separate from the other limits. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Sonnet 5.5. The first run with the limit set stores its Monday as the first brief week. The worker queues each closed week from then on, and a wakeup at each Monday boundary queues the week that just closed. The worker never queues a week before the first brief week. A week with no new records makes no call and no brief.

The command below needs `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- brief redraft --week 2026-10-05
```

`redraft` queues the week if it has no entry. It moves a Succeeded, Blocked, or Failed entry back to Pending. It also works for a week before the first brief week. The date must be a Monday, and the week must have ended. The command prints `{"requeued":1}` when it queued or moved the entry, and `{"requeued":0}` when the entry is already Pending, Running, or waiting. The step settles as `unchanged` when the records of the week and their revisions in the active release have not changed since the last brief; see [staleness](architecture/briefs.md#staleness). A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

The [live test](#verification) for brief drafting sends one paid request.

### Member roster

Enable the source `congress-members` in the collection configuration to capture the public member file once a day. It needs no credential. The command below needs `CIVIC_LENS_DATABASE`. It reads the latest snapshot and writes nothing.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- roster propose config/federal-coverage.json
```

Enable the source `executive-officials` the same way to capture the executive file, which lists presidents and vice presidents. Publish reads it to write the terms of a configured person with a Wikidata ID.

Enable the source `member-photos` to capture one portrait for each configured member. It needs no credential. Each member roster job then enqueues the photo jobs, and each person gets a new job every 30 days. The first run makes about 540 jobs, paced at one request a second. Run the roster source first. See [roster](architecture/roster.md#member-photos).

The output lists the members to add and the configured people who left the roster. Edit the configuration by hand and commit it. Press sources of a new member are a manual step. See [roster](architecture/roster.md#proposals).

### Take down a member photo

Set `"withholdPhoto": true` on the person in the collection configuration and commit it. Restart the worker and the review workspace, because each reads the configuration only at start. A worker with the old configuration writes the photo back at its next automatic publish. Then run `releases publish <idempotency-key>` with no record. The command makes a release that applies the flag. When the person has no published record, no release shows their photo. The command then refuses with "Nothing to publish", and nothing needs to be done. The new release shows no photo for the person, and the official page shows the neutral line "No portrait is shown for this official." Collection continues, so removing the flag, restarting, and publishing again restores the newest photo.

The flag does not remove files from old releases. A kept release directory keeps the photo file until a prune removes it; see [release retention](architecture/publication.md#release-retention). Purge the file from the Cloudflare cache if it must leave at once. A withheld photo can stay in kept release folders for at least 48 hours. A rollback with `releases activate` to an older release shows the photo again. See [roster](architecture/roster.md#limits-and-planned-work).

## Automatic publication

The `worker` command can publish every approved record: actions, votes, executive actions, statements, relationships, briefs, and document changes. A person approval needs no owner step. [Publication](architecture/publication.md#automatic-publication) holds the rules. A person approval that replaces a published record needs a correction note, so the stage leaves it out and logs it. Publish that record with `releases publish --note`. The stage is off by default. It runs when the variable below is `1`. Any other value stops the worker with exit code 2. Code approves an action or a vote only after a model drafts its headline and summary. So the stage publishes nothing until you also set `CIVIC_LENS_ACTION_DRAFTING_DAILY_TOKENS` and `CIVIC_LENS_VOTE_DRAFTING_DAILY_TOKENS`.

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_PUBLISHING` | Set to `1` to run the stage. Unset means off |
| `CIVIC_LENS_RELEASE_DIRECTORY` | The release root, shared with the review workspace and the `releases` commands. Required when the stage runs |
| `CIVIC_LENS_ISSUES_CONFIG` | The issue file. Required when the stage runs, as for `releases publish` |

The stage reads new member photos from the artifact directory of the worker command line. The worker also needs `CIVIC_LENS_COLLECTION_CONFIG`, as always. The stage ticks when the worker starts and then at each full hour by the database clock. A tick publishes batches of up to 64 records until no approved record is ready. Each kind gets an equal share of a batch. A failure is logged at error level with its exception on stderr, under the stage name `publishing`. A failure of the tick counts in the stage result. A record that cannot build is reported and left out, and it does not count. The stage waits after a rollback with `releases activate`. It resumes when you activate the latest release or publish with the CLI. Each release is about 29 MB in development. Each publish prunes old release directories; see [release retention](architecture/publication.md#release-retention).

## Reader report queue

The `worker` command can pull reader reports from the search service into the review queue at `/Review/Reports`. [Review](architecture/review.md#reader-reports) holds the rules. The stage is off by default. It runs when the first variable below is `1`. Any other value stops the worker with exit code 2.

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_REPORT_PULL` | Set to `1` to run the stage. Unset means off |
| `CIVIC_LENS_REPORT_PULL_URL` | The address of the search service, for example `http://127.0.0.1:8100`. Only `http` and `https` are valid. Required when the stage runs |
| `CIVIC_LENS_REPORT_PULL_TOKEN` | The same value as `CIVIC_LENS_REPORT_PULL_TOKEN` of the service, with at least 32 characters. Required when the stage runs. The worker never prints it |

A missing or invalid value stops the worker with exit code 2. The stage pulls when the worker starts and then every 5 minutes by the database clock. Before each pull it deletes the closed reports that were resolved more than 365 days ago. A failed deletion is logged at error level and the pull still runs. A call has a limit of 30 seconds. A failure is logged at error level with its exception on stderr, under the stage name `reportPull`. It counts in the stage result. The cursor stays, so the next tick asks again.

To try it on the dev machine, run the search service as in [run the search service](#run-the-search-service) with a pull token and send a report. Start the worker with the three variables above, the service address, and the same token. The next tick shows the report at `/Review/Reports`. A restart of the worker pulls at once.

## Publish a static release

The owner can publish from the Review home page. Select records in the Publish section and choose Review selection. The list shows each record as a document change, a statement, an action, a vote, a relationship, or a brief. The summary lists each headline with its kind, as new or a replacement, and shows the release number. Write a correction note in each replacement row. A withdrawn record shows a checkbox once review approves a revision above the withdrawn revision. Until then the row tells you to revise it. A returning record is marked Returning and needs a note. Choose Publish to confirm. The page lists recent releases read-only. The Blocked by review panel lists the published records that review blocked, with the withdraw command for each. Rollback stays in the CLI.

The Review page cannot withdraw a record. The CLI is a local owner command. Set `CIVIC_LENS_DATABASE`, `CIVIC_LENS_REVIEW_OWNER` (the owner subject recorded on the release), and `CIVIC_LENS_RELEASE_DIRECTORY` (an absolute path, for example `$PWD/.runtime/releases`). Publishing also needs `CIVIC_LENS_COLLECTION_CONFIG`, an absolute configuration path that supplies the officials' display names, and `CIVIC_LENS_ISSUES_CONFIG`, an absolute path that supplies the issue and policy area names. `CIVIC_LENS_ARTIFACT_DIRECTORY` is required. Set it to the capture directory (the worker `<artifact-directory>`, for example `$PWD/.runtime/captures`). `releases publish` stops with exit code 2 before it runs when the variable is unset, and prints its generic invalid-configuration line. The review workspace stops at startup with exit code 2 when it has `CIVIC_LENS_RELEASE_DIRECTORY` and no artifact directory. Its configuration line names the artifact directory. A photo that the previous release holds is linked and its capture is not read. A new photo that cannot be read does not fail the publish. The release keeps the earlier photo or shows none, and the log holds an error with the person and the attempt. The worker stage uses the directory of its own command line, and the review workspace reads the same variable. A new photo or another change to an official profile appears at the next publish. To publish it now, run `releases publish <idempotency-key>` with no record. The command refuses to publish when no record and no official changed, unless you add `--rewrite-officials`. It needs an active release. The command trusts the local operator, who already holds database credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases publish <idempotency-key> [<kind>:<id>...] [--note <kind>:<id> <text>]... [--withdraw <kind>:<id> <text>]... [--rewrite-officials]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases list [limit]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases blocked
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases activate <release-number>
```

A development database from before schema version 1 cannot publish. The publisher reads the active release and refuses a release of another schema version. Follow [Dev database reset](#dev-database-reset) once. The dates of the first publication of briefs start again after the reset. This is acceptable in development.

Write each record as `<kind>:<id>`. The kind is `documentChange`, `statement`, `action`, `vote`, `brief`, or `relationship`. For example, use `documentChange:` and a 32-character draft ID, `statement:` and a 64-character candidate ID, or `action:` and a 64-character action record ID. The other three kinds take a 64-character record ID too. The command never guesses the kind from the ID. An unknown kind or a bad shape exits with code 2. The revision must be approved with no unresolved concerns. A record that replaces a record of the active release needs a note. Give it with `--note <kind>:<id> <text>`, and repeat the option for each replacement. A note has 1 to 500 characters. The note shows on the record page as a correction. A note for a new record, or for a record that you did not name, exits with code 2.

To take a record out of the release, give `--withdraw <kind>:<id> <text>`. A request can have withdrawals and no records. The text has 1 to 500 characters and shows on the notice page. Review must have blocked the record: its status is Approval withdrawn, Changes requested, or Awaiting review with concerns. The publish exits with code 2 in three cases:

- The record is not in the active release.
- Review did not block the record.
- A relationship or a brief that stays in the release cites the record. The message lists the citing records. Withdraw them in the same request.

To list the published records that review blocked, run `releases blocked`. It prints the kind, the ID, the headline, the published revision, and the review status of each record. The list is empty when no release is active. Use it to choose the records to withdraw.

To write the pages of all officials again, add `--rewrite-officials`. The publish then treats every official as touched. The option itself adds no record. It keeps the release history, the withdrawals, and the first publication date of each record. A rewrite with no named record needs an active release. Use it once after a deploy that changes the layout of the official pages. Until then an untouched official keeps its old page. On a failure, run the same command again. See the [publication limits](architecture/publication.md#limits-and-planned-work).

After the deploy that ships the Congress tabs, run this once:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases publish <idempotency-key> --rewrite-officials
```

### Return a record

To bring a withdrawn record back, first revise it in review and approve the new revision. The revision number must be higher than the withdrawn revision. Return it from the Review page, or with the CLI: publish the withdrawn ID with a note: `releases publish <idempotency-key> <kind>:<id> --note <kind>:<id> <text>`. The note shows on the record page as a correction. The publish exits with code 2 if the note is missing or the revision is not higher. The code subject cannot return a record. The worker only logs the withdrawn record and skips it.

A relationship or a brief that cited the record stays withdrawn. Publish it again, in the same request or later.

A concurrent review decision, draft save, other release, or rollback makes the commit fail with exit code 1. The built directory is deleted and nothing is activated. Invalid input or a non-owner actor exits with code 2 and prints the rule that failed. The publish rules are in [publication](architecture/publication.md#publish).

Repeat the same key and records to receive the original result. The repeat also repairs a link swap that failed after the commit. To roll back, activate an earlier release number. If the files of that release are gone, the command prints "Release N has no files on this machine, so it cannot be activated.", exits with code 2, and changes nothing. The next `publish` builds on the active release, so records added after it are not carried forward. `list` shows `activeReleaseNumber`, the release that Postgres records as active, and `servedDirectoryName`, the release that the link serves. If they differ, run `releases activate` with the active release number. Each publish prunes old release folders; see [release retention](architecture/publication.md#release-retention).

The command `releases activate` prints one object with four fields. `summary` is the activated release. `restoredWithdrawals` lists records that the replaced release withdrew and the activated release shows again. `withdrawnAgain` lists records that the activated release withdraws and the replaced release showed. `blocked` lists records of the activated release that review now blocks. The lists describe the release that the link serves. Read them after each rollback. A rollback to a release that a prune removed is refused with "has no files on this machine", and nothing changes. Each publish keeps the files of the 30 highest release numbers, of every release younger than 48 hours, and of the newest older release. It also keeps the active release and the release that `current` serves. A failed read gives a warning on standard error and empty lists, and the switch stays done. If the manifest of the activated release cannot be read, all three lists are empty. If the manifest of the replaced release cannot be read, `restoredWithdrawals` and `withdrawnAgain` are empty. If the review read fails, `blocked` is empty.

To check that the public pages work without the operational application, stop the review workspace, the worker, and Postgres. Then serve the active release with a plain static server:

```sh
python3 -m http.server 8090 --bind 127.0.0.1 --directory "$CIVIC_LENS_RELEASE_DIRECTORY/current"
```

Open `http://127.0.0.1:8090/`. The home page shows the latest brief, the 20 newest records, and links to the issue and policy area pages. `all.html` shows 50 records for each page, with links to the previous and next pages, and a kind label next to each headline. Every page links to `search.html`. A document change page shows the account, the changes, the citations, and both full document versions. A statement page shows the speakers, the statement date with its evidence, each quotation with its kind, and the full source text with the quotations marked. An action page shows the bill, a sponsorship table with cited quotes, the policy area and CRS summary citations, and its Congress.gov sources. A statement, action, or vote page lists the related records that published relationships link to it. A relationship page shows the quote, the fact, and the two linked records. The production static server is still a deployment decision.

## Run the search service

The search service reads a published release over HTTP and keeps its own Postgres database. It needs no operational credential. The design is in [search](architecture/search.md).

Start a separate Postgres container. Use another port and another volume than the operational database. Port 5434 differs from the port 5433 of `make db`:

```sh
docker run -d --name civic-lens-search-db -e POSTGRES_USER=search -e POSTGRES_PASSWORD=search -e POSTGRES_DB=search \
  -p 127.0.0.1:5434:5432 postgres:18.3-alpine@sha256:54451ecb8ab38c24c3ec123f2fd501303a3a1856a5c66e98cecf2460d5e1e9d7
```

Serve the active release root with a plain static server:

```sh
python3 -m http.server 8090 --bind 127.0.0.1 --directory "$CIVIC_LENS_RELEASE_DIRECTORY/current"
```

Run the service in a second shell. It applies its schema at startup, ingests the release, and polls every 30 seconds:

```sh
export CIVIC_LENS_RELEASE_BASE_URL=http://127.0.0.1:8090/
export CIVIC_LENS_SEARCH_DATABASE="Host=127.0.0.1;Port=5434;Database=search;Username=search;Password=search"
export ASPNETCORE_URLS=http://127.0.0.1:8100
dotnet run --project src/CivicLens.PublicApi
```

Check it with `curl 'http://127.0.0.1:8100/v1/release'` and `curl 'http://127.0.0.1:8100/v1/search?q=housing'`. A 503 reply means that the first ingest is not done. Stop the service with Ctrl+C. Remove the database with `docker rm -f civic-lens-search-db`.

Reader reports need three more variables. Without the Turnstile secret, `POST /v1/reports` answers 503. Without the pull token, `/internal/v1/reports` does not exist.

```sh
export CIVIC_LENS_TURNSTILE_SECRET=1x0000000000000000000000000000000AA
export CIVIC_LENS_TURNSTILE_HOSTNAME=localhost
export CIVIC_LENS_REPORT_PULL_TOKEN="$(openssl rand -hex 24)"
```

The secret above is the always-pass test secret of Cloudflare, with the dummy token `XXXX.DUMMY.TOKEN.XXXX`. The secret `2x0000000000000000000000000000000AA` always fails. The hostname that siteverify returns for test keys needs a check against a live reply. The Cloudflare page shows `localhost` only as an example. Set `CIVIC_LENS_TURNSTILE_HOSTNAME` to the site hostname in production. Optional limits: `CIVIC_LENS_REPORTS_PER_HOUR` (default 5 for each client) and `CIVIC_LENS_REPORTS_PER_DAY` (default 200).

To keep the variables between runs, put them in `.runtime/search/api.env` as `NAME=value` lines and load them with `set -a; . .runtime/search/api.env; set +a`. The `.runtime` folder is ignored by Git. Never commit a real secret or token.

Test a report with `curl -d recordId=<64 hex characters> -d revision=1 -d category=wrong-date -d cf-turnstile-response=XXXX.DUMMY.TOKEN.XXXX http://127.0.0.1:8100/v1/reports`. Pull with `curl -H "Authorization: Bearer $CIVIC_LENS_REPORT_PULL_TOKEN" 'http://127.0.0.1:8100/internal/v1/reports'`.

The `/internal/*` route is not public. The [Caddyfile](#web-server) answers it with 404 on the API host.

To use the search page, set `CIVIC_LENS_PUBLIC_API_URL=http://127.0.0.1:8100` in the environment of the process that publishes (the `releases publish` command or the review workspace), and serve the release root from a server that sends no cross-origin limit. The service allows any origin. Without the variable, `search.html` says that search is not available.

To give each page a canonical address, set `CIVIC_LENS_PUBLIC_BASE_URL` in the same environment, for example `https://civic-lens.info`. The value must be a bare origin: https, or http only for `localhost` and `127.0.0.1`, with no user, path, query, or fragment. Use a host name, not an IPv6 address, and do not end the host with a dot. A trailing slash is removed. The stored value is in lower case, with a punycode host and no default port. A blank value counts as unset. An invalid value stops `releases publish` with exit code 2. It also stops the worker at start when publishing is on, because that composition reads the value then. It stops the review workspace at start when `CIVIC_LENS_RELEASE_DIRECTORY` is set. With the variable set, a release holds `sitemap.xml`, `robots.txt`, `404.html`, and a canonical link and `og:url` on every page except `404.html`. Without it, a release holds none of these. The process reads the variable once, so restart the review workspace and the worker after a change. Set the variable before the first production publish. A page that a publish carries by hard link keeps the address it was written with.

To use the report page, also set `CIVIC_LENS_TURNSTILE_SITE_KEY` in the environment of the process that publishes. For a local trial use the Cloudflare test site key `1x00000000000000000000AA`, which always passes. Publish, serve the release root, and open a record page. Use its link "Report a problem with this record". Send the report, then run the pull command above to see it. Without the API address or the site key, `report.html` says that reporting is not available.

Optional variables: `CIVIC_LENS_TRUSTED_PROXY_RANGES` (empty in development) and `CIVIC_LENS_REQUESTS_PER_MINUTE` (default 60). In production, set the ranges to `127.0.0.0/8,::1/128`. Each range needs a prefix length, and the service stops at start when one has none. The tunnel and Caddy run on the same machine, so the peer is loopback. Caddy passes the `CF-Connecting-IP` header that Cloudflare sets. The container image of the search service is in [Container images](#container-images).

## Container images

The `Dockerfile` in the repository root builds two images for linux-arm64. The workflow `.github/workflows/release.yml` builds them on a native ARM64 runner. On main, from a push or a manual run, it pushes them to ghcr.io and adds a build provenance attestation to each. On a pull request that changes the build inputs, it builds them and does not push or attest. Pull request runs never wait for or cancel main runs. A newer main run can cancel a queued main run, so deploy only a commit whose image tag exists.

| Image | Holds | Entry point |
| --- | --- | --- |
| `ghcr.io/young-kobe/civic-lens-host` | The host in `/app/host` and the collector in `/app/collector` | `dotnet CivicLens.Host.dll` |
| `ghcr.io/young-kobe/civic-lens-search` | The search service only, in `/app/search` | `dotnet CivicLens.PublicApi.dll` |

- Run each host command, the worker and the review workspace as arguments of the host image. The worker takes the collector path `/app/collector/CivicLens.Collector.dll` and a capture directory that you mount.
- The search image holds no host, application, or infrastructure assembly. This keeps publication isolation.
- Both images run as user and group 10001:10001. Give each bind-mounted directory to this user.
- Both images hold `libgssapi-krb5-2`. Without it, the process prints a harmless error about `libgssapi_krb5.so.2` each time Npgsql opens a connection.
- The images hold no secret, no configuration file, and no runtime data. Pass them as environment variables and mounts.
- After the first run on main, set the packages `civic-lens-host` and `civic-lens-search` to public in the ghcr package settings. Do this one time by hand. The Jetson then pulls without a token.
- Each image has two tags: the full commit SHA and `main`. Deploy by the commit SHA. Pull one with `docker pull ghcr.io/young-kobe/civic-lens-host:<commit-sha>`. Check its attestation with `gh attestation verify oci://ghcr.io/young-kobe/civic-lens-host:<commit-sha> --repo young-kobe/civic-lens`.
- Every base image is pinned by digest, and the tag is in a comment. The SDK image must equal the `global.json` version. The aspnet image must equal the runtime of that SDK (see the .NET release notes). To update a base image, change the digest and the comment together.
- The `libgssapi-krb5-2` package is not pinned. Each build installs the current version from the distribution archive.

## Web server

`deploy/caddy/Caddyfile` configures Caddy for the temporary production machine. Public traffic goes from Cloudflare through the `cloudflared` tunnel to Caddy. Cloudflare ends TLS, so Caddy speaks plain HTTP and has `auto_https off`. Caddy listens on `127.0.0.1` only (`default_bind`), and its admin endpoint is off. `cloudflared` targets `http://127.0.0.1:<port>`. The review workspace is not behind Caddy.

| Variable | Meaning |
| --- | --- |
| `CIVIC_LENS_CADDY_PORT` | The loopback port of Caddy. Default 8080. The tunnel points both host names at it |
| `CIVIC_LENS_RELEASE_DIRECTORY` | The release root. The same variable as for publishing. Required. Caddy serves `<root>/current`, the link that a release activation swaps |
| `CIVIC_LENS_API_PORT` | The loopback port of the search service. Default 8100. It must equal the port in `ASPNETCORE_URLS` of the search service. The compose file, in a later change, will derive one from the other |

- `civic-lens.info` serves the static release. `file_server` uses the precompressed `release.json.gz` and `release.json.br`, the only compressed files that a publish writes. Caddy compresses other responses when it sends them. A missing path gets the release `404.html` with status 404. Other error statuses, for example 405, get a plain text body. The same block also answers `127.0.0.1`, so the search service on the same machine can read the release without the tunnel. A request for any other host name gets 404.
- `api.civic-lens.info` proxies to the search service. `/internal/*` gets 404 and never reaches the service. Caddy passes the `CF-Connecting-IP` header unchanged. Cloudflare sets it on every request. It refuses a request that already carries the header, with status 403 and error code 1000. The service trusts it because the peer is loopback.
- `Cache-Control` values: `release.json` and every other file, HTML pages and assets included, have `public, max-age=60`. A browser that cached a page before a publish can show the old page for up to 60 seconds after the publish. The search service reads Caddy on loopback, not through the CDN. Only `photos/<64 hex characters>.jpg` has `public, max-age=31536000, immutable`, because the name is the hash of the content. Error pages have `no-store`.
- Every page, asset, and error that Caddy answers from its own blocks has `X-Content-Type-Options: nosniff` and `Referrer-Policy: strict-origin-when-cross-origin`. A 502 from the search proxy and a 404 when the release has no `404.html` carry neither header. No Content-Security-Policy is set.
- Caddy sends no `X-Robots-Tag` header, so search engines can index the site. The published pages set `noindex` only on the pages that a reader must not land on from a search. `robots.txt` stays as published.
- Never add a `log` directive to a site block. Without one, Caddy writes no access log. The privacy page says that page reads and searches leave no record. Caddy still writes error lines, for example when `404.html` is missing. The global `log` block in the Caddyfile filters them. They keep the host, method, status, and error. They hold no query string, headers, or IP address. A file error can name the file path of the page, with no reader data. Do not weaken that filter.
- Check the rule with `caddy adapt`. The output must have no `logs` key inside `apps.http.servers`, and the global `logs` entry must delete `request>uri`, `request>headers`, `request>remote_ip`, `request>remote_port`, and `request>client_ip`. `deploy/scripts/check-live.sh` checks the live log; see [Go-live checks](#go-live-checks).

Check the file with `caddy validate --config deploy/caddy/Caddyfile --adapter caddyfile`, with `CIVIC_LENS_RELEASE_DIRECTORY` set.

## Dev database reset

Reset the development database once, if it was created before the initial migration. The migrations are one initial migration, and the protocol, configuration, schema, parser, and behavior versions start at 1. The code reads no row, capture, or release from before the initial migration. Production has no older data and needs no reset.

A dev statement that was published before `textOrigin` and `approvedBy` existed lacks both fields, and a reader refuses it. Publish those statements again, or reset the database.

The reset also covers the term kind change. Each term of `officials.json` now requires `kind`. `PreviousRelease.ReadOfficialsAsync` throws on a release without it, so a publish fails until the reset.

The reset also covers the linked pairs change. A relationship revision now holds the matched official. An older revision reads with no matched official, so a person edit of it and its publish fail. Release manifests now hold link fields and `billTitle`, and an older release does not read. Reset once after this change merges.

1. Stop the services that use the database: the `make review` command (proxy, review workspace, and worker), the static server, and the search service. Stop any other `dotnet` host that runs.
2. Drop the database. The container name, user, and database name come from the ignored env file:

```sh
source .runtime/database/postgres.env
docker exec civic-lens-postgres psql -U "$POSTGRES_USER" -d postgres -c "DROP DATABASE \"$POSTGRES_DB\" WITH (FORCE)"
```

3. Delete the saved releases and captures. The capture directory also holds the `.pending` receipt handoffs:

```sh
rm -rf .runtime/releases .runtime/captures
```

4. Recreate the search container, so that an old release 1 in its database cannot hide the new release 1:

```sh
docker rm -f civic-lens-search-db
```

   Then run the `docker run` command in [Run the search service](#run-the-search-service). Start the service again only after the first publish.
5. Run `make migrate`. It starts the Postgres container if needed, creates the database, and applies the migrations.

Release numbers restart at 1 after this reset.

A vote source change needs a reset only when the database holds Senate vote drafts from before the `source` field. A vote file and a vote revision with no `source` read as Congress.gov, which is right for every House vote. A stored Senate revision with no `source` breaks the pairing rule of `VoteDraftRevision.ValidateShape`. Run this check. If the count is 0, do not reset for this change.

```sql
SELECT count(*) FROM vote_drafts WHERE chamber = 'Senate';
```

Production has no Senate vote drafts, because senate.gov returned 403. Production needs no reset for this change.

A release contract change can add a required field to a published file. Old files then do not read, and the next publish fails. On the dev machine, repeat steps 1 to 5. These steps delete all captures and records, not only the releases. Collect again and let code approve the facts, then publish a fresh release with `releases publish` as in [Publish a static release](#publish-a-static-release). Production has no release yet, so it has no older files.

## Production compose

`deploy/compose.yaml` runs the temporary production machine with Docker Compose v2. It starts two Postgres containers, the worker, the review workspace, the search service, Caddy, and cloudflared. Check out the repository at the commit that you deploy, because Compose mounts `deploy/caddy/Caddyfile` from the checkout. The images are described under [Container images](#container-images). Backups run from the same file; see [Production runbook](#production-runbook). The heartbeat is not in this file.

- The worker, the review workspace, the search service, Caddy, and cloudflared use host networking. The review workspace keeps its loopback listener and the search service trusts only loopback proxies. Reach the review workspace through `tailscale serve`, not through Caddy.
- The Postgres containers publish `127.0.0.1:5433` (operational) and `127.0.0.1:5434` (search). The connection strings in the secret env files name these ports. Change both places together.
- The root is `/srv/civic-lens`, the Linux convention for data that a machine serves. To use another root, give `CIVIC_LENS_ROOT` to every script, for example `sudo CIVIC_LENS_ROOT=/data/civic-lens deploy/scripts/update.sh <sha>`. `sudo` drops an exported value. Every directory is mounted at the same path inside the containers, so one path value serves both sides.
- Under the root, `env/` holds the env files, `config/` holds `collection.json` and `issues.json`, and `captures/`, `releases/`, `review-keys/` and `postgres/` hold data. `captures/`, `releases/` and `review-keys/` belong to user 10001, the user of our images. The two Postgres directories belong to user 70, the `postgres` user of the Alpine Postgres image.
- `compose.env` holds the image tag and the three ports (`CIVIC_LENS_CADDY_PORT`, `CIVIC_LENS_API_PORT`, `CIVIC_LENS_REVIEW_PORT`). The search address and the Caddy proxy target come from `CIVIC_LENS_API_PORT`. The release address of the search service comes from `CIVIC_LENS_CADDY_PORT`.
- Secrets are in files that end in `.secret.env` and have mode 600. Each process reads only its own files. The database password is in two files, the Postgres file and the connection string. Keep the two equal. The report pull token is in the host and search files. Keep these two equal too.
- The AI stages, publishing, and report pull are off while their variables are empty. Set `CIVIC_LENS_PUBLISHING=1`, `CIVIC_LENS_REPORT_PULL=1`, and the `*_DAILY_TOKENS` values to turn them on. Publishing also needs the drafting stages; see [Automatic publication](#automatic-publication).
- The search service reads the release from Caddy at `127.0.0.1` on `CIVIC_LENS_CADDY_PORT`, so it does not need the tunnel.

Run the scripts with `sudo`, because they write under the root and the env files hold secrets.

```sh
sudo deploy/scripts/install.sh
sudo deploy/scripts/update.sh <full-commit-sha>
sudo deploy/scripts/compose.sh run --rm cli releases list
```

`install.sh` creates the directories and copies each `deploy/env/*.env.example` file only when the target is absent. It never overwrites a file. Fill in the env files and the two config files, then run `update.sh`.

`update.sh` stops when the checkout is not at the given SHA, when `deploy/` has uncommitted changes, or when the Caddy port in `compose.env` differs from the port in the tunnel config. It sets the image tag for the run and pulls the images. It stops the worker, so the old worker never runs on the new schema. Then it applies the migrations with a one-off `cli` container. Then it writes the tag to `compose.env`, because the schema now belongs to that commit. Last, it recreates the services, and it always recreates Caddy and cloudflared so that a changed Caddyfile or tunnel config takes effect. If a step after the migration fails, run `update.sh` again with the same SHA. If the migration fails, the worker stays stopped. Fix the cause and run `update.sh` again, or run `sudo deploy/scripts/compose.sh up --detach worker` to start the old worker. Services never migrate at start. The tag must be a full commit SHA whose images exist. To roll back, run `update.sh` with an earlier SHA. A rollback does not undo a migration.

`compose.sh` is `docker compose` with the compose file and `compose.env` set. The `cli` service starts the host image with no command. The `restic` service has the same `tools` profile. Add the host command after `cli`, for example `releases publish` or `db migrate`.

## Production runbook

This section is for the temporary production machine, a Jetson with Docker Compose v2. The long-term cloud move will use Terraform. Terraform state never goes to R2, because it holds credentials. The first launch uses no Terraform.

**Vendor data rule.** Cloudflare may receive public content and the encrypted backups. Nothing else leaves the machine. The review workspace is never behind Cloudflare, because it holds review notes and reviewer identity.

### First install

1. Install Docker with Compose v2. Clone the repository to `/opt/civic-lens` and check out the commit to deploy. The systemd units use this path. It must be on the root filesystem, because systemd loads the linked units at boot, before other disks mount.
2. Run `sudo deploy/scripts/install.sh`. It creates the directories under `/srv/civic-lens` and the env files from the templates.
3. Fill in the env files in `/srv/civic-lens/env`, and put `collection.json` and `issues.json` in `/srv/civic-lens/config`.
   The `api` sources in `config/federal-coverage.json` ship with `enabled: false`. Without them the site has no bills, votes, roster, or photos, so nothing publishes. Set `enabled` to `true` for `congress-members`, `congress-bills`, `congress-house-votes`, `voteview-senate-votes`, `member-photos`, `executive-officials`, and `federal-register-documents`. See [Voteview Senate vote stream](#voteview-senate-vote-stream) for the Senate votes. The two Congress.gov sources need `CIVIC_LENS_CONGRESS_API_KEY` in `host.secret.env`. See [Congress.gov bill stream](#congressgov-bill-stream) and [Member roster](#member-roster). The Host refuses a configuration file over 1 MB, and added indentation can push this file past that limit. So save it compact.
   Set `CIVIC_LENS_COLLECTION_CONCURRENCY=3` in `/srv/civic-lens/env/host.env`.
4. Create the tunnel (see [Tunnel](#tunnel)) and the restic password file and repository (see [Backups](#backups)).
5. Run `sudo deploy/scripts/update.sh <full-commit-sha>`.
6. Set up Tailscale for the review workspace (see [Review workspace access](#review-workspace-access)).
7. Enable the backup timer.
8. Run `sudo deploy/scripts/check-live.sh`. See [Go-live checks](#go-live-checks).

Do step 5 only after step 4. Without the credential file, the cloudflared container cannot start.

On an existing deploy, copy the two White House source lines into `/srv/civic-lens/config/collection.json`, then restart the worker. A restart reads the configuration again.

### Go-live checks

Run `sudo deploy/scripts/check-live.sh` from `/opt/civic-lens` after the first install and after each `update.sh`. The script prints one PASS or FAIL line per check, runs every check, and exits with code 1 if one failed. Each FAIL line says what it expected, what came back, and what to check. It sends requests to the public host names, so it needs the tunnel. The checks are:

- `X-Robots-Tag: noindex` on the home page and on an api response. The script reads `deploy/caddy/Caddyfile`. With an `import noindex` line, each response must carry the header. Without one, no response may carry it.
- A missing page returns 404 with the release 404 page and `Cache-Control: no-store`.
- `api.civic-lens.info/internal/v1/reports` returns 404. With a pull token set, the search service answers that route with 401, so a 404 proves that Caddy blocks it. Without a token, the route does not exist and the check proves nothing.
- Caddy keeps no access log. The script reads your address from `/cdn-cgi/trace` of the site, then requests a missing path with a random query value. Then it reads the Caddy log of the last 5 minutes. The log must hold neither the query value nor your address.
- A backup succeeded less than 26 hours ago. `backup.sh` writes the time to `/srv/civic-lens/backup/last-success` as its last step, so a failed run leaves the old time. A reboot does not clear the file.
- A reader cannot forge `CF-Connecting-IP`. A request that carries the header gets status 403 from Cloudflare.
- The search service limits each reader. The script reads `CIVIC_LENS_REQUESTS_PER_MINUTE` from `search.env`, with 60 as the default. It fails if `CIVIC_LENS_TRUSTED_PROXY_RANGES` is empty, because then all readers share one limit. It sends twice the limit plus two requests to the search route, 20 at a time, and one must get 429. The limit counts in fixed one-minute windows, so this number fills one window even when a window ends during the check. This check runs last. It blocks the address of the caller on the search service for up to one minute.

**Withdraw test.** The script does not test withdrawal, because the test changes the live site. Do it by hand at the first launch and after a change to publication code. Do it only when a record is ready to publish, for example an approved statement. The rollback in step 5 pauses the publishing stage, and the publish in step 7 ends the pause; see [publication](architecture/publication.md).

1. Stop the worker, so that no fact release lands during the test: `sudo deploy/scripts/compose.sh stop worker`.
2. In the review workspace, request changes on one published record. Pick a record that no relationship or brief cites, or the withdrawal stops with exit code 2.
3. Withdraw it: `sudo deploy/scripts/compose.sh run --rm cli releases publish <idempotency-key> --withdraw <kind>:<id> <text>`. Use a new key.
4. Check that the page of the record says it is withdrawn.
5. Read the release numbers with `sudo deploy/scripts/compose.sh run --rm cli releases list`. Activate the release from before the withdrawal: `sudo deploy/scripts/compose.sh run --rm cli releases activate <release-number>`. The record shows again. The `blocked` list in the output names it, because review still blocks it.
6. In the review workspace, approve the record again. The approval must resolve the open concern and carry a resolution note.
7. Publish a different ready record, for example the approved statement: `sudo deploy/scripts/compose.sh run --rm cli releases publish <idempotency-key> <kind>:<id>`. Do not use the record from step 6. The active release already holds it at its current revision, so the publish refuses it. Then check with `releases list` that the active release is the latest release.
8. Start the worker: `sudo deploy/scripts/compose.sh up --detach worker`.

The `cli` service gets every variable that these commands need from `deploy/compose.yaml` and the host env files.

### Tunnel

`deploy/cloudflared/config.yml` is the tunnel config. It sends `civic-lens.info` and `api.civic-lens.info` to Caddy at `http://127.0.0.1:8080`, and every other host name gets 404. The port equals the default of `CIVIC_LENS_CADDY_PORT`. If you change that variable, change the file in the same commit. `update.sh` stops when the two differ. The review workspace is not in the config.

1. Install the `cloudflared` package on the machine for this one-time setup. The service uses the pinned image. Run `cloudflared tunnel login` and pick the `civic-lens.info` zone.
2. Run `cloudflared tunnel create civic-lens`. It prints the tunnel ID and writes `<tunnel-id>.json`.
3. Move that file to `/srv/civic-lens/cloudflared/credentials.json`. Run `sudo chown 10001:10001` and `sudo chmod 600` on it. The credential file stays on the machine and never goes to Git.
4. Set `CIVIC_LENS_TUNNEL_ID` in `/srv/civic-lens/env/compose.env` to the tunnel ID.
5. Create the DNS records: `cloudflared tunnel route dns civic-lens civic-lens.info` and `cloudflared tunnel route dns civic-lens api.civic-lens.info`.
6. Delete `cert.pem` from the cloudflared home folder after step 5. The tunnel does not need it.

### Review workspace access

The review workspace uses Tailscale. Cloudflare Access is not used.

1. Install Tailscale on the machine and sign in. Enable HTTPS certificates in the Tailscale admin console.
2. Run `sudo tailscale serve --bg --https=443 http://127.0.0.1:5081`. The review port equals `CIVIC_LENS_REVIEW_PORT`.
3. Set `CIVIC_LENS_REVIEW_ORIGIN` in `host.env` to `https://<machine>.<tailnet>.ts.net`, the exact origin that `tailscale serve status` shows. Run `update.sh` again.
4. Give each reviewer access to the machine in the tailnet.

### Vendor checklist

No Terraform manages these steps. Check each one by hand.

- Cloudflare: the zone `civic-lens.info` exists. The tunnel exists and has two DNS routes. A Turnstile widget exists, and its site key and secret are in the env files. An R2 bucket exists for the backups, with an API token that is limited to that bucket.
- Auth0: a regular web application exists. Its allowed callback URL is `<Tailscale origin>/signin-oidc`. It needs no logout URL, because sign-out clears only the review cookie. The owner subject and reviewers are in `host.env`.
- Tailscale: HTTPS certificates are on, and `tailscale serve` runs.

### Backups

A nightly systemd timer runs `deploy/scripts/backup.sh`. The script takes a lock, so two runs cannot overlap. It dumps the operational database with `pg_dump --format=custom` inside the Postgres container. Then it runs restic from the pinned official image. It backs up the dump, `config/`, `captures/`, and `releases/`, in one snapshot. Then it keeps 7 daily, 4 weekly, and 6 monthly snapshots and prunes the rest. Last, it writes the time to `backup/last-success`, which [Go-live checks](#go-live-checks) reads. Restic encrypts the data before it leaves the machine. The repository is a Cloudflare R2 bucket.

The About page says a backup can keep a closed reader report for up to 6 months after its deletion. If you change `--keep-monthly`, change the page too. A backup outage of several weeks can also break that claim, because the prune does not run.

The backup does not hold the search database, because a publish rebuilds it. It does not hold env files, secrets, or Terraform state. It does not hold `review-keys/`. If the data-protection keys are lost, reviewers must sign in again. Nothing else breaks.

Set up the backup one time:

1. Fill in `/srv/civic-lens/env/backup.secret.env` with the R2 repository URL and the API token.
2. Make the restic password file: `sudo sh -c 'umask 077; openssl rand -base64 32 > /srv/civic-lens/env/restic-password'`. Keep an offline copy of the password. Without it, no backup can be read.
3. Create the repository: `sudo deploy/scripts/compose.sh run --rm restic init`.
4. Link the units. The alert test in step 5 needs the alert unit.

```sh
sudo systemctl link /opt/civic-lens/deploy/systemd/civic-lens-backup.service /opt/civic-lens/deploy/systemd/civic-lens-backup.timer /opt/civic-lens/deploy/systemd/civic-lens-alert@.service
```

5. Set up the failure alert (see [Failure alerts](#failure-alerts)).
6. Enable the timer: `sudo systemctl enable --now civic-lens-backup.timer`.

Day-to-day commands:

```sh
sudo systemctl start civic-lens-backup.service
sudo journalctl -u civic-lens-backup.service
systemctl list-timers civic-lens-backup.timer
systemctl --failed
sudo deploy/scripts/compose.sh run --rm restic snapshots
```

The first backup run by hand also proves the credentials. The timer runs at 03:30 local time. Check `list-timers` and `--failed` every week. A timer that never runs sends no alert, so the last run time must be less than a day old.

**Restore.** Test this procedure once before you need it, on a spare machine or directory.

1. Stop the backup timer and the services that use the databases: `sudo systemctl stop civic-lens-backup.timer` and `sudo deploy/scripts/compose.sh stop worker review search search-postgres`.
2. Restore the snapshot to a work directory: `sudo deploy/scripts/compose.sh run --rm -v /srv/restore:/restore restic restore latest --target /restore`. The files keep their full paths, for example `/srv/restore/srv/civic-lens/captures`.
3. Drop and create the operational database again. A restore into the old database would keep the tables of newer migrations. Start Postgres if it is down. Then run:

```sh
sudo deploy/scripts/compose.sh exec -T postgres sh -c 'psql --username="$POSTGRES_USER" --dbname=postgres --command="DROP DATABASE \"$POSTGRES_DB\" WITH (FORCE)" --command="CREATE DATABASE \"$POSTGRES_DB\" OWNER \"$POSTGRES_USER\""'
sudo deploy/scripts/compose.sh exec -T postgres sh -c 'pg_restore --single-transaction --exit-on-error --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' < /srv/restore/srv/civic-lens/backup/operational.dump
```

4. Move the old folders aside, then copy the restored folders in. A copy into the old folders would keep releases newer than the snapshot, and the next publish would reuse their numbers. `cp -a` keeps the owners, the modes, and the hard links between releases.

```sh
for name in config captures releases; do
  sudo mv "/srv/civic-lens/$name" "/srv/civic-lens/$name.old"
  sudo cp -a "/srv/restore/srv/civic-lens/$name" /srv/civic-lens/
done
```

   Check that `releases/current` is a link to the active release.
5. Empty the search database, because it can hold releases newer than the snapshot. The search service would serve them again when their numbers come back.

```sh
sudo mv /srv/civic-lens/postgres/search /srv/civic-lens/postgres/search.old
sudo install -d -m 700 -o 70 -g 70 /srv/civic-lens/postgres/search
```

6. Start the services with `update.sh`. The search service ingests the active release when it starts, and then every 30 seconds. Wait for one poll, then search on the site.
7. Start the timer: `sudo systemctl start civic-lens-backup.timer`. When the site works, delete `/srv/restore` and the `.old` folders.

The dump file stays in `/srv/civic-lens/backup`, mode 700, until the next run replaces it.

### Failure alerts

If `civic-lens-backup.service` fails, systemd starts `civic-lens-alert@civic-lens-backup.service`. That unit runs `deploy/scripts/alert.sh` with the failed unit name. The script sends one email through the Resend HTTP API from `alerts@civic-lens.info`. The body holds the unit name, the host name, the time, and the command `journalctl -u <unit>`. It holds no log line and no other data. Resend receives only this text. If the send fails, the script exits with an error in the journal of the alert unit. Other jobs can use the script later: give it any unit name.

1. In Resend, add and verify the `civic-lens.info` domain. Add the DNS records that Resend shows in the Cloudflare zone.
2. Make a send-only API key in Resend.
3. Fill in `/srv/civic-lens/env/alert.secret.env` with the key and the recipient address. The file has mode 600. The script passes the key to `curl` in a temporary header file, not on the command line.
4. Check that `curl --version` shows version 7.55 or later. The script needs `curl` on the host.
5. Test: `sudo systemctl start civic-lens-alert@civic-lens-test.service`. An email arrives that names the unit `civic-lens-test`. Read `sudo journalctl -u civic-lens-alert@civic-lens-test.service` if it does not.

### No page logs

Caddy keeps no access log for page reads or searches. The About page privacy claims depend on this. Do not add a `log` directive to a site block. See [Web server](#web-server).

## Runtime data

Runtime data belongs in ignored .runtime/. Generated publication and evaluation artifacts belong in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. The deploy files are in [Production compose](#production-compose). Published releases live under the configured release directory, not in the repository. The MCP runbook will come here when it exists.
