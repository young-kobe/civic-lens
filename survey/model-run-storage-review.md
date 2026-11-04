# Review: model run storage survey

Reviewed `survey/model-run-storage.md` against `origin/main` at `de620566`. Nothing was built or run. Facts come from `git grep` and file reads.

## 1. Claims that are wrong or imprecise

Most cited paths, columns and line numbers check out. The list below holds only what does not.

| Claim | Truth | File:line |
|---|---|---|
| `quotes_json` has one reader, `StatementGateQueueMapping.cs:56-57`, and is "gate output, not model input" (D8). | The row mapper is not the consumer. `StatementGateCarryOver.From` reads `gateRun.Quotes` and feeds the issue IDs to the statement draft step. It reads `output_json` too. | `StatementGateCarryOver.cs:12,15`, `PostgresStatementDraftStore.cs:53` |
| Eight `Serialize*Context` methods lose their last caller in PR 6. | Nine methods exist: `SerializeCatalog`, `ToContextJson`, `SerializeUnmatchedContext` and six `SerializeContext`. Two tests also call them, so they keep a caller until the tests change. | `ActionDraftPromptGoldenTests.cs:66`, `PostgresDocumentChangeAnalysisStoreTests.cs:115` |
| "Eight step lines" set `ContextJson`. | Nine lines in eight files. `RelationshipDraftStep` sets it twice. | `RelationshipDraftStep.cs:27,31` |
| Section 4: `AnalysisRun` swaps `ContextJson` for `Input`. | PR 2 cannot swap. `context_json` stays live until PR 6: relationship staleness reads it, and the Interrupted rule needs it. Section 4 and the PR order disagree. | `AnalysisRun.cs:8,34`, `RelationshipStaleness.cs:58,65` |
| Section 4: validation errors need "one extra read by `run_id` in `ToRun`". | `ToRun` is a static, synchronous mapper with no database access. It has nine callers. The read must move to the callers, or use an EF navigation. | `AnalysisRunRows.cs:35-46` |
| Section 3.2: `AnalysisRun.Validate` enforces the new input rule, and old rows "still pass". | `ToRun` calls `Validate()` on every read. A legacy call run with null `Input` would throw. See PR 2 below. | `AnalysisRunRows.cs:44` |
| Gap 4: output is dropped for `refusal` and `max_tokens`. | True, but incomplete. The code drops text for every stop that is not `end_turn`, so `UnexpectedStop` runs lose output too. | `AnthropicStructuredCall.cs:54-56` |
| `DocumentChangeDraftPrompt.cs:19` holds a system "const". | It is `static readonly string`, built by interpolation. Nothing changes in the plan. | `DocumentChangeDraftPrompt.cs:19` |
| Section 7: 26,555 runs, 146 MB of user messages, 1,454 MB database. | No file in the repo states these numbers. They came from outside the repo, so this review cannot confirm them. | none |
| Section 3.1 hash: SHA-256 over version tag, role and text. D2 check: `sha256(role \|\| E'\n' \|\| text)`. | The two do not match. With a tag in the C# hash and none in the check, every insert fails the check. The survey notes the option but leaves 3.1 as written. | `survey/model-run-storage.md:127,241` |
| PR 3 backfill uses `jsonb_array_elements ... WITH ORDINALITY`. | `validation_json` is `text`, and `message` is `varchar`. It needs `jsonb_array_elements_text(validation_json::jsonb)`. | `AnalysisTableConfiguration.cs:84` |

Claims checked and correct:

- `Attempt.Run` builds every call run. Callers are `ResponseRun`, `FailUnexpectedAsync` and `FailCallAsync`. `request` is set and cleared together with `RunId` (`AnalysisWorker.cs:171,179,213,232,260-266`).
- `BypassRun` and `Interrupt` build their runs directly and hold no request (`AnalysisWorker.cs:182-187`, `PostgresAnalysisQueue.cs:292-304`).
- Relationship staleness reads `context_json::jsonb ->> 'fingerprint'` (`RelationshipStaleness.cs:58,65`). Unmatched pairs hold no fingerprint, so the backfill leaves null.
- `Effort.Medium` is hard coded (`AnthropicStructuredCall.cs:43`). All eight task `Effort` consts are `"medium"` and feed only the hash.
- `statement_analysis_runs.quotes_json` exists and is written and read at `StatementGateQueueMapping.cs:52,56-57`.
- Output is dropped on refusal and `max_tokens`. Interrupted and bypassed runs carry no request.
- The read-only hidden pattern hides none of `output_json`, `validation_json`, `context_json` (`readonly-grants.sql`, `hidden_column`).

