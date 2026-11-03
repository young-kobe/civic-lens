# Survey: model run storage (R-63, option B)

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
`statement_analysis_runs` holds two tasks, `gate` and `draft`. `action_analysis_runs` holds the text task and the issue choice task. `brief_analysis_runs` holds `draft` and `check`.

`brief_check_flags` is an existing child table of `brief_analysis_runs` (pk `run_id, flag_number`). It is the closest precedent for a run child table.

### 1.2 The JSON columns

| Column | Holds | Writer | Readers |
|---|---|---|---|
| `output_json` | Concatenated text blocks of the model answer. Set only when `stop_reason` is `end_turn` and the text is non-empty (`AnthropicStructuredCall.cs:54-57`). | `AnalysisRunRows.cs:28` from `AnalysisRun.OutputJson` (`AnalysisRun.cs:7`). `AnalysisWorker.cs:179-180` passes `response.OutputJson`. | `AnalysisRunRows.cs:40` into `AnalysisRun`. `StatementGateCarryOver.From` parses it (`StatementGateCarryOver.cs:12`). That parse is the input of the statement draft step. Resolvers parse the live response, not the row. No SQL reader. |
| `validation_json` | JSON array of up to 64 strings, each up to 512 chars (`AnalysisRun.cs:10-11`). Only `CitationRejected` and `OutputRejected` runs hold it (`AnalysisRun.cs:28-33`). | `AnalysisRunRows.cs:29`. The loop sets the errors at `AnalysisValidationLoop.cs:41-44`. | `AnalysisRunRows.cs:41`. The retry reads them back at `AnalysisValidationLoop.cs:22` to rebuild the prompt after a crash. |
| `context_json` | Per-kind JSON copy of inputs. Required for every run except `Interrupted`, which must have none (`AnalysisRun.cs:34-35`). | `AnalysisRunRows.cs:30`. `AnalysisWorker.cs:160` holds it, and `:187` (bypass) and `:266` (every call run) copy it. | `AnalysisRunRows.cs:42`. One SQL reader: `RelationshipStaleness.cs:58,65` reads `context_json::jsonb ->> 'fingerprint'`. Tests only otherwise. |
| `quotes_json` | Gate quotes (`StatementGateQuote[]`). Statement runs only. | `StatementGateQueueMapping.cs:52`. | `StatementGateQueueMapping.cs:56-57`. `StatementGateCarryOver.From` reads `gateRun.Quotes` (`StatementGateCarryOver.cs:15`) for the draft step. `PostgresStatementDraftStore.cs:53` calls it. |

`quotes_json` is a JSON column outside option B. R-63 still owns it. This survey only flags it.

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

Run row readers: `AnalysisRunRows.ToRun` (`:35-46`). It is a static, synchronous mapper with no database access. It calls `Validate()` on every row it maps (`:44`). It has nine call sites: `PostgresStatementDraftStore.cs:38`, `PostgresStatementGateStore.cs:32` via `StatementGateQueueMapping.cs:56`, `PostgresActionDraftStore.cs:44`, `PostgresVoteDraftStore.cs:47`, `PostgresRelationshipDraftStore.cs:39`, `PostgresBriefDraftStore.cs:76`, `PostgresBriefCheckStore.cs:33`, `PostgresDocumentChangeAnalysisStore.cs:42,51`. `PostgresStatementDraftStore.cs:53` also reaches it through the gate mapping. Each site reads one run by exact ID. `GetAnalysisRun.cs:13` exposes it.

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

Rejection reasons are appended to the user message (`AnalysisPromptText.cs:20-26`). So each retry pass has its own user message. Each pass is its own run.

At call time one object owns all four values:

- The request is stored on the attempt at `AnalysisWorker.cs:171` (`(RunId, previousRunId, request, started) = ...`). Field at `:155`.
- It goes to the SDK through `send` at `:173`, then `AnthropicStructuredCall.SendAsync` (`:32-46`).
- The raw answer text is `AnalysisModelResponse.OutputJson` (`AnalysisModelResponse.cs:5`), built at `AnthropicStructuredCall.cs:54-57`.
- Every run built from a call goes through one method, `Attempt.Run` (`AnalysisWorker.cs:260-266`). Callers: `ResponseRun` (`:179`), `FailUnexpectedAsync` (`:213`), `FailCallAsync` (`:232`). `request` is non-null whenever `RunId` is set.

