# Survey: model run storage (R-63, option B)

Owner decisions D1 to D8 are recorded in section 6. D1a, D9, D10 and D11 were decided on 2026-11-04. No owner decision stays open.

Read-only survey of `origin/main` at `de620566`. Nothing here is implemented. Line numbers cite that commit.
The review in `survey/model-run-storage-review.md` found no change of these facts on `c3c116b9`. Every review fix was checked there with `git grep`.
Scope: six `*_analysis_runs` tables, the shared run writer, and the eight model tasks that fill them.

## 1. Run tables and JSON columns

### 1.1 Shared columns

`AnalysisTableConfiguration.ConfigureRun` (`src/CivicLens.Infrastructure/Analysis/Persistence/AnalysisTableConfiguration.cs:53-90`) gives every run table the same columns.

- Identity: `run_id` (PK, 32 hex), a subject key column (64), `task`, `task_version`, `model`, `prompt_version`, `schema_version`, `input_hash` (64 hex), `previous_run_id` (self FK).
- Result: `outcome` (string enum), `stop_reason`, `provider_request_id`.
- Usage: `input_tokens`, `output_tokens`, `cache_read_tokens`, `cache_write_tokens`, `charged_tokens`.
- Time: `started_at_utc_ticks`, `finished_at_utc_ticks`.
- JSON text: `output_json` (:83), `validation_json` (:84), `context_json` (:85). All three are nullable `text`.
- Checks: `ck_{table}_tokens` (:58), `ck_{table}_times` (:61), plus per-kind `ids`, `reason`, `outcome` checks.
- Index: `(subject key, task, started_at_utc_ticks)` (:89).

The row interface is `IAnalysisRunRow.cs:24-26`. Each kind repeats the three properties on its row class.

| Table | Subject key | Extra columns | Config |
|---|---|---|---|
| `document_change_analysis_runs` | `comparison_id` | `draft_id`, `revision_number` (FK to `document_change_revisions`, unique) | `DocumentChangeAnalysisRunRowConfiguration.cs:10-26` |
| `statement_analysis_runs` | `candidate_id` | `issue_revision_id`, `reason`, `quotes_json` | `StatementAnalysisRunRowConfiguration.cs:11-26` |
| `action_analysis_runs` | `candidate_id` | `reason` | `ActionAnalysisRunRowConfiguration.cs:12-24` |
| `vote_analysis_runs` | `candidate_id` | `reason` | `VoteAnalysisRunRowConfiguration.cs:12-24` |
| `relationship_analysis_runs` | `pair_id` | `reason` | `RelationshipAnalysisRunRowConfiguration.cs:12-24` |
| `brief_analysis_runs` | `record_id` | `reason`, `checked_revision_number`, `recommendation`, `input_fingerprint`, `source_run_id` | `BriefAnalysisRunRowConfiguration.cs:18-41` |

Row classes: `StatementAnalysisRunRow.cs:27-30`, `ActionAnalysisRunRow.cs:26-28`, `VoteAnalysisRunRow.cs:26-28`, `RelationshipAnalysisRunRow.cs:26-28`, `BriefAnalysisRunRow.cs:31-33`, `DocumentChangeAnalysisRunRow.cs:24-26`.
Task names are the `Task` constants of the task classes. `document_change_analysis_runs` holds `draft`. `statement_analysis_runs` holds `gate` (`StatementGateTask.cs:7`) and `statement-draft` (`StatementDraftTask.cs:7`). `vote_analysis_runs` holds `vote-draft` (`VoteDraftTask.cs:7`). `relationship_analysis_runs` holds `relationship-draft` (`RelationshipDraftTask.cs:7`). `action_analysis_runs` holds `action-draft` only (`ActionDraftTask.cs:7`). The text call and the issue choice call share that task name and differ by prompt version. `brief_analysis_runs` holds `brief-draft` (`BriefDraftTask.cs:7`) and `brief-check` (`BriefCheckTask.cs:7`).

`brief_check_flags` is an existing child table of `brief_analysis_runs` (pk `run_id, flag_number`). It is the closest precedent for a run child table.

### 1.2 The JSON columns

| Column | Holds | Writer | Readers |
|---|---|---|---|
| `output_json` | Concatenated text blocks of the model answer. Set only when `stop_reason` is `end_turn` and the text is non-empty (`AnthropicStructuredCall.cs:54-57`). | `AnalysisRunRows.cs:28` from `AnalysisRun.OutputJson` (`AnalysisRun.cs:7`). `AnalysisWorker.cs:179-180` passes `response.OutputJson`. | `AnalysisRunRows.cs:40` into `AnalysisRun`. `StatementGateCarryOver.From` parses it (`StatementGateCarryOver.cs:12`). That parse is the input of the statement draft step. Resolvers parse the live response, not the row. No SQL reader. |
| `validation_json` | JSON array of up to 64 strings, each up to 512 chars (`AnalysisRun.cs:10-11`). Only `CitationRejected` and `OutputRejected` runs hold it (`AnalysisRun.cs:28-33`). | `AnalysisRunRows.cs:29`. The loop sets the errors at `AnalysisValidationLoop.cs:41-44`. | `AnalysisRunRows.cs:41`. The retry reads them back at `AnalysisValidationLoop.cs:22` to rebuild the prompt after a crash. |
| `context_json` | Per-kind JSON copy of inputs. Required for every run except `Interrupted`, which must have none (`AnalysisRun.cs:34-35`). | `AnalysisRunRows.cs:30`. `AnalysisWorker.cs:160` holds it, and `:187` (bypass) and `:266` (every call run) copy it. | `AnalysisRunRows.cs:42`. One SQL reader: `RelationshipStaleness.cs:58,65` reads `context_json::jsonb ->> 'fingerprint'`. Tests only otherwise. |
| `quotes_json` | Gate quotes (`StatementGateQuote[]`). Statement runs only. | `StatementGateQueueMapping.cs:52`. | `StatementGateQueueMapping.cs:56-57`. `StatementGateCarryOver.From` reads `gateRun.Quotes` (`StatementGateCarryOver.cs:15`) for the draft step. `PostgresStatementDraftStore.cs:53` calls it. |

`quotes_json` moves to a child table of gate quotes in this series (D8, PR 4).

Raw `validation_json`, `output_json` and `context_json` have no reader in `tools/`, `.github/` or `deploy/`. `readonly-grants.sql` names none of them.

`context_json` content per kind, with the step that sets it:

- Document change: officials and issue IDs (`DocumentChangeDraftPrompt.cs:60-61`, set at `DocumentChangeDraftStep.cs:31`).
- Statement gate: `issueRevisionId` and the rubric (`StatementGateRubric.cs:33`, `StatementGateStep.cs:28`).
- Statement draft: officials, carried issue IDs, carried policy area IDs (`StatementDraftPrompt.cs:46-47`, `StatementDraftStep.cs:31`).
- Action: officials, candidate issue IDs, `issueRevisionId`, `fingerprint`, `summaryVersion` (`ActionDraftPrompt.cs:47-54`, `ActionDraftStep.cs:37`).
- Vote: officials, candidate issue IDs, `issueRevisionId`, `fingerprint` (`VoteDraftPrompt.cs:54-61`, `VoteDraftStep.cs:34`).
- Relationship: `pairId`, `fingerprint`, statement and target record IDs and revision numbers, `basis` (`RelationshipDraftPrompt.cs:49-61`, `RelationshipDraftStep.cs:31`). The unmatched case holds `{pairId, matched:false}` (`:63-64`, `RelationshipDraftStep.cs:27`).
- Brief draft: week, cutoff release, `fingerprint`, `considered`, officials, and each sent record with its citation ID (`BriefDraftPrompt.cs:41-56`, `BriefDraftStep.cs:26`).
- Brief check: revision number, citations, not-cited records, `omittedNotCited` (`BriefCheckPrompt.cs:52-59`, `BriefCheckStep.cs:27`).

Run row writers, one per kind, all through `AnalysisRunRows.Create`:
`StatementGateQueueMapping.cs:34,47-52`, `StatementDraftQueueMapping.cs:32-34`, `ActionDraftQueueMapping.cs:33-35`, `VoteDraftQueueMapping.cs:33-35`, `RelationshipDraftQueueMapping.cs:31-33`, `BriefDraftQueueMapping.cs:33-35`, `BriefCheckQueueMapping.cs:30-32`, `DocumentChangeAnalysisQueueMapping.cs:30,47-49`.
The one caller of every `AddRun` is `PostgresAnalysisQueue.SettleRunAsync` (`:225-244`, call at `:242`) and the interrupt path (`:292-304`, call at `:302`).

Run row readers: `AnalysisRunRows.ToRun` (`:35-46`). It is a static, synchronous mapper with no database access. It calls `Validate()` on every row it maps (`:44`). It has ten queries that need the child read: `PostgresStatementDraftStore.cs:38`, `PostgresStatementDraftStore.cs:53` (through the gate mapping), `PostgresStatementGateStore.cs:32` via `StatementGateQueueMapping.cs:56`, `PostgresActionDraftStore.cs:44`, `PostgresVoteDraftStore.cs:47`, `PostgresRelationshipDraftStore.cs:39`, `PostgresBriefDraftStore.cs:76`, `PostgresBriefCheckStore.cs:33`, `PostgresDocumentChangeAnalysisStore.cs:42,51`. Eight of them read one run by run ID. `PostgresDocumentChangeAnalysisStore.cs:40-42` reads by `draft_id`, which is unique. `PostgresStatementDraftStore.cs:47-49` reads the latest gate run by candidate. `GetAnalysisRun.cs:13` exposes the mapper.

