# Analysis

## Purpose

The analysis feature runs model tasks over retained evidence through durable, budgeted queues. Today the worker drafts an account of each complete document change. Statement, action, vote, relationship, and brief tasks use the same machinery; see [statements](statements.md), [actions](actions.md), [votes](votes.md#drafting), [relationships](statements.md#relationship-drafting), and [briefs](briefs.md). A model prediction is never a human decision.

## Decisions

- Application/Analysis owns the queue rules once. Each feature supplies only its task step and its table mapping, so every queue has one owner for claims, budgets, and pauses.
- The queue owns every retry. SDK retries are off, so one rule decides backoff and attempt counts.
- An answer that fails a check is rejected once and retried once with the reasons. Code never repairs or drops an answer or a citation.
- Code verifies every citation against the extraction text. Citations therefore carry no model confidence.
- Model confidence values are raw and uncalibrated. They drive no routing, and the review UI does not show them as a score. They wait for later calibration against human review.
- Confidence stays in the run output, never in a revision. A revision is a human-editable public record.
- A prompt over 100,000 UTF-8 bytes or a comparison over 64 hunks is blocked, not truncated. A person drafts it by hand.
- The token reservation is the prompt byte count plus the output allowance. A byte-level tokenizer gives at most one token for each byte, so this is an upper bound.
- The byte bound also keeps each prompt in the lower Haiku 5.5 price tier.
- A call that may have been billed stays fully charged. A crash or a lost connection therefore never hides spend.
- AI drafts use the reserved author subject `civic-lens:analysis`. That subject can never decide, so an AI author can never approve its own draft.
- Code approves a worker revision of an action or a vote under the reserved subject `civic-lens:code`, after it checks the citations. This is a code decision and not a model decision. No other task gets a code approval.
- An AI-proposed change date needs explicit human confirmation. A published record never carries an unconfirmed AI date.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- A queue entry has a lease token if and only if it is Running. Enforced in both directions by `ck_<table>_running` on every queue table: `(status = 'Running') = (lease_token IS NOT NULL)`. No test.
- A move matches the subject, task, status, lease token, and fence. Enforced by `AnalysisLifecycle`. Test: `DocumentChangeDraftLifecycleTests.AStaleLeaseFenceOrStatusCannotMoveTheEntry`.
- Each target status carries only its own outcome. Enforced by `AnalysisLifecycle`. Test: `DocumentChangeDraftLifecycleTests.EachTargetCarriesOnlyItsOwnOutcome`.
- An entry gets at most eight attempts. Enforced by `PostgresAnalysisQueue` with `AnalysisPolicy`. Test: `PostgresDocumentChangeAnalysisStoreTests.TheClaimFailsAnEntryThatUsedAllItsAttemptsWithoutACall`.
- The prompt officials are the officials that the source covers in the stored configuration revision of the after job, on the later observation date. Enforced by `StatementSpeakerRoster.OfficialsOn` through `DocumentChangeDraftStep`. Test: `DocumentChangeDraftStepTests.OnlyTheOfficialsThatTheSourceCoversOnTheLaterObservationDateReachThePromptAndTheRunContext`.
- Charged tokens never pass the daily limit. Enforced by `PostgresAnalysisQueue`, which locks the budget row. Test: `PostgresDocumentChangeAnalysisStoreTests.ConcurrentReservationsNeverPassTheDailyLimit`.
- The claim scan takes the lowest priority number first, then the oldest entry, then the key. Enforced by `PostgresAnalysisQueue.GetEligibleAsync` and the index `ix_<table>_active_claim`. Test: `PostgresStatementGateStoreTests.GateClaimsPreferAHigherPriorityOverAnOlderLowerPriorityRowSoTheReviewerSeesTheMostUsefulDraftsFirst`.
- An entry has tier 1 if its record would pair with a statement. It has tier 2 if the record has a tracked issue, else tier 3. Enforced by `ReviewPriority.Of`. The bill number match is in `RelationshipPairRule`. `PairPriority` applies the pair rule to saved drafts. Tests: `ReviewPriorityTests.AWouldPairRecordOutranksATrackedIssueAndATrackedIssueOutranksTheRest`, `PostgresBillStreamTests.TheExactTierLowersAnEarlyTierOneWhenNoStatementOfASponsorPairs`, and `PostgresHouseVoteTests.TheExactTierLowersAnEarlyTierOneWhenNoMemberOfTheVoteIsTheSpeaker`.
- A running entry keeps its priority. Enforced by `AnalysisPriority`, which skips Running entries. Test: `PostgresBillStreamTests.ARunningRowKeepsItsPriorityWhenANewStatementNamesItsBill`.
- A reservation is set or cleared as a whole. Enforced by `ck_<table>_reservation` on every queue table. No test.
- A run charges at least the tokens it used. Enforced by `ck_document_change_analysis_runs_tokens` and `AnalysisRun`. Test: `AnalysisRunTests.ARunCannotChargeLessThanTheTokensItUsed`.
- At most one run exists for each draft revision. Enforced by the unique index `IX_document_change_analysis_runs_draft_id_revision_number` on `(draft_id, revision_number)`. A run without a draft has a null `draft_id`. No test.
- Only a human owner requeues. Enforced by `RequeueAnalyses`, `RequeueBlockedActionDrafts`, `RequeueBlockedVoteDrafts`, and `RedraftBrief`. Tests: `RequeueAnalysesTests.OnlyAHumanOwnerRequeues`, `ActionCommandTests.TheAiDrafterCannotActAsTheOwner`, `VoteCommandTests.TheAiDrafterCannotActAsTheOwner`, `RelationshipCommandTests.TheAiDrafterCannotActAsTheOwner`, and `RedraftBriefTests.OnlyTheOwnerMayRedraftAWeek`.

## Behavior

### Queue and lifecycle

The shared owners are generic over `TRun`, the run type that a feature stores: `AnalysisWorker<TRun>`, `AnalysisValidationLoop<TRun, TAccepted>`, `IAnalysisQueue<TRun>`, and `IAnalysisTaskStep<TRun>`. `PostgresAnalysisStore<TRun>` is the public face of the Postgres queue.

`PostgresDocumentComparisonStore` adds a `draft` entry to `document_change_analysis` for each complete comparison with at least one hunk. It does this in the transaction that saves the comparison, from the worker or the CLI. A repeat save keeps one entry. There is no classification gate yet.

`AnalysisLifecycle` owns the valid moves for every analysis feature. A feature gives it three rules. The first says which `Succeeded` outcomes are valid. The others say whether a `Succeeded` or a `Blocked` entry may return to Pending. `DocumentChangeDraftLifecycle` adds the revision rule. `PostgresAnalysisQueue` applies every status write through the lifecycle of its feature, under a row lock on the entry.

The states are the values of `ck_<table>_status`, the same for every queue table. A move must match the status, lease token, and fence of the entry. A move out of Running needs a current claim: status Running and a lease. Each target carries only its own outcome. `Succeeded` needs a revision for the same comparison, or the code `draftDiscarded` or `humanDraftExists`.

| From | To | Write |
|---|---|---|
| Pending, RetryWaiting, WaitingForBudget | Running | Claim of due work |
| Running | Running | Claim after an expired lease, or a validation retry inside a claim |
| Running | Pending | Release on shutdown, or an account-wide provider pause |
| Running | RetryWaiting | Durable retry with a bounded delay |
| Running | WaitingForBudget | Daily limit reached |
| Running | Succeeded, Blocked, Failed | Settlement |
| WaitingForBudget | Pending | Budget wait release at worker startup |
| Failed | Pending | Owner requeue (`analyses requeue`) |
| Succeeded | Pending | Only where the feature lifecycle allows it: a skipped statement gate (`statements regate`), any brief draft, and a relationship draft whose pair has a new input |
| Blocked | Pending | Only where the feature lifecycle allows it: an action draft (`actions draft requeue --blocked`), a vote draft (`votes draft requeue --blocked`), a brief draft (`brief redraft`), and a relationship draft that was blocked by an unapproved side |

These events count as attempts: a durable retry, a reclaim of an expired lease, a release with tokens reserved, and a rejected draft. The claim owns the limit. A claim of an entry with eight attempts fails it as `attemptsExhausted` with no call.

`IAnalysisQueueStore<TRun, TView, TItem>` adds a run read, a list for each view, and the counts to `IAnalysisQueue<TRun>`. The owner handlers `GetAnalysisStatus`, `ListAnalyses`, and `GetAnalysisRun` work on any such store. The statement gate, statement drafting, action drafting, vote drafting, and relationship drafting use them. The Host puts them with `RequeueAnalyses` in one `AnalysisQueueCommands` for each queue. Document-change drafting keeps its own read handlers. They serve the review UI, need the reviewer role, and find entries by comparison or draft ID.

### Executor

The drafting executor is `AnalysisWorker<AnalysisRun>` with `DocumentChangeDraftStep`. The worker owns the drain loop, the claim, and lease renewal. It also owns the daily budget check, token reservation and settlement, the retry delay, the pauses, and release on shutdown. The step owns only its evidence read, its prompt, its model call, and the resolution of the answer. The worker gives the step an `IAnalysisAttempt<TRun>` for the reserve-and-call and settle operations.

The executor runs beside the collection loop in the `worker` process. Its wakeup uses the `civic_lens_analysis` channel with the payload `document_change_analysis:draft`. Collection therefore never waits for a model call. With `--once`, collection drains first and drafting drains next. If one loop fails, both stop.

`AnalysisWorker` takes an optional hook that runs before each drain pass, inside the same error handling as the eligible read. The brief executor uses it to queue the closed weeks. No other executor sets it. The relationship executor finds its pairs in the review decision hooks instead.

At most `CIVIC_LENS_ANALYSIS_CONCURRENCY` calls run at a time. The default is 2 and the maximum is 4. On shutdown the executor releases claimed entries.

An unexpected error before a reservation blocks a bad input or retries other errors. An unexpected error after a reservation fails the entry with a `ProcessingFailed` run that keeps the charge.

### Validation loop

`AnalysisValidationLoop` owns the rule "reject once, retry once with the errors". A step gives it a prompt builder, a resolver, and a way to read the earlier rejected run. The step also gives a function that wraps each run that the loop saves. The loop makes the call, checks the stop reason, and calls the resolver. It saves a rejected answer as a rejected run and calls again with the rejection reasons. The queue keeps the ID of the rejected run, so a restart or a budget wait resumes the second call.

The loop returns one of four ends:

- Settled: a stop, a failure, or a lost lease that the loop already handled.
- Accepted.
- Exhausted: a second rejection, or a retry prompt over the cap.
- Not sent: the first prompt is over the cap, or the resumed retry prompt is.

Every task checks its text fields with `AnalysisAnswerRules.CheckText`. A text over its bound is rejected with its actual length, so the retry can shorten it. A required blank text gets only the required message.

The step decides what each end means. The drafting steps for document changes, statements, actions, votes, relationships, and briefs call `AnalysisValidationLoop.FailAsync`, which fails the entry with the rejection code. The statement gate fails open.

### Prompt and output

The prompt holds the source and both observation times. It gives each hunk an ID (`h1` and so on) with its before window, after window, and changed text. It also holds the issue IDs and the officials that the source covers (ID and name). Coverage comes from the configuration revision of the job that collected the later version, on the date of the later observation. `StatementSpeakerRoster` owns that rule, as for statement drafting. A comparison with no stored revision lists no officials. The evidence read loads the revision in the same transaction. Source text is marked as untrusted data. The request uses model `claude-haiku-5-5`, effort `medium`, and at most 16,000 output tokens.

Structured outputs fix these fields:

- headline, summary, significance, limits, and institution
- change date, with a citation index and a confidence
- official IDs and issue IDs, each an ID with a confidence
- citations, each a hunk ID, a side (`before` or `after`), and an exact quote
- a confidence for the whole account

Schema enums limit the hunk, official, and issue IDs. The schema sets no numeric bounds, because structured outputs do not support `minimum` or `maximum`. Code rejects an unlisted ID. It also rejects a confidence that is not finite or is outside 0 to 1. Code checks the stop reason before it reads content. A golden test pins the system prompt, the schema, and one input hash to the prompt and schema versions.

Confidence meanings:

| Confidence | Probability that |
|---|---|
| Account | a careful reviewer approves the account with no change of substance |
| ID | the ID belongs on the record |
| Date | the date is the date of the change |

### Citation checks

Code looks for each quote in its named window, and the quote must occur exactly once. Code then computes UTF-16 offsets into the extraction and checks the span against the extraction text. A missing, ambiguous, or misplaced quote rejects the whole draft.

A change date must also appear in its cited quote, in one of these forms: `2026-01-05`, `January 5, 2026`, `Jan 5, 2026`, `Jan. 5, 2026`, `5 January 2026`, or `1/5/2026`. A rejected draft gets one more call with the reasons. A second rejection fails the entry with the code of that rejection.

### Budget

`document_change_analysis_budget` holds the charged tokens for each UTC day of database time. The call cap is the smaller of 120,000 tokens and the daily limit. One call never reserves more than the call cap. Startup rejects a daily limit below the smallest reservation, which is the system prompt bytes plus the output allowance.

1. Before it reads evidence, the executor checks that the day can still hold the smallest reservation. If not, the entry waits and the drain pass stops.
2. Before a call, the executor reserves its tokens under a row lock on that day. If they do not fit, the entry waits as WaitingForBudget until the next UTC midnight, and the pass stops.
3. After the call, settlement changes the reservation to the actual charge: input, output, cache read, and cache write tokens.

A rate limit, a provider error, or a rejected request charges nothing. A connection failure, a crash, or a shutdown during a call keeps the full reservation charged. Worker startup returns budget waits to Pending, so a raised limit takes effect on restart.

### Retries and pauses

A rate limit or a single provider or connection failure retries durably. The delay starts at 30 seconds, doubles, and stops at 30 minutes. The SDK exceptions do not expose the `retry-after` header, so a 429 also uses this backoff. A 408 or 409 counts as a provider failure. Other 4xx responses fail the entry at once.

A refusal, a `max_tokens` stop, and any other stop fail the entry at once. A response that arrives but cannot be read fails the entry as `invalidResponse`. It keeps the reported token use, or the full reservation when the use is unknown. It does not count toward an outage.

Account-wide errors pause the whole executor. The entry returns to Pending and loses no attempt.

- A 401, 402, 403, or 404 pauses drafting until the worker restarts. The operator first fixes the key, the credit, or the model access.
- Three provider or connection failures in a row pause drafting for 5 minutes. The pause doubles up to 1 hour, and then the executor tries the provider again.
- The first successful call after an outage clears the pause and resets the doubling.
- An outage pause never replaces a pause that waits for a restart.

`AnalysisStatus` owns this rule (`IsHardPaused`, `IsTimedPaused`). `PostgresAnalysisStatusStore` locks the status row with `FOR UPDATE`, applies the rule in C#, and writes. A restart or a second pause therefore cannot slip between the check and the write.

### Draft and authorship

A drafted settlement takes the row lock of the entry and then the review lock. One transaction adds the draft and revision 1, the run with its draft link, the queue settlement, and the budget change. Revision 1 uses the author subject `civic-lens:analysis`. The run row links to that exact revision.

The worker checks for a draft before the call and again at commit. If a person started a draft during the call, the run stays as `DraftDiscarded` and no second draft is added.

Human paths, the review store, the publication store, and collection workspace actions reject the reserved subject as an actor. Review settings reject it as an owner or reviewer.

A person can approve revision 1 unchanged, unless it holds an AI-proposed change date. The editor shows a Date checked box for that date. A save without the tick removes the date and its evidence. The review store applies this rule inside its locked save, so a request that skips the box cannot keep the date. Approval rejects an AI revision with a change date.

### Review UI

Each new change shows its drafting state:

- Queued or Drafting
- Retry at a time, or Waiting for budget until a time
- Too large for AI
- the failure reason

`IAnalysisStatusStore` is a small port. The worker writes it and the review UI reads it. At startup the worker writes one `document_change_analysis_status` row with the daily token limit and the time. A null limit means drafting is off. A pause adds its reason and, for an outage, its end time. A restart clears the pause.

The New changes panel shows the drafting status. It is on with its limit, off, paused with the reason and action, or unknown before a worker starts. An outage pause whose end time has passed shows as not paused. The review host never reads the environment of the worker.

The editor labels an AI revision and flags an unverified AI change date. Its technical details show the model, prompt version, run ID, and token use. A saved citation that is smaller than a whole changed passage stays a selectable option. A human save therefore keeps the AI quotes.

### Credentials

The worker drafts only when `CIVIC_LENS_ANALYSIS_DAILY_TOKENS` and `ANTHROPIC_API_KEY` are both set. A limit without the key is a configuration error. The key alone does not turn drafting on, because the other AI stages share it. The adapter takes the key from the Host and never reads the environment for it. It sets the production API address and no auth token. A test checks that a request goes to that address without an Authorization header.

The collector child process receives only an allowlist of environment variables: `PATH`, `HOME`, `TMPDIR`, `LANG`, `LC_*`, `DOTNET_ROOT`, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT`, `SSL_CERT_FILE`, `SSL_CERT_DIR`, and the proxy variables.

Ordinary tests use a fake model at the port. One live smoke test runs only with `CIVIC_LENS_LIVE_AI=1`.

## Storage and contracts

- The generated [analysis schema](schema/analysis.md) lists every table, column, key, check, index, and trigger.
- `document_change_analysis` has one entry for each comparison and task. The `civic_lens_analysis` notify trigger on the table wakes the worker. Every analysis queue table has this trigger. Test: `PostgresAnalysisWakeupTests.EveryAnalysisQueueTableWakesItsListenersThroughTheNotifyTrigger`.
- Only analysis runs keep the task version.
- `AnalysisTableConfiguration.ConfigureQueue` gives every queue table the partial claim index `ix_<table>_active_claim`. It covers `(task, priority, created_at_utc_ticks, key)` for Pending, RetryWaiting, WaitingForBudget, and Running entries, in the order of the eligible scan. The `(status, created_at_utc_ticks, key)` index stays for requeue and the review lists, which filter one finished status.
- Every queue table has `priority`, a smallint from 1 to 3 with default 3. `ReviewPriority.Of` owns the three tiers. Tier 1 means the record would pair with a statement. Tier 2 means the record has a tracked issue. Tier 3 is the rest. The priority does not use the event date.
- Statement gate and draft entries are tier 1. An action or vote entry gets its first tier when it is queued. It is tier 1 when a drafted statement names its bill number, or a new statement candidate names it. Else it is tier 2 when `bill_gates.has_tracked_issue` is true, and tier 3 if not. A vote with no bill is tier 3.
- The first tier does not check the speaker. A statement that names the bill may come from a person who is not a sponsor. The bill number comes from the `billNumbers` of the current statement revisions. A new candidate uses `BillNumberFinder` on its article text. The stored path uses `statement_candidates.created_at_utc_ticks` as the capture date. The new candidate path uses the observed time of its attempt. Both use the capture date to find the Congress when the statement date is unknown.
- A saved action or vote draft gets its exact tier. `PairPriority` finds the pairs over the latest revisions with no approval filter. The entry is tier 1 only if a pair exists. A saved statement draft raises the entries of the actions and votes that pair with it. A raise never lowers a tier. `AnalysisPriority` locks the rows in key order and skips Running entries.
- A person who edits a draft does not refresh the tier. The next draft of the record does.
- `document_change_analysis`, `relationship_analysis`, and `brief_analysis` keep the default tier. Their order does not change.
- `document_change_analysis_runs` keeps one insert-only row for each model call. It holds the versions, the input hash, the token use, the output, and the catalog copy of the officials and issues that the prompt listed.
- The run does not store prompt text. The versioned prompt in code rebuilds it from the retained comparison and the catalog copy. The input hash proves the rebuild.
- A run recovered after a crash or shutdown is `Interrupted`. It keeps its real start time, has no catalog copy, and uses the current task constants as its versions.
- `document_change_analysis_budget` holds charged tokens for each UTC day.
- `document_change_analysis_status` holds one row for each task with its limit, time, and pause state. The statement tasks also use this table.
- The code of a run outcome is its name in camel case.
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Run and outcomes | `src/CivicLens.Core/Analysis/AnalysisRun.cs`, `src/CivicLens.Core/Analysis/AnalysisRunOutcome.cs` |
| Shared queue rules | `src/CivicLens.Application/Analysis/AnalysisWorker.cs`, `src/CivicLens.Application/Analysis/AnalysisLifecycle.cs`, `src/CivicLens.Application/Analysis/AnalysisValidationLoop.cs`, `src/CivicLens.Application/Analysis/AnalysisStatus.cs`, `src/CivicLens.Application/Analysis/AnalysisPolicy.cs` |
| Shared answer and schema rules | `src/CivicLens.Application/Analysis/AnalysisAnswerRules.cs`, `src/CivicLens.Application/Analysis/AnalysisSchema.cs` |
| Ports | `src/CivicLens.Application/Analysis/IAnalysisQueue.cs`, `src/CivicLens.Application/Analysis/IAnalysisQueueStore.cs`, `src/CivicLens.Application/Analysis/IAnalysisTaskStep.cs`, `src/CivicLens.Application/Analysis/IAnalysisAttempt.cs`, `src/CivicLens.Application/Analysis/IAnalysisStatusStore.cs` |
| Document-change drafting | `src/CivicLens.Application/Analysis/DocumentChangeDraftStep.cs`, `src/CivicLens.Application/Analysis/DocumentChangeDraftTask.cs`, `src/CivicLens.Application/Analysis/DocumentChangeDraftPrompt.cs`, `src/CivicLens.Application/Analysis/DocumentChangeDraftResolver.cs`, `src/CivicLens.Application/Analysis/DocumentChangeDraftLifecycle.cs` |
| Owner queue handlers | `src/CivicLens.Application/Analysis/GetAnalysisStatus.cs`, `src/CivicLens.Application/Analysis/ListAnalyses.cs`, `src/CivicLens.Application/Analysis/GetAnalysisRun.cs`, `src/CivicLens.Application/Analysis/RequeueAnalyses.cs`, `src/CivicLens.Host/Analysis/AnalysisQueueCommands.cs` |
| Postgres queue | `src/CivicLens.Infrastructure/Analysis/PostgresAnalysisQueue.cs`, `src/CivicLens.Infrastructure/Analysis/PostgresAnalysisStore.cs`, `src/CivicLens.Infrastructure/Analysis/PostgresAnalysisStatusStore.cs`, `src/CivicLens.Infrastructure/Analysis/PostgresAnalysisWakeup.cs`, `src/CivicLens.Infrastructure/Analysis/Persistence/AnalysisTableConfiguration.cs`, `src/CivicLens.Infrastructure/Analysis/AnalysisRunRows.cs` |
| Document-change store and model | `src/CivicLens.Infrastructure/Analysis/PostgresDocumentChangeAnalysisStore.cs`, `src/CivicLens.Infrastructure/Analysis/DocumentChangeAnalysisQueueMapping.cs`, `src/CivicLens.Infrastructure/Analysis/AnthropicDocumentChangeDraftModel.cs`, `src/CivicLens.Infrastructure/Analysis/AnthropicStructuredCall.cs` |
| Other shared types | `AnalysisState`, `AnalysisRecord`, `AnalysisClaim`, `AnalysisCheckpoint`, `AnalysisSettings`, `AnalysisErrorCodes`, `AnalysisTaskIdentity`, `AnalysisPromptHash`, `AnalysisModelRequest`, `AnalysisModelResponse`, and `AnalysisModelException`, each in its own file in `src/CivicLens.Application/Analysis/` |
| Document-change parts | `DocumentChangeAnalysisRecord` adds the draft ID, and `DocumentChangeDraftCheckpoint` adds the revision. `DocumentChangeDraftErrorCodes`, `DocumentChangeDraftSettings`, `DocumentChangeDraftCatalog`, and `DocumentChangeDraftEvidence` (with the configuration revision) complete the feature. |
| Review priority | `src/CivicLens.Core/Review/ReviewPriority.cs`, `src/CivicLens.Infrastructure/Analysis/AnalysisPriority.cs`, `src/CivicLens.Infrastructure/Relationships/PairPriority.cs`, `src/CivicLens.Infrastructure/Statements/StatementBillMentions.cs`, `src/CivicLens.Application/Relationships/RelationshipPairRule.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipRevisionScope.cs` |
| Reserved author | `src/CivicLens.Core/Review/ReviewAuthor.cs` |
| CLI and composition | `src/CivicLens.Host/Analysis/AnalysisCommand.cs`, `src/CivicLens.Host/Analysis/DocumentChangeDraftComposition.cs` |
| Shared Host setup | `src/CivicLens.Host/Analysis/AnalysisEnvironment.cs`, `src/CivicLens.Host/Analysis/AnalysisComposition.cs` |

The shared wakeup and lease mechanics are in [Shared worker mechanics](../architecture.md#shared-worker-mechanics).

## Limits and planned work

The [roadmap](../roadmap.md) holds the delivery order. These design rules apply to the planned work:

- A concurrent statement registration and action enqueue can miss a raise. The row then waits until a statement draft saves.
- The exact tier of a saved draft changes the claim order only after a requeue.
- A change to the tracked issues does not refresh `has_tracked_issue` or the waiting tiers. No command recomputes them yet.
- The early mention check scans the current statement revisions on each enqueue. On the dev database it took about 5 ms with about 244 statement drafts. A mention table is the planned fix if statement volume grows.
- Rules from measured change history gate document changes. A model tier follows only if the R-13 measurement shows a need (D-05). A change classified as nonsubstantive skips review. Routing supports process, defer, and skip-expensive-analysis, with reasons and retained input references.
- Skipped changes stay visible and inspectable, and a reviewer can return one to review. Audit a sample of skipped records.
- Structured actions bypass relevance models. Explicitly watched documents keep their versions whatever the relevance decision.
- Humans give classification labels under a written, versioned rubric of narrow typed questions. Labels are separate from editorial approval, and model output is never a label.
- The classifier makes decisions only. It does not extract quotations or write briefs.
- Classification is a separate stage in the same queue, with its own task versions, budgets, and escalation policy.
- The planned ladder is deterministic rules, then a cheap probability tier, then a stronger model, then a person. The model tiers depend on the R-13 measurement. A bake-off selects the models and thresholds for each tier.
- Deterministic rules include learned churn regions. A two-state Markov chain (stable or churning) over check history models each page region. Recurring churn, such as timestamps or rotating widgets, then needs no hand-written profile for each source.
- When the engine is unsure, the change goes to review. The engine never drops a change silently.
- A version bump has no selective rerun yet (R-39). `analyses requeue` moves Failed entries only, and `statements regate` covers the gate only.
- The rubric asks narrow typed questions. Examples: navigation or boilerplate, date or timestamp only, a changed number, obligation, named person, or policy claim. Others: removed content and touched configured issues.
- A label binds to the exact comparison, the rubric version, and the labeler. Model output, also agreement between models, is a prediction, never a label.
- Labels from the review queue are biased by the gate. False-negative estimates therefore use the random sample of skipped changes, weighted by its selection rate.
- Measure agreement between the two reviewers on an overlapping sample.
- Self-hosted encoders are out of scope for now, because of the CPX21 memory limit.
- A confidence is shown as a score only after a calibration map for that task exists. The map is fitted on human outcomes and versioned. Code-checked results are verified or failed, not scored.
- A labeled eval set follows the rubric. A prompt or model change runs against it on demand, within a budget, and ships only without a regression.
- Weekly briefs reuse the same adapter and the same checks. Statement and action extraction will reuse them too.
- The document-change gate starts with measurement, after the first cloud deployment. Most observed source change is new press releases, not in-place edits. On 2026-10-16 the dev database held no document change: 361 pages checked again minutes later had the same bytes. That data cannot show change rates over days. Our own check history produces the in-place edits to measure and label.
- Model tiers are added only if the changes that pass the rules are too many for people to review.

Classification first steps:

1. [ ] After the first cloud deployment, check the House press index pages and about 300 press release URLs daily for 2 to 4 weeks. Count byte, text, and line changes for each source.
2. [ ] Write the labeling rubric, and label about 100 changes from that check history.
3. [ ] Implement rules for the churn found, and deterministic change features: share of text changed, longest change, and configured issue terms added or removed. Also link changes (with a removed self-link) and HTTP status or redirect changes.
4. [ ] Do this step only if the changes that pass the rules are too many for people to review. Run a budgeted bake-off on the labeled set: logistic regression over the features, Cloudflare Clef-flash and Clef, and Haiku 5.5. Then set the ladder tiers and thresholds from the results.

Classification acceptance:

- [ ] An AI draft of a real substantive edit passes the citation checks, and a person approves and publishes it.
- [ ] The false negatives of the gate are measured on held-out labeled examples and visible in the skipped-change sample.
- [x] Budget exhaustion leaves a visible, resumable backlog.
- [ ] A change of one task version reruns only the affected work.

Pending decisions for the ladder are [D-05](../roadmap.md#pending-decisions).

- Model extraction is planned. Extraction returns validated source evidence.
- The worker drafts action records, vote records, and weekly briefs as proposed revisions. Code approves an action or vote revision after it checks the citations; see [review](review.md#code-approval). A brief still needs a human approval. Each published record is bound to its exact approved revision.
- Match statements and actions on identifiers, people, dates, jurisdiction, and issue candidates before any model comparison. Relationship assessments need editorial context.
- Briefs use bounded evidence selections and citation IDs; see [briefs](briefs.md#selection).
- Do not silently truncate long documents. Record chunk coverage and extraction failures.
- The data model will separate observations, document versions, changes, statements, actions, proposed relationships, analysis runs, reviews, briefs, and releases. Evidence points to exact versioned text spans or page locations.
- Event time, publisher time, observed time, and last-checked time stay distinct.
- Cache keys include input hashes, task, model, and schema versions, and the relevant configuration and context.
- Store model responses and provider metadata, because hosted aliases can change. Enforce limits for each task and each run. Budget exhaustion leaves visible pending work.
- Measure false negatives, precision, calibration, latency, downstream savings, and review workload on held-out examples. Group examples so that document versions do not leak across splits.
- Planned ([D-13](../roadmap.md#pending-decisions)): a benchmark of classifiers for the statement issue gate, with a Clef model fine-tuned on our labels. The gate is the costliest stage for each item, about 7,800 charged tokens for each call on 2026-10-16.
- Planned dataset work will curate validation sets, golden sets, and training or fine-tuning datasets from retained evidence and attributed editorial history. Clef has open weights, so fine-tuning on our labels is possible.
- Publication approval and dataset acceptance are separate judgments.
- Dataset exports, task labels, eligibility rules, source-use permissions, provenance manifests, correction handling, and leakage-resistant splits are not designed yet. No dataset export or training workflow exists. Agree on them before any dataset export.
- Planned: budgets and cache keys for each task, decision explanations, skipped-document sampling, and a local evaluation MCP. Ordinary CI never calls paid APIs.
