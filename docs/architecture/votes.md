# Votes

## Purpose

The votes feature collects House roll-call votes from the Congress.gov `house-vote` API and Senate roll-call votes from the senate.gov XML files. It fetches each House roll call as two durable jobs, the detail and the member votes. A Senate roll call is one job, because one XML file holds both. Each roll call version with all its pages captured becomes a vote candidate. The `worker` drafts one vote record for each source and roll call from these candidates. Code approves each worker revision after it checks the citations, because the facts come from the House Clerk XML, senate.gov, and Congress.gov. A person can still edit, request changes on, and withdraw a record on the vote review pages. An approved vote record publishes as the vote kind of a release; see [publication](publication.md#vote-record).

## Decisions

- One vote model serves both chambers. The enum `Chamber` (`House`, `Senate`) is a field of the vote revision, the published vote, and the official vote row. The tables of versions, candidates, and drafts have a `chamber` column. Names that hold no chamber rule are neutral: `VoteSession`, `IVoteAdmissionStore`, `PostgresCollectionJobStore.Votes.cs`, and the tables `vote_versions` and `vote_candidates`. Parsers, URLs, the renderer, and the kind table are per chamber and carry the chamber in their names. The reader has one branch for each chamber and shares the rest.
- A roll number restarts in each session and in each chamber. Every key or label of a roll call therefore holds the chamber. This covers the unique indexes, the newer-candidate check, the roll call lookup, the labels, and the record and candidate IDs. The ID hash text starts with `house-vote` or `senate-vote`, from the chamber. `PostgresCollectionJobStore.HashVoteRecord` and `HashVoteCandidate` own this rule. House IDs equal the IDs from before the chamber field.
- The vote connector mirrors the [bills](bills.md) connector. It reuses job keys, admission, budgets, URL page rules, and preparation dispatch.
- The Congress.gov vote list ignores `fromDateTime` and is not ordered by date. The source therefore has no cursor and no window.
- A daily sweep reads the whole list of each session, from the session of `backfillFrom` to the session of today. Unchanged votes cost one list read and no job.
- The session of a calendar year Y has congress `(Y - 1787) / 2` (integer division). Its number is 1 for an odd Y and 2 for an even Y. `VoteSession` owns this rule.
- The sweep is one chain of list jobs. The first job lists the first session. The last page of a session admits the first page of the following session, up to the session of today.
- Every list page is a root job, as for bills, so a running sweep blocks the next scheduled check.
- A detail job is keyed by source, roll call, and exact update time. A known key is a duplicate. A changed vote has a new update time and so gets a new job.
- Every collected roll call becomes a candidate. No relevance gate exists. The noise is measured before a gate is considered.
- A roll call may have no legislation. The bill identity is then null. A legislation type that the bills feature does not store also gives a null bill identity.
- The bill type uses the lowercase form of `CongressBillListItem.NormalizeType`. The known type list has one owner.
- The page limit rules (`limit`, `format`, `offset`) are the ones of `ApiPageQuery`. The 251-job admission rule is the one of bills.
- Preparation selects its work from `apiResource`, never from the URL text.
- A deferred job would be lost. Admission therefore throws `InvalidDataException` and saves nothing when the budget defers a job.
- The members response is one request. It lists every member, so it has no paging. Measured on 2026-10-16, a roll call lists 433 members.
- The detail and members `updateDate` were equal on 4 live votes on 2026-10-16 (119/1/100, 119/1/306, 119/1/320, 119/2/74). A members response with another update time than the recorded detail is still refused, because it is another version.
- The sweep of a session starts on 1 January, about 2 days before Congress starts on 3 January. The API returns 200 for a session that has not started (measured 2026-10-16 for `/house-vote/120/1`), so the early sweep is harmless.
- The Speaker election has totals by candidate, not by party. The parser reads them as candidate totals, and the renderer writes one line for each. The parser and render versions stay at v1. No input that gave a v1 extraction renders other text. A party item that also names a candidate is now invalid. No captured vote has that shape.
- Preparation renders each detail and each members response into extraction text, as the [bills](bills.md#extraction) connector does for its pages. The candidate binds both extractions. Drafting cites the exact lines of these extractions.
- A vote record cites the title and the policy area of its bill. The bill detail comes from the [bill stream](bills.md#admission), which saves a detail extraction for every listed bill. The vote feature adds no job type and no job admission across streams.
- A candidate whose roll call has a bill identity waits for a saved bill detail of that bill. It gets no queue row until then. The bill detail admission queues the waiting candidates in its own transaction. A candidate without a bill identity is queued at once.
- Drafting reads the newest saved bill detail of the bill, whichever source saved it. The match is the congress of the vote, the bill type, and the bill number.
- The tracked issues of a vote come from the policy area of its bill on the vote day. `BillGate.TrackedIssueIdsFor` owns this rule, as for [actions](actions.md#step).
- Drafting copies [action drafting](actions.md#step): its own queue, budget and run tables, the shared analysis worker, and the shared revision store `ReviewRecords`.
- One vote record exists for each source and roll call. A re-collected vote with a newer update time appends a revision to the same record.
- Every candidate is drafted. No gate exists. The only candidate that settles without a revision is a roll call with no tracked member.
- Code produces every fact with its citation. The model writes only the headline and a plain summary of the question.
- The model sees only the fact lines. It sees no member and no vote, and its text may name no configured person. Code lists the tracked members and their votes beside the text. So copied text can never state a vote that a later correction changed.
- When only the votes of tracked members change, drafting makes no model call. It copies the text of the current revision, as action drafting does for sponsorships.
- A person edits only the headline and the summary of a vote record. The revision holds a `TextOrigin` and a suggestion, with the same append rules as an [action](actions.md#revision-and-append). Text that a person wrote survives later model runs. The model text then becomes a suggestion.
- Votes get review as actions do: a review store, a list page, an editor page, a decide handler, review counts, and review history.
- Review writes take the `civic-lens-vote-review` lock, the key that the drafting append already takes. Publication also takes this key, after the action key and before the brief key.
- The owner command `votes draft` has `status`, `list`, and `requeue`. It copies the `actions draft` command and its shared queue commands. `status` also shows the number of waiting votes.
- A Blocked vote entry can return to Pending. The lifecycle allows the move, as the [action](actions.md#decisions) lifecycle does. A Succeeded entry never returns to Pending. The owner command moves only Blocked entries with an exact code.
- The vote kind (D-10, accepted on 2026-10-17) comes from a code table, not from the model. `VoteKindTable` maps the `Question` line of a House vote to a kind. `SenateVoteKindTable` does the same for a Senate vote. The kinds are rule, previous question, motion to recommit, amendment, suspension, passage, conference report, veto override, nomination, cloture, motion to proceed, and other. An unknown question gives other. House questions give none of the last three.
- `SenateVoteKindTable` matches the start of the `Question` line, with the strings of the real senate.gov files. Any cloture question is cloture, and `On Overriding the Veto` is veto override. `On the Nomination` is nomination, and `On the Motion to Proceed` is motion to proceed. Amendment, conference report, and passage reuse the House kinds. A resolution is passage as in the House. `On the Resolution of Ratification` is other. An unknown question is other. A cloture vote on a nomination is cloture, because the vote decides to end debate. The Senate table does not read the document type, because the question already names every nomination vote in the data.
- A motion to commit and a motion to commit with instructions are a motion to recommit. A motion to concur in a Senate amendment is passage. A consideration of a resolution stays other.
- A resolution agreed to by the question `On Agreeing to the Resolution` is a rule when its legislation is a House resolution (`HRES`). Its bill title must also hold `consideration of`, in any case and at any position. Any other resolution so agreed is passage, because agreeing to it is its final vote. This choice is deliberate: the kind list has no resolution kind.
- Code stores the kind in the revision at draft time, with the cited `Question` line, as `PolicyAreaId` is stored for actions. Publication never derives it again.
- Code also stores the vote day, `VotedOn`, with the cited `Started` line, as it stores the kind. The day is the date of the start time in the offset that the API gives. A vote without a start time blocks with `evidenceUnavailable`, because its day would have no citation. The vote recovers when the API sends a later roll call version, which is a new candidate. The task version is `vote-draft-v5`. It rose to v4 when the revision gained the field `Chamber`, and to v5 when the prompt gained a Senate form. Code reads no older revision shape, so the dev rows are reset.
- The citation covers the `Question` line only. The rule test also reads the `Legislation` and `Title` facts. The same revision cites them.
- D-15 (accepted on 2026-10-19): code approves the worker revisions of a vote, as it does for [actions](actions.md#decisions). Public pages label the headline and the summary as an AI summary of the official record when the origin of the published text is Model. They add no label when a person wrote the text.
- A worker revision of a newer candidate gets a new code approval if three rules hold. No concern is open. The revision holds no text that a person wrote. The approval checks pass. A person edit stays with a person even after a person approved it. The release keeps the earlier approved revision until the owner publishes the new one. A copy revision for changed member votes follows the same rule.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- The event date of a vote is its vote day. The draft row copies it, the candidate ID, the tracked issue IDs, and the member person IDs from the current revision at each append. Enforced by `VoteDraftRows.Revised`. Test: `PostgresHouseVoteTests.EachWorkerRevisionReplacesTheListedColumnsWithThoseOfTheCurrentRevision`.
- One record exists for each vote version. Enforced by `ux_vote_versions_roll`. Test: `PostgresHouseVoteTests.ASecondDetailJobOfTheSameVoteVersionRecordsNothingAndAdmitsNoMembersJob`.
- A changed vote is a new version with its own members job. Enforced by `HouseVoteJobKeys` and `PostgresCollectionJobStore.AdmitDetailAsync`. Test: `PostgresHouseVoteTests.AChangedVoteIsRecordedAsANewVersionWithItsOwnMembersJob`.
- A bill type has a number, and a number has a type. Enforced by `ck_vote_versions_bill` and `ck_vote_candidates_bill`. Test: `PostgresHouseVoteTests.TheDatabaseRefusesABillTypeWithoutANumber`.
- A candidate needs a recorded detail of the same version. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.MembersOfAVersionWithoutARecordedDetailAreInvalidDataAndWriteNoCandidate`.
- A candidate binds an extraction of its own detail job and an extraction of its own members job. Enforced by `PostgresCollectionJobStore.EnsureExtractionIsOfJobAsync`. Test: `PostgresHouseVoteTests.ADetailWithAnExtractionOfAnotherJobIsInvalidDataAndRecordsNoVote`.
- A vote version has one candidate. Enforced by `ux_vote_candidates_detail_job`. Test: `PostgresHouseVoteTests.ReplayingTheMembersOfAVoteAddsNoSecondCandidate`.
- A detail alone makes no candidate. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.NoCandidateExistsUntilTheMembersOfTheVoteAreCaptured`.
- A roll call without legislation still gets a candidate. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.ARollCallWithoutLegislationStillBecomesACandidateBecauseNoRelevanceGateExists`.
- Each job URL has the exact form of its resource kind. Enforced by `HouseVoteListUrl`, `HouseVoteRollUrl`, and `CollectionJobDefinition.Validate`. Test: `HouseVoteSourceConfigurationTests.AJobUrlMustHaveTheExactFormOfItsResourceSoTheWorkerNeverReadsItAsAnotherKind`.
- A next link stays in the source scope and the session, advances the offset, and carries no `api_key`. Enforced by `HouseVoteListUrl.ReadNext`. Test: `HouseVoteListUrlTests.NextLinksCarryingACredentialAreRejectedSoNoKeyIsStoredInAJobDefinition`.
- A malformed response is invalid data and blocks preparation. Enforced by the three parsers. Test: `JobPreparationTests.AMalformedVoteResponseIsInvalidDataSoTheWorkerBlocksThePermanentFailure`.
- A total is a party total or a candidate total, never both or neither. Enforced by `HouseVoteParser`. Test: `HouseVoteParserTests.ATotalMustBeExactlyOneOfAPartyTotalOrACandidateTotalWithACountSoNoCountIsMisfiled`.
- A response for another roll call than the job URL is invalid. Enforced by `HouseVoteWire.ToVote`. Test: `HouseVoteParserTests.ADetailOfAnotherRollCallThanTheJobRequestedIsInvalid`.
- The sweep covers every session from `backfillFrom` to today, also across a year boundary. Enforced by `VoteSession`. Test: `VoteSessionTests.TheSweepFollowsSessionByStepUntilTheSessionOfToday`.
- Vote admission never defers a job; a deferral fails the transaction. Enforced by `PostgresCollectionJobStore`. Test: `PostgresHouseVoteTests.AFullPageWithItsNextPageJobFitsTheBudgetButOneMoreJobIsAnInvalidDeferralThatSavesNothing`.
- The shipped source budgets hold a full page and its next-page job. Enforced by `CollectionConfiguration.Validate`. Test: `PostgresHouseVoteTests.ShippedSourceBudgetsAdmitAFullPageAndItsNextPage`.
- One vote record exists for each chamber, source, and roll call. Enforced by `ux_vote_drafts_roll` and `VoteDraftRows.AppendAnalysisRevisionAsync`. Tests: `PostgresHouseVoteTests.TheDatabaseAllowsOneDraftForEachRollCallAndRefusesABadRecordId`, `PostgresHouseVoteTests.ACollectedAgainVoteAppendsRevisionTwoToTheSameDraftBecauseTheRecordIsOnePerRollCall`, `VoteChamberMigrationTests.TheSameRollNumberCanExistInBothChambersButNotTwiceInOne`, and `PostgresRollLookupTests.ARollCallFindsOnlyTheVoteDraftOfItsChamberCongressSessionAndNumber`.
- A newer candidate of another chamber never supersedes a vote. Enforced by the chamber filter of `VoteDraftRows.HasNewerCandidateAsync`. Test: `PostgresHouseVoteTests.ANewerSenateCandidateOfTheSameNumbersDoesNotSupersedeTheHouseVote`.
- A record ID and a candidate ID differ between chambers for the same source, Congress, session, and roll. House IDs keep their old values. Enforced by `PostgresCollectionJobStore.HashVoteRecord` and `HashVoteCandidate`. Test for the record ID: `PostgresHouseVoteTests.HouseIdsKeepTheirOldValuesAndASenateVoteOfTheSameNumbersGetsOtherIds`. Test for the Senate candidate ID: `PostgresSenateVoteTests.SenateCandidateIdsAreTheHashOfTheirFramedTextSoTheyNeverChange`.
- A candidate with a bill identity gets no queue row until a bill detail of that bill is saved. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.AVoteWhoseBillHasNoSavedDetailGetsNoQueueRowBecauseTheDraftNeedsTheBillTitle`.
- A Senate vote file that did not change adds no version. The version key holds the file modify date. Enforced by `PostgresCollectionJobStore.AdmitSenateVoteAsync` through `FindVoteAsync`. Test: `PostgresSenateVoteTests.ASecondFetchOfAFileWithTheSameModifyDateRecordsNothingNew`.
- A Senate candidate is queued like a House candidate. It waits for the bill detail of its bill, and a vote with no bill, such as a nomination, queues at once. Enforced by `PostgresCollectionJobStore.QueueCandidateAsync` and `VoteDraftQueueRows.WaitingForBill`. Tests: `PostgresSenateVoteTests.ASenateCandidateWithoutABillIsQueuedAtOnceBecauseANominationHasNoBillTitleToWaitFor`, `PostgresSenateVoteTests.ASenateCandidateWhoseBillHasNoSavedDetailGetsNoQueueRowBecauseTheDraftNeedsTheBillTitle`, and `PostgresHouseVoteTests.TheBillDetailQueuesAWaitingSenateCandidateAsItQueuesAHouseCandidate`.
- A Senate vote matches a tracked person through the `lis` ID of the roster, taken over all snapshots. A tracked senator with no `lis` ID, or with two different `lis` IDs, blocks the draft with `evidenceUnavailable`. Enforced by `VoteReader` and `PostgresMemberRosterStore.ReadSenatorLisIdsAsync`. Tests: `SenateVoteReaderTests.TrackedSenatorsMatchByLisIdAndEachCastCitesItsMemberLineOfTheSavedText`, `SenateVoteReaderTests.ASenatorWithTwoLisIdsBlocksTheDraftBecauseTheRosterCannotSayWhichIdCastTheVote`, `SenateVoteReaderTests.ATrackedSenatorWithoutALisIdBlocksTheDraftBecauseHisVoteWouldBeLostWithoutNotice`, and `PostgresMemberRosterStoreTests.SenatorLisIdsListOnlySenatorsFromEverySnapshotSoASenatorWhoLeftStillMatchesHisOldVotes`.
- A Senate vote file with a `lis` ID that no roster snapshot maps blocks the draft. Enforced by `VoteReader.ReadSenateAsync`. Test: `SenateVoteReaderTests.AVoteFileWithALisIdThatNoSnapshotMapsBlocksTheDraftBecauseATrackedOfficialCouldBeMissingFromAPublishedVote`.
- The Senate reader reads the capture of a vote file once, because the detail page and the members page name the same job. Enforced by `VoteReader.ReadSenateAsync`. Test: `SenateVoteReaderTests.TheVoteFileIsReadOnceBecauseTheDetailPageAndTheMembersPageAreTheSameCapture`.
- A nomination has no bill and no tracked issue. Its facts are the lines of `VoteFactPrefixes.SenateDetail`. Enforced by `VoteReader` and `BillError`. Test: `SenateVoteReaderTests.ANominationCitesItsQuestionTextTitleAndDocumentAsFactsAndHasNoBillOrPolicyArea`.
- The prompt names the chamber of the vote and its source. The House bytes did not change. Enforced by `VoteDraftPrompt`. Tests: `VoteDraftPromptGoldenTests.ThePromptAndSchemaMatchTheirPinnedVersions` and `VoteDraftPromptGoldenTests.TheSenatePromptNamesTheSenateAndItsSourcesWhileTheHouseGoldenAbovePinsTheHouseBytes`.
- A Senate vote file whose counts differ from its member list is invalid data. Enforced by `SenateVoteParser`. Test: `SenateVoteParserTests.CountsThatDifferFromTheMemberListAreInvalidBecauseOneOfThemIsWrong`.
- A job URL of a Senate source has the exact form of its resource kind. Enforced by `CollectionJobDefinition.Validate` through `SenateVoteUrl`. Test: `SenateVoteSourceConfigurationTests.AJobUrlMustHaveTheExactFormOfItsResourceSoTheWorkerNeverReadsItAsAnotherKind`.
- The bill detail admission queues the waiting candidates of that bill in its own transaction, once. Enforced by `PostgresCollectionJobStore.AdmitDetailAsync` (bill) and `VoteDraftQueueRows`. Tests: `PostgresHouseVoteTests.TheBillDetailQueuesTheWaitingVoteInTheSameTransactionThatRecordsTheBill` and `PostgresHouseVoteTests.ALaterVersionOfTheBillAddsNoSecondQueueRowBecauseALaterDetailDoesNotRedraftAVote`.
- A candidate without a bill identity is queued at once. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.AVoteWithoutABillIdentityIsQueuedAtOnceBecauseItHasNoTitleToWaitFor`.
- A vote and its bill cannot race, because both writes take the job coordination lock. Enforced by `PostgresCollectionJobStore.TransactionAsync`. Test: `PostgresHouseVoteTests.TheVoteWriteAndTheBillDetailWriteBothWaitForTheJobLockSoAVoteAndItsBillCannotRace`.
- Evidence holds the newest saved bill detail of the voted bill. Enforced by `BillDetailRows` and `PostgresVoteDraftStore.ReadEvidenceAsync`. Test: `PostgresHouseVoteTests.EvidenceHoldsTheNewestSavedBillDetailOfTheVotedBillAndNoneForAVoteWithoutABill`.
- Each candidate gets one queue row in the transaction that writes it, or in the transaction of its bill detail. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync` and `VoteDraftQueueRows`. Tests: `PostgresHouseVoteTests.TheCandidateWriteQueuesOnePendingDraftRowInTheSameTransactionAndReplayKeepsIt` and `VoteDraftingMigrationTests.BackfillQueuesOnePendingDraftRowForEveryVoteCandidateThatExistedBeforeTheMigration`.
- Code writes every fact and cites its exact line in the saved extraction. Enforced by `VoteReader`. Test: `VoteDraftStepTests.CitationsPointAtTheExactRenderedLinesSoAReaderCanCheckEveryFactAgainstTheSavedExtraction`.
- The bill title and the policy area enter the record as cited lines of the saved bill detail. Enforced by `VoteReader`. Test: `VoteDraftStepTests.TheBillTitleAndPolicyAreaAreCitedFromTheSavedBillDetailSoTheModelKnowsWhatTheMeasureIs`.
- A bill detail blocks before any call in three cases. It is missing, it has another parser version, or its saved text differs from the render of its capture. Enforced by `VoteReader`. Tests: `VoteDraftStepTests.ABillDetailOfAnOlderParserBlocksBeforeAnyCallBecauseItsLinesMayHaveAnotherMeaning`, `VoteDraftStepTests.ASavedBillDetailThatDiffersFromTheRenderOfItsCaptureBlocksSoTheCitedLinesAreNeverTrustedBlindly`, and `VoteDraftStepTests.AVoteOnABillWithoutASavedBillDetailBlocksBecauseTheDraftNeedsTheTitle`.
- The tracked issues come from the policy area on the vote day, and the revision names the issue revision. Enforced by `VoteDraftStep` and `BillGate.TrackedIssueIdsFor`. Tests: `VoteDraftStepTests.TheTrackedIssuesComeFromThePolicyAreaOfTheBillAndTheRevisionNamesTheIssueRevision` and `VoteDraftStepTests.TheTrackedIssuesAreCheckedOnTheVoteDaySoAnIssueThatEndedBeforeTheVoteIsNotTracked`.
- A change of the title, the tracked issues, or the issue revision calls the model. Enforced by `VoteDraftContext.ChangeSinceCurrent`. Tests: `VoteDraftStepTests.AChangedBillTitleCallsTheModelBecauseTheTitleIsAFact` and `VoteDraftStepTests.AnotherIssueRevisionCallsTheModelEvenWhenEveryFactIsTheSameBecauseTheTrackedIssuesCameFromOtherRules`.
- Only tracked members enter the record. Enforced by `VoteReader`. Test: `VoteDraftStepTests.MembersWhomNobodyTracksNeverEnterTheRecordBecauseOnlyTrackedVotesAreCited`.
- A member is tracked when the source covers the member on the vote day. Enforced by `VoteReader`, which uses the start time of the vote. Test: `VoteDraftStepTests.CoverageIsCheckedOnTheVoteDayBecauseALaterCorrectionMustNotChangeWhoWasTrackedForTheVote`.
- A revision stores the vote day with a bounded citation of the `Started` line, and a vote without a start time blocks. Enforced by `VoteReader` and `VoteDraftRevision.ValidateShape`. Tests: `VoteDraftStepTests.TheRevisionStoresTheVoteDayAndCitesTheStartLineBecausePublishedDatesMustQuoteTheirSource`, `VoteDraftStepTests.AVoteWithoutAStartLineBlocksBecauseItsDayHasNoCitationToPublish`, and `VoteDraftRevisionTests.TheVoteDayMustCiteItsStartLineSoThePublishedDateIsNeverAnUnbackedClaim`.
- The prompt holds only the agreed fact lines. Enforced by `VoteDraftPrompt`. Test: `VoteDraftStepTests.ThePromptHoldsOnlyTheAgreedFactLinesAndNoMemberBecauseCodeOwnsTheMembersAndTheirVotes`.
- The text names no configured person. Enforced by `VoteDraftResolver`. Test: `VoteDraftStepTests.TextThatNamesAnyConfiguredPersonIsRejectedSoNoCopiedTextCanStateAStaleVote`.
- A change of member votes alone makes no model call and keeps the text. Enforced by `VoteDraftContext.ChangeSinceCurrent` and `VoteDraftStep`. Test: `VoteDraftStepTests.ACorrectedMemberVoteAppendsAtOnceWithoutAModelCallAndKeepsTheTextBecauseCodeOwnsTheVotes`.
- A change of any fact calls the model. A change of the tracked member set alone copies the text, because the text names no member. Enforced by `VoteDraftContext.ChangeSinceCurrent`. Tests: `VoteDraftStepTests.AMemberVoteChangeTogetherWithAChangedResultStillCallsTheModel` and `VoteDraftStepTests.AChangedTrackedMemberSetCopiesTheTextWithoutACallBecauseTheTextNamesNoMember`.
- A vote kind is stored in the revision with a bounded citation of the `Question` line. Only the kind other may have no citation. Enforced by `VoteKindTable` and `VoteDraftRevision.ValidateShape`. Tests: `VoteKindTableTests.EachQuestionLineMapsToTheKindThatNamesItsStageSoTheReviewerCanGroupVotesByWhatTheyDecided`, `VoteDraftStepTests.TheRevisionStoresTheKindOfTheQuestionAndCitesTheQuestionLineSoPublishNeverDerivesItAgain`, and `VoteDraftRevisionTests.AKindOtherThanOtherMustCiteItsQuestionSoTheReviewerCanCheckTheKindAgainstTheSavedLine`.
- A resolution is a rule only when it is a House resolution with a bill title that holds `consideration of`. Enforced by `VoteKindTable`. Tests: `VoteKindTableTests.AHouseResolutionThatProvidesForConsiderationIsARuleBecauseItOnlySetsTheTermsOfDebate` and `VoteKindTableTests.AResolutionThatIsNotARuleIsPassageBecauseAgreeingToItIsItsFinalVote`.
- A change of the kind alone copies the text and makes no model call. A change of a fact still redrafts. The stored kind updates at the next draft of that roll call. Nothing requeues stored votes after a table change. Enforced by `VoteDraftContext.ChangeSinceCurrent` and `VoteDraftStep`. Test: `VoteDraftStepTests.AChangedKindAloneCopiesTheTextWithoutAModelCallBecauseTheKindComesFromCodeAndNotFromTheText`.
- The waiting count and the queue rule share one definition of a waiting vote. A vote without a bill identity never waits. Enforced by `VoteDraftQueueRows.WaitingForBill`. Test: `PostgresHouseVoteTests.TheWaitingCountHoldsOnlyVotesOnABillWithNoQueueRowAndDropsWhenTheBillDetailQueuesThem`.
- An owner requeue of Blocked votes moves only Blocked entries with exactly the given code. Enforced by `PostgresAnalysisQueue.RequeueAsync` and `VoteDraftLifecycle`. Tests: `PostgresHouseVoteTests.RequeueBlockedMovesOnlyBlockedVotesWithTheGivenCodeAndResetsTheirAttempts` and `VoteDraftLifecycleTests.ABlockedVoteDraftMovesBackToPendingBecauseTheOwnerCommandRequeuesItAfterFixingTheCause`.
- Only the owner runs `votes draft` commands. Enforced by `ReviewAuthorization.RequireOwner` in each use case. Test: `VoteCommandTests.TheAiDrafterCannotActAsTheOwner`.
- A re-collected vote with the same facts and votes writes nothing. Enforced by `VoteDraftContext.ChangeSinceCurrent`. Test: `VoteDraftStepTests.ARecollectedVoteWithTheSameFactsAndVotesSettlesWithoutACallEvenWhenItsUpdateDateMoved`.
- A roll call with no tracked member writes no revision and makes no call. Enforced by `VoteDraftStep`. Test: `VoteDraftStepTests.ARollCallWithNoTrackedMemberSettlesSucceededWithoutACallOrARevision`.
- The saved extraction text must equal the new render of its capture. Enforced by `VoteReader`. Test: `VoteDraftStepTests.ARenderedTextThatDiffersFromTheSavedExtractionBlocksBeforeAnyCall`.
- A saved extraction of an older parser blocks before any capture is parsed. Enforced by `VoteReader`. Test: `VoteDraftStepTests.AChangedParserVersionBlocksBeforeAnyCaptureIsParsedSoAnUpgradeIsNeverReportedAsBrokenData`.
- A candidate with a newer candidate of the same roll call never becomes a revision. Enforced by `VoteDraftStep` and `VoteDraftRows.AppendAnalysisRevisionAsync`. Tests: `VoteDraftStepTests.ACandidateWithANewerCandidateOfTheSameRollCallIsSupersededWithoutACall` and `PostgresHouseVoteTests.ANewerCandidateThatArrivesAfterTheClaimSupersedesTheAppendSoTheOlderDraftNeverBecomesARevision`.
- The vote page links the action draft of its bill only when the vote names a bill and that bill has a draft. Enforced by `PostgresVoteReviewStore.GetAsync`. Tests: `VoteReviewWorkspaceTests.TheVotePageLinksTheActionDraftOfItsBillAndTheRollCallOnTheClerkSite` and `VoteReviewWorkspaceTests.TheVotePageHasNoActionLinkWhenTheVoteNamesNoBillOrTheBillHasNoActionDraft`.
- The clerk link names the calendar year of the session and writes the roll number without padding. Enforced by `SourcePages.RollCall`. Test: `VoteReviewWorkspaceTests.TheClerkLinkNamesTheCalendarYearOfTheSessionAndWritesTheRollNumberWithoutPadding`.
- A person edits only the headline and the summary. Enforced by `VoteDraftRevision.ValidatePersonEditOf`, checked again under the vote review lock. Test: `VoteDraftRevisionTests.AVoteReviewerEditsOnlyTheHeadlineAndTheSummaryBecauseCodeOwnsTheDayTheKindTheFactsAndTheMemberVotes`.
- A person may keep or clear the suggestion, but never change it. Enforced by `ReviewTextRules.RequireSuggestionKeptOrCleared`, checked again under the lock. Tests: `VoteDraftRevisionTests.APersonMayKeepOrClearTheSuggestionButNeverChangeItBecauseOnlyTheModelWritesSuggestions` and `PostgresHouseVoteTests.APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne`.
- A later model run keeps the text of a person and offers its own text as a suggestion. Enforced by `ReviewText.AfterModelDraft` and `ReviewTextRules.RequireAnalysisText`. Tests: `VoteDraftStepTests.APersonsTextIsKeptAndTheModelsNewTextBecomesASuggestionBecauseAModelPredictionIsNeverAHumanEdit`, `VoteDraftStepTests.ACorrectedMemberVoteKeepsAPersonsTextAndTheSuggestionWithoutAModelCall`, and `VoteDraftRevisionTests.ARevisionAfterAPersonsTextMustKeepThatTextAndOfferTheModelsAsASuggestion`.
- An approval binds to the exact revision that the page showed. Enforced by `ReviewWriteRules` under the lock. Tests: `PostgresHouseVoteTests.ApprovalBindsToTheExactRevisionSoAWorkerRevisionAfterThePageLoadedTurnsItIntoAConflict` and `PostgresHouseVoteTests.ADecisionWithAStaleReviewStateVersionIsAConflictSoTwoReviewersCannotOverwriteEachOther`.
- A model revision never approves itself. Code approves a worker revision in the same transaction. The revision needs no open concern, no text that a person wrote, and passing approval checks. Enforced by `ReviewRecords.AppendCodeApprovedAsync`. Tests: `PostgresHouseVoteTests.CodeApprovesAWorkerVoteRevisionInTheSameTransactionAndTheRecordIsReadyToPublish`, `PostgresHouseVoteTests.CodeApprovesAWorkerRevisionAfterAnEarlierCodeApprovalAndCountsItAsApproved`, `PostgresHouseVoteTests.AWorkerRevisionKeepsAnOpenConcernSoCodeDoesNotApproveIt`, `PostgresHouseVoteTests.ACodeApprovalLeavesAVoteForAPersonWhenAnApprovalCheckFails`, and `PostgresHouseVoteTests.APersonEditAfterACodeApprovalWaitsForAPersonAndAWorkerRevisionOverItStaysForAPerson`.
- A code approval queues the relationship pairs and the brief weeks of the vote, as a person approval does. Enforced by `VoteDraftQueueMapping.AfterTransitionAsync` through `AfterDecisionHooks.ForRecordKind`. Test: `PostgresHouseVoteTests.ACodeApprovalOfAVoteRunsTheRelationshipHookSoAnApprovedStatementOfAMemberQueuesItsPair`.
- An approval checks every citation of the revision against its saved extraction. Enforced by `PostgresVoteReviewStore` and `VoteDraftRevision.ValidateAgainst`. Test: `PostgresHouseVoteTests.ApprovalChecksEveryCitationAgainstItsSavedExtractionSoNoApprovalRestsOnAMissingLine`.
- An append needs the expected revision number and the vote review lock. Enforced by `VoteDraftRows.AppendAnalysisRevisionAsync`. Tests: `PostgresHouseVoteTests.APersonsConcurrentRevisionFailsTheExpectedVersionCheckSoTheRunRetriesWithBackoffAndAppendsNothing` and `PostgresHouseVoteTests.AnAppendWaitsForTheVoteReviewLockSoAHumanSaveAndTheWorkerNeverInterleave`.
- An unchanged or no-member settlement carries its bypassed run. Enforced by `VoteDraftQueueMapping.PrepareSettlementAsync`. Test: `PostgresHouseVoteTests.AnUnchangedOrNoMemberSettlementWithoutItsRunIsRefused`.
- An analysis revision has a run, and a revision follows the current number. Enforced by `ck_vote_revisions_run` and `VoteDraftRevision.ValidateAsAnalysisRevision`. Tests: `PostgresHouseVoteTests.TheDatabaseRefusesAnAnalysisRevisionWithoutARunAndAPersonRevisionWithOne` and `VoteDraftRevisionTests.AnAnalysisRevisionFollowsTheCurrentOneAndAFirstRevisionIsNumberOne`.
- A revision lists 1 to 600 member votes, at most 64 tracked issues, and a valid issue revision ID. Every citation is a bounded span. Enforced by `VoteDraftRevision.ValidateShape`. Tests: `VoteDraftRevisionTests.TheIssueRevisionIsAHashAndTheTrackedIssuesAreBoundedSoTheRecordNamesTheRulesThatChoseThem`, `VoteDraftRevisionTests.ARecordNeedsAtLeastOneMemberVoteBecauseItExistsToListTheTrackedVotes` and `VoteDraftRevisionTests.EveryCitationMustBeABoundedSpanOfAnExtractionSoTheReviewerCanOpenTheLine`.
- The prompt and schema match their pinned versions. Enforced by `VoteDraftTask`. Test: `VoteDraftPromptGoldenTests.ThePromptAndSchemaMatchTheirPinnedVersions`.

## Behavior

### Source configuration

One `api` source with `apiParser` set to `congressHouseVoteList` follows the House votes. It uses the same fields as the bill source: `credential`, `minimumCongress`, and `backfillFrom`. It has no `coverage`, and it covers every configured person with a `bioguideId` from `backfillFrom` on.

`CollectionConfiguration.CoveredPersonIdsOf` owns this coverage for both Congress.gov parsers. `ListedPersonIdsOf` returns no people for the source.

The shipped source `congress-house-votes` sets `backfillFrom` 2025-01-03 and `minimumCongress` 119. It ships with `enabled: false`. Enable steps are in [operations](../operations.md#congressgov-house-vote-stream).

### Daily sweep

The due check of the schedule creates one list job for `VoteSession.FirstOfSweep`. That is the session of `backfillFrom`, or the first session of `minimumCongress` when that is later. No job is created when the session starts after today. The check waits one interval.

A scheduled check does not start while a list job of the source is active. The source checks once a day.

The list URL is `{source url}/{congress}/{session}?format=json&limit=250`, with `offset` on later pages. A list page job admits:

- one detail job for each roll call on the page,
- the next page of the same session, when the page has a `next` link,
- otherwise the first page of the following session, when that session is not after the session of today.

The next-page job key is `house-vote/{source}/page/{list job ID}`, for both kinds of next page.

Paging is by offset. A vote updated during a sweep can move and push another vote past the reader. The next daily sweep reads the list again, so a missed vote appears one day late at most.

### Resource kinds and URLs

A job definition of a vote source carries `apiResource`. The kinds are `houseVoteList`, `houseVoteDetail`, and `houseVoteMembers`. A kind needs a source with `apiParser` `congressHouseVoteList` and a bound configuration revision.

The configuration binding check requires the exact URL form of each kind:

- `houseVoteList`: `{source url}/{congress}/{session}` with `format=json`, `limit=250`, and optionally one `offset` of 0 or more (`HouseVoteListUrl`).
- `houseVoteDetail`: `{source url}/{congress}/{session}/{roll}?format=json` (`HouseVoteRollUrl`).
- `houseVoteMembers`: `{source url}/{congress}/{session}/{roll}/members?format=json` (`HouseVoteRollUrl`).

Numbers must be canonical, the session must be 1 or 2, and the roll must be 1 or more. Any other or repeated parameter, including `api_key`, fails. The parser builds the detail URL itself and ignores the item `url`.

### Parsing

Each succeeded job goes through [preparation](collection.md#preparation). The parsers read the capture through `ICaptureBodyReader`. A malformed response is invalid data, so preparation blocks the job.

| Parser | Version | Reads |
|---|---|---|
| `HouseVoteListParser` | `congress-house-vote-list-v1` | One list page of at most 250 items |
| `HouseVoteParser` | `congress-house-vote-v1` | One detail |
| `HouseVoteMembersParser` | `congress-house-vote-members-v1` | One members response, with at least one member |

`HouseVoteRenderer` renders the parsed detail and members. Its normalization version is `house-vote-render-v2`. The `Started` line keeps the offset of the API, for example `2025-07-02T21:15:00-04:00`, so the cited day is the House day. Version 1 wrote UTC. Code reads no extraction of version 1, so the dev rows are reset.

A list item needs `congress`, `sessionNumber`, `rollCallNumber`, and `updateDate`. Its session must be the session of the page. A detail and a members response need the same roll call as the job URL and an `updateDate`.

The API covers the 118th and 119th Congresses. Each member entry gives `bioguideID`, the name, `voteParty`, `voteState`, and `voteCast`. A member needs `bioguideID` and `voteCast`. The detail parser keeps the question, vote type, result, start time, legislation, party totals, candidate totals, and source data URL. A total is a party total or a candidate total. A party total needs a party. A candidate total needs a candidate and a `total` of 0 or more. The parser ignores other counts on a candidate total. Any other total is invalid. One response can mix both kinds.

An unknown legislation type, such as an amendment, gives no bill identity whatever its number. A known type needs a number of 1 or more. A number without a type is invalid.

### Extractions

The text has one fact on each line, so a citation can quote one line. The extraction parser version is the `Version` of the parser. The source attempt is the captured attempt of the job. Optional facts that the API omits have no line. Line breaks inside a value become spaces. The `Legislation` line keeps the API spelling, such as `HCONRES`, and the vote row stores the lowercase bill type. `RenderedText` owns the line rules for bills and votes.

Detail, with party totals in party order and candidate totals in candidate order:

```
House vote: 119 1 100
Updated: 2025-06-24T12:34:41Z
Started: 2025-04-10T11:08:00-04:00
Question: On Motion to Concur in the Senate Amendment
Vote type: Yea-and-Nay
Result: Passed
Legislation: HCONRES 14
Party total D: Yea 0, Nay 212, Present 0, Not voting 1
Party total I: Yea 0, Nay 0, Present 0, Not voting 0
Party total R: Yea 216, Nay 2, Present 0, Not voting 2
Source data: https://clerk.house.gov/evs/2025/roll100.xml
```

Members, one line each, in bioguide ID order:

```
House vote members: 119 1 100
Updated: 2025-06-24T12:34:41Z
Member: A000055, Robert Aderholt, R, AL, Yea
```

### Admission

`IVoteAdmissionStore` admits one list page, one detail, or one members response in one transaction. It shares `AdmitApiJobsAsync` with the [bill stream](bills.md#admission). The budgets come from `admissionPolicy` through `DiscoveryAdmissionPolicy`.

For each list item, the store admits one detail job. A known key is a duplicate and is skipped. A page of 250 items plus the next page needs 251 jobs. `CollectionConfiguration.Validate` rejects a vote source whose policy cannot reserve them.

A detail job saves its extraction first. It then records the vote version and the extraction in `vote_versions` and admits one members job. The extraction must belong to an attempt of the job. A second detail job of the same version records nothing and admits nothing.

A members job finds the `vote_versions` row by source, roll call, and exact update time. A missing row is invalid data. This covers a vote that changed between the two fetches. The members of that newer version then wait for its own detail job.

When the row is found, the store writes one candidate in `vote_candidates`. The candidate copies the vote identity, the bill identity, and the detail extraction of the row. It adds the members extraction, which must belong to an attempt of the members job. A replay finds the candidate and writes nothing.

The store then queues the candidate for drafting. A candidate without a bill identity is queued at once. A candidate with a bill identity is queued only when a bill detail of that bill is already saved. A saved bill detail is a `bill_pages` row of the role Detail, found through its `bill_gates` row. The extraction alone is not enough, because it is saved before the bill admission transaction.

The bill detail admission (`PostgresCollectionJobStore.AdmitDetailAsync` for bills) records the detail page. It then queues every candidate with the same congress, type, and number that has no queue row. Both paths run in `TransactionAsync` under the job coordination lock. So a vote and its bill cannot both miss each other. A candidate that a newer candidate superseded may still be queued. The step settles it as superseded.

### Job keys

`HouseVoteJobKeys` owns the keys. A replayed preparation finds the known keys and creates no job.

| Job | Key |
|---|---|
| Detail | `house-vote/{source}/vote/{congress}/{session}/{roll}/{updateDate}` |
| Members | `house-vote/{source}/members/{congress}/{session}/{roll}/{updateDate}` |
| Next list page | `house-vote/{source}/page/{list job ID}` |

The update date is the exact UTC time as `yyyy-MM-ddTHH:mm:ssZ`. `BillStreamJobKeys.ExactTime` owns the format.

### Drafting

`VoteDraftStep` runs on the shared `AnalysisWorker<VoteDraftRun>`. It reads the evidence of one candidate and settles the queue entry. The flow follows [action drafting](actions.md#step).

1. `PostgresVoteDraftStore.ReadEvidenceAsync` returns the candidate, the configuration revision, the saved detail and members pages, the current revision of the record, and whether a newer candidate exists. A missing candidate blocks with `evidenceUnavailable`. A newer candidate settles as `supersededByNewerCandidate`.
2. `VoteReader` checks that both pages were saved by the current parser and normalization. It parses both captures again and renders them. A render that differs from the saved text blocks with `renderedTextMismatch`.
3. `VoteReader` takes the facts. A fact is a line of the detail text that starts with `Started: `, `Question: `, `Result: `, `Legislation: `, `Party total `, or `Candidate total `. The Speaker election therefore cites its candidate totals. Each fact keeps its text and a span of its line. The vote type, the update time, and the source data line are not facts.
4. `PostgresVoteDraftStore.ReadEvidenceAsync` also returns the newest saved bill detail of a voted bill. The newest is the one with the latest update time, and the job ID breaks a tie. `VoteReader` checks that its parser and normalization versions are current. It parses the bill capture again with `CongressBillDetailJob`, the code that [action drafting](actions.md#step) also uses. It renders the bill and compares the text with the saved extraction. A voted bill with no saved detail blocks with `evidenceUnavailable`. A bad version blocks with `extractionVersionChanged`. A text that differs blocks with `renderedTextMismatch`. The reader adds the `Title: ` line and the `Policy area: ` line as facts, after the vote facts. The policy area line is omitted when the bill has none. The tracked issues use the policy area of the parsed bill.
5. `VoteReader` takes the member votes. The tracked people are those of `CollectionConfiguration.CoveredBioguidePeopleOf` on the vote day. That is the date of the start time in the offset that the API gives. A vote without a start time blocks before this step. Each tracked member in the members response gives the person, the bioguide ID, the cast, and a span of the member line. The order is the bioguide ID order.
6. The fingerprint is the SHA-256 of the fact lines, with the title and policy area, and the member votes (`VoteFingerprint`, version `vote-fingerprint-v1`). It is the input hash of a run that makes no call.
7. No tracked member settles `Succeeded` with `noTrackedMember`, a bypassed run, and no revision. More than 600 members block with `tooManyMembers`. More than 64 facts, or a fact over 2,000 characters, block with `factTooLong`.
8. `VoteKindTable.Classify` reads the vote kind from the facts, after the bill facts are added. It returns the kind and the citation of the `Question` line. A roll call with no `Question` line gets the kind other and no citation. The snapshot holds both.
9. `VoteDraftContext.ChangeSinceCurrent` compares the snapshot with the current revision. Equal fact texts, an equal kind, equal tracked issues, an equal issue revision, equal members, and equal casts give `unchanged`. Equal facts, issues, and issue revision with other members or casts give `memberVotesChanged`. Equal facts, issues, issue revision, members, and casts with another kind give `kindChanged`. Anything else, or no current revision, drafts with the model.

`memberVotesChanged` and `kindChanged` append a revision at once. `VoteDraftResolver.CopyText` keeps the headline and the summary. The run is bypassed with zero charged tokens. A change of the tracked set is also a member change, because the text names no member.

`VoteDraftStep` takes the tracked issue IDs from `BillGate.TrackedIssueIdsFor`. It uses the policy area, the vote day, and the issue configuration revision of the worker. The revision stores the IDs and the issue revision ID. The IDs are not in the prompt.

The prompt holds the fact lines only. It tells the model that the `Title` line is the title of the measure that the vote concerns. It tells the model to name no person, because code lists the tracked members and their votes beside the text. The task is `vote-draft` with the model `claude-haiku-5-5`, set as `VoteDraftTask.Model`. It allows two passes and 2,000 output tokens. The shared `AnalysisValidationLoop` runs them.

`VoteDraftResolver` rejects an answer that is not the schema. It rejects text over its bound or with a NUL. It rejects a confidence outside 0 to 1. It rejects text that names any configured person. It matches full names, without regard to case. A rejection retries once with the reasons. A second rejection fails the entry with `outputRejected`.

### Outcomes

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | A revision was appended, by the model or as a copy for changed member votes |
| Succeeded | `unchanged` | The facts, the members, and the casts match the current revision |
| Succeeded | `noTrackedMember` | No tracked member is in the roll call, so no record is drafted |
| Succeeded | `supersededByNewerCandidate` | The roll call has a candidate with a later update time |
| RetryWaiting | `revisionConflict` | Another writer appended first |
| Blocked | `evidenceUnavailable`, `tooManyMembers`, `factTooLong`, `extractionVersionChanged`, `renderedTextMismatch`, `overByteCap` | The input cannot be drafted |
| Failed | `outputRejected`, a stop code, or `attemptsExhausted` | Owner requeue needed |

A run that makes no call has the reason `unchanged`, `noTrackedMember`, `memberVotesChanged`, or `kindChanged`.

### Record and append

The record ID is the SHA-256 of the source, the congress, the session, and the roll call (`PostgresCollectionJobStore.HashVoteRecord`). It frames the text with the prefix `house-vote-record:` or `senate-vote-record:`. The update time is not part of it.

`VoteDraftRevision` (Core/Votes) holds the headline, the summary, the text origin, the suggestion, the candidate identity, and the bill identity. It also holds the facts and the vote kind with the citation of its question. Last come the member votes, the tracked issue IDs, and the issue revision ID. `VoteDraftRows.AppendAnalysisRevisionAsync` appends it in the settlement transaction of the queue row. It takes the `civic-lens-vote-review` lock and refuses a candidate with a newer one. It checks the expected revision number and calls `ReviewRecords.AppendRevision`. A refused append discards the run and keeps the entry for a retry.

### Vote review

Vote review uses the shared decision rules and review cycles in [review](review.md#review-cycles). It copies the shape of [action review](actions.md#action-review). The Host has vote pages on top of the shared handlers.

**Ownership.** Core/Votes holds `VoteReview`, the aggregate of one record, and the review rules on `VoteDraftRevision`. Application/Votes holds `SaveVoteDraft`, `DecideVoteReview`, `GetVoteReview`, `ListVoteReviews`, `GetVoteReviewCounts`, and the store boundary `IVoteReviewStore`. Infrastructure/Votes holds `PostgresVoteReviewStore` on the shared `ReviewRecords` core. The core and the draft rows share one instance, `VoteDraftRows.Records`. A person cannot create a vote draft, so the store has no create. The shared text rules of actions and votes are in `ReviewText`, `ReviewTextRules`, and `ReviewTextSuggestion` (Core/Review).

**Save and decide.** `SaveVoteDraft` and `DecideVoteReview` work as the action handlers do. A decision requeues no brief until briefs can cite votes. It still calls `BriefStaleness.AfterDecision`, which takes the brief locks in the action order.

**Reads.** `GetVoteReview` returns the review cycle, the extractions that the loaded revisions cite, and the configured names of the people. The names come from the configuration revisions of the candidates. `ListVoteReviews` takes the filter `All`, `NeedsAction`, or `Approved`. Every filter orders by tier, then newest vote day, then record ID; see [review](review.md#lists-and-paging). The list also takes an issue, an official, and a vote day range. The page holds the tier of each row. `GetVoteReviewCounts` counts with the same status filter.

**Pages.** `/Review/Votes` lists the records with the filter tabs and their counts, and a filter form for issue, official, and dates. A row shows a tier badge for tier 1 and tier 2. Each row shows the headline, the roll call, the vote kind, the status, and the open concerns.

`/Review/Votes/{record-id}` has these parts:

- the change since the last approved revision: the old and the new text side by side, with the facts of each revision beside it
- those facts are the roll call, the kind, the bill, the tracked issues, and the member votes
- the saved revision and the "Edit wording" form, with the suggestion and its "Use suggestion" and "Dismiss" buttons
- the evidence: the vote kind with its cited `Question` line, and each fact with its cited line
- a link to the roll call page on clerk.house.gov
- a link to the action draft of the bill, when the vote names a bill that has a draft
- the tracked members: the name and the cast of each member, with the cited member line
- the shared `ReviewDecisionPanel` and `ReviewHistoryPanel`

The action link comes from `GetVoteReview`. The review read finds the action draft of the bill with `FindBillActionRecordIdsAsync` in the same transaction, which publication uses too. The clerk link is the HTML vote page: `https://clerk.house.gov/Votes/{year}{number}`. The year is the calendar year of the session. The roll number has no padding, so roll 100 of 2025 is `2025100` and roll 5 is `20255`. The format was checked against the live site on 2026-10-19.

A person cannot edit the member votes. The rail has a Votes link.

### Owner commands

`votes draft` needs `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`. `VoteCommand` runs on the shared `AnalysisQueueCommands`, as `ActionCommand` does. The [operations](../operations.md#vote-drafting) guide shows each command.

| Command | Effect |
|---|---|
| `votes draft status` | JSON with `queue` (the status, `chargedToday`, and `counts` for each view) and `waitingForBill` |
| `votes draft list [view] [limit]` | Entries in a view: `drafted`, `unchanged`, `superseded`, `no-member`, `pending`, or `failed` |
| `votes draft requeue <candidate-id\|--all>` | Failed rows, and no others, back to Pending |
| `votes draft requeue --blocked --code <block-code>` | Blocked rows with exactly that code, and no others, back to Pending. The code is required and must not be empty |

`waitingForBill` counts the vote candidates that name a bill and have no queue row. They wait for the bill detail. `VoteDraftQueueRows.WaitingForBill` owns this rule. The bill detail admission uses the same query to queue them.

### Executor

The executor is `AnalysisWorker<VoteDraftRun>` with `VoteDraftStep`. It runs in the `worker` process after action drafting and before brief drafting, with its own wakeup, status row, daily limit, and call cap. It uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`.

Its variables are in [operations](../operations.md#vote-drafting). A daily limit without the key is a configuration error. The status row is in `document_change_analysis_status` under the task `vote-draft`.

### Senate collection

The Senate publishes one XML file for each roll call and one menu for each session. Both are public. The source needs no key and no robots check. senate.gov has no `robots.txt`.

One `api` source with `apiParser` set to `senateVoteList` follows the Senate votes. It sets `minimumCongress` and `backfillFrom` like the House source. It refuses a `credential` and a `coverage` list. It covers every configured person with a `bioguideId`, as the House source does. The roster `lis` ID matches members in the drafting work.

The source URL is `https://www.senate.gov/legislative/LIS`. The shipped source `senate-votes` sets `backfillFrom` 2025-01-03 and `minimumCongress` 119. It ships with `enabled: false`. The admission policy holds 1,000 jobs, because one menu lists up to 999 votes.

The daily sweep is the House sweep. The due check creates one menu job for `VoteSession.FirstOfSweep`. A menu job admits one job for each vote and the menu of the following session. The next-menu key is `senate-vote/{source}/menu/{menu job ID}`.

| Kind | URL (`SenateVoteUrl`) | Parser | Version |
|---|---|---|---|
| `senateVoteMenu` | `{source url}/roll_call_lists/vote_menu_{congress}_{session}.xml` | `SenateVoteMenuParser` | none, because a menu has no extraction |
| `senateVote` | `{source url}/roll_call_votes/vote{congress}{session}/vote_{congress}_{session}_{roll, 5 digits}.xml` | `SenateVoteParser` | `senate-vote-v1` |

A vote file holds the detail and the members, so one job gives one extraction. `SenateVoteRenderer` renders it with the normalization version `senate-vote-render-v2`. The `Vote title` line holds the title of the vote, so it cannot collide with the `Title` fact of a bill. The first line is `Senate vote: {congress} {session} {roll}`. The `Updated` line holds the modify date in UTC. The `Started` line keeps the Eastern offset. A `Count` line and one `Member` line for each senator follow. A member line holds the `lis` ID, the name, the party, the state, and the cast.

The files give times with no zone. The parser reads them as US Eastern, with `TimeZoneInfo` offset rules. This is a guess. For a time that occurs twice or never at a clock change, the standard offset applies.

The parsers refuse unknown shapes and do not guess. A vote file needs a roll that matches its URL, a valid date and time, and at least one member. A member needs a `lis` ID, a last name, a party, a state, and a cast. The counts must equal the members by cast: `Yea`, `Nay`, `Present`, and `Not Voting`. Any other cast makes the file invalid. The Vice President is in neither the counts nor the member list, so a tie-break vote parses. A menu lists at most 999 votes, each once. A menu day of 1 or 2 January belongs to the year after `congress_year`, because a session can end then. The document of a vote gives a bill link only for a bill type. Other types, such as `PN`, need no number.

The modify date is the version of a vote. The menu has no modify date, so a job key cannot hold it. A vote key holds the day of the sweep when the vote day is 30 days or less before today. Each daily sweep then fetches these votes again. An older vote has the key suffix `final`, so the first sweep after the window ends fetches it one last time. `SenateVoteJobKeys` owns these keys.

Admission of a vote file finds the `vote_versions` row by source, chamber, roll call, and exact modify date. A found row is a duplicate and writes nothing. Otherwise the store checks the extraction against the job, then writes the version and the candidate in one transaction. It then queues the draft like the House members admission. The candidate waits for the detail of its bill. A vote with no bill queues at once. It admits no further job.

### Senate drafting

`VoteReader` dispatches on the chamber of the candidate. The Senate branch checks the parser and normalization versions of the one saved page, parses the capture, and compares the render with the saved text. It takes the vote day from the `Started` line, which keeps the Eastern offset. The facts are the lines that start with the prefixes of `VoteFactPrefixes.SenateDetail`: `Started`, `Question`, `Question text`, `Vote title`, `Result`, `Document`, and `Count`. A vote on a bill adds the bill facts as the House path does. A vote with no bill identity, such as a nomination, skips the bill detail.

The reader takes the `lis` IDs of the senators from `IMemberRosterStore.ReadSenatorLisIdsAsync` for the roster source. The call returns each senator and each `lis` ID in any snapshot of that source, so a senator who left still matches the old votes. A person with no Senate term in any snapshot is no senator and is skipped. A `lis` ID in the vote file that no snapshot maps blocks the draft with `evidenceUnavailable`. The prompt says Senate and names senate.gov and Congress.gov as the sources. The task is `vote-draft-v5` with the prompt `vote-draft-prompt-v3`.

## Storage and contracts

- The generated [votes schema](schema/votes.md) lists every table, column, key, check, and index.
- `vote_versions` holds one row for each vote version, keyed by its detail job. The row stores the chamber and the identity, the exact update time, and the nullable bill identity. It also names the extraction of the detail. The store reads it to find duplicates and to write the candidate.
- `vote_candidates` holds one row for each vote version with both pages captured. It stores the chamber, and it names the detail job, the members job, both extractions, and the configuration revision. Drafting reads these rows.
- The candidate ID is a SHA-256 hash of the source, the roll call, and the exact update time. `PostgresCollectionJobStore.HashVoteCandidate` builds it. It frames the text like the bill candidate ID, with the prefix `house-vote:` or `senate-vote:`.
- A Senate vote has one job. The version row and the candidate row both name that job and its extraction, in `detail_job_id` and `members_job_id`, and in `detail_extraction_id` and `members_extraction_id`. The Senate reader reads the capture once and parses both parts from it. No migration is needed.
- The store writes no page rows. The two extractions and the captures of the two jobs are the evidence of a candidate.
- `ix_vote_candidates_roll` serves the newer-candidate check of a roll call.
- `ix_vote_candidates_bill` on congress, bill type, and bill number serves the waiting-candidate query of the bill detail admission.
- `vote_analysis`, `vote_analysis_runs`, and `vote_analysis_budget` hold the queue, the runs, and the daily charge of the task `vote-draft`. They have the shape of the [action tables](actions.md#storage-and-contracts). A run has the reason column for a bypassed run.
- `vote_drafts` holds one record for each source and roll call. `vote_revisions` holds its revisions as JSON, each with the run of its analysis author. `vote_review_decisions` holds the review decisions of vote records. It mirrors the action decision table.
- A bill detail is found through `bill_gates` and `bill_pages`, which the bill stream owns. `BillDetailRows` owns this lookup for both the queue rule and the evidence read. Vote drafting adds no table and no column.

## Code map

| Concern | Files |
|---|---|
| Sessions and the sweep | `src/CivicLens.Application/Collection/Votes/VoteSession.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteRoll.cs` |
| URLs and keys | `src/CivicLens.Application/Collection/HouseVotes/HouseVoteListUrl.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteRollUrl.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteJobKeys.cs` |
| Rendering | `src/CivicLens.Application/Collection/HouseVotes/HouseVoteRenderer.cs`, `src/CivicLens.Application/Collection/RenderedText.cs` |
| Parsers | `src/CivicLens.Application/Collection/HouseVotes/HouseVoteListParser.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteParser.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteMembersParser.cs`, `src/CivicLens.Application/Collection/HouseVotes/HouseVoteWire.cs` |
| Resource kinds and parser list | `src/CivicLens.Application/Collection/ApiResource.cs`, `src/CivicLens.Application/Collection/ApiParser.cs` |
| Configuration and job binding | `src/CivicLens.Application/Collection/CollectionConfiguration.cs`, `src/CivicLens.Application/Collection/ConfiguredCollectionSource.cs`, `src/CivicLens.Application/Collection/Jobs/CollectionJobDefinition.cs` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Senate parsers, URLs, keys, rendering | `src/CivicLens.Application/Collection/SenateVotes/` |
| Admission port | `src/CivicLens.Application/Collection/Votes/IVoteAdmissionStore.cs` |
| Postgres admission and candidates | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Votes.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Scheduling.cs` |
| Table rows | `src/CivicLens.Infrastructure/Collection/Votes/` |
| Drafting step and checks | `src/CivicLens.Application/Votes/VoteDraftStep.cs`, `src/CivicLens.Application/Votes/VoteDraftContext.cs`, `src/CivicLens.Application/Votes/VoteDraftResolver.cs` |
| Bill detail lookup | `src/CivicLens.Infrastructure/Collection/BillStream/BillDetailRows.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.BillFollowOns.cs` |
| Evidence reader, snapshot, fingerprint | `src/CivicLens.Application/Votes/VoteReader.cs`, `src/CivicLens.Application/Votes/VoteSnapshot.cs`, `src/CivicLens.Application/Votes/VoteFingerprint.cs`, `src/CivicLens.Application/Documents/LineCitations.cs` |
| Prompt, task identity, settings, codes | `src/CivicLens.Application/Votes/VoteDraftPrompt.cs`, `src/CivicLens.Application/Votes/VoteDraftTask.cs`, `src/CivicLens.Application/Votes/VoteDraftSettings.cs`, `src/CivicLens.Application/Votes/VoteDraftErrorCodes.cs` |
| Queue lifecycle and store boundary | `src/CivicLens.Application/Votes/VoteDraftLifecycle.cs`, `src/CivicLens.Application/Votes/IVoteDraftStore.cs`, `src/CivicLens.Application/Votes/IVoteDraftModel.cs` |
| Vote kind | `src/CivicLens.Core/Votes/VoteKind.cs`, `src/CivicLens.Application/Votes/VoteKindTable.cs`, `src/CivicLens.Application/Votes/SenateVoteKindTable.cs`, `src/CivicLens.Application/Votes/VoteFactPrefixes.cs`, `src/CivicLens.Application/Votes/VoteFacts.cs` |
| Owner command | `src/CivicLens.Host/Votes/VoteCommand.cs`, `src/CivicLens.Application/Votes/GetVoteDraftStatus.cs`, `src/CivicLens.Application/Votes/RequeueBlockedVoteDrafts.cs`, `src/CivicLens.Application/Votes/VoteDraftView.cs`, `src/CivicLens.Application/Votes/VoteDraftListItem.cs` |
| Publication reads | `src/CivicLens.Application/Votes/UnpublishedApprovedVote.cs`, `src/CivicLens.Application/Votes/PublishableVote.cs`, `src/CivicLens.Infrastructure/Votes/VoteReviewQueries.cs`, `src/CivicLens.Application/Publication/VoteRecordLoader.cs` |
| Vote revision | `src/CivicLens.Core/Votes/VoteDraftRevision.cs`, `src/CivicLens.Core/Review/ReviewText.cs`, `src/CivicLens.Core/Review/ReviewTextRules.cs`, `src/CivicLens.Core/Review/ReviewTextSuggestion.cs`, `src/CivicLens.Core/Review/IHeadlineSummaryRevision.cs` |
| Review aggregate | `src/CivicLens.Core/Votes/VoteReview.cs` |
| Review commands and reads | `src/CivicLens.Application/Votes/SaveVoteDraft.cs`, `src/CivicLens.Application/Votes/DecideVoteReview.cs`, `src/CivicLens.Application/Votes/GetVoteReview.cs`, `src/CivicLens.Application/Votes/ListVoteReviews.cs`, `src/CivicLens.Application/Votes/GetVoteReviewCounts.cs`, `src/CivicLens.Application/Votes/IVoteReviewStore.cs`, `src/CivicLens.Application/Votes/BillVoteDraft.cs` |
| Review store | `src/CivicLens.Infrastructure/Votes/PostgresVoteReviewStore.cs`, `src/CivicLens.Infrastructure/Review/ConfiguredPersonNames.cs`, `src/CivicLens.Infrastructure/Votes/BillVoteDrafts.cs` |
| Review pages | `src/CivicLens.Host/Review/VoteList.razor`, `src/CivicLens.Host/Review/DraftListPage.cs`, `src/CivicLens.Host/Review/VoteEditor.razor`, `src/CivicLens.Host/Review/VoteLabels.cs`, `src/CivicLens.Host/Review/VoteRoutes.cs`, `src/CivicLens.Host/Review/SourcePages.cs` |
| Postgres drafting store and append | `src/CivicLens.Infrastructure/Votes/PostgresVoteDraftStore.cs`, `src/CivicLens.Infrastructure/Votes/VoteDraftRows.cs`, `src/CivicLens.Infrastructure/Votes/VoteDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Votes/VoteDraftQueueRows.cs` |
| Drafting tables and model adapter | `src/CivicLens.Infrastructure/Votes/Persistence/`, `src/CivicLens.Infrastructure/Votes/AnthropicVoteDraftModel.cs` |
| Worker composition | `src/CivicLens.Host/Votes/VoteDraftComposition.cs`, `src/CivicLens.Host/Votes/VoteDraftConfiguration.cs` |

## Limits and planned work

- The lists of vote records read `vote_drafts` without the partial status indexes of `action_drafts`. The table held 676 roll calls on 2026-10-17 and grows by about 700 a year. `brief_drafts` also has no such indexes. Add the indexes when the table passes about 10,000 rows.
- A later bill detail does not redraft a vote that was already drafted. The next change of the vote itself drafts again.
- A voted bill from a congress before `minimumCongress` of the bill source would wait forever. No such vote exists today, because the vote sweep starts in 2025.
- Measured on 2026-10-16 with 147 drafts: no headline contradicts the result in 74 checked drafts. But 23 of the 74 summaries say the facts do not describe the measure. 28 explain House procedure from model knowledge.
- Measured on 2026-10-16 over 676 roll calls: 145 are procedural, 96 are amendments, and 324 pass a bill.
- Planned in [R-43](../roadmap.md#next): the talk and action profile groups the vote kinds.
- Measured on 2026-10-17 over 676 roll calls in the development data: 266 passage, 157 suspension, 90 amendment, 70 motion to recommit, and 61 previous question. Also 2 veto override and 30 other.
- No rule and no conference report occur in that data yet. The 79 roll calls on a House resolution still wait for their bill title, so they count as passage until the title arrives.
- The other kind holds motions to table, discharge, adjourn, refer, instruct, and reconsider. It also holds the consideration of a resolution and the division votes. It also holds the Speaker election and the quorum calls.
- The tracked members of a candidate come from its configuration revision. A member added to tracking later gets no past votes. This is pending as [D-11](../roadmap.md#pending-decisions).
- Planned: measure the noise of drafting every roll call before adding a gate. The 119th Congress had 362 roll calls in session 1 and 314 so far in session 2 on 2026-10-16.
- A roll call with no tracked member has no record. The record needs at least one member vote.
- When a newer version of a roll call has no tracked member, the earlier revision stays current with its earlier votes. Actions behave the same way with `noTrackedOfficial`.
- The `house-vote` API is a beta. Its shape can change. A changed shape makes a parser raise invalid data, which blocks the job.
- A roll call with an unknown legislation type has no bill link, so it does not link to an action record.
- A members response that is newer than the recorded detail blocks its job until the next sweep admits the newer detail.
- Rolling back the `VoteKindChangedReason` migration fails while any vote run has the `kindChanged` reason.
- The session of today uses the UTC date. The new session therefore starts a few hours early on 1 January in US time zones. Its list is empty then.
- Senate drafting is built but the shipped `senate-votes` source is disabled. PR 4 of [R-42](../roadmap.md#next) adds the operations steps and the enable flag. See [actions](actions.md#limits-and-planned-work).
- The Senate reader skips a tracked person who is not in the roster only if the vote file has no `lis` ID for that person. A real senator missing from the roster blocks the draft.
- The Senate time zone is a guess. The Senate files do not name it. No en bloc or treaty vote file has been seen. The parser has no rule for them. A file with a document type that is no bill type parses as a vote with no bill. A file that breaks a parser rule is invalid data, which blocks the job.
- The parser ignores the `tie_breaker` element. A tie-break cast of the Vice President is not in the member list, so no record shows it.
- A Senate vote file with a `lis` ID that no roster snapshot maps blocks the draft with `evidenceUnavailable`. A senator who left before the roster collection began, such as one who left in January 2025, has no snapshot row. Early 119th Congress votes with such senators stay blocked, because a tracked official could be missing from a published vote.
- The casts `Guilty`, `Not Guilty` (impeachment trials), and `Present, Giving Live Pair` fail the file. Only `Yea`, `Nay`, `Present`, and `Not Voting` are counted.
- Roll call lookup keys by chamber, Congress, session, and number. It ignores the source of a vote draft, because each chamber has one source and no code owns its ID. A second source for one chamber needs a source filter in `FindRecordIdByRollAsync`. The lookup text `Roll N` means a House roll, and `Senate roll N` means a Senate roll.