Other readers of run SQL: `AnalysisRunLookup.Latest` (`AnalysisRunLookup.cs:9-17`) filters on `started_at_utc_ticks` only. Its callers project IDs and load no run rows. `deploy/scripts/readonly-grants.sql` grants the read-only role every column not matching its hidden pattern. The pattern hides no run column today.

## 2. Exact inputs and outputs in memory

Every kind builds the same record, `AnalysisModelRequest` (`AnalysisModelRequest.cs:3-5`): `Model`, `MaximumOutputTokens`, `SystemPrompt`, `UserPrompt`, `SchemaJson`, `InputHash`, `ReservedTokens`, `CacheSystemPrompt`, `PromptVersion`, `SchemaVersion`.
The record is built once per pass by one `Build` method per kind. `AnalysisPromptHash.Compute` (`AnalysisPromptHash.cs:9-20`) hashes model, effort, max tokens, system, user, schema into `InputHash`.

| Kind | Builds system / user / schema | Notes |
|---|---|---|
| Document change | `DocumentChangeDraftPrompt.cs:19` (system, a `static readonly string` built by interpolation), `:48` user, `:49` schema, `:52-55` return | No cache flag. `DocumentChangeDraftStep.cs:58` only bounds tokens. |
| Statement gate | `StatementGatePrompt.cs:35-37`, hash `:40`, return `:42-43` | System prompt includes the rubric JSON (`:46-47`). |
| Statement draft | `StatementDraftPrompt.cs:35-37`, hash `:40`, return `:42` | System varies by one or several speakers (`:27`). |
| Action text | `ActionDraftPrompt.cs:36,38`, hash `:41`, return `:43-44` | System adds issue instructions when candidates exist. |
| Action issue choice | `ActionIssuePrompt.cs:17-30` | Sets `PromptVersion` and `SchemaVersion` to the issue ones (`:29-30`). Stored in the same table. |
| Vote | `VoteDraftPrompt.cs:42-45`, hash `:48`, return `:50-51` | House or Senate system text. |
| Relationship | `RelationshipDraftPrompt.cs:39-40`, hash `:43-44`, return `:45` | |
| Brief draft | `BriefDraftPrompt.cs:31-32`, hash `:35-36`, return `:37` | |
| Brief check | `BriefCheckPrompt.cs:42-43`, hash `:46-47`, return `:48` | |

Nine `Build` methods make an `AnalysisModelRequest`. Eight are one per kind. The ninth is the action issue choice. Lines are on `origin/main` at `efd90376`:

1. `ActionDraftPrompt.Build` (`:30`)
2. `ActionIssuePrompt.Build` (`:17`)
3. `DocumentChangeDraftPrompt.Build` (`:41`)
4. `BriefCheckPrompt.Build` (`:39`)
5. `BriefDraftPrompt.Build` (`:28`)
6. `RelationshipDraftPrompt.Build` (`:36`)
7. `StatementDraftPrompt.Build` (`:29`)
8. `StatementGatePrompt.Build` (`:30`)
9. `VoteDraftPrompt.Build` (`:36`)

Each one passes its own task `Effort` const to `AnalysisPromptHash.Compute`. D3 gives them one constant (gap 2 below).

Rejection reasons are appended to the user message (`AnalysisPromptText.cs:20-26`). So each retry pass has its own user message. Each pass is its own run.

At call time one object owns all four values:

- The request is stored on the attempt at `AnalysisWorker.cs:171` (`(RunId, previousRunId, request, started) = ...`). Field at `:155`.
- It goes to the SDK through `send` at `:173`, then `AnthropicStructuredCall.SendAsync` (`:32-46`).
- The raw answer text is `AnalysisModelResponse.OutputJson` (`AnalysisModelResponse.cs:5`), built at `AnthropicStructuredCall.cs:54-57`.
- Every run built from a call goes through one method, `Attempt.Run` (`AnalysisWorker.cs:260-266`). Callers: `ResponseRun` (`:179`), `FailUnexpectedAsync` (`:213`), `FailCallAsync` (`:232`). `request` is non-null whenever `RunId` is set.

So one attach point serves all eight tasks. No kind builds a run on its own.

Gaps that matter for "exact":

1. **Wire bytes are not available.** The SDK serializes the HTTP body. The code holds the request fields, not the body. The schema is parsed and cloned (`AnthropicStructuredCall.cs:83-87`), then re-serialized by the SDK. Whitespace and key order of the schema on the wire may differ from `SchemaJson`. The stored schema text is the exact string the code built, not the exact wire bytes. All kinds are the same.
2. **Effort and max tokens are not on the request.** The SDK call hard-codes `Effort.Medium` (`AnthropicStructuredCall.cs:43`). Each task has its own `Effort` const (for example `ActionDraftTask.cs:14`) that feeds only the hash. They agree today by convention. All eight consts are the string `"medium"`, and the SDK takes an enum. So a request that carries `Effort` needs a mapping from string to enum at `:43`. The eight consts collapse into one constant, `AnalysisEffort.Medium` in `Application/Analysis`. The nine `Build` methods pass it to `AnalysisPromptHash.Compute` and to the request. The eight task consts lose their last caller and go. `MaximumOutputTokens` is on the request (`AnthropicStructuredCall.cs:38`) but not stored on the run.
3. **System prompt is sent as a text block when cached.** `CacheSystemPrompt` (`:69-71`) changes the shape, not the text. The text is the same string.
4. **Output text is dropped for every stop that is not `end_turn`.** `AnthropicStructuredCall.cs:54-56` returns null text for `refusal`, `max_tokens`, and any other stop, which becomes `UnexpectedStop`. Those runs keep no output today. A refusal message, a truncated answer, or an unexpected stop text is lost.
5. **Interrupted runs have no request.** `PostgresAnalysisQueue.Interrupt` (`:292-304`) builds the run from the reservation columns (`pending_run_id`, `pending_input_hash`, `ReserveAsync` `:150-154`). The request lived only in the dead worker. The model call may or may not have been sent.
6. **Bypassed runs have no request.** `AnalysisWorker.BypassRun` (`:182-187`) makes no call. There is no user message to store.
7. **A call that fails before a response** (`ConnectionFailed`, `RateLimited` and so on) still has the full request. `FailCallAsync` (`:221-233`) builds the run through `Run`. It would keep its input and null output.

No kind is unable to give its request. Only the wire body is unavailable, for all kinds.

## 3. Proposed schema

### 3.1 Prompt text tables (D7)

Two tables, `analysis_system_prompts` and `analysis_output_schemas`. Both have the same columns. Plain foreign keys enforce the role, so there is no `role` column.

| Column | Type | Notes |
|---|---|---|
| `text_sha256` | `varchar(64)` PK | Lowercase hex. Hash recipe below. |
| `text` | `text` not null | Verbatim string the code built. |
| `created_at_utc_ticks` | `bigint` not null | Ticks of the first writer. |

Checks, named like the repo's (`ck_{table}_{rule}`):

- `ck_{table}_id`: `text_sha256 ~ '^[0-9a-f]{64}$'`. Precedent: `ck_vote_member_lists_id`, `ck_collection_configuration_revisions_id`.
- `ck_{table}_hash` (D2 B): `text_sha256 = encode(sha256(convert_to(text, 'UTF8')), 'hex')`. The database refuses a text that does not match its key.
- `ck_{table}_text`: `char_length(text) BETWEEN 1 AND 100000`. The cap is open for the owner. Proposal: one Core constant with the value 100 000, used also by `ck_{table}_user_message`. The eight task classes set `MaximumPromptBytes = 100_000` (for example `ActionDraftTask.cs:17`). Each of them changes to `MaximumPromptBytes = <Core constant>`, so the call sites in the settings and prompt classes stay as they are. A test that only ties the values would leave two owners of the limit. One constant leaves one. Each builder rejects a prompt when system, user and schema together exceed it (`DocumentChangeDraftPrompt.cs:50-51`). A text length in chars is never above its byte count, so no valid text fails. `AnalysisRun.MaximumOutputLength` (`AnalysisRun.cs:12`, 1 000 000) is too loose for input.
- `ck_{table}_created_at_utc_ticks`: the standard tick range check every table has.

Hash recipe, one only: SHA-256 over the UTF-8 bytes of `text`, written as lowercase hex. One Core member owns it: `AnalysisRunInput.TextHash(text)`. The CHECK repeats it in SQL. The hash covers the text only. The same text in both tables gets the same key, and that is harmless, because each run column has its own foreign key.
`VoteMemberLists.IdOf` adds a version tag (`vote-member-list-v1`, `VoteMemberLists.cs:15,30-31`). These tables add no tag, so that SQL can repeat the hash. A new recipe later needs a migration.

Rule for content hashes (document it in `docs/architecture/analysis.md` in PR 1): a content hash over one row gets a CHECK. A content hash over child rows gets a verify recompute, as `vote_member_lists` does (`PostgresVoteRevisionBackfillStore.cs:68` compares `row.MemberListId` with `VoteMemberLists.IdOf(members)`). A CHECK cannot see child rows, and AGENTS.md limits triggers.

### 3.2 Run columns

Add to all six run tables through `ConfigureRun`, so one change covers every kind:

| Column | Type | Notes |
|---|---|---|
| `system_prompt_sha256` | `varchar(64)` null | FK to `analysis_system_prompts(text_sha256)`, restrict. |
| `output_schema_sha256` | `varchar(64)` null | FK to `analysis_output_schemas(text_sha256)`, restrict. |
| `user_message` | `text` null | Exact text. |
| `effort` | `varchar(16)` null | D3. |
| `max_output_tokens` | `integer` null | D3. |

