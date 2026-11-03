# Bill stream test failure at de620566

## Verdict

Test defect. The fixture picks its extraction at random and can pick an orphan row. The product code is correct.

## Failure

`PostgresBillStreamTests.TheExactTierLowersAnEarlyTierOneWhenNoStatementOfASponsorPairs` threw "Stored extraction has no captured source attempt."
The throw comes from `PostgresDocumentExtractionStore.ReadManyAsync`, lines 136 to 138.
The method finds no captured attempt for an extraction row, so it fails by design.

## How an extraction exists without an attempt

1. `RelationshipWorldSeeder.StatementAsync` (`tests/CivicLens.Tests/Fixtures/RelationshipWorldSeeder.cs:16-30`) turns off foreign keys with `session_replication_role = replica`.
2. It inserts a `document_extractions` row with id `RelationshipFixtures.ExtractionId` (64 times `c`) and `attempt_id = 'attempt'`.
3. No `collection_attempts` row named `attempt` exists. The row is an orphan.
4. The test calls `StatementNamingBillSevenAsync("markey")` first (`PostgresBillStreamTests.ActionPriority.cs:65`). That inserts the orphan.
5. `SettleAsync` (`PostgresBillStreamTests.ActionDrafts.cs:964`) calls `CitingStoredTextAsync` (lines 979 to 990).
6. That method runs `SELECT min(extraction_id) FROM document_extractions` (line 981) and rewrites every citation to the result.
7. `CheckpointAsync` then validates the approval. `ReadManyAsync` reads the cited extraction and finds no attempt.

## Why it is rare

The real extraction ids are SHA-256 hashes of a random attempt id.
`ExtractionAsync(jobId)` uses `Guid.NewGuid()` for text (`PostgresBillStreamTests.cs:1224`). The test fixtures also use `Guid.NewGuid()` for attempt ids.
This test creates two real extractions: the bill detail and the actions page.
The orphan wins `min()` only when both real ids sort above `cccc...c`. That needs a first hex digit of d, e, or f.
The chance per extraction is about 3/16. For two extractions it is about 3.5% per run.
The same risk applies to `TheExactTierCountsAPairWithAStatementThatNoPersonHasApprovedYet` (line 75). It seeds its statement before `SettleAsync` too.
Without the seeded statement, `min()` always returns a real row. That is why other tests do not fail.

## Runs

| Run | Result |
| --- | --- |
| Build | 0 Error(s), 0 Warning(s) |
| `PostgresBillStreamTests`, runs 1 to 5 | 297 passed, 0 failed each run |
| Shard 3 (`--shards 4 --index 3`, 82 classes) | 1826 passed, 1 failed, 1 skipped |

Not reproduced in five runs. This matches the 3.5% estimate (about 84% chance of five clean runs).
Shard 3 contains `PostgresBillStreamTests` and it passed there.

The shard failure is a different test: `ReviewWorkspaceTests.AFailedServeKeepsTheSavedDecisionAndAPostFromAFreshPageServesItWithoutASecondOneAsync`.
It reported "Expected: Conflict, Actual: Found". It failed again when I ran the class alone (86 passed, 1 failed).
It is not related to the bill stream failure. I did not diagnose it. It may depend on this environment.

## Proposed fix

File: `tests/CivicLens.Tests/Infrastructure/Collection/BillStream/PostgresBillStreamTests.ActionDrafts.cs`, method `CitingStoredTextAsync` (line 981).
Pick only an extraction that has a captured attempt. For example:

```sql
SELECT min(e.extraction_id) FROM document_extractions e
JOIN collection_attempts a ON a.attempt_id = e.attempt_id
```

This is the smallest change and keeps the seeder as it is.
An alternative is to give the seeder a distinct attempt id and a real attempt row. That costs more and touches other suites.
No production change is needed.

Apply no fix yet. This file only diagnoses.
