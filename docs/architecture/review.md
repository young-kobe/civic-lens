# Review

## Purpose

The review feature is the private editorial workspace. People draft, revise, and decide on proposed public records beside their evidence. Every decision binds to an exact immutable revision. This page covers document changes and the decision rules that statements, actions, and votes share. Statement-specific review is in [statements](statements.md#statement-review). Action-specific review is in [actions](actions.md#action-review). Vote-specific review is in [votes](votes.md#vote-review). Brief-specific review is in [briefs](briefs.md#brief-review).

## Decisions

- One authorized operator may both draft and approve a record. A separate approver is not required, and self-approval is allowed.
- The owner can draft, review, and manage editorial access, and can publish with the CLI. The code subject publishes through the worker stage; see [publication](publication.md#automatic-publication). The additional reviewer can draft and review, but cannot publish or manage access.
- Each person signs in with a separate Auth0 identity, and each decision names its actor. Sign-in alone grants no editorial access.
- Authorization is enforced at mutation boundaries, not only by hiding UI controls.
- Server configuration maps exact subject IDs to roles. Client role claims confer no permissions.
- Access changes need a server configuration update and a restart. There is no management UI.
- Approval binds to the exact public wording and evidence versions reviewed. An edit to either needs a new review. Earlier approvals stay as history and do not authorize the new revision.
- Approval and publication are separate actions.
- Code approves an executive action record at admission, and a worker revision of an action or a House vote. Their facts come from Congress.gov and the Clerk XML, and code checks every citation. The actor of that decision is the reserved subject `civic-lens:code`. Code also approves a worker revision of a statement that passes the checks in [statements](statements.md#statement-code-approval). People approve all other statements, relationships, briefs, document changes, and every revision that a person writes.
- Code approves a worker revision only if three rules hold. No concern is open. The revision holds no text that a person wrote. The approval checks pass. If one rule fails, the revision waits for a person. A person who withdraws an approval or requests changes therefore stays effective across worker redrafts. A record that review blocked in this way can be withdrawn from the release; see [publication](publication.md#corrections-and-withdrawal).
- A person can still edit and withdraw a fact record. A person edit is a revision that waits for a person to approve it.
- Writes use expected versions, not editing locks or last-write-wins. The UI never silently merges editorial wording.
- Decisions are immutable. They cannot be edited or deleted.
- A saved revision never resolves a concern by itself. Only an explicit approval with a resolution note does.
- Shared Application handlers own these rules for the UI, the CLI, and future MCP callers.
- Document changes, statements, actions, votes, and briefs share one decision policy, `ReviewPolicy`. Briefs reach it through `ReviewRecords`.
- A review cycle starts at the latest approval. Reads load the current revision, the last approved revision, and the decisions since that approval. A cycle has no revision cap. The open-concern cap bounds its decisions.
- One step appends a revision and computes `review_status` again. Person saves and analysis appends both use it. One step records a decision, and the person path and the code path both use it.
- `review_status` and `has_changes` are stored columns that C# computes. SQL filters read them, so SQL keeps no copy of the policy.
- Autosave and live preview are not part of the first workflow.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- A model author cannot decide. Enforced by `ReviewPolicy`. Test: `ReviewPolicyTests.TheAiDrafterCannotRecordAnyDecisionEvenOnItsOwnDraft`.
- A decision binds to its exact revision. Enforced by composite foreign keys from the decision tables to the revision tables. No test.
- One decision exists for each record and review state version. Enforced by unique indexes on both decision tables. Test: `PostgresStatementReviewStoreTests.DecisionReplayReturnsTheOriginalDecisionAndAStaleStateConflicts`.
- Expected versions are checked under the review lock. Enforced by `ReviewWriteRules`. Test: `PostgresDocumentChangeReviewStoreTests.CreateReplayIsStableAndConcurrentSavesAppendOnlyOneCurrentRevision`.
- A record has at most 64 open concerns, so a review cycle holds at most 65 decisions: its opening approval and the concerns after it. Only an approval resolves a concern, and the next approval opens a new cycle. The cycle read takes `ReviewPolicy.MaximumDecisionsPerCycle` rows for each record. Enforced by `ReviewPolicy`. Tests: `ReviewPolicyTests.ACycleHoldsItsApprovalAndAtMostTheOpenConcernCapBecauseOnlyTheNextApprovalResolvesAConcern` and `PostgresDocumentChangeReviewStoreTests.ReadsLoadOneReviewCycleSoARecordWithMoreDecisionsInTotalThanACycleHoldsStillReadsAndAcceptsDecisions`.
- A batched read finds the cycle start of each record on its own. Enforced by `ReviewRecords`. Test: `PostgresDocumentChangeReviewStoreTests.AListReadsEachRecordFromItsOwnCycleStartSoStatusesAndConcernsStayApartInOneBatch`.
- A read replays every decision of the current cycle against the policy. Enforced by `ReviewDecisionHistory`. Test: `PostgresDocumentChangeReviewStoreTests.ReadsReplayEveryDecisionSinceTheLatestApprovalSoATamperedDecisionInTheCycleFailsTheRead`.
- A reused idempotency key with another payload fails. Enforced by `ReviewIdempotency`. Test: `PostgresDocumentChangeReviewStoreTests.DecisionReplayReturnsOriginalDecisionAndRejectsPayloadReuse`.
- The stored status equals the policy status. Enforced by `ReviewPolicy`, `ReviewRecords.AppendRevision`, and the status check constraints. Tests: `PostgresDocumentChangeReviewStoreTests.StoredStatusEqualsThePolicyStatusAfterEveryKindOfWrite` and `PostgresBillStreamTests.APersonEditAfterACodeApprovalWaitsForAPersonAndKeepsTheCodeApprovedRevisionAsTheLastApprovedOne`.
- Code approves a worker revision of an action, a vote, or a statement in the transaction that appends it. It then runs the decision hooks of a person decision. Enforced by `ReviewRecords.AppendCodeApprovedAsync` and `ReviewRecords.AfterCodeApprovalAsync`. Tests: `PostgresBillStreamTests.CodeApprovesAWorkerRevisionInTheSameTransactionAndTheRecordIsReadyToPublish`, `PostgresHouseVoteTests.CodeApprovesAWorkerVoteRevisionInTheSameTransactionAndTheRecordIsReadyToPublish`, and `PostgresBillStreamTests.ACodeApprovalRunsTheRelationshipHookSoAnApprovedStatementOfTheSponsorQueuesItsPair`, and `PostgresStatementDraftStoreTests.ACodeApprovalRunsTheRelationshipHookSoAnApprovedStatementOfTheSponsorQueuesItsPair`.
- Code does not approve a revision in three cases. A concern is open, the revision holds text that a person wrote, or an approval check fails. The revision then waits for a person. Enforced by `ReviewRecords.AppendCodeApprovedAsync`. Tests: `PostgresBillStreamTests.AWorkerRevisionKeepsAnOpenConcernSoCodeDoesNotApproveItAndTheBlockerStaysVisible`, `PostgresBillStreamTests.APersonWithdrawalStaysEffectiveWhenTheWorkerDraftsAgain`, `PostgresBillStreamTests.AWorkerRevisionWithTextAPersonWroteStaysForAPersonWhenNoPersonApprovedThatText`, and `PostgresBillStreamTests.ACodeApprovalLeavesTheRecordForAPersonWhenAnApprovalCheckFails`.
- Only the worker path can use the subject `civic-lens:code`. A person, an owner, or a reviewer cannot act as it or be configured as it. Enforced by `ReviewAuthor.IsReserved` in `ReviewAuthorization`, `ReviewWriteRules`, and `ReviewWorkspaceSettings`. The publish handler lets the code subject publish only actions and votes; see [publication](publication.md#invariants). Tests: `PostgresBillStreamTests.APersonCannotActAsTheCodeSubject` and `DocumentChangeDraftConfigurationTests.TheAiDrafterAndCodeCheckSubjectsCannotBeAnOwnerOrReviewer`.
- An approval resolves every open concern. Enforced by `ReviewPolicy`. Test: `ReviewPolicyTests.RequestChangesCarriesAcrossRevisionAndApprovalMustResolveExactConcern`.
- A reader of named revisions reads exactly the named revisions and no other revision of the same record. It refuses a revision of another kind, because a mixed call would read the wrong table. It fails when any named revision is not stored, and never drops the record. Enforced by `CitedRevisions.ReadRevisionJsonAsync`, which the brief, relationship, and vote readers share. Tests: `PostgresHouseVoteTests.ReadMembersReturnsTheRecordAndPersonOfEachMemberOfTheNamedRevisionsInOneQueryAndRefusesARevisionThatIsNotStored`, `PostgresHouseVoteTests.AVoteRevisionThatIsNotNamedIsNotReadBecauseTheReleaseBindsToTheExactRevision`, `PostgresHouseVoteTests.ARevisionOfAnotherKindIsRefusedBeforeAnyQueryBecauseAMixedCallWouldReadTheWrongTable`, `PostgresBriefDraftStoreTests.AReleaseThatNamesARevisionThatIsNotStoredFailsLoudInsteadOfDroppingTheRecord`, and `PostgresRelationshipPublicationTests.TheReadFailsLoudWhenACitedRevisionIsNotStoredInsteadOfDroppingTheSide`.
- A withdrawal needs a current approval. Enforced by `ReviewPolicy`. Test: `ReviewPolicyTests.WithdrawalRequiresCurrentApprovalAndCreatesAConcernForReapproval`.
- The statement list orders by newest event date, then record ID. Enforced by `ReviewKeyset.Page` and `ListedDrafts`. Test: `PostgresStatementReviewStoreTests.ListOrdersByTheNewestEventDateThenTheIdAcrossPagesAndFiltersByTheStoredStatus`.
- The issue, official, and date filters of the statement list keep only matching rows and combine with the status filter. Enforced by `ListedDrafts`. Test: `PostgresStatementReviewStoreTests.ListFiltersByIssueOfficialAndEventDateAndCombinesThemWithTheStatus`.
- The Review home panel lists the actions and votes in Needs action, at most one page of each kind, and counts every one. Every Approved record is left out. The order is the last revision time, newest first, then record ID. Every revision sets the column `last_revised_at_utc_ticks`. Enforced by `ReviewRecords.ReadNeedingActionAsync`, `ActionDraftRows.Revised`, `VoteDraftRows.Revised`, and the merge in `ReviewHome`. Tests: `PostgresReviewListTests.ANewRevisionOfAnOldActionMovesItAboveNewerRecords`, `PostgresReviewListTests.ActionsNeedingActionAreBoundedByTheLimitLeaveOutApprovedRecordsAndTotalEveryOne`, `PostgresReviewListTests.VotesNeedingActionAreBoundedByTheLimitLeaveOutApprovedRecordsAndTotalEveryOne`, `PostgresBillStreamTests.APersonEditAfterACodeApprovalWaitsForAPersonAndKeepsTheCodeApprovedRevisionAsTheLastApprovedOne`, `PostgresHouseVoteTests.CodeApprovesAWorkerRevisionAfterAnEarlierCodeApprovalAndCountsItAsApproved`, `ActionReviewWorkspaceTests.TheReviewHomeMergesActionsAndVotesByLastRevisionNewestFirstAsync`, and `ActionReviewWorkspaceTests.TheReviewHomeListsEachActionAndVoteThatCodeLeftForAPersonAndLinksItsEditorAsync`.
- A list cursor holds a rank only when its list pages by rank. A cursor from another list is invalid input. Enforced by `PageCursor.Parse` and `PageCursor.RequireRank`. Tests: `PageCursorTests.ABadRankIsRejectedAsInvalidInput`, `PostgresReviewListTests.AListWithoutTiersRefusesARankedCursorSoACursorFromAnotherListCannotMovePastItsRows`, and `PostgresCollectionJobStoreTests.APageCursorWithARankIsInvalidBecauseTheCheckListHasNoTiers`.
- Statement triage lists only records that need action, 10 for each page, with the list filters. Enforced by `PostgresStatementReviewStore.ListTriageAsync`. Test: `PostgresStatementReviewStoreTests.TriageListsDraftedStatementsThatNeedActionWithTheirExtractionAndTheApprovalCheckResult`.
- A triage read uses a fixed number of queries, whatever the page size. Enforced by the batch reads of `PostgresStatementReviewStore.ListTriageAsync`. Test: `PostgresStatementReviewStoreTests.TriageReadsADraftedPageWithAFixedNumberOfQueriesWhateverItsSize`.
- A triage row shows the result of the checks that Approve runs. Enforced by `ApprovalCheck` and `ListStatementTriage`. Tests: `PostgresStatementReviewStoreTests.TriageFailsTheApprovalCheckOfAStatementWhoseSelectionIsNoLongerConfigured` and `StatementReviewWorkspaceTests.TheTriagePageShowsTheQuoteAndAFailedCheckWithNoApproveAndRequestChangesNeedsANoteAsync`.
- A triage row offers Approve only when its check passed, and no decide control when it has an open concern. Enforced by `TriageDecisionControls`. Test: `StatementReviewWorkspaceTests.TheTriagePageShowsTheQuoteAndAFailedCheckWithNoApproveAndRequestChangesNeedsANoteAsync`.
- An inline decision binds to the expected revision and review state version of its row, and it needs an idempotency key. Enforced by `StatementTriage` and `DecideStatementReview`. Tests: `StatementReviewWorkspaceTests.AnInlineApproveBindsTheRowRevisionRedirectsAndNeedsAnIdempotencyKeyAsync` and `StatementReviewWorkspaceTests.AnInlineApproveOfARevisionThatAPersonReplacedAfterThePageLoadedIsAConflictAsync`.
- A triage row whose check failed shows no quote, so a bad quotation span never breaks the page. Enforced by `ListStatementTriage`. Test: `PostgresStatementReviewStoreTests.TriageListsDraftedStatementsThatNeedActionWithTheirExtractionAndTheApprovalCheckResult`.
- A lookup text names a bill, or a House or Senate roll call of the current session, or it is invalid input. Enforced by `LookupDraft`. Tests: `LookupDraftTests.AnInputThatNamesNoBillOrRollCallIsAUserErrorBeforeAnyRead`, `LookupDraftTests.ASenateRollCallMeansASenateRollOfTheCurrentCongressAndSessionBecauseTheTwoChambersNumberTheirRollsApart`, and `BillLinksWorkspaceTests.AnUnknownBillOrRollCallAndAnInvalidInputShowAMessageOnTheHomePageWithoutAnError`.
- A bill without a Congress means the current Congress, and a roll call means a House roll of the current Congress and session. Enforced by `LookupDraft` and `CongressCalendar`. Tests: `LookupDraftTests.ABillWithoutACongressMeansTheCurrentCongressAndEveryCitationSpellingFindsTheSameActionDraft`, `LookupDraftTests.ARollCallMeansAHouseRollOfTheCurrentCongressAndSessionBecauseRollNumbersRestartEachSessionAndEachChamber`, and `CongressCalendarTests.ASessionIsTheFirstOrSecondYearOfTheCongressAndTheTermYearRuleMatchesCongressOn`.
- A lookup opens the draft of exactly the named bill, or of exactly the named roll call of its chamber, Congress, and session. A name with no draft opens nothing. Enforced by `FindRecordIdByBillAsync` and `FindRecordIdByRollAsync`. Tests: `BillLinksWorkspaceTests.TheLookupBoxOpensTheActionDraftOfABillAndTheVoteDraftOfARollCall` and `PostgresActionLookupTests.ABillFindsOnlyTheActionDraftOfItsCongressTypeAndNumber` and `PostgresRollLookupTests.ARollCallFindsOnlyTheVoteDraftOfItsChamberCongressSessionAndNumber`.
- Each append replaces the listed columns of the draft row with those of the current revision. Enforced by the `revised` delegates `ActionDraftRows.Revised` and `VoteDraftRows.Revised`. Statements call `StatementDraftRows.SetListing` from three places: create, save, and the AI draft. Tests: `PostgresBillStreamTests.EachWorkerRevisionReplacesTheListedColumnsWithThoseOfTheCurrentRevision`, `PostgresHouseVoteTests.EachWorkerRevisionReplacesTheListedColumnsWithThoseOfTheCurrentRevision`, and `PostgresStatementReviewStoreTests.EachWritePathSetsTheListedColumnsFromTheRevisionAndTheEventDateFallsBackToTheCaptureDate`.
- A reader report resolves once. Enforced by the conditional update in `PostgresReaderReportStore.ResolveAsync`, which needs the status Open and the expected version. Tests: `PostgresReaderReportStoreTests.AReportThatIsResolvedOrChangedCannotBeResolvedAgainAndAMissingOneIsNotFound` and `ReaderReportWorkspaceTests.ACorrectionNeedsANoteAndAResolvedReportCannotBeResolvedAgain`.
- A report that needs a correction has a note. Enforced by `ResolveReaderReport` and the check constraint `ck_reader_reports_resolution`. Test: `ResolveReaderReportTests.ACorrectionNeedsANoteSoTheRecordPageCanTellTheEditorWhatToFix`.
- Only a reviewer or the owner resolves a report. The code subject cannot. Enforced by `ReviewAuthorization` in each report handler. Test: `ResolveReaderReportTests.OnlyAReviewerOrTheOwnerResolvesAndTheCodeSubjectCannotSoNoMachineClosesAReaderReport`.
- The Open list shows the oldest report first, then the report ID ascending. The Resolved list shows the newest first, then the report ID descending. Neither skips or repeats a row at a page edge. Enforced by `ReviewKeyset.Page` in `PostgresReaderReportStore.ListAsync`. Tests: `PostgresReaderReportStoreTests.TheOpenQueueListsTheOldestReportFirstThenTheReportIdAscendingAndPagesBothWaysWithoutSkippingATie` and `PostgresReaderReportStoreTests.TheResolvedListShowsTheNewestReportFirstThenTheReportIdDescendingAndPagesBothWays`.
- A record page shows the banner of a correction until the active release holds a newer revision than the revision of the report. A dismissed or open report shows no banner. Enforced by `PostgresReaderReportStore.ListUnansweredCorrectionsAsync`. Tests: `PostgresReaderReportStoreTests.ACorrectionStaysUnansweredUntilThePublishedRevisionPassesTheRevisionOfTheReport`, `ReaderReportWorkspaceTests.TheRecordPageShowsTheCorrectionBannerUntilANewerRevisionIsPublishedAndAnOpenOrDismissedReportShowsNone`, and `ReaderReportWorkspaceTests.AnExecutiveActionPageShowsTheBannerOfACorrectionReportOfThatKind`.
- Every record kind has a current revision path in the report queue. Enforced by the kind list of `PostgresReaderReportStore`. Test: `PostgresReaderReportStoreTests.EveryRecordKindHasACurrentRevisionPathSoNoNewKindShowsNoEditorLinkOnItsReports`.
- Every revision has a number of 1 or more, an author subject of 1 to 256 characters, and a UTC creation time. Enforced by `ReviewAuthor.IsValidRevisionHeader`, which each revision type calls in its shape check. Tests: `ReviewAuthorTests.ARevisionHeaderNeedsANumberAnAuthorOfOneTo256CharactersAndAUtcTimeSoEveryRevisionKindSharesOneRule` and `VoteDraftRevisionTests.ARevisionNeedsAnAuthorAndAUtcCreationTimeBecauseEveryRevisionKindSharesOneHeaderRule`.
- A closed report is deleted 365 days after its resolved time, and an open report is never deleted. Enforced by `PostgresReaderReportStore.DeleteExpiredClosedAsync` with `ReaderReportProtocol.ClosedRetentionDays`, and by the check constraint `ck_reader_reports_resolution`, which keeps the resolved time of an open report empty. Test: `PostgresReaderReportStoreTests.OnlyClosedReportsResolvedMoreThanAYearAgoAreDeletedSoAnOpenReportAndARecentOneAreNeverLost`.
- The About page states the report retention: one year in the review system and up to 6 more months in the encrypted backups. The page text does not read `ReaderReportProtocol.ClosedRetentionDays` or `--keep-monthly 6` in `deploy/scripts/backup.sh`, so a change to either needs a change to the page. Enforced by the fixed text in `PublishedAboutPage`. The backup tie has no test. Test, which also fails when the day count changes: `ReleaseRendererTests.AboutPageStatesTheClosedReportRetentionAndFailsWhenTheRuleNoLongerSaysOneYear`.
- A failed deletion of expired reports does not stop the pull. Enforced by `PullReaderReports`. Test: `ReaderReportPullTests.AFailedDeletionOfExpiredReportsIsLoggedAndTheNewReportsAreStillPulledSoAStuckDeleteNeverBlocksTheQueue`.
- Reader text shows as plain text. Enforced by Razor encoding in `ReportList`, `ReportDetail`, and `ReaderReportBanner`. Test: `ReaderReportWorkspaceTests.TheQueueShowsReaderTextAsPlainTextTheReplacedRevisionAndNoEditorLinkForARecordThatIsNotInReview`.

## Behavior

### What reviewers check

The workspace centers the proposed public record beside its supporting evidence. Document history and comparisons support the investigation. Reviewers assess wording, quotation fidelity, attribution, dates, context, citations, and interpretation.

- For a document change, reviewers separate a substantive source edit from navigation churn or a processing change.
- For a relationship, reviewers verify the claimed connection. The page shows the statement passage beside the facts of the target, and a flag when a cited record changed.
- For a brief, reviewers verify each assertion against the cited revision and read the coverage description. A brief shows a flag when a cited record changed.

An explanation of significance is an editorial judgment that needs review. A text difference never makes it automatic. A comparison alone does not show substantive significance.

### Drafts

A document-change draft starts from an explicitly selected, complete saved comparison with an actual text change. There are three paths:

- The worker writes an AI draft of each complete text change; see [analysis](analysis.md).
- An operator explicitly creates a draft from a saved comparison.
- A reviewer starts a draft by hand.

A change has at most one draft. Starting a draft for a change that already has one opens the existing draft.

The public record holds:

- a headline and a plain-language summary
- optional evidence-supported significance
- limits and uncertainty
- the source institution and supported official links
- configured issue categories
- bindings to the exact comparison, the before and after versions, and the cited passages

The earlier and later observation dates stay distinct from the actual change date. The record includes a change date only when evidence supports it.

### Revisions

Each explicit save keeps an immutable revision of the wording, attribution, issue selection, and evidence bindings. Each record has one current revision. Incomplete drafts can be saved. Approval of a document change needs a headline, a summary, an institution, at least one valid citation, and explicit resolution of all outstanding concerns. An actual change date needs a selected exact citation.

Editing an approved revision creates a new unapproved revision. The historical approval and any published record do not change. The UI warns before a person leaves unsaved edits.

Review shows a revision beside the supporting before and after passages. Technical identifiers stay in the evidence details.

Initial citations select positive ranges from the first 32 comparison hunks. The editor offers all hunks for explicit selection within the citation limit.

### Review decisions

A saved revision starts awaiting review. A decision targets an exact revision and keeps the authenticated actor.

| Decision | Effect and requirements |
|---|---|
| Approve | Marks the revision suitable for publication after checking wording, attribution, dates, context, citation fidelity, and substantive significance. One authorized approval suffices. For a worker revision of an action, a vote, or a statement, code approves after its checks pass; see [Code approval](#code-approval). An approval can carry a correction note for readers, 1 to 500 characters. The worker publishes the note as a correction when the approved revision replaces a public record. No other decision takes this note. See [publication](publication.md#corrections-and-withdrawal). |
| Request changes | Blocks publication until explicitly resolved. Needs a note that describes the needed corrections. |
| Withdraw approval | Invalidates the effective approval and blocks future publication. Needs a reason. |

Either reviewer can raise a blocking concern. A later approval must acknowledge the outstanding concerns and include a resolution note. It records the exact outstanding decision IDs it resolves. The original decisions keep their original revision bindings. Unresolved concerns carry forward as publication blockers when a new revision is saved.

### Code approval

`ReviewRecords.AppendCodeApprovedAsync` appends the worker revision of an action, a vote, or a statement. It then writes an Approve decision in the same transaction. The decision names the new revision number and the next review state version, and its actor is `civic-lens:code`. It uses the same approval checks as a person: `ValidateForApproval` with the saved extractions, and `ReviewPolicy.ValidateDecision`. The decision row, the state version, and the status come from the one step, `RecordDecision`, that `DecideAsync` also uses.

The settlement calls `AfterCodeApprovalAsync` after it adds its run row. That call runs the after-decision hooks of the record kind in the order that a person decision uses: relationship staleness, then brief staleness. The hooks save the context, so they must run after the run row exists. The worker already holds the review key of the kind. The hooks then take the later keys in the declared order; see [Advisory locks](../architecture.md#advisory-locks).

A failed check is not stored. The record stays awaiting review for a person.

Statement code approval runs its checks in `StatementDraftStep`, on the extraction that the claim read. This is safe for four reasons. The extraction and the admitting configuration revision never change. The issue configuration is fixed for each process. A regate requeues only gates with `NoTrackedIssue`. A human draft that appears during the call makes `TryAddAnalysisDraftAsync` return false, so the code approval is dropped with the draft. `StatementAllowedSelections.RequireApprovable` is the single path for these checks. Only the date rule differs: code passes `dateReadByCode` when it read the date from the release.

### Status states

The check constraints on `document_change_drafts`, `statement_drafts`, and `action_drafts` list the stored statuses. Statements add NotDrafted. `ReviewPolicy.GetCurrentStatus` computes it after each write: the latest decision on the current revision sets it, and without one it is awaiting review. `ReviewPolicy.GetInitialStatus` gives the status of a new draft row, which has no decisions.

| From | To | Write |
|---|---|---|
| NotDrafted | AwaitingReview | The first revision, by a person or by analysis |
| Any status | AwaitingReview or AwaitingReviewWithConcerns | A saved or appended revision, with or without open concerns |
| Any drafted status | Approved | Approve, or a code approval. Approve must resolve every open concern |
| Any drafted status | ChangesRequested | Request changes |
| Approved | ApprovalWithdrawn | Withdraw approval |

### Review cycles

A review cycle starts at the latest approval of a record. A record with no approval has one cycle that starts at its first decision. An approval resolves every open concern, so the decisions before it never change the status or the open concerns.

- Reads load the current revision, the last approved revision, and the decisions of the cycle. The AI draft outcome list loads revision 1 through `ReviewRecords.ReadFirstRevisionsAsync` for the model draft. The statement read loads it too, but uses only the run of revision 1. No read loads other revisions or older decisions.
- A read checks the approval at the start of the cycle against its revision, and replays every later decision against the policy.
- The history panel shows the current and the last approved revisions, and the decisions of the cycle. `ReviewHistory.LoadedRevisions` builds that revision list, and each review aggregate carries it as `Revisions`.
- A cycle has no revision cap. The decisions of a cycle are bounded by the open-concern cap; see [Invariants](#invariants).

### Concurrency and replay

- A draft save checks the expected current revision atomically.
- A stale save returns a conflict. It keeps the unsaved text of the operator for comparison with the latest revision.
- A review decision checks the expected current revision and the review-state version atomically. A changed revision or an intervening decision needs a refresh before the person decides.
- An idempotent retry of the same successful request returns the original outcome. It creates no duplicate revision or decision.
- Keys are scoped to the authenticated actor and operation and bound to the request payload. Reuse with a conflicting payload is rejected.
- Authorization is checked before a replayed or a new outcome returns.

### Lists and paging

The draft lists, the new-change list, and the recent collection checks use keyset paging. A request holds one opaque cursor with a direction and a (time, id) key. Each page lists the newest rows first. It returns a newer cursor and an older cursor, and a cursor is null at that end.

| List | Order |
|---|---|
| Document-change drafts | Creation time, then draft ID |
| Statement drafts | Event date (newest first), then record ID |
| Briefs and relationships | Needs action: last revision time, then record ID. Other filters: creation time, then record ID |
| Review home panel of actions and votes | Needs action: last revision time, then record ID |
| New changes | Observation time of the later version, then comparison ID |
| Checks | Creation time, then job ID |

The statement list adds a rank to the cursor: `o.<rank>.<ticks>.<id>`. The rank is always 1, which keeps the ranked keyset path of `ReviewKeyset.Page`. The ticks are the event date at midnight. `PageCursor.Parse` accepts only the tiers that `ReviewPriority` defines. The two-part form still serves the other lists.

The event date of a statement is its statement date, or else the capture date of its candidate. The tier of an action or a vote is the `priority` of the analysis row for the draft task. It orders the analysis queues, and no review list reads it; see [analysis](analysis.md#storage-and-contracts).

The statement lists also filter on one issue, one official, and an event date range, both ends included. The Host reads `issue`, `official`, `from`, and `to` from the query and checks them against the configured issues and the people of the collection configuration. It keeps them in the tab and pager links. A value that is not valid shows the same error as a bad cursor. A start date after the end date shows its own message. The Application handlers take one `DraftListFilter` with the status and these four values.

A new change is a complete comparison with at least one text hunk and no draft. The draft list can filter on status in SQL. Approved means that the newest decision on the current revision approves it. Needs action means every other status. The overview counts reuse the same status filter in `ReviewRecords.Drafts`, so each count equals the total of its list.

The Review home page lists new changes, drafts, the actions and votes in Needs action, source health, and recent activity. The owner also sees the latest root job of each configured source; see [collection](collection.md#sources-page). The recent activity feed reads the newest events of each kind from the review tables. For the owner only, it also reads the job and processing tables. Application merges the events by time. The stat strip also counts the statements, weekly briefs, and relationships that need action, and each card links to its list. One card counts the actions and votes that need action and links to the panel.

Code approves a bill action or a vote when its checks pass. The worker then publishes it; see [actions](actions.md#behavior). A record shows in the panel only when code left it for a person, or when a person asked for changes or withdrew approval. The panel shows at most 10 records of each kind, most recently revised first, and its header gives the total. A row shows the headline, the kind, and the status, and links to the editor that `RecordRoutes.Editor` names. The rail has no Actions or Votes link, because no list page exists. The editors open only from a link.

`RecordRoutes.Editor` maps a record kind and a record ID to its review page. It calls the route class of the kind.

### Finding a draft

The Review home page has a search box, "Find a draft". It sends a GET request with the query `find`, like the list filters, so it needs no antiforgery token. `LookupDraft` reads the text and trims it. "HR 1234", "H.R. 1234", and "HJ Res 5" name a bill of the current Congress. A leading Congress number, as in "118 HR 1234", names another Congress. "Roll 245" names a House roll call of the current Congress and the current session. "Senate roll 245" names a Senate roll call. The vote page links the roll call to clerk.house.gov or senate.gov by chamber (`SourcePages.RollCall`). Test: `VoteReviewWorkspaceTests.ASenateVotePageLinksTheSenateRollCallAndNamesTheSenateInItsHints`. A roll call with a Congress number is not valid, because the session would be unknown.

`CongressCalendar` gives the current Congress and session from the UTC date. `CongressBillListItem.NormalizeCitationType` gives the bill type, so no second type table exists. A bill goes to the action page through `FindRecordIdByBillAsync` of the action store, and a roll call goes to the vote page through `FindRecordIdByRollAsync`. The page redirects to the editor. When no draft exists, the home page shows "No draft for" and the text. When the text names no bill or roll call, `DraftLookupFormatException` is an invalid input. The page shows its message with status 200 and does not fail. An operational failure shows a retry message.

### Triage

Statements have a triage page, `/Review/Statements/Triage`. It lists the drafted statements that need action. It keeps the order, the issue, official, and date filters, and the cursors of the list. A page holds 10 rows (`ListStatementTriage.PageSize`). The statement list links to it. Actions and votes have no triage page, because code approves them.

A triage read loads the records of a page and the extractions that they cite in one transaction. The number of queries does not grow with the page. A row shows the headline, the summary, the cited quotes, and the result of the approval check. `ListStatementTriage` builds the quotes next to the check, so the page renders only text. A row whose check failed shows no quote and its failure message, and it never breaks the page.

`ApprovalCheck` holds that result as data. It runs the validation that Approve runs and keeps the failure message. A statement check also uses the allowed selections of the admitting configuration revision. One batch read loads those revisions for the page.

A row without an open concern has a note field, Approve, and Request changes. Approve is not offered when the check failed. The buttons post to `DecideStatementReview`. The post holds the expected revision number, the expected review state version, and an idempotency key for the render. Request changes needs a note. `ReviewCommand` maps a conflict, a bad request, and an operational failure to a status code and a message, as the editors do. The page shows the message and reloads. An unauthorized actor goes to the access-denied page. A decision redirects to the same triage page with a short result message. The page keeps its filters.

A row with an open concern has only the link "Open editor". A concern ends only with a note that resolves it, and only the editor writes that note.

`SourceSpan.QuoteIn` reads the quote of a citation. The editors and the statement triage page share it.

### Workspace forms

The editor shows the saved account and its exact selected passages as the approval target. It keeps them apart from editable or recovered unsaved wording. Reviewers judge substantive change, wording, attribution, dates, interpretation, and evidence limits. Automatic citation and revision checks establish integrity, not semantic correctness.

The workspace keeps conflicting edits. It disables decisions for unsaved, failed, or conflicting forms. Every mutation uses an antiforgery token. The reader preview updates on the server render after an explicit save.

### Evidence verification

The New changes list is the one read that does not verify full evidence. One query reads each stored comparison result, source, requested URL, and observation time into `NewDocumentChange`. That type holds the passage count and the first passage. The query loads no extraction text and does not compute the comparison again, so a page of 100 rows stays cheap. A list row is only a pointer.

Saving a comparison, the single review read, starting a draft, approval, and publication all verify the full evidence. A read verifies each comparison and its two extractions once and reuses them for every revision.

### Reader reports

The rail links to the queue at `/Review/Reports`. The link shows no count, because the layout would then run a query on every page. The home stat card shows the open count. The queue holds the reports that readers sent about published records. The worker pulls them from the search service; see [search](search.md#operational-pull). A report stores the record ID and kind, the revision, the category, the reader text, and the received time. It stores nothing about the reporter.

A reviewer or the owner opens a report and resolves it as Dismissed or CorrectionNeeded. A correction needs a note. A dismissal may have one. A resolved report cannot be resolved again. The resolve command checks the expected version, as a review decision does, and a second command gets a conflict. The queue keeps an open report until a person resolves it. It deletes a closed report 365 days after its resolved time, whether the status is Dismissed or CorrectionNeeded. A correction with no newer published revision is deleted too, and its banner goes with it.

The list has two tabs, Open and Resolved. The Open tab lists the oldest report first, then the report ID ascending. The Resolved tab lists the newest report first by received time, then the report ID descending. Both use `ReviewKeyset.Page`, which has an ascending option for the Open tab. The pager links read Previous and Next. A row shows the category, the start of the text, the kind, and the revision. A report on a replaced revision shows "Revision N, current is M". M comes from one query for the page over the seven draft tables. The query matches the kind and the record ID. A report on a record that review storage does not hold shows "Not in review" and no editor link.

The `reportPull` stage deletes the expired closed reports at the start of each pass, before the pull. `PullReaderReports` logs a failed deletion at error level and still pulls. The store compares the resolved time with the database clock.

A record page shows a banner for each report that was resolved as CorrectionNeeded. The banner shows the category, the reader text, the note of the reviewer, and a link to the report. The report page links to the editor. The banner shows while the active release holds no newer revision of the record than the revision of the report. A fix that is saved but not published keeps the banner. The review state of the record does not change.

### Authentication

The Host uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.14 for the Auth0 authorization-code flow with PKCE. The CLI acts as the configured owner subject. The authenticated `review` command binds to loopback and expects an HTTPS reverse proxy; see [host](host.md).

## Storage and contracts

- The generated [review schema](schema/review.md) lists every table, column, key, check, index, and trigger.
- The tables `reader_reports` and `reader_report_cursor` belong to the report queue; see the generated [reports schema](schema/reports.md). A report row holds these values: the report ID, the record ID, the kind, the revision, the category, and the text. It also holds the received time, the status, the note, the resolver, the resolved time, and a version. The version starts at 0 and rises by 1 when the report is resolved. The cursor table holds one row with the opaque cursor of the search service.
- Core/Review owns decision semantics. Application/Review owns authenticated commands and configured selection validation. Infrastructure/Review implements the store boundary with Postgres.
- `ReviewPolicy` holds the status rule, the open-concern rule, and the decision checks. It works on a record ID, the current revision number, the review state version, and the decisions. It knows no record type.
- Review writes take the review keys from `PostgresAdvisoryLock`. The keys and their order are in [advisory locks](../architecture.md#advisory-locks).
- `ReviewRecords` is the shared store core for each record type. Each store holds one instance over its draft, revision, and decision rows, its lock key, and its record ID property. It owns the create, save, and decide flow, the append step, the status filters, the draft page, and the unpublished approved list. Its bounded batch reads parse and validate one review cycle for each record into a `ReviewHistory`. Its batched revision 1 read parses and validates the shape of each revision 1. A store gives only its feature rules as delegates. They are the create body, the save preparation, the revision row builder, the decision checks, and the decision row builder. The save preparation also gets the draft row. Actions, votes, and briefs also give a delegate that records the revision time on the draft row. The action and vote delegate also copies the candidate ID, event date, issue IDs, and official IDs. Actions and votes share the headline and summary text rules in `ReviewText` and `ReviewTextRules`. Each store can give a callback for a withdrawn approval, which requeues the briefs that cite the record; see [briefs](briefs.md#staleness). The rows implement `IReviewDraftRow`, `IReviewRevisionRow`, and `IReviewDecisionRow`, and the revisions implement `IReviewRevision`. `IReviewRevision` is a deliberate departure from the concrete-types rule and the first interface in Core. Core cannot see Infrastructure, and the shared core needs it as its generic constraint.
- `ListedDrafts` owns the list read of statement drafts: the filters and the page. `ReviewKeyset.Page` has a plain form for two keys and a ranked form for three keys. The rows of statements, actions, and votes implement `IListedDraftRow`. Each draft row holds `event_on`, `issue_ids`, and `official_ids`, and the action and vote rows hold `candidate_id`. Each array has a GIN index, and the issue and official filters use `@>` so those indexes can serve them. Only `statement_drafts` has the index `ix_statement_drafts_listing`. `ReviewRecords.ReadNeedingActionAsync` reads the actions and votes of the Review home panel with the Needs action filter.
- `ReviewRecords.AppendRevision` is the one step that appends a revision and computes `review_status` again. The person save and the action, vote, and brief analysis appends call it. Document-change and statement analysis drafts only create revision 1, so they use `ReviewPolicy.GetInitialStatus`.
- `ReviewIdempotency` reads and writes replay receipts and checks the actor, key, and payload hash. `ReviewDecisionHistory` parses and replays the stored decisions of one review cycle. `IReviewDecisionRow` exposes the decision kind, so the cycle read finds the latest approval in SQL. `ReviewKeyset` builds the keyset page filter. `ReviewJson` holds the stored JSON options.
- Four tables hold record pointers, immutable revision JSON, immutable decision JSON, and actor, operation, and request-key replay receipts.
- Draft and decision expected versions are checked inside the locked transaction.
- Each draft row stores `review_status`, which `ReviewPolicy` computes in C#. Create, save, the action analysis append, and every decision write it under the lock. A check constraint lists the five status names. `ReviewStatus` adds `NotDrafted` for a record with no revision, which a document change never has.
- Each comparison row stores `has_changes`, which is `DocumentComparison.HasChanges()`: a complete comparison with at least one hunk.
- Partial indexes on `has_changes` and on the Approved and not-Approved statuses serve the filters, the New changes list, and the overview counts.
- The list of unpublished approved drafts reads the active release records. It stays a read query on top of `review_status`.
- Revisions keep the author and the UTC save time. Decisions reference the exact revision with a composite foreign key.
- Reads validate stored identities, historical transitions, and exact retained evidence.
- Bounds: 64 open concerns, which also bound the decisions of a cycle (see [Invariants](#invariants)), 64 citations for each revision, and bounded field sizes. Writes fail before they exceed a bound.
- The public release contract includes review and publication dates and correction context. It excludes author identity, reviewer email, Auth0 identifiers, and private notes; see [publication](publication.md).

## Code map

| Concern | Files |
|---|---|
| Decision policy and status | `src/CivicLens.Core/Review/ReviewPolicy.cs`, `src/CivicLens.Core/Review/ReviewStatus.cs`, `src/CivicLens.Core/Review/ReviewDecision.cs`, `src/CivicLens.Core/Review/AiProposedDate.cs` |
| Document-change revision | `src/CivicLens.Core/Review/DocumentChangeDraftRevision.cs`, `src/CivicLens.Core/Review/DocumentChangeReview.cs` |
| Commands | `src/CivicLens.Application/Review/CreateDocumentChangeDraft.cs`, `src/CivicLens.Application/Review/SaveDocumentChangeDraft.cs`, `src/CivicLens.Application/Review/DecideDocumentChangeReview.cs` |
| Reads | `src/CivicLens.Application/Review/GetDocumentChangeReview.cs`, `src/CivicLens.Application/Review/ListDocumentChangeReviews.cs`, `src/CivicLens.Application/Review/ListEligibleDocumentComparisons.cs`, `src/CivicLens.Application/Review/GetReviewOverview.cs`, `src/CivicLens.Application/Activity/GetRecentActivity.cs` |
| Validation and authorization | `src/CivicLens.Application/Review/ReviewValidation.cs` |
| Paging | `src/CivicLens.Application/Paging/PageCursor.cs`, `src/CivicLens.Application/Paging/KeysetSlice.cs`, `src/CivicLens.Application/Paging/PageLimit.cs` |
| Draft lists | `src/CivicLens.Application/Review/DraftListFilter.cs`, `src/CivicLens.Infrastructure/Review/ListedDrafts.cs`, `src/CivicLens.Infrastructure/Review/RankedDraft.cs`, `src/CivicLens.Infrastructure/Review/ListedDraftRows.cs`, `src/CivicLens.Infrastructure/Review/ReviewKeyset.cs`, `src/CivicLens.Infrastructure/Review/Persistence/IListedDraftRow.cs`, `src/CivicLens.Infrastructure/Review/Persistence/ListedDraftTableConfiguration.cs`, `src/CivicLens.Host/Review/DraftListPage.cs`, `src/CivicLens.Host/Review/ReviewOfficials.cs`, `src/CivicLens.Host/Review/DraftFilterForm.razor` |
| Shared store core | `src/CivicLens.Infrastructure/Review/ReviewRecords.cs`, `src/CivicLens.Infrastructure/Review/ReviewHistory.cs`, `src/CivicLens.Core/Review/IReviewRevision.cs`, `src/CivicLens.Infrastructure/Review/Persistence/IReviewDraftRow.cs`, `src/CivicLens.Infrastructure/Review/Persistence/IReviewRevisionRow.cs`, `src/CivicLens.Infrastructure/Review/Persistence/IReviewDecisionRow.cs` |
| Postgres store and helpers | `src/CivicLens.Infrastructure/Review/PostgresDocumentChangeReviewStore.cs`, `src/CivicLens.Infrastructure/Review/ReviewIdempotency.cs`, `src/CivicLens.Infrastructure/Review/ReviewDecisionHistory.cs`, `src/CivicLens.Infrastructure/Review/ReviewKeyset.cs`, `src/CivicLens.Infrastructure/Review/ReviewJson.cs`, `src/CivicLens.Infrastructure/Review/ReviewQueries.cs`, `src/CivicLens.Infrastructure/Review/CitedRevisions.cs` |
| Pages | `src/CivicLens.Host/Review/ReviewHome.razor`, `src/CivicLens.Host/Review/UnapprovedFact.cs`, `src/CivicLens.Host/Documents/DraftEditor.razor`, `src/CivicLens.Host/Review/ReviewDecisionPanel.razor`, `src/CivicLens.Host/Review/ReviewHistoryPanel.razor`, `src/CivicLens.Host/Review/ReviewRecordPage.cs`, `src/CivicLens.Host/Documents/DocumentRoutes.cs`, `src/CivicLens.Host/Review/RecordRoutes.cs` |
| Triage | `src/CivicLens.Application/Review/ApprovalCheck.cs`, `src/CivicLens.Host/Statements/StatementTriage.razor`, `src/CivicLens.Host/Statements/TriageTarget.cs`, `src/CivicLens.Host/Statements/TriageTargets.cs`, `src/CivicLens.Host/Review/ReviewCommand.cs`, `src/CivicLens.Host/Statements/TriageDecisionControls.razor`; the statement files are in [statements](statements.md#code-map) |
| Draft lookup | `src/CivicLens.Application/Review/LookupDraft.cs`, `src/CivicLens.Application/Review/DraftLookupHit.cs`, `src/CivicLens.Application/Review/DraftLookupFormatException.cs`, `src/CivicLens.Host/Review/ReviewHome.razor` |
| Brief review | `src/CivicLens.Host/Briefs/BriefList.razor`, `src/CivicLens.Host/Briefs/BriefEditor.razor`, `src/CivicLens.Infrastructure/Briefs/PostgresBriefReviewStore.cs`; see [briefs](briefs.md#code-map) |
| Relationship review | `src/CivicLens.Host/Relationships/RelationshipList.razor`, `src/CivicLens.Host/Relationships/RelationshipEditor.razor`, `src/CivicLens.Infrastructure/Relationships/PostgresRelationshipReviewStore.cs`; see [statements](statements.md#relationship-review) |
| Reader report queue | `src/CivicLens.Application/Reports/`, `src/CivicLens.Infrastructure/Reports/PostgresReaderReportStore.cs`, `src/CivicLens.Infrastructure/Reports/Persistence/`, `src/CivicLens.Host/Reports/ReportList.razor`, `src/CivicLens.Host/Reports/ReportDetail.razor`, `src/CivicLens.Host/Reports/ReaderReportBanner.razor`, `src/CivicLens.Host/Reports/ReportRoutes.cs`, `src/CivicLens.Host/Reports/ReportLabels.cs`; the pull is in [search](search.md#operational-pull) |
| Workspace settings | `src/CivicLens.Host/Review/ReviewWorkspace.cs`, `src/CivicLens.Host/Review/ReviewWorkspaceSettings.cs` |

## Limits and planned work

- Live Auth0 tenant verification is pending. Both reviewers will reach the shared production workspace through their browsers over Tailscale; see [host](host.md#limits-and-planned-work).
- The official filter, the official chips of the document-change editor, and the official IDs that a save accepts all come from `ReviewOfficials`. It lists the `People` of the collection configuration (`CIVIC_LENS_COLLECTION_CONFIG`) with their names, with no limit on the count. Without that file the choices are empty and every `official` value is invalid.
- The filters and the order are computed for each page over the filtered set. This is fine at the present volume.
- The tab counts ignore the issue, official, and date filters. They count every record of the status.
- Reads show the current review cycle only. A page of older cycles is future work.
- A read trusts the approval at the start of a cycle and does not check it against older concerns again. The decide step checked it under the lock when it was written.
- Local MCP will inspect jobs, explain processing, inspect source health, compare analysis runs, and run explicitly budgeted evaluations.
- MCP review tools will list and read cases, propose corrections, and record explicit decisions. Mutations will use expected versions and idempotency keys. Initial local tooling is read-only.
- AI suggestions, model predictions, and human decisions stay distinct records.
- D-03 decides the dynamic search across retained evidence and review records. Integrated visual comparison is planned and has no roadmap item.
- Global search and expanded reviewer source visibility are pending decisions. Add an evidence library page once document search exists.
- The analysis queues claim work by priority, so the drafting budget goes to the records that matter most first; see [analysis](analysis.md#storage-and-contracts). The action page lists the vote drafts of its bill, and the vote page links the action draft; see [actions](actions.md#behavior). The home page finds a draft by bill or roll call; see [finding a draft](#finding-a-draft).
- A reviewer approves a weekly brief on its own. A brief cites records that are already published, and each cited record keeps its own decision bound to its exact revision. Approval needs every cited revision to be approved and in the active release. Model output never approves or publishes. See [briefs](briefs.md#limits-and-planned-work).
- See [roadmap](../roadmap.md) for order.