`output_json` stays and keeps its name. It holds the exact answer for every stop that has text (D4).

Drop `context_json`, `validation_json` and `quotes_json` in later steps (section 5).

Checks per run table, added in `ConfigureRun`:

- `ck_{table}_input`: `num_nulls(system_prompt_sha256, output_schema_sha256, user_message, effort, max_output_tokens) IN (0, 5)`. All set or all null. Precedent: `ck_action_revisions_columns_written` (`ActionRevisionRowConfiguration.cs:36`).
- `ck_{table}_input_outcome`: `outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL`. A run without a call holds no request.
- `ck_{table}_user_message`: `user_message IS NULL OR char_length(user_message) BETWEEN 1 AND 100000` (same constant as the text tables).
- `ck_{table}_max_output_tokens`: `max_output_tokens IS NULL OR max_output_tokens > 0`.

Old runs have all nulls, so every check passes. No rebuild, as decided.
A call run written by new code must carry its input. The DB cannot tell a new run from an old one. The write rule has one owner: `AnalysisRun.ValidateForWrite()` in Core. The queue calls it at the two write sites (section 4). `AnalysisRun.Validate()` is the read rule. It does not require `Input`, because `ToRun` calls it on every legacy row. The verify command covers old data (see PR 3).

Indexes: EF adds an index per FK by default, as on `IX_document_change_analysis_runs_previous_run_id` (`schema/analysis.md`). Keep both. They answer "which runs used this prompt". Cost is small: two 64-byte keys per run. Deleting a text row is never done.
EF already shortens long foreign key names (`InitialSchema.cs:1716`). The new check names collide with no name in `docs/architecture/schema/*.md`. The longest, `ck_document_change_analysis_runs_input_outcome`, has 46 characters and fits the 63 limit.

### 3.3 Validation child rows

One child table per run table, built by one shared `ConfigureRunError` helper next to `ConfigureRun`. The CLR shape of the six tables is decided in D9. Names: `document_change_analysis_run_errors`, `statement_analysis_run_errors`, `action_...`, `vote_...`, `relationship_...`, `brief_analysis_run_errors`.

| Column | Type |
|---|---|
| `run_id` | `varchar(32)` |
| `error_number` | `smallint` |
| `message` | `varchar(512)` |

- PK `(run_id, error_number)`. FK `run_id` to its run table, restrict. Precedent: `brief_check_flags` (`schema/briefs.md:54-62`).
- `ck_{table}_number`: `error_number BETWEEN 1 AND 64`. Mirrors `AnalysisRun.MaximumValidationErrors` (`AnalysisRun.cs:10`) and `ck_brief_check_flags_number`.
- `ck_{table}_message`: `char_length(message) BETWEEN 1 AND 512`. Mirrors `AnalysisRun.MaximumValidationErrorLength` (`:11`) and `ck_brief_check_flags_message`.
- Rule "only a rejected run has errors" stays in `AnalysisRun.Validate` (`:28-33`). Postgres cannot require a child row without a deferred trigger. AGENTS prefers C# when C# can hold it. The child FK plus a composite check cannot see the parent outcome. Option: add `outcome` to the child and a composite FK `(run_id, outcome)`. Not recommended. It is more machinery than the rule is worth.
- Order of messages is the order of `error_number`. The writer numbers from 1 (as flags do, `BriefCheckQueueMapping.cs:40`).
- Index: the PK serves the one read, `WHERE run_id = @id ORDER BY error_number`.
- Read path and CLR shape: D9, section 4 and PR 2.

### 3.4 Context data to columns and rows (D1)

Decision: each run keeps a foreign key to every record revision it was given. Control values drop: `summaryVersion`, `considered` and `omittedNotCited`. Officials and issue IDs name configuration and sit in the user message, so they drop too. Action and vote fingerprints already live on their revisions (`ActionRevisionRows.cs:38`). The statement gate `issue_revision_id` already is a column (`StatementAnalysisRunRowConfiguration.cs:22`).

Because the new columns and rows can be rebuilt from `context_json`, a verify step runs before the drop (PR 5).

**Relationship runs** (`relationship_analysis_runs`). Context shape: `RelationshipDraftPrompt.cs:49-61`.

- `input_fingerprint varchar(64) null`, with the hex check. `RelationshipStaleness.cs:58,65` needs the fingerprint of a settled run once `context_json` is gone, and PR 3 moves it to the column. Precedent: `brief_analysis_runs.input_fingerprint` (`BriefAnalysisRunRowConfiguration.cs:26-28,35`).
- The revisions the pair held: `statement_record_id varchar(64)` and `statement_revision_number int`, with a restricting FK to `statement_revisions` (key `(candidate_id, revision_number)`, `StatementRevisionRowConfiguration.cs:16`). The target is an Action or a Vote. See sub-choice D1a for its columns.
- Check `ck_relationship_analysis_runs_pair`: the statement pair is all set or all null, and the target pair is set exactly when the statement pair is set. The unmatched bypass has none (`RelationshipDraftStep.cs:27`), and so does an Interrupted run. Every run built from the attempt carries the pair, failure runs included (D10).

The fingerprint check is `ck_relationship_analysis_runs_fingerprint`, built like `ck_brief_analysis_runs_fingerprint` (`BriefAnalysisRunRowConfiguration.cs:26-28`). In PR 3 it checks the format only: `input_fingerprint IS NULL OR input_fingerprint ~ '^[0-9a-f]{64}$'`.

- Rollback rule. A row that older code writes has no new columns and must stay valid (`actions.md:60`, test `PostgresBillStreamTests.ARowThatOlderCodeWritesHasNoNewColumnsAndPassesEveryCheckBecauseRollbackMustStaySafe`). After an image rollback, old code inserts a Drafted run with a null fingerprint. A check that needs the fingerprint would refuse that insert. So the database does not need it in PR 3.
- C# requires it. Owner: `RelationshipDraftQueueMapping.AddRun`, which every relationship run passes, for settle and for interrupt. It throws `ArgumentException` for a Drafted or DraftDiscarded run without a fingerprint. Precedent: `PrepareSettlementAsync` in the same class (`RelationshipDraftQueueMapping.cs:38-62`) throws `ArgumentException` for a settlement without its run. Bypassed runs may have none, because the unmatched bypass has none. DraftDiscarded must keep it, because `RelationshipDraftQueueMapping.cs:50-51` turns a Drafted run into DraftDiscarded with `run with { Run = ... }`. Staleness filters on outcome (`RelationshipStaleness.cs:64`), so a discarded run never counts as settled. Test: `AddRun` refuses a Drafted and a DraftDiscarded run without a fingerprint, and accepts a Bypassed run with or without one.
- Strict check. PR 7 replaces the format check with the strict form, after verify: `(outcome IN ('Drafted','DraftDiscarded') AND input_fingerprint ~ '^[0-9a-f]{64}$') OR (outcome = 'Bypassed' AND (input_fingerprint IS NULL OR input_fingerprint ~ '^[0-9a-f]{64}$')) OR (outcome NOT IN ('Drafted','DraftDiscarded','Bypassed') AND input_fingerprint IS NULL)`. `votes.md:409` sets this plan for the vote columns: the columns stay nullable until the drop step. No drop step has shipped yet, and the repo has no `SET NOT NULL` migration, so this is a plan and not an executed precedent. The Down of PR 7 removes the strict form, because PR 7 also drops `context_json`, and the rollback already needs the explicit EF Down.

Backfill, by the rerunnable command of section 5 (D11), before the drop. The migration adds the schema only. Each step fills only the rows that still lack data:
- Fingerprint: `UPDATE relationship_analysis_runs SET input_fingerprint = context_json::jsonb ->> 'fingerprint' WHERE input_fingerprint IS NULL AND outcome IN ('Drafted','DraftDiscarded','Bypassed') AND context_json::jsonb ->> 'fingerprint' IS NOT NULL`. The outcome filter matches the strict check of PR 7. `AnalysisWorker.cs:187,266` put `ContextJson` on every run of an attempt, including rejected, stop-failure and discarded runs, so an unfiltered copy would break that check. The unmatched bypass context has no `fingerprint` key, so it stays null.
- Revisions: `UPDATE ... SET statement_record_id = context_json::jsonb #>> '{statement,recordId}', statement_revision_number = (context_json::jsonb #>> '{statement,revisionNumber}')::int, <target columns from {target,kind}, {target,recordId}, {target,revisionNumber}> WHERE statement_record_id IS NULL AND context_json::jsonb ? 'statement'`. Every run that carries a matched pair gets them, whatever its outcome. The unmatched context `{pairId, matched:false}` has no `statement` key.
- The command selects only runs whose named revisions exist, so the restricting FKs never abort a batch. It counts the other runs and reports their IDs as skipped with the reason `MissingRevision`. It also counts Drafted and DraftDiscarded runs that still have a null fingerprint after the fill. Both counts must be zero before verify passes.

The writer needs the new fields, or every new run gets null and every pair looks stale (`RelationshipStaleness.cs:32-33`), so paid redrafts repeat. Today `RelationshipDraftRun` holds only `(Run, Reason)`, and `RelationshipDraftQueueMapping.AddRun` sets only `Reason` (`:31-36`). The writer has four parts:

