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

CI runs the tests in 4 parallel jobs, each with its own Postgres container. `tools/test-shards.py` assigns each test class to one job. It sorts classes by the seconds in `tools/test-shard-weights.json`, heaviest first, and gives each to the lightest shard. A class missing from the table gets the mean weight. To refresh the table, run the full suite with `--logger trx --results-directory /tmp/trx`, then run `python3 tools/test-shards.py --write-weights /tmp/trx/*.trx`. To run one job locally after the Release build, run `python3 tools/test-shards.py --shards 4 --index 0 --runsettings shard.runsettings`. Then run `dotnet test tests/CivicLens.Tests --configuration Release --no-build --settings shard.runsettings`.

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

To list passed bills that have no action candidate because a cosponsor or actions page job failed or is blocked, run this query. A failed summaries or subjects page does not block a candidate, so the query skips it. A test runs this exact query.

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

After the subjects migration, bills that the gate saw earlier have no subjects chain. The owner command below adds the chains of the newest version of each bill. It needs `CIVIC_LENS_DATABASE`, `CIVIC_LENS_REVIEW_OWNER`, and `CIVIC_LENS_ARTIFACT_DIRECTORY`, the directory of the saved captures.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- bills subjects backfill 100
```

The limit is 100 when you give none. The command prints `examined`, `admitted`, and `failed`. A bill whose saved detail capture is missing or unreadable counts as failed, and the run goes on with the next bill. Run it again until `examined` equals `failed`. A failed bill keeps a null count and is listed again each run, ahead of the later bills. If `failed` reaches the limit, raise the limit. Look at each failed bill: its saved detail capture is missing or unreadable. Collect the bill again, or leave it. A bill with a new update date gets a new gate row with its own count. It is safe to repeat. The worker fetches the subjects pages. When a chain closes for a bill that already has an action candidate, the worker queues one more action draft for that bill. Each such draft spends model budget. Start with a small limit and watch the daily limit of [action drafting](#action-drafting). Run `analyses redraft --stale votes` after the subjects arrive. It selects only votes whose newest draft used an older task, prompt, or schema version. A vote drafted under the current prompt before its subjects arrived is not selected. Use `votes draft requeue` for such a vote. [Bills](architecture/bills.md#subjects-backfill) holds the rules. A rollback past the subjects migration is refused when an action revision came from a candidate that includes subjects. After the backfill drafted a bill, such a rollback needs a backup, or a manual delete of that revision first.

After the status migration, bills that the gate saw earlier have no stored latest action. The owner command below reads it from the saved detail capture of the newest version of each bill. It makes no Congress.gov request. It needs the same three variables as the subjects command.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- bills status backfill 100
```