## 2. Readers the survey misses

| Column | Missed reader | File:line |
|---|---|---|
| `output_json` | `StatementGateCarryOver.From` parses it on every draft. The survey lists it, but not that it is the draft's input. | `StatementGateCarryOver.cs:12` |
| `validation_json` | Test asserts the read-only pattern does not hide it. PR 5 drops the column, so the test needs an edit. | `PostgresBriefCheckStoreTests.cs:319` |
| `output_json` | Same test, same reason. | `PostgresBriefCheckStoreTests.cs:318` |
| `context_json` | Raw insert that names the column. PR 6 breaks it. The survey lists only `:511` and the seeder. | `PostgresRelationshipDraftStoreTests.cs:155` |
| `context_json` (doc) | Staleness text says the run holds the fingerprint. | `docs/architecture/statements.md:385` |
| `context_json` (doc) | "Every run keeps `context_json`". | `docs/architecture/statements.md:509` |
| `output_json` (doc) | Brief check storage text. | `docs/architecture/briefs.md:237` |
| prompt storage (doc) | "The run does not store prompt text... the input hash proves the rebuild". The new design contradicts it. | `docs/architecture/analysis.md:198-200` |
| `QuotesJson` | `StatementGateCarryOver.cs:15` reads the quotes (see section 1). | `StatementGateCarryOver.cs:15` |
| `ContextJson` / `AnalysisRun` constructor | Nine test files build `new AnalysisRun(...)` or `DraftedRun` with a positional `ContextJson`. PR 2 and PR 6 break all of them. The survey lists none of these. | `FakeAnalysisStore.cs:41`, `FakeStatementDraftStore.cs:36`, `FakeStatementGateStore.cs:42`, `StatementCodeApprovalTests.cs:191`, `StatementGateCarryOverTests.cs:55`, `PostgresBillStreamTests.ActionDrafts.cs:999`, `PostgresBillStreamTests.ActionReview.cs:684`, `PostgresHouseVoteTests.Drafting.cs:518`, `PostgresRelationshipDraftStoreTests.cs:282` |
| settled fingerprint | The helper also feeds the store read and the stale-pair query. They need no change if the helper changes, but the survey does not name them. | `PostgresRelationshipDraftStore.cs:18`, `RelationshipStaleness.cs:32-33` |

No reader exists in `tools/`, `.github/` or `deploy/` outside `readonly-grants.sql`. That script names none of the columns. `AnalysisRunLookup.Latest` callers project IDs only, so they cause no N+1 read for child rows.

Raw inserts into run tables (`GateLabelRuns.cs:12`, `RelationshipWorldSeeder.cs:92`, `PostgresBriefCheckStoreTests.cs:188`, `PostgresBillStreamTests.ActionDrafts.cs:711`, `PostgresHouseVoteTests.Drafting.cs:466`) stay valid in PR 2, because every new column is nullable.

## 3. Decisions D1 to D8

- **D1.** Facts hold. One precedent is missing. Action and vote fingerprints already live on their revisions (`ActionRevisionRows.cs:38`, `ActionDraftContext.cs:34`), so those kinds need no run column. Bypassed action and vote runs also store the fingerprint in `input_hash` (`ActionDraftStep.cs:78`, `VoteDraftStep.cs:64`). Relationship Drafted runs do not, since their `input_hash` is the prompt hash. Recommendation B stands.
- **D2.** The claim "hash-of-content checks have no precedent" is true. No `sha256(` appears in any migration or configuration. The 3.1 and D2 hash recipes conflict (section 1). Postgres runs 18.3 (`deploy/compose.yaml:30`), so `sha256()` is available.
- **D3.** Facts hold. `Effort` is a string const and the SDK takes an enum, so the request needs a mapping at `AnthropicStructuredCall.cs:43`. The survey does not say so.
- **D4.** Facts hold. `StopOutcome` already keys on `StopReason` first (`AnalysisModelResponse.cs:7-13`), so option B is safe. It also covers every non-`end_turn` stop.
- **D5.** Facts hold. The pattern is a regex on column names, so one added name hides it in every table. Existing tests that read `ReadOnlyGrants.HiddenColumn()` check per table, so a new name will not break them (`ReviewDecisionColumnsMigrationTests.cs:258-268`).
- **D6.** Facts hold. `docs/architecture/analysis.md:200` already says an Interrupted run keeps no catalog copy.
- **D7.** Facts hold. The FK cannot enforce role. The survey says so.
- **D8.** The reason given is wrong. `quotes_json` feeds the next task through `StatementGateCarryOver` (section 1). It is still a defensible deferral, but the draft step reads it.