- `RelationshipDraftRun` gains only `InputFingerprint` (like `BriefCheckRun`, `BriefCheckRun.cs:5`). The pair revisions live on `AnalysisRun.References` (D10). The new positional field breaks these constructor sites: `RelationshipDraftStep.cs:18` (`Wrap`), `:54` (recite), `:87` (no relationship), `:95` (`SettleWithoutCallAsync`), `RelationshipDraftQueueMapping.cs:29` (`WrapInterrupted`), `PostgresRelationshipDraftStore.cs:39` (`GetRunAsync`, reads the columns) and `PostgresRelationshipDraftStoreTests.cs:288`.
- `RelationshipDraftStep` sets the fingerprint only on the Drafted and Bypassed runs: the two runs in `SettleAcceptedAsync` (pass it the context), the recite run, and the `SettleWithoutCallAsync` runs for unchanged and no-relationship. `SettleWithoutCallAsync` (`:91-96`) gains a nullable fingerprint parameter. The unmatched path passes `PairId` as its input hash (`:28`), so it passes null.
- Pair revisions on every run (D10). One Core type owns them: `AnalysisRunReference`. `AnalysisRun` gains `ImmutableArray<AnalysisRunReference> References` as a last parameter with an empty default. `RelationshipDraftStep` sets the references on the attempt, the way it sets `ContextJson` today. `Attempt.Run` and `BypassRun` copy them to every run they build, so rejected and failed runs get them too. `Wrap` (`:18`) stays a bare function. The worker calls it with no context for ProcessingFailed runs (`AnalysisWorker.cs:213`) and call failures (`AnalysisWorker.cs:232`), and it needs none.
- `AddRun` reads `run.Run.References` and writes them, as `BriefCheckQueueMapping.cs:35` does. `RelationshipDraftRun` holds no reference, so no path has a second owner.
- The settled-run filter `r.context_json::jsonb ->> 'fingerprint' IS NOT NULL` (`RelationshipStaleness.cs:65`) moves to `r.input_fingerprint IS NOT NULL`, with `:58`, in PR 3.

**Brief runs** (`brief_analysis_runs`, tasks `brief-draft` and `brief-check`). Context shapes: `BriefDraftPrompt.cs:41-56` (sent `records`: `id`, `kind`, `recordId`, `revisionNumber`) and `BriefCheckPrompt.cs:52-59` (`citations` with `id`, `notCited` without an ID).

New child table `brief_analysis_run_records`. Precedent: `brief_check_flags` (`BriefCheckFlagRowConfiguration.cs:12-30`).

| Column | Type |
|---|---|
| `run_id` | `varchar(32)` |
| `position` | `smallint` |
| `citation_id` | `varchar(16)` null |
| record revision keys | see D1a |

- PK `(run_id, position)`. FK `run_id` to `brief_analysis_runs`, restrict. `ck_brief_analysis_run_records_position`: `position >= 1`. A unique index on `(run_id, citation_id)` where `citation_id IS NOT NULL`.
- One row per record sent. A brief draft run holds the records it sent, each with its citation ID. A brief check run holds the cited records first (with their citation IDs from the revision), then the not-cited records with a null `citation_id`. Position keeps the sent order. Note for the owner: the cited rows of a check run copy the citations of the checked revision (`BriefCheckPrompt.cs:56` reads `revision.Citations`). Only the not-cited rows are new data.
- Writers: `BriefDraftStep` and `BriefCheckStep` set the records as `AnalysisRunReference` values on the attempt (D10). `BriefDraftRun` and `BriefCheckRun` gain no record field. `BriefDraftQueueMapping.AddRun` and `BriefCheckQueueMapping.AddRun` read `run.Run.References` and write the rows, as `BriefCheckQueueMapping.cs:40` writes flags. They replace `SerializeContext` at `BriefDraftPrompt.cs:41` and `BriefCheckPrompt.cs:52`. Failure runs get the references through the attempt.
- Backfill, by the command of section 5 (D11): `INSERT INTO brief_analysis_run_records ... SELECT r.run_id, e.ordinality::smallint, ... FROM brief_analysis_runs r CROSS JOIN LATERAL jsonb_array_elements(r.context_json::jsonb -> 'records') WITH ORDINALITY AS e(value, ordinality) WHERE r.task = 'brief-draft' AND r.context_json IS NOT NULL AND NOT EXISTS (SELECT 1 FROM brief_analysis_run_records x WHERE x.run_id = r.run_id)`. For `brief-check` runs, the same shape runs over `citations` and then over `notCited`, and the not-cited positions start after `jsonb_array_length(citations)`. A `CASE` maps the kind text of each context to the key columns. The two writers differ in casing. The draft context writes `Kind.ToString()`, which is PascalCase: `DocumentChange`, `Statement`, `Action` (`BriefDraftPrompt.cs`, `SerializeContext`). The check context writes `KindOf(...)`, which is camelCase: `documentChange`, `statement`, `action` (`BriefCheckPrompt.cs:124`, `JsonNamingPolicy.CamelCase`). The `CASE` of each task matches its own casing. The task names are `BriefDraftTask.Task` and `BriefCheckTask.Task`. A wrong name would copy nothing and raise no error, so the command checks the result.
- Pre-check, in the command, after each fill. Count the brief runs whose row count differs from the length of their context lists: `SELECT count(*) FROM brief_analysis_runs r WHERE r.context_json IS NOT NULL AND coalesce(jsonb_array_length(r.context_json::jsonb -> 'records'), 0) + coalesce(jsonb_array_length(r.context_json::jsonb -> 'citations'), 0) + coalesce(jsonb_array_length(r.context_json::jsonb -> 'notCited'), 0) <> (SELECT count(*) FROM brief_analysis_run_records x WHERE x.run_id = r.run_id)`. The command prints the count and the run IDs, and exits 1 when the count is not zero. This count includes runs that got no record rows and must have them. A wrong task filter then fails loud.

**Sub-choice D1a (shape of a foreign key to a polymorphic revision).** DECIDED 2026-11-04: option (i).
The relationship target is an Action or a Vote. A brief record is a DocumentChange, a Statement or an Action. One FK cannot name two tables. The decision: one nullable column group per kind, each with its own restricting FK, and a `num_nulls` check that exactly one group is set. Precedent for the check: `ck_action_revisions_columns_written` (`ActionRevisionRowConfiguration.cs:36`). Precedent for the FK: the R-63 rule that a revision link is a restricting FK (`actions.md:62`).
Options not chosen: (ii) a kind column with record ID and revision number and no FK. It follows the existing practice for references by kind: `relationship_analysis.target_kind` and `target_record_id` (`RelationshipAnalysisRowConfiguration.cs:16,19-20`), `publication_withdrawals` (`PublicationWithdrawalRowConfiguration.cs:18-20`) and `PublishedRevisionBinding`. (iii) One child table per kind.
Reason: D1 C chose a database proof of each link, and only (i) gives it.
Keys: `action_revisions (record_id, revision_number)`, `vote_revisions (record_id, revision_number)`, `statement_revisions (candidate_id, revision_number)` and `document_change_revisions (draft_id, revision_number)`. The columns are `target_action_record_id`, `target_action_revision_number`, `target_vote_record_id` and `target_vote_revision_number` on relationship runs. Brief records use `document_change_draft_id`, `statement_candidate_id` and `action_record_id`, each with its revision number column. The check on a relationship run is `num_nulls(<four target columns>)` in (2, 4) with the pair rule above. The check on a brief record is exactly one group set.

## 4. Write path

One owner for both tables: a new `AnalysisPromptTexts` static class in `src/CivicLens.Infrastructure/Analysis/`. Precedent: `VoteMemberLists` (`VoteMemberLists.cs:13-50`), which owns the identity and the `INSERT ... ON CONFLICT DO NOTHING` through raw SQL on the caller's transaction. A second precedent is the configuration revision insert (`JobRowWriter.cs:38`).