So one attach point serves all eight tasks. No kind builds a run on its own.

Gaps that matter for "exact":

1. **Wire bytes are not available.** The SDK serializes the HTTP body. The code holds the request fields, not the body. The schema is parsed and cloned (`AnthropicStructuredCall.cs:83-87`), then re-serialized by the SDK. Whitespace and key order of the schema on the wire may differ from `SchemaJson`. The stored schema text is the exact string the code built, not the exact wire bytes. All kinds are the same.
2. **Effort and max tokens are not on the request.** The SDK call hard-codes `Effort.Medium` (`AnthropicStructuredCall.cs:43`). Each task has its own `Effort` const (for example `ActionDraftTask.cs:14`) that feeds only the hash. They agree today by convention. All eight consts are the string `"medium"`, and the SDK takes an enum. So a request that carries `Effort` needs a mapping from string to enum at `:43`. `MaximumOutputTokens` is on the request (`AnthropicStructuredCall.cs:38`) but not stored on the run.
3. **System prompt is sent as a text block when cached.** `CacheSystemPrompt` (`:69-71`) changes the shape, not the text. The text is the same string.
4. **Output text is dropped for every stop that is not `end_turn`.** `AnthropicStructuredCall.cs:54-56` returns null text for `refusal`, `max_tokens`, and any other stop, which becomes `UnexpectedStop`. Those runs keep no output today. A refusal message, a truncated answer, or an unexpected stop text is lost.
5. **Interrupted runs have no request.** `PostgresAnalysisQueue.Interrupt` (`:292-304`) builds the run from the reservation columns (`pending_run_id`, `pending_input_hash`, `ReserveAsync` `:150-154`). The request lived only in the dead worker. The model call may or may not have been sent.
6. **Bypassed runs have no request.** `AnalysisWorker.BypassRun` (`:182-187`) makes no call. There is no user message to store.
7. **A call that fails before a response** (`ConnectionFailed`, `RateLimited` and so on) still has the full request. `FailCallAsync` (`:221-233`) builds the run through `Run`. It would keep its input and null output.

No kind is unable to give its request. Only the wire body is unavailable, for all kinds.

## 3. Proposed schema

### 3.1 Prompt text table

`analysis_prompt_texts` (one shared table, not per kind).

| Column | Type | Notes |
|---|---|---|
| `text_sha256` | `varchar(64)` PK | Lowercase hex. Hash recipe below. |
| `role` | `varchar(16)` not null | `system` or `schema`. |
| `text` | `text` not null | Verbatim string the code built. |
| `created_at_utc_ticks` | `bigint` not null | Ticks of the first writer. |

Checks, named like the repo's (`ck_{table}_{rule}`):

- `ck_analysis_prompt_texts_id`: `text_sha256 ~ '^[0-9a-f]{64}$'`. Precedent: `ck_vote_member_lists_id`, `ck_collection_configuration_revisions_id`.
- `ck_analysis_prompt_texts_role`: `role IN ('system','schema')`.
- `ck_analysis_prompt_texts_text`: `char_length(text) BETWEEN 1 AND <cap>`. Mirror `AnalysisRun.MaximumOutputLength` (`AnalysisRun.cs:12`) or the task prompt cap.
- `ck_analysis_prompt_texts_created_at_utc_ticks`: the standard tick range check every table has.
- Optional, see decision D2: `text_sha256 = encode(sha256(convert_to(role || E'\n' || text, 'UTF8')), 'hex')`.

Hash recipe, one only: SHA-256 over the UTF-8 bytes of `role`, a newline (`\n`), and `text`, written as lowercase hex. C# `IdOf(role, text)` and the D2 check use this exact recipe. The role in the hash means the same text under two roles gets two keys, so one key never has two roles.
`VoteMemberLists.IdOf` adds a version tag (`vote-member-list-v1`, `VoteMemberLists.cs:15,30-31`). This table adds no tag, so that SQL can repeat the hash. A new recipe later needs a migration. The role set has no newline, so the join is unambiguous.

