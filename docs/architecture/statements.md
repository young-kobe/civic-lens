# Statements

## Purpose

The statements feature finds articles that may hold an official's statement and gates them against tracked issues. It then drafts one statement record for each article that passes. Code approves a record from the own office of its official when every check passes. People review and approve every other record on the statement review pages.

## Decisions

- A candidate is the first extraction of an article that discovery admission created. Later fetches and parser upgrades never create a second candidate.
- Some senate.gov article pages return 403 to our User-Agent, but the feeds of those senators carry the full text. A feed source can therefore set `statementTextInFeed`. A person sets it only after the person checks that the feed holds the full text. Each feed item then makes one candidate with the text of the item. No config file sets the flag yet.
- A feed item candidate has the same key as a page job for the same link: the source plus the item link. The first text that registers wins. A later page text for the link adds nothing.
- A public statement from a feed item cites the feed capture and links the article. See [publication](publication.md#release-content).
- The gate uses the two issue layers in [configuration](configuration.md#decisions).
- Watched pages are never candidates. They stay document-change sources only.
- A later edit to an admitted article is a document change, not a new statement.
- Statements mirror the document-change feature: their own queue, runs, drafts, revisions, and review pages, in the same shape. They share only parts that already have one owner. These are the Core review actor and decision policy, the Anthropic adapter, the citation span checks, and the budget mechanics.
- The gate fails open on two invalid answers or an oversized prompt. A doubtful article then goes to drafting, not to a silent skip.
- Provider, auth, and budget problems are not fail-open. They follow the shared retry and pause rules.
- The gate also keeps a release about a bill or a recorded vote when no tracked issue matches. Two rules decide this: the model answer `namesBillOrVote`, and a bill number that `BillNumberFinder` finds in the text. The bill number rule needs no model and is a floor. Each pass path has its own reason code, so a measurement can count each path.
- A skipped article stays visible with a code reason, so a person can inspect it.
- A rubric change queues nothing by itself. `statements regate` is an explicit owner command.
- The speakers come from the configuration revision that admitted the article, not the current file. A later configuration change cannot change the speakers of an admitted article.
- Code sets the quotation kind and the bill numbers. The model and the reviewer never set them.
- Code never guesses a statement date. When the article states no date, the draft date is null, and the reviewer sets it.
- Code approves a statement draft only from the own office of its official. The source must set `officeOf` to the one speaker of the draft. Coverage alone does not show authorship. A statement from a source without `officeOf` waits for a person.
- Code approves only a draft that names no other speaker. Any other speaker sends the draft to a person.
- Code approves a draft with no date, or with a model date that lies on the capture day or up to 7 days before it. The quote must state the date. An older or later date waits for a person.
- Policy areas stay the choice of the model. Relationships stay person-approved. No command approves old drafts by code.
- A person edit after a code approval is a new revision that waits for a person.
- A gate label holds the tracked issue IDs and the `namesBillOrVote` answer of one release. It holds no quote, policy area, or pass verdict. The gate rule derives a pass from a label, so the rule keeps one owner.
- Labels are append-only rows, like revisions. The newest human row of a candidate is the label of record. A model row never replaces it.
- Code assigns about 30 percent of releases to a held-out set by a hash of the candidate ID. The set is the test set of the gate benchmark ([D-13](../roadmap.md#pending-decisions)). A held-out release never gets a model label.
- Only the owner labels releases and exports labels. `GateLabelEditor` does not extend `ReviewRecordPage`, because that base class serves revisions and decisions and a label has neither.
- Vendor rule: only public press release text, gate labels, and published releases may go to Cloudflare. Never send review notes, reviewer identity, or credentials. Encrypted backups are the one exception, as [host](host.md#decisions) states.
- Drafting does not cache the prompt. The article text is most of each request, and the schema changes with the covered officials of each source.
- A relationship links one approved statement revision to one approved action or vote revision. A person or code approves the statement, and code or a person approves the action or vote. It has a named type and no verdict. A stance such as consistent or inconsistent needs a method note first.
- A relationship revision holds the matched official, which is the speaker whose sponsorship or vote made the pair. A person edit never changes it. A changed match makes the pair draft again. An approved relationship also shows as a pair on the current Congress page of that official only. The pair lists the statement, the link type, the bill or vote, and for a vote the cast of the official. The panel states that a reviewer approved the link and that a link does not mean a match. See [publication](publication.md#issue-panel).
- The types for an action are "describes own bill", "states position on", and "claims credit for". The only type for a vote is "statement about a vote". The model may also answer none. Then the queue row settles. A new pair gets no relationship record. An existing draft stays as it is, its changed-record flag blocks approval, and a person can reject it.
- An approved statement, by code or by a person, publishes through the `publishing` worker stage with no owner step; see [publication](publication.md#automatic-publication).
- Relationships are their own record kind, with a queue, a budget, drafts, revisions, review decisions, and a review lock. Reviewers use their own pages; see [Relationship review](#relationship-review). An approved relationship publishes as its own record kind; see [publication](publication.md#relationship-record).
- A person edits only the type, the reason, the quote, and the fact of a relationship. The reviewer picks the quote from the passages of the statement and the fact from the facts of the target. Code builds the span.
- A reviewer rejects a false pair with Request changes and a note. No dismiss tool exists.
- Approval needs both cited revisions to be the approved revision of their records. The flag is derived at read time, as for briefs. An approved relationship stays approved until a person withdraws it.
- Publication uses a stricter rule than the review flag. It refuses a relationship when the new release lacks either cited revision, or when the latest decision on either is not an approval. A published relationship stays in later releases. The statement, action, and vote pages list the relationships that cite them; see [related records](publication.md#related-records).
- Code finds the pairs, not the model. A pair is the same official plus a shared bill. For an action, the official needs a sponsorship of the bill in any role. A cosponsor who withdrew still counts. For a vote, the official needs to have cast it.
- Code finds the shared bill by number first and then by title. A title is the saved title of the action revision. The match ignores case, a leading "the", and a trailing year such as "of 2025". A title of fewer than three words never matches ([D-09](../roadmap.md#pending-decisions)). When the statement date is known, a title matches only an action of the Congress of that date.
- A vote has no saved title, so a vote pairs by bill number only.
- A bill number comes from `BillNumberFinder` in the quoted passages of the statement. The number has no Congress, so it takes the Congress of the statement date. A Congress starts on 3 January of an odd year, and 1 and 2 January belong to the Congress before it. A statement with no date cannot place a number. Code skips the number and counts the statement.
- Pairs that share only a tracked issue wait for a measurement.
- A relationship cites the revision number of each side, as a brief cites its records.
- The queue key of a pair is the lowercase SHA-256 hash of `relationship-v1|statementCandidateId|targetKind|targetRecordId`. The match basis and the matched span are in the revision, not in the key.
- The model proposes the type and one sentence of reason. It also quotes the statement and names one fact of the record. Code finds the quote and requires it to cover the matched words. A person approves or rejects the relationship.
- Relationship drafting uses Haiku with its own queue, budget table, and variable. No gate runs before it.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- The event date of a statement is its statement date, or else the capture date of its candidate (`statement_candidates.created_at_utc_ticks`, as a UTC date). Enforced by `StatementDraftRevision.EventOn`. Test: `StatementDraftRevisionTests.TheEventDateIsTheStatementDateElseTheCaptureDate`.
- A count equals the total of its list filter, so a draft with no revision needs action. Enforced by `PostgresStatementReviewStore.CountAsync` and `ReviewRecords.Drafts`. Test: `PostgresStatementReviewStoreTests.CountsUseTheSameStatusRuleAsTheListFiltersSoADraftWithoutARevisionNeedsAction`.
- A statement candidate is the source plus the URL, and it never repeats. Enforced by the primary key of `statement_candidates`. Test: `PostgresStatementCandidateRegistrationTests.ConcurrentSavesForTheSameSourceAndUrlLeaveOneCandidate`.
- Only discovery-admitted jobs and the items of a flagged feed become candidates. Enforced by `PostgresStatementCandidateRegistration` for pages, and by `JobPreparation` with `ConfiguredCollectionSource.StatementTextInFeed` for feed items. Tests: `PostgresStatementCandidateRegistrationTests.ExtractionFromAWatchedPageJobRegistersNoCandidate` and `JobPreparationFeedStatementTests.AFeedThatIsNotFlaggedSavesNoItemTextBecauseAPersonMustCheckTheFeedFirst`.
- A feed item and a page job for one link make one candidate, and the first text stays. Enforced by `PostgresCollectionJobStore.HashCandidate` and the primary key of `statement_candidates`. Tests: `PostgresStatementCandidateRegistrationTests.AFeedItemForALinkThatAPageJobAlreadyHoldsAddsNeitherACandidateNorAnExtraction` and `PostgresStatementCandidateRegistrationTests.APageJobForALinkThatAFeedItemAlreadyHoldsKeepsTheFeedItemTextAsTheFirstText`.
- One feed capture adds at most `maxJobs` candidates of its source in total, newest item first, also when preparation runs again. Enforced by `PostgresDocumentExtractionStore.SaveFeedItemsAsync`. It first locks the row of the feed attempt in `collection_attempts` with `FOR NO KEY UPDATE`. Two saves of one attempt then run one after the other and stay within the limit together. No other writer locks that row. The lock is the first lock of the transaction, so no lock order can cross. Links that already have a candidate do not use the limit. Tests: `PostgresStatementCandidateRegistrationTests.ASaveOfAFeedAttemptWaitsForAnotherWriterOfTheSameAttemptSoTheBoundIsReadAfterThatWriterCommits`, `PostgresStatementCandidateRegistrationTests.LinksThatAlreadyHaveACandidateDoNotUseTheBoundSoNewerItemsStillRegister`, `PostgresStatementCandidateRegistrationTests.FeedItemsRegisterNewestFirstUpToTheLimitSoOneBusyFeedCannotFloodTheGate` and `PostgresStatementCandidateRegistrationTests.AReplayOfTheSameFeedAttemptNeverAddsCandidatesBeyondTheLimit`.
- A feed item link must pass the origin and path prefix of its source, as a page job link does. Enforced by `FeedStatementExtractions` through `CollectionRequest.ResolveCandidate`. Test: `FeedStatementExtractionsTests.ALinkOutsideTheAllowedPrefixOrOriginIsRefusedLikeAPageJobWould`.
- A feed item extraction has the identity of its capture and its index in the feed, so a rerun makes the same IDs. Enforced by `FeedStatementExtractions` and `DocumentExtraction`. Test: `FeedStatementExtractionsTests.TheIdentityOfAnItemDependsOnTheCaptureAndItsIndexSoARerunMakesTheSameIds`.
- A Succeeded gate has a known reason. Enforced by `StatementGateLifecycle`. Test: `StatementGateLifecycleTests.ASucceededGateNeedsAKnownReason`.
- When a valid answer has no verified issue and namesBillOrVote is false, a bill number in the text still passes the release. Enforced by `StatementGateResolver`. Test: `StatementGateResolverTests.ABillNumberInTheTextPassesWithoutTheModelAnswerSoTheFloorHoldsWhenTheModelMissesIt`.
- A gate answer must hold `namesBillOrVote`, so a missing answer never reads as false. Enforced by `StatementGateResolver.Parse` with the required constructor parameters of `AnalysisAnswerRules.OutputJson`. Test: `StatementGateResolverTests.AnAnswerWithoutTheBillOrVoteFieldIsRejectedSoAMissingFieldNeverReadsAsFalse`.
- Each of the three pass paths has its own reason. Enforced by `StatementGateResolver` and `ck_statement_analysis_runs_reason`. Test: `StatementGateResolverTests.TheThreePassPathsRecordThreeDistinctReasonsSoAMeasurementCanCountEachPath`.
- Code approves a statement draft only when the own office, speaker, gate, quote, and date checks pass. Enforced by `StatementCodeApproval` with `StatementAllowedSelections.RequireApprovable`. Tests: `StatementCodeApprovalTests.ADraftOfTheOwnOfficeSourceThatPassesEveryCheckIsApprovedByCode`, `ADraftFromASourceWithoutOfficeOfWaitsForAPersonBecauseCoverageIsNotAuthorship`, `ADraftFromTheOfficeOfAnotherPersonWaitsForAPersonBecauseTheSpeakerIsNotTheOwner`, `ADraftWithANamedOtherSpeakerWaitsForAPerson`, `ADraftThatNamesAnotherCoveredOfficialAsSpeakerWaitsForAPerson`, `ADraftWhoseDateIsOnTheCaptureDayIsApprovedByCodeBecauseTheQuoteStatesIt`, `ADraftWhoseDateIsSevenDaysBeforeCaptureIsApprovedByCode`, `ADraftWhoseDateIsMoreThanSevenDaysBeforeCaptureWaitsForAPerson`, `ADraftWhoseDateIsAfterTheCaptureDayWaitsForAPerson`, `ThePersonPathStillRefusesAnAiDateBecauseOnlyCodeMayAcceptADateItRead`, `ADraftFromABillOrVotePassWithNoTrackedIssueIsApprovedByCode`, `ADraftQueuedAfterAGateThatVerifiedNothingWaitsForAPerson`, `ADraftIssueWithoutAVerifiedGateQuoteIsNotApprovedByCode`, and `ARuleThatAPersonApprovalEnforcesAlsoBlocksCodeApproval`.
- A statement code approval is stored with the draft and its first revision in one transaction. It runs the hooks of a person approval. Enforced by `StatementDraftRows`, `ReviewRecords.AppendCodeApprovedAsync`, and `StatementDraftQueueMapping`. Tests: `PostgresStatementDraftStoreTests.AStatementAiRevisionFromTheOfficialsOwnOfficeIsStoredAsApprovedByTheCodeSubjectInTheSameTransaction` and `PostgresStatementDraftStoreTests.ACodeApprovalRunsTheRelationshipHookSoAnApprovedStatementOfTheSponsorQueuesItsPair`.
- A person edit of a code-approved statement waits for a person. Enforced by `ReviewPolicy.GetCurrentStatus`. Test: `PostgresStatementDraftStoreTests.APersonEditAfterACodeApprovalWaitsForAPersonBecauseThePersonRevisionIsNotTheCheckedOne`.
- A draft from a bill or vote pass accepts an empty issue list. Enforced by the draft step and the revision rules. Test: `StatementDraftStepTests.ADraftFromABillOrVotePassHasNoIssueIdsBecauseNoTrackedIssueMatched`.
- Only a skipped gate returns from Succeeded to Pending. Enforced by `StatementGateLifecycle`. Test: `StatementGateLifecycleTests.OnlyASkippedGateMayReturnToPendingForRegate`.
- A pass adds exactly one Pending draft entry in the same transaction. Enforced by `StatementGateQueueMapping`. Test: `PostgresStatementGateStoreTests.APassSettlesTheGateAndInsertsExactlyOnePendingDraftRowInTheSameTransaction`.
- Only an analysis revision has a run ID, and one revision exists for each run. Enforced by `ck_statement_revisions_run` and `IX_statement_revisions_run_id`. Test: `PostgresStatementDraftStoreTests.TheDatabaseRefusesAnAiRevisionThatHasNoRun`.
- NotDrafted holds exactly when the revision number is 0. Enforced by `ck_statement_drafts_not_drafted`. Test: `PostgresStatementDraftStoreTests.TheDatabaseRefusesANotDraftedStatusOnADraftThatHasARevision`.
- A candidate gets one draft and one first revision. Enforced by `StatementDraftRows` under the `PostgresAdvisoryLock.StatementReview` key. Test: `PostgresStatementReviewStoreTests.AHumanCreateAndAnAiSettlementRacingForTheSameCandidateLeaveExactlyOneFirstRevision`.
- A label row is only added. It is never changed or deleted. Enforced by `PostgresGateLabelStore`, which has no update or delete method. Test: `PostgresGateLabelStoreTests.LabelsAreAppendedAndTheNewestHumanRowIsTheLabelOfRecordSoACorrectionNeverEditsHistory`.
- The newest human label is the label of record, and a model label never replaces it. Enforced by `PostgresGateLabelStore`. Test: `PostgresGateLabelStoreTests.AModelLabelNeverReplacesTheHumanLabelOfRecord`.
- A model label has a run ID and a human label has none. Enforced by `ck_statement_gate_labels_run`. Test: `PostgresGateLabelStoreTests.TheDatabaseRefusesAModelLabelWithoutARunAndAHumanLabelWithOne`.
- A held-out release takes no model label. Enforced by `PostgresGateLabelStore` with `GateLabelSplit`. Test: `PostgresGateLabelStoreTests.AHeldOutReleaseRefusesAModelLabelAndStoresNothingSoTheTestSetStaysBlindToModels`.
- The read of a held-out release never returns a model label, even if a row exists. Enforced by `PostgresGateLabelStore.GetAsync`. Tests: `PostgresGateLabelStoreTests.AModelRowThatReachesAHeldOutCandidateByRawSqlIsStillNeverReturnedSoTheReadKeepsTheTestSetBlind` and `GateLabelPageTests.AModelRowOnAHeldOutReleaseNeverPrefillsTheFormSoTheTestSetStaysBlind`.
- A model label cites a real run, and one run labels one release. Enforced by the foreign key and the unique index on `run_id`. Tests: `PostgresGateLabelStoreTests.AModelLabelNeedsARealRunAndEachRunLabelsOnlyOneRelease` and `PostgresGateLabelStoreTests.TheRunIdHasAForeignKeyToTheAnalysisRunsAndAUniqueIndexSoOneRunLabelsOneRelease`.
- A candidate ID always has the same held-out answer. Enforced by `GateLabelSplit`. Test: `GateLabelSplitTests.TheSameCandidateAlwaysLandsOnTheSameSideSoALaterRunCannotLeakATestReleaseIntoTraining`.
- A label cites the extraction of its candidate. Enforced by `PostgresGateLabelStore`. Test: `PostgresGateLabelStoreTests.ALabelThatCitesAnotherExtractionOrAnUnknownCandidateIsRefused`.
- A label holds only tracked issue IDs of the issue file. Enforced by `SaveGateLabel`. Test: `SaveGateLabelTests.AnIssueThatTheIssueFileDoesNotTrackIsRefusedSoLabelsStayInTheGateVocabulary`.
- Only the owner labels, reads the label queue, or exports labels. Enforced by `ReviewAuthorization.RequireOwner` in each use case, and by a redirect on the pages. Tests: `SaveGateLabelTests.AReviewerCannotLabelBecauseLabelsFeedTheOwnersBenchmark`, `GateLabelOwnerOnlyTests.AReviewerCannotReadTheLabelQueueATaskOrTheExportBecauseOnlyTheOwnerRunsTheBenchmark` and `GateLabelPageTests.ReviewersAreSentToAccessDeniedSoOnlyTheOwnerFeedsTheBenchmark`.
- An export line never holds an author subject, a review note, or a credential, and the file stays inside `.runtime`. Enforced by `GateLabelExportRow`, which has no such field, and by `GateLabelExport`. Tests: `GateLabelExportTests.EachLineHoldsOnlyTheFieldsTheBenchmarkNeedsSoNoSubjectNoteOrCredentialCanLeave` and `GateLabelExportTests.APathOutsideTheRuntimeDirectoryIsRefused`.
- A pair key is the hash of the statement, the target kind, and the target record. It does not change with a revision or a match basis. Enforced by `RelationshipPair.IdOf`. Tests: `RelationshipPairFinderTests.ThePairIdIsPinnedSoAStoredQueueKeyNeverChangesWithoutAVersionBump` and `RelationshipPairFinderTests.ThePairIdIsTheHashOfTheStatementTheKindAndTheTargetAndIgnoresTheMatchBasisAndTheRevisions`.
- Only approved revisions make pairs. Enforced by `RelationshipEvidenceReader`. Test: `PostgresRelationshipDraftStoreTests.ASideThatIsNotApprovedMakesNoPairBecauseOnlyApprovedRevisionsMayBeCited`.
- A pair needs the same official: a sponsorship for an action and a cast vote for a vote. Enforced by `RelationshipPairFinder`. Tests: `RelationshipPairFinderTests.AnActionThatTheSpeakerDidNotSponsorMakesNoPairBecauseThePairNeedsTheSameOfficial` and `RelationshipPairFinderTests.AVoteOnAMentionedBillMakesAPairForTheOfficialWhoCastIt`.
- A bare bill number takes the Congress of the statement date. A statement with no date skips the number and is counted. Enforced by `StatementReading` and `CongressCalendar`. Tests: `RelationshipPairFinderTests.AStatementWithoutADateCannotPlaceABareNumberSoItIsSkippedAndCounted` and `CongressCalendarTests.ACongressStartsOnThirdJanuaryOfAnOddYearAndTheFirstTwoDaysBelongToTheCongressBefore`.
- A title of fewer than three words never matches. Enforced by `BillTitleMatcher`. Test: `BillTitleMatcherTests.ATitleOfFewerThanThreeWordsIsTooShortToMatchBecauseItWouldMatchEverySentenceThatUsesThoseWords`.
- A relationship type fits the kind of its target. Enforced by `RelationshipTypes`, the output schema, and `RelationshipRevision`. Tests: `RelationshipRevisionTests.ATypeThatDoesNotFitTheKindOfTheTargetIsRefused` and `RelationshipDraftPromptGoldenTests.TheSchemaOffersOnlyTheTypesOfTheTargetKindAndNoneSoTheModelCannotPickAWrongSide`.
- The quote of a relationship lies inside one passage of the statement and covers the words that code matched. Enforced by `RelationshipDraftResolver` and `RelationshipRevision`. Tests: `RelationshipDraftResolverTests.AQuoteThatLeavesOutTheMatchedWordsIsRejectedAsACitationError` and `RelationshipDraftResolverTests.AQuoteFromTheArticleOutsideEveryPassageOfTheStatementIsRejected`.
- A relationship cites the exact revision of both sides. Enforced by `RelationshipDraftResolver`. Test: `RelationshipDraftStepTests.TheFirstDraftCallsTheModelOnceAndWritesRevisionOneThatCitesBothApprovedRevisions`.
- A none answer settles the queue row and saves no relationship. Enforced by `RelationshipDraftStep`. Test: `PostgresRelationshipDraftStoreTests.ANoneAnswerSavesNoRelationshipAndTheNextPassDoesNotAskAgain`.
- A target revision that changes no fact of the model input and not the role of the paired official does not change the fingerprint. The fingerprint holds the statement revision, the match, the role of the paired official, the target facts, and the task and prompt versions. It does not hold the target revision number. Enforced by `RelationshipFingerprint`. Tests: `RelationshipPairFinderTests.TheFingerprintIgnoresTheTargetRevisionNumberAndTheCastsOfOtherMembers` and `PostgresRelationshipDraftStoreTests.ASummaryChangeOfTheTargetDraftsAgainBecauseThePromptReadsTheSummary`.
- A target revision with the same fingerprint makes no model call. The step copies the current AI revision to the new target citation, and the pair stays approvable. The cited statement and the match of that revision must equal those of the pair. Otherwise the step drafts with the model. Enforced by `RelationshipDraftStep`, `RelationshipDraftResolver.Recite`, and `RelationshipStaleness`. Tests: `RelationshipDraftStepTests.ANewTargetRevisionWithTheSameFingerprintCopiesTheRevisionToTheNewCitationWithoutAModelCall` and `PostgresRelationshipDraftStoreTests.AnotherMembersCosponsorshipMakesNoModelCallAndMovesTheCitationSoTheDraftStaysApprovable`.
- After a none answer, a new target revision with the same fingerprint neither requeues the pair nor copies the existing draft. A pass without a call keeps the reason `noRelationship`. A changed fingerprint still drafts again. Enforced by `RelationshipStaleness` and `RelationshipDraftStep`. Tests: `PostgresRelationshipDraftStoreTests.ANoneAnswerAfterAnAiRevisionLeavesItUncopiedAndNotApprovableWhenTheTargetRevisionChangesAgain`, `PostgresRelationshipDraftStoreTests.ANoneAnswerAfterAPersonRevisionStartsNoFurtherModelCallOnLaterRefreshes`, `PostgresRelationshipDraftStoreTests.ALaterFingerprintChangeAfterANoneAnswerDraftsAgain`, and `RelationshipDraftStepTests.ARevisionBehindANoneAnswerIsNeverCopiedToTheNewTargetCitation`.
- A person revision that cites an older target revision gets a new model draft, so its citation does not stay stale. Enforced by `RelationshipDraftStep`. Test: `RelationshipDraftStepTests.APersonRevisionThatCitesAnOlderTargetRevisionDraftsAgainSoItsCitationDoesNotStayStale`.
- An unchanged fingerprint makes no model call. Enforced by `RelationshipDraftStep`. Test: `RelationshipDraftStepTests.AnUnchangedFingerprintSettlesWithABypassedRunAndNoModelCall`.
- A new approved revision of a side moves an unapproved pair back to Pending, and an approved relationship never moves. Enforced by `RelationshipStaleness`. Tests: `PostgresRelationshipDraftStoreTests.ANewApprovedRevisionOfASideMovesAnUnapprovedPairBackToPendingThroughTheQueueOwner` and `PostgresRelationshipDraftStoreTests.AnApprovedRelationshipIsNeverRequeuedEvenWhenACitedRevisionChanged`.
- A pair that is no longer found is flagged and never deleted, but only while both sides are approved. Enforced by `RelationshipStaleness`. Tests: `PostgresRelationshipDraftStoreTests.APairThatIsNotFoundAnymoreIsFlaggedAndNotDeletedAndTheFlagClearsWhenItIsFoundAgain` and `PostgresStatementReviewStoreTests.ReApprovingAStatementRequeuesAPairThatBlockedWhileItWasNotApprovedAndAChangeRequestNeverFlagsIt`.
- A review decision queues the pairs of its record in the same transaction, so an idle worker wakes. Enforced by `RelationshipStaleness.AfterDecision` in the three review stores. Tests: `PostgresStatementReviewStoreTests.ApprovingAStatementQueuesItsRelationshipPairsInTheSameTransactionSoTheRelationshipWorkerWakes` and `PostgresStatementReviewStoreTests.ADecisionWakesAnIdleRelationshipWorkerSoANewApprovalIsDraftedWithoutAnyOtherEvent`.
- A title match respects the Congress of the statement date. Enforced by `RelationshipPairFinder`. Test: `RelationshipPairFinderTests.ATitleMatchesOnlyAnActionOfTheCongressOfTheStatementDateSoAnOlderBillWithTheSameNameIsNotPaired`.
- The relationship hook takes the pair rows of its record, then the relationship key, and only then the brief rows and key. Enforced by `AfterDecisionHooks.ForRecordKind` in the review stores. Tests: `PostgresStatementReviewStoreTests.ADecisionThatWaitsForTheRelationshipKeyHasNotReachedTheBriefKeyYetSoTheBriefHookStaysLast` and `PostgresStatementReviewStoreTests.ADecisionOnAnActiveRevisionTakesTheBriefLockBeforeItWritesSoABriefApprovalNeverInterleavesWithIt`.
- A decision locks only the pair rows of its own record, and a decision that leaves the record not approved takes no relationship lock. Enforced by `RelationshipStaleness`. Tests: `PostgresStatementReviewStoreTests.ADecisionLocksOnlyTheRelationshipRowsOfItsOwnRecordSoAnUnrelatedPairRowDoesNotBlockIt` and `PostgresStatementReviewStoreTests.ADecisionThatLeavesTheRecordNotApprovedTakesNoRelationshipLock`.
- A person edit changes only the type, the reason, the quote, and the fact. Enforced by `RelationshipRevision.ValidatePersonEditOf`. Test: `RelationshipRevisionTests.APersonEditChangesOnlyTheTypeTheReasonTheQuoteAndTheFact`.
- A relationship revision names the matched official of its pair, who must be a speaker of the statement. A person edit keeps it. A person revision stays only while the pair matches the same official. Enforced by `RelationshipDraftResolver`, `RelationshipRevision.ValidateShape`, `RelationshipRevision.ValidatePersonEditOf`, and `RelationshipRevision.IsPersonRevisionCiting`. Tests: `RelationshipDraftResolverTests.AnAcceptedAnswerBecomesRevisionOneBoundToTheExactRevisionsOfBothSides`, `RelationshipRevisionTests.ARevisionNamesTheMatchedOfficialWithinTheLimit`, `RelationshipRevisionTests.APersonEditChangesOnlyTheTypeTheReasonTheQuoteAndTheFact`, `RelationshipRevisionTests.APersonRevisionStaysOnlyForTheSameCitationsAndTheSameOfficial`.
- A person quote is the saved quote or a passage of the cited statement that covers the matched words. A person fact is a fact that the cited target lists. Enforced by `RelationshipEditOptions.Require` in `PostgresRelationshipReviewStore.SaveRevisionAsync`. Tests: `PostgresRelationshipReviewStoreTests.AQuoteThatNoPassageOffersOrAFactTheTargetDoesNotListIsRefusedAndNoRevisionIsSaved` and `RelationshipReviewWorkspaceTests.ASaveChangesTheTypeReasonQuoteAndFactFromTheOfferedChoicesAndAForgedQuoteIsABadRequest`.
- Approval needs the cited statement and the cited target to be at their approved revisions. Enforced by `PostgresRelationshipReviewStore.DecideAsync` through `RelationshipCitedReader.FindChangedAsync`. Tests: `PostgresRelationshipReviewStoreTests.ApprovalIsRefusedWhenACitedRevisionIsNoLongerTheApprovedRevisionOfItsRecord` and `RelationshipReviewWorkspaceTests.AChangedCitedRecordShowsTheFlagInTheEditorAndRefusesApprovalWhileAnApprovedLinkStaysApproved`.
- An approved relationship stays approved when a cited record changes, and the flag shows it. Enforced by `PostgresRelationshipReviewStore.GetAsync`. Test: `PostgresRelationshipReviewStoreTests.AnApprovedLinkStaysApprovedWhenACitedRecordChangesAndOnlyAPersonCanWithdraw`.
- A person revision that cites the same statement and target revisions as the pair settles the entry as `unchanged` before any model call. The run is `Bypassed` and its fingerprint is settled, so a refresh does not queue the pair again. `RelationshipRevision.IsPersonRevisionCiting` owns the rule. `RelationshipDraftRows.AppendAnalysisRevisionAsync` repeats it with the code `personEditKept` as a backstop, and then the run is kept as `DraftDiscarded`. A changed citation lets the revision land. Enforced by `RelationshipDraftStep`. Tests: `PostgresRelationshipDraftStoreTests.APersonRevisionThatCitesTheSameRecordsSettlesAsUnchangedWithoutAModelCallAndARefreshDoesNotQueueItAgain`, `PostgresRelationshipDraftStoreTests.TheAppendRefusesAModelRevisionThatCitesTheSameRecordsAsAPersonRevisionThatChangedDuringTheCall`, and `PostgresRelationshipDraftStoreTests.AModelRevisionThatCitesANewerRevisionOfASideLandsOnAPersonRevision`.
- A refresh queues a withdrawn pair again when a cited revision changed while the link was approved. Enforced by `RelationshipStaleness`. Test: `PostgresRelationshipDraftStoreTests.RefreshQueuesAWithdrawnPairAgainWhenACitedRevisionChangedWhileItWasApproved`.
- The approval gate reads the cited records after it takes the relationship lock. Enforced by `PostgresRelationshipReviewStore.DecideAsync`. Test: `PostgresRelationshipReviewStoreTests.TheApprovalGateReadsTheCitedRecordsAfterItTakesTheRelationshipLockSoAStatementApprovalThatCommitsFirstBlocksIt`.
- The read and the editor show the flag `no_longer_matches`. Enforced by `PostgresRelationshipReviewStore.ToReview` and the pages. Tests: `PostgresRelationshipReviewStoreTests.ANoLongerMatchingPairShowsTheFlagInTheRead` and `RelationshipReviewWorkspaceTests.ANoLongerMatchingPairShowsANoticeInTheEditor`.
- A model revision never lands on an approved relationship. The run keeps the outcome `DraftDiscarded` and the entry settles as `unchanged`. Enforced by `RelationshipDraftRows.AppendAnalysisRevisionAsync` and `RelationshipDraftQueueMapping`. Test: `PostgresRelationshipDraftStoreTests.AnApprovalThatLandsWhileTheModelRunsLeavesTheApprovedRevisionAndSettlesTheRunAsUnchanged`.
- Only a reviewer reads, saves, or decides, and the analysis author never decides. Enforced by the review handlers. Test: `PostgresRelationshipReviewStoreTests.OnlyAReviewerMayReadSaveOrDecideAndTheAnalysisAuthorNever`.
- A relationship that does not exist is not found. Enforced by `GetRelationshipReview` and `RelationshipEditor`. Tests: `PostgresRelationshipReviewStoreTests.ARelationshipThatDoesNotExistIsNotFoundAndSavingOrDecidingOnItFailsLoud` and `RelationshipReviewWorkspaceTests.AnUnknownOrMalformedRelationshipIsNotFoundAndOnlyAConfiguredSubjectMayOpenThePages`.
- A stale save returns a conflict and shows both versions. Enforced by `ReviewRecordPage` and `RelationshipEditor`. Tests: `PostgresRelationshipReviewStoreTests.AStaleSaveIsAConflictAndKeepsTheNewerRevision` and `RelationshipReviewWorkspaceTests.AStaleSaveReturnsConflictKeepsTheTypedWordingAndShowsBothVersions`.
- The editor shows the statement passage and the facts of the target side by side, with both cited revisions and the match. Enforced by `PostgresRelationshipReviewStore.GetAsync` and `RelationshipEditor`. Tests: `PostgresRelationshipReviewStoreTests.TheReadShowsTheCitedRevisionAndTheFactsOfThatRevisionNotTheNewerApprovedOne` and `RelationshipReviewWorkspaceTests.TheEditorShowsTheStatementPassageAndTheTargetFactsSideBySideWithTheCitedRevisionsAndTheMatch`.
- A relationship revision is appended under the relationship review lock and follows the current revision. Enforced by `RelationshipDraftRows`. Test: `PostgresRelationshipDraftStoreTests.OneWorkerPassDraftsTheApprovedPairSavesRevisionOneUnderTheReviewLockAndLeavesTheDraftAwaitingReview`.
- Each task charges its own budget. Enforced by the primary key of `statement_analysis_budget`. Test: `PostgresStatementGateStoreTests.TheGateChargesItsOwnDailyBudgetAndNotTheDocumentChangeBudget`.

## Behavior

### Candidate queue

`PostgresDocumentExtractionStore.SaveAsync` calls `PostgresStatementCandidateRegistration` after it inserts a new extraction, in the same transaction. An attempt came from admission when its job has an idempotency key that starts with `discovery-admission/`. The job is found through `collection_job_attempts`. Admission never admits a listing page of a source, so the configured page and the allowed path prefix never become candidates. See [collection](collection.md#discovery-and-admission).

The registration inserts the candidate with `ON CONFLICT (candidate_id) DO NOTHING`. When that insert adds a row, it also inserts the Pending `gate` row in `statement_analysis`. A failure rolls back the extraction, the candidate, and the gate row together.

The first registered extraction of a candidate is the one that the registration inserts first. For a page job it is the extraction whose attempt has the earliest observed time. A feed item has its own extraction and keeps it. Ties go to the lower attempt ID, then the lower extraction ID. An extraction has no stored creation time, so the observed time of its capture sets the order.

### Feed items

When a source sets `statementTextInFeed`, `JobPreparation` also reads each captured feed of the source. It runs after article admission. `FeedStatementExtractions` parses the stored capture again. For each item it takes `content:encoded`, else `description`, and converts the HTML with `ExtractHtmlFragmentAsync`. It skips an item with no link or no text. It also skips an item whose link is outside the scope of the source or that an earlier item already used. It reads at most `maxCandidates` scoped links, newest first, as the collector counts them. It skips an item whose markup cannot be read and writes a warning with the count. One bad item never blocks the other items. It makes no guess about items that hold a summary only.

Each kept item makes one extraction, with the index of the item in the feed as its part. The text starts with the lines `Feed item guid`, `Link`, `Title`, and `Published`, then a blank line, then the body. A line is left out when the feed has no value. These lines are part of the cited text, so a citation can name the guid and the link. Footers that the site adds stay in the text.

`PostgresDocumentExtractionStore.SaveFeedItemsAsync` takes the items newest first. The newest item has the latest `pubDate`, and an item with no date comes last. The call skips a link that already has a candidate. It then saves the extraction, registers the candidate with `ON CONFLICT (candidate_id) DO NOTHING`, and stops when the capture has `maxJobs` candidates. `maxJobs` is the admission limit of the source. The candidate URL is the item link. Its attempt is the feed capture. A feed that is not valid RSS XML, or is larger than 10 million characters, blocks the job with the code `feedStatementTextInvalid`. The page jobs of the same check are still admitted.

### Issue gate

The gate reads the first extraction of each candidate and decides whether the article touches a tracked issue. It also decides whether the article names a bill or a recorded vote. A pass queues the `statement-draft` task.

The gate executor is `AnalysisWorker<StatementGateRun>` with `StatementGateStep`. It runs beside the collection and drafting loops in the `worker` process. It has its own status row, daily limit, and wakeup. It uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`, the same setting as drafting (default 2, maximum 4). The wakeup uses the `civic_lens_analysis` channel with the payload `statement_analysis:gate`. Its deadline query reads the `gate` rows of `statement_analysis`.

With `--once`, the worker drains collection, then document-change drafting, then the gate, then statement drafting. If one loop fails, all stop.

The gate runs only when `CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS`, `ANTHROPIC_API_KEY`, and `CIVIC_LENS_ISSUES_CONFIG` are set. A daily limit without a key is a configuration error. When the issue file is not set, the gate stage does not run. The worker then records the limit and the hard pause reason `issuesConfigurationUnset` in the gate status row. `statements status` shows both.

**Input.** The input is the extraction text, the tracked issues active on the first capture date, and all policy areas. The date is the UTC date of the observed time of the extraction's attempt. An issue is active when `startsOn <= date < endsBefore`, and a missing bound is open. Issues and policy areas keep the order of the configuration file. The request uses model `claude-haiku-5-5`, effort `medium`, and at most 4,000 output tokens.

**Prompt and cache.** The system prompt holds the instructions, every policy area, and the active tracked issues, in configuration order. It holds no value of the article. The user message holds the source ID, the URL, the capture date, and the article text, marked as untrusted data.

The adapter marks the end of the system prompt with an ephemeral `cache_control` breakpoint (5-minute TTL). The JSON schema holds only the ID enums of the active issues and the policy areas. Two articles with the same active issue set therefore get a byte-identical system prompt and schema. A change to the schema clears the cache. Each run records cache read and cache write tokens, and both count toward the charge. A golden test pins the system prompt, the schema, and one input hash to the prompt and schema versions.

**Output.** Structured outputs fix these fields: `trackedIssues` (each with `id`, `quote`, `confidence`), `policyAreas` (each with `id`, `confidence`), `namesBillOrVote`, and a `confidence` for the whole answer. `namesBillOrVote` is true when the release reports a speaker action on a specific bill, resolution, or recorded vote. The actions are to introduce, cosponsor, vote, or state a position. The run output keeps the answer. It can label examples for a later benchmark ([D-13](../roadmap.md#pending-decisions)). There is no free-text reason.

Code rejects an unlisted ID, a repeated ID, and a confidence that is not finite or is outside 0 to 1. It rejects a quote that is empty, longer than 2,000 characters, missing, or ambiguous, or that splits a character. A quote must occur exactly once in the extraction text. Code then computes the UTF-16 offsets and checks them with `DocumentTextSpan`. Code never repairs an answer.

**Outcome.** The gate row ends Succeeded, and its `error_code` holds the reason code. The run keeps the same code.

| Reason | Meaning | `statement-draft` row |
|---|---|---|
| `passed` | At least one matched tracked issue with a verified quote | Inserted |
| `passed-bill-or-vote` | No tracked issue matched, and the model answered `namesBillOrVote` true | Inserted |
| `passed-bill-number` | No tracked issue matched, the model answered false, and the text holds a bill number | Inserted |
| `no-tracked-issue` | No tracked issue matched, the model answered false, and the text holds no bill number. Or no tracked issue is active on the capture date (no model call). | None |
| `over-byte-cap` | The prompt is over the byte cap, so there is no model call | Inserted |
| `gate-failed-open` | Two invalid answers, or a retry prompt over the cap | Inserted |

The resolver picks the reason in this order: a verified issue, then the model answer, then the bill number. A release that passes only by a bill or a vote has no tracked issue, and its draft starts with an empty issue list. When no tracked issue is active on the capture date, the gate makes no model call and does not look for a bill number.

The byte cap is the smaller of two values: 100,000 UTF-8 bytes for the full prompt, and the [call cap](analysis.md#budget) minus the output allowance. The settlement and the `statement-draft` insert share one transaction. A repeat insert keeps one row.

An invalid answer gets one more call, with the rejection reasons added to the prompt. The queue keeps the ID of the rejected run, so a budget wait between the two calls resumes the second call. Both rejected runs stay.

Provider, auth, and budget conditions follow the [drafting rules](analysis.md#retries-and-pauses):

- durable retry with backoff
- a pause until restart for a 401, 402, 403, or 404
- a timed pause after three provider failures in a row
- WaitingForBudget until the next UTC day

A refusal, a `max_tokens` stop, an unreadable response, and other 4xx responses fail the entry. The article then waits for `statements requeue`.

### Statement drafting

The drafting executor is `AnalysisWorker<StatementDraftRun>` with `StatementDraftStep`. It drains the `statement-draft` rows that the gate queues and writes an AI draft of one statement record for each candidate. It runs beside the collection, document-change drafting, and gate loops in the `worker` process. It has its own wakeup, status row, daily limit, and call cap, and it uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`.

The executor runs when `CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS` and `ANTHROPIC_API_KEY` are set. A limit without a key is a configuration error.

**Speakers.** The source of the candidate and the UTC date of its first capture select the covered officials. The configuration is the revision that the collection job saved when it admitted the article. `PostgresStatementDraftStore` reads that revision with the evidence, through the candidate, its attempt, and the job. It decodes the job definition with the job store.

`StatementSpeakerRoster` is a pure function over a configuration revision. It lists the officials that the source covers in that revision on that date, in ID order. The names come from the People list of that revision. A job with no configuration revision, or a revision without the source, covers no official. The count of officials sets the shape of the prompt.

| Covered officials | Shape | Speaker |
|---|---|---|
| None | No call. The entry is Blocked as `speakersUnavailable`. | None |
| One | The schema has no speaker field. | Code sets that official. A speaker field in the answer is rejected. |
| Several | The schema has `speakerOfficialIds` with an enum of their IDs. | The model chooses at least one listed official. An unlisted or repeated ID is rejected. |

`otherSpeakers` holds the names of other people whom the article quotes. A name must be plain: 1 to 128 characters, with no space at either end. It must occur in the article text. It is never a linked identity.

**Prompt and output.** The system prompt holds the instructions only. Each shape has its own fixed system prompt, with no value of the article or the source. The user message holds the source ID, the URL, the capture date, the covered officials, and the article text, marked as untrusted data. The request uses model `claude-sonnet-5-5`, effort `medium`, and at most 16,000 output tokens.

Structured outputs fix these fields:

- `headline` and `summary`
- `statementDate`: a date, an exact quote, and a confidence, or null
- `speakerOfficialIds`, in the several shape only
- `otherSpeakers`
- `quotations`, each a quote and a confidence
- a confidence for the whole answer

The model is not asked for issues or policy areas. The golden test pins the system prompt, the schema, and one input hash for each shape.

**Checks.** Code checks these rules and rejects the whole answer on any failure:

- The headline and summary are present and within their bounds.
- Every confidence is finite and from 0 to 1. This applies to the whole answer, the date, each speaker, each other speaker, and each quotation.
- The statement date is `yyyy-MM-dd`. Its quote occurs exactly once in the article text. The quote states the date in a form that document-change drafting accepts (`AnalysisAnswerRules.StatesDate`). Code then checks the UTF-16 offsets with `DocumentTextSpan`.
- There are 1 to 32 quotations. Each quote occurs exactly once in the article text, and no two quotes are the same span.

A quote error is a citation error. Every other error is an output error. The loop gives one more call with the reasons. After a second rejection, the entry fails with `citationRejected` or `outputRejected`. Both rejected runs stay, and the owner can requeue the entry. A retry prompt over the byte cap also fails the entry with the rejection code. The byte cap uses the same formula as the gate.

**Quotation kind.** `QuotedText` finds the quoted passages once for each article.

- A straight double mark toggles. A curly opening mark opens and a curly closing mark closes.
- A mark that does not fit the current state is plain text. Quotations do not nest.
- A quotation does not cross a line break. An opening mark with no closing mark on its line runs to the end of that line. This matches a quotation that continues into the next paragraph.
- Single quotation marks and apostrophes are ignored.

A span is a `DirectQuotation` when it lies entirely inside one quoted passage. Every other span is `ReleaseText`, also a span that contains the marks.

**Bill numbers.** `BillNumberFinder` finds `H.R.`, `S.`, `H.Res.`, `S.Res.`, `H.J.Res.`, `S.J.Res.`, `H.Con.Res.`, and `S.Con.Res.` numbers of 1 to 5 digits in the article text. It accepts spacing variants and writes each number in one form, such as `H.R. 1234`. It does not accept a form without periods, such as `HR 1234`. Each number appears once, in order of first use. The revision keeps them in `billNumbers`.

**Gate carry-over.** The revision starts with the issue IDs and policy area IDs of the gate. `PostgresStatementDraftStore` reads the latest gate run that has a reason. `StatementGateCarryOver` takes the verified tracked issue IDs and the policy area IDs when the reason is one of the three pass reasons. The two bill or vote reasons give an empty issue list. A failed-open or over-cap gate gives empty lists. Reviewers edit the lists later.

**Settlement.** A drafted settlement runs in one transaction in the C# store. It adds the draft, revision 1, the run with its link, the queue settlement, and the budget change. A draft that exists before the call succeeds the entry as `humanDraftExists`, with no call. A draft that appears during the call keeps the run as `DraftDiscarded` and adds no second draft. The entry then succeeds as `draftDiscarded`.

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | Draft and AI revision 1 written |
| Succeeded | `over-byte-cap` | No model call. A `Bypassed` run and a draft with no revision. Review shows the statement as not drafted, and a reviewer writes the draft. |
| Succeeded | `humanDraftExists`, `draftDiscarded` | A draft already existed |
| Blocked | `evidenceUnavailable`, `speakersUnavailable` | The input is missing |
| Failed | `citationRejected`, `outputRejected`, a stop code, or `attemptsExhausted` | Owner requeue needed |

### Statement code approval

`StatementDraftStep` reads the evidence once. It resolves the allowed selections and the covered officials with `StatementSelectionResolver`. After the draft passes the resolver, `StatementCodeApproval.Check` decides. `Check` returns an `ApprovalCheck`. The result goes in `StatementDraftCheckpoint.CodeApproval`.

Code approves only when all these rules hold. Otherwise the draft waits for a person, as before. The check stops at the first failure. It tests the gate, the issues, the speaker, and then the approval rules of a person. The draft row keeps the failure message in `held_reason`; see [review](review.md#code-approval).

- The source of the article sets `officeOf`, and the draft has that person as its one speaker. The draft names no other speaker.
- The latest gate run is a pass. A failed-open or over-cap gate gives `StatementGateCarryOver.None`, so it never passes. Each issue of the draft has a verified gate quote.
- Every quote occurs exactly once in the article text. `StatementDraftResolver` already enforces this.
- The statement date is null. Or its quote states it, and it lies on the capture day or up to 7 days before it. `StatementDraftResolver` checks the quote with `AnalysisAnswerRules.StatesDate`. `StatementCodeApproval` owns this window and passes `dateReadByCode` to `RequireApprovable`. A person approval never passes it, so a person still needs a confirmed date. The public page then says "Date read from the release by code".
- A gate pass about a bill or vote with no tracked issue can be code-approved. The draft then has an empty issue list.
- The approval rules of a person pass: `StatementAllowedSelections.RequireApprovable`.

The messages are the constants `GateNotPassed`, `IssueNotInGate`, and `SpeakerIsNotOfficeOwner` of `StatementCodeApproval`. The date rule and the approval rules of a person give the message of `RequireApprovable`.

At settlement, `StatementDraftRows` appends the first revision through `ReviewRecords.AppendCodeApprovedAsync`. The Approve decision has the actor `civic-lens:code`. `StatementDraftQueueMapping` then runs `AfterCodeApprovalAsync`, so relationship pairs and brief staleness follow as for a person approval.

### Statement review

Statement review mirrors document-change review and uses the shared decision rules in [review](review.md#review-decisions). The Host has statement pages on top of the shared handlers.

**Ownership.** Core/Statements holds `StatementReview`, the aggregate of one candidate, and the rules on `StatementDraftRevision`. Application/Statements holds the commands `CreateStatementDraft`, `SaveStatementDraft`, and `DecideStatementReview`. It also holds the reads `GetStatementReview`, `ListStatementReviews`, and `ListStatementsNotInReview`, and the store boundary `IStatementReviewStore`.

Infrastructure/Statements holds `PostgresStatementReviewStore` and `StatementReviewQueries`. The store runs on the shared `ReviewRecords` core; see [review](review.md#storage-and-contracts). The handlers use `ReviewAuthorization`, `ReviewValidation`, `PageCursor`, `KeysetSlice`, and `PageLimit`. Every command needs a reviewer. The AI author can never decide or author a person revision.

**Write lock.** Statement writes take the statement review key; see [advisory locks](../architecture.md#advisory-locks). The AI settlement and the over-cap draft row take the same lock. A human create and an AI settlement for one candidate therefore run one after the other, and only one first revision exists.

**Create.** `CreateStatementDraft` writes revision 1 with the person as author, empty text, and no selections. It inserts the draft row, or it adds revision 1 to an over-cap row that has no revision. A draft that has a revision raises `StatementDraftExistsException`. The candidate and its extraction must exist.

**Save.** The request names the candidate, the expected revision number, the text, and the date with its evidence span. It also names the speakers, the other speakers, the issue IDs, the policy area IDs, and the quotation spans. A person never types the quotation kind or the bill numbers. `StatementDraftResolver.WithArticleDerivedFields` sets both, and the AI drafting path uses the same method.

- `StatementReviewContextReader` reads the evidence once for each write.
- `GetStatementReview` reads the page once. The store also reads the admitting configuration revision. `StatementSelectionResolver` gives the allowed selections and the officials to both paths.
- The speakers must be covered officials of the admitting configuration revision on the capture date (`StatementSpeakerRoster.OfficialsOn`).
- The issue IDs and policy area IDs must be in the issue configuration.
- Every span must cite the extraction of the candidate and fit in its text. `StatementDraftRevision.ValidateShape` checks the bounds.
- A stale expected revision raises `ReviewConflictException`.

**Published labels.** A published statement holds `textOrigin` and `approvedBy`. `approvedBy` is `code` when `StatementCodeApproval` approved the statement, and `person` otherwise. A code approval with a date also shows a date label; see [Statement code approval](#statement-code-approval). See [publication](publication.md#release-content).

**AI date.** An AI-proposed statement date survives a person's save only when the person ticks that it was checked. `AiProposedDate` in Core/Review owns this rule for statements and document changes. A person cannot approve an AI revision that has a date as it is. Ticking Date checked and pressing Save and approve writes a person revision that keeps the date and approves it. Code can approve the AI revision, when it read the date from the release; see [Statement code approval](#statement-code-approval).

**Decide.** `DecideStatementReview` checks the expected revision number and review state version, then applies `ReviewPolicy`. `ReviewValidation.CreateDecision` builds the decision for both record types. An approval runs `StatementAllowedSelections.RequireApprovable`, which calls `StatementDraftRevision.ValidateForApproval`. A person approval also runs `RequireConfirmedDate`. The approval needs a headline, a summary, at least one speaker, at least one quotation, and a date with evidence or no date. It also checks the allowed selections again and checks the spans against the extraction.

**Reads.** `GetStatementReview` returns the aggregate, the status, the source ID, the URL, and the extraction for quote display. It also returns the run ID when revision 1 is an AI draft. It runs the approval check on the saved revision of a statement that is not approved. The check uses the rules for a person. The result is `Approval`. A candidate with no draft row reads as `NotDrafted`.

`ListStatementReviews` takes the filter `All`, `NeedsAction`, or `Approved`. Every filter orders by newest event date, then candidate ID; the statement list has no tier. See [review](review.md#lists-and-paging). The list also takes an issue, an official, and an event date range. `NotDrafted` counts as needs action. `GetStatementReviewCounts` counts each filter with the same status rule, and the Review home page shows the needs action count.

`ListStatementsNotInReview` lists candidates that have no draft revision, each with its stage and reason code. These are over-cap draft rows. They are also candidates with no draft row whose gate or draft entry is skipped, failed, or blocked. `StatementReviewQueries` holds the unpublished approved and not-in-review SQL.

**Pages.** `/Review/Statements` lists the drafts and the statements that are not in review.

- The drafts table has the filter tabs All, Needs action, and Approved, with their counts. It has a filter form for issue, official, and dates, and a pager on `ListStatementReviews`. It shows the headline (or "Not drafted"), the source, the status, and the open concern count.
- The query values are `drafts` (filter), `draftsCursor`, `issue`, `official`, `from`, `to`, `notInReview` (cursor), and `status`.
- The "Not in review" table has a pager on `ListStatementsNotInReview`. It shows the source, the URL, and the reason code. Each row has a Start draft post form.

`/Review/Statements/{CandidateId}` mirrors the document-change editor. It shows the AI authorship notice with the analysis run, the open-concern notice, and the compare panel after a stale save. It also shows the failure text of `Approval` in a notice at the top, so a person sees why Save and approve fails.

- The Statement panel has the tabs "Saved revision N" (`StatementRecord`) and "Edit wording".
- The Evidence panel shows the article text with the cited spans marked through `DocumentText` and `CitedText`.
- The Decide panel and the History table are the shared `ReviewDecisionPanel` and `ReviewHistoryPanel`.
- A `NotDrafted` candidate shows the evidence and a Start draft form in place of the editor.
- Both roles can draft and decide. The handlers keep the analysis subject out.

**Forms.** The pages use the static post-back pattern: named forms, an antiforgery token, and idempotency keys for each render. Hidden fields hold the expected revision and review state version. They post, redirect, and get with `?status=saved`, `decided`, or `started`.

`ReviewRecordPage` is the one owner of the failure mapping for both editors:

| Failure | Result |
|---|---|
| `ReviewConflictException` | 409, and the typed wording stays |
| `ArgumentException` | 400 |
| `UnauthorizedAccessException` | Redirect to `/access-denied` |
| Missing record | 404 |
| Any other failure | 503 |

A save checks the expected revision before it resolves the chosen spans, because the offered spans depend on the latest revision.

**Edit form.** The fields are the headline, the summary, and the statement date with a "Date checked" box for an AI-proposed date. The date evidence is a radio group with a "No statement date" choice. The speakers are choices from the officials allowed on the capture date. Other speakers are one name per line, at most `MaximumOtherSpeakers`.

Issues and policy areas are choices from the allowed sets. The quotations are checkboxes. The quotation kind and the bill numbers are not editable; the save handler sets them.

**Span picker rule.** `StatementSelectableSpans` builds the spans that the form offers, in this order:

1. the quotations and date evidence of the saved revision
2. the double-quoted passages that `QuotedText` finds in the article
3. the article paragraphs, each a line from `DocumentLines`, the same splitter that `DocumentComparison` uses

The list drops a repeated (start, length) pair and a span longer than `MaximumQuotationLength`. It stops at 200 spans. The page accepts only a posted key that the list offers.

### Gate labels

A gate label is the answer a person gives for one release. It lists the tracked issues that the release speaks to. It also says whether the release names a bill or a recorded vote. The labels measure the gate and the models that may replace it ([D-13](../roadmap.md#pending-decisions)).

**Store.** `PostgresGateLabelStore` adds one row for each save. A row holds the candidate ID, the extraction ID of the saved text, the author, the optional run ID, and the label JSON. The extraction ID must be the extraction of the candidate. `GetAsync` returns the saved text, the newest human label, and the newest model label.

**Split.** `GateLabelSplit.IsHeldOut` hashes the candidate ID with SHA-256. It holds the release out when the first four bytes, as a number, give a remainder under 30 when divided by 100. The result never changes for an ID. About 30 percent of releases are held out.

The store refuses a model label for a held-out release. A person still labels a held-out release, but never sees a model label for it.

**Page.** `/Review/GateLabels` is for the owner only. A reviewer goes to the access-denied page. It lists unlabeled releases, with held-out releases first. They are the test set, so a person label is the only label they can have. A release with only a model label is still unlabeled. The list shows 25 releases and the total.

`/Review/GateLabels/{CandidateId}` shows the saved text, a checkbox for each tracked issue of the issue file, and a yes or no choice for `namesBillOrVote`. The yes or no choice has no default. The form is prefilled with the human label, or else with the model label when the release is not held out. A save always adds a human row. It confirms or corrects the prefill. After a save, the page opens the next unlabeled release.

**Labeling rules.** People, model pre-labels, and judges use the same rules. They apply where the gate prompt is open to more than one reading.

- A listing page has no issue and no bill. Examples are press release indexes, news pages, issue pages, and newsletter archives. A listing page is not a statement, so the gate must skip it.
- Count the body of the release, including a background paragraph. Ignore sidebars, related links, and navigation.
- `namesBillOrVote` needs a bill title, a bill number, or a roll call that the text identifies. A bill with no name or number gives false.
- `namesBillOrVote` needs the speaker's own action or position on the bill, such as "I voted for" or "I introduced". A law that only funds an announced grant gives false.
- Tag every issue that matches. A conflict tags both `foreign-policy-and-war` and its narrower issue, as the issue file says.

**Export.** `statements labels export <path>` writes one JSON line for each release that has a human label. A line holds `candidateId`, `extractionId`, `heldOut`, `issueIds`, `namesBillOrVote`, and `text`. The text is the public release text. The command refuses a path that is not inside the `.runtime` directory under the current directory. Run it from the repository root.

### Relationship drafting

The executor is `AnalysisWorker<RelationshipDraftRun>` with `RelationshipDraftStep`. It runs after vote drafting and before brief drafting in the `worker` process. It has its own wakeup, status row, daily limit, and call cap, and it uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`. Its required variables are in [operations](../operations.md#relationship-drafting). The status row is in `document_change_analysis_status` under the task `relationship-draft`.

**Pair finding.** A review decision on a statement, an action, or a vote finds the pairs of that record. `RelationshipStaleness.AfterDecision` does this inside the decision transaction, as `BriefStaleness.AfterDecision` does for briefs. The review stores of the three record kinds run it before the brief hook. It works in this order:

1. It saves the decision row and the draft status first, because the read needs the new state. A record that is not approved ends the hook here. Otherwise it locks the pair rows of the record in pair key order and then takes the relationship review lock.
2. For a statement, it reads the approved statement. It also reads the approved actions that a speaker sponsors and the approved votes on the bills that the statement mentions. For an action, it reads the approved action and the approved statements of its sponsors. For a vote, it reads the approved vote and the approved statements of its members that carry a date in the Congress of the vote.
3. `RelationshipPairFinder` makes one pair for each statement and each action or vote that it matches. It takes the bill number first and the bill title second.
4. It inserts a Pending queue row for each new pair, with `ON CONFLICT DO NOTHING`. It moves the stale pairs of the record back to Pending. It sets the match flags of the pairs of the record.

The insert and the requeue fire the queue trigger, so an idle relationship worker wakes at commit. The queue owner `PostgresAnalysisQueue` does the requeue, so the lifecycle rules apply. A decision on a record that is not approved finds no pair and changes no row.

Records that were approved before the migration have no pairs. The owner runs `relationships draft refresh` once. It runs the same finder and the same writes over every approved statement.

**Staleness.** A fingerprint is a hash of the pair key, the statement revision number, and the match basis and span. It also covers the paired official, the role of that official, the numbered target facts, the task version, and the prompt version. The role holds the sponsorship, the withdrawal date, or the cast of that official. The facts are the text that the prompt shows. A change of another member does not change the fingerprint. The `context_json` of the latest `Drafted` or `Bypassed` run holds the fingerprint of the last settled input. A Succeeded pair returns to Pending when its fingerprint differs and its relationship is not approved. It also returns when its current revision cites an old target revision. After a none answer (latest settled run reason `noRelationship`), it does not return for this cause. The old draft stays and a person rejects it. With the same fingerprint, the step copies the AI revision to the new citation in a `Bypassed` run. The run has the reason `targetRecited` and makes no model call. This needs an AI revision whose statement citation and match equal those of the pair. Otherwise the step drafts with the model. A Blocked pair with the code `sideNotApproved` returns when both sides are approved again. An approved relationship never returns. A running pair settles first and then returns by the same rule.

A vote pairs by bill number, and a number needs the statement date. An undated statement can never pair with a vote, so the vote read leaves it out. An action read keeps undated statements, because a title can pair them.

The finder may miss a pair of the decided record. Then its draft gets `no_longer_matches`, but only while both sides are approved. A statement that is back in review flags nothing. Code never deletes the draft. The flag clears when the pair is found again.

A Failed row and a Blocked row with the code `overByteCap` or `evidenceUnavailable` do not return on a new revision, as for briefs. The owner requeues a Failed row. A new revision does not change an over-cap prompt.

**Step.** The step reads the queue row and the current approved revision of each side. Then it follows this order:

1. A side that is not approved blocks the entry with `sideNotApproved`. The entry waits for the next refresh.
2. The step finds the pair again with `RelationshipPairFinder`. When no pair remains, the entry settles as `noLongerMatches` with no call.
3. When the fingerprint equals the last settled fingerprint, the entry settles as `unchanged` with no call.
4. Otherwise, the step runs the validation loop with at most two passes, like the other drafting steps.

An accepted answer appends revision 1 or the next revision in the settlement transaction, under the relationship review lock. A none answer keeps its `Drafted` run with the reason `noRelationship` and appends nothing.

**Prompt and output.** The prompt holds the date and the officials of the statement, its quoted passages, and the words that code matched. It also holds the role of the official and the facts of the record. An action has these facts: the bill identity, the saved title, and the headline and summary of the approved revision. A vote has the lines of its record. The role is the sponsorship (`sponsor`, `cosponsor`, `original cosponsor`, and a withdrawn date) or the cast of the vote.

The model answers four fields: `type`, `reason`, `statementQuote`, and `targetFactId`. The schema limits `type` to the types of the target kind and `none`. It limits `targetFactId` to the listed fact IDs. No confidence field exists.

**Checks.** `RelationshipDraftResolver` rejects an answer when:

- the reason is empty, has more than one line, is over 400 characters, or names a configured official that the record does not list
- the fact ID is not listed
- the quote is not unique in the article, is not inside one quoted passage of the statement, or leaves out the matched words

A rejection gets one more call with the reasons added. A second rejection fails the entry. Code sets the matched span, so the model cannot move it.

**Revision.** `RelationshipRevision` holds the record ID (the pair key), the number, the author, and the time. It cites both revisions by kind, record ID, and revision number. It also holds the match basis with its span, the type, the reason, the quote span, and the fact ID. A person edit is described in [Relationship review](#relationship-review).

**Outcomes.**

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | A revision was appended |
| Succeeded | `noRelationship` | The model answered none |
| Succeeded | `unchanged` | The fingerprint matches the last settled input, an approval landed while the call ran, or a person revision cites the same records |
| Succeeded | `noLongerMatches` | Both sides are approved, but the pair no longer matches |
| RetryWaiting | `revisionConflict` | Another writer appended first |
| Blocked | `evidenceUnavailable`, `sideNotApproved`, `overByteCap` | The input cannot be drafted now |
| Failed | `outputRejected`, `citationRejected`, a stop code, or `attemptsExhausted` | Owner requeue needed |

**Commands.** These are owner commands.

| Command | Effect |
|---|---|
| `relationships draft status` | Queue status, limit, pause reason, and the count of statements that name a bill number and have no Congress |
| `relationships draft list [view] [limit]` | Queue rows. Views: `drafted`, `unchanged`, `none`, `unmatched`, `pending`, `failed` |
| `relationships draft requeue <pair-id\|--all>` | Failed rows back to Pending |
| `relationships draft refresh` | Finds the pairs of every approved statement. Run it once after the migration |

The count reads the approved statements only. A statement has no Congress when it has no date or a date before 1935.

### Relationship review

Relationship review uses the shared decision rules and review cycles in [review](review.md#review-cycles). The Host has relationship pages on top of the shared handlers. The handlers are `GetRelationshipReview`, `SaveRelationshipDraft`, and `DecideRelationshipReview`. `PostgresRelationshipReviewStore` implements `IRelationshipReviewStore` on `RelationshipDraftRows.Records`, the same `ReviewRecords` core as the other kinds.

**Reads.** `GetRelationshipReview` returns the review cycle and the exact cited revisions. These are the cited statement revision with its article text, and the cited target with its numbered facts. It also returns one flag for each side. `RelationshipCitedReader.FindChangedAsync` sets a flag when the cited revision is not the current revision of a draft with the status Approved.

**Edit options.** `RelationshipEditOptions` offers the saved quote and each passage of the cited statement that covers the matched words. It also offers the facts of the cited target. The types come from `RelationshipTypes.For` for the kind of the target. The page and the store use these options, so one rule decides what a person may pick.

**Save.** `SaveRelationshipDraft` copies the expected revision, applies the four fields, and runs `RelationshipRevision.ValidatePersonEditOf`. The store runs the options check inside the save transaction, after the revision-number check. A save writes a revision with the author kind `Person`.

**Decide.** `DecideRelationshipReview` builds the decision as the other review kinds do. On approval, the store refuses the decision when `FindChangedAsync` names the statement or the target. The refusal is an invalid-input error, and the page shows it as a bad request. A decision runs no after-decision hook, because no other record kind cites a relationship. A withdrawal or a request for changes has no flag check.

**Pages.** `/Review/Relationships/{record-id}` has these parts. The Needs a person queue lists a relationship that needs a person. An approved relationship has no headline, so the approved-record search does not reach it. Open it with Find a draft by record ID, or from a reader report. The owner chose to keep approved relationships out of the search for now. Revisit this choice with R-52. No relationship list page exists.

- the statement panel with its headline, summary, and saved quote. It also shows the cited revision, the date, the match basis, and the matched words, with a link to the statement
- the target panel with its cited revision and its numbered facts, and a link to the action or vote review page
- the saved revision and the "Edit link" form. The form lists the types that fit the target, a one-line reason, the offered quotes, and the facts
- notices for a changed cited record, a pair that no longer matches, and a revision from the AI drafter. A changed record also adds a header badge
- the shared `ReviewDecisionPanel` and `ReviewHistoryPanel`

**Changed record.** The flag blocks approval only. A relationship that is approved stays approved when a cited record changes, and the notice tells the reviewer to withdraw the approval. No event requeues it. The queue returns a pair to Pending only when its relationship is not approved. After a person withdraws a link with stale citations, the owner runs `relationships draft refresh`. The refresh requeues the pair, because the link is no longer approved. A model call that finishes after an approval does not append a revision: the settlement keeps the approved revision.

### Commands

All statement commands are owner commands.

| Command | Effect |
|---|---|
| `statements status` | Gate status, limit, and pause reason |
| `statements list [outcome] [limit]` | Gate rows. Outcomes: `passed` (all three pass reasons), `skipped`, `failed-open`, `over-cap`, `pending`, `failed` |
| `statements run <run-id>` | One gate run |
| `statements requeue <candidate-id\|--all>` | Failed gate rows back to Pending |
| `statements regate [--since <date>]` | Skipped gate rows back to Pending when the latest run used another issue revision than the current file, or another gate prompt version |
| `statements draft status` | Drafting status |
| `statements draft list [view] [limit]` | Draft rows. Views: `drafted`, `over-cap`, `existing`, `pending`, `failed` |
| `statements draft run <run-id>` | One draft run |
| `statements draft requeue <candidate-id\|--all>` | Failed draft rows back to Pending. It never moves a gate row. |
| `statements labels export <path>` | Labels of record as JSON lines, inside `.runtime` only |

With `--since`, `regate` moves only candidates first captured on or after that UTC date. A skip made under another gate prompt version moves too, because the new prompt may find a bill or a vote. `regate` never runs by itself, and it needs `CIVIC_LENS_ISSUES_CONFIG`. A passed, failed-open, or over-cap gate row never moves.

## Storage and contracts

- The generated [statements schema](schema/statements.md) lists every table, column, key, check, index, and trigger.
- Candidate identity is the source plus the admitted URL. It is hashed as `PostgresCollectionJobStore.HashCandidate` hashes it for `collection_candidate_jobs`. The two tables share this value without a foreign key.
- `statement_candidates` keeps the first extraction of the article and its attempt. For a feed item, the URL is the item link and the attempt is the feed capture.
- `statement_analysis` is keyed by `(candidate_id, task)`. Its tasks are `gate` and `statement-draft`. A `gate` row exists for every candidate. A `statement-draft` row exists after the gate lets the candidate pass.
- `statement_analysis` has the queue columns and checks of `document_change_analysis`, without `draft_id`.
- The trigger `trg_statement_analysis_notify` calls `civic_lens_notify_analysis_change`. The gate and drafting workers therefore wake on the same channel as the document-change worker.
- Gate lifecycle rules: a `Succeeded` entry needs a known reason code. A skipped entry may return to Pending for `regate`. A settled pass queues the `statement-draft` row in the same transaction.
- Core adds the run outcomes `Classified` and `Bypassed`. `AnalysisRun.ComparisonId` holds the candidate ID for gate runs.
- `statement_analysis_runs` keeps one insert-only row for each model call or bypass. It adds the issue revision, the reason, and the verified quotes to the document-change run.
- A gate run keeps the `IssueConfigurationRevision` ID it used. `context_json` holds a copy of the active rubric. An `Interrupted` gate run has no issue revision.
- A `Bypassed` run has no model call and no tokens. Its input hash is the text hash.
- A draft run has the task `statement-draft` and no issue revision ID. A `Bypassed` draft run holds the reason `over-byte-cap`. Its `context_json` holds the covered officials and the carried IDs.
- `statement_analysis_budget` holds the charged tokens for each UTC day and task. The gate, statement drafting, and document-change drafting each have a separate budget. `PostgresAnalysisQueue` runs the budget steps for both budget tables. The table mapping names the budget table and the task column.
- Status rows are in `document_change_analysis_status` under the task `gate` or `statement-draft`. The task name `draft` belongs to document-change drafting.
- `statement_drafts` has one row for each candidate, keyed by the candidate ID. `current_revision_number` is 0 when the draft has no revision.
- Each `statement_revisions` row has the author kind (`ReviewAuthorKind`: `Person` or `Analysis`). Revision 1 by the analysis author is the AI revision. Code only inserts revision rows.
- `StatementDraftRevision` holds the headline, the summary, and the statement date with its evidence span. It also holds `speakerOfficialIds`, `otherSpeakers`, `issueIds`, `policyAreaIds`, the quotations (an extraction span and a kind), and `billNumbers`. Confidence is never in the revision; it stays in the run output.
- The review record ID is the 64-character candidate ID. `ReviewValidation.ValidateCandidateId` accepts a lowercase SHA-256 value only.
- `statement_drafts` also has `event_on`, `issue_ids`, and `official_ids`, with GIN indexes on the two arrays and the index `ix_statement_drafts_listing`. Create, save, and the AI draft each call `StatementDraftRows.SetListing` with the revision and the capture date. The over-cap draft row gets the capture date.
- `statement_drafts` also has `last_revised_at_utc_ticks` and the nullable `held_reason`; see [review](review.md#storage-and-contracts).
- `statement_drafts` also has `review_state_version` and `review_status`. The store computes both with `ReviewPolicy` under the lock.
- A draft with no revision has the status `NotDrafted`.
- `statement_review_decisions` holds the immutable decision JSON and the same decision as columns. `statement_review_decision_resolutions` holds the concerns that an approval resolves; see [review](review.md#invariants).
- The commands reuse the replay receipt table with the operations `statement-create`, `statement-save`, and `statement-decide`.
- `statement_gate_labels` holds the gate labels. Its key is a 32-character label ID. A row names the candidate and the extraction. It also holds the author subject, the author kind (`Person` or `Analysis`), the run ID, the label JSON, and the creation time. `ck_statement_gate_labels_run` ties the run ID to the `Analysis` kind. The run ID has a foreign key to `statement_analysis_runs` and a unique index, so one run labels one release.
- Seven tables hold relationship drafting: `relationship_analysis`, `relationship_analysis_runs`, `relationship_analysis_budget`, `relationship_drafts`, `relationship_revisions`, `relationship_review_decisions`, and `relationship_review_decision_resolutions`. The review store writes the last two.
- `relationship_analysis` is keyed by `(pair_id, task)`. A row also holds the statement candidate ID, the target kind (`Action` or `Vote`), and the target record ID. The candidate ID has a foreign key to `statement_candidates`. The trigger `trg_relationship_analysis_notify` calls `civic_lens_notify_analysis_change`.
- `relationship_analysis_runs` adds the reason `unchanged`, `noRelationship`, `noLongerMatches`, or `targetRecited`. The reason `targetRecited` marks a `Bypassed` run that tried to copy a revision to a new target citation. A refused copy keeps the reason and copies nothing. The refusals are `personEditKept`, `alreadyApproved`, and `revisionConflict`. Every run keeps `context_json`. A matched run holds the pair key, the fingerprint, and both cited revisions.
- `relationship_drafts` has one row for each pair that has a revision. It has the review columns of the other draft tables and the flag `no_longer_matches`.
- The relationship review lock is `civic-lens-relationship-review`. It is declared between the vote key and the brief key. Revision appends take it. The publication lists of locks do not hold it until relationship publication exists.
- Relationship drafting has its own budget table, separate from the other budgets.
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Revision, quotation kind, bill numbers | `src/CivicLens.Core/Statements/StatementDraftRevision.cs`, `src/CivicLens.Core/Statements/QuotedText.cs`, `src/CivicLens.Core/Statements/BillNumberFinder.cs` |
| Candidate registration | `src/CivicLens.Infrastructure/Statements/PostgresStatementCandidateRegistration.cs` |
| Feed item text | `src/CivicLens.Application/Collection/FeedStatements/FeedItemParser.cs`, `src/CivicLens.Application/Collection/FeedStatements/FeedItemRenderer.cs`, `src/CivicLens.Application/Collection/FeedStatements/FeedStatementExtractions.cs`, `src/CivicLens.Application/Documents/FeedItemExtraction.cs` |
| Gate | `src/CivicLens.Application/Statements/StatementGateStep.cs`, `src/CivicLens.Application/Statements/StatementGatePrompt.cs`, `src/CivicLens.Application/Statements/StatementGateResolver.cs`, `src/CivicLens.Application/Statements/StatementGateLifecycle.cs`, `src/CivicLens.Application/Statements/RegateStatementCandidates.cs` |
| Drafting | `src/CivicLens.Application/Statements/StatementDraftStep.cs`, `src/CivicLens.Application/Statements/StatementCodeApproval.cs`, `src/CivicLens.Application/Statements/StatementDraftPrompt.cs`, `src/CivicLens.Application/Statements/StatementDraftResolver.cs`, `src/CivicLens.Application/Statements/StatementDraftLifecycle.cs`, `src/CivicLens.Application/Statements/StatementSpeakerRoster.cs`, `src/CivicLens.Application/Statements/StatementGateCarryOver.cs` |
| Shared quote check | `src/CivicLens.Application/Statements/ArticleQuote.cs`, `src/CivicLens.Core/Documents/UniqueQuote.cs` |
| Stores and models | `src/CivicLens.Infrastructure/Statements/PostgresStatementGateStore.cs`, `src/CivicLens.Infrastructure/Statements/StatementAnalysisListing.cs` (queue table name, shared list and article read), `src/CivicLens.Application/Statements/StatementArticle.cs` (article record), `src/CivicLens.Infrastructure/Statements/PostgresStatementDraftStore.cs`, `src/CivicLens.Infrastructure/Statements/StatementGateQueueMapping.cs`, `src/CivicLens.Infrastructure/Statements/StatementDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Statements/AnthropicStatementGateModel.cs`, `src/CivicLens.Infrastructure/Statements/AnthropicStatementDraftModel.cs` |
| CLI and composition | `src/CivicLens.Host/Statements/StatementCommand.cs`, `src/CivicLens.Host/Statements/StatementGateComposition.cs`, `src/CivicLens.Host/Statements/StatementDraftComposition.cs` |
| Review aggregate | `src/CivicLens.Core/Statements/StatementReview.cs`, `src/CivicLens.Core/Review/AiProposedDate.cs` |
| Review commands and reads | `src/CivicLens.Application/Statements/CreateStatementDraft.cs`, `src/CivicLens.Application/Statements/SaveStatementDraft.cs`, `src/CivicLens.Application/Statements/DecideStatementReview.cs`, `src/CivicLens.Application/Statements/GetStatementReview.cs`, `src/CivicLens.Application/Statements/ListStatementReviews.cs`, `src/CivicLens.Application/Statements/GetStatementReviewCounts.cs`, `src/CivicLens.Application/Statements/ListStatementsNotInReview.cs`, `src/CivicLens.Application/Statements/StatementSelectionResolver.cs`, `src/CivicLens.Application/Statements/StatementSelectableSpans.cs` |
| Review store | `src/CivicLens.Infrastructure/Statements/PostgresStatementReviewStore.cs`, `src/CivicLens.Infrastructure/Statements/StatementReviewQueries.cs` |
| Review pages | `src/CivicLens.Host/Statements/StatementList.razor`, `src/CivicLens.Host/Statements/StatementEditor.razor`, `src/CivicLens.Host/Review/ReviewRecordPage.cs` |
| Gate labels: label, split, store | `src/CivicLens.Core/Statements/GateLabel.cs`, `src/CivicLens.Core/Statements/GateLabelEntry.cs`, `src/CivicLens.Core/Statements/GateLabelSplit.cs`, `src/CivicLens.Application/Statements/IGateLabelStore.cs`, `src/CivicLens.Infrastructure/Statements/PostgresGateLabelStore.cs` |
| Gate label use cases | `src/CivicLens.Application/Statements/SaveGateLabel.cs`, `src/CivicLens.Application/Statements/ListGateLabelQueue.cs`, `src/CivicLens.Application/Statements/GetGateLabelTask.cs`, `src/CivicLens.Application/Statements/ExportGateLabels.cs` |
| Gate label pages and export | `src/CivicLens.Host/Statements/GateLabelList.razor`, `src/CivicLens.Host/Statements/GateLabelEditor.razor`, `src/CivicLens.Host/Statements/GateLabelExport.cs` |
| Relationship pairs | `src/CivicLens.Application/Relationships/RelationshipPairFinder.cs`, `src/CivicLens.Application/Relationships/RelationshipPair.cs`, `src/CivicLens.Application/Relationships/RelationshipTarget.cs`, `src/CivicLens.Application/Relationships/ApprovedStatement.cs`, `src/CivicLens.Application/Relationships/StatementPassage.cs`, `src/CivicLens.Application/Relationships/StatementReading.cs`, `src/CivicLens.Application/Relationships/BillMention.cs`, `src/CivicLens.Application/Relationships/RelationshipFingerprint.cs` |
| Review priority of actions and votes | `src/CivicLens.Infrastructure/Statements/StatementBillMentions.cs`, `src/CivicLens.Infrastructure/Relationships/PairPriority.cs`, `src/CivicLens.Application/Relationships/RelationshipPairRule.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipRevisionScope.cs` |
| Relationship rules | `src/CivicLens.Core/Relationships/CongressCalendar.cs`, `src/CivicLens.Core/Relationships/BillTitleMatcher.cs`, `src/CivicLens.Core/Relationships/RelationshipTypes.cs`, `src/CivicLens.Core/Relationships/RelationshipRevision.cs`, `src/CivicLens.Core/Statements/BillNumberMatch.cs` |
| Relationship drafting | `src/CivicLens.Application/Relationships/RelationshipDraftStep.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftPrompt.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftResolver.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftLifecycle.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftTask.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftErrorCodes.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftEvidence.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftContext.cs`, `src/CivicLens.Application/Relationships/RelationshipDraftSettings.cs`, `src/CivicLens.Application/Relationships/IRelationshipDraftStore.cs`, `src/CivicLens.Application/Relationships/GetRelationshipDraftStatus.cs`, `src/CivicLens.Application/Relationships/RefreshRelationshipPairs.cs` |
| Relationship stores | `src/CivicLens.Infrastructure/Relationships/PostgresRelationshipDraftStore.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipStaleness.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipPairSource.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipPairScope.cs`, `src/CivicLens.Infrastructure/Review/AfterDecisionHooks.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipEvidenceReader.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipDraftRows.cs`, `src/CivicLens.Infrastructure/Relationships/AnthropicRelationshipDraftModel.cs`, `src/CivicLens.Infrastructure/Relationships/Persistence/` |
| Relationship CLI and composition | `src/CivicLens.Host/Relationships/RelationshipCommand.cs`, `src/CivicLens.Host/Relationships/RelationshipDraftComposition.cs`, `src/CivicLens.Host/Relationships/RelationshipDraftConfiguration.cs` |
| Relationship review rules | `src/CivicLens.Core/Relationships/RelationshipReview.cs`, `src/CivicLens.Core/Documents/SourceSpan.cs`, `src/CivicLens.Application/Relationships/RelationshipEditOptions.cs` |
| Relationship review commands and reads | `src/CivicLens.Application/Relationships/SaveRelationshipDraft.cs`, `src/CivicLens.Application/Relationships/DecideRelationshipReview.cs`, `src/CivicLens.Application/Relationships/GetRelationshipReview.cs`, `src/CivicLens.Application/Relationships/IRelationshipReviewStore.cs`, `src/CivicLens.Application/Relationships/RelationshipReviewRead.cs` |
| Relationship review store | `src/CivicLens.Infrastructure/Relationships/PostgresRelationshipReviewStore.cs`, `src/CivicLens.Infrastructure/Relationships/RelationshipCitedReader.cs` |
| Relationship review pages | `src/CivicLens.Host/Relationships/RelationshipEditor.razor`, `src/CivicLens.Host/Relationships/RelationshipRoutes.cs`, `src/CivicLens.Host/Relationships/RelationshipLabels.cs` |
| Reserved author kind | `src/CivicLens.Core/Review/ReviewAuthor.cs` |

The gate and drafting run on the shared owners in [analysis](analysis.md#code-map).

## Limits and planned work

- An approved link whose cited record changed shows only in its editor. No queue row names it until the planned citation rows land. The ready list leaves out such an unpublished link, and a publish by ID refuses it. A release that already holds it keeps it until a person withdraws it. An approved link that no longer matches does show to the owner in the Needs a person queue; see [review](review.md#needs-a-person-queue).
- Actions are in [actions](actions.md). Weekly briefs are in [briefs](briefs.md).
- Two parallel preparations of one source can deadlock when their feed items have an equal `pubDate` and swap order. Postgres aborts one transaction, the worker retries it, and no data is lost. The insert order stays as it is.
- Planned: structured statement extraction and temporal identity resolution.
- A feed item source still admits page jobs for the same links. A page that returns 403 fails and adds nothing. A page that works adds nothing when the feed text came first.
- The feed parser reads RSS 2.0 only. It uses the first `link` element of an item and ignores `xml:base` and the charset of the HTTP header. An Atom feed blocks the job with `feedStatementTextInvalid`.
- `FeedStatementExtractions` converts the HTML of every item before the store skips links that already have a candidate. The application layer has no list of known links, and a new read for it would cost more than the conversion. The `maxCandidates` limit bounds the work.
- An item that the feed later edits keeps its first text. A later edit is not a new statement.
- Each quotation stays addressable for later matching with actions.
- Planned after the first deploy ([R-38](../roadmap.md#later)): find the same or nearly the same sentences in releases from different offices. It shows coordinated messaging and who used the words first.
- The bill or vote answer of the model is unmeasured. The `passed-bill-or-vote` and `passed-bill-number` counts show how often each path fires. The bill number rule finds only the forms that `BillNumberFinder` accepts.
- The gate labels are the first part of D-13. The model pre-label, the model comparison, and the fine-tuning are planned. A model label needs `GateLabelEntry` with the `Analysis` kind and the ID of a run in `statement_analysis_runs`.
- The label page offers every tracked issue of the issue file. It does not hide an issue that was inactive on the capture date.
- The label queue reads every unlabeled candidate (ID, source, and URL) on each list load and each save, because code computes the split. Revisit this if labeling moves to a large set.
- Two labels with equal timestamps fall back to label ID order. This is acceptable for a single owner.
- Pages outside the URL rule of admission, such as `/news/email` subscribe pages, can still reach the gate. The gate does not reliably reject listing pages. In a preview, about half of the releases where models disagreed were listing pages.
- The labeling rules for unnamed bills, bills cited as credit, and sidebars are stricter than the gate prompt. A later prompt version may adopt them after the benchmark.
- A label save has no idempotency key. A double submit adds two equal rows, and the newest is the label of record.
- The export path check compares full paths. It does not resolve symbolic links.
- The split is a hash, so the held-out share is near 30 percent but not exact.
- Measured on 2026-10-16 with 30 House press feeds: the gate dropped 25 of 48 candidates. Most were grants and events. It also dropped a release about a farm bill vote, two bill introductions, and a Senate inquiry.
- Measured on 2026-10-16: 4 of 21 drafts carry a bill number. 2 of them name a bill with a collected House vote, and both members voted on it.
- Measured on 2026-10-16: some issue tags are doubtful. For example, a post office closure was tagged `elections-and-voting`.
- Each record page shows a timeline of its own dated events. It needs no review, because it only orders dates that the record file holds. See [publication](publication.md).
- A relationship that is flagged "no longer matches" can still publish. The flag shows in review.
- Planned after a measurement of the releases that title matching leaves unmatched: code lists the bills and votes of the official in a date window. The model picks one or none. Code checks the quoted span, and a person approves the pair.
- Measured on 2026-10-17 over 263 gated releases: 38 hold a bill number and 127 name something that ends in "Act". Some of those are listing pages or older laws. No release writes a number without periods, such as `HR 1234`.
- `BillNumberFinder` finds a bare number only. A number that names its Congress, such as "H.R. 1234 of the 118th Congress", takes the Congress of the statement date.
- A pair for an action needs a sponsorship by a speaker. A statement about a bill that the speaker did not sponsor pairs only with a vote that the speaker cast.
- A vote pairs by bill number only. A title match never reaches a vote, because a vote has no saved title.
- A vote decision reads the approved dated statements of its members in the Congress of the vote, with the article text. Revisit this if that set grows large.
- The `context_json` of a run is the only record of the last settled fingerprint. A run row that someone deletes makes the pair draft again.
- The model answer is unmeasured. The `relationships draft list` views count how often it answers none.

Statements and actions acceptance:

- [x] Every statement and action has exact, inspectable provenance.
- [x] Quotation validity and interpretation are separate checks.
- [x] Human review binds to the reviewed version.
- [x] Adding or removing tracked officials needs no code change and keeps history.