- `EnsureAsync(db, input, nowTicks, ct)` runs one statement for each of the two tables, system prompts first: `INSERT INTO analysis_system_prompts (...) SELECT ... FROM unnest(...) ORDER BY text_sha256 ON CONFLICT DO NOTHING`. Sorting by key gives every writer the same insert order, and the fixed table order gives every writer the same lock order. Settles of one kind already lock the budget day row first (`PostgresAnalysisQueue.cs:238`), so they run one after another and the order adds little. Only Bypassed runs skip that lock, and they insert no text. Each kind has its own budget table, so settles of different kinds do not share a lock. The order still guards that case. Keep it, at no cost.
- One caller: `PostgresAnalysisQueue.SettleRunAsync`, just before `mapping.AddRun` (`:242`). It runs in the checkpoint transaction under the subject row lock (`CheckpointAsync` `:161-180`). Every kind passes here, so no kind has its own insert.
- The Interrupted path (`:302`) and the Bypassed branch (`:229-233`) skip it. They hold no input.
- Hash once, one owner. A Core record `AnalysisRunInput(SystemPrompt, UserMessage, OutputSchema, Effort, MaximumOutputTokens)` owns the recipe in a static `TextHash(text)` and computes the two text hashes at construction. Core already uses `SHA256` (`BriefWeek.cs`). `AnalysisRunRows.Create` (`AnalysisRunRows.cs:9-33`) copies the hashes and `user_message`. `EnsureAsync` reads the same hashes from the `Input`. No `IdOf` in Infrastructure, no second hash pass.
- `AnalysisRun` (`AnalysisRun.cs:5-8`) gains `AnalysisRunInput? Input = null` as its last parameter. It sits beside `ContextJson`. It does not replace it, because `context_json` stays live until PR 7. The Interrupted rule in `Validate` needs it, and verify and rollback read it. `Attempt.Run` (`AnalysisWorker.cs:260-266`) builds the input from `request`. `BypassRun` (`:182-187`) passes null.
- Write rule and read rule are separate. `AnalysisRun.Validate()` stays the read rule and accepts a null `Input`. `AnalysisRun.ValidateForWrite()` calls it, then requires `Input` for every outcome except Bypassed and Interrupted, and none for those two. The owner of the write rule is the queue, which already validates before it writes. `CheckpointAsync` calls `Validate()` before the transaction (`PostgresAnalysisQueue.cs:164`). Change that call to `ValidateForWrite()`. `Interrupt` calls `run.Validate()` at `:301`. Change it to `ValidateForWrite()` too, so both write sites use one rule. Do not add a third call in `AnalysisRunRows.Create`. `ToRun` keeps `Validate()` (`AnalysisRunRows.cs:44`).
- NUL rule. `ValidateForWrite()` refuses a NUL character in the system prompt, the user message, the schema and each validation error. Postgres `text` and `varchar` cannot hold NUL, so the insert would fail late with a database error. `Validate()` refuses NUL in `OutputJson` and the labels today (`AnalysisRun.cs:24,41`). It does not check the errors. Existing rows cannot hold a raw NUL, because Postgres refuses it. They can hold the JSON escape `\u0000` in `validation_json`, `context_json` or `quotes_json`, and `::jsonb` refuses that escape. The backfill command therefore checks existing rows for the escape, skips them with the reason `NulEscape`, and reports their run IDs (section 5).
- Reads: `ToRun` leaves `Input` null. A reader needs the text only for an inspection command. Add that later, not now (AGENTS: no member without a caller).
- Validation errors must arrive with the run, not through a new query inside `ToRun`. `ToRun` stays static and gets the messages as an argument: `ToRun(IAnalysisRunRow row, IEnumerable<string> errors)`. The compiler then refuses a site that forgets them. The repo already reads child rows in a separate keyed query and stitches them in. Precedent: `VoteRevisionRows.ReadPageAsync` (`VoteRevisionRows.cs:63-81`), `ExecutiveActionRevisionRows.ReadPageAsync` (`ExecutiveActionRevisionRows.cs:44-53`) and `ReviewQueries.RevisionChildren` (`ReviewQueries.cs:57-58`). They use `FromSqlRaw` with `unnest` and `ToLookup`. D9 option A copies this. The shared reader and the CLR shape are in D9.
- Ten queries need the rows (section 1.2). No site loops over many runs, and `AnalysisRunLookup.Latest` callers load no run rows, so this is not an N+1 read. A rejected run without its errors makes `Validate()` throw ("at least one" error). The resume path (`AnalysisValidationLoop.cs:22`) needs these rows for recovery.

Concurrency: `ON CONFLICT DO NOTHING` on the PK makes two writers of one text safe. The second waits for the first to commit, then skips. The run FK sees a committed row.

## 5. PR order

### Rerunnable backfill command (D11)

The backfills of PR 2, 3 and 4 do not run inside their migrations. A migration runs once. After a code rollback, the old image writes runs with only the JSON columns. On roll forward the migration does not run again, so those rows never get the new columns or child rows. Three effects follow:

- A rejected run with no error rows makes `AnalysisRun.Validate` throw (`AnalysisRun.cs:31-33`).
- `StatementGateCarryOver` silently gets empty quotes (`StatementGateCarryOver.cs:15`).
- `RelationshipStaleness` skips runs with a null fingerprint (`RelationshipStaleness.cs:65`), so pairs redraft at a cost.

The owner chose (b): each backfill is a rerunnable command that fills only the rows still missing data. A rollback then needs no special EF Down step for PR 2, 3 and 4. The migrations add the schema only, and every new column stays nullable, so old code writes valid rows.

Design:

- Command: `analysis runs backfill <limit>`. A handler `BackfillAnalysisRuns` in Application calls `IAnalysisRunBackfillStore`. `AnalysisRunBackfill` in `Infrastructure/Analysis` implements the store. Precedent for this split: `BackfillBillSubjects` with `IBillSubjectsBackfillStore`, and `VoteCommand` with `BackfillRevisions`.
- Does it plug into the shared engine? No. `RevisionBackfill<TRow, TRevision, TPage>` (`RevisionBackfill.cs`) needs `TRow : IReviewRevisionRow`, a `RevisionKey(RecordId, RevisionNumber)` cursor, a review lock per kind, and a C# rebuild of each revision that it compares to the stored JSON (`RevisionBackfillKind`). Run tables key by `run_id`. They have no revision number and no review lock. A run backfill is a field copy with no domain type. A fake revision key would add a second meaning to the engine.
- What it reuses from the engine: the shape. A batch is one short transaction. The command walks `run_id` in key order. Each step selects only rows that still lack data, so a rerun does nothing for finished rows. It prints counts and run IDs, never text. It exits 1 when any run was skipped. The set-based SQL of PR 2, 3 and 4 stays as written, with an added `NOT EXISTS` or null-column filter.
- Skip reasons: `MissingRevision` (PR 3), `MissingExtraction` (PR 4) and `NulEscape` (all). The command selects only fillable rows, so a restricting FK never aborts a batch. It counts the others and prints their run IDs.
- It takes no review lock. Run rows are insert-only, and a step writes only null columns or absent child rows. A new-code run already holds its data, so the filter skips it.
- Precedent checked: `votes revisions backfill` and `executive-actions revisions backfill` (`PostgresVoteRevisionBackfillStore`, `PostgresExecutiveActionRevisionBackfillStore`), `bills subjects backfill` (`BillBackfillCommand.cs`), and `20261031041526_ReviewDecisionColumns.cs:552-575` (a one-shot migration backfill, which this decision replaces for run tables).
- Operations: add the command to `deploy/scripts/operator-command.sh` and to the listed commands in `operations.md:1235`, and add a runbook section next to "Vote revision backfill". The runbook runs the command until `examined` is 0, then runs verify, on every deploy that crosses PR 2, 3 or 4. The new code reads child rows, so run it straight after `update.sh`. A rejected run read before the fill fails that one read, and the rerun fixes it.
- PR 6, 7 and 8 still need the explicit EF Down, because they drop columns.

Each PR starts from `origin/main`. No stacking. Each is a migration plus code that stands alone and is safe to roll back. Each PR edits the docs of the behavior it changes, so docs never lag. `tools/check-docs.py` runs on each PR.

**PR 1. Prompt texts and run input, written by every kind.**
The tables have a production caller only when a run writes to them. So the tables, their owner, and the run columns ship together.
- Migration: `analysis_system_prompts`, `analysis_output_schemas` (each with the hash CHECK of D2), the five run columns, the four run checks, and the two FKs on the six tables through `ConfigureRun`.
- Code: `AnalysisPromptTexts` (`EnsureAsync`), `AnalysisRunInput` (with `TextHash`), `Attempt.Run`, `AnalysisRunRows.Create`, `SettleRunAsync`. Add `Effort` to `AnalysisModelRequest`, fed by the one constant `AnalysisEffort.Medium` in the nine `Build` methods (section 2), and map it to the SDK enum at `AnthropicStructuredCall.cs:43` (D3). Delete the eight task `Effort` consts. Return the output text for every stop that has text (D4, `AnthropicStructuredCall.cs:54-56`).
- `AnalysisRun` gains `Input` beside `ContextJson`. The context stays written and read. Nothing is swapped.
- Rules: `Validate()` stays the read rule, so `ToRun` loads legacy rows with a null `Input`. `ValidateForWrite()` requires `Input` for call runs and refuses NUL (section 4). Owner: `PostgresAnalysisQueue.CheckpointAsync` and `Interrupt` (section 4).
- Grants (D5 C): add the token `user_message` exactly to the hidden column pattern in `deploy/scripts/readonly-grants.sql`. A bare `message` token would hide `brief_check_flags.message`. System prompt and schema tables stay readable.
- Docs: `docs/architecture/analysis.md:198-200` says the run stores no prompt text. Rewrite it. Add the content hash rule of section 3.1 (CHECK over one row, verify over child rows). Update the schema docs.