## 4. PR order

- **PR 1 breaks a repo rule.** `AGENTS.md` says every added member needs a production caller before commit. PR 1 adds `AnalysisPromptTexts` and the table with only test callers. Fold it into PR 2.
- **PR 2 can break main.** `Validate` requires `Input` for call runs, and `ToRun` calls `Validate` on read. Legacy rows hold null `Input` and fail to load. This hits `GetAnalysisRun`, validation resume, and `StatementGateCarryOver`. Split write rules from read rules, or require `Input` only on write.
- **PR 2 and PR 6 conflict.** Section 4 swaps `ContextJson` out in PR 2. PR 6 removes it. PR 2 must add `Input` beside `ContextJson`.
- **PR 3 depends on a ToRun change.** A rejected run must load its errors, or `Validate` throws ("at least one" error). Every `ToRun` caller needs the child read.
- **PR 4 is fine.** It needs only the PR 2 columns and PR 3 rows.
- **PR 5 and PR 6 are fine.** Each follows its backfill or verify step. PR 6 drops `context_json` with no verify step. R-63 asks for a check before a drop, and the survey accepts the loss in D1.
- **PR 7 as listed is fine.** The survey itself recommends folding docs into each PR.

## 5. SQL and EF checks

- Check names `ck_{table}_input`, `_input_outcome`, `_user_message`, `_max_output_tokens` collide with no name in `docs/architecture/schema/*.md`. The longest, `ck_document_change_analysis_runs_input_outcome`, has 46 characters. That fits the 63 limit.
- EF already shortens long foreign key names (`InitialSchema.cs:1716`), so the new foreign keys follow that precedent.
- `INSERT ... SELECT ... FROM unnest(...) ORDER BY ... ON CONFLICT DO NOTHING` is valid Postgres.
- The `ck_{table}_input_outcome` logic is correct for null and non-null cases.
- The error tables' lengths, PK and FK pattern match `brief_check_flags` (`schema/briefs.md:54-62`). `char_length` counts code points, which are never more than the C# UTF-16 length, so no existing message fails.
- `AnalysisRunInput` in Core is allowed. Core already uses `SHA256` (`BriefWeek.cs`, `DocumentComparison.cs`).

## Verdict

Mostly accurate on facts, but PR 1 has no caller and PR 2 would fail to read legacy rows, so fix those two before any code.

## Resolution

The survey now applies every finding above, checked on `origin/main` at `c3c116b9` with `git grep` and file reads. PR 1 is folded into PR 2. The plan now has five PRs. These claims were rejected:

- `PostgresBriefCheckStoreTests.cs:318` (`output_json`) needs no edit. Option B keeps `output_json`. Only line 319 (`validation_json`) changes, in the PR that drops that column.
- `briefs.md:237` needs no edit. It names only `output_json`, which stays.
- The production figures (run count, user message size, database size) are not in the repo. The survey no longer states them. It tells the owner to measure after PR 1.
- The review says PR 5 and PR 6 are fine. PR 5 (old PR 6) has no verify step. The survey states that cost in D1 and leaves the choice to the owner.

## Round 2 resolution

Each item was checked on `origin/main` with `git show` and `git grep`. All were applied. None was rejected.

