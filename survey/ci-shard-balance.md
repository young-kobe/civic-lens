# CI shard balance survey

Commit: de620566 (origin/main). This survey changes no code. Seconds come from the single run in survey/test-pruning.md on branch survey-test-pruning (1,404 summed class seconds over 348 classes). Job times are the measured values from recent CI runs.

## Summary

- Shard 3 takes 1,030 to 1,170 s. Shards 0, 1, and 2 take 290 to 475 s. The hash balances class count, not class cost.
- The three slowest classes cost 459 s. All three hash to shard 3.
- The Postgres collection runs its classes one by one. Shard time follows the Postgres seconds in the shard.
- A committed weight table with longest-first assignment gives about 580 to 620 s for the longest job. That is the recommendation.

## Current hash

`tools/test-shards.py` hashes the full class name that `dotnet test --list-tests` prints. That name includes the namespace. A short class name gives other results. The 449 test classes split 109, 118, 121, and 101 over shards 0 to 3. That split is even.

The Postgres collection classes split 25, 24, 19, and 24. That split is also close to even. Cost is not even.

The survey lists seconds for only 34 classes. They total 868 s. The other 415 classes share the remaining 536 s. This survey spreads those 536 s evenly: 466 s over the 77 unlisted Postgres classes and 70 s over the 338 other classes.

| Shard | Listed classes (s) | Modeled total (s) | Measured job (s) |
|---|---|---|---|
| 0 | 128 | 278 | 290 to 340 |
| 1 | 11 | 156 | 430 to 475 |
| 2 | 173 | 290 | 435 to 455 |
| 3 | 556 | 680 | 1,030 to 1,170 |

The model ranks shard 3 as the slowest. It also ranks shard 1 too low. Shard 1 holds 24 Postgres classes whose seconds the survey does not list. The model for shards 0, 1, and 2 is weak. The model for shard 3 is firm, because five listed classes make up most of it.

## Why shard 3 is slow

Shard 3 holds these listed classes. All are Postgres collection classes except where noted.

| Class | Seconds |
|---|---|
| PostgresHouseVoteTests | 176.8 |
| PostgresBillStreamTests | 142.5 |
| ReviewWorkspaceTests | 140.2 |
| ReaderReportWorkspaceTests | 34.7 |
| PostgresDocumentExtractionStoreTests | 31.9 |
| PostgresStatementReviewStoreTests | 22.2 |

These six classes total 548 s. The top three are the three slowest classes in the suite. The hash puts them in one shard by chance. Each of them is a Postgres collection class, so none runs in parallel with another.

Shard 0 holds DatabaseCommandTests (73.4 s) and StatementReviewWorkspaceTests (42.9 s). Shard 2 holds PublishReleaseTests (72.8 s) and BillLinksWorkspaceTests (34.7 s).

## Where wall time goes in a shard

The `test` job in `.github/workflows/ci.yml` runs these steps in order.

1. Checkout, `setup-dotnet`, and `setup-python`.
2. `dotnet restore --locked-mode`.
3. `dotnet build --no-restore --configuration Release`.
4. `docker version`.
5. `tools/test-shards.py`. It runs `dotnet test --list-tests` to find the classes, then writes `shard.runsettings`.
6. `dotnet test --no-build --settings shard.runsettings`.

The `verify` job runs steps 1 to 3 plus format, diagram, and docs checks in about 200 s. Steps 1 to 3 therefore cost about 150 s in a shard. The survey did not time steps 4 to 6 separately. The estimates are test discovery at 10 to 20 s and the Postgres image pull and start at 15 to 30 s. The container also migrates one template database. Fixed cost per shard is about 190 s. This figure is an estimate.

The test step is the rest. Measured job time minus 190 s gives these test times: shard 0 about 100 to 150 s, shard 1 about 240 to 285 s, shard 2 about 245 to 265 s, and shard 3 about 840 to 980 s. They add to about 1,550 s against 1,404 summed class seconds. The ratio is about 1.1.

Inside the test step, xUnit works in two phases.

- `PostgresCollectionDefinition` sets `DisableParallelization = true` in `tests/CivicLens.Tests/Infrastructure/Collection/PostgresCollectionAttemptStoreTests.cs`. xUnit runs the other collections first. It runs this collection last, alone.
- 92 classes carry `[Collection(PostgresCollection.Name)]`. Classes in one xUnit collection never run in parallel. They run one by one against one container.
- The other classes form their own collections. They run in parallel up to the thread limit. They cost about 210 summed seconds for the whole suite.
- Each Postgres test already creates its own database from a migrated template (`CreateMigratedDatabaseAsync`). Isolation exists. The serial run is a property of the collection, not of the data.

A shard finishes in about fixed cost, plus a short parallel phase, plus its Postgres seconds. The Postgres seconds decide the longest shard. They total 1,194 s over 92 classes. Perfect balance gives about 300 s of Postgres time per shard.

## Options

Job seconds below use 190 s fixed cost plus 1.1 times summed seconds. The longest-class floor is 177 s. Modeled sums use the even spread above, so they carry its error.

### (a) Committed weight table with longest-first assignment

- **Change.** Add a committed table of class weights, for example `tools/test-shard-weights.json`. `test-shards.py` reads it. It sorts classes by weight, heaviest first, and gives each class to the lightest shard. Ties break by class name. A class with no entry gets a default weight, for example the median of the table. A `--write-weights` mode could turn the TRX files of one full run into a new table.
- **Balance over time.** The table needs a refresh when class cost drifts. A new class gets the default weight until the next refresh. Longest-first assignment moves classes when weights change. That is safe, because shards share no state.
- **New file or convention.** One JSON file and one refresh command. The docs in `docs/operations.md` gain one sentence.
- **Expected longest shard.** Modeled sums are 351 s on all four shards. The job is about 580 s. Allow 10 percent for stale or default weights: about 620 s.
- **Tradeoffs.** It fixes the cause. It needs one full timed run to seed the table. A stale table degrades slowly toward the current result, not suddenly.
- **Precedent checked.** `tools/test-shards.py` is the owner of the assignment rule today. `config/issues.json` and `config/federal-coverage.json` show that the repo keeps committed data tables read by code. `docs/operations.md` line 19 documents the current rule.