Tests:
- New `AnalysisPromptTextsTests`: same text gives one row, parallel writers, insert order. A test that the database refuses a row whose key is not the SHA-256 of its text, in each table. `AnalysisRunInputTests`: the hash is the SHA-256 of the text.
- A grants test: `civic_lens_readonly` cannot read `user_message`, and still reads `brief_check_flags.message` (`PostgresBriefCheckStoreTests.cs:316`).
- `AnalysisRunTests`: `Validate()` accepts a null `Input`. `ValidateForWrite()` rejects it for call runs and for Bypassed and Interrupted runs that hold one. It also rejects a NUL character in each text field and each error.
- `AnalysisPromptHashTests`: the hash equals the stored parts.
- `AnthropicDocumentChangeDraftModelTests`: effort comes from the request. A new test: a refusal and a `max_tokens` stop keep their output text.
- Per kind: `PostgresDocumentChangeAnalysisStoreTests`, `PostgresStatementGateStoreTests`, `PostgresStatementDraftStoreTests`, `PostgresBillStreamTests.ActionDrafts`, `PostgresRelationshipDraftStoreTests`, `PostgresBriefCheckStoreTests`, and the vote and brief draft store tests. Each asserts that the stored user message, system text, and schema equal the request, and that a retry run holds its own user message.
- A new test: Bypassed and Interrupted keep nulls, and a legacy row with null input loads through `ToRun`.
- A migration test modeled on `VoteRevisionColumnsMigrationTests`. `SchemaReferenceTests` regenerates the schema doc. `ArchitectureTests` stays green.
- Tests that build `new AnalysisRun(...)` compile unchanged, because `Input` defaults to null. Three Postgres helpers build call runs and persist them through the queue, so they must pass an `Input`: `PostgresBillStreamTests.ActionDrafts.cs:999`, `PostgresHouseVoteTests.Drafting.cs:518`, and the `Run(...)` helper at `PostgresDocumentChangeAnalysisStoreTests.cs:496`. The last one uses a target-typed `new(` and builds the RateLimited and Refused runs of `:415` and `:482`. Five sites derive a Bypassed run from the Drafted helper with `with { Outcome = ... }`, so they copy its `Input` and must also set `Input = null`. They are `PostgresBillStreamTests.ActionDrafts.cs:793` and four in `PostgresBillStreamTests.ActionReview.cs`: `:426`, `:497`, `:523` and `:569` (lines at `de620566`; `origin/main` at `efd90376` shows them at `:436`, `:507`, `:533` and `:579`). The last three are `bypass = bypass with { Run = bypass.Run with { Outcome = AnalysisRunOutcome.Bypassed ... } }` lines. This PR breaks them, because `ck_{table}_input_outcome` and `ValidateForWrite()` refuse a Bypassed run that holds an input. Two other helpers build Bypassed runs and must not pass one: `PostgresBillStreamTests.ActionReview.cs:684-686` and `PostgresRelationshipDraftStoreTests.cs:282-284`. The five in-memory builders need no edit: `FakeAnalysisStore.cs:41`, `FakeStatementDraftStore.cs:36`, `FakeStatementGateStore.cs:42`, `StatementCodeApprovalTests.cs:191`, `StatementGateCarryOverTests.cs:55`.
- `DocumentChangeDraftPromptGoldenTests` stays unchanged. Raw inserts into run tables stay valid, because every new column is nullable: `GateLabelRuns.cs:12`, `RelationshipWorldSeeder.cs:92`, `PostgresBriefCheckStoreTests.cs:188`, `PostgresBillStreamTests.ActionDrafts.cs:711`, `PostgresHouseVoteTests.Drafting.cs:466`.

**PR 2. Validation errors to child rows.**
- Migration: the six error tables, with no data. The command of section 5 (D11) introduces `analysis runs backfill`, and its first step fills them from `validation_json`, which is `text`:
  `INSERT INTO <errors> (run_id, error_number, message) SELECT r.run_id, e.ordinality::smallint, e.value FROM <runs> r CROSS JOIN LATERAL jsonb_array_elements_text(r.validation_json::jsonb) WITH ORDINALITY AS e(value, ordinality) WHERE r.validation_json IS NOT NULL AND NOT EXISTS (SELECT 1 FROM <errors> x WHERE x.run_id = r.run_id)`.
  Precedent: `jsonb_array_elements_text` in `20261031041526_ReviewDecisionColumns.cs:563`. The step skips runs whose `validation_json` holds the escape `\u0000`.
- Rollback: an image from before this PR runs on the new schema with no EF Down. It writes `validation_json` only. After roll forward, the runbook runs the command before verify, and the rejected runs get their error rows.
- Operations: add the command to `operator-command.sh`, the listed commands in `operations.md`, and a runbook section.
- Writer: `AnalysisRunRows.Create` plus `AddRun` add the child rows.
- Reader: the ten queries (section 4) call the one shared reader in `AnalysisRunRows`, in the shape that D9 decides. `validation_json` stays written this PR, for rollback safety. It is no longer read.
- Tests: a store test per kind for a rejected run round trip, which loads its errors through the shared reader. A backfill test with legacy rows, and a rerun test: a run written by old code after the first fill gets its rows on the second run, and a finished run is untouched. A NUL test: `ValidateForWrite()` refuses a NUL in an error, and the command skips a `\u0000` row. A resume test for `AnalysisValidationLoop` that reads errors from rows. A test that `StatementGateQueueMapping.ToRun` and `PostgresStatementDraftStore.cs:47-53` load errors for a rejected gate run.

**PR 3. Context columns and sent-record rows (D1, D1a).**
Section 3.4 holds the design. This PR adds data and keeps `context_json` written, so it is safe to roll back. Staleness moves to the column in this PR.
- Migration: the relationship columns and checks, and the `brief_analysis_run_records` table. No data moves in it.
- Backfill: new steps of the command (D11) for the relationship fingerprint, the relationship revisions and the brief records, with the pre-checks that count nulls, missing revisions and mismatched record counts of section 3.4. A rollback needs no EF Down. The runbook runs the command before verify.
- Writers: one `AnalysisRunReference` type in Core, `AnalysisRun.References`, the references on the attempt (`IAnalysisAttempt`, `Attempt.Run`, `BypassRun`), `RelationshipDraftRun.InputFingerprint`, `RelationshipDraftStep`, the brief steps, and the `AddRun` of the three mappings. `BriefDraftRun` and `BriefCheckRun` gain no record field. `RelationshipDraftQueueMapping.AddRun` also owns the C# fingerprint rule. Section 3.4 lists the constructor sites.
- Docs: edit `statements.md:385` (the staleness paragraph) and `statements.md:581` (the last settled fingerprint), because they name `context_json` as the source. `statements.md:491`, `:493` and `:509` stay for PR 7.
- Reader: move `RelationshipStaleness.cs:58` and the filter at `:65` to the column. The store read (`PostgresRelationshipDraftStore.cs:18`) and the stale-pair query (`RelationshipStaleness.cs:32-33`) call that helper and need no change.
Tests: a new relationship run, once settled, writes its fingerprint, and its pair is no longer stale at `RelationshipStaleness.cs:32`. The database check accepts a null fingerprint and refuses a value that is not 64 hex characters, and a row that older code writes passes it. `AddRun` refuses a Drafted or DraftDiscarded run without a fingerprint and accepts a Bypassed run with or without one. A rejected relationship run writes its pair revisions. A worker test covers failure runs: a ProcessingFailed run and a call failure run (`AnalysisWorker.cs:213,232`) both keep the references that the step set on the attempt, for a relationship, a brief draft and a brief check run. The database refuses a revision that does not exist, for each FK group. A brief draft run and a brief check run round trip their records in order. A backfill test with legacy rows for both brief tasks, with the PascalCase and the camelCase kind. A rerun test: a pre-PR 3 run written after the first fill gets its fingerprint and rows on the second run, and the command skips a run that names a missing revision and reports it. Edit `PostgresRelationshipDraftStoreTests.cs`: the raw insert at `:155` names `context_json`, and the update at `:511` writes `"old"`, which fails the hex check, so it must write a 64-character hex value. A PR 3 test also writes a pre-PR 3 shaped run row with no new columns, and the checks accept it.

**PR 4. Gate quotes to child rows (D8).**
Same steps as `validation_json`: add, backfill, read from rows, verify, drop.
- Rollback: an image from before this PR runs with no EF Down. It writes `quotes_json` only. Without the fill, `StatementGateCarryOver` gets empty quotes without an error. The runbook runs the command before verify, and the command is the fix.
- Migration, schema only: `statement_analysis_run_quotes` (named like `statement_analysis_run_errors`) with `run_id`, `quote_number`, `issue_id`, `extraction_id`, `start` and `length` (the fields of `StatementGateQuote`). PK `(run_id, quote_number)`. FK `run_id` to `statement_analysis_runs`, restrict. FK `extraction_id` to `document_extractions`, restrict (precedent `statements.md:59`). Checks: `start >= 0` and `length >= 1`. Backfill, as a step of the command (D11): `INSERT INTO statement_analysis_run_quotes (run_id, quote_number, issue_id, extraction_id, start, length) SELECT r.run_id, q.ordinality::smallint, q.value ->> 'IssueId', q.value ->> 'ExtractionId', (q.value ->> 'Start')::int, (q.value ->> 'Length')::int FROM statement_analysis_runs r CROSS JOIN LATERAL jsonb_array_elements(r.quotes_json::jsonb) WITH ORDINALITY AS q(value, ordinality) WHERE r.quotes_json IS NOT NULL AND NOT EXISTS (SELECT 1 FROM statement_analysis_run_quotes x WHERE x.run_id = r.run_id)`. Pre-check for a missing extraction. A quote with a null `ExtractionId`, or one that names no row in `document_extractions`, would break the FK and abort the batch. The command counts those runs first. It skips each such run with the reason `MissingExtraction` and prints the run IDs, and it fills the other runs. It exits 1 while any run is skipped. The keys are PascalCase, because the writer uses the default `JsonSerializer` (`StatementGateQueueMapping.cs:52`) on `StatementGateQuote(IssueId, ExtractionId, Start, Length)`.
- Writer: `StatementGateQueueMapping.cs:52` writes the rows. `quotes_json` stays written this PR, for rollback safety.
- Reader: `StatementGateQueueMapping.cs:56-57` reads the rows through the same keyed-query shape that D9 decides. `StatementGateCarryOver.From` (`StatementGateCarryOver.cs:15`) and `PostgresStatementDraftStore.cs:53` keep reading `gateRun.Quotes`, so the carry-over path stays whole.
- Tests: a gate store round trip, `StatementGateCarryOverTests`, a backfill test with legacy rows, a rerun test, a test that the command skips and reports a run with a missing `ExtractionId`, and a test that the database refuses a quote with an unknown extraction.