- P1 (relationship fingerprint has no writer): applied in section 3.4, PR 5 and D1 option B. `RelationshipDraftRun` holds only `(Run, Reason)` and `AddRun` sets only `Reason` (`RelationshipDraftQueueMapping.cs:31-36`), as the review said. The plan now adds the field, sets it in `RelationshipDraftStep`, writes it in `AddRun`, adds an all-or-none check, and moves the `IS NOT NULL` filter at `RelationshipStaleness.cs:65`. The shared `Wrap` also builds rejected runs, so the plan sets the fingerprint at the accepted, recite and bypass sites instead. A new test shows that a settled pair is not stale.
- P2a (two hash owners): applied. `AnalysisRunInput.TextHash(role, text)` in Core is the one recipe. `IdOf` is gone from the plan. `EnsureAsync` reads the `Input` hashes.
- P2b (four helpers): applied. Only `PostgresBillStreamTests.ActionDrafts.cs:999` and `PostgresHouseVoteTests.Drafting.cs:518` pass an `Input`. The other two build Bypassed runs and must not (`ActionReview.cs:684-686`, `PostgresRelationshipDraftStoreTests.cs:282-284`).
- P2c (write rule owner): applied. `CheckpointAsync` calls `Validate()` at `PostgresAnalysisQueue.cs:164` and `Interrupt` at `:301`. Both change to `ValidateForWrite()`, and that is the owner. `AnalysisRunRows.Create` gets no call.
- P2d (CLR shape): added as D9, with options A, B and C. `OwnsMany` and `OwnsOne` have no use in `src`, and neither has `Include`. The recommendation is C, after a spike on restrict deletes. Section 3.3, section 4 and PR 2 refer to D9.
- P3 count: there are ten queries, not nine. `PostgresStatementDraftStore.cs:47-49` reads the latest gate run by candidate. Fixed in section 1.2, section 4 and PR 2.
- P3 `IAttempt`: the file is `IAnalysisAttempt.cs:8`. PR 5 now names it.
- P3 D5 option C: names the token `user_message` exactly, and cites `PostgresBriefCheckStoreTests.cs:316`.
- P3 text cap: proposed 100 000 as one constant. All eight tasks set `MaximumPromptBytes = 100_000`. It stays open for the owner.
- P3 ORDER BY: kept. The text now says that settles of one kind already lock the budget day row (`PostgresAnalysisQueue.cs:238`), so the order adds little. It still guards settles of different kinds, which use different budget tables.

## Round 3 resolution

Each item was checked on `origin/main` with `git show` and `git grep`. All were applied. None was rejected.

- P1a (DraftDiscarded keeps the fingerprint): confirmed at `RelationshipDraftQueueMapping.cs:50-51`. The check now allows DraftDiscarded. Clearing the fingerprint adds code and loses data. Staleness filters on outcome (`RelationshipStaleness.cs:64`).
- P1b (backfill filter): confirmed at `AnalysisWorker.cs:187,266`. The backfill UPDATE has `WHERE outcome IN ('Drafted','DraftDiscarded','Bypassed')`. PR 5 also counts null results for Drafted and DraftDiscarded rows before the check.
- P2a (D9 precedent): confirmed. Navigations with Restrict exist in four row configurations. The repo has no `Include(`, so the explicit load is the one new piece. D9 now recommends A.
- P2b (owned types): kept as a likely limit, with the Restrict rule cited at `actions.md:42,62` and `collection.md:369`. A spike must prove it.
- P2c (constructor sites): confirmed. Section 3.4 lists all seven sites. `SettleWithoutCallAsync` gets a nullable fingerprint parameter, and the unmatched path passes null.
- P3a (check SQL): section 3.4 states it. The test text matches it.
- P3b (`"old"`): PR 5 says to write a 64-character hex value at `PostgresRelationshipDraftStoreTests.cs:511`.
- P3c (prompt cap owner): the eight tasks use the one Core constant. A test that ties two values would keep two owners.

## Owner decisions folded in

The owner chose D1 C (extended), D2 B, D3 B, D4 B, D5 C, D6 A, D7 two tables and D8 B. D9 stays open. The survey records each in section 6 and updates sections 3 to 5. The plan now has eight PRs. New facts checked on `origin/main`:

- Brief draft sent records carry `id`, `kind`, `recordId` and `revisionNumber` (`BriefDraftPrompt.cs:41-56`). Brief check carries `citations` with IDs and `notCited` without (`BriefCheckPrompt.cs:52-59`). Relationship context carries `statement` and `target` revisions (`RelationshipDraftPrompt.cs:49-61`).
- The relationship target (Action or Vote) and the brief record kinds (DocumentChange, Statement, Action) name several revision tables, so one FK cannot hold them. This is the new sub-choice D1a, with a recommendation.
- `RelationshipDraftStep.Wrap` now needs the context, because rejected runs also carry the pair revisions.
- The role join in verify is gone, because two tables and plain FKs hold the role.

## Round 4 resolution

Each item was checked on `origin/main` (`8f47562c`) with `git show` and `git grep`. The brief named `actions.md:43` for the R-63 rollback rule. The rule is at `actions.md:60`. The survey cites line 60.