### 3.2 Run columns

Add to all six run tables through `ConfigureRun`, so one change covers every kind:

| Column | Type | Notes |
|---|---|---|
| `system_prompt_sha256` | `varchar(64)` null | FK to `analysis_prompt_texts(text_sha256)`, restrict. |
| `output_schema_sha256` | `varchar(64)` null | Same FK. |
| `user_message` | `text` null | Exact text. |
| `effort` | `varchar(16)` null | See D3. |
| `max_output_tokens` | `integer` null | See D3. |

`output_json` stays and keeps its name. It already holds the exact answer for `end_turn` runs. See D4 for the other stops.

Drop `context_json` and `validation_json` in later steps (section 5).

Checks per run table, added in `ConfigureRun`:

- `ck_{table}_input`: `(system_prompt_sha256 IS NULL) = (output_schema_sha256 IS NULL) AND (system_prompt_sha256 IS NULL) = (user_message IS NULL) AND (system_prompt_sha256 IS NULL) = (effort IS NULL) AND (effort IS NULL) = (max_output_tokens IS NULL)`. All set or all null. Precedent: all-or-none checks such as `ck_document_change_analysis_reservation`.
- `ck_{table}_input_outcome`: `outcome NOT IN ('Bypassed','Interrupted') OR system_prompt_sha256 IS NULL`. A run without a call holds no request.
- `ck_{table}_user_message`: `user_message IS NULL OR char_length(user_message) BETWEEN 1 AND <cap>`.
- `ck_{table}_max_output_tokens`: `max_output_tokens IS NULL OR max_output_tokens > 0`.

Old runs have all nulls, so every check passes. No rebuild, as decided.
A call run written by new code must carry its input. The DB cannot tell a new run from an old one. The write rule has one owner: `AnalysisRun.ValidateForWrite()` in Core, which `AnalysisRunRows.Create` calls (see PR 1). `AnalysisRun.Validate()` is the read rule. It does not require `Input`, because `ToRun` calls it on every legacy row. The verify command covers old data (see PR 3).

Indexes: EF adds an index per FK by default, as on `IX_document_change_analysis_runs_previous_run_id` (`schema/analysis.md`). Keep both. They answer "which runs used this prompt". Cost is small: two 64-byte keys per run. Deleting a text row is never done.
EF already shortens long foreign key names (`InitialSchema.cs:1716`). The new check names collide with no name in `docs/architecture/schema/*.md`. The longest, `ck_document_change_analysis_runs_input_outcome`, has 46 characters and fits the 63 limit.

### 3.3 Validation child rows

One child table per run table, built by one shared `ConfigureRunError` helper next to `ConfigureRun`. Names: `document_change_analysis_run_errors`, `statement_analysis_run_errors`, `action_...`, `vote_...`, `relationship_...`, `brief_analysis_run_errors`.

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
- Read path: section 4 and PR 2.

### 3.4 Other column changes

- `relationship_analysis_runs.input_fingerprint varchar(64) null`, with the hex check. `RelationshipStaleness.cs:58,65` needs the fingerprint of a Drafted or Bypassed run once `context_json` is gone. Precedent: `brief_analysis_runs.input_fingerprint` (`BriefAnalysisRunRowConfiguration.cs:26-28,35`). Backfill from `context_json::jsonb ->> 'fingerprint'` in the same migration, before the drop.
- No other kind reads a context field. Statement gate `issue_revision_id` already is a column (`StatementAnalysisRunRowConfiguration.cs:22`).

## 4. Write path

One owner: a new `AnalysisPromptTexts` static class in `src/CivicLens.Infrastructure/Analysis/`. Precedent: `VoteMemberLists` (`VoteMemberLists.cs:13-50`), which owns the identity and the `INSERT ... ON CONFLICT DO NOTHING` through raw SQL on the caller's transaction. A second precedent is the configuration revision insert (`JobRowWriter.cs:38`).