**PR 5. Verify command.**
A read-only check, as R-63 requires before any drop. `RevisionBackfill.VerifyAsync` is generic over revision rows keyed by `(record_id, revision_number)`, and run tables key by `run_id`. So the run verify has a new owner: `AnalysisRunVerifier` in `src/CivicLens.Infrastructure/Analysis/`. It holds one SQL condition per check.
- For each run: errors rebuilt from rows equal `validation_json`. For each new call run, `AnalysisPromptHash` recomputed from stored model, effort, max tokens, system, user, and schema equals `input_hash`. The recompute runs in C#: the verifier reads batches and calls `AnalysisPromptHash.Compute`. SQL cannot reproduce it, because the hash covers JSON that `AnalysisPromptText.CompactJson` writes with `UnsafeRelaxedJsonEscaping`.
- Relationship columns equal the `context_json` fields (fingerprint, statement pair, target pair). The fingerprint comparison covers only runs `WHERE outcome IN ('Drafted','DraftDiscarded','Bypassed')`. Other outcomes hold a context fingerprint and a null column by design.
- Brief record rows equal the sent records of `context_json`, in order. Gate quote rows equal `quotes_json`.
- It prints counts and run IDs, never text. It exits 1 on mismatch. Precedent for the command: `votes revisions verify` (`docs/architecture/votes.md:150`). The text tables need no verify join, because a CHECK holds each key and plain FKs hold each role.
- The backfills of PR 2, 3 and 4 are steps of the rerunnable `analysis runs backfill` command (D11), not migration SQL. Verify runs after it. A run backfill is a plain field copy keyed by `run_id`. It needs no domain type and no hash recompute, so it uses set-based SQL behind the command and not the revision engine. The hash recompute of verify is the one C# part. Each drop migration (PR 6, 7, 8) repeats the check for its column in SQL before it drops, as `DropDecisionJson.CheckSql` does. A migration does not call application code. The runbook runs the command, then verify, on every deploy that crosses PR 2, 3 or 4, and before each drop.
Tests: a verify test per condition, like `PostgresHouseVoteTests.VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsTextBecauseTheTextIsPrivate`, and a command exit code test.

**PR 6. Drop `validation_json`.**
Migration drops the column after verify passes in production. Remove the property and the JSON code in `AnalysisRunRows.cs:29,41`. Delete the backfill step that reads `validation_json`, because it loses its source. Down migration restores the column and rebuilds it from the child rows with `jsonb_agg(e.message ORDER BY e.error_number)::text`, so the array keeps the order of `error_number`.
Rollback: an image from before PR 6 reads and writes `validation_json`, and the column does not exist. Add a paragraph to `operations.md` after the `DropDecisionJson` text (`operations.md:252`). It says that a rollback to such an image needs the explicit EF rollback of the migration first, then `update.sh` with the old SHA. It says that `db migrate` only moves forward. The Down rebuilds the column from the error rows, with the same JSON fields and a different key order and spacing.
Tests: migration down and up test with rows. Schema doc regeneration. Edit `PostgresBriefCheckStoreTests.cs:319`, which asserts that the hidden pattern does not match `validation_json`. Line 318 (`output_json`) stays, since `output_json` stays. Edit `briefs.md:93`, which names `validation_json`. `briefs.md:237` names only `output_json` and stays.

**PR 7. Drop `context_json`.**
This PR drops the column after verify passes in production (D1). Its replacements ship in PR 3.
- Migration: drop the column, and replace the format check of `ck_relationship_analysis_runs_fingerprint` with the strict form (section 3.4). Down migration restores the column and the format check. It rebuilds the fields that the columns and rows hold. Where it cannot rebuild a context, it writes `'{}'` for every run that is not Interrupted, and leaves an Interrupted run null. Old `AnalysisRun.Validate` requires a context on every run that is not Interrupted (`AnalysisRun.cs:34`), and old `ToRun` validates every row it reads (`AnalysisRunRows.cs:44`). Without `'{}'`, old code fails on the first read of such a row. A test runs the Down on legacy-shaped rows and loads each through the old `Validate`.
- Rollback: add a paragraph to `operations.md` as for `DropDecisionJson` (`operations.md:252`). An image from before PR 7 needs `context_json`. The order is: stop the worker and the review host, roll the database back with the EF tool, then run `update.sh` with the old SHA. Running `update.sh` alone fails every run write.
- Code: delete the backfill steps that read `context_json` (fingerprint, revisions, brief records). Remove `ContextJson` from `IAnalysisAttempt` (`IAnalysisAttempt.cs:8`), `AnalysisWorker.cs:160,187,266`, `AnalysisRun.cs:34-35`, the row interface, and the column. Interrupted runs lose the special rule. `PostgresAnalysisQueue.cs:297-300` builds the Interrupted `AnalysisRun` with a null context in the positional list, so it changes too. The typed run references of D10 take the place of `ContextJson` on the attempt.
- Remove nine context lines in eight step files: `ActionDraftStep.cs:37`, `DocumentChangeDraftStep.cs:31`, `BriefCheckStep.cs:27`, `BriefDraftStep.cs:26`, `RelationshipDraftStep.cs:27,31`, `StatementDraftStep.cs:31`, `StatementGateStep.cs:28`, `VoteDraftStep.cs:34`.
- Remove nine context methods that lose their last production caller: six `SerializeContext` (action, brief check, brief draft, relationship, statement draft, vote), `SerializeCatalog`, `ToContextJson`, and `SerializeUnmatchedContext`.
- Docs: `statements.md:491,493,509` describe `context_json`. `analysis.md` too. PR 3 already edits `statements.md:385` and `:581`.

Tests:
- Two tests still call context methods and change: `ActionDraftPromptGoldenTests.cs:66` and `PostgresDocumentChangeAnalysisStoreTests.cs:115`.
- Remove the context rule from `AnalysisRunTests.cs:27-28`.
- Update `PostgresBillStreamTests.ActionDrafts.cs:484,503,505`, `StatementGateStepTests.cs:46`, `DocumentChangeDraftStepTests.cs:29`, `VoteDraftStepTests.cs:426`, `BriefCheckStepTests.cs:96`, and `RelationshipWorldSeeder.cs:93`.
- `PostgresRelationshipDraftStoreTests.cs:155` still names `context_json` in its raw insert. Rewrite it to use the new columns.
- `ContextJson` sits in the middle of the `AnalysisRun` record. Removing it breaks every `new AnalysisRun(...)`. Edit the nine files below. Three more helpers use the same positional list and change too: `PostgresDocumentChangeAnalysisStoreTests.cs:496`, `PostgresHouseVoteTests.Drafting.cs:523` (a Bypassed run) and `AnalysisRunTests.cs:38`.
  Files: `FakeAnalysisStore.cs:41`, `FakeStatementDraftStore.cs:36`, `FakeStatementGateStore.cs:42`, `StatementCodeApprovalTests.cs:191`, `StatementGateCarryOverTests.cs:55`, `PostgresBillStreamTests.ActionDrafts.cs:999`, `PostgresBillStreamTests.ActionReview.cs:684`, `PostgresHouseVoteTests.Drafting.cs:518`, `PostgresRelationshipDraftStoreTests.cs:282`.

**PR 8. Drop `quotes_json`.**
Migration drops the column after verify passes. Remove the property (`StatementAnalysisRunRowConfiguration.cs:24`, `StatementAnalysisRunRow.cs:28`) and the JSON code at `StatementGateQueueMapping.cs:52,57`. Down migration restores the column and rebuilds it from the gate quote rows. Delete the backfill step that reads `quotes_json`. When the last step goes, delete the command. Update the schema docs and `statements.md`. Rollback: add a paragraph to `operations.md` as for `DropDecisionJson` (`operations.md:252`). An image from before PR 8 writes `quotes_json` and needs the column. It needs the explicit EF rollback first, then `update.sh` with the old SHA.

No separate docs PR. PR 1, 2, 3, 6 and 7 touch `AnalysisRunRows.cs`, and PR 4 and 8 touch `StatementGateQueueMapping.cs`, so merge in order, one at a time. Roadmap says R-63 revision steps follow R-51 PR 3 and PR 4.

## 6. Open decisions for the owner

**D1. What to do with `context_json` data that is not in the user message.** DECIDED: C, extended.
Decision: each run keeps a foreign key to every record revision it was given. Brief draft and brief check runs get a child table of sent records (citation ID, record, revision number). Relationship runs get statement and target revision columns plus `input_fingerprint`. Control values (`summaryVersion`, `considered`, `omittedNotCited`) drop. Action and vote fingerprints already live on revisions (`ActionRevisionRows.cs:38`). A verify step runs before the `context_json` drop, because the new data can be rebuilt from it. Design: section 3.4, PR 3, PR 5 and PR 7. Sub-choice D1a (the FK shape for a polymorphic revision) is decided in section 3.4.
Precedent checked: `brief_check_flags`, `brief_analysis_runs.input_fingerprint`, `RelationshipStaleness.cs:58`, `votes revisions verify`.

**D2. How a text table proves that text matches its key.** DECIDED: B.
Decision: a database CHECK that the key is the SHA-256 of the text (section 3.1). Rule for the plan: a content hash over one row gets a CHECK. A content hash over child rows (`vote_member_lists`) gets a verify recompute (`PostgresVoteRevisionBackfillStore.cs:68`), because a CHECK cannot see child rows and AGENTS.md limits triggers. PR 1 writes the rule into the analysis architecture doc.
Precedent checked: `vote_member_lists` (hash of a canonical list), `ck_collection_configuration_revisions_id` (format only). Hash-of-content CHECKs have no precedent. This is a departure to document. Postgres runs 18.3 (`deploy/compose.yaml:30`), so `sha256()` is available.