- P1-1 (brief task names): applied. The real names are `brief-draft` and `brief-check` (`BriefDraftTask.cs:7`, `BriefCheckTask.cs:7`). The same check found three more wrong names in line 35: the statement draft task is `statement-draft`, and `action_analysis_runs` holds only `action-draft`. The issue choice call shares that task name and differs by prompt version (`ActionIssuePrompt.cs`). The backfill SQL and line 35 are fixed. The migration now counts brief runs whose record rows differ from their context lists, and raises an exception if the count is not zero.
- P1-2 (`Wrap` cannot reach failure runs): confirmed at `AnalysisWorker.cs:213,232`. `BriefDraftStep.Wrap` and `BriefCheckStep.Wrap` have the same shape. Written as D10. The owner decided (a) on 2026-11-04: the attempt carries typed run references, as `ContextJson` does. The `Wrap` closure item and the "depends on D10" marks are gone.
- P1-3 (D9 precedent): confirmed. `VoteRevisionRows.ReadPageAsync` (`:63-81`), `ExecutiveActionRevisionRows.cs:44-53` and `ReviewQueries.RevisionChildren` (`:57-58`) read child rows in a keyed query with `FromSqlRaw`, `unnest` and `ToLookup`. The earlier claim that the explicit load is new was wrong. D9 option A now copies that precedent: one shared reader in `AnalysisRunRows`. The CLR shape is six error row classes with a shared `IAnalysisRunErrorRow`, and `IAnalysisRunRow<TError>` for the typed write collection. This matches the vote and executive precedent, where each child table has its own row class. `ToRun` takes the messages as an argument, so the compiler finds a missed load. The owner decided A on 2026-11-04.
- P2-1 (D1a precedent): applied. Option (ii) follows `relationship_analysis.target_kind` and `target_record_id` (no FK, `RelationshipAnalysisRowConfiguration.cs:16,19-20`), `publication_withdrawals` (`PublicationWithdrawalRowConfiguration.cs:18-20`) and `PublishedRevisionBinding`. Option (i) follows `actions.md:62` and the `num_nulls` check at `ActionRevisionRowConfiguration.cs:36`. The owner decided (i) on 2026-11-04.
- P2-2 (fingerprint check breaks rollback): confirmed against `actions.md:60`. The PR 3 database check now checks the format only. `RelationshipDraftQueueMapping.AddRun` requires the fingerprint in C# for Drafted and DraftDiscarded runs, as `PrepareSettlementAsync` throws for a bad settlement. A test is listed. PR 7 adds the strict form after verify. `votes.md:409` plans this, but no drop step has shipped and the repo has no `SET NOT NULL` migration, so the survey calls it a plan.
- P2-3 (PR 7 Down): applied. Old `Validate` needs a context on every run that is not Interrupted (`AnalysisRun.cs:34`), and old `ToRun` validates each read (`AnalysisRunRows.cs:44`). The Down writes `'{}'` where it cannot rebuild, with a test. PR 6, 7 and 8 get an `operations.md` rollback paragraph, as for `DropDecisionJson`.
- P2-4 (test sites): added `PostgresDocumentChangeAnalysisStoreTests.cs:496` to PR 1, and `AnalysisRunTests.cs:38` and `PostgresHouseVoteTests.Drafting.cs:523` to PR 7. The re-grep found two more: `PostgresBillStreamTests.ActionDrafts.cs:793` and `PostgresBillStreamTests.ActionReview.cs:426` derive a Bypassed run from the Drafted helper with `with`. They copy `Input`, so they must set `Input = null` in PR 1. It also found one production site, `PostgresAnalysisQueue.cs:297-300`, which PR 7 now lists.
- P2-5 (staleness move): PR 3 everywhere. The reason list at the `AnalysisRun` paragraph no longer names staleness. The `statements.md:385` and `:581` edits moved to PR 3.
- P2-6 (verify owner): new `AnalysisRunVerifier` in `Infrastructure/Analysis`, because `RevisionBackfill.VerifyAsync` is keyed by `(record_id, revision_number)`. The backfills run as SQL in the migrations (precedent `ReviewDecisionColumns.cs:552-575`). A run backfill is a field copy with no domain type and no hash recompute. Each drop migration repeats its check in SQL, as `DropDecisionJson.CheckSql` does.
- P3: the quotes backfill uses the PascalCase keys. `statement_gate_quotes` is now `statement_analysis_run_quotes`. Line 3 states the decisions. The input check uses `num_nulls(...) IN (0, 5)`. The count of query kinds is fixed: eight by run ID, one by `draft_id`, one by candidate. The note on cited rows of a check run is added.