- `IdOf(role, text)` computes the key.
- `EnsureAsync(db, input, nowTicks, ct)` runs one statement: `INSERT INTO analysis_prompt_texts (...) SELECT ... FROM unnest(...) ORDER BY text_sha256 ON CONFLICT DO NOTHING`. Sorting by key gives every writer the same insert order, so two writers inserting the same pair cannot deadlock.
- One caller: `PostgresAnalysisQueue.SettleRunAsync`, just before `mapping.AddRun` (`:242`). It runs in the checkpoint transaction under the subject row lock (`CheckpointAsync` `:161-180`). Every kind passes here, so no kind has its own insert.
- The Interrupted path (`:302`) and the Bypassed branch (`:229-233`) skip it. They hold no input.
- Hash once. A Core record `AnalysisRunInput(SystemPrompt, UserMessage, OutputSchema, Effort, MaximumOutputTokens)` carries the two text hashes, computed at construction. `AnalysisRunRows.Create` (`AnalysisRunRows.cs:9-33`) copies the hashes and `user_message`. `EnsureAsync` reuses the same hashes. No second hash pass.
- `AnalysisRun` (`AnalysisRun.cs:5-8`) gains `AnalysisRunInput? Input = null` as its last parameter. It sits beside `ContextJson`. It does not replace it, because `context_json` stays live until PR 5 (relationship staleness reads it, and the Interrupted rule needs it). `Attempt.Run` (`AnalysisWorker.cs:260-266`) builds the input from `request`. `BypassRun` (`:182-187`) passes null.
- Write rule and read rule are separate. `AnalysisRun.Validate()` stays the read rule and accepts a null `Input`. `AnalysisRun.ValidateForWrite()` calls it, then requires `Input` for every outcome except Bypassed and Interrupted, and none for those two. `AnalysisRunRows.Create` calls `ValidateForWrite()`.
- Reads: `ToRun` leaves `Input` null. A reader needs the text only for an inspection command. Add that later, not now (AGENTS: no member without a caller).
- Validation errors read through an EF navigation, not a new query in `ToRun`. `ToRun` stays static. `IAnalysisRunRow` gains an `Errors` collection. Each of the nine call sites already reads one run by exact ID. Each adds `Include(Errors)` ordered by `error_number` to that query, so the errors arrive with the run in one round trip. This is not an N+1 read: no site loops over many runs, and `AnalysisRunLookup.Latest` callers load no run rows. A rejected run without its errors makes `Validate()` throw ("at least one" error). So a missed `Include` fails the per-kind round-trip test. The resume path (`AnalysisValidationLoop.cs:22`) needs these rows for recovery.

Concurrency: `ON CONFLICT DO NOTHING` on the PK makes two writers of one text safe. The second waits for the first to commit, then skips. The run FK sees a committed row.

## 5. PR order

Each PR starts from `origin/main`. No stacking. Each is a migration plus code that stands alone and is safe to roll back. Each PR edits the docs of the behavior it changes, so docs never lag. `tools/check-docs.py` runs on each PR.

**PR 1. Prompt texts and run input, written by every kind.**
The table has a production caller only when a run writes to it. So the table, its owner, and the run columns ship together.
- Migration: `analysis_prompt_texts`, the five run columns, the four checks, and the two FKs on the six tables through `ConfigureRun`.
- Code: `AnalysisPromptTexts` (`IdOf`, `EnsureAsync`), `AnalysisRunInput`, `Attempt.Run`, `AnalysisRunRows.Create`, `SettleRunAsync`. Add `Effort` to `AnalysisModelRequest`, and map it to the SDK enum at `AnthropicStructuredCall.cs:43` (D3).
- `AnalysisRun` gains `Input` beside `ContextJson`. The context stays written and read. Nothing is swapped.
- Rules: `Validate()` stays the read rule, so `ToRun` loads legacy rows with a null `Input`. `ValidateForWrite()` requires `Input` for call runs. Owner: `AnalysisRunRows.Create` (section 4).
- Docs: `docs/architecture/analysis.md:198-200` says the run stores no prompt text. Rewrite it. Update the schema docs. Apply D5 to `deploy/scripts/readonly-grants.sql` if the owner chooses to hide columns.