**D3. Store `effort` and `max_output_tokens` on the run.** DECIDED: B.
Decision: store both as columns, and put `Effort` on the request, with a string to enum mapping at `AnthropicStructuredCall.cs:43`. All eight consts are `"medium"` today, and they become the one constant `AnalysisEffort.Medium`. It also removes drift between the task consts and the SDK call.

**D4. Raw output for refusal and `max_tokens` stops.** DECIDED: B.
Decision: return text for every stop that has text (`AnthropicStructuredCall.cs:54-56`). `StopOutcome` (`AnalysisModelResponse.cs:7-13`) decides the outcome from `StopReason` first, so this is safe. The column keeps its name.

**D5. Who may read `user_message` and prompt text.** DECIDED: C.
Decision: hide only `user_message`, with the exact token `user_message` in the pattern of `deploy/scripts/readonly-grants.sql` (PR 1). A bare `message` token would also hide `brief_check_flags.message` and fail `PostgresBriefCheckStoreTests.cs:316`. System prompt and schema text stay readable.

**D6. Interrupted runs.** DECIDED: A.
Decision: leave the input null. State it in the doc. The run keeps its `input_hash`.

**D7. One text table or two.** DECIDED: two tables.
Decision: `analysis_system_prompts` and `analysis_output_schemas`. Plain FKs enforce the role, so there is no role column. The hash covers the text only, so the D2 CHECK is the SHA-256 of the text. One owner, `AnalysisPromptTexts`, writes both. PR 5 has no role join.

**D8. `quotes_json` on `statement_analysis_runs`.** DECIDED: B.
Decision: move it to a child table of gate quotes in this series, with backfill, a carry-over read from rows (`StatementGateCarryOver`), verify, then drop. These are the same steps as `validation_json`. PR 4 and PR 8.

**D9. CLR shape and load of the six error tables.** DECIDED 2026-11-04: A.
Problem: PR 2 needs one child table per run table, and `ToRun` must see the errors with the run. The shape decides how the rows load, and a wrong load makes `Validate()` throw for a rejected run.
Options:
- A. Follow the keyed-query precedent. Each run row class gets a typed `Errors` collection for writes. One shared reader in `AnalysisRunRows` reads the run rows, then their error rows by `run_id`, and returns the messages as a lookup. `ToRun` takes the messages as an argument.
- B. One shared-type entity for all six tables. EF supports it, but a shared-type entity is used through `Set<T>(name)`, which gives a weak type and no clean navigation.
- C. `OwnsMany(...).ToTable(...)` on each run row configuration. One error class maps to six tables. EF loads owned rows with their owner, so a missed load cannot happen.
Precedent checked (A): the repo reads child rows in a separate keyed query and stitches them in. `VoteRevisionRows.ReadPageAsync` (`VoteRevisionRows.cs:63-81`) reads the revision rows, then facts and issues with `FromSqlRaw(ReviewQueries.RevisionChildren(table), recordIds, numbers)`, then `ToLookup`. `ExecutiveActionRevisionRows.ReadPageAsync` (`ExecutiveActionRevisionRows.cs:44-53`) does the same. `ReviewQueries.RevisionChildren` (`ReviewQueries.cs:57-58`) joins `unnest` to the child table. Writes assign a typed collection on the row (`VoteRevisionRows.cs:59-60`), and the configuration uses `HasMany(nav).WithOne().HasForeignKey(...).OnDelete(DeleteBehavior.Restrict)` (`ActionRevisionRowConfiguration.cs:74-75`, `VoteRevisionRowConfiguration.cs:72-73`). `git grep` finds no `Include(` and no `OwnsMany` or `OwnsOne` in `src`. So A needs no new EF mechanism. The repo rule is restricting foreign keys (`actions.md:42,62`, `collection.md:369`). Owned collection types cascade deletes in EF Core and, as far as the reviewer knows, cannot use Restrict. So C likely cannot hold that rule. A spike must prove it.
CLR shape under A. `IAnalysisRunRow` cannot hold an `Errors` collection, because six error classes have six types. The vote and executive precedent gives each child table its own row class and keeps the typed collection on the parent class only. So A uses:
- Six sealed error row classes, one per table, that share a small interface `IAnalysisRunErrorRow` (`RunId`, `ErrorNumber`, `Message`). One generic `ConfigureRunError<TError>` helper next to `ConfigureRun` maps all six.
- Each run row class implements `IAnalysisRunRow<TError>`, which adds `List<TError> Errors`. The generic parameter keeps the type per kind. `AnalysisRunRows.Create<TRow, TError>` sets `Errors` from the run, in one place.
- One reader, `AnalysisRunRows.ReadPageAsync<TRow, TError>(db, query, errorTable, ct)`. It runs the query for the run rows, then `SELECT c.* FROM {errorTable} c JOIN unnest({0}) AS k(id) ON c.run_id = k.id`, and returns the rows with `ILookup<string, string>` of messages ordered by `error_number`. The ten call sites use it. None writes its own errors query. The gate quote rows of PR 4 use the same reader shape.
- `ToRun(IAnalysisRunRow row, IEnumerable<string> errors)`. The argument is required, so the compiler finds a site that forgets the load.
Tradeoffs: A is plain EF and follows the precedent. It keeps Restrict. It costs six error row classes, one generic interface, one shared reader, two queries for each run read, and ten call sites that change from a direct query to the reader. A missed load becomes a compile error, not a test failure. C removes the load code, but it is new, and it probably breaks the Restrict rule. B has the weakest typing.
Decision: A. It follows the keyed-query precedent for child rows and keeps Restrict.

**D10. How a failure run gets the typed references that `context_json` held.** DECIDED 2026-11-04: (a).
Problem: the plan gives relationship runs their pair revisions and brief runs their sent records (section 3.4). `RelationshipDraftStep.Wrap` (`RelationshipDraftStep.cs:18`) takes a bare `AnalysisRun`. The worker calls `worker.step.Wrap(...)` with no context for a ProcessingFailed run (`AnalysisWorker.cs:213`) and for a call failure (`AnalysisWorker.cs:232`). So a closure in `Wrap` cannot reach the failure runs. `BriefDraftStep.Wrap` and `BriefCheckStep.Wrap` have the same shape. Today `ContextJson` reaches every run, because the attempt holds it and `Attempt.Run` copies it (`AnalysisWorker.cs:160,187,266`, `IAnalysisAttempt.cs:8`).
Options:
- (a) Carry typed run references on the attempt, as `IAnalysisAttempt.ContextJson` does today. The step sets the references. `Attempt.Run` and `BypassRun` copy them to every run built from the attempt. The `Wrap` closure item leaves the plan. This replaces `ContextJson` on the attempt in PR 7.
- (b) Limit backfill and verify to the outcomes the step builds. Document that failure runs lose the references.
Precedent checked: the `ContextJson` path on the attempt (`IAnalysisAttempt.cs:8`, copied to every run by `Attempt.Run`).
Tradeoffs: (a) adds one typed member to the attempt and to `AnalysisRun`, and it loses no data. (b) adds no member. It leaves failure runs without references, and it limits the backfill WHERE and the verify to three outcomes. It also leaves a gap in the audit trail of a failed call.
Decision: (a), because it follows the existing attempt precedent and loses no data. The `Wrap` closure item is gone from the plan.
One owner for the references: a single Core type, `AnalysisRunReference`, and `ImmutableArray<AnalysisRunReference> References` on `AnalysisRun`. `AddRun` of each mapping reads `run.Run.References`. `RelationshipDraftRun` keeps only `InputFingerprint`, and `BriefDraftRun` and `BriefCheckRun` gain no record field. A test covers worker failure runs (PR 3).

**D11. Rerunnable backfill for the run tables.** DECIDED 2026-11-04: (b).
Problem: a backfill inside a migration runs once. After a code rollback, old code writes runs with only the JSON columns, and roll forward does not run the backfill again (section 5).
Options:
- (a) Keep the one-shot SQL in the migrations. Accept the gap after a rollback, or add an EF Down step per PR.
- (b) Make each backfill a rerunnable command that fills only the rows still missing data, and run it before verify on every deploy that crosses PR 2, 3 or 4.
Decision: (b). A rollback then needs no special EF Down step for PR 2, 3 and 4.
Precedent checked: the revision backfills (`votes revisions backfill`, `executive-actions revisions backfill`) and the shared engine `RevisionBackfill` (`RevisionBackfill.cs`, `RevisionBackfillKind.cs`). Model runs do not plug into that engine, because it keys on `(record_id, revision_number)`, takes a review lock and rebuilds a revision in C#. They need their own command with the same shape: batches, a filter on missing data, counts and IDs, and exit code 1 on a skip. Details: section 5.

## 7. Risks to keep in view

- Size: user messages add raw text to six run tables. The repo holds no size figure. Postgres compresses large `text` in TOAST. Measure after PR 1 with `pg_total_relation_size` on each run table before and after one week.
- Down migrations: PR 6, 7 and 8 drop columns. Each needs the repo's down-and-up test with legacy rows.
- `tools/check-docs.py` and `tools/render-architecture.py --check` run on each PR. The architecture diagram changes only if a project boundary moves. None does here.
- The Core project may not take a package. Hashing uses the BCL, as `AnalysisPromptHash` does in Application, so `AnalysisRunInput` in Core is allowed. If the owner prefers, put the record in Application and keep `AnalysisRun` in Core with plain string fields.
