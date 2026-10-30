# Actions

## Purpose

The actions feature drafts one record for each bill that has an [action candidate](bills.md#action-candidate). A record lists what every tracked official did with the bill: sponsoring it, cosponsoring it, and withdrawing a cosponsorship. Code approves each worker revision after it checks the citations, because the facts come from Congress.gov. A person can still edit, request changes on, and withdraw a record on the action review pages. A model prediction is never a human decision.

## Decisions

- One action record covers one bill and lists every tracked official's sponsorship. The bill's bioguide IDs match configured people, and the record shows the configured name.
- The first action slice is bill sponsorship and cosponsorship. Member records are not captured in this slice, because Congress.gov member terms give years only.
- A withdrawn cosponsorship stays in the record, with both dates cited.
- A new revision follows only a change in the compared facts. An update to the actions of a bill alone adds no revision.
- CRS bill summaries are optional cited evidence. A record must work without one, because CRS often publishes weeks after introduction. Drafting does not wait for a summary.
- The record cites the newest summary by action date. A new summary version makes a new revision.
- Code builds every citation from the saved extraction lines. The model writes none.
- The model writes only the headline and the summary. The structured part comes from code, and a person can never edit it.
- Code owns the cosponsor list. The model text names no cosponsor and gives no cosponsor count, so a change in the cosponsors needs no new text. The text does name the sponsors, so a change in the sponsors needs new text.
- When only the cosponsor entries changed since the current revision, the worker makes no model call. It appends a revision with the new structured part and copies the headline, the summary, `textOrigin`, and the suggestion of the current revision unchanged. This holds with or without an approved revision.
- Text that a person wrote survives later model runs. The model text then becomes a suggestion, so the worker never overwrites a person's wording.
- Each record states the date of its evidence. `ActionDraftRevision.CandidateUpdateDate` holds the update date of the bill version.
- Approval binds to the revision number, so every new revision needs a new review.
- The newest candidate of a bill wins. Candidates are ordered by update date, then by whether they include summaries.
- A prompt over the byte cap drops the summary text, so the model drafts from the structured facts alone.
- A bill with more than 20 summary pages drafts without its summaries. The step never reads those pages.
- The step verifies saved extractions against current versions before it reads any capture. A version upgrade and changed saved text therefore get different codes.
- Actions have their own review lock key, `civic-lens-action-review`, last in the publication lock order.
- Each record is one queue item. A newer analysis revision replaces an unreviewed one, and review shows the change since the last approved revision.
- The analysis append computes `review_status` again under the action review lock. The same append copies four values of the revision to the draft row. They are the candidate ID, the event date, the tracked issue IDs, and the sponsor person IDs. The lists filter and sort on them.
- The event date of an action is the newest sponsored or withdrawn date of its sponsorships. Enforced by `ActionDraftRevision.EventOn`. Test: `ActionDraftRevisionTests.TheEventDateIsTheNewestSponsoredOrWithdrawnDateAmongTheSponsorships`.
- A suggestion shows beside the text of a person, with "Use suggestion" and "Dismiss". A person's save may keep or clear the suggestion, but never change it.
- D-15 (accepted on 2026-10-19): code approves the worker revisions of an action. Code checks every citation. The model writes the headline, the summary, and the issue choice from the candidate issues of the policy area. Public pages label that text as an AI summary of the official record when the origin of the published text is Model. They add no label when a person wrote the text.
- A worker revision of a newer candidate gets a new code approval if three rules hold. No concern is open. The revision holds no text that a person wrote. The approval checks pass. A person edit stays with a person even after a person approved it. The release keeps the earlier approved revision until the owner publishes the new one. A reader report on a published action goes to the [review queue](review.md#reader-reports), and a correction shows as a banner on the action page.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- One draft exists for each bill. Enforced by `ux_action_drafts_bill`. Test: `PostgresBillStreamTests.TheDatabaseAllowsOneDraftForEachBillAndRefusesABadRecordId`.
- Only an analysis revision has a run ID, and one revision exists for each run. Enforced by `ck_action_revisions_run` and `IX_action_revisions_run_id`. Test: `PostgresBillStreamTests.TheDatabaseRefusesAnAnalysisRevisionWithoutARunAndAPersonRevisionWithOne`.
- An append needs the expected current revision. Enforced by `ActionDraftRows` under the `PostgresAdvisoryLock.ActionReview` key. Test: `PostgresBillStreamTests.APersonsConcurrentRevisionFailsTheExpectedVersionCheckSoTheRunRetriesWithBackoffAndAppendsNothing`.
- A new revision needs a new fingerprint or summary version. Enforced by `ActionDraftStep`. Test: `ActionDraftStepTests.AMatchingFingerprintAndSummaryVersionSettleWithoutAModelCall`.
- A cosponsor-only change appends without a model call and keeps the text and the suggestion. Any other change, including a change in a sponsor, calls the model. Enforced by `ActionDraftStep` and `ActionDraftRevision.HasSameSponsorsAs`. Tests: `ActionDraftStepTests.ANewSponsorCallsTheModelBecauseTheTextNamesTheSponsors`, `ActionDraftStepTests.ANewSponsorshipAppendsAtOnceWithoutAModelCallAndKeepsTheTextBecauseCodeOwnsTheSponsorshipList`, `ActionDraftStepTests.ASponsorshipChangeKeepsAPersonsTextAndTheSuggestionUnchangedWithItsRunId`, and `ActionDraftStepTests.ASponsorshipChangeTogetherWithAnotherFactStillCallsTheModel`.
- The tracked issues of a revision come from the candidate issues of its policy area, and the model may choose none. Enforced by `BillIssueChoice.Check` in `ActionDraftResolver`, and by `ActionDraftRevision.ValidateShape`. A legacy revision with no candidate set is exempt from the subset rule. Tests: `PostgresBillStreamTests.ARevisionStoredBeforeTheModelChoiceHasNoCandidateSetAndStillPublishesOpensAndTakesAPersonEdit`, `ActionDraftStepTests.AChoiceOutsideTheCandidatesOrRepeatedIsRejectedWithItsReasonAndRetriedOnce`, `ActionDraftStepTests.AModelThatPicksNoIssueLeavesTheRecordWithoutOneAndNeverFallsBackToThePolicyAreaSet`, and `ActionDraftRevisionTests.TheChosenIssuesMustComeFromTheCandidatesBecauseTheModelMayNotInventOrPickAnUnlistedIssue`.
- The model is asked to choose, even when the policy area has one candidate. Enforced by `ActionDraftPrompt`. Test: `ActionDraftStepTests.ASingleCandidateIsStillOfferedToTheModelBecauseThePolicyAreaAloneDoesNotMakeAnIssueFit`.
- The model text names no tracked cosponsor who is not also a sponsor. A sponsor may be named, even when the sponsor's name contains the full name of a cosponsor. Enforced by `ActionDraftResolver.CheckNames`. Tests: `ActionDraftStepTests.TextThatNamesATrackedCosponsorIsRejectedBecauseCodeOwnsTheCosponsorList`, `ActionDraftStepTests.ASponsorMayBeNamedAndAPartOfTheirNameIsNotAnotherPerson`, `ActionDraftStepTests.APersonWhoSponsorsAndCosponsorsMayBeNamedBecauseOnlyAPureCosponsorBelongsToCode`, and `ActionDraftStepTests.ASponsorWhoseNameContainsACosponsorsFullNameMayBeNamed`.
- The prompt names no cosponsor. Enforced by `ActionDraftPrompt`. Test: `ActionDraftStepTests.ThePromptNamesOnlyTheSponsorsBecauseCodeOwnsTheCosponsorListThatSponsorshipChangesUpdate`.
- The text of a person stays, and model text becomes a suggestion. Enforced by `ActionDraftRevision`. Test: `ActionDraftRevisionTests.ARevisionAfterAPersonsTextMustKeepThatTextAndOfferTheModelsAsASuggestion`.
- A person edits only the headline and the summary. Enforced by `ActionDraftRevision`. Test: `ActionDraftRevisionTests.ARecordReviewerEditsOnlyTheHeadlineAndTheSummary`.
- A person may keep or clear the suggestion, but never change it. Enforced by `ReviewTextRules.RequireSuggestionKeptOrCleared`, checked again under the action review lock. Tests: `ActionDraftRevisionTests.APersonMayKeepOrClearTheSuggestionButNeverChangeItBecauseOnlyTheModelWritesSuggestions` and `PostgresBillStreamTests.APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne`.
- An analysis append computes the stored status again, then code approves the revision when the rules allow it. Enforced by `ReviewRecords.AppendRevision` and `ReviewRecords.AppendCodeApprovedAsync`. Tests: `PostgresBillStreamTests.CodeApprovesAWorkerRevisionInTheSameTransactionAndTheRecordIsReadyToPublish`, `PostgresBillStreamTests.CodeApprovesAWorkerRevisionThatOnlyCopiesTheTextAfterANewSponsorJoins`, and `PostgresBillStreamTests.APersonEditAfterACodeApprovalWaitsForAPersonAndKeepsTheCodeApprovedRevisionAsTheLastApprovedOne`.
- Code does not approve a revision in three cases. A concern is open, the revision holds text that a person wrote, or an approval check fails. Enforced by `ReviewRecords.AppendCodeApprovedAsync`. Tests: `PostgresBillStreamTests.AWorkerRevisionKeepsAnOpenConcernSoCodeDoesNotApproveItAndTheBlockerStaysVisible`, `PostgresBillStreamTests.APersonWithdrawalStaysEffectiveWhenTheWorkerDraftsAgain`, `PostgresBillStreamTests.AWorkerRevisionWithTextAPersonWroteStaysForAPersonWhenNoPersonApprovedThatText`, `PostgresBillStreamTests.AWorkerRedraftAfterAPersonApprovedAPersonEditStaysAwaitingReviewWithTheModelSuggestionAttached`, and `PostgresBillStreamTests.ACodeApprovalLeavesTheRecordForAPersonWhenAnApprovalCheckFails`.
- A code approval queues the relationship pairs and the brief weeks of the record, as a person approval does. Enforced by `ActionDraftQueueMapping.AfterTransitionAsync` through `AfterDecisionHooks.ForRecordKind`. Tests: `PostgresBillStreamTests.ACodeApprovalRunsTheRelationshipHookSoAnApprovedStatementOfTheSponsorQueuesItsPair` and `PostgresBillStreamTests.ACodeApprovalOfTheRevisionThatTheActiveReleaseServesRequeuesTheBriefOfItsWeek`.
- The stored status is one of the five drafted statuses. Enforced by `ck_action_drafts_review_status`. Test: `PostgresBillStreamTests.TheDatabaseRefusesAnActionStatusThatThePolicyNeverStores`.
- An approval binds to the exact revision that the page showed. Enforced by `ReviewWriteRules` under the lock. Test: `PostgresBillStreamTests.ApprovalBindsToTheExactRevisionSoAWorkerRevisionAfterThePageLoadedTurnsItIntoAConflict`.
- An older candidate never becomes the newest revision. Enforced by `ActionDraftRows.HasNewerCandidateAsync`, checked when the step reads its evidence and again at the append. Tests: `PostgresBillStreamTests.ADelayedOlderCandidateIsSupersededSoItsStaleContentNeverBecomesTheNewestRevision`, `PostgresBillStreamTests.ANewerCandidateThatArrivesAfterTheClaimSupersedesTheAppendSoTheOlderDraftNeverBecomesARevision`, and `PostgresBillStreamTests.ALaterUpdateDateWithoutSummariesSupersedesAnEarlierCandidateWithSummariesBecauseTheDateDecidesFirst`.
- More than 20 summary pages never block a bill; it drafts without them. Enforced by `ActionDraftStep`. Test: `ActionDraftStepTests.MoreThanTwentySummaryPagesDraftWithoutSummariesAndNeverReadThemBecauseSummariesAreOptionalInput`.
- An owner requeue of Blocked drafts moves only Blocked drafts with exactly the given code. Enforced by `PostgresAnalysisQueue.RequeueAsync` and `ActionDraftLifecycle`. Tests: `PostgresBillStreamTests.RequeueBlockedMovesOnlyBlockedEntriesWithTheGivenCodeAndResetsTheirAttempts` and `ActionDraftLifecycleTests.GateStatementAndDocumentChangeTasksKeepBlockedFinalBecauseTheirLifecyclesDoNotOptIn`.
- A redraft moves only a Succeeded `drafted` or `unchanged` entry whose bill has its newest model-drafted run under an older prompt version. Enforced by `PostgresActionDraftStore.RequeueOlderPromptAsync` and `ActionDraftLifecycle`. Tests: `PostgresBillStreamTests.RedraftMovesASettledEntryWhoseNewestDraftRunUsedAnOlderPromptAndLeavesASupersededEntryAndResetsAttempts`, `PostgresBillStreamTests.RedraftMovesNothingWhenTheNewestDraftRunOfTheBillUsedTheCurrentPrompt`, `PostgresBillStreamTests.RedraftIgnoresANewerBypassedRunBecauseOnlyTheNewestDraftedRunNamesThePromptTheDraftCameFrom`, and `ActionDraftLifecycleTests.OnlyASucceededActionDraftWithADraftToReplaceMayReturnToPendingForARedraft`.
- The vote list of an action page holds only the vote drafts of its own bill. It lists the newest vote day first, at most 25. Enforced by `BillVoteDrafts`. Tests: `BillLinksWorkspaceTests.TheActionPageListsTheVoteDraftsOfItsOwnBillNewestFirstAndSkipsOtherBillsAndCongresses` and `BillLinksWorkspaceTests.TheActionPageShowsOnlyTheNewestVoteDraftsOfABillAndSaysSo`.
- The congress.gov link is built from the stored bill identity, with the ordinal and the slug of the bill type. Enforced by `SourcePages.Bill`. Test: `BillLinksWorkspaceTests.TheActionPageLinksTheBillOnCongressGovForEachBillTypeAndOrdinal`.
- Model confidence never reaches a revision. Enforced by `ActionDraftStep`. Test: `ActionDraftStepTests.ConfidenceStaysInTheRunOutputAndNeverReachesTheRevision`.

## Behavior

### Ownership

The step runs on the shared analysis owners: `AnalysisWorker<TRun>`, `AnalysisValidationLoop`, `PostgresAnalysisQueue`, `AnalysisAnswerRules`, `AnalysisSchema`, `AnthropicStructuredCall`, and `AnalysisRunRows`. See [analysis](analysis.md#behavior) for the queue lifecycle, budgets, retries, and pauses.

`BillGate.TrackedIssueIdsFor` maps a policy area to the issues that are active on a day. The bill gate and drafting both call it. `CollectionConfiguration.CoveredBioguidePeopleOf` lists the covered people who have a bioguide ID. `ApiResponseReader` reads the saved capture of a job, and `JobPreparation` shares it.

The store port `IActionDraftStore` extends `IAnalysisQueueStore`. The owner commands therefore use the shared queue handlers in [analysis](analysis.md#queue-and-lifecycle).

### Record identity

The record ID is the SHA-256 of the source and the bill (congress, type, number), length framed like the candidate ID. It has no update date. Each candidate is one bill version, and a candidate finds its record by computing the same hash. `action_drafts` has one row for each record.

### Queue

`action_analysis` has one entry for each candidate, with the task `action-draft`. The entry is added in the transaction that writes the candidate. Late summaries make a new candidate, so no entry is ever reopened. The evidence of a candidate without summaries never includes summaries pages, even after they arrive.

The worker claims due entries by priority, then oldest first, like the other analysis tasks. `ActionDraftRows.HasNewerCandidateAsync` owns the newest-wins rule, and the index `ix_action_candidates_bill` serves it. A candidate with summaries is newer than one without them on the same update date. The budget is `action_analysis_budget`. The notify trigger on the queue table only wakes the worker.

When summaries arrive after the older candidate drafted, the bill version gets a second candidate and a second call.

### Step

The step does these checks in order. None of them calls the model.

1. No evidence: Blocked as `evidenceUnavailable`.
2. The bill has a newer candidate: Succeeded as `supersededByNewerCandidate`.
3. More than 20 summary pages: the step drops the summary pages and does not read them. The bill drafts without summaries. Each run has the reason `summaryOmitted`, and the revision has no summary version and no summary citation.
4. A saved extraction has another extraction version than the current parser and extractor would write: Blocked as `extractionVersionChanged`. This check runs before any capture is read or parsed.
5. The step reads the detail page, each cosponsor page, and each summary page. It parses each capture with the preparation parser and renders it with `CongressBillRenderer`. Any difference from the saved extraction text: Blocked as `renderedTextMismatch`, which means the saved text changed.
6. No tracked official on the bill: Succeeded as `noTrackedOfficial`, with no revision. More than 600 sponsorships: Blocked as `tooManySponsorships`. A bill title over 4,000 characters: Blocked as `titleTooLong`.
7. The step computes eight values. They are the sponsorship fingerprint, the newest summary version, the policy area, the policy area ID, and the bill title. They also include the candidate issue IDs, the issue revision ID, and the person ID of each tracked official. The candidate issue IDs are the issues that `BillIssueChoice.For` lists for the policy area. The step resolves the policy area ID with `IssueConfiguration.PolicyAreaIdFor`. A label that the configuration lacks gives no ID. If the current revision has the same eight values: Succeeded as `unchanged`.
8. Only the fingerprint differs, and the Sponsor-role entries are equal (bioguide ID, person ID, and sponsorship date): the step makes no model call. It appends a revision that copies the text, the origin, and the suggestion of the current revision, with a Bypassed run whose reason is `sponsorshipsChanged`. The entry settles Succeeded with no code.

Actions pages are not read. A change in a Sponsor-role entry calls the model. Any other difference in step 7 calls the model too. A renamed issue, a changed candidate set, or a bioguide ID mapped to another person drafts again. A current revision with no stored candidate set drafts again. A bill with no current revision is always drafted. `ActionDraftContext.ChangeSinceCurrent` owns this comparison, and `ActionDraftRevision.HasSameSponsorsAs` owns the sponsor test.

The `noTrackedOfficial` and `unchanged` outcomes write a Bypassed run with the reason as its code and the same context as a drafted run. The sponsorship-only append writes a Bypassed run with the reason `sponsorshipsChanged`, and the revision names that run. The queue refuses either code without its run. The `supersededByNewerCandidate` outcome of step 2 writes no run.

A Blocked entry stays Blocked until the owner fixes the cause and requeues it by its code. The command accepts any block code, including an exception type name such as `InvalidDataException` from an unexpected error. The requeue moves only Blocked entries with exactly that code back to Pending, so an unknown code moves none. It clears the attempts, the retry fields, and the lease, like a Failed requeue. A Blocked entry holds no reservation, because the step blocks before it reserves tokens.

An owner redraft queues the settled entries of bills whose newest model-drafted run used an older prompt version. The newest run decides for the whole bill, because a sponsorship-only copy writes a Bypassed run that names the current prompt. The requeue clears the attempts like a Failed requeue. Code approval, the budget gates, and the person-text rule stay as they are. A new revision of a record with person text goes to a person, with the model text as a suggestion. The step calls the model again only when the stored draft differs from the new inputs. A draft that has no stored issue candidates counts as different, so drafts made before issue narrowing get narrowed issues.

### Tracked officials

The tracked officials are the covered people with a bioguide ID on the UTC day of the bill's update date. They come from the configuration revision of the candidate, so the stage does not read the current collection configuration. They match the bill by bioguide ID.

Other members never enter the facts, the fingerprint, or the record. A sponsor entry has the introduced date. A cosponsor entry has the original flag, the sponsorship date, and the withdrawal date.

`BillIssueChoice` (in `Application/Issues`) holds the candidate issues of a bill. They are the issues that `BillGate.TrackedIssueIdsFor` returns for the policy area. It builds the prompt section, the `issueIds` schema property, and the check of the answer. The step uses the issue configuration that the worker loaded at the time of the call. Its revision ID is in the revision, and the candidate IDs are in the run context.

### Fingerprint and summary version

`ActionFingerprint` is the SHA-256 of `action-fingerprint-v1`, then one line for each entry in ordinal order. The line is `bioguide|sponsor or cosponsor|original or added|date|withdrawn date or -`.

`BillSummarySelection` picks the newest summary: the latest action date, then the latest update time, then the highest version code. Its version is `{version code}@{update time in UTC}`.

### Prompt and output

The request uses model `claude-haiku-5-5`, effort `medium`, and at most 4,000 output tokens. The user message holds the bill: its identity, title, introduced date, and policy area. It also holds the candidate issues with their definitions, includes, and excludes, the names of the tracked sponsors, and the newest summary text. It holds no cosponsor and no latest action. The system prompt tells the model not to name a cosponsor or give a cosponsor count.

Structured outputs fix a `headline` and a `summary`, each with a text and a confidence. When the policy area has at least one candidate issue, the schema also requires `issueIds`: a list of unique candidate IDs, which may be empty. The model chooses the issues that the bill is about. Code never falls back to the candidate set. A policy area with no candidate gets the schema without `issueIds`. The confidence stays in the run output and is never in the revision. The prompt asks for a headline of about 150 characters or fewer. It tells the model to name the bill by its number. It tells the model not to copy the bill title, because the public page shows it. The validator bound stays 256. Text length rejections follow the [shared rule](analysis.md#validation-loop). The golden test pins the system prompt, the schema, and one input hash.

The byte cap is the smaller of 100,000 UTF-8 bytes and the [call cap](analysis.md#budget) minus the output allowance.

### Checks

Code rejects the whole answer on any of these:

- A text is empty, over its bound (256 characters for the headline, 8,000 for the summary), or has a NUL.
- A confidence is not finite or is outside 0 to 1.
- `issueIds` is missing when candidates exist, lists an ID that is not a candidate, or lists an ID twice.
- The headline or the summary contains the full name of a configured person who is not an official in the facts, compared without case. A name that is part of a listed official's name does not count.
- The headline or the summary contains the full name of a tracked cosponsor who is not also a sponsor of the bill, compared without case. A tracked sponsor may be named.

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

The worker appends a new revision to the record's draft, written by `civic-lens:analysis` with its run ID. A sponsorship-only revision copies the text, the origin, and the suggestion; see step 8. Otherwise, if the current revision has `textOrigin` `Person`, the new revision keeps that text and origin. It updates the structured part and stores the model's text as the suggestion. The origin travels with the revision, so a later run still keeps the person's text.

The settlement takes the queue row lock and then `civic-lens-action-review`. `ActionDraftRows.AppendAnalysisRevisionAsync` returns a refusal code or appends through `ReviewRecords.AppendCodeApprovedAsync`, which computes `review_status` again and sets the listed columns. It also writes the code approval when the rules allow it; see [review](review.md#code-approval). `ActionDraftQueueMapping.AfterTransitionAsync` then runs the decision hooks of the action kind, with no new lock key. The refusal cases are:

- The bill has a newer candidate: the run is kept as `DraftDiscarded`, and the entry settles as `supersededByNewerCandidate`.
- The draft's current revision number is not one less than the new revision: a drafted run is kept as `DraftDiscarded`. A Bypassed run stays Bypassed. The entry moves to RetryWaiting with the code `revisionConflict` and the usual backoff, and the next attempt compares again.

Each queue entry is written in the same transaction as its candidate. An older entry that reads its evidence therefore sees each newer candidate that committed, and it settles with no model call. Candidate inserts do not take the action review lock. A newer candidate that commits between the append check and the commit lets the older revision append. The newer candidate then drafts on top of it, so the newest candidate still wins.

A person edits only the headline and the summary. `ActionDraftRevision.ValidatePersonEditOf` rejects any change to the structured part. `ReviewTextRules` rejects any change to the suggestion and accepts a kept or a cleared one. [Votes](votes.md#vote-review) use the same text rules.

### Outcomes

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | A revision was appended, by the model or as a sponsorship-only copy |
| Succeeded | `unchanged` | All compared values match the current revision |
| Succeeded | `noTrackedOfficial` | No tracked official is on the bill, so no record is drafted |
| Succeeded | `supersededByNewerCandidate` | The bill has a newer candidate (later update date, or the same date with summaries) |
| RetryWaiting | `revisionConflict` | Another writer appended first |
| Blocked | `evidenceUnavailable`, `tooManySponsorships`, `titleTooLong`, `extractionVersionChanged`, `renderedTextMismatch`, `overByteCap` | The input cannot be drafted |
| Failed | `outputRejected`, a stop code, or `attemptsExhausted` | Owner requeue needed |

### Executor

The executor is `AnalysisWorker<ActionDraftRun>` with `ActionDraftStep`. It runs beside the other analysis loops in the `worker` process, with its own wakeup, status row, daily limit, and call cap. It uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`.

Its required variables are in [operations](../operations.md#action-drafting). A daily limit without the key or the issue file is a configuration error. The status row is in `document_change_analysis_status` under the task `action-draft`.

### Action review

Action review uses the shared decision rules and review cycles in [review](review.md#review-cycles). The Host has action pages on top of the shared handlers.

**Ownership.** Core/Actions holds `ActionReview`, the aggregate of one record, and the review rules on `ActionDraftRevision`. Application/Actions holds `SaveActionDraft`, `DecideActionReview`, `GetActionReview`, `ListActionsNeedingAction`, and the store boundary `IActionReviewStore`. Infrastructure/Actions holds `PostgresActionReviewStore` on the shared `ReviewRecords` core. A person cannot create an action draft, so the store has no create.

**Save.** `SaveActionDraft` reads the expected revision and copies it with the new headline and summary. The request holds one `SuggestionChoice`: `Keep`, `Dismiss`, or `Use`. `Keep` keeps the stored suggestion. `Dismiss` clears it. `Use` takes the headline and the summary from the stored suggestion, ignores the posted text, and clears the suggestion. `Use` fails when the revision has no suggestion. The store checks the edit against the current revision again under the lock, because another writer can append first.

**Decide.** `DecideActionReview` mirrors the statement handler. An approval runs `ActionDraftRevision.ValidateForApproval` and checks every citation against its saved extraction.

**Reads.** `GetActionReview` returns the review cycle, the extractions that the loaded revisions cite, and the configured names of the officials. The names come from the configuration revisions of the candidates. `ListActionsNeedingAction` returns the records in Needs action, at most one page, and the total of all of them. Needs action means every status except Approved. That includes a record that code skipped and a person edit that waits. It also includes a record with changes requested and a record with withdrawn approval. The order is `last_revised_at_utc_ticks` descending, then record ID. A new revision of an old record moves it to the top. Every revision sets the column, as for weekly briefs and relationships.

**Pages.** No page lists all actions. The Review home page shows the records in Needs action in one panel with the vote exceptions; see [review](review.md#lists-and-paging). Each row links to the editor. The rail has no Actions link, and a missing record shows a link back to Review home.

`/Review/Actions/{record-id}` is the editor. It has these parts:

- the change since the last approved revision: the old and the new text side by side
- the saved revision and the "Edit wording" form, with the suggestion and its "Use suggestion" and "Dismiss" buttons
- the evidence: each sponsorship with its cited lines, the policy area, and the CRS summary
- a link to the bill on congress.gov, built from the stored bill identity
- the vote drafts of the bill, each with its roll call, vote kind, vote day, and status, and a link to its vote page
- the shared `ReviewDecisionPanel` and `ReviewHistoryPanel`

The vote drafts come from the same read as the page, in the same transaction. They are the drafts whose candidate names the bill: same Congress, bill type, and bill number. The list shows the newest 25 by vote day and says so when it is full. The status badge has no open-concern count, because the read takes no decision history. The page stores no copy and needs no migration. A vote has no stored result, so the list shows none.

"Use suggestion" saves the stored suggestion text and clears the suggestion. "Dismiss" saves the form text and clears the suggestion. The plain save keeps the suggestion. The forms use the static post-back pattern of the statement pages, and `ReviewRecordPage` maps the failures.

### Publication

An approved action record with no open concern publishes in a static release, like the other kinds. `ActionRecordLoader` owns the action kind for publish and the ready list. `PostgresActionReviewStore.GetForPublicationAsync` reads the selected drafts, their review cycles, and every extraction that their current revisions cite in one repeatable-read transaction. It checks each citation against its extraction before it returns.

`PublishedActionBuilder` maps the current revision to `PublishedAction`. Each sponsorship shows the configured name of its official from the current publication catalog. The policy area ID is the one that the approved revision stored at draft time. Publish never reads the issue configuration. Each distinct cited extraction becomes one embedded source with its URL and observation time. The record and its search filters are in [publication](publication.md#release-content).

### Commands

| Command | Effect |
|---|---|
| `actions draft status` | Daily limit, pause, tokens charged today, and the count of entries in each outcome |
| `actions draft list [view] [limit]` | Entries. Views: `drafted`, `unchanged`, `superseded`, `no-official`, `pending`, `failed` |
| `actions draft requeue <candidate-id\|--all>` | Failed rows, and no others, back to Pending |
| `actions draft requeue --blocked --code <block-code>` | Blocked rows with exactly that code, and no others, back to Pending. The code is required and must not be empty |
| `actions draft redraft` | Succeeded `drafted` and `unchanged` rows of bills with an older prompt version, and no others, back to Pending. Older entries of a stale bill requeue too and settle as superseded, so the printed count is entries, not bills. Needs no argument and calls no model itself |

All are owner commands. Variables and examples are in [operations](../operations.md#action-drafting).

## Storage and contracts

- The generated [actions schema](schema/actions.md) lists every table, column, key, check, index, and trigger.
- `action_drafts` has no foreign key to `action_candidates`. The record ID comes from the bill identity, and each revision names its candidate in `candidateId`.
- A Bypassed run records its reason in `action_analysis_runs`. `ck_action_analysis_runs_reason` lists the allowed reasons, including `sponsorshipsChanged`.
- `action_drafts` holds `review_state_version` and `review_status`. `action_review_decisions` mirrors the statement decision table.
- The commands use the replay receipt table with the operations `action-save` and `action-decide`.
- `ActionDraftRevision` holds the headline, the summary, `textOrigin` (`Model` or `Person`), and an optional `suggestion`. The suggestion holds a headline, a summary, a run ID, and the fingerprint it was made under.
- It also holds the bill identity and title, `candidateId`, `candidateUpdateDate`, `candidateIncludesSummaries`, `fingerprint`, `summaryVersion`, the policy area, the optional `policyAreaId`, and `issueRevisionId`.
- It holds the tracked issue IDs and the candidate issue IDs (`issueCandidateIds`).
- Every tracked issue ID is also a candidate ID. A revision with no `issueCandidateIds` is a legacy revision from before the model choice. It stays valid, publishes, and takes person edits. It drafts again under the budget.
- It holds 1 to 600 sponsorships, each with one or two citations, and the summary and policy area citations. It holds at most 64 tracked issue IDs.
- Action drafting has its own budget, separate from the other budgets.
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Revision and sponsorships | `src/CivicLens.Core/Actions/ActionDraftRevision.cs`, `src/CivicLens.Core/Actions/ActionSponsorship.cs`, `src/CivicLens.Core/Actions/ActionSponsorshipRole.cs`, `src/CivicLens.Core/Review/ReviewTextOrigin.cs`, `src/CivicLens.Core/Review/ReviewTextSuggestion.cs`, `src/CivicLens.Core/Review/ReviewText.cs`, `src/CivicLens.Core/Review/ReviewTextRules.cs` |
| Step and evidence read | `src/CivicLens.Application/Actions/ActionDraftStep.cs`, `src/CivicLens.Application/Actions/ActionDraftContext.cs`, `src/CivicLens.Application/Actions/ActionBillReader.cs`, `src/CivicLens.Application/Actions/ActionBillSnapshot.cs`, `src/CivicLens.Application/Actions/ActionCitations.cs` |
| Prompt, resolver, lifecycle, settings | `src/CivicLens.Application/Actions/ActionDraftPrompt.cs`, `src/CivicLens.Application/Actions/ActionDraftResolver.cs`, `src/CivicLens.Application/Actions/ActionDraftLifecycle.cs`, `src/CivicLens.Application/Actions/ActionDraftSettings.cs`, `src/CivicLens.Application/Actions/ActionDraftTask.cs`, `src/CivicLens.Application/Issues/BillIssueChoice.cs` |
| Store and model ports | `src/CivicLens.Application/Actions/IActionDraftStore.cs`, `src/CivicLens.Application/Actions/IActionDraftModel.cs` |
| Fingerprint and summary choice | `src/CivicLens.Application/Collection/Bills/ActionFingerprint.cs`, `src/CivicLens.Application/Collection/Bills/BillSummarySelection.cs` |
| Store, queue mapping, model | `src/CivicLens.Infrastructure/Actions/PostgresActionDraftStore.cs`, `src/CivicLens.Infrastructure/Actions/ActionDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Actions/ActionDraftRows.cs`, `src/CivicLens.Infrastructure/Actions/AnthropicActionDraftModel.cs` |
| Review aggregate | `src/CivicLens.Core/Actions/ActionReview.cs` |
| Review commands and reads | `src/CivicLens.Application/Actions/SaveActionDraft.cs`, `src/CivicLens.Application/Actions/DecideActionReview.cs`, `src/CivicLens.Application/Actions/GetActionReview.cs`, `src/CivicLens.Application/Actions/ListActionsNeedingAction.cs`, `src/CivicLens.Application/Actions/ActionsNeedingAction.cs`, `src/CivicLens.Application/Actions/IActionReviewStore.cs`, `src/CivicLens.Application/Votes/BillVoteDraft.cs` |
| Review store | `src/CivicLens.Infrastructure/Actions/PostgresActionReviewStore.cs`, `src/CivicLens.Infrastructure/Actions/Persistence/ActionReviewDecisionRow.cs`, `src/CivicLens.Infrastructure/Votes/BillVoteDrafts.cs` |
| Review pages | `src/CivicLens.Host/Review/ReviewHome.razor`, `src/CivicLens.Host/Actions/ActionEditor.razor`, `src/CivicLens.Host/Actions/ActionLabels.cs`, `src/CivicLens.Host/Actions/ActionRoutes.cs`, `src/CivicLens.Host/Collection/SourcePages.cs` |
| Publication | `src/CivicLens.Application/Publication/ActionRecordLoader.cs`, `src/CivicLens.Application/Publication/PublishedActionBuilder.cs`, `src/CivicLens.Publication.Contracts/PublishedAction.cs`, `src/CivicLens.Host/Publication/PublishedActionPage.razor` |
| Record ID hash | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.ActionCandidates.cs` |
| Owner requeue of Blocked entries | `src/CivicLens.Application/Actions/RequeueBlockedActionDrafts.cs` |
| Owner redraft | `src/CivicLens.Application/Actions/RedraftActionDrafts.cs` |
| CLI and composition | `src/CivicLens.Host/Actions/ActionCommand.cs`, `src/CivicLens.Host/Actions/ActionDraftComposition.cs`, `src/CivicLens.Host/Actions/ActionDraftConfiguration.cs` |

## Limits and planned work

- `actions draft redraft` draws a new draft only when the step 7 inputs differ from the current draft. A missing candidate set is one such difference, so the command serves drafts that lack issue candidates. A later prompt bump with the same inputs settles `unchanged`, because `ActionDraftContext.ChangeSinceCurrent` does not compare the prompt version. The newest `Drafted` run still names the old prompt. The entry stays selected, so each later run requeues it and adds one Bypassed run row. Add the prompt comparison with the next prompt bump that must rerun.
- Requeued entries keep their created time. A fresh bill at the same priority can wait behind a redraft backlog, as with failed requeue and regate.
- Action records can produce about 110 revisions a day, about half of them sponsorship changes on approved records. Those revisions copy the text and need no model call. Code approves them.
- The editor page has no Host test with a drafted record yet. The Host tests cover the Review home panel and missing records.
- The prompt does not include the candidate update date, and no page renders it yet.
- Sponsorship changes draft at once. Measure how often an approved record gets a new revision within a few days. Add a waiting window only if that rate is high.
- After the first action slice, build in this order: House votes, reviewed relationships between statements and actions, then Congressional Record articles as a statement connector. Cosponsorship is cheap position-taking, so votes carry most of the signal about whether words match deeds. Floor speeches cover every member, including officials with no usable web source.
- House votes do not need dated terms. Each House Clerk vote record gives the bioguide ID, party, and state of each member on the vote date. Votes match people by bioguide ID, as sponsorships do, and keep the party and state from the record.
- House votes: [votes](votes.md) holds the implemented collection, drafting, review, and publication.
- Senate votes: [votes](votes.md) holds the implemented collection and drafting. They come from Voteview and match members by the bioguide ID that the [roster](roster.md) holds, because Congress.gov has no Senate vote data.
- The congress.gov bill link follows the public page form `/bill/{ordinal}-congress/{type slug}/{number}`. No repository fixture or stored row shows that form. Check it against the live site once.
- Bill lookup ignores the source of an action draft, because bills have one source and no code owns its ID. A second bill source needs a source filter in `FindRecordIdByBillAsync`.