Tests:
- New `AnalysisPromptTextsTests`: same text gives one row, role changes the key, parallel writers, insert order.
- `AnalysisRunTests`: `Validate()` accepts a null `Input`. `ValidateForWrite()` rejects it for call runs and for Bypassed and Interrupted runs that hold one.
- `AnalysisPromptHashTests`: the hash equals the stored parts.
- `AnthropicDocumentChangeDraftModelTests`: effort comes from the request.
- Per kind: `PostgresDocumentChangeAnalysisStoreTests`, `PostgresStatementGateStoreTests`, `PostgresStatementDraftStoreTests`, `PostgresBillStreamTests.ActionDrafts`, `PostgresRelationshipDraftStoreTests`, `PostgresBriefCheckStoreTests`, and the vote and brief draft store tests. Each asserts that the stored user message, system text, and schema equal the request, and that a retry run holds its own user message.
- A new test: Bypassed and Interrupted keep nulls, and a legacy row with null input loads through `ToRun`.
- A migration test modeled on `VoteRevisionColumnsMigrationTests`. `SchemaReferenceTests` regenerates the schema doc. `ArchitectureTests` stays green.
- Tests that build `new AnalysisRun(...)` compile unchanged, because `Input` defaults to null. Four Postgres helpers persist runs through `Create`, so they must pass an `Input`: `PostgresBillStreamTests.ActionDrafts.cs:999`, `PostgresBillStreamTests.ActionReview.cs:684`, `PostgresHouseVoteTests.Drafting.cs:518`, `PostgresRelationshipDraftStoreTests.cs:282`. The five in-memory builders need no edit: `FakeAnalysisStore.cs:41`, `FakeStatementDraftStore.cs:36`, `FakeStatementGateStore.cs:42`, `StatementCodeApprovalTests.cs:191`, `StatementGateCarryOverTests.cs:55`.
- `DocumentChangeDraftPromptGoldenTests` stays unchanged. Raw inserts into run tables stay valid, because every new column is nullable: `GateLabelRuns.cs:12`, `RelationshipWorldSeeder.cs:92`, `PostgresBriefCheckStoreTests.cs:188`, `PostgresBillStreamTests.ActionDrafts.cs:711`, `PostgresHouseVoteTests.Drafting.cs:466`.

**PR 2. Validation errors to child rows.**
- Migration: the six error tables. Backfill from `validation_json`, which is `text`:
  `INSERT INTO <errors> (run_id, error_number, message) SELECT r.run_id, e.ordinality::smallint, e.value FROM <runs> r CROSS JOIN LATERAL jsonb_array_elements_text(r.validation_json::jsonb) WITH ORDINALITY AS e(value, ordinality) WHERE r.validation_json IS NOT NULL`.
  Precedent: `jsonb_array_elements_text` in `20261031041526_ReviewDecisionColumns.cs:563`.
- Writer: `AnalysisRunRows.Create` plus `AddRun` add the child rows.
- Reader: the `Errors` navigation and `Include` at the nine `ToRun` call sites (section 4). `validation_json` stays written this PR, for rollback safety. It is no longer read.
- Tests: a store test per kind for a rejected run round trip, which loads its errors through the include. A migration test with legacy rows. A resume test for `AnalysisValidationLoop` that reads errors from rows. A test that `StatementGateQueueMapping.ToRun` and `PostgresStatementDraftStore.cs:53` load errors for a rejected gate run.

**PR 3. Verify command.**
A read-only check, as R-63 requires before any drop. For each run, errors rebuilt from rows equal `validation_json`. For each new call run, `AnalysisPromptHash` recomputed from stored model, effort, max tokens, system, user, and schema equals `input_hash`. A join checks that each run's two keys point to rows with the right roles (D7). Prints counts and run IDs, never text. Exits 1 on mismatch. Precedent: `votes revisions verify` (`docs/architecture/votes.md:150`).
Tests: a verify test per condition, like `PostgresHouseVoteTests.VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsTextBecauseTheTextIsPrivate`, and a command exit code test.

