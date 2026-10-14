# Actions

## Purpose

The actions feature drafts one record for each bill that has an [action candidate](bills.md#action-candidate). A record lists what every tracked official did with the bill: sponsoring it, cosponsoring it, and withdrawing a cosponsorship. A person reviews and approves each record later. A model prediction is never a human decision.

## Decisions

- One action record covers one bill and lists every tracked official's sponsorship. The bill's bioguide IDs match configured people, and the record shows the configured name.
- The first action slice is bill sponsorship and cosponsorship. Member records are not captured in this slice, because Congress.gov member terms give years only.
- A withdrawn cosponsorship stays in the record, with both dates cited.
- A new revision follows only a change in the compared facts. An update to the actions of a bill alone adds no revision.
- CRS bill summaries are optional cited evidence. A record must work without one, because CRS often publishes weeks after introduction. Drafting does not wait for a summary.
- The record cites the newest summary by action date. A new summary version makes a new revision.
- Code builds every citation from the saved extraction lines. The model writes none.
- The model writes only the headline and the summary. The structured part comes from code, and a person can never edit it.
- Text that a person wrote survives later model runs. The model text then becomes a suggestion, so the worker never overwrites a person's wording.
- Each record states the date of its evidence. `ActionDraftRevision.CandidateUpdateDate` holds the update date of the bill version.
- Approval binds to the revision number, so every new revision needs a new review.
- The newest candidate of a bill wins. Candidates are ordered by update date, then by whether they include summaries.
- A prompt over the byte cap drops the summary text, so the model drafts from the structured facts alone.
- A bill with more than 20 summary pages drafts without its summaries. The step never reads those pages.
- The step verifies saved extractions against current versions before it reads any capture. A version upgrade and changed saved text therefore get different codes.
- Actions have their own review lock key, `civic-lens-action-review`, last in the publication lock order.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- One draft exists for each bill. Enforced by `ux_action_drafts_bill`. Test: `PostgresBillStreamTests.TheDatabaseAllowsOneDraftForEachBillAndRefusesABadRecordId`.
- Only an analysis revision has a run ID, and one revision exists for each run. Enforced by `ck_action_revisions_run` and `IX_action_revisions_run_id`. Test: `PostgresBillStreamTests.TheDatabaseRefusesAnAnalysisRevisionWithoutARunAndAPersonRevisionWithOne`.
- An append needs the expected current revision. Enforced by `ActionDraftRows` under the `PostgresAdvisoryLock.ActionReview` key. Test: `PostgresBillStreamTests.APersonsConcurrentRevisionFailsTheExpectedVersionCheckSoTheRunRetriesWithBackoffAndAppendsNothing`.
- A new revision needs a new fingerprint or summary version. Enforced by `ActionDraftingStep`. Test: `ActionDraftingStepTests.AMatchingFingerprintAndSummaryVersionSettleWithoutAModelCall`.
- The text of a person stays, and model text becomes a suggestion. Enforced by `ActionDraftRevision`. Test: `ActionDraftRevisionTests.ARevisionAfterAPersonsTextMustKeepThatTextAndOfferTheModelsAsASuggestion`.
- A person edits only the headline and the summary. Enforced by `ActionDraftRevision`. Test: `ActionDraftRevisionTests.ARecordReviewerEditsOnlyTheHeadlineAndTheSummary`.
- An older candidate never becomes the newest revision. Enforced by `ActionDraftRows.HasNewerCandidateAsync`, checked when the step reads its evidence and again at the append. Tests: `PostgresBillStreamTests.ADelayedOlderCandidateIsSupersededSoItsStaleContentNeverBecomesTheNewestRevision`, `PostgresBillStreamTests.ANewerCandidateThatArrivesAfterTheClaimSupersedesTheAppendSoTheOlderDraftNeverBecomesARevision`, and `PostgresBillStreamTests.ALaterUpdateDateWithoutSummariesSupersedesAnEarlierCandidateWithSummariesBecauseTheDateDecidesFirst`.
- More than 20 summary pages never block a bill; it drafts without them. Enforced by `ActionDraftingStep`. Test: `ActionDraftingStepTests.MoreThanTwentySummaryPagesDraftWithoutSummariesAndNeverReadThemBecauseSummariesAreOptionalInput`.
- An owner requeue of Blocked drafts moves only Blocked drafts with exactly the given code, and only for actions. Enforced by `PostgresAnalysisQueue.RequeueAsync` and `ActionDraftLifecycle`. Tests: `PostgresBillStreamTests.RequeueBlockedMovesOnlyBlockedEntriesWithTheGivenCodeAndResetsTheirAttempts` and `ActionDraftLifecycleTests.OtherTasksKeepBlockedFinalBecauseOnlyActionsTurnTheOptionOn`.
- Model confidence never reaches a revision. Enforced by `ActionDraftingStep`. Test: `ActionDraftingStepTests.ConfidenceStaysInTheRunOutputAndNeverReachesTheRevision`.

## Behavior

### Ownership

The step runs on the shared analysis owners: `AnalysisWorker<TRun>`, `AnalysisValidationLoop`, `PostgresAnalysisQueue`, `AnalysisAnswerRules`, `AnalysisSchema`, `AnthropicStructuredCall`, and `AnalysisRunRows`. See [analysis](analysis.md#behavior) for the queue lifecycle, budgets, retries, and pauses.

`BillGate.TrackedIssueIdsFor` maps a policy area to the issues that are active on a day. The bill gate and drafting both call it. `CollectionConfiguration.CoveredBioguidePeopleOf` lists the covered people who have a bioguide ID. `ApiResponseReader` reads the saved capture of a job, and `JobPreparation` shares it.

The store port `IActionDraftStore` extends `IStatementQueueStore`. The owner commands therefore use the same generic handlers as statements.

### Record identity

The record ID is the SHA-256 of the source and the bill (congress, type, number), length framed like the candidate ID. It has no update date. Each candidate is one bill version, and a candidate finds its record by computing the same hash. `action_drafts` has one row for each record.

### Queue

`action_analysis` has one entry for each candidate, with the task `action-draft`. The entry is added in the transaction that writes the candidate. Late summaries make a new candidate, so no entry is ever reopened. The evidence of a candidate without summaries never includes summaries pages, even after they arrive.

The worker claims due entries oldest first, like the other analysis tasks. `ActionDraftRows.HasNewerCandidateAsync` owns the newest-wins rule, and the index `ix_action_candidates_bill` serves it. A candidate with summaries is newer than one without them on the same update date. The budget is `action_analysis_budget`. The notify trigger on the queue table only wakes the worker.

When summaries arrive after the older candidate drafted, the bill version gets a second candidate and a second call.

### Step

The step does these checks in order. None of them calls the model.

1. No evidence: Blocked as `evidenceUnavailable`.
2. The bill has a newer candidate: Succeeded as `supersededByNewerCandidate`.
3. More than 20 summary pages: the step drops the summary pages and does not read them. The bill drafts without summaries. Each run has the reason `summaryOmitted`, and the revision has no summary version and no summary citation.
4. A saved extraction has other parser or normalization versions than the current parser, extractor, and renderer would write: Blocked as `extractionVersionChanged`. This check runs before any capture is read or parsed.
5. The step reads the detail page, each cosponsor page, and each summary page. It parses each capture with the preparation parser and renders it with `CongressBillRenderer`. Any difference from the saved extraction text: Blocked as `renderedTextMismatch`, which means the saved text changed.
6. No tracked official on the bill: Succeeded as `noTrackedOfficial`, with no revision. More than 600 sponsorships: Blocked as `tooManySponsorships`. A bill title over 4,000 characters: Blocked as `titleTooLong`.
7. The step computes seven values. They are the sponsorship fingerprint, the newest summary version, the policy area, and the bill title. They also include the tracked issue IDs, the issue revision ID, and the person ID of each tracked official. If the current revision has the same seven values: Succeeded as `unchanged`.

Actions pages are not read. Any difference in step 7 makes a new revision, so a renamed issue or a bioguide ID mapped to another person drafts again. A bill with no current revision is always drafted.

The `noTrackedOfficial` and `unchanged` outcomes write a Bypassed run with the reason as its code and the same context as a drafted run. The queue refuses either code without its run. The `supersededByNewerCandidate` outcome of step 2 writes no run.

A Blocked entry stays Blocked until the owner fixes the cause and requeues it by its code. The command accepts any block code, including an exception type name such as `InvalidDataException` from an unexpected error. The requeue moves only Blocked entries with exactly that code back to Pending, so an unknown code moves none. It clears the attempts, the retry fields, and the lease, like a Failed requeue. A Blocked entry holds no reservation, because the step blocks before it reserves tokens.

### Tracked officials

The tracked officials are the covered people with a bioguide ID on the UTC day of the bill's update date. They come from the configuration revision of the candidate, so the stage does not read the current collection configuration. They match the bill by bioguide ID.

Other members never enter the facts, the fingerprint, or the record. A sponsor entry has the introduced date. A cosponsor entry has the original flag, the sponsorship date, and the withdrawal date.

The tracked issues use the issue configuration that the worker loaded at the time of the call. Its revision ID is in the revision, and the issue facts are in the run context.

### Fingerprint and summary version

`ActionFingerprint` is the SHA-256 of `action-fingerprint-v1`, then one line for each entry in ordinal order. The line is `bioguide|sponsor or cosponsor|original or added|date|withdrawn date or -`.

`BillSummarySelection` picks the newest summary: the latest action date, then the latest update time, then the highest version code. Its version is `{version code}@{update time in UTC}`.

### Prompt and output

The request uses model `claude-haiku-5-5`, effort `medium`, and at most 4,000 output tokens. The user message holds the bill: its identity, title, introduced date, and policy area. It also holds the tracked issues, the officials with role and dates, and the newest summary text. It holds no latest action.

Structured outputs fix a `headline` and a `summary`, each with a text and a confidence. The confidence stays in the run output and is never in the revision. The golden test pins the system prompt, the schema, and one input hash.

The byte cap is the smaller of 100,000 UTF-8 bytes and the run token limit minus the output allowance.

### Checks

Code rejects the whole answer on any of these:

- A text is empty, over its bound (256 characters for the headline, 8,000 for the summary), or has a NUL.
- A confidence is not finite or is outside 0 to 1.
- The headline or the summary contains the full name of a configured person who is not an official in the facts, compared without case. A name that is part of a listed official's name does not count.

The name check cannot find people who are not in the configuration. The [validation loop](analysis.md#validation-loop) gives one more call with the reasons. After a second rejection, the entry fails as `outputRejected`.

A prompt over the byte cap is built again without the summary text, so the model drafts from the structured facts alone. A retry prompt over the cap takes the same fallback. Each run whose own prompt left out the summary has the reason `summaryOmitted`, also when the answer is rejected. The revision keeps its summary version and its code-built citation.

Facts that alone exceed the cap are Blocked as `overByteCap` on the first prompt. On a retry, they fail the entry with the rejection code.

### Citations

Code builds the citations from the line offsets of the saved extractions.

- A sponsor cites its `Sponsor:` line and the `Introduced:` line of the detail.
- A cosponsor cites its one `Cosponsor:` line, which holds the original flag, the sponsorship date, and the withdrawal date.
- The policy area cites its line.
- The summary cites only the block of the newest summary.

The summary block starts at its `Summary version:` line, which must match the code and the `Updated:` line of that summary. It ends at the next blank line or at the end of the page text.

A cosponsor is matched by its exact rendered line. A cosponsor who withdrew and rejoined therefore has one entry and one citation for each stint. An exact duplicate entry on two pages counts once.

### Revision and append

The worker appends a new revision to the record's draft, written by `civic-lens:analysis` with its run ID. If the current revision has `textOrigin` `Person`, the new revision keeps that text and origin. It updates the structured part and stores the model's text as the suggestion. The origin travels with the revision, so a later run still keeps the person's text.

The settlement takes the queue row lock and then `civic-lens-action-review`. `ActionDraftRows.AppendAnalysisRevisionAsync` returns a refusal code or appends:

- The bill has a newer candidate: the run is kept as `DraftDiscarded`, and the entry settles as `supersededByNewerCandidate`.
- The draft's current revision number is not one less than the new revision: the run is kept as `DraftDiscarded`. The entry moves to RetryWaiting with the code `revisionConflict` and the usual backoff, and the next attempt compares again.

Each queue entry is written in the same transaction as its candidate. An older entry that reads its evidence therefore sees each newer candidate that committed, and it settles with no model call. Candidate inserts do not take the action review lock. A newer candidate that commits between the append check and the commit lets the older revision append. The newer candidate then drafts on top of it, so the newest candidate still wins.

A person edits only the headline and the summary. `ActionDraftRevision.ValidatePersonEditOf` rejects any change to the structured part.

### Outcomes

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | A revision was appended |
| Succeeded | `unchanged` | All compared values match the current revision |
| Succeeded | `noTrackedOfficial` | No tracked official is on the bill, so no record is drafted |
| Succeeded | `supersededByNewerCandidate` | The bill has a newer candidate (later update date, or the same date with summaries) |
| RetryWaiting | `revisionConflict` | Another writer appended first |
| Blocked | `evidenceUnavailable`, `tooManySponsorships`, `titleTooLong`, `extractionVersionChanged`, `renderedTextMismatch`, `overByteCap` | The input cannot be drafted |
| Failed | `outputRejected`, a stop code, or `attemptsExhausted` | Owner requeue needed |

### Executor

The executor is `AnalysisWorker<ActionDraftRun>` with `ActionDraftingStep`. It runs beside the other analysis loops in the `worker` process, with its own wakeup, status row, daily limit, and run limit. It uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`.

Its required variables are in [operations](../operations.md#action-drafting). A daily limit without the key or the issue file is a configuration error. The status row is in `document_change_analysis_status` under the task `action-draft`.

### Commands

| Command | Effect |
|---|---|
| `actions draft status` | Daily limit, pause, tokens charged today, and the count of entries in each outcome |
| `actions draft list [view] [limit]` | Entries. Views: `drafted`, `unchanged`, `superseded`, `no-official`, `pending`, `failed` |
| `actions draft requeue <candidate-id\|--all>` | Failed rows, and no others, back to Pending |
| `actions draft requeue --blocked --code <block-code>` | Blocked rows with exactly that code, and no others, back to Pending. The code is required and must not be empty |

All are owner commands. Variables and examples are in [operations](../operations.md#action-drafting).

## Storage and contracts

- The generated [actions schema](schema/actions.md) lists every table, column, key, check, index, and trigger.
- The migration `ActionDrafting` replaces `summaries_completed_at_utc_ticks` with `includes_summaries`. Each existing candidate keeps its ID and has no summaries.
- For each existing candidate whose summaries had closed, the migration adds the candidate with summaries, created at the old completion time. It adds the index `ix_action_candidates_bill` on the source, the bill, and the update date. It then queues one Pending entry for every candidate.
- `action_drafts` has no foreign key to `action_candidates`. The record ID comes from the bill identity, and each revision names its candidate in `candidateId`.
- A Bypassed run records its reason in `action_analysis_runs`.
- `ActionDraftRevision` holds the headline, the summary, `textOrigin` (`Model` or `Person`), and an optional `suggestion`. The suggestion holds a headline, a summary, a run ID, and the fingerprint it was made under.
- It also holds the bill identity and title, `candidateId`, `candidateUpdateDate`, `candidateIncludesSummaries`, `fingerprint`, `summaryVersion`, the policy area, the tracked issue IDs, and `issueRevisionId`.
- It holds 1 to 600 sponsorships, each with one or two citations, and the summary and policy area citations. It holds at most 64 tracked issue IDs.
- Action drafting has its own budget, separate from the other budgets.
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Revision and sponsorships | `src/CivicLens.Core/Actions/ActionDraftRevision.cs`, `src/CivicLens.Core/Actions/ActionSponsorship.cs`, `src/CivicLens.Core/Actions/ActionSponsorshipRole.cs`, `src/CivicLens.Core/Actions/ActionTextOrigin.cs`, `src/CivicLens.Core/Actions/ActionTextSuggestion.cs` |
| Step and evidence read | `src/CivicLens.Application/Actions/ActionDraftingStep.cs`, `src/CivicLens.Application/Actions/ActionBillReader.cs`, `src/CivicLens.Application/Actions/ActionBillSnapshot.cs`, `src/CivicLens.Application/Actions/ActionCitations.cs` |
| Prompt, resolver, lifecycle, settings | `src/CivicLens.Application/Actions/ActionDraftPrompt.cs`, `src/CivicLens.Application/Actions/ActionDraftResolver.cs`, `src/CivicLens.Application/Actions/ActionDraftLifecycle.cs`, `src/CivicLens.Application/Actions/ActionDraftSettings.cs`, `src/CivicLens.Application/Actions/ActionDraftTask.cs` |
| Store and model ports | `src/CivicLens.Application/Actions/IActionDraftStore.cs`, `src/CivicLens.Application/Actions/IActionDraftingModel.cs` |
| Fingerprint and summary choice | `src/CivicLens.Application/Collection/Bills/ActionFingerprint.cs`, `src/CivicLens.Application/Collection/Bills/BillSummarySelection.cs` |
| Store, queue mapping, model | `src/CivicLens.Infrastructure/Actions/PostgresActionDraftStore.cs`, `src/CivicLens.Infrastructure/Actions/ActionDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Actions/ActionDraftRows.cs`, `src/CivicLens.Infrastructure/Actions/AnthropicActionDraftingModel.cs` |
| Record ID hash | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.ActionCandidates.cs` |
| Owner requeue of Blocked entries | `src/CivicLens.Application/Actions/RequeueBlockedActionDrafts.cs` |
| CLI and composition | `src/CivicLens.Host/Actions/ActionCommand.cs`, `src/CivicLens.Host/Actions/ActionDraftComposition.cs`, `src/CivicLens.Host/Actions/ActionDraftConfiguration.cs` |

## Limits and planned work

- Action review ([R-05](../roadmap.md#now)) is not built. The decisions table and the review status columns come with it. The review pages will use `ActionDraftRevision.ValidatePersonEditOf`.
- Action publication is not built. Decide release growth and the search index before it; see the [release limit](publication.md#limits-and-planned-work).
- Action records can produce about 110 revisions a day, about half of them sponsorship changes on approved records. Review must therefore be as streamlined as possible:
  - [ ] bulk approval, as part of the [review volume tools](review.md#limits-and-planned-work)
  - [ ] a faster review for a revision where only the code-built sponsorship part changed, which shows only the change and its citations
  - [ ] one queue item for each record: a newer worker revision replaces an unreviewed one
  - [ ] review shows the change since the last approved revision
- The prompt does not include the candidate update date, and no page renders it yet.
- Sponsorship changes draft at once. Measure how often an approved record gets a new revision within a few days. Add a waiting window only if that rate is high.
- After action publication, move the generic queue handlers, such as `IStatementQueueStore` and `StatementQueueCommands`, into Application/Analysis.
- After action publication, use one shared citation span type for document changes, statements, and actions, and unify naming across the analysis features.
- After the first action slice, build in this order: House votes, reviewed relationships between statements and actions, then Congressional Record articles as a statement connector. Cosponsorship is cheap position-taking, so votes carry most of the signal about whether words match deeds. Floor speeches cover every member, including officials with no usable web source.
- Offices and dated terms are a separate step before House votes. Senate votes need senate.gov data and a `lis_member_id` crosswalk, because Congress.gov has neither.
