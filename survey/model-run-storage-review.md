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