**PR 4. Drop `validation_json`.**
Migration drops the column after verify passes in production. Remove the property and the JSON code in `AnalysisRunRows.cs:29,41`. Down migration restores the column and rebuilds it from the child rows.
Tests: migration down and up test with rows. Schema doc regeneration. Edit `PostgresBriefCheckStoreTests.cs:319`, which asserts that the hidden pattern does not match `validation_json`. Line 318 (`output_json`) stays, since `output_json` stays. Edit `briefs.md:93`, which names `validation_json`. `briefs.md:237` names only `output_json` and stays.

**PR 5. Drop `context_json`. No verify step.**
This PR drops the column with no verify step, because the dropped fields have no rebuild source (see D1). R-63 asks for a check before a drop. The owner decides in D1.
- Migration: add `relationship_analysis_runs.input_fingerprint`, backfill it from `context_json::jsonb ->> 'fingerprint'`, and move `RelationshipStaleness.cs:58` to the column. The store read (`PostgresRelationshipDraftStore.cs:18`) and the stale-pair query (`RelationshipStaleness.cs:32-33`) call that helper and need no change.
- Code: remove `ContextJson` from `IAttempt`, `AnalysisWorker.cs:160,187,266`, `AnalysisRun.cs:34-35`, the row interface, and the column. Interrupted runs lose the special rule.
- Remove nine context lines in eight step files: `ActionDraftStep.cs:37`, `DocumentChangeDraftStep.cs:31`, `BriefCheckStep.cs:27`, `BriefDraftStep.cs:26`, `RelationshipDraftStep.cs:27,31`, `StatementDraftStep.cs:31`, `StatementGateStep.cs:28`, `VoteDraftStep.cs:34`.
- Remove nine context methods that lose their last production caller: six `SerializeContext` (action, brief check, brief draft, relationship, statement draft, vote), `SerializeCatalog`, `ToContextJson`, and `SerializeUnmatchedContext`.
- Docs: `statements.md:385,491,493,509,581` describe `context_json`. `analysis.md` too.

Tests:
- Two tests still call context methods and change: `ActionDraftPromptGoldenTests.cs:66` and `PostgresDocumentChangeAnalysisStoreTests.cs:115`.
- Remove the context rule from `AnalysisRunTests.cs:27-28`.
- Update `PostgresBillStreamTests.ActionDrafts.cs:484,503,505`, `StatementGateStepTests.cs:46`, `DocumentChangeDraftStepTests.cs:29`, `VoteDraftStepTests.cs:426`, `BriefCheckStepTests.cs:96`, and `RelationshipWorldSeeder.cs:93`.
- `PostgresRelationshipDraftStoreTests.cs`: the raw insert at `:155` names `context_json`. The update at `:511` writes it. Rewrite both to use `input_fingerprint`. A settled fingerprint reads the column.
- `ContextJson` sits in the middle of the `AnalysisRun` record. Removing it breaks every `new AnalysisRun(...)`. Edit the nine files: `FakeAnalysisStore.cs:41`, `FakeStatementDraftStore.cs:36`, `FakeStatementGateStore.cs:42`, `StatementCodeApprovalTests.cs:191`, `StatementGateCarryOverTests.cs:55`, `PostgresBillStreamTests.ActionDrafts.cs:999`, `PostgresBillStreamTests.ActionReview.cs:684`, `PostgresHouseVoteTests.Drafting.cs:518`, `PostgresRelationshipDraftStoreTests.cs:282`.

No separate docs PR. PR 1, 2, 4 and 5 all touch `AnalysisRunRows.cs`, so merge them one at a time. Roadmap says R-63 revision steps follow R-51 PR 3 and PR 4.

## 6. Open decisions for the owner

