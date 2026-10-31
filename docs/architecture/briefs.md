# Briefs

## Purpose

The briefs feature drafts one weekly brief from the records that Civic Lens published during one week. A brief is short text with cited records: statements, document changes, and action records. A person reviews and approves each brief. The action records that it cites carry an approval by code or by a person. A model prediction is never a human decision.

The feature stores, queues, selects, drafts, and reviews briefs. A person edits, requests changes, approves, and withdraws approval on private pages. The worker publishes an approved brief in a release, when the active release holds the records that it cites. See [Publication](#publication).

## Decisions

- A week runs from Monday 00:00 to the next Monday 00:00 in the `America/New_York` time zone. Postgres computes the bounds from its own clock, so a clock change is exact. A week lasts 167, 168, or 169 hours.
- One brief covers one week. The record ID is the SHA-256 of `brief|{week start}`, for example `brief|2026-10-05`.
- Only a closed week is drafted. The first run with briefs enabled stores its Monday as the first brief week. Every closed week from then on is queued. Earlier weeks are never queued by the worker, but the owner can redraft any week.
- A brief cites records that were already published. It does not wait for the approval of those records. Each citation names the exact revision that the release holds.
- A record is new in a week when the cutoff release has it and its first release was published in the week. The cutoff release is the newest release published before the week ends. The match uses the kind and the record ID.
- A new revision of a record that was published before the week is not new. A record that an earlier release dropped and a later release lists again stays in the week of its first release. The brief does not repeat it.
- The set of new records is fixed by the two releases. The text of each record comes from its revision in the active release at the time of the read. A requeued brief therefore sees a revision that a later release published.
- A record that the active release no longer holds is left out. A record is left out too when the latest review decision on its revision in the active release is not an approval. A later draft revision does not change that. The coverage counts every left-out record as considered and not sent.
- Actions on the same bill form one group. Every other record is its own group.
- Groups rank by the number of records, then the number of distinct officials, then the bill movement. Bill movement is the number of sponsorship entries of the group. A group of another kind scores 0. The group key in ascending order breaks a tie.
- The prompt takes whole groups in rank order up to the byte cap and up to `BriefRevision.MaximumCitations` records. Filling stops at the first group that does not fit either limit. Code assigns the IDs `R1`, `R2`, and so on after the cut, in order.
- The model need not cite every record that it receives. Code builds the citation list from the IDs that the paragraphs cite. Each paragraph cites at least one ID.
- Coverage records how many records were considered, sent to the model, and cited. Omitted records are the considered records minus the sent records.
- The model text names only officials of the records that it received. `AnalysisAnswerRules.CheckNamesOutsideFacts` owns that check, and the action resolver calls it too.
- A week with no new records makes no model call and no revision. The queue entry settles as `noNewRecords`.
- A week whose new records are all left out makes no model call and no revision. The queue entry settles as Blocked with `noCitableRecords`.
- The fingerprint covers the cutoff release and the sorted kind, record, and revision of each sent record. The same fingerprint settles as `unchanged`. A new revision of a record, a dropped record, or a withdrawn approval changes the fingerprint.
- The week of a record is the brief week of the first release that lists the record. The first release has the lowest release number.
  - Three events requeue the brief of the week of each touched record: a publication commit, an activation of another release, and a review decision.
  - The decision must be on the revision that the active release holds.
  - A brief that is approved is never requeued.
  - A Succeeded entry, a Running entry, and a Blocked `noCitableRecords` entry move back to Pending.
  - A Running worker then loses its lease and cannot save its draft.
- The stored release time is the database time of the commit. The selection read takes the brief lock, so it waits for a commit in flight and never misses a release of the week.
- Only a week that has ended can be redrafted. The database clock decides.
- The worker enqueues closed weeks at the start of each pass. A timed wakeup at the next Monday boundary starts a pass when no other work is due.
- The model text carries `textOrigin` and an optional `suggestion` like an action record. A suggestion holds its own citations, because citation IDs are only valid inside one selection.
- A person edits the headline, the paragraph text, and which listed citations each paragraph carries. A brief keeps 1 to 12 paragraphs. The week window, the fingerprint, and the record set stay code-owned.
- A person picks citations only from those that the revision already lists. A citation that no paragraph uses any more leaves the list at the save. Code counts the cited records again.
- A person may keep or clear the suggestion after an edit. A person never changes it. A person may also adopt the suggestion. Adoption replaces the headline, the paragraphs, and the citations with those of the suggestion as one unit, and clears the suggestion.
- Review of briefs uses the draft table, so the list shows drafted briefs only. A week without a brief revision has no row.
- The editor shows the headline and the summary of the exact cited revision for each citation. It links to the review page of that record. One batch read of the exact kind, record, and revision triples feeds it.
- A citation has changed when the active release holds another revision of the record or none. It has also changed when the latest decision on the cited revision is not an approval. A brief is flagged "cited records changed" when any citation has changed. Code derives the flag at each read. No column or lock holds it.
- Approval needs an unflagged brief. The approval gate and the flag share one read, `BriefRecordReader.FindChangedAsync`.
- A flagged brief that is approved stays approved. This differs on purpose from actions, where a worker revision gets a new code approval when the rules allow it. The approval of a brief covers the text of the reviewer, which a record change does not rewrite.
- The remedy for a flagged approved brief is a person Withdraw. The page also shows the flag on the list. Publication of briefs refuses a flagged brief; see [Publication](#publication).
- A reviewer reads each brief before approval.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- One brief exists for each week. The record ID names the week. Enforced by `BriefWeek.RecordIdOf`, `BriefRevision.ValidateShape`, the key of `brief_drafts`, and `ux_brief_drafts_week`. Tests: `BriefWeekTests.TheRecordIdIsTheSha256OfTheWeekSoEveryRunFindsTheSameBrief`, `BriefRevisionTests.TheRecordIdMustNameTheWeekSoNoTwoBriefsCoverTheSameWeek`, and `PostgresBriefDraftStoreTests.OneWeekHasOneBriefAndOneQueueRowBecauseTheWeekIsUnique`.
- A week starts on a Monday. Enforced by `BriefWeek.RequireMonday` and the checks `ck_brief_drafts_week` and `ck_brief_analysis_week`. Tests: `BriefWeekTests.AWeekMustStartOnAMondayBecauseTheBriefPeriodIsMondayToMonday` and `PostgresBriefDraftStoreTests.TheBriefTableRefusesAWeekThatDoesNotStartOnAMondayOrAStatusThatThePolicyNeverStores`.
- The week bounds are Monday midnight to Monday midnight in New York, with the clock change exact. Enforced by `BriefWeekSql`. Tests: `PostgresBriefDraftStoreTests.TheWeekRunsFromMondayMidnightToTheNextMondayMidnightInNewYorkWithTheClockChangeExact` and `BriefRevisionTests.AWeekLastsOnlyAsLongAsAClockChangeAllows`.
- Only closed weeks from the first brief week are queued, and a second enqueue changes nothing. Enforced by `BriefWeek.ClosedWeeks` and `PostgresBriefDraftStore.EnqueueClosedWeeksAsync`. Tests: `PostgresBriefDraftStoreTests.TheFirstEnqueueStoresThisMondayAsTheFirstWeekAndQueuesNothingBecauseThisWeekIsStillOpen`, `PostgresBriefDraftStoreTests.EnqueueAddsEveryClosedWeekFromTheFirstWeekAndNeverEarlierOnes`, and `PostgresBriefDraftStoreTests.EnqueueIsIdempotentAndLeavesAFinishedWeekAlone`.
- A record is new only when the cutoff release has it and its first release was published in the week. Enforced by `PostgresBriefDraftStore.ReadNewRecordsAsync` and `BriefFirstRelease`, which `BriefStaleness` shares. Tests: `PostgresBriefDraftStoreTests.ARecordIsNewWhenTheCutoffReleaseHasItAndTheReleaseBeforeTheWeekDoesNot`, `PostgresBriefDraftStoreTests.ARecordBelongsOnlyToTheWeekOfItsFirstReleaseEvenWhenAnEarlierReleaseDroppedItAndALaterOneListsItAgain`, `PostgresBriefDraftStoreTests.ANewRevisionOfARecordThatWasPublishedBeforeTheWeekIsNotNewSoTheBriefNeverRepeatsIt`, and `PostgresBriefDraftStoreTests.AReleaseAfterTheWeekEndsNeverEntersTheWeek`.
- An old release entry without a kind is a document change. Enforced by `ReviewQueries.ReadReleaseRecordsAsync`. Test: `PostgresBriefDraftStoreTests.AnOldReleaseEntryWithoutAKindIsADocumentChangeSoOldReleasesStillCount`.
- The brief reads each record at its revision in the active release, and a missing revision fails the step. Enforced by `PostgresBriefDraftStore.AtActiveRevisionsAsync` and `CitedRevisions.ReadRevisionJsonAsync`, through `BriefRecordReader`. Tests: `PostgresBriefDraftStoreTests.TheBriefReadsTheExactRevisionTheReleaseNamesAndTheOfficialsOfEachRecord` and `PostgresBriefDraftStoreTests.AReleaseThatNamesARevisionThatIsNotStoredFailsLoudInsteadOfDroppingTheRecord`.
- The brief leaves out a record and counts it as considered when the active release dropped it. It also leaves out a record when the latest decision on its published revision is not an approval. Enforced by `PostgresBriefDraftStore.AtActiveRevisionsAsync`, `BriefRecordReader.ReadAsync`, `CitedRevisions.FindNotApprovedAsync`, and `BriefDraftContext.Create`. Tests: `PostgresBriefDraftStoreTests.ARecordTheActiveReleaseNoLongerHoldsIsLeftOutAndCounted`, `PostgresBriefDraftStoreTests.ARecordWhoseLatestDecisionOnThePublishedRevisionIsNotAnApprovalIsLeftOutAndCounted`, `PostgresBriefDraftStoreTests.ARecordApprovedAgainAfterAWithdrawalOfThePublishedRevisionIsInTheSelectionAgain`, `PostgresBriefDraftStoreTests.ADraftOnANewerRevisionDoesNotWithdrawThePublishedRevisionBecauseTheActiveReleaseStillServesIt`, `PostgresBriefDraftStoreTests.ARequeueAfterAnApprovalIsWithdrawnAppendsARevisionThatLeavesTheRecordOut`, `PostgresStatementReviewStoreTests.AnEditAfterAWithdrawalStillLeavesThePublishedRevisionOutBecauseTheNewRevisionIsNotApproved`, and `BriefDraftResolverTests.RecordsLeftOutBeforeTheSelectionStillCountAsConsideredSoTheCoverageShowsThem`.
- A requeue reads the revision that the active release holds now, so a later revision gives a new brief revision. Enforced by `PostgresBriefDraftStore.AtActiveRevisionsAsync`. Tests: `PostgresBriefDraftStoreTests.ARequeueReadsEachRecordAtItsRevisionInTheActiveReleaseAndKeepsTheWeekSetFixed` and `PostgresBriefDraftStoreTests.ARequeueAfterANewRevisionIsPublishedAppendsARevisionThatReadsTheNewRevision`.
- The selection read waits for a commit in flight. Enforced by `PostgresBriefDraftStore.ReadEvidenceAsync` under the `PostgresAdvisoryLock.BriefReview` key. Test: `PostgresBriefDraftStoreTests.TheSelectionReadWaitsForACommitInFlightBecauseItTakesTheBriefLock`.
- The prompt keeps `BriefDraftTask.RetryReasonReserveBytes` free, so a retry with rejection reasons still fits the byte cap. Enforced by `BriefDraftStep.FitsWithRetryReserve`. Test: `BriefDraftStepTests.TheSelectionKeepsRoomForRetryReasonsSoARejectedAnswerStillGetsItsSecondCall`.
- Groups rank by size, officials, movement, and key. A group enters whole or not at all. IDs have no gap. The prompt never offers more records than a brief can cite. Enforced by `BriefSelection`. Tests: `BriefSelectionTests.FillingStopsAtTheCitationLimitAndTheRestCountAsConsidered`, `BriefDraftStepTests.AWeekOfMoreRecordsThanABriefCanCiteOffersOnlyTheLimitSoAnAnswerCitingThemAllGetsTheCorrectiveRetry`, `BriefSelectionTests.GroupsRankByRecordCountThenDistinctOfficialsThenBillMovementThenKey`, `BriefSelectionTests.AGroupEntersWholeOrNotAtAllAndFillingStopsAtTheFirstGroupThatDoesNotFit`, and `BriefSelectionTests.CitationIdsAreAssignedAfterTheCutInOrderSoTheyNeverHaveAGap`.
- A paragraph cites at least one listed ID, once. An unknown ID is rejected. Enforced by `BriefDraftResolver` and `BriefRevision.ValidateShape`. Tests: `BriefDraftResolverTests.AnIdThatWasNeverListedIsRejectedAsACitationError`, `BriefDraftResolverTests.AnIdRepeatedInOneParagraphIsRejected`, `BriefDraftResolverTests.AParagraphWithoutAnIdIsRejected`, and `BriefRevisionTests.AParagraphCannotCiteTheSameIdTwiceOrAnIdTheBriefDoesNotList`.
- The model need not cite every record that it received. Enforced by `BriefDraftResolver`. Test: `BriefDraftResolverTests.TheModelNeedNotCiteEveryRecordSentToIt`.
- The coverage counts the listed citations, and omitted records are considered minus sent. Enforced by `BriefRevision.ValidateShape` and `BriefCoverage`. Tests: `BriefDraftResolverTests.ConsideredCountsRecordsTheByteCapLeftOutSoTheCoverageShowsWhatTheBriefOmits` and `BriefRevisionTests.CoverageMustCountTheListedCitationsAndCannotSendMoreThanItConsidered`.
- The text names no configured person who is not an official in the records sent. Enforced by `AnalysisAnswerRules.CheckNamesOutsideFacts`. Tests: `BriefDraftResolverTests.ATextThatNamesAConfiguredPersonWhoIsNotAnOfficialInTheSentRecordsIsRejected` and `BriefDraftResolverTests.ARecordLeftOutByTheCutDoesNotCountAsAnOfficialInTheFacts`.
- The prompt lists only the officials of the records sent. Enforced by `BriefDraftPrompt`. Test: `BriefDraftStepTests.ThePromptListsOnlyTheOfficialsOfTheRecordsSentAndNoOtherConfiguredPerson`.
- A week with no new records makes no call and no revision. Enforced by `BriefDraftStep`. Tests: `BriefDraftStepTests.AWeekWithNoNewRecordsMakesNoModelCallAndNoRevisionAndSettlesWithABypassedRun` and `PostgresBriefDraftStoreTests.AWeekWithNoNewRecordsSettlesWithNoBriefAndABypassedRun`.
- A week whose new records are all left out blocks as `noCitableRecords` and does not succeed. Enforced by `BriefDraftStep`. Test: `BriefDraftStepTests.AWeekWhoseConsideredRecordsAreAllLeftOutBlocksInsteadOfClaimingThatNothingWasPublished`.
- The same fingerprint settles without a call. A new record or a new revision of a record makes a new revision. Enforced by `BriefDraftStep` and `BriefFingerprint`. Tests: `BriefDraftStepTests.TheSameRecordsAndCutoffSettleUnchangedWithoutACallBecauseTheFingerprintMatches`, `BriefDraftStepTests.ANewRecordOrANewRevisionOfARecordAppendsRevisionTwoWithANewFingerprint`, and `BriefSelectionTests.TheFingerprintIgnoresInputOrderAndChangesWithTheRevisionOrTheCutoff`.
- A group that alone exceeds the byte cap blocks the week as `overByteCap`. Enforced by `BriefDraftStep`. Test: `BriefDraftStepTests.AGroupThatAloneExceedsTheByteCapBlocksTheWeekWithoutACall`.
- Model confidence never reaches a revision. Enforced by `BriefDraftStep`. Test: `BriefDraftStepTests.ConfidenceStaysInTheRunOutputAndNeverReachesTheRevision`.
- A model revision never lands on an approved brief. The run keeps the outcome `DraftDiscarded` and the week settles as `unchanged`. Enforced by `BriefDraftRows.AppendAnalysisRevisionAsync` and `BriefDraftQueueMapping`. Test: `PostgresBriefDraftStoreTests.AnApprovalThatLandsWhileTheModelRunsLeavesTheApprovedRevisionAndSettlesTheWeekAsUnchanged`.
- An append needs the expected current revision. Enforced by `BriefDraftRows` under the `PostgresAdvisoryLock.BriefReview` key. Test: `PostgresBriefDraftStoreTests.ARevisionThatFindsADifferentCurrentRevisionIsDiscardedAndRetriedSoTwoDraftersNeverShareARevisionNumber`.
- The text of a person stays, and model text becomes a suggestion with its own citations. Enforced by `BriefRevision.ValidateAsAnalysisRevision`. Tests: `BriefRevisionTests.AnAnalysisRevisionNeverOverwritesTheTextOfAPersonAndOffersItsOwnAsASuggestion` and `BriefDraftResolverTests.WhenAPersonWroteTheCurrentTextTheModelTextOnlyBecomesASuggestionWithItsOwnCitations`.
- A publication commit requeues the brief of the week of each record that it changes, drops, or adds again. Enforced by `BriefStaleness.RequeueAfterPublicationAsync` in the `PostgresPublicationStore` commit transaction, through `PostgresAnalysisQueue.RequeueInAsync`. Tests: `BriefStalenessTests.APublishThatChangesARevisionRequeuesOnlyTheBriefOfTheWeekThatFirstPublishedTheRecord`, `BriefStalenessTests.APublishThatChangesARecordOfAnEarlierWeekLeavesTheBriefOfThisWeekAlone`, `BriefStalenessTests.APublishThatDropsARecordRequeuesTheBriefOfItsWeekForEachKind`, `BriefStalenessTests.APublishThatOnlyAddsARecordLeavesTheFinishedBriefAloneBecauseTheNewRecordBelongsToTheOpenWeek`, `BriefStalenessTests.ARecordPublishedAgainAfterItWasDroppedRequeuesTheWeekThatFirstPublishedIt`, `BriefStalenessTests.AChangedRecordRequeuesABlockedWeekOnlyWhenItHadNoRecordToCite`, `BriefStalenessTests.APendingBriefStaysAsItIsBecauseItReadsTheSelectionWhenItRunsAnyway`, `BriefStalenessTests.ARunningBriefIsRequeuedAndItsLateSettlementIsRefusedSoItCannotSaveAStaleRevision`, and `PostgresBriefDraftStoreTests.AStaleSignalWhileTheBriefIsRunningLeadsToANewDraftAndNeverASavedStaleRevision`.
- An activation of another release requeues the brief of the week of each record that differs between the old and the new active release. A record differs in its revision or in its presence. Enforced by `BriefStaleness.RequeueAfterActivationAsync` in `PostgresPublicationStore.ActivateAsync`. Tests: `BriefStalenessTests.ARollbackRequeuesTheBriefOfTheAffectedWeekAndTheRedraftReadsTheOlderRevision` and `BriefStalenessTests.ARollbackToAReleaseWithoutTheRecordsRequeuesTheBriefAndTheRedraftLeavesThemOut`.
- A review decision on the revision that the active release holds requeues the brief of the week of the record, in the same transaction. A decision on any other revision does not. Enforced by `BriefStaleness.AfterDecision`, which each review store passes to `ReviewRecords.DecideAsync`. Tests: `PostgresStatementReviewStoreTests.WithdrawingAnApprovalRequeuesTheBriefOfTheWeekAndTheRedraftLeavesTheRecordOut`, `PostgresStatementReviewStoreTests.ApprovingTheSameRevisionAgainAfterAWithdrawalRequeuesTheBriefAndTheRecordReturns`, `PostgresStatementReviewStoreTests.RequestingChangesAfterAnApprovalOfThePublishedRevisionRequeuesTheBriefAndTheRecordLeavesIt`, `PostgresStatementReviewStoreTests.ARecordWithdrawnApprovedAgainAndPublishedAtANewRevisionReturnsToTheBrief`, `PostgresStatementReviewStoreTests.ADecisionOnARevisionThatTheActiveReleaseDoesNotServeLeavesTheBriefAlone`, `PostgresDocumentChangeReviewStoreTests.ADecisionOnTheRevisionThatTheActiveReleaseServesRequeuesTheBriefOfItsWeek`, and `PostgresBillStreamTests.ADecisionOnTheRevisionThatTheActiveReleaseServesRequeuesTheBriefOfItsWeek`.
- Week selection and staleness read only the kinds that a brief can cite. A published brief is never a new record of a week, and it never requeues a week. `BriefCitationKinds.IsCitable` is the one owner of the citable kinds. Enforced by `PostgresBriefDraftStore.ReadNewRecordsAsync` and `BriefStaleness.RequeueWeeksOfAsync`. Tests: `PostgresBriefPublicationTests.ABriefInTheReleaseIsNeverANewRecordOfItsWeekSoPublishingItDoesNotChangeTheBriefOfItsOwnWeek`, `PostgresBriefPublicationTests.ActivatingAReleaseThatAddsABriefRequeuesNoWeekBecauseAPublishedBriefIsNotACitableRecord`, and `PostgresBriefPublicationTests.ActivatingAReleaseThatChangesACitableRecordStillRequeuesItsWeek`.
- The record reader refuses a kind that a brief cannot cite. It never reads the action tables for it. Enforced by the `BriefCitationKind` parameter type of `BriefRecordReader.SourceOf`. No test.
- Brief publication rules, the brief file shape, and the contract limits are in [publication](publication.md#invariants).
- The public page names the week, the cutoff release, the coverage counts, and the sample note, and it links each citation to its record page. Enforced by `PublishedBriefPage`. Test: `ReleaseRendererTests.BriefPageShowsEachCitationAsALinkToItsRecordPageWithTheCopiedHeadlineTheWeekTheCutoffAndTheSampleNote`.
- A brief that is approved is never requeued by any of the three events. Enforced by the review status filter of `BriefStaleness`. Tests: `BriefStalenessTests.ABriefThatIsApprovedIsNeverRequeuedByAPublishOrARollbackBecauseAReviewerHasAlreadyTakenResponsibilityForIt` and `PostgresStatementReviewStoreTests.ADecisionLeavesAnApprovedBriefAloneBecauseAReviewerAlreadyOwnsIt`.
- A person revision keeps the code-owned part and changes only the headline, the paragraphs, and their listed citations. Enforced by `BriefRevision.ValidatePersonEditOf`, which `PostgresBriefReviewStore.SaveRevisionAsync` calls under the lock. Tests: `BriefRevisionTests.APersonEditsTheHeadlineTheParagraphsAndTheirListedCitationsButNeverTheWeekTheFingerprintOrTheRecordCounts` and `PostgresBriefReviewStoreTests.AStaleSaveIsAConflictAndKeepsTheNewerRevisionSoThePageCanShowBothVersions`.
- A person keeps only citations that the revision already lists, or adopts the suggestion as one unit. Adoption takes the headline, the paragraphs, and the citations of the suggestion, clears it, and counts the coverage again. A mix of the two is refused. Enforced by `BriefRevision.ValidatePersonEditOf` and `BriefRevision.ValidateShape`. Tests: `BriefRevisionTests.APersonAdoptsTheSuggestionOnlyAsOneUnitOfHeadlineParagraphsAndCitationsAndTheSuggestionIsCleared`, `BriefRevisionTests.AdoptingTheSuggestionWithAlteredCitationsIsRefusedBecauseThePersonMayNotRebindACitationToAnotherRecord`, `PostgresBriefReviewStoreTests.AdoptingTheSuggestionSavesItsHeadlineParagraphsAndCitationsAsOneUnitAndClearsIt`, `PostgresBriefReviewStoreTests.AdoptionRefusesTextThatIsNotTheSuggestionAndARevisionWithoutASuggestion`, `PostgresBriefReviewStoreTests.AfterAWithdrawalAndARedraftAPersonRecoversABriefWhoseCitedRecordChangedByAdoptingTheSuggestion`, `BriefRevisionTests.APersonKeepsOnlyCitationsThatTheBriefAlreadyListsSoNoPersonCanCiteARecordTheDrafterNeverSelected`, `PostgresBriefReviewStoreTests.TheStoreRefusesACitationThatTheCurrentRevisionDoesNotListEvenWhenTheEditIsOtherwiseWellFormed`, and `PostgresBriefReviewStoreTests.APersonCannotCiteAnIdThatTheBriefDoesNotListSoTheHandlerRefusesTheSave`.
- A citation that no paragraph uses leaves the list, and the coverage counts the rest. Enforced by `SaveBriefDraft` and `BriefRevision.ValidateShape`. Tests: `PostgresBriefReviewStoreTests.ASaveDropsACitationThatNoParagraphUsesAnymoreAndRecomputesTheCoverage` and `BriefRevisionTests.APersonMayDropACitationThatNoParagraphUsesAnymoreWhenTheCoverageCountsTheRest`.
- A person keeps or clears the suggestion and never changes it. Enforced by `BriefRevision.ValidatePersonEditOf` and the structural equality of `BriefTextSuggestion`. Tests: `BriefRevisionTests.APersonMayKeepOrClearTheSuggestionButNeverChangeItEvenWhenTheCopyIsEqualInContent` and `PostgresBriefReviewStoreTests.APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne`.
- A rerun after a person edit keeps the text of the person and stores the model text as a suggestion. Enforced by `BriefDraftResolver` and `BriefRevision.ValidateAsAnalysisRevision`. Test: `PostgresBriefReviewStoreTests.ARerunAfterAPersonEditKeepsTheTextOfThePersonAndStoresTheModelTextAsASuggestion`.
- A decision binds to the exact brief revision. Enforced by `ReviewWriteRules` through `PostgresBriefReviewStore.DecideAsync`. Test: `PostgresBriefReviewStoreTests.ApprovalBindsToTheExactBriefRevisionSoASaveAfterThePageLoadedTurnsItIntoAConflict`.
- Approval needs every cited revision to be approved and in the active release. Enforced by `PostgresBriefReviewStore.DecideAsync` through `BriefRecordReader.FindChangedAsync`. Tests: `PostgresBriefReviewStoreTests.ApprovalIsRefusedWhenACitedRevisionIsNotApprovedAndInTheActiveReleaseAndTheFlagNamesIt` and `BriefReviewWorkspaceTests.ApprovalWithAChangedCitedRecordIsRefusedWithABadRequestAndNoDecision`.
- The cited records changed flag uses the rule of the approval gate and changes no stored state. Enforced by `BriefRecordReader.FindChangedAsync`. Tests: `PostgresBriefReviewStoreTests.ApprovingTheCitedRecordAgainClearsTheFlagAndLetsTheBriefBeApproved`, `PostgresBriefReviewStoreTests.AnApprovedBriefStaysApprovedWhenACitedRecordChangesButTheFlagShowsItSoOnlyAPersonCanWithdraw`, and `BriefReviewWorkspaceTests.AChangedCitedRecordShowsTheFlagOnTheListAndInTheEditorWhileTheBriefStaysApproved`.
- The list shows drafted briefs only, and the counts use the status rule of the filters. Enforced by `PostgresBriefReviewStore.ListAsync` and `CountAsync`. Tests: `PostgresBriefReviewStoreTests.TheListShowsDraftedBriefsOnlyAndCountsEachFilterWithTheSameStatusRule` and `BriefReviewWorkspaceTests.TheRailLinksTheBriefListWhichCountsEachFilterAndListsDraftedBriefsOnly`.
- Only a reviewer reads, saves, or decides, and the analysis author cannot decide. Enforced by `ReviewAuthorization` in each handler and by `ReviewPolicy`. Tests: `PostgresBriefReviewStoreTests.OnlyAReviewerMayReadSaveOrDecideBecauseTheHandlersCheckTheRoleBeforeTouchingTheStore`, `PostgresBriefReviewStoreTests.TheAnalysisAuthorCannotDecideSoAModelPredictionIsNeverAHumanApproval`, and `BriefReviewWorkspaceTests.OnlyAConfiguredSubjectMayOpenTheBriefPagesAsync`.
- A brief that does not exist is not found. Enforced by `GetBriefReview`, `PostgresBriefReviewStore`, and `BriefEditor`. Tests: `PostgresBriefReviewStoreTests.ABriefThatDoesNotExistIsNotFoundAndSavingOrDecidingOnItFailsLoud` and `BriefReviewWorkspaceTests.AnUnknownOrMalformedBriefIsNotFoundAsync`.
- A stale save returns a conflict and shows both versions with the cited headlines of each paragraph. Enforced by `ReviewRecordPage` and `BriefEditor`. Test: `BriefReviewWorkspaceTests.AStaleSaveReturnsConflictKeepsTheTypedWordingAndShowsBothVersions`.
- A conflict clears each ticked citation whose record or revision changed since the form loaded, so the save fails until the reviewer chooses again. The form carries the loaded citations. Enforced by `BriefEditor.BriefInput.KeepCitationsUnchangedSinceLoad`. Test: `BriefReviewWorkspaceTests.AConflictClearsEachTickedCitationThatNowNamesAnotherRecordSoTheReviewerMustChooseAgain`.
- An invalid save is a bad request and a forged expected revision is a conflict, never a server error. Enforced by `ReviewRecordPage.RunCommandAsync`. Test: `BriefReviewWorkspaceTests.AnInvalidSaveIsABadRequestWithNoNewRevisionAndAForgedExpectedRevisionTakesTheComparePath`.
- The editor shows the exact cited revision inline with a link to its review page. Enforced by `PostgresBriefReviewStore.GetAsync` and `BriefEditor`. Test: `BriefReviewWorkspaceTests.TheEditorShowsTheExactCitedRevisionInlineWithALinkToTheReviewPageOfEachRecord`.
- The form binds each paragraph to its ticked citations. Enforced by `BriefEditor`. Test: `BriefReviewWorkspaceTests.ASaveBindsEachParagraphToItsTickedCitationsAndDropsACitationNoParagraphKeeps`.
- The brief lock is last in the lock order, and a brief queue row lock comes before it. Enforced by `PostgresAdvisoryLock.ReviewKeysBeforeBriefs` and `BriefStaleness.TakeLocksAsync`. Tests: `BriefStalenessTests.ThePublishWaitsForTheBriefLockLastInTheOrderBecauseAnAppendOfABriefMustNotSlipInDuringIt` and `BriefStalenessTests.ThePublishDoesNotHoldTheBriefLockWhileItWaitsForAnEarlierLockSoTheBriefLockStaysLastInTheOrder`.
- An owner redraft moves only finished weeks back to Pending. Only the owner may run it. Only a week that has ended can be redrafted. Enforced by `PostgresBriefDraftStore.RedraftAsync` and `RedraftBrief`. Tests: `PostgresBriefDraftStoreTests.RedraftRefusesAWeekThatHasNotEndedBecauseItsRecordsCanStillChange`, `PostgresBriefDraftStoreTests.RedraftPutsAFinishedWeekBackToPendingWithItsAttemptsCleared`, `PostgresBriefDraftStoreTests.RedraftLeavesARunningWeekAloneSoItNeverStealsALeaseFromAWorker`, and `RedraftBriefTests.OnlyTheOwnerMayRedraftAWeek`.
- The worker queues closed weeks before every pass. Enforced by the `beforeEachPass` hook of `AnalysisWorker`. Test: `BriefDraftStepTests.TheWorkerQueuesClosedWeeksBeforeEveryPassSoAWeekThatJustEndedIsNotMissed`.
- The wakeup fires at the next Monday 00:00 in New York time. Enforced by `BriefWeekSql.NextBoundaryTicks` in the deadline query of `PostgresAnalysisWakeup`. Test: `PostgresBriefDraftStoreTests.TheBriefWakeupFiresAtTheNextMondayMidnightInNewYorkEvenWithNoQueuedWork`.
- `BriefDraftTask` pins the prompt, the schema, and one input hash. Enforced by the `BriefDraftTask` versions. Test: `BriefDraftPromptGoldenTests.ThePromptAndSchemaMatchTheirPinnedVersions`.

## Behavior

### Ownership

The step runs on the shared analysis owners: `AnalysisWorker<TRun>`, `AnalysisValidationLoop`, `PostgresAnalysisQueue`, `AnalysisAnswerRules`, `AnalysisSchema`, `AnthropicStructuredCall`, and `AnalysisRunRows`. See [analysis](analysis.md#behavior) for the queue lifecycle, budgets, retries, and pauses.

`IBriefDraftStore` extends `IAnalysisQueue`. It adds the evidence read, the run read, the weekly enqueue, and the owner redraft. The review reads and writes are in `IBriefReviewStore`; see [Brief review](#brief-review).

`ReviewRecords` is the shared append core for the review stores. `PostgresBriefReviewStore.Records` is the one instance. `BriefDraftRows` and the review store share it for the append, the status computation, and the decisions.

### Weeks

`BriefWeek` holds the pure rules: the record ID, the Monday check, and the list of closed weeks. `BriefWeekSql` holds the time zone arithmetic. It builds the current Monday, the start and end ticks of a week, and the next Monday boundary from the database clock.

### Queue

`brief_analysis` has one entry for each week, with the task `brief-draft`. The key is the record ID, because every run must belong to a 64-character hex subject. The entry has a unique `week_start`.

At the start of each pass, `AnalysisWorker` calls the hook `beforeEachPass`. The brief composition passes `PostgresBriefDraftStore.EnqueueClosedWeeksAsync`. The method does these steps:

1. It stores the current Monday in `brief_schedule` when no row exists. This is the first brief week.
2. It reads the first week and the current Monday from the database clock.
3. It inserts one Pending entry for each closed week with `INSERT ... ON CONFLICT DO NOTHING`. The statement stamps the creation time from the database clock. The method skips this step when no week is closed.

The wakeup adds the next Monday boundary to its deadline query. The boundary is the next Monday 00:00 in New York time, taken from the database clock. The worker then starts a pass at the boundary and queues the week that just closed. The notify trigger on the queue table only wakes the worker. The budget is `brief_analysis_budget`.

### Step

The step does these checks in order:

1. No queue entry for the record: Blocked as `evidenceUnavailable`.
2. No record is left to cite, because the selection left out every new record: Blocked as `noCitableRecords`.
3. No new records: Succeeded as `noNewRecords`. The step writes a Bypassed run and no revision.
4. The selection holds no group, because the first group alone exceeds the byte cap: Blocked as `overByteCap`.
5. The current revision has the same fingerprint: Succeeded as `unchanged`, with a Bypassed run.
6. Otherwise the step calls the model with the validation loop and appends a revision.

The `noNewRecords` and `unchanged` outcomes write a Bypassed run with the reason as its code and the context of the selection. The queue refuses either code without its run.

### Selection

`PostgresBriefDraftStore.ReadEvidenceAsync` reads the queue row, the window, and the records. It runs in one transaction that first takes the brief lock. A commit in flight holds the lock until it ends, so the read sees every release of the week.

1. One query reads the week bounds and the cutoff and previous release numbers. A release counts when its `published_at_utc_ticks` is before the end or the start of the week.
2. One read lists the records of the cutoff release. One query then keeps the records whose first release was published in the week, by kind and record ID. This set does not change after the week ends.
3. One read lists the records of the active release. Each new record takes the revision that the active release holds. A record that the active release lacks is left out.
4. `BriefRecordReader` drops each record whose latest review decision on the published revision is not an approval. One query for each kind reads the latest decision by `review_state_version`, the rule of `ReviewPolicy.GetCurrentStatus`. The reader then reads the exact revisions in one query for each kind and maps them with `BriefRecord.From`.
5. The current brief revision comes from `BriefDraftRows.ReadCurrentAsync`.

The left-out count is the size of the set of new records minus the records that were read. `BriefDraftContext.Create` adds it to the considered count of the selection, so the coverage shows the left-out records as omitted.

A week whose new records are all left out settles as Blocked with `noCitableRecords`. A brief that already exists keeps its text and citations in that case. When a record of that week changes again, staleness requeues the Blocked entry. Staleness leaves other Blocked entries alone.

`BriefSelection` groups and ranks the records and fills the prompt. `BriefDraftContext` adds the fingerprint and the names of the officials in the records sent.

The first release number of the window is the previous release plus one. The cutoff release number is the cutoff release. A revision needs the first number to be at most the cutoff number.

### Fingerprint

`BriefFingerprint` is the SHA-256 of `brief-fingerprint-v1`, the cutoff release number, and one line for each sent record in ordinal order. The line is `kind|record ID|revision number`.

### Prompt and output

The request uses model `claude-sonnet-5-5`, effort `medium`, and at most 8,000 output tokens. The user message holds the week dates and the records sent. Each record has its ID, its kind, its headline, its summary, an optional date, an optional bill, and the names of its officials. The system prompt tells the model to write plain neutral language and to cite only listed IDs. It must name only listed officials and never state the status of a bill.

Structured outputs fix a `headline`, a list of `paragraphs`, and a `confidence`. Each paragraph has a `text` and its `citationIds`. The schema allows only the IDs that were sent. The confidence stays in the run output and is never in the revision.

The byte cap is the smaller of 100,000 UTF-8 bytes and the [call cap](analysis.md#budget) minus the output allowance. Filling the prompt keeps `BriefDraftTask.RetryReasonReserveBytes` (4,096 bytes) free for the rejection reasons of a retry. A first group that does not fit within the cap minus the reserve blocks the week as `overByteCap`.

### Checks

Code rejects the whole answer on any of these:

- A text is empty, over its bound (256 characters for the headline, 4,000 for a paragraph), or has a NUL.
- The paragraph count is not from 1 to 12.
- The confidence is not finite or is outside 0 to 1.
- A paragraph cites no ID, cites an ID that was not sent, or cites an ID twice.
- The paragraphs cite more distinct IDs than `BriefRevision.MaximumCitations`.
- The headline or a paragraph contains the full name of a configured person who is not an official in the records sent, compared without case. A name that is part of a listed name does not count.

A citation error gives the run the outcome `CitationRejected`. Any other error gives `OutputRejected`. The [validation loop](analysis.md#validation-loop) gives one more call with the reasons. After a second rejection, the entry fails with the rejection code.

### Revision and append

The worker appends a new revision to the brief of the week, written by `civic-lens:analysis` with its run ID. `BriefDraftResolver` builds the revision. The citation list holds the sent records that a paragraph cites, in ID order. If the current revision has the origin `Person`, the new revision keeps that text and its citations and stores the model text as the suggestion.

The settlement takes the queue row lock and then `civic-lens-brief-review`. `BriefDraftRows.AppendAnalysisRevisionAsync` returns a refusal code or appends through `ReviewRecords.AppendRevision`. If the current revision number is not one less than the new one, the run is kept as `DraftDiscarded`. The entry moves to RetryWaiting with the code `revisionConflict`. If the brief is approved, the refusal code is `alreadyApproved`. The run is kept as `DraftDiscarded` and the entry settles as Succeeded with `unchanged`. The approved revision stays as it is.

### Brief review

Brief review uses the shared decision rules and review cycles in [review](review.md#review-cycles). The Host has brief pages on top of the shared handlers.

**Ownership.** Core/Briefs holds `BriefReview`, the aggregate of one brief, and the person-edit rule on `BriefRevision`. Application/Briefs holds `SaveBriefDraft`, `DecideBriefReview`, `GetBriefReview`, `ListBriefReviews`, `GetBriefReviewCounts`, and the store boundary `IBriefReviewStore`. Infrastructure/Briefs holds `PostgresBriefReviewStore` on the shared `ReviewRecords` core. A person cannot create a brief, so the store has no create. The worker owns the first revision.

**Save.** `SaveBriefDraft` reads the expected revision. It copies it with the new headline and paragraphs and sets the origin to Person. It keeps the listed citations that a paragraph uses and counts them again in the coverage. The request holds one `SuggestionChoice`: `Keep`, `Dismiss`, or `Use`. `Keep` keeps the stored suggestion. `Dismiss` clears it. `Use` takes the headline, the paragraphs, and the citations from the stored suggestion, ignores the posted text, and clears the suggestion. `Use` fails when the revision has no suggestion. The core rule still checks that the text equals the suggestion. The store checks the edit against the current revision again under the lock.

**Decide.** `DecideBriefReview` mirrors the action handler. An approval runs `BriefRevision.ValidateForApproval`. The store then calls `BriefRecordReader.FindChangedAsync` and refuses the approval when any citation changed. The refusal is invalid input. A decision does not requeue any brief, because it concerns a brief, not a cited record.

**Reads.** `GetBriefReview` returns the review cycle. For each citation of the current revision, it returns the headline, the summary, and the changed flag of the exact cited revision. It returns the same for the suggestion in a second map. The IDs of the suggestion can equal those of the brief. One call to `BriefRecordReader.ReadExactAsync` reads both. `BriefRecordReader.ReadExactAsync` reads the revisions of the triples without an approval filter. `ListBriefReviews` takes the filter `All`, `NeedsAction`, or `Approved`. It reads the flags of the whole page in one call. Needs action orders by `last_revised_at_utc_ticks`, then record ID. The other filters order by creation time, then record ID. `GetBriefReviewCounts` counts with the same status filter.

**Changed flag.** `BriefRecordReader.FindChangedInActiveReleaseAsync` reads the active release once and passes its records to `BriefRecordReader.FindChangedAsync`. Publication calls `FindChangedAsync` with the records of the new release instead; see [Publication](#publication). It reads the latest decision on each cited revision in one query for each kind. A citation changed when the release holds another revision or none, or when the latest decision is not an approval. The read and the approval gate are the same call. The flag takes no lock of its own. The approval gate runs under the `civic-lens-brief-review` key, which the decision takes first. Every writer that can change the state of a cited record takes that key before it writes. These writers are a decision on an active revision (through `BriefStaleness.AfterDecision`), a publication commit, and an activation. Such a write therefore commits before the gate or after the approval, never between them. A write that commits after the approval shows as the flag at the next read. Publication checks again; see [Publication](#publication). Tests: `PostgresBriefReviewStoreTests.TheApprovalGateRunsUnderTheBriefLockSoNoCitedRecordWriterCommitsBetweenTheGateAndTheApproval`, `PostgresStatementReviewStoreTests.ADecisionOnAnActiveRevisionTakesTheBriefLockBeforeItWritesSoABriefApprovalNeverInterleavesWithIt`, and `BriefStalenessTests.ThePublishWaitsForTheBriefLockLastInTheOrderBecauseAnAppendOfABriefMustNotSlipInDuringIt`.

**Pages.** `/Review/Briefs` lists the drafted briefs with the filter tabs and their counts. A row shows a "Cited records changed" badge when the flag is set. `/Review/Briefs/{record-id}` has these parts:

- the saved revision and the "Edit wording" form, with a text area and the listed citations as check boxes for each paragraph
- an empty paragraph at the end of the form, which adds a paragraph when filled. An emptied paragraph is removed at the save
- the suggestion, with the headline of each record that a paragraph cites, and "Use suggestion" and "Dismiss" buttons. The plain save keeps it
- the cited records, each with its ID, headline, summary, kind, and revision, a "Changed" badge, and a link to its review page
- the coverage line, the shared `ReviewDecisionPanel`, and the shared `ReviewHistoryPanel`

The page differs from the action page: it has no "Change since the last approval" panel. The rail has a Briefs link. The review home has a stat card with the count of briefs that need action, and it links to the list.

**Withdraw and redraft.** A flagged approved brief stays approved until a person withdraws it. No event requeues it after the withdrawal. A later publication, activation, or decision that touches a record of the week requeues it, because it is no longer approved. After a withdrawal, the owner can run `brief redraft --week`. The command refuses a week whose brief is approved, with a message to withdraw the approval first, and it makes no model call. The redraft appends a revision with the new fingerprint. If the current text is model text, the revision replaces it and cites the revisions that the active release holds. If the current text is person text, the revision keeps the old citations and stores the new text as a suggestion. In that case the flag stays until one of three things happens. The person adopts the suggestion. The person drops the changed citation. The cited record is approved again.

### Publication

The `publishing` stage publishes a brief with the same ready list as the other kinds; see [publication](publication.md#publish). A flagged brief waits, and a later batch of the same tick publishes it when its citations are current. `BriefRecordLoader` owns the Application side. The rules are:

- The ready list holds an approved brief with no open concern. The active release must not hold its current revision. All citations must be current in the active release. `PostgresBriefReviewStore.ListUnpublishedApprovedAsync` reads the flags of the whole page with one `FindChangedAsync` call. A flagged approved brief stays approved but is not listed.
- A publish reads each requested brief with `GetForPublicationAsync`. The call takes the records of the new release: the records of the base release, with each requested record replaced by its new revision. `PublishRelease` loads the briefs after the other kinds, so a record that the same publish adds or replaces counts. The new release must hold each cited revision. A brief is refused when the same publish replaces a cited record with a newer revision.
- The same call returns the headline of each cited revision. `PublishedBriefBuilder` copies it into the file, so the page names each cited record without a second read.
- The commit reads the cited revisions of each added brief at the exact revision number that the build read. It calls `FindChangedAsync` with the records of the commit. This runs under the brief lock after `BriefStaleness.TakeLocksAsync`, next to the review state check. No cited record can change after the check, because every writer of cited review state takes its own key first.
- The check reads no queue. A redraft can leave the queue entry of an approved week Blocked or Pending, but the approved text and its citations decide, not the queue.
- A withdrawal of approval after a publish changes no file, like the other kinds. A new approved revision replaces the record in the next release.
- A brief entry fills only `eventDate`, with the Monday of the week. Other filters stay empty.
- The brief file holds the headline, the paragraphs with their citation IDs, the week start, the cutoff release number, and the coverage counts. It also holds the citations. Each citation holds its ID, kind, record ID, revision number, and the copy of the cited headline.
- The page links each citation to `records/{id}`. It shows the sample note. The brief cites some of the new records that Civic Lens considered for the week. It is not a full account.

### Staleness

`BriefStaleness` finds the brief queue entries to requeue. A queue entry qualifies when its week holds the first release of a touched record and its brief is not approved. The first release of a record is the lowest `first_release` of its rows in `publication_record_states`. `BriefFirstRelease` owns that query, and the selection read uses it too. Both read only the kinds that `BriefCitationKinds.IsCitable` names, so a brief in a release never counts as a touched record. `BriefWeekSql` gives the week bounds. One query answers for the whole set of touched records.

The entries move from Succeeded, Running, or Blocked `noCitableRecords` back to Pending through `PostgresAnalysisQueue.RequeueInAsync`. The queue owner checks the lifecycle, clears the lease, the attempts, and the retry link, and closes an open reservation as an Interrupted run. Entries in any other state stay as they are, because a Pending, RetryWaiting, or WaitingForBudget entry reads the selection when it runs. A Blocked entry with another code waits for an owner redraft, because a record change does not remove its cause.

A Running worker that read the old input loses its lease when its entry moves. Its settlement then fails the lifecycle check before the revision append, so the worker saves no stale revision. The entry runs again and reads the new input.

- `PostgresPublicationStore.CommitAsync` touches each record that differs between the previous active release and the new release. A record differs when its revision changes or when the new release drops or adds it. A new record has no earlier release, so it belongs to the open week and nothing is requeued for it. A record that returns after a drop belongs to the week of its first release.
- `PostgresPublicationStore.ActivateAsync` touches each record whose revision or presence differs between the old active release and the target release.
- `ReviewRecords.DecideAsync` calls `afterDecision` for every decision. The callback acts only when the decided revision is the revision that the active release holds. The stores of document changes, statements, and actions pass `BriefStaleness.AfterDecision`.

The lock order for these paths is in [advisory locks](../architecture.md#advisory-locks).

The stores of the three review kinds and the publication store receive the brief queue through their constructors. Each public constructor builds the queue from its context factory, like the draft stores.

A requeued brief reads the selection again. The record set of a closed week is fixed, but each record is read in the active release. A new revision, a dropped record, or a changed decision changes the fingerprint, and the step appends a new brief revision.

### Outcomes

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | A revision was appended |
| Succeeded | `noNewRecords` | The week has no new records, so no brief exists |
| Succeeded | `unchanged` | The fingerprint matches the current revision, or an approval landed while the call ran |
| RetryWaiting | `revisionConflict` | Another writer appended first |
| Blocked | `evidenceUnavailable`, `overByteCap`, `noCitableRecords` | The input cannot be drafted |
| Failed | `outputRejected`, `citationRejected`, a stop code, or `attemptsExhausted` | Owner redraft needed |

### Executor

The executor is `AnalysisWorker<BriefDraftRun>` with `BriefDraftStep`. It runs beside the other analysis loops in the `worker` process, with its own wakeup, status row, daily limit, and call cap. It uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`.

Its required variables are in [operations](../operations.md#brief-drafting). A daily limit without the key is a configuration error. The status row is in `document_change_analysis_status` under the task `brief-draft`. The names of the officials come from the collection configuration of the worker.

### Commands

| Command | Effect |
|---|---|
| `brief redraft --week <yyyy-MM-dd>` | Queues the week if it has no entry. Moves a Succeeded, Blocked, or Failed entry back to Pending. The date must be a Monday, and the week must have ended |

This is an owner command. It prints the count of entries that it queued or moved. A week before the first brief week is allowed. A week that has not ended is invalid input, because its records can still change. The database clock decides. A week whose brief is approved is also invalid input. Withdraw the approval first.

## Storage and contracts

- The generated [briefs schema](schema/briefs.md) lists every table, column, key, check, index, and trigger.
- Eight tables hold weekly briefs: `brief_analysis`, `brief_analysis_runs`, `brief_analysis_budget`, `brief_schedule`, `brief_drafts`, `brief_revisions`, `brief_review_decisions`, and `brief_review_decision_resolutions`.
- The commands use the replay receipt table with the operations `brief-save` and `brief-decide`.
- The review of briefs adds no lock. It uses `brief_drafts`, `brief_revisions`, `brief_review_decisions`, and `brief_review_decision_resolutions`.
- The citations of a brief stay in `revision_json`. No table indexes them.
- A Bypassed run records its reason in `brief_analysis_runs`. The reasons are `unchanged` and `noNewRecords`.
- `brief_schedule` holds the first brief week for the task.
- `BriefRevision` holds the record ID, the revision number, the author, the creation time, the window, and the fingerprint. It also holds the headline, `textOrigin`, an optional `suggestion`, the paragraphs, the citations, and the coverage.
- The window holds the week start, the start and end ticks in UTC, the first release number, and the cutoff release number.
- A paragraph holds its text and its citation IDs. A citation holds its ID, its kind (`DocumentChange`, `Statement`, or `Action`), its record ID, and its revision number.
- Brief drafting has its own budget, separate from the other budgets.
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Revision, window, and week | `src/CivicLens.Core/Briefs/BriefRevision.cs`, `src/CivicLens.Core/Briefs/BriefWindow.cs`, `src/CivicLens.Core/Briefs/BriefWeek.cs`, `src/CivicLens.Core/Briefs/BriefParagraph.cs`, `src/CivicLens.Core/Briefs/BriefCitation.cs`, `src/CivicLens.Core/Briefs/BriefCitationKind.cs`, `src/CivicLens.Core/Briefs/BriefCoverage.cs`, `src/CivicLens.Core/Briefs/BriefTextSuggestion.cs`, `src/CivicLens.Core/Review/ReviewTextOrigin.cs` |
| Step and evidence | `src/CivicLens.Application/Briefs/BriefDraftStep.cs`, `src/CivicLens.Application/Briefs/BriefDraftEvidence.cs`, `src/CivicLens.Application/Briefs/BriefDraftContext.cs`, `src/CivicLens.Application/Briefs/BriefRecord.cs` |
| Selection and fingerprint | `src/CivicLens.Application/Briefs/BriefSelection.cs`, `src/CivicLens.Application/Briefs/BriefSelectedRecord.cs`, `src/CivicLens.Application/Briefs/BriefFingerprint.cs` |
| Prompt, resolver, lifecycle, settings | `src/CivicLens.Application/Briefs/BriefDraftPrompt.cs`, `src/CivicLens.Application/Briefs/BriefDraftResolver.cs`, `src/CivicLens.Application/Briefs/BriefDraftLifecycle.cs`, `src/CivicLens.Application/Briefs/BriefDraftSettings.cs`, `src/CivicLens.Application/Briefs/BriefDraftTask.cs`, `src/CivicLens.Application/Briefs/BriefDraftErrorCodes.cs` |
| Store and model ports | `src/CivicLens.Application/Briefs/IBriefDraftStore.cs`, `src/CivicLens.Application/Briefs/IBriefDraftModel.cs` |
| Owner redraft | `src/CivicLens.Application/Briefs/RedraftBrief.cs` |
| Review aggregate and rules | `src/CivicLens.Core/Briefs/BriefReview.cs`, `src/CivicLens.Core/Briefs/BriefRevision.cs`, `src/CivicLens.Core/Briefs/BriefTextSuggestion.cs` |
| Review commands and reads | `src/CivicLens.Application/Briefs/SaveBriefDraft.cs`, `src/CivicLens.Application/Briefs/DecideBriefReview.cs`, `src/CivicLens.Application/Briefs/GetBriefReview.cs`, `src/CivicLens.Application/Briefs/ListBriefReviews.cs`, `src/CivicLens.Application/Briefs/GetBriefReviewCounts.cs`, `src/CivicLens.Application/Briefs/IBriefReviewStore.cs`, `src/CivicLens.Application/Briefs/BriefCitationKinds.cs` |
| Review store and changed flag | `src/CivicLens.Infrastructure/Briefs/PostgresBriefReviewStore.cs`, `src/CivicLens.Infrastructure/Briefs/BriefRecordReader.cs`, `src/CivicLens.Infrastructure/Briefs/BriefReviewQueries.cs` |
| Publication | `src/CivicLens.Application/Publication/BriefRecordLoader.cs`, `src/CivicLens.Application/Publication/PublishedBriefBuilder.cs`, `src/CivicLens.Application/Briefs/PublishableBrief.cs`, `src/CivicLens.Application/Briefs/UnpublishedApprovedBrief.cs`, `src/CivicLens.Publication.Contracts/PublishedBrief.cs`, `src/CivicLens.Host/Publication/PublishedBriefPage.razor` |
| Review pages | `src/CivicLens.Host/Briefs/BriefList.razor`, `src/CivicLens.Host/Briefs/BriefEditor.razor`, `src/CivicLens.Host/Briefs/BriefRoutes.cs`, `src/CivicLens.Host/Review/SuggestionActions.cs` |
| Store, queue mapping, model | `src/CivicLens.Infrastructure/Briefs/PostgresBriefDraftStore.cs`, `src/CivicLens.Infrastructure/Briefs/BriefDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Briefs/BriefDraftRows.cs`, `src/CivicLens.Infrastructure/Briefs/AnthropicBriefDraftModel.cs` |
| Week SQL and record read | `src/CivicLens.Infrastructure/Briefs/BriefWeekSql.cs`, `src/CivicLens.Infrastructure/Briefs/BriefRecordReader.cs`, `src/CivicLens.Infrastructure/Review/ReviewQueries.cs` |
| Staleness | `src/CivicLens.Infrastructure/Briefs/BriefStaleness.cs`, `src/CivicLens.Infrastructure/Review/ReviewRecords.cs`, `src/CivicLens.Infrastructure/Publication/PostgresPublicationStore.cs` |
| Tables | `src/CivicLens.Infrastructure/Briefs/Persistence/` |
| CLI and composition | `src/CivicLens.Host/Briefs/BriefCommand.cs`, `src/CivicLens.Host/Briefs/BriefDraftComposition.cs`, `src/CivicLens.Host/Briefs/BriefDraftConfiguration.cs` |

## Limits and planned work

- A Blocked `noCitableRecords` week has no brief text for the new records, so nothing exists to approve or publish for it.
- The manifest times of a release come from the build, not from the commit. See [publication](publication.md#invariants).
- Records that changed after the week started are not new, so a brief never shows a later revision of an older record.
- The bill movement counts the sponsorship entries of a new action record, because a new record has no earlier revision to compare against.
- Filling stops at the first group that does not fit. A smaller group later in the order never takes its place.
- The text of a person survives later model runs by rule. A person who edited a brief cannot rebind a changed citation to the new revision. The person can only drop the citation.
- The approval gate reads the flag at the moment of approval only. Publication reads it again.
- A published brief page links to the current page of each cited record. A newer revision of a cited record can replace the cited one in a later release. The page then shows the revision number that the brief cited, and the linked page shows the newer text.
- The cited-record texts on the editor come from one read per page load. A cited record can change while the page is open. The flag shows at the next load.
- News coverage in briefs is deferred; see [roadmap](../roadmap.md#deferred).