The limit is 100 when you give none. The command prints `examined`, `recorded`, and `failed`. A bill whose saved detail capture is missing or unreadable counts as failed and stays unread, so it is listed again each run. Run it until `examined` is 0 or equals `failed`. If `failed` reaches the limit, raise the limit, because failed bills stay ahead of the later bills. A capture that has no usable `latestAction` is recorded with no status. After the backfill, the worker publishes the status release on its next tick. You can also run `releases publish <idempotency-key>` with no record. The publish writes `bill-status.json` and every action page that now has a status. The [bill status line](architecture/publication.md#bill-status-line) holds the rules. The first status release writes again one page for each action that has a status, and it holds them all in memory until staging. Count the actions with a status and check the free memory of the worker box first.

After the bill text source exists, bills that the gate saw earlier have no text read. The owner command below adds a text versions job for the newest version of each bill that has no Congress.gov summary and no text read. It needs the three variables of the subjects command. It also needs `CIVIC_LENS_COLLECTION_CONFIG` for a configuration where `congress-bill-text` has `enabled` set to `true`. The new jobs use that configuration, not the configuration of the old detail jobs.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- bills text backfill 100
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- bills text backfill --bills config/action-text-measure-bills.json
```

The limit is 100 when you give none. The second form handles only the bills of the file, for example the 20 bills of `actions text measure`. That command reads the text through the gate of the newest candidate, so a bill needs its text state before the measure can use it. The IDs follow the rules of that command. A bad or repeated ID stops the run before any job is admitted. The command prints `pendingActionDrafts`, `examined`, `admitted`, and `skipped`. The `skipped` list holds each bill that got no job, with its ID and the reason. The reasons are `noGate`, `hasSummary`, `hasTextState`, and `notAdmitted`. The last one means that a newer gate or an existing job stopped the admission. A source ID that is not in the configuration stops the run before any admission. If an action draft job is queued or leased, `pendingActionDrafts` is above 0, the command admits nothing, and it exits with code 1. Check `actions draft status`, wait until the drafts end, and run the command again. A rerun admits no second job for a bill. A bill whose job has not run yet is examined again and not counted as admitted. The worker fetches the text. Run the command again after it has finished, to admit the next bills. A failed versions or text job keeps a bill in the list, and it holds a place before the later bills. [Bills](architecture/bills.md#text-backfill) holds the rules.

### Federal Register stream

The source `federal-register-documents` in `config/federal-coverage.json` follows the presidential documents of the Federal Register. It ships with `enabled: false`. It needs no key and no roster. [Executive actions](architecture/executive-actions.md) holds the design. To enable it:

1. Check that the configuration holds a person for each President whose documents the window covers. The shipped file holds Joe Biden, from 2021-01-20 to 2025-01-20, and Donald Trump from 2025-01-20. A document with no configured President blocks its job.
2. Set `enabled` to `true` for `federal-register-documents` and restart the worker. A restart reads the configuration again.
3. Deploy the search service before the publisher. The search service of an older version refuses a release that holds an executive action.

A release that holds the correction field `factsUnchanged` needs the same order. The search service reads files with `JsonOptions`, which refuses unmapped members, so an older search service refuses the field. `deploy/scripts/update.sh` starts the search service and the worker from one `compose up` at one image tag. It sets no order between them. A deploy of the worker alone must update the search service first.

To stop the source, set `enabled` to `false` and restart the worker.

The first run lists every document from `backfillFrom` 2024-10-29. It admits one job for each document, about 700 over two years. Each check after that runs every 6 hours and lists from 2 days before the newest stored publication date. Run `jobs list` and `jobs get` as in [managed collection jobs](#managed-collection-jobs) to see progress. A blocked job names its reason.

To withdraw a record, open `/Review/ExecutiveActions/{id}` and use Take off site; see [take a record off the site](#take-a-record-off-the-site). Withdrawing approval alone does not change the public site.

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

The Senate votes come from Voteview. senate.gov blocks the collector with a 403, so no other Senate vote source exists. The source `voteview-senate-votes` in `config/federal-coverage.json` ships with `enabled: false`. A configuration may enable only one Voteview source. The Host refuses a live configuration file that enables a source with the retired senate.gov parser `senateVoteList`. Set `"enabled":false` on that source, or remove it.

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

The first full run drafts every roll call that has a tracked senator. Code approves each draft after it checks the citations. A Senate vote from Voteview rests on Voteview data, and the record page names Voteview as the source.

## Postgres adapter and migrations

Set `CIVIC_LENS_DATABASE` in your local environment to a Postgres connection string with Host and Database, and with authentication for your local server. Runtime commands need this variable and have no default database. Keep credentials out of committed files, command arguments, and captured output. The commands need a reachable Postgres server and enough database permissions. They do not install or start Postgres.

After you set the variable, run:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- db migrate
```

`db migrate` applies all pending checked-in EF migrations and can be repeated. Then it applies the grants of the public role again, also when no migration is pending. See [public role login](#public-role-login). An image from before the migration `DropPublicationRecordsJson` cannot run until you roll that migration back with an explicit EF rollback, because that image needs the dropped column `publication_releases.records_json`. The rollback rebuilds the column from `publication_record_states`. An image from before the migration `RejectDecision` runs on the new schema only until the first rejection. After a rejection, that image cannot parse the kind Reject or the status Rejected, and the `Down` of the migration fails with a check violation and rolls back, because it deletes no decision. Roll forward with a fixed image in that case. An image from before the migration `DropDecisionJson` needs the same explicit EF rollback, because that image needs the dropped column `decision_json` in the seven `<kind>_review_decisions` tables. The rollback rebuilds the column from the decision columns and the resolution rows. To roll back to such an image, do these steps in this order. Stop the worker and the review host. Have a developer with the EF tool roll the database back to the migration before `DropDecisionJson`. Then run `update.sh` with the old SHA. No production command rolls a migration back. Running `update.sh` alone is wrong, because the old image starts without the column and every decision write fails. Running the rollback while the new worker runs is also wrong, because the new writer fails on the rebuilt NOT NULL column. `db migrate` only moves forward. The migration `JobAttemptColumns` moves the request and resolution JSON of `collection_job_attempts` into typed columns and drops both JSON columns. It rewrites every attempt row and locks `collection_attempts` and `collection_job_attempts` against writes while it runs, so the worker must be stopped; `update.sh` stops it. The migration stops with an error and rolls back when a JSON value cannot be read or does not reach its column. Its `Down` rebuilds the JSON columns. Each migration command fails after 10 minutes, for example when it waits on a lock. Imports never apply migrations. Capture files stay in the artifact directory. [Managed collection jobs](#managed-collection-jobs) collect and import evidence.

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

### Public role login

The migration `PublicApiViews` creates the roles `civic_lens_public` and `civic_lens_public_owner`. Both have no login and no password. It also creates the schema `public_api` with its views. The migration creates only the owner role. The role `civic_lens_public` and every grant come from `db migrate` after the migrations, in one step that runs every time. The step holds no secret, because a secret in code lands in Git. `readonly-grants.sql` creates `civic_lens_readonly` with LOGIN and no password. The public role must log in over the network, so it needs a login and a password.

The same step revokes `TEMPORARY` on the database and `EXECUTE` on the `pg_advisory_*` and `pg_try_advisory_*` functions from `PUBLIC`. So `db migrate` needs a superuser. It is the only place that revokes `TEMPORARY`, so `readonly-grants.sql` relies on it. The app role of the compose database is one. The role `civic_lens_public` then creates no temporary table and takes no advisory lock.

Enable the login by hand, after the first `db migrate` that holds the migration. Open an interactive `psql` session on the database host as the database owner. Run `ALTER ROLE civic_lens_public LOGIN PASSWORD` with a long random password. Keep the password out of shell history, logs, and Git. Store it only where `PublicApi` reads its settings. The service reads the connection from `CIVIC_LENS_PUBLIC_VIEWS_DATABASE`, for example `Host=<host>;Database=<database>;Username=civic_lens_public;Password=<password>`. It sets `Maximum Pool Size=4` itself and no timeout, because the role sets the statement timeout. The service stops at startup when the variable is missing.

The role reads only the views in `public_api`. It cannot write, a statement stops after 2 seconds, an idle session ends after 60 seconds, and it holds at most 5 connections. After a restore, run `db migrate`. It creates a missing role and sets the grants again, because `pg_restore --no-privileges` drops them. Roles belong to the cluster and a dump can leave them out, so the login of the role needs a new `ALTER ROLE` after a restore into a new cluster. Without the login the views stay closed, which is the safe state. `PublicApi` uses the role for `/v1/release` and the vote routes. Without the login those routes fail and the search routes keep working.

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

**Interruption.** Ctrl+C does not cancel the job permanently. After an interruption, start the worker again with the complete artifact root, including `.pending`, or its relocated copy. A same-origin unresolved attempt blocks other managed jobs until it is reconciled. The worker reconciles imported evidence even when its handoff is missing. Otherwise `receipts replay` imports without a fetch. The worker removes a handoff file of an old version or without an elapsed time, and fetches the job again. It keeps any other invalid file.

**Worker.** `worker <collector.dll> <artifact-directory>` needs `CIVIC_LENS_COLLECTION_CONFIG` with an absolute configuration path. It runs the durable collection-to-review engine. `--once` admits due checks, drains the runnable work, and exits without waiting for future deadlines. The publishing stage makes one pass, so it makes at most one release of up to 64 records. Ctrl+C stops the worker. Change the configuration and restart the worker to apply it. Cancelling one job does not disable its source schedule. Keep one worker for each operational store. Leases and fenced checkpoints protect ownership if another worker runs.

**Worker health.** `worker health` needs `CIVIC_LENS_DATABASE`. It reads the heartbeat rows that the loops of the worker write. It prints a heartbeat line and exits 0 when every loop beat is at most 10 minutes old. It exits 1 when one beat is older or when no beat exists. The line names the loop and its age. A model request times out after 5 minutes, and a collector attempt takes about 305 seconds plus the settlement timeouts. A single item, or a single release publish, that runs longer than 10 minutes fails health. When `CIVIC_LENS_RELEASE_DIRECTORY` is set, it can print a second line, so the output can hold two lines. It exits 3 when the worker is healthy and the records and withdrawals of the active release plus the approved records outside it exceed the cap. Exit code 3 is not a dead worker. A dead worker always exits 1, and its output still prints the cap line. The line reads "Release full: The release holds N records. M new records wait for room." M counts the real backlog, not one batch. It leaves out withdrawn records. When no approved record waits, the command does not read the release manifest.

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

`config/federal-pilot.json` holds a small live-source configuration for Elizabeth Warren and Ted Cruz. It also tracks Hakeem Jeffries, a House member with no sources, so that the House vote stream has a tracked member. It is a collection pilot, not representative national coverage. The coverage start date records when this project began to monitor these sources, not when either senator took office. Office press releases show what an office published. Claims about enacted legislation or completed government actions need corroborating authoritative records before publication.

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
| `CIVIC_LENS_ISSUE_ICONS_CONFIG` | Absolute path to the issue icon file, at most 1 MB. Publishing needs it. Unset in the review host gives every issue tile the default icon on a Take off site release |
| `CIVIC_LENS_COLLECTION_CONFIG` | Optional absolute path to the collection configuration for the owner Sources page and the official choices of the review pages. Without it no officials are listed |
| `CIVIC_LENS_RELEASE_DIRECTORY` | Optional absolute path to the release root. With the collection configuration and the issue file, it turns on the release list and the blocked list on the Review home page |
| `CIVIC_LENS_ARTIFACT_DIRECTORY` | The capture directory. Required when `CIVIC_LENS_RELEASE_DIRECTORY` is set, because a publish reads it for a new member photo. Without it the workspace stops at startup with exit code 2; see [Publish a static release](#publish-a-static-release) |

Without the collection configuration, the Sources page reports that source checks are not set up. The release list and the blocked list on the Review home page need the collection configuration, the issue file, and the release directory. Take off site on the record editors needs the same three. Without them the owner sees the line "Take off site needs the release directory, collection and issue files" and no button. The issue file also offers issues and policy areas as choices on the statement pages. The `releases publish` command needs the same two files.

**Auth0 and proxy.** Set the Auth0 allowed callback URL to `<CIVIC_LENS_REVIEW_ORIGIN>/signin-oidc`. Configure an HTTPS reverse proxy on the same host. It forwards to loopback and keeps the configured external Host header, with its port if it is not standard. Public access must end HTTPS at the proxy. Local workstation testing also needs a local HTTPS proxy and an allowed Auth0 callback; there is no unauthenticated review mode.

**Access.** To change access, change the server subject configuration and restart. The data-protection keys are not encrypted at rest by the application. Protect the persistent key volume and its backups as credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- review 5081
```

Open `/Review` through the configured HTTPS origin. Keep the separate `worker` running, because it collects, extracts, and compares automatically. `/Review/Sources` is the owner Sources page. `/Review/GateLabels` is the owner page for gate labels. `/Review/Statements` lists statements, and `/Review/Statements/{candidate-id}` shows one statement for editing and decisions. `/Review/Actions/{record-id}` and `/Review/Votes/{record-id}` show one action or vote. The Review home page links to the records that need action.

A record allows at most 64 open concerns. An approval resolves them and starts a new cycle. When a limit rejects a write, do not discard history to continue. Browsing comparisons may return an empty batch with a continuation when the batch holds only unchanged, incompatible, or limited results. Use the continuation; the empty batch does not prove that no changes exist.

An AI draft is revision 1 by the AI drafter. Check every sentence and citation against the evidence. If the editor flags an unverified change date, check it, tick Date checked, and press Save and approve. The press saves your revision and approves it. Approved accounts publish about 1 minute after the approval, and at least 10 minutes after the last release, when the stage `publishing` runs. A lost notification delays the publish to the next full hour. A failed tick or a tick that ends on a conflict delays it for at least 10 minutes, to the next approval or the next full hour.

Code approves a worker revision of an action or a vote after it checks the citations. The decision history shows the actor "Code check". A record shows as Needs action only if code did not approve it. A concern is open, a person wrote the text, or an approval check failed. A person can edit or withdraw any record.

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

To draft again the finished drafts that a version bump left stale, run this command as the owner. It needs the same two variables.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- analyses redraft --stale
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- analyses redraft --stale votes
```

The optional stage is `actions`, `votes`, or `briefs`. The command prints the count of requeued entries for each stage. It moves only Succeeded entries whose newest drafted run used an older task, prompt, or schema version. It leaves an entry that is Running or leased, and a week whose brief a person approved or rejected. It calls no model. Each stage commits in its own transaction. A failed stage leaves the earlier stages requeued, so a rerun is safe. The worker drafts the entries again and spends tokens, so check the daily limit first. Document-change drafts and statement drafts have no stale redraft. See the [limits](architecture/analysis.md#limits-and-planned-work).

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
| `CIVIC_LENS_ACTION_DRAFTING_BILL_TEXT` | Optional. Set to `1` to draft a bill with no CRS summary from its official bill text. Unset means off. Any other value stops the worker with exit code 2. |

Leave `CIVIC_LENS_ACTION_DRAFTING_BILL_TEXT` unset. The setting stays off until the owner reads the measured sample and the publish pull request merges. With the setting on, each text draft waits for a person, and code does not approve it. A person cannot approve it either, until the publish pull request merges. That pull request adds the text citations, the label, and code approval. With the setting off, the step writes the text by code, as before.

The owner measures the text prompt before the setting goes on. The command calls the paid model, so run it on request only. It reads the 20 fixed bills of `config/action-text-measure-bills.json` from the database that `CIVIC_LENS_DATABASE` names. Point it at the dev database. It needs `CIVIC_LENS_REVIEW_OWNER`, `CIVIC_LENS_ARTIFACT_DIRECTORY`, `CIVIC_LENS_ISSUES_CONFIG`, and `ANTHROPIC_API_KEY`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions text measure 1500000 config/action-text-measure-bills.json
```

The first argument is the spend cap in tokens. The command refuses to start without it. It stops before a call that could pass the cap. Each call of a 60,000 character text reserves about 65,000 tokens, so 20 bills can reserve 1,300,000 tokens. The command writes no database row. It writes the report to `artifacts/action-text-measure/<time>.json`. A bill with no stored text is skipped with its reason. Read each sentence and its quote in the report. Put the totals and your reading of the sample on the pull request, before the publish pull request. See [text measure](architecture/actions.md#text-measure).

Drafting uses Claude Haiku 5.5. The first run can spend up to the daily limit on queued candidates. Start with a low limit. A bill version whose summaries or subjects have closed has up to three queued candidates. Each older one settles as `supersededByNewerCandidate` with no call, also when the newest candidate blocks. The [outcomes](architecture/actions.md#outcomes) table lists every other code.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft list unchanged 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft requeue <candidate-id|--all>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft requeue --blocked --code titleTooLong
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions draft requeue --refused
```

`status` prints the daily limit, any pause, the tokens charged today, and the count of entries in each outcome. `list` takes `drafted`, `unchanged`, `superseded`, `no-official`, `pending`, or `failed`, and a limit up to 500 (default 100). `requeue` moves Failed and no other rows back to Pending. `requeue --blocked --code <block-code>` moves Blocked rows with that code back to Pending. Fix the cause first, or the entry blocks again. The code is required, so one command cannot requeue every Blocked entry. Use a code from the [outcomes](architecture/actions.md#outcomes) table, or the exception type name that `list failed` shows for an unexpected error. A mistyped code requeues nothing and prints `{"requeued":0}`. `requeue --refused` moves the one candidate that a draft names as refused back to Pending, and no other rows. It skips a draft that is still rejected, because a rejected record would refuse the update again. Press Request changes on the record first, then run the command. If the update failed, run `requeue <candidate-id>`. If it is blocked, run `requeue --blocked --code <block-code>`. Until the new draft arrives, a person cannot approve the record. `analyses redraft --stale actions` moves drafted and unchanged rows of bills with an older task, prompt, or schema version back to Pending, and prints the count of entries, not bills. Older entries of a stale bill count too and settle as superseded. Use it for drafts that lack issue candidates. A prompt change alone draws no new draft, because the comparison ignores the prompt version. After the command, let the worker draft within its daily limit. The worker makes the paid calls, not the command. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

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
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- votes draft requeue --refused
```

`status` prints one JSON object. Its `queue` field holds the daily limit, any pause, `chargedToday`, and `counts` for each outcome. Its `waitingForBill` field is the number of votes on a bill that wait for the bill detail and have no queue row yet. `list` takes `drafted`, `unchanged`, `superseded`, `no-member`, `pending`, or `failed`, and a limit up to 500 (default 100). `requeue` moves Failed and no other rows back to Pending. `requeue --blocked --code <block-code>` moves Blocked rows with that code back to Pending. Fix the cause first, or the entry blocks again. The code is required. Use a code from the [outcomes](architecture/votes.md#outcomes) table. A mistyped code requeues nothing and prints `{"requeued":0}`. `requeue --refused` moves the one candidate that a rejected draft names as refused back to Pending, and no other rows. Run it after a person pressed Request changes on the record, because a rejected record refuses the update again. If the update failed, run `requeue <candidate-id>`. If it is blocked, run `requeue --blocked --code <block-code>`. Until the new draft arrives, a person cannot approve the record. `analyses redraft --stale votes` moves drafted and unchanged rows of votes on a bill with an older task, prompt, or schema version back to Pending. It prints the count of entries, not roll calls. Older entries of a stale roll call count too and settle as superseded. The newest drafted run of the roll call decides. A vote with no bill never moves. A redraft can write new revisions. Code approval of a new revision can queue relationship pairs, and a queued pair can cost a model call. An approved link that cites the old revision shows "cited records changed" and needs a person. A vote redraft requeues no brief, because briefs cannot cite votes. The worker then drafts each requeued vote again, because its newest draft used an older prompt. The daily limit bounds the spend. A second run of the command requeues nothing. A vote whose text a person wrote, or that has an open concern or a withdrawn approval, is skipped. A vote on an older prompt that is collected again, corrected, or requeued also drafts again with no command, once for each stale roll call. A Failed redraft keeps the old text, and only `votes draft requeue` retries it. Code approves new model text when the checks pass, and the next publish replaces the published text, so no publish option is needed. The model also chooses the issues again, so they can change. Each replaced vote gains a public correction entry. It reads "Summary rewritten by the AI model" when only the headline or the summary changed. After the command, let the worker draft within its daily limit. The worker makes the paid calls, not the command. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

### Action revision backfill

R-63 steps 2 and 3 move old action revisions into columns and child rows. A revision written by current code has them, but a revision written before the derived columns existed has no `event_on` and `cosponsor_count`. The commands need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions revisions backfill 500
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- actions revisions verify
```

`backfill <limit>` moves at most `limit` revisions that have no event date. Those are legacy revisions and revisions without the derived columns. It walks the table in key order in batches of 25. Each batch is one short transaction that holds the action review lock. For each row it deletes the old child rows, writes the columns and the issue, sponsorship, and citation rows, reads the row back, rebuilds the revision, and compares it to the stored JSON. A row that differs is skipped with `RebuildMismatch`. It prints `examined`, `moved`, `skipped`, `skippedByReason` (a count for each reason), and the first 50 skipped revisions as record ID, revision number, and reason. The exit code is 1 when any row was skipped, and the counts still print. The reasons are `UnreadableJson`, `InvalidShape`, `RebuildMismatch`, and `database:<constraint>`. A skipped row keeps its earlier columns and child rows.

`verify` rebuilds every revision from its rows and compares it to its JSON as trees. It writes nothing and takes no lock. It prints `checked`, `legacy`, `mismatched`, and the first 20 mismatches as record ID, revision number, and JSON key names. It never prints text. The exit code is 1 while `legacy` or `mismatched` is above 0. No key is allowed to be absent.

Run order: `db migrate`, then `backfill`, then `verify`. Every action reader reads the columns and fails on a row with null columns. That includes the review pages, the queue, the relationship and brief readers, and the redraft skip. On production, `deploy` restarts the worker and the review host, so run `backfill` right after the deploy. For a manual run, start the new code only when `verify` exits 0, so `legacy` and `mismatched` are both 0.

### Executive action revision backfill

R-63 step 3 moves old executive action revisions into columns and child rows. A revision written by current code already has them. The migration `ExecutiveActionRevisionColumns` also moves each `notice_json` of a document into the notice columns, and drops `notice_json`. It sets the code author on each old revision. It fails and changes nothing when a notice differs from its row, when a revision names another author, or when a `notice_json` or `revision_json` is not valid JSON. The error names the row. The commands need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- executive-actions revisions backfill 500
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- executive-actions revisions verify
```

`backfill <limit>` moves at most `limit` legacy revisions. It walks the table in key order in batches of 25. Each batch is one short transaction that holds the executive action review lock. For each row it writes the columns and the fact and notice rows, reads the row back, rebuilds the revision, and compares it to the stored JSON. A row that differs is skipped with `RebuildMismatch`. The output and the exit code are the same as for the [action backfill](#action-revision-backfill). The reasons are `UnreadableJson`, `InvalidShape`, `RebuildMismatch`, and `database:<constraint>`.

`verify` rebuilds every revision from its rows and compares it to its JSON as trees. It writes nothing and takes no lock. The output and the exit code are the same as for the action `verify`. A revision whose rows cannot be read shows the field name `columns`. No key is allowed to be absent.

Run order: `db migrate`, then `backfill`, then `verify`. On production, `deploy` migrates and then restarts the worker and the review host. The new code runs from the deploy. Run `backfill` right after the deploy, because every executive action read fails until it ends. For a manual run, start the new code only when `verify` exits 0. Every reader fails on a row with null columns. Production held 688 revisions when the survey was made. A rollback past the migration rebuilds `notice_json` from the notice columns.

### Statement revision backfill

R-63 step 3 moves old statement revisions into columns and child rows. A revision written by current code already has them. The migration `StatementRevisionColumns` adds the columns and tables. It does not read any JSON. The commands need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements revisions backfill 500
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- statements revisions verify
```

`backfill <limit>` moves at most `limit` legacy revisions. It walks the table in key order in batches of 25. Each batch is one short transaction that holds the statement review lock. For each row it writes the columns and the speaker, other speaker, issue, policy area, quotation, and bill number rows. Then it reads the row back, rebuilds the revision, and compares it to the stored JSON. A row that differs is skipped with `RebuildMismatch`. A quotation or date evidence span of another extraction than the candidate has fails a key. The row is then skipped with `database:<constraint>`. The output and the exit code are the same as for the [action backfill](#action-revision-backfill). The reasons are `UnreadableJson`, `InvalidShape`, `RebuildMismatch`, and `database:<constraint>`.

`verify` rebuilds every revision from its rows and compares it to its JSON as trees. It writes nothing and takes no lock. The output and the exit code are the same as for the action `verify`. A revision whose rows cannot be read shows the field name `columns`. No key is allowed to be absent.

Run order: `db migrate`, then `backfill`, then `verify`. On production, `deploy` migrates and then restarts the worker and the review host. The new code runs from the deploy. Run `backfill` right after the deploy, because every statement read fails until it ends. That includes the review pages, the queue, the relationship and brief readers, and the bill mention priority. For a manual run, start the new code only when `verify` exits 0. A rollback past the migration keeps the JSON.

### Brief revision backfill

R-63 step 3 moves old brief revisions into columns and child rows. A revision written by current code already has them. The migration `BriefRevisionColumns` adds nullable columns and moves no data. The commands need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- brief revisions backfill 500
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- brief revisions verify
```

`backfill <limit>` moves at most `limit` legacy revisions. It walks the table in key order in batches of 25. Each batch is one short transaction that holds the brief review lock. For each row it writes the columns and the citation, paragraph, and paragraph citation rows, reads the row back, rebuilds the revision, and compares it to the stored JSON. A row that differs is skipped with `RebuildMismatch`. A citation of a revision that is not stored is skipped with `database:` and the name of the foreign key. The output and the exit code are the same as for the [action backfill](#action-revision-backfill). The reasons are `UnreadableJson`, `InvalidShape`, `RebuildMismatch`, and `database:<constraint>`.

`verify` rebuilds every revision from its rows and compares it to its JSON as trees. It writes nothing and takes no lock. The output and the exit code are the same as for the action `verify`. A revision whose rows cannot be read shows the field name `columns`. No key is allowed to be absent.

Run order: `db migrate`, then `backfill`, then `verify`. On production, `deploy` migrates and then restarts the worker and the review host. Run `backfill` right after the deploy, because every brief read fails until it ends. Production held 1 brief revision when the survey was made.

### Document change revision backfill

R-63 step 3 moves old document change revisions into columns and child rows. A revision written by current code already has them. The migration `DocumentChangeRevisionColumns` fills the author columns of each old revision from its JSON. It fails and changes nothing when a `revision_json` is not valid JSON, or when it has no author of 1 to 256 characters. The error names the draft and the revision number. The commands need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- document-changes revisions backfill 500
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- document-changes revisions verify
```

`backfill <limit>` moves at most `limit` legacy revisions. It walks the table in key order in batches of 25. Each batch is one short transaction that holds the document change review lock. For each row it writes the columns and the official, issue, and citation rows, reads the row back, rebuilds the revision, and compares it to the stored JSON. A row that differs is skipped with `RebuildMismatch`. The output and the exit code are the same as for the [action backfill](#action-revision-backfill). The reasons are `UnreadableJson`, `InvalidShape`, `RebuildMismatch`, and `database:<constraint>`.

`verify` rebuilds every revision from its rows and compares it to its JSON as trees. It writes nothing and takes no lock. The output and the exit code are the same as for the action `verify`. A revision whose rows cannot be read shows the field name `columns`. No key is allowed to be absent.

Run order: `db migrate`, then `backfill`, then `verify`. On production, `deploy` migrates and then restarts the worker and the review host. The new code runs from the deploy. Run `backfill` right after the deploy, because every document change read fails until it ends. That includes the review pages, the activity feed, the approved record search, and the weekly brief reader. For a manual run, start the new code only when `verify` exits 0. A rollback past the migration keeps the JSON, and the next run of the migration fills the author columns again.

## Relationship drafting

The `worker` command drafts one relationship for each pair of an approved statement and an approved action or vote that share an official and a bill. [Statements](architecture/statements.md#relationship-drafting) holds the rules. Relationship drafting runs when both required variables below are set in the worker's protected environment. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. The names of the officials come from the collection configuration of the worker.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_RELATIONSHIP_DRAFTING_DAILY_TOKENS` | Daily token limit for relationship drafting calls, per UTC day. It is separate from the other limits. Start low. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

Drafting uses Claude Haiku 5.5. The stage runs after vote drafting and before brief drafting. A review decision on a statement, an action, or a vote queues a Pending row for each new pair of that record. Run `relationships draft refresh` once after the migration to queue the pairs of records that were approved earlier. The same stale check runs after each review decision (`AfterDecisionHooks`). Then the worker can spend up to the daily limit. A pair with a new approved revision on either side returns to Pending unless a person approved its relationship. A pair with the same input as the last settled run makes no call. The [outcomes](architecture/statements.md#relationship-drafting) table lists every code.

The commands below need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft status
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft list none 50
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft requeue <pair-id|--all>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships draft refresh
```

`status` prints one JSON object. Its `queue` field holds the daily limit, any pause, `chargedToday`, and `counts` for each outcome. Its `statementsWithoutCongress` field is the number of approved statements that name a bill number and have no date, or a date before 1935. Code skips their bill numbers, so set the date on the statement to pair it by number. `list` takes `drafted`, `unchanged`, `none`, `unmatched`, `pending`, or `failed`, and a limit up to 500 (default 100). `requeue` moves Failed and no other rows back to Pending. `refresh` prints `{"pairs":N}`, the number of pairs it found, and it is safe to repeat. A paused or waiting stage follows the same table as document-change drafting: fix the key or the credit and restart the worker, or wait for the next UTC day.

### Relationship revision backfill

R-63 step 3 moves old relationship revisions into columns. A revision written by current code already has them. The migration `RelationshipRevisionColumns` adds the columns and checks, and moves no data. It fails and changes nothing when a `revision_json` is not valid JSON. The error names the row. The commands need `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships revisions backfill 500
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- relationships revisions verify
```

`backfill <limit>` moves at most `limit` legacy revisions. It walks the table in key order in batches of 25. Each batch is one short transaction that holds the relationship review lock. For each row it writes the columns, reads the row back, rebuilds the revision, and compares it to the stored JSON. A row that differs is skipped with `RebuildMismatch`. The output and the exit code are the same as for the [action backfill](#action-revision-backfill). The reasons are `UnreadableJson`, `InvalidShape`, `RebuildMismatch`, and `database:<constraint>`. A cited statement, action, or vote revision that is not stored gives a `database:` reason.

`verify` rebuilds every revision from its columns and compares it to its JSON as trees. It writes nothing and takes no lock. The output and the exit code are the same as for the action `verify`. A revision whose columns cannot be read shows the field name `columns`.

Run order: `db migrate`, then `backfill`, then `verify`. On production, `deploy` migrates and then restarts the worker and the review host. Run `backfill` right after the deploy, because every relationship read fails until it ends. Production held 2 relationship revisions when the survey was made. The stale check does not requeue a pair whose revision still has null columns. It runs in `relationships draft refresh` and after each review decision (`AfterDecisionHooks`), so run `backfill` before both.

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

### Brief checking

The `worker` command also checks the current revision of each brief when both required variables below are set. The check gives advice only. [Briefs](architecture/briefs.md#brief-check) holds the rules. A limit without `ANTHROPIC_API_KEY` stops the worker with exit code 2. With no limit, the check stays off and no call is made. Entries still queue, so the first start with a limit set checks each queued brief.

| Variable | Meaning |
|---|---|
| `ANTHROPIC_API_KEY` | Claude API key, shared with the other stages |
| `CIVIC_LENS_BRIEF_CHECKING_DAILY_TOKENS` | Daily token limit for brief check calls, per UTC day. It is separate from the other limits. |
| `CIVIC_LENS_ANALYSIS_CONCURRENCY` | Optional number of calls at the same time, shared with the other stages (default 2, 1 to 4) |

The check uses Claude Sonnet 5.5. No automated test sends a paid request for it.

No page shows a failed or blocked check yet. Run this query to list them:

```sql
SELECT record_id, week_start, status, error_code, attempts
FROM brief_analysis
WHERE task = 'brief-check' AND status IN ('Failed', 'Blocked')
ORDER BY week_start DESC;
```

### Member roster

Enable the source `congress-members` in the collection configuration to capture the public member file once a day. It needs no credential. The command below needs `CIVIC_LENS_DATABASE`. It reads the latest snapshot and writes nothing.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- roster propose config/federal-coverage.json
```

Enable the source `executive-officials` the same way to capture the executive file, which lists presidents and vice presidents. Publish reads it to write the terms of a configured person with a Wikidata ID.

Enable the source `census-zcta-districts` to capture the Census file that maps each ZCTA to the districts of the 119th Congress. It needs no credential. It checks once a week. The ZIP lookup on the home page reads the tables through the route `/v1/zip-districts/{zcta}`. The form needs `CIVIC_LENS_PUBLIC_API_URL`, as search does. Without the source, the route answers 404 for every ZCTA. See [districts](architecture/districts.md).

Enable the source `member-photos` to capture one portrait for each configured member. It needs no credential. Each member roster job then enqueues the photo jobs, and each person gets a new job every 30 days. The first run makes about 540 jobs, paced at one request a second. Run the roster source first. See [roster](architecture/roster.md#member-photos).

The output lists the members to add and the configured people who left the roster. Edit the configuration by hand and commit it. Press sources of a new member are a manual step. See [roster](architecture/roster.md#proposals).

### Import people into tables

The tables `people`, `person_setting_revisions`, `person_office_revisions`, and `person_office_revision_terms` hold the people of the configuration. Run the import once after `db migrate`. It needs `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`. It prints `added` and `alreadyKnown`. Two imports at the same time make the second fail with exit code 1 and write nothing, so run it once. A repeat adds nothing. It exits with code 2 and writes nothing when a bioguide ID or a Wikidata ID belongs to another person. No reader uses the tables yet. See [roster](architecture/roster.md#officials-in-tables).

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- people import config/federal-coverage.json
```

### Take down a member photo

Set `"withholdPhoto": true` on the person in the collection configuration and commit it. Restart the worker and the review workspace, because each reads the configuration only at start. A worker with the old configuration writes the photo back at its next automatic publish. Then run `releases publish <idempotency-key>` with no record. The command makes a release that applies the flag. When the person has no published record, no release shows their photo. The command then refuses with "Nothing to publish", and nothing needs to be done. The new release shows no photo for the person, and the official page shows the neutral line "No portrait is shown for this official." Collection continues, so removing the flag, restarting, and publishing again restores the newest photo.

The flag does not remove files from old releases. A kept release directory keeps the photo file until a prune removes it; see [release retention](architecture/publication.md#release-retention). Purge the file from the Cloudflare cache if it must leave at once. A withheld photo and withdrawn content can stay in a kept release folder with no time limit. A prune removes a folder only when all of these are true. At least 30 higher release numbers exist. The release is not the highest of a UTC day in the last 7 days. The folder is not the active or served folder. A later publish runs the prune. Seven days is the minimum for the highest release of a day. It is not a maximum for any folder. Restic backups can keep `releases/` for up to 6 months; see [Backups](#backups). The search index keeps withdrawn rows; see [search](architecture/search.md#invariants). A rollback with `releases activate` to an older release shows the photo again. See [roster](architecture/roster.md#limits-and-planned-work).

## Automatic publication

The `worker` command can publish every approved record: actions, votes, executive actions, statements, relationships, briefs, and document changes. A person approval needs no owner step. The stage also publishes one release when a bill status changed and no record is ready. [Publication](architecture/publication.md#automatic-publication) holds the rules. A person approval that replaces a published record needs a correction note, so the stage leaves it out and logs it. The record waits. To free it, withdraw the approval, then press Save and approve with no edit and with a correction note. A record with 20 corrections that have a note cannot take another person revision. Only a code-approved revision publishes. The stage is off by default. It runs when the variable below is `1`. Any other value stops the worker with exit code 2. Code approves an action or a vote only after a model drafts its headline and summary. So the stage publishes nothing until you also set `CIVIC_LENS_ACTION_DRAFTING_DAILY_TOKENS` and `CIVIC_LENS_VOTE_DRAFTING_DAILY_TOKENS`.

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_PUBLISHING` | Set to `1` to run the stage. Unset means off |
| `CIVIC_LENS_RELEASE_DIRECTORY` | The release root, shared with the review workspace and the `releases` commands. Required when the stage runs |
| `CIVIC_LENS_ISSUES_CONFIG` | The issue file. Required when the stage runs, as for `releases publish` |
| `CIVIC_LENS_ISSUE_ICONS_CONFIG` | The issue icon file. Required when the stage runs, as for `releases publish` |

The stage reads new member photos from the artifact directory of the worker command line. The worker also needs `CIVIC_LENS_COLLECTION_CONFIG`, as always. The stage wakes when the worker starts, about 60 seconds after an approval, at each full hour by the database clock, and when 10 minutes have passed since the newest release. A wake makes at most one release, of up to 64 approved records. Each kind gets an equal share. Approvals in the same window publish together. A wake less than 10 minutes after the newest release makes no release. The wake at the end of the 10 minutes publishes the next 64 records, so a backlog drains with no new approval while each wake succeeds and the release has room. It needs about 10 minutes for each 64 records, which is at most 9216 records a day. Each kind alternates published replacements and new records, a replacement first, and replacements fill at most half of the kind while new records wait. A correction publishes soon, and a flow of corrections cannot hold back new records. The database clock measures the 10 minutes from the time stored for the release, and that time is the commit time. The `releases publish` command and a status-only release also count as releases. The command has no limit itself. A tick that fails or ends on a conflict does the same for the next 10 minutes of the worker process: its wakes do nothing. The next try is the first wake after that, an approval or the next full hour. The wake for the end of the 10 minutes often falls inside this wait and does nothing, so a retry can wait up to about 70 minutes. A restart of the worker clears this wait. A status-only release, which shows a moved bill status when no record is ready, runs only on the wake of a full hour and at the start. A conflict on it starts the same wait. When the release holds the maximum number of records, the stage still publishes replacements and status-only releases. New records wait, and each pass logs one error that counts the waiting records of that batch. `worker health` also reports it, as in the worker health paragraph. Ask the developer to raise the cap. A withdrawal frees no slot, because records and withdrawals share the cap. A failure is logged at error level with its exception on stderr, under the stage name `publishing`. A failure of the tick counts in the stage result. A record that cannot build is reported and left out, and it does not count. The stage waits after a rollback with `releases activate`. A publish from Take off site also ends the wait. It resumes when you activate the latest release or publish with the CLI. Each release is about 29 MB in development. Each publish prunes old release directories; see [release retention](architecture/publication.md#release-retention).

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

The worker stage publishes every approval; see [Automatic publication](#automatic-publication). An editor who approves a record that replaces a public record writes a correction note in the Correction note box next to Save and approve. The note shows on the public record page as a correction. The box is optional on the page, and the stage leaves out a replacement that has no note. The Review home page lists recent releases read-only. The CLI command `releases blocked` lists the published records that review blocked, with the reason for each. The Needs a person queue shows a record that holds 20 corrections with a note and has a person-approved revision waiting, with the badge "Cannot publish" and the reason. For an action, a vote, a statement, or an executive action, only a code-approved revision can publish such a record. A withdrawn record in this state cannot return. Rollback stays in the CLI.

The CLI is a local owner command. It can also withdraw a record; the Review page does the same with Take off site. Set `CIVIC_LENS_DATABASE`, `CIVIC_LENS_REVIEW_OWNER` (the owner subject recorded on the release), and `CIVIC_LENS_RELEASE_DIRECTORY` (an absolute path, for example `$PWD/.runtime/releases`). Publishing also needs `CIVIC_LENS_COLLECTION_CONFIG`, an absolute configuration path that supplies the officials' display names, and `CIVIC_LENS_ISSUES_CONFIG`, an absolute path that supplies the issue and policy area names. It also needs `CIVIC_LENS_ISSUE_ICONS_CONFIG`, an absolute path to the issue icon file. `CIVIC_LENS_ARTIFACT_DIRECTORY` is required. Set it to the capture directory (the worker `<artifact-directory>`, for example `$PWD/.runtime/captures`). `releases publish` stops with exit code 2 before it runs when the variable is unset, and prints its generic invalid-configuration line. The review workspace stops at startup with exit code 2 when it has `CIVIC_LENS_RELEASE_DIRECTORY` and no artifact directory. Its configuration line names the artifact directory. A photo that the previous release holds is linked and its capture is not read. A new photo that cannot be read does not fail the publish. The release keeps the earlier photo or shows none, and the log holds an error with the person and the attempt. The worker stage uses the directory of its own command line, and the review workspace reads the same variable. A new photo or another change to an official profile appears at the next publish. To publish it now, run `releases publish <idempotency-key>` with no record. The command refuses to publish when no record and no official changed, unless you add `--rewrite-officials`, `--rewrite-entries`, or `--rewrite-pages`. It needs an active release. The command trusts the local operator, who already holds database credentials.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases publish <idempotency-key> [<kind>:<id>...] [--withdraw <kind>:<id> <text>]... [--rewrite-officials] [--rewrite-entries] [--rewrite-pages]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases list [limit]
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases blocked
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases activate <release-number>
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases first-published backfill
```

Write each record as `<kind>:<id>`. The kind is `documentChange`, `statement`, `action`, `vote`, `brief`, `relationship`, or `executiveAction`. For example, use `documentChange:` and a 32-character draft ID, `statement:` and a 64-character candidate ID, or `action:` and a 64-character action record ID. The other four kinds take a 64-character record ID too. The command never guesses the kind from the ID. An unknown kind or a bad shape exits with code 2. The revision must be approved with no unresolved concerns. A record that replaces a record of the active release needs the correction note of its approval. The command exits with code 2 when the approval has no note. A note has 1 to 500 characters. The note shows on the record page as a correction. A note on the approval of a new record is dropped.

To take a record out of the release, give `--withdraw <kind>:<id> <text>`. A request can have withdrawals and no records. The text has 1 to 500 characters and shows on the notice page. Review must have blocked the record: its status is Approval withdrawn, Changes requested, or Awaiting review with concerns. The publish exits with code 2 in three cases:

- The record is not in the active release.
- Review did not block the record.
- A relationship or a brief that stays in the release cites the record. The message lists the citing records. Withdraw them in the same request.

To list the published records that cannot move on, run `releases blocked`. It prints the kind, the ID, the headline, the published revision, the review status, and the reason of each record. The reason `reviewBlocksIt` means review blocked the record. Use these rows to choose the records to withdraw. The reasons `correctionLimit` and `withdrawnAtCorrectionLimit` mean the record holds 20 corrections with a note, and a person approved a revision that cannot publish. A withdrawal does not free these rows. The list is empty when no release is active.

To write the pages of all officials again, add `--rewrite-officials`. The publish then treats every official as touched. The option itself adds no record. It keeps the release history, the withdrawals, and the first publication date of each record. A rewrite with no named record needs an active release. Use it once after a deploy that changes the layout of the official pages. Until then an untouched official keeps its old page. On a commit conflict, run the same command again. A stop that names a record kind and ID does not clear on a retry. Compare that record file with its manifest entry, and send the message to the developer. Do not publish around it. Stop the worker before every `--rewrite-*` publish with `sudo deploy/scripts/compose.sh stop worker`, and start it after the command ends with `sudo deploy/scripts/compose.sh start worker`. A worker publish that commits while a rewrite runs makes the rewrite conflict, so a rewrite can lose that race. If a conflict happens anyway, run the command again. See the [publication limits](architecture/publication.md#limits-and-planned-work).

To fill the new fields of carried vote and action entries, add `--rewrite-entries`. The publish reads the record file of each carried vote and action and builds its manifest entry again. It adds `yeaCount`, `nayCount`, `alignment`, `chamber`, `voteKind`, `congress`, `cosponsorIds`, and the `billLine` of an action. It checks each record file and stops with an error that names the record when the file is invalid or when its entry differs in any other field. It changes no other entry field. It keeps the record files linked and writes none of them again. A withdrawn record stays withdrawn. The option adds no record, and it needs an active release. It reads one file for each carried vote and action. On a failure, run the same command again.

In the deploy that ships the vote kind and Congress fields, run `releases publish <idempotency-key> --rewrite-entries` once. Carried vote entries then get both fields. Until then they have neither, and no reader guesses them.

A page layout change, such as a new source link, reaches only the pages that a publish writes. A carried record page is a hard link to the page of the previous release. To render every carried page again, add `--rewrite-pages`. The publish reads the record file of each carried record, one at a time, and renders its page. It keeps the record files as the same files on disk, and it keeps the bill link, the related records, and the corrections of each page. A withdrawn record stays withdrawn. It adds no record, and it needs an active release. It reads one file for each carried record, and the cost grows with the number of records. Every page gets a new file, so each rewrite adds the full size of all pages to the kept releases. You may retry a failed run with the same key. A run that fails before the commit commits nothing. A run that fails after the commit, in the serve or prune step, replays the commit and serves the release. A run that overlaps a worker publish fails at the commit with a conflict message and exit code 1, and commits nothing. Run it again with the same key. After a success, use a new idempotency key for the next rewrite.

Every publish writes `officials-summary.json`, the roll-up of the figures that rankings need. A touched official gets new figures. An untouched official keeps the figures of the previous release. A release from before the roll-up has none, so its untouched officials have no vote figures until `--rewrite-officials` runs. The four board pages are partial until then. A board leaves out an official with no vote figures and says so. The cosponsor counts come from the manifest entries of every publish. The count is null until `--rewrite-entries` gives every carried action its cosponsor data. A reader must not read null as 0.

`--rewrite-officials` also fills `previous` in each summary row. The voting boards need `previous` to keep an earlier Congress. Until it runs, a board shows the newest Congress, and a row without `previous` leaves its member off a kept board. After `releases activate` of a release older than this rewrite, run it again. Use a new key each time, because the same key and payload replay the old result with no rewrite:

```sh
sudo deploy/scripts/compose.sh run --rm cli releases publish <idempotency-key> --rewrite-entries --rewrite-officials
```

The bill line needs one rollout command, because carried vote pages and official record pages are not written again otherwise:

```sh
sudo deploy/scripts/compose.sh run --rm cli releases publish <idempotency-key> --rewrite-entries --rewrite-officials --rewrite-pages
```

The rewrite needs a stored entry row for every carried action, and a missing row stops it. So do these steps in this order. Deploy. Run the [entries backfill](#entries-backfill) until it exits with 0 and `openRangesWithoutRow` is 0. Run the rewrite publish, which fills the bill line of the rows of the open ranges at once. Then run `releases entries verify`. The backfill creates the rows that are still missing. Rows of closed ranges from before the line keep a null line. `public_api.bills` and the bills routes show a null `bill_line` for a row until it is filled.

Every publish now reads all release vote revisions. Before the deploy, run `votes revisions verify` and expect exit code 0. One vote revision that is not stored or not backfilled stops every publish.

### First publication backfill

The table `published_records` stores the first publication time of each record. A publish writes a row for each record that it adds. Run the backfill once after the migration `PublishedRecords`. It needs `CIVIC_LENS_DATABASE`, `CIVIC_LENS_REVIEW_OWNER`, and `CIVIC_LENS_RELEASE_DIRECTORY`.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases first-published backfill
```

The command reads the manifest of the active release. It inserts a row for each record and each withdrawal, with the `firstPublishedAtUtc` of the entry. It never changes a row that exists, so a repeat is safe. It prints `examined`, `added`, and `alreadyPresent` as JSON. A record that no active entry or withdrawal names gets no row. Its row comes with the publish that adds it.

Until the backfill runs, a publish that adds a record of the active release stops with the message "Run releases first-published backfill". A guess could give one record two first times. The worker logs the error and tries again at its next wake. Run the backfill, and the next publish works. A rollback keeps the rows.

The withdrawal tables add a second need. Every publish whose release lists a withdrawal, carried or new, also stops with the same message until the backfill runs. The database refuses a withdrawal row for a record that `published_records` lacks. On a deploy that adds the migration `PublicationWithdrawals`, do these steps in order: stop the worker, migrate, run `releases first-published backfill`, then start the worker.

### Entries backfill

The tables `published_entries`, `published_entry_officials`, `published_entry_issues`, and `published_entry_policy_areas` hold one entry for each record revision. A commit writes the rows of the revisions that it adds. This command fills the rows of revisions from earlier releases. Run `releases first-published backfill` first, because an entry row needs the `published_records` row of its record. The command needs `CIVIC_LENS_DATABASE`, `CIVIC_LENS_REVIEW_OWNER`, and `CIVIC_LENS_RELEASE_DIRECTORY`. Other backfills do not need to finish first.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases entries backfill <limit>
```

The limit counts inserted rows. It does not bound the reads: each run reads every state row, every kept manifest that covers a range without a row, and the first record file of each such range. The command reads each kept manifest once, from the oldest to the newest. It takes the entry of a revision from the oldest kept folder of its range. It checks the record file of that folder, and it compares the entry in every later kept folder of the range. A kept folder of the range that omits the revision counts as a difference. It never fixes a value. A row that fails a check is skipped with a reason. The command also fills the bill line of action rows that hold none, from the same kept folders. It never changes a line that a row holds. A repeat is safe, because the command inserts only missing rows and writes a line only into a null column, so a commit during the run cannot create a second row or a second line.

The command prints JSON with these keys:

- `examined`: the revision ranges without a row at the start.
- `added`: the rows that this run inserted.
- `billLinesFilled`: the action rows that had no bill line and got one in this run. The limit does not cap them.
- `alreadyPresent`: the ranges that already had a row.
- `skipped`, `skippedByReason`, and `skippedEntries`: the skipped rows, a count for each reason, and the first 50 rows. The reasons are `manifestInvalid`, `recordFileInvalid`, `recordFileDiffers`, `foldersDiffer`, and `firstPublishedMissing`.
- `openRangesWithoutRow`: the open ranges that still have no row.
- `closedRangesWithoutKeptFolder`: the closed ranges that no kept folder lists. These ranges get no row, and this is expected.
- `readyRangesWithoutRow`: the open or closed ranges that passed every check but got no row, because the limit stopped the run.

The exit code is 0 only when `skipped`, `openRangesWithoutRow`, and `readyRangesWithoutRow` are all 0. Otherwise it is 1. After a run with a limit that stops early, `readyRangesWithoutRow` is not 0. Run the command again until it exits with 0. A `firstPublishedMissing` skip means that `releases first-published backfill` must run first. A `recordFileDiffers` or `foldersDiffer` skip needs a person to look at the folder, because the command does not choose a value.

### Entries verify

This command proves that the entry, state, first publication, and withdrawal tables hold what each kept `release.json` holds. Run it after `releases entries backfill` exits with 0. It writes nothing and takes no lock, so it is safe beside the worker. It needs the same three variables as the backfill.

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- releases entries verify
```

The command reads each kept manifest once. It writes the file again with no change, and it checks the bytes. Then it checks the records of the state table against the entries of the file. Last, it rebuilds the manifest from the tables and compares the bytes with the file. The rebuild takes the bill line from the table. It takes only the official names, the five other optional entry fields, `publishedAtUtc`, and some withdrawals from the file.

The command prints JSON with these keys:

- `passed`: the kept releases that the tables rebuild byte for byte.
- `legacy`: the kept releases that pass but list withdrawals that no withdrawal row covers, and that are older than the first release with a withdrawal row. Withdrawals made before the migration `PublicationWithdrawals` have no rows. A newer release with such withdrawals fails. Legacy releases do not change the exit code.
- `failed`, `failedByReason`, and `failedReleases`: the failed releases, a count for each reason, and each release with its number, folder, reason, and first record. The reasons are `manifestInvalid`, `manifestNotReproducible`, `entryRowMissing`, `firstPublishedMissing`, `stateRowNotListed`, `entryOutsideRange`, and `bytesDiffer`.
- `retired`: the release rows whose folder holds no manifest.

A release that an older image wrote, before a manifest field was added (for example the entry `chamber` field), fails with `manifestNotReproducible`, although its entries are correct. The output then lists that release in `failedReleases` and the exit code is 1. Check the newest release first. If the newest release passes, the older failures need no action.

The exit code is 0 only when `failed` is 0. The command never fixes a value. An `entryRowMissing` failure means that the backfill must run again. A `firstPublishedMissing` failure means that `releases first-published backfill` must run first. A `bytesDiffer` failure names the first record whose entry differs, or no record when the order, the withdrawals, or the top-level fields differ. A person must compare the folder with the tables.

### Take a record off the site

Only the owner sees Take off site. It is on the editor of every record kind that releases publish, below the decision panel. The button shows only while the active release holds the record. Type a reason of 1 to 500 characters and press the button. The page then does these steps:

1. It checks the active release. If the release already withdraws the record, it serves that release again and stops. Otherwise it checks that the record is on the site, and that no relationship or brief on the site cites it. If a check fails, the page writes no decision and names the citing records. Take those off first.
2. It records a decision on the record. An approved record gets Withdraw approval. A record with a newer revision that waits for review gets Request changes. A record that review already blocks, or that review rejected, gets no new decision.
3. It withdraws the record in the review workspace with the same code as `releases publish --withdraw`. The release makes the record page a notice. The reason shows on the notice.

Both writes use keys that come from the key of the form. If step 3 fails, press the button again. The retry records no second decision. If the release committed but the serve failed, readers still see the old site. The panel then shows a warning and keeps the button. The repair does not depend on the form key. When the active release already withdraws the record, the button only serves that release again. It writes no decision and no new release. A change of the review state while the page was open shows a conflict message, and the page writes nothing. If another release goes out first during step 3, the page says that the site still shows the record. It also says that the decision is saved. Press the button again. If the server cannot write or move the release, the page says so and keeps the button. Check the release directory on the server, then press it again. After any success, the page reads the active release. If that release still lists the record, the page says so and does not report success.

The panel warns when someone rolled the site back. While that lasts, the worker publishes nothing. Taking a record off then publishes a new release. That ends the rollback pause, and the worker then publishes every ready approval. The panel does not refuse the action.

An off-site record returns only when a person approves a newer revision with a correction note. A code approval never returns it.

The review workspace needs `CIVIC_LENS_RELEASE_DIRECTORY`, `CIVIC_LENS_ARTIFACT_DIRECTORY`, `CIVIC_LENS_COLLECTION_CONFIG`, and `CIVIC_LENS_ISSUES_CONFIG`. The `review` service in `deploy/compose.yaml` has all four. It mounts the release directory read and write.

### Return a record

To bring a withdrawn record back, first revise it in review. A person presses Save and approve with a correction note. The press needs an edit, unless the current revision is already higher than the withdrawn revision, because a press with no edit approves the same revision. The revision number must be higher than the withdrawn revision. The stage `publishing` then returns the record. You can also run `releases publish <idempotency-key> <kind>:<id>` for a person approval. The note shows on the record page as a correction. A withdrawn record whose newest approval has no note, or was made by code, stays off the site, and the worker logs and skips it. A code approval never returns a record, also with the command. To free such a record, withdraw the code approval and press Save and approve again with a note. The command exits with code 2 if the approval has no note or the revision is not higher.

A relationship or a brief that cited the record stays withdrawn. Publish it again, in the same request or later.

A concurrent review decision, draft save, other release, or rollback makes the commit fail with exit code 1. The built directory is deleted and nothing is activated. Invalid input or a non-owner actor exits with code 2 and prints the rule that failed. The publish rules are in [publication](architecture/publication.md#publish).

Repeat the same key and records to receive the original result. The repeat also repairs a link swap that failed after the commit. To roll back, activate an earlier release number. If the files of that release are gone, the command prints "Release N has no files on this machine, so it cannot be activated.", exits with code 2, and changes nothing. The next `publish` builds on the active release, so records added after it are not carried forward. `list` shows `activeReleaseNumber`, the release that Postgres records as active, and `servedDirectoryName`, the release that the link serves. If they differ, run `releases activate` with the active release number. Each publish prunes old release folders; see [release retention](architecture/publication.md#release-retention).

The command `releases activate` prints one object with four fields. `summary` is the activated release. `restoredWithdrawals` lists records that the replaced release withdrew and the activated release shows again. `withdrawnAgain` lists records that the activated release withdraws and the replaced release showed. `blocked` lists records of the activated release that review now blocks. The command sends no wake to the worker. After you activate the latest release, a stage that waited resumes at its next wake: an approval, the end of the 10 minutes, or the next full hour. The lists describe the release that the link serves. Read them after each rollback. A rollback to a release that a prune removed is refused with "has no files on this machine", and nothing changes. Each publish keeps the files of the 30 highest release numbers and the highest release of each of the last 7 UTC days. It also keeps the active release and the release that `current` serves. The daily releases need 7 days to build after the first deploy, so full rollback reach takes 7 days to appear. A failed read gives a warning on standard error and empty lists, and the switch stays done. If the manifest of the activated release cannot be read, all three lists are empty. If the manifest of the replaced release cannot be read, `restoredWithdrawals` and `withdrawnAgain` are empty. If the review read fails, `blocked` is empty.

To check that the public pages work without the operational application, stop the review workspace, the worker, and Postgres. Then serve the active release with a plain static server:

```sh
python3 -m http.server 8090 --bind 127.0.0.1 --directory "$CIVIC_LENS_RELEASE_DIRECTORY/current"
```

Open `http://127.0.0.1:8090/`. The home page shows the hero with the break card, the latest brief, the issue tiles, and the latest votes and executive actions. `all.html` shows 50 records for each page, with links to the previous and next pages, and a kind label next to each headline. Every page links to `search.html`. A document change page shows the account, the changes, the citations, and both full document versions. A statement page shows the speakers, the statement date with its evidence, each quotation with its kind, and the full source text with the quotations marked. An action page shows the bill, a sponsorship table with cited quotes, the policy area and CRS summary citations, and its Congress.gov sources. A statement, action, or vote page lists the related records that published relationships link to it. A relationship page shows the quote, the fact, and the two linked records. The production static server is still a deployment decision.

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
export CIVIC_LENS_PUBLIC_VIEWS_DATABASE="Host=127.0.0.1;Port=5433;Database=civic_lens;Username=civic_lens_public;Password=<password of the public role>"
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

The `Dockerfile` in the repository root builds two images for linux-arm64. The workflow `.github/workflows/release.yml` builds them on a native ARM64 runner. On main, from a push or a manual run, it pushes them to ghcr.io and adds a build provenance attestation to each. On a pull request that changes the build inputs, it builds them and does not push or attest. The workflow passes the commit SHA as the build argument `SOURCE_REVISION_ID`, and the `Dockerfile` fails without it. Each `dotnet publish` gets it as `SourceRevisionId`, and `CodeVersion` reads it from the assembly. A local `docker build` needs `--build-arg SOURCE_REVISION_ID=<40 hex digits>`. Pull request runs never wait for or cancel main runs. A newer main run can cancel a queued main run, so deploy only a commit whose image tag exists.

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
| `CIVIC_LENS_API_PORT` | The loopback port of the search service. Default 8100. The compose file sets `ASPNETCORE_URLS` of the search service from it |

- `civic-lens.info` serves the static release. `file_server` uses the precompressed `release.json.gz` and `release.json.br`, the only compressed files that a publish writes. Caddy compresses other responses when it sends them. A missing path gets the release `404.html` with status 404. Other error statuses, for example 405, get a plain text body. The same block also answers `127.0.0.1`, so the search service on the same machine can read the release without the tunnel. A request for any other host name gets 404.
- `api.civic-lens.info` proxies to the search service. `/internal/*` gets 404 and never reaches the service. Caddy passes the `CF-Connecting-IP` header unchanged. Cloudflare sets it on every request. It refuses a request that already carries the header, with status 403 and error code 1000. The service trusts it because the peer is loopback.
- `Cache-Control` values: `release.json` and every other file, HTML pages and assets included, have `public, max-age=60`. A browser that cached a page before a publish can show the old page for up to 60 seconds after the publish. The search service reads Caddy on loopback, not through the CDN. Only `photos/<64 hex characters>.jpg` has `public, max-age=31536000, immutable`, because the name is the hash of the content. Error pages have `no-store`.
- Every page, asset, and error that Caddy answers from its own blocks has `X-Content-Type-Options: nosniff` and `Referrer-Policy: strict-origin-when-cross-origin`. A 502 from the search proxy and a 404 when the release has no `404.html` carry neither header. No Content-Security-Policy is set.
- Caddy sends no `X-Robots-Tag` header, so search engines can index the site. The published pages set `noindex` only on the pages that a reader must not land on from a search. `robots.txt` stays as published.
- Never add a `log` directive to a site block. Without one, Caddy writes no access log. The privacy page says that page reads and searches leave no record. Caddy still writes error lines, for example when `404.html` is missing. The global `log` block in the Caddyfile filters them. They keep the host, method, status, and error. They hold no query string, headers, or IP address. A file error can name the file path of the page, with no reader data. Do not weaken that filter.
- Check the rule with `caddy adapt`. The output must have no `logs` key inside `apps.http.servers`, and the global `logs` entry must delete `request>uri`, `request>headers`, `request>remote_ip`, `request>remote_port`, and `request>client_ip`. `deploy/scripts/check-live.sh` checks the live log; see [Go-live checks](#go-live-checks).

Check the file with `caddy validate --config deploy/caddy/Caddyfile --adapter caddyfile`, with `CIVIC_LENS_RELEASE_DIRECTORY` set.

## Dev database reset

Reset the development database to start again from an empty state. Production has no reset path, and it needs none.

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

A release contract change can add a required field to a published file. Old files then do not read, and the next publish fails. On the dev machine, repeat steps 1 to 5. These steps delete all captures and records, not only the releases. Collect again and let code approve the facts, then publish a fresh release with `releases publish` as in [Publish a static release](#publish-a-static-release). Production holds live releases, so do not reset it for a contract change. Add the field as optional and fill the carried entries with `--rewrite-entries`; see [Publish a static release](#publish-a-static-release).

## Production compose

`deploy/compose.yaml` runs the temporary production machine with Docker Compose v2. It starts two Postgres containers, the worker, the review workspace, the search service, Caddy, and cloudflared. Check out the repository at the commit that you deploy, because Compose mounts `deploy/caddy/Caddyfile` from the checkout. The images are described under [Container images](#container-images). Backups run from the same file; see [Production runbook](#production-runbook).

- The worker, the review workspace, the search service, Caddy, and cloudflared use host networking. The review workspace keeps its loopback listener and the search service trusts only loopback proxies. Reach the review workspace through `tailscale serve`, not through Caddy.
- The Postgres containers publish `127.0.0.1:5433` (operational) and `127.0.0.1:5434` (search). The connection strings in the secret env files name these ports. Change both places together.
- The root is `/srv/civic-lens`, the Linux convention for data that a machine serves. To use another root, give `CIVIC_LENS_ROOT` to every script, for example `sudo CIVIC_LENS_ROOT=/data/civic-lens deploy/scripts/update.sh <sha>`. `sudo` drops an exported value. Every directory is mounted at the same path inside the containers, so one path value serves both sides.
- Under the root, `env/` holds the env files, `config/` holds `collection.json`, `issues.json` and `issue-icons.json`, and `captures/`, `releases/`, `review-keys/` and `postgres/` hold data. `captures/`, `releases/` and `review-keys/` belong to user 10001, the user of our images. The two Postgres directories belong to user 70, the `postgres` user of the Alpine Postgres image.
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

`update.sh` stops when the checkout is not at the given SHA, when `deploy/` has uncommitted changes, or when the Caddy port in `compose.env` differs from the port in the tunnel config. It sets the image tag for the run and pulls the images. It stops the worker and the review host, so no old image runs on the new schema or commits a release during the migration. Then it applies the migrations with a one-off `cli` container. Then it writes the tag to `compose.env`, because the schema now belongs to that commit. Last, it recreates the services, and it always recreates Caddy and cloudflared so that a changed Caddyfile or tunnel config takes effect. The worker stays stopped until the services start from the new tag. A search service of an older image fails the ingest of a release with a new published value, keeps its last index, and tries again at the next interval. It recovers by itself once the new image runs. If a step after the migration fails, run `update.sh` again with the same SHA. If the migration fails, the worker and the review host stay stopped. Fix the cause and run `update.sh` again, or run `sudo deploy/scripts/compose.sh up --detach worker review` to start the old services. Services never migrate at start. The tag must be a full commit SHA whose images exist. To roll back, run `update.sh` with an earlier SHA. A rollback does not undo a migration, so a rollback past `DropDecisionJson` needs the steps in [Postgres adapter and migrations](#postgres-adapter-and-migrations) first. If the migration `DropPublicationRecordsJson` raises "Cannot drop records_json", run `db migrate` from the prior image, which still rebuilds the record state table, and then run `update.sh` again. If the migration `DropDecisionJson` raises "Cannot drop decision_json", the migration rolled back, the column is intact, and `compose.env` still holds the old tag. Run `sudo deploy/scripts/compose.sh up --detach worker review` to restore service, and give the named decision to the developer.

**Receipt spool check.** Do this before an `update.sh` that changes the receipt handoff version. Stop the worker with `sudo deploy/scripts/compose.sh stop worker`. Run `sudo ls -A /srv/civic-lens/captures/.pending`. An empty directory is the goal. Each file is a receipt that was saved but not yet imported. Let the old worker finish first, so that the spool empties. If a file remains, the new worker refuses it. It logs an error, removes the file, settles the attempt as interrupted with the code `HandoffRefused`, and fetches the job again. The saved capture file stays in the directory and is not used. `receipts replay` does not do this. It reports the file as invalid and keeps it.

**Vote revision JSON drop.** The migration `DropVoteRevisionJson` stops with `Cannot drop revision_json` when a vote revision has a null column. The message names the revision. Nothing changes in that case. Run the vote revision backfill with the previous image. The current image has no `votes revisions` command. Then run `db migrate` again.

`compose.sh` is `docker compose` with the compose file and `compose.env` set. The `cli` service starts the host image with no command. The `restic` service has the same `tools` profile. Add the host command after `cli`, for example `releases publish` or `db migrate`.

## Production runbook

This section is for the temporary production machine, a Jetson with Docker Compose v2.

**Vendor data rule.** Cloudflare may receive public content and the encrypted backups. An agent that uses [operator access](#operator-access) sends each result that it reads to its model vendor, and that can include unpublished drafts and model output. Nothing else leaves the machine. The review workspace is never behind Cloudflare, because it holds review notes and reviewer identity.

### First install

1. Install Docker with Compose v2. Clone the repository to `/opt/civic-lens` and check out the commit to deploy. The systemd units use this path. It must be on the root filesystem, because systemd loads the linked units at boot, before other disks mount.
2. Run `sudo deploy/scripts/install.sh`. It creates the directories under `/srv/civic-lens` and the env files from the templates.
3. Fill in the env files in `/srv/civic-lens/env`, and put `collection.json`, `issues.json` and `issue-icons.json` in `/srv/civic-lens/config`.
   The `api` sources in `config/federal-coverage.json` ship with `enabled: false`. Without them the site has no bills, votes, roster, or photos, so nothing publishes. Set `enabled` to `true` for `congress-members`, `congress-bills`, `congress-house-votes`, `voteview-senate-votes`, `member-photos`, `executive-officials`, `federal-register-documents`, and `census-zcta-districts`. See [Voteview Senate vote stream](#voteview-senate-vote-stream) for the Senate votes. The two Congress.gov sources need `CIVIC_LENS_CONGRESS_API_KEY` in `host.secret.env`. See [Congress.gov bill stream](#congressgov-bill-stream) and [Member roster](#member-roster). The Host refuses a configuration file over 1 MB, and added indentation can push this file past that limit. So save it compact.
   Set `CIVIC_LENS_COLLECTION_CONCURRENCY=3` in `/srv/civic-lens/env/host.env`.
4. Create the tunnel (see [Tunnel](#tunnel)) and the restic password file and repository (see [Backups](#backups)).
5. Run `sudo deploy/scripts/update.sh <full-commit-sha>`.
6. Set up Tailscale for the review workspace (see [Review workspace access](#review-workspace-access)).
7. Enable the backup timer.
8. Run `sudo deploy/scripts/check-live.sh`. See [Go-live checks](#go-live-checks).

Do step 5 only after step 4. Without the credential file, the cloudflared container cannot start.

### Go-live checks

Run `sudo deploy/scripts/check-live.sh` from `/opt/civic-lens` after the first install and after each `update.sh`. The script prints one PASS or FAIL line per check, runs every check, and exits with code 1 if one failed. Each FAIL line says what it expected, what came back, and what to check. It sends requests to the public host names, so it needs the tunnel. `update.sh` recreates `cloudflared`, and Cloudflare answers 530 until the new tunnel connects. The script first tries each host name up to 12 times, 5 seconds apart, until it answers with a status other than 530. A failed request also counts as not ready. Then it runs the checks. If a host still answers 530 or fails, it prints a NOTE and the checks fail as usual. The checks are:

- `X-Robots-Tag: noindex` on the home page and on an api response. The script reads `deploy/caddy/Caddyfile`. With an `import noindex` line, each response must carry the header. Without one, no response may carry it.
- A missing page returns 404 with the release 404 page and `Cache-Control: no-store`.
- `api.civic-lens.info/internal/v1/reports` returns 404. With a pull token set, the search service answers that route with 401, so a 404 proves that Caddy blocks it. Without a token, the route does not exist and the check proves nothing.
- Caddy keeps no access log. The script reads your address from `/cdn-cgi/trace` of the site, then requests a missing path with a random query value. Then it reads the Caddy log of the last 5 minutes. The log must hold neither the query value nor your address.
- A backup succeeded less than 26 hours ago. `backup.sh` writes the time to `/srv/civic-lens/backup/last-success` as its last step, so a failed run leaves the old time. A reboot does not clear the file.
- A reader cannot forge `CF-Connecting-IP`. A request that carries the header gets status 403 from Cloudflare.
- The search service limits each reader. The script reads `CIVIC_LENS_REQUESTS_PER_MINUTE` from `search.env`, with 60 as the default. It fails if `CIVIC_LENS_TRUSTED_PROXY_RANGES` is empty, because then all readers share one limit. It sends twice the limit plus two requests to the search route, 20 at a time, and one must get 429. The limit counts in fixed one-minute windows, so this number fills one window even when a window ends during the check. This check runs last. It blocks the address of the caller on the search service for up to one minute.

**Withdraw test.** The script does not test withdrawal, because the test changes the live site. Do it by hand at the first launch and after a change to publication code. Do it only when a record is ready to publish, for example an approved statement. The rollback in step 5 pauses the publishing stage, and the publish in step 7 ends the pause; see [publication](architecture/publication.md).

1. Stop the worker, so that no fact release lands during the test: `sudo deploy/scripts/compose.sh stop worker`.
2. Pick one published record that no relationship or brief cites, or the withdrawal stops with an error.
3. In the review workspace, open the editor of the record and use Take off site with a reason. This checks the page path. To check the CLI path, request changes on the record and run `sudo deploy/scripts/compose.sh run --rm cli releases publish <idempotency-key> --withdraw <kind>:<id> <text>` with a new key.
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

1. Stop the backup and health timers and the services that use the databases: `sudo systemctl stop civic-lens-backup.timer civic-lens-health.timer` and `sudo deploy/scripts/compose.sh stop worker review search search-postgres`.
2. Restore the snapshot to a work directory: `sudo deploy/scripts/compose.sh run --rm -v /srv/restore:/restore restic restore latest --target /restore`. The files keep their full paths, for example `/srv/restore/srv/civic-lens/captures`.
3. Drop and create the operational database again. A restore into the old database would keep the tables of newer migrations. Start Postgres if it is down. Then run:

```sh
sudo deploy/scripts/compose.sh exec -T postgres sh -c 'psql --username="$POSTGRES_USER" --dbname=postgres --command="DROP DATABASE \"$POSTGRES_DB\" WITH (FORCE)" --command="CREATE DATABASE \"$POSTGRES_DB\" OWNER \"$POSTGRES_USER\""'
sudo deploy/scripts/compose.sh exec -T postgres sh -c 'pg_restore --single-transaction --exit-on-error --no-privileges --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' < /srv/restore/srv/civic-lens/backup/operational.dump
```

   Then run `db migrate`, because `--no-privileges` leaves out the grants of the public role and the revokes from `PUBLIC`.
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
7. Run `sudo deploy/scripts/install-operator-access.sh "<agent public key>"` again, because the restore leaves out the grants of the read-only role.
8. Start the timers: `sudo systemctl start civic-lens-backup.timer civic-lens-health.timer`. When the site works, delete `/srv/restore` and the `.old` folders.

The dump file stays in `/srv/civic-lens/backup`, mode 700, until the next run replaces it.

### Operator access

One SSH key can run two root-owned commands. It belongs to the AI agent on the development machine. It is not a login: the key has no shell, forwarding, or terminal, and it runs only `/usr/local/bin/civic-lens-agent`. That program takes `sql`, or `ops` and a listed command. The key maps to the user `civic-lens-agent`, which has no password, no docker group, and no other sudo. No other user gets these commands.

The agent makes the key, and the private key stays on the development machine. Install it once with the public key:

```sh
sudo deploy/scripts/install-operator-access.sh "ssh-ed25519 AAAA..."
```

To end the access, delete `/etc/sudoers.d/civic-lens-operator` and the `authorized_keys` file of that user.

The installer refuses a checkout that anyone other than root can write, because the commands run scripts from it as root. Run the same install command again after a migration, because a new table or column stays unreadable until then. Run it again after a restore too, because the restore leaves out the grants of the read-only role. Run it again when one of the scripts or `readonly-grants.sql` changes, because the installed copies and the grants do not follow the checkout. The commands expect the checkout at `/opt/civic-lens` and the data root at `/srv/civic-lens`. The `origin` remote must use HTTPS, because root has no SSH key for `deploy`.

**Read-only SQL.** `/usr/local/bin/civic-lens-sql` reads SQL from stdin and runs it as the Postgres role `civic_lens_readonly`. The role has no write privilege on any table and cannot create temp objects. A statement stops after 30 seconds by default, but a query can change that limit, and a long query can delay a migration. The role cannot read review notes, reviewer identity, credentials, or raw collector requests. `deploy/scripts/readonly-grants.sql` owns that list of hidden columns. Revision JSON is hidden too. So are the headline, summary, fact text, and suggestion columns of vote and action revisions, and the `user_message` column of every run table. `readonly_access.revisions` takes any `_revisions` table, for example `'action_revisions'`. It returns each revision row as one JSON value in `row_data`, without the author. A table that still has `revision_json` returns it inside the row, also without the author. It is the one exception: `row_data` holds the revision text columns for every such table. The command refuses a backslash, because a psql command can run a shell.

Every result that an agent reads goes to its model vendor, so add a column to the hidden list before it holds private text.

**Listed commands.** `/usr/local/bin/civic-lens-ops` runs only the commands that `deploy/scripts/operator-command.sh` lists: `status`, `release sizes`, `deploy`, `db migrate`, `worker health`, `analyses redraft --stale` with an optional stage, `bills subjects backfill`, `bills status backfill`, `actions revisions backfill`, `actions revisions verify`, `executive-actions revisions backfill`, `executive-actions revisions verify`, `relationships revisions backfill`, `relationships revisions verify`, `statements revisions backfill`, `statements revisions verify`, `brief revisions backfill`, `brief revisions verify`, `document-changes revisions backfill`, `document-changes revisions verify`, `releases first-published backfill`, `releases entries backfill` with a count, `releases entries verify`, and `releases publish` with a key and the rewrite options. It cannot withdraw, activate an older release, reset a database, or name a record. `release sizes` counts a shared file once, so the oldest release shows its full size and each later release shows only its new bytes. Only one command runs at a time, apart from `status`, `release sizes`, and `worker health`.

`deploy` takes a full commit SHA on `origin/main` that is the checkout or newer. The scripts of that commit run as root, and a migration has no undo, so a rollback stays with the owner. It checks out the commit, runs `update.sh`, and runs `check-live.sh`. A merge to `main` is therefore the real approval. GitHub does not stop the agent from merging, because it uses the owner's token. Only the agent rules and the Claude Code permissions stop it.

### Deploy with backfills

Use this order when a deploy also needs backfills. Each step links to the section that owns the detail. Steps 1, 3, 4, and 9 need the owner, because the listed operator commands cannot stop or start the worker or run a backup. The other steps can use the [listed commands](#operator-access).

1. Owner: take a backup. Run `sudo systemctl start civic-lens-backup.service`; see [Backups](#backups).
2. Deploy the commit SHA. `deploy` runs `update.sh`, which pulls the images, stops the worker and the review host, runs `db migrate`, and then starts every service again, the worker included. It then runs `check-live.sh`. The worker is therefore running after this step.
3. Owner: stop the worker before the backfills. Run `sudo deploy/scripts/compose.sh stop worker`.
4. Owner, only when `operator-command.sh` gained commands: install the operator access again; see [Operator access](#operator-access). The agent cannot run a command that is not yet installed.
5. Run each revisions backfill, then its verify, one kind at a time: [action](#action-revision-backfill), [executive action](#executive-action-revision-backfill), [statement](#statement-revision-backfill), [brief](#brief-revision-backfill), [document change](#document-change-revision-backfill), and [relationship](#relationship-revision-backfill). Run a backfill again until its verify passes.
6. Run the [first publication backfill](#first-publication-backfill), and then the [entries backfill](#entries-backfill).
7. When the release format changed, run a rewrite publish with the options that the change needs; see [Publish a static release](#publish-a-static-release).
8. Run [entries verify](#entries-verify) and read the result for the newest release first.
9. Owner: start the worker. Run `sudo deploy/scripts/compose.sh up --detach worker`.
10. Run the [go-live checks](#go-live-checks).

### Failure alerts

If `civic-lens-backup.service` fails, systemd starts `civic-lens-alert@civic-lens-backup.service`. That unit runs `deploy/scripts/alert.sh` with the failed unit name. The script sends one email through the Resend HTTP API from `alerts@civic-lens.info`. The body holds the unit name, the host name, the time, and the command `journalctl -u <unit>`. It holds no log line and no other data. Resend receives only this text. If the send fails, the script exits with an error in the journal of the alert unit. Other jobs can use the script: give it any unit name. `deploy/scripts/send-alert.sh` owns the Resend call, and the failure alert and the [health alert](#health-alerts) both use it.

1. In Resend, add and verify the `civic-lens.info` domain. Add the DNS records that Resend shows in the Cloudflare zone.
2. Make a send-only API key in Resend.
3. Fill in `/srv/civic-lens/env/alert.secret.env` with the key and the recipient address. The file has mode 600. The script passes the key to `curl` in a temporary header file, not on the command line.
4. Check that `curl --version` shows version 7.55 or later. The script needs `curl` on the host.
5. Test: `sudo systemctl start civic-lens-alert@civic-lens-test.service`. An email arrives that names the unit `civic-lens-test`. Read `sudo journalctl -u civic-lens-alert@civic-lens-test.service` if it does not.

### Health alerts

`civic-lens-health.timer` runs `deploy/scripts/check-health.sh` every 10 minutes. The script sends one email when `worker health` exits 1, or with any code other than 0 and 3. It sends a separate "release is full" email when `worker health` exits 3. It sends one email when free space under `/srv/civic-lens` is below 10 GB (`min_free_gb` in the script). The 10 GB value is a guess, not a measurement. A failed `docker compose` call also counts as an unhealthy worker. The email holds a fixed sentence, the host name, and the time. It holds no output from the worker. The check measures only the filesystem that holds `/srv/civic-lens`. It does not measure `/var/lib/docker` or `/tmp` if they sit on another filesystem. The worker check stops after 120 seconds and does not start stopped services.

The script writes a marker file in `/run/civic-lens` for each problem that it reported. The markers are `disk.alerted`, `worker.alerted`, and `release-full.alerted`. One alarm never silences another. The release is full email says that new records wait for room and that you must ask the developer to raise the cap. A clear check removes only its own marker. A full release stays until a deploy raises the cap. While the marker exists, the script sends no more email for that problem. When the check passes, the script removes the marker, so the next failure sends a new email. A reboot clears `/run`, so a problem that lasts through a reboot sends one new email. If a send fails, the script writes no marker and tries again on the next tick. A failure of the check itself starts the failure alert for `civic-lens-health.service`.

A worker beat can be up to 10 minutes old before it fails health, and the timer runs every 10 minutes. So a dead worker sends an email 10 to 20 minutes after it stops. Each documented worker stop, such as the withdraw test or a long rewrite, can send one email if it lasts that long. A deploy or a reboot restarts the worker. `worker health` can report no heartbeat until every loop has started, so a deploy or a reboot can send one health email that clears by itself. See [Managed collection jobs](#managed-collection-jobs) for the rules.

Install it after the failure alert works. The unit files link from the checkout:

```sh
sudo systemctl link /opt/civic-lens/deploy/systemd/civic-lens-health.service /opt/civic-lens/deploy/systemd/civic-lens-health.timer
sudo systemctl enable --now civic-lens-health.timer
```

Test the worker path: run `sudo deploy/scripts/compose.sh stop worker`, wait more than 10 minutes, run `sudo systemctl start civic-lens-health.service`, and read the email. Then run `sudo deploy/scripts/compose.sh start worker`. The marker stops a second email, so run `sudo rm /run/civic-lens/worker.alerted` before you test again. For the release is full email, remove `/run/civic-lens/release-full.alerted` instead. Test the disk path the same way: edit `min_free_gb` in `/opt/civic-lens/deploy/scripts/check-health.sh` to a value above the free space, run `sudo systemctl start civic-lens-health.service`, then undo the edit. Check `systemctl list-timers civic-lens-health.timer` every week.

### No page logs

Caddy keeps no access log for page reads or searches. The About page privacy claims depend on this. Do not add a `log` directive to a site block. See [Web server](#web-server).

## Runtime data

Runtime data belongs in ignored .runtime/. Generated publication and evaluation artifacts belong in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. The deploy files are in [Production compose](#production-compose). Published releases live under the configured release directory, not in the repository.