**D1. What to do with `context_json` data that is not in the user message.**
Problem: contexts hold fields the user message lacks. Examples: `fingerprint`, `summaryVersion`, `considered`, each sent brief record's kind, record ID and revision number, and `omittedNotCited` (section 1.2). The brief draft prompt names records by citation ID only. The map from citation ID to record revision lives in `context_json` and in the accepted revision. A rejected or discarded brief run loses the map when the column goes.
Options:
- A. Drop all of it. Matches the plan. Rejected runs lose the record map.
- B. Add one column where a reader exists (`input_fingerprint` on relationship runs). Drop the rest.
- C. B plus a child table of sent records for brief runs.
Cost, stated plainly: PR 5 drops `context_json` with no verify step. R-63 asks for a check before a drop. After the drop, nothing rebuilds the lost fields from the run. The user message replaces the context only in part. The owner keeps the choice: accept the loss, or add a verify step before PR 5.
Facts: action and vote fingerprints already live on their revisions (`ActionRevisionRows.cs:38`). Bypassed action and vote runs pass the fingerprint into `input_hash` (`ActionDraftStep.cs:78`, `VoteDraftStep.cs:64`). Relationship Drafted runs do not, because their `input_hash` is the prompt hash. So only relationship runs need a column.
Recommendation: B. Only the relationship fingerprint has a reader. Revisions carry exact evidence versions for anything that publishes. C builds a table with no caller. Reopen if a brief inspection command is planned.
Precedent checked: `brief_analysis_runs.input_fingerprint`, `RelationshipStaleness.cs:58`, `docs/architecture/briefs.md` flag rules.

**D2. How the table proves that text matches its key.**
Problem: `ON CONFLICT DO NOTHING` skips a row on a key clash without comparing text. A C# bug that hashes the wrong string stores wrong text under a right key. The roadmap says our data is first class.
Options:
- A. C# computes the key and a test covers it. Same as `vote_member_lists`.
- B. Add the CHECK `text_sha256 = encode(sha256(convert_to(role || E'\n' || text,'UTF8')),'hex')`. The database refuses a mismatch. This is the same recipe as section 3.1, with no version tag in either place.
- C. A verify query only (PR 3).
Recommendation: B. The cost is one SHA-256 per new text, and texts are few. C# still computes the key for the FK. The key then cannot lie. Postgres runs 18.3 (`deploy/compose.yaml:30`), so `sha256()` is available.
Precedent checked: `vote_member_lists` (A, hash of a canonical list that SQL cannot rebuild cheaply), `ck_collection_configuration_revisions_id` (format only), `document_extractions.text_sha256` (format only). Hash-of-content checks have no precedent. This is a departure to document.

**D3. Store `effort` and `max_output_tokens` on the run.**
Problem: the input hash covers them (`AnalysisPromptHash.cs:9-20`) but runs do not store them, and `effort` is hard-coded in the SDK call. Without them the stored parts cannot rebuild `input_hash`, so verify (PR 3) cannot prove the stored text is what was hashed. `Effort` is a string const on each task. The SDK takes an enum, so option B needs a mapping from string to enum at `AnthropicStructuredCall.cs:43`. All eight consts are `"medium"` today.
Options:
- A. Store neither. Verify checks only that the three texts exist.
- B. Store both, as columns. Put `Effort` on the request.
- C. Store both and `model` inside a JSON blob. Rejected, since R-63 removes JSON.
Recommendation: B. It is two small columns. It also removes the drift between the task consts and `AnthropicStructuredCall.cs:43`.
Precedent checked: `AnalysisModelRequest` fields, task `Effort` consts.

**D4. Raw output for refusal and `max_tokens` stops.**
Problem: `AnthropicStructuredCall.cs:54-56` returns text only for `end_turn`. The owner wants the exact model output. Refused, truncated, and every other non-`end_turn` run (`UnexpectedStop`) keep none.
Options:
- A. Leave it. `output_json` stays null for those stops.
- B. Return text for every stop that has text, including `UnexpectedStop`. `StopOutcome` (`AnalysisModelResponse.cs:7-13`) already decides the outcome from `StopReason` first, so this is safe. Relax nothing in `Validate` (it requires output only for Drafted, DraftDiscarded, Classified).
- C. B plus rename the column to `output_text` since it is not always JSON. Defer the rename to R-63's JSON cleanup.
Recommendation: B. It is a small change and gives the truncated answer, which is the most useful debug data. Skip C: rename costs a migration and touches every reader for no gain.
Precedent checked: `AnalysisModelResponse.StopOutcome`, `AnalysisRun.Validate` output rules.