### (b) Hash salt chosen once

- **Change.** Hash a fixed salt plus the class name. Pick the salt by trying many values against the known class seconds. Store the salt as a constant in `test-shards.py`.
- **Balance over time.** Weak. The salt balances only the classes that were known when it was chosen. A new heavy class lands at random. A shift in cost needs a new search. The search needs the same weights as option (a).
- **New file or convention.** One constant and a search script. The search script is a new tool.
- **Expected longest shard.** The survey tried 20,000 salts on the 34 listed classes. The best salt gave 220 s on the listed classes alone. Against 300 random draws for the unlisted classes, the longest shard averaged 1.05 times the mean and peaked at 1.13 times. The current hash averaged 1.92 times. The mean is 351 s. The longest shard is about 370 s, or 600 s as a job. This holds only if no unlisted class is heavy. The listed top ten stops at 31.9 s, so a class of 10 to 30 s may hide in the unlisted set.
- **Tradeoffs.** It adds no runtime table. It hides the weights inside one number, so a reader cannot see why the shards balance. It needs the data of option (a) to be trustworthy.
- **Precedent checked.** The crc32 hash in `tools/test-shards.py`. A search for "salt" in the repo found no salt for any hash. This option sets a new convention.

### (c) More shards

- **Change.** Change `--shards 4` and the matrix in `ci.yml`. Add no file.
- **Balance over time.** None. The hash stays blind to cost. With more shards, the single largest classes each sit alone more often, which helps a little.
- **New file or convention.** None.
- **Expected longest shard.** Modeled with the current hash: 5 shards give 511 s, 6 shards give 468 s, 8 shards give 382 s. As jobs that is about 750 s, 700 s, and 610 s. Only 8 shards with longest-first assignment reach the floor of 177 s, about 395 s as a job.
- **Tradeoffs.** Each shard repeats the 190 s fixed cost. Eight shards use about 3,100 runner seconds. Four balanced shards use about 2,300. It costs more and wins less than option (a).
- **Precedent checked.** The `shard: [0, 1, 2, 3]` matrix in `ci.yml` and the `--shards` flag. `docs/operations.md` says the count is 4.

### (d) Run the Postgres classes in parallel

- **Change.** Removing `DisableParallelization` is not enough. Classes in one collection still run one by one. Each class must leave the collection. They can share one container through a lazily created static server. Each class would stop using `ICollectionFixture`.
- **Balance over time.** Better. A shard's Postgres seconds shrink by the parallel gain. Imbalance between shards shrinks in absolute seconds. The hash still ignores cost.
- **New file or convention.** A shared static server type. Edits to 92 classes. A new rule: no test may depend on being alone on the server.
- **Risk.** Several tests may break under parallel load. Candidates are the 15,000 row timing test in `PostgresPublicationRecordStateTests`, the sleeps (`Task.Delay(300)` and `Task.Delay(500)`), `PostgresIntervalWakeupTests` (2.5 s), and the Host classes that start a process. Concurrent `CREATE DATABASE` from one template may also fail while another copy runs. The survey did not test that. The survey did not check the runner CPU count (4 in this session). Postgres, test threads, and Host processes would share them.
- **Expected longest shard.** Unmeasured. If the Postgres phase gains 2.5 times on 4 CPUs, shard 3 falls from about 640 Postgres seconds to about 260. With the current hash, the job is about 190 + 260 + 80 = 530 s. With option (a) added, about 410 s. These figures are guesses.
- **Tradeoffs.** It gives the largest gain. It touches the most code and the test rules. It also changes a concurrency convention. AGENTS.md asks for an owner decision on that.
- **Precedent checked.** The template database clone in `PostgresCollection.CreateMigratedDatabaseAsync` already isolates each test. No other `ICollectionFixture` or `IClassFixture` exists in `tests/`, so the repo has no second pattern to copy. `survey/test-pruning.md` (Slow but needed) proposes shared databases per class, which is a different change.

## Comparison

| Option | Longest job (s) | Stays balanced | New artifact |
|---|---|---|---|
| Current hash | 1,030 to 1,170 | No | None |
| (a) Weight table, longest first | 580 to 620 | Yes, with refresh | JSON table, refresh mode |
| (b) Salt | about 600 | Weak | Constant, search tool |
| (c) 8 shards, current hash | about 610 | No | None |
| (c) 8 shards, with (a) | about 395 | Yes, with refresh | Table, 4 more runners |
| (d) Parallel Postgres | about 530, a guess | Partly | Shared server, 92 edits |

## Recommendation

Choose (a). It removes the cause: the hash does not know class cost. It adds one file and one refresh command. Its result is close to the 4 shard floor of about 580 s.

Seed the table from one full TRX run, not from the partial list in `survey/test-pruning.md`. That list covers 34 classes. The model above fails to rank shards 0, 1, and 2.

Treat (d) as a separate decision. It needs owner approval and a flake check. Combined with (a), it could bring the longest job to about 410 s.

Skip (b) and (c). Option (b) needs the same data as (a) and gives a worse result over time. Option (c) costs runner time and does not fix the cause.

## Not run

- No CI run. No timing of steps 4 to 6 of the test job.
- No full per-class timing. Only 34 classes carry seconds in the source survey.
- No test of parallel Postgres classes.