Owner decisions of 2026-11-04: D1a (i), D9 A, D10 (a). No owner decision stays open.

## Round 5 resolution

Each item was checked on `origin/main` (`efd90376`) with `git show` and `git grep`. All were applied. Two were adjusted, and the reasons follow.

- P1 (rerunnable backfill): confirmed. `AnalysisRun.Validate` throws for a rejected run with no errors (`AnalysisRun.cs:31-33`). `StatementGateCarryOver` reads `gateRun.Quotes` (`:15`). `RelationshipStaleness` filters on the context fingerprint (`:65`). The survey records the owner choice (b) as D11 and adds a section 5 design. Model runs do not plug into `RevisionBackfill`. That engine needs `IReviewRevisionRow`, a `RevisionKey(RecordId, RevisionNumber)` cursor, a review lock, and a C# rebuild that it compares to the JSON. Run tables key by `run_id`. They get their own command, `analysis runs backfill`, with the same shape. Precedents checked: `votes revisions backfill`, `executive-actions revisions backfill`, `bills subjects backfill`. PR 2, 3, 4, 5, 6, 7 and 8, the verify order, the operations notes and the rollback text are updated. PR 2 to 4 need no EF Down.
- P2-1 (one owner for run references): applied. Core gets `AnalysisRunReference` and `AnalysisRun.References`. `AddRun` reads `run.Run.References`. `RelationshipDraftRun` keeps only `InputFingerprint`. The brief run records gain no record field, which removes the second path that section 3.4 had. PR 3 lists a test for worker failure runs.
- P2-2 (verify outcome filter): applied in PR 5. The fingerprint comparison covers only Drafted, DraftDiscarded and Bypassed.
- P2-3 (Bypassed `with` sites): applied in PR 1. The lines 497, 523 and 569 are correct at `de620566`, which the survey cites. On `origin/main` they sit at 507, 533 and 579. The survey states both. Line 426 was already listed.
- P2-4 (prompt hash in C#): applied in PR 5. `AnalysisPromptHash.Compute` serializes with `UnsafeRelaxedJsonEscaping` (`AnalysisPromptText.cs:13,18`). SQL cannot reproduce it.
- P3-1 (brief kind casing): confirmed. The draft context writes `Kind.ToString()` (PascalCase). The check context writes `KindOf` with `JsonNamingPolicy.CamelCase` (`BriefCheckPrompt.cs:124`). The survey names both and says each `CASE` matches its own writer.
- P3-2 (nine `Build` sites, one effort constant): confirmed. Nine `Build` methods exist, eight kinds plus `ActionIssuePrompt`. The survey lists them. It adds `AnalysisEffort.Medium` and deletes the eight task consts.
- P3-3 (PR 6 Down): applied. The Down uses `jsonb_agg(e.message ORDER BY e.error_number)`.
- P3-4 (NUL): adjusted. `ValidateForWrite` refuses NUL in the three input texts and in each error. A pre-check for a raw NUL in existing rows finds nothing, because Postgres refuses NUL in `text`. The real risk is the JSON escape `\u0000` in the old JSON columns, because `::jsonb` refuses it. The command checks for that escape, skips those runs with the reason `NulEscape`, and reports their IDs.
- P3-5 (missing `ExtractionId`): applied in PR 4. The command skips and reports such runs with `MissingExtraction`, so the FK never aborts a batch. A test is listed.
- P3-6 (task list): applied. Line 35 now names `vote-draft` and `relationship-draft`.
- Side effects of D11: the migrations of PR 2 to 4 add schema only. The drop PRs delete the backfill step that reads the dropped column. The last drop deletes the command. A short read window between `update.sh` and the first command run is noted in section 5.

## Round 6 findings

Each item was checked on `origin/main` at `14586748` with `git show`, `git grep` and file reads. Lines cite that commit. The analysis code is unchanged since `de620566`. Test files and some docs moved. Lines in `survey/model-run-storage.md` cite the survey before this fix, at `0e91a8e3`.

### P1. The deploy fill runs after the new worker starts

- File: `survey/model-run-storage.md:287` (section 5, Operations), against `deploy/scripts/update.sh:37-40`.
- Defect: the runbook runs `analysis runs backfill` after `update.sh`. But `update.sh` stops the worker and the review host (`:37`), migrates (`:38`), writes the tag (`:39`) and starts every service (`:40`). The new worker reads legacy runs before their child rows exist.
- Failure scenario, PR 2: an entry stopped mid-retry keeps `retry_of_run_id`. The new worker reads the legacy rejected run (`AnalysisValidationLoop.cs:67-71`). `ToRun` gets no error rows, and `Validate` throws `ArgumentException` (`AnalysisRun.cs:31-33`). `RunId` is still null, so `FailUnexpectedAsync` moves the entry to Blocked (`AnalysisWorker.cs:215`). The statement gate, statement draft and document change lifecycles pass no Blocked requeue rule (`StatementGateLifecycle.cs:7-8`, `StatementDraftLifecycle.cs:7`, `DocumentChangeDraftLifecycle.cs:5`, `AnalysisLifecycle.cs:16`). The entry never returns to Pending.
- Failure scenario, PR 4: a pending statement draft reads a legacy gate run with no quote rows. `StatementGateCarryOver.From` returns empty issues and raises no error (`StatementGateCarryOver.cs:15`).
- Failure scenario, PR 3: every legacy settled pair has a null `input_fingerprint`. `RelationshipStaleness` finds no settled run (`RelationshipStaleness.cs:62-66`), so each pair is stale (`:32`) and drafts again at a cost.
- Fix: owner decision D12. `update.sh` runs the fill and then verify after `db migrate`, while the worker and the review host are stopped.

### P2-1. The fill has no end condition that `update.sh` can use

- File: `survey/model-run-storage.md:241,281,287`.
- Defect: the command takes a `<limit>`, and the runbook says to run it "until `examined` is 0". The survey never defines `examined`. The brief record step selects runs by `NOT EXISTS` on child rows. A brief draft that settles with `NoNewRecords` has a context with `records: []` (`BriefDraftStep.cs:26,29-30`, `BriefDraftPrompt.cs:48`). That run never gets a row, so every pass selects it again.
- Failure scenario: under D12, `update.sh` calls the command once. A limit below the legacy row count leaves rows unfilled, and verify then stops the deploy. The listed command caps a count at four digits (`operator-command.sh:18`). A loop "until `examined` is 0" never ends, because the empty brief runs come back on each pass.
- Fix: the command takes no limit. It walks each run table once to its end, in batches of one short transaction each. The brief step selects only runs whose list is not empty (`jsonb_array_length(...) > 0`). The command prints `filled` and `skipped` counts per step.

### P2-2. The drop PRs leave verify conditions on dropped columns

- File: `survey/model-run-storage.md:342-344` (PR 5), `:350` (PR 6), `:358` (PR 7), `:372` (PR 8).
- Defect: PR 6, 7 and 8 delete the backfill step that reads the dropped column. They do not delete the verify condition that reads the same column.
- Failure scenario: PR 6 drops `validation_json`. Verify still compares error rows with `validation_json` and fails with `column "validation_json" does not exist`. Under D12, `update.sh` runs verify on each deploy, so every deploy after PR 6 stops before `compose up`.
- Fix: PR 6 deletes the error condition. PR 7 deletes the relationship and brief record conditions. PR 8 deletes the quote condition. Each PR adds a test that verify runs on the new schema.

### P2-3. Verify does not prove that the fill is complete

- File: `survey/model-run-storage.md:217,342-344`.
- Defect: verify compares rebuilt rows with the JSON, but the survey states no null rule. A legacy rejected run with no error rows rebuilds to SQL NULL. `NULL <> validation_json` is NULL, so a plain comparison counts no mismatch. The same holds for a null `input_fingerprint`, null revision columns and absent quote or record rows. The count of Drafted and DraftDiscarded runs with a null fingerprint (`:217`) sits in the fill, not in verify. PR 7 adds the strict check "after verify", so verify must prove that count is zero.
- Failure scenario: the fill skips a batch or a step has a wrong filter. Verify exits 0. Under D12, `compose up` starts the new worker, and the P1 failures return.
- Fix: every verify condition compares with `IS DISTINCT FROM`, so an absent value is a mismatch. Verify also counts Drafted and DraftDiscarded relationship runs with a null fingerprint. A test leaves one legacy rejected run unfilled and asserts exit code 1.

### P2-4. The brief check fill skips the not-cited records

- File: `survey/model-run-storage.md:241` (section 3.4, brief backfill).
- Defect: for `brief-check` runs, "the same shape runs over `citations` and then over `notCited`". Each statement carries `NOT EXISTS` on the run's record rows. The first statement inserts the cited rows. The second statement then sees them and selects nothing.
- Failure scenario: a check run with two citations and one not-cited record gets two rows. The pre-check counts three in the context and two rows, so the command exits 1 for every such run. Under D12, every deploy that crosses PR 3 stops.
- Fix: one `INSERT` reads both lists through `UNION ALL`, under one `NOT EXISTS`. A test fills a check run with both kinds of record in one pass.

### P3-1. Production and doc citations drifted on `origin/main`

- File: `survey/model-run-storage.md:64,67,198,210,245,262,379,410`.
- Defect and fix, old to new: `ActionDraftQueueMapping.cs:33-35` to `:34-38`. `PostgresActionDraftStore.cs:44` to `:47`. `ActionRevisionRows.cs:38` to `:41`. `actions.md:60` to `:61`. `actions.md:62` to `:63`. `VoteRevisionRows.cs:63-81` to `:65-81`. `ExecutiveActionRevisionRows.cs:44-53` to `:46-62`. `ActionRevisionRowConfiguration.cs:74-75` to `:77-78`. `collection.md:369` (now blank) to `:372`.
- Failure scenario: an implementer opens `collection.md:369` and finds a blank line, or opens `actions.md:62` and finds the bill page citation rule.

### P3-2. PR 1 test list misses a Bypassed site and cites moved lines

- File: `survey/model-run-storage.md:310-311,366,369`.
- Defect: `CodeRun` (`PostgresBillStreamTests.ActionDrafts.cs:1054-1058`) derives a Bypassed run from `DraftedRun` with `with`. It copies `Input`, so PR 1 must set `Input = null` there too. The survey lists five such sites, and there are six. Lines also moved: `ActionDrafts.cs:999` to `:1042`, `:793` to `:836`, `:711` to `:695`, `:484,503,505` to `:468,487,489`, and `ActionReview.cs:684-686` to `:694-696`.
- Failure scenario: after PR 1, `ValidateForWrite()` refuses the `CodeRun` run, and the test that settles it at `:754` fails.
- Fix: add `CodeRun` to the PR 1 list and cite the lines on `14586748`.

### P3-3. The relationship target check accepts a mixed pair

- File: `survey/model-run-storage.md:206,248`.
- Defect: the check is `num_nulls(<four target columns>) IN (2, 4)`. Two nulls also match `target_action_record_id` with `target_vote_revision_number`. A composite foreign key with a null column is not checked (Postgres `MATCH SIMPLE`), so neither FK refuses that row.
- Failure scenario: a writer bug sets the action record ID and the vote revision number. The insert passes, and the run names no real revision.
- Fix: each group is all set or all null: `num_nulls(target_action_record_id, target_action_revision_number) IN (0, 2)`, the same for the vote group, and `num_nulls` of all four `IN (2, 4)`. The statement group and each brief record group use the same per-group rule.

### P3-4. PR 8 leaves members with no production caller

- File: `survey/model-run-storage.md:372` (PR 8), `:340-347` (PR 5).
- Defect: PR 8 says "delete the command". It does not name the handler `BackfillAnalysisRuns`, the port `IAnalysisRunBackfillStore`, the store `AnalysisRunBackfill`, the `operator-command.sh` entry, the listed command in `operations.md:1235` or the runbook section. PR 5 adds verify but never adds it to `operator-command.sh` or `operations.md`.
- Failure scenario: after PR 8 the port and the store keep only test callers. An operator cannot run verify through `civic-lens-ops`.
- Fix: PR 5 adds `analysis runs verify` to `operator-command.sh` and `operations.md:1235`. PR 8 deletes the handler, the port, the store, its tests, the `operator-command.sh` entry, the listed command and the runbook section.

### Claims checked and rejected

- None of the P1 text was rejected. The main session cited `AnalysisWorker.cs` "about line 215". The Blocked branch is exactly line 215.
- The survey holds no production row counts. Round 1 removed figures that came from outside the repo. D12 states the deploy cost without them.

### Resolution

Every finding above is applied in `survey/model-run-storage.md`. D12 is recorded in section 6, and PR 2, 5, 6, 7 and 8 carry its steps.