**D5. Who may read `user_message` and prompt text.**
Problem: `readonly-grants.sql` grants every column not matching its hidden pattern. `user_message` and `analysis_prompt_texts.text` would be readable by `civic_lens_readonly`. User messages hold article text, evidence, and unreviewed model context. They are large. No query needs them from that role. The pattern is a regex on column names, so one added name hides the column in every table. Existing grant tests check per table (`ReviewDecisionColumnsMigrationTests.cs:258-268`), so a new name breaks none of them.
Options:
- A. Leave them readable. Consistent with `output_json` and `validation_json`, which `briefs.md:93` says the role reads on purpose.
- B. Hide `user_message` through the column pattern, and list `analysis_prompt_texts` under `hidden_table`. Add a test for the grants.
- C. Hide only `user_message`.
Recommendation: C. System and schema text is code-derived and helps debug. The user message carries evidence and is large. Hiding it keeps the role small. Reopen if the agent role needs it.
Precedent checked: `deploy/scripts/readonly-grants.sql` (`hidden_column`, `hidden_table`), `docs/architecture/briefs.md:93`.

**D6. Interrupted runs.**
Problem: an interrupted call may have been sent, but its request lived only in the dead worker. Section 2 gap 5.
Options:
- A. Leave null. State it in the doc. Matches the plan.
- B. Persist the request on the queue row at reservation (`ReserveAsync` `:150-154`), copy it into the Interrupted run, clear it at settle. This writes up to the prompt cap on every call, even when no crash happens.
- C. Persist only the hashes and user message hash. Does not give the text.
Recommendation: A. Interrupts are rare, the charge is recorded, and B adds a large write to the hot path of every call. The run keeps its `input_hash`.
Precedent checked: `PostgresAnalysisQueue.Interrupt`, `docs/architecture/analysis.md:200`.

**D7. One text table or two.**
Problem: the table keys by hash and carries a role. The role cannot be enforced by a single-column FK. A system prompt hash could sit in `output_schema_sha256`.
Options:
- A. One table. The role sits in the hash (D2 B checks it). A test proves that writers pass the right role. The DB does not stop a swapped FK.
- B. One table with composite FK `(sha256, role)` and a constant role column on each run table. Extra columns for little value.
- C. Two tables, `analysis_system_prompts` and `analysis_output_schemas`. The FK enforces the role. The owner chose one table.
Recommendation: A, with a verify query that joins each run's two keys to their roles (PR 3). It follows the owner's choice. Revisit C if a swap ever happens.
Precedent checked: `vote_member_lists` and entries (single table per concept), per-kind review tables.

**D8. `quotes_json` on `statement_analysis_runs`.**
Problem: it is a JSON column on a run table that option B does not mention. R-63 says own data leaves JSON.
Fact: the draft step reads it. `StatementGateCarryOver.From` reads `gateRun.Quotes` and `output_json` to carry issue IDs into the draft (`StatementGateCarryOver.cs:12,15`, `PostgresStatementDraftStore.cs:53`). A move must keep that path whole.
Options:
- A. Leave it for the statement R-63 step.
- B. Move it to a child table in this work.
Recommendation: A. It is gate output, not model input. Its readers are `StatementGateQueueMapping.cs:56` and the carry-over. Do it in its kind's step.
Precedent checked: `brief_check_flags`, R-63 text.

## 7. Risks to keep in view

- Size: user messages add raw text to six run tables. The repo holds no size figure. Postgres compresses large `text` in TOAST. Measure after PR 1 with `pg_total_relation_size` on each run table before and after one week.
- Down migrations: PR 4 and 5 drop columns. Each needs the repo's down-and-up test with legacy rows.
- `tools/check-docs.py` and `tools/render-architecture.py --check` run on each PR. The architecture diagram changes only if a project boundary moves. None does here.
- The Core project may not take a package. Hashing uses the BCL, as `AnalysisPromptHash` does in Application, so `AnalysisRunInput` in Core is allowed. If the owner prefers, put the record in Application and keep `AnalysisRun` in Core with plain string fields.
