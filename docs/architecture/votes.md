# Votes

## Purpose

The votes feature collects House roll-call votes from the Congress.gov `house-vote` API and Senate roll-call votes from the Voteview CSV files. It fetches each House roll call as two durable jobs, the detail and the member votes. A chain of three Voteview jobs gives many Senate roll calls. Each roll call version with all its pages captured becomes a vote candidate. The `worker` drafts one vote record for each source and roll call from these candidates. Code approves each worker revision after it checks the citations. The facts come from the House Clerk XML, Congress.gov, and the Voteview vote files. Code approval of a Senate vote rests on Voteview data. A person can still edit, request changes on, and withdraw a record on the vote review pages. An approved vote record publishes as the vote kind of a release; see [publication](publication.md#vote-record).

## Decisions

- One vote model serves both chambers. The enum `Chamber` (`House`, `Senate`) is a field of the vote revision, the published vote, and the official vote row. The tables of versions, candidates, and drafts have a `chamber` column. Names that hold no chamber rule are neutral: `VoteSession`, `IVoteAdmissionStore`, `PostgresCollectionJobStore.Votes.cs`, and the tables `vote_versions` and `vote_candidates`. Parsers, URLs, the renderer, and the kind table are per chamber and carry the chamber in their names. The reader has one branch for the House and one for each of the two Senate sources, and it shares the rest.
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
- The Speaker election has totals by candidate, not by party. The parser reads them as candidate totals, and the renderer writes one line for each. The extraction versions stay at v1. No input that gave a v1 extraction renders other text. A party item that also names a candidate is now invalid. No captured vote has that shape.
- Preparation renders each detail and each members response into extraction text, as the [bills](bills.md#extraction) connector does for its pages. The candidate binds both extractions. Drafting cites the exact lines of these extractions.
- A vote record cites the title and the policy area of its bill. The bill detail comes from the [bill stream](bills.md#admission), which saves a detail extraction for every listed bill. The vote feature adds no job type and no job admission across streams.
- A candidate whose roll call has a bill identity waits for a saved bill detail of that bill. It gets no queue row until then. The bill detail admission queues the waiting candidates in its own transaction. A candidate without a bill identity is queued at once.
- Drafting reads the newest saved bill detail of the bill, whichever source saved it. The match is the congress of the vote, the bill type, and the bill number.
- Drafting also reads the saved subjects pages of the bill as optional facts for the issue choice. They come from the newest bill version whose subjects chain is closed (`BillDetailRows.SubjectPagesOfNewestVersion`). A bill with no such pages drafts as before. A subjects page blocks the draft like the bill detail does when its extraction version or its saved text is wrong. The record stores no subject.
- The candidate issues of a vote come from the policy area of its bill on the vote day. `BillIssueChoice` owns the candidates, the prompt section, the schema property, and the answer check, as for [actions](actions.md#prompt-and-output). The model chooses the tracked issues from the candidates, and may choose none.
- Drafting copies [action drafting](actions.md#step): its own queue, budget and run tables, the shared analysis worker, and the shared revision store `ReviewRecords`.
- One vote record exists for each source and roll call. A re-collected vote with a newer update time appends a revision to the same record.
- Every candidate is drafted. No gate exists. The only candidate that settles without a revision is a roll call with no tracked member.
- Code produces every fact with its citation. The model writes only the headline and a plain summary of the question.
- The model sees only the fact lines. It sees no member and no vote, and its text may name no configured person. Code lists the tracked members and their votes beside the text. So copied text can never state a vote that a later correction changed.
- When only the votes of tracked members change, drafting makes no model call. It copies the text of the current revision, as action drafting does for sponsorships. A roll call whose newest draft used an older prompt is the exception: it drafts again.
- A person edits only the headline and the summary of a vote record. The revision holds a `TextOrigin` and a suggestion, with the same append rules as an [action](actions.md#revision-and-append). Text that a person wrote survives later model runs. The model text then becomes a suggestion.
- Votes get review as actions do: a review store, a list page, an editor page, a decide handler, review counts, and review history.
- Review writes take the `civic-lens-vote-review` lock, the key that the drafting append already takes. Publication also takes this key, after the action key and before the brief key.
- The owner command `votes draft` has `status`, `list`, `requeue`, and `redraft`. It copies the `actions draft` command and its shared queue commands. `status` also shows the number of waiting votes.
- A Blocked vote entry can return to Pending. The lifecycle allows the move, as the [action](actions.md#decisions) lifecycle does. A Succeeded `drafted` or `unchanged` entry can also return to Pending. `votes draft requeue --blocked` moves only Blocked entries with an exact code. `votes draft redraft` moves only the Succeeded entries of a roll call with an older prompt.
- The vote kind (D-10, accepted on 2026-10-17) comes from a code table, not from the model. `VoteKindTable` maps the `Question` line of a House vote to a kind. `SenateVoteKindTable` does the same for a Senate vote. The kinds are rule, previous question, motion to recommit, amendment, suspension, passage, conference report, veto override, nomination, cloture, motion to proceed, and other. An unknown question gives other. House questions give none of the last three.
- `SenateVoteKindTable` matches the start of the `Question` line, with the question strings that Voteview lists. Any cloture question is cloture, and `On Overriding the Veto` is veto override. `On the Nomination` is nomination, and `On the Motion to Proceed` is motion to proceed. Amendment, conference report, and passage reuse the House kinds. A resolution is passage as in the House. `On the Resolution of Ratification` is other. An unknown question is other. A cloture vote on a nomination is cloture, because the vote decides to end debate. The Senate table does not read the document type, because the question already names every nomination vote in the data.
- A motion to commit and a motion to commit with instructions are a motion to recommit. A motion to concur in a Senate amendment is passage. A consideration of a resolution stays other.
- A resolution agreed to by the question `On Agreeing to the Resolution` is a rule when its legislation is a House resolution (`HRES`). Its bill title must also hold `consideration of`, in any case and at any position. Any other resolution so agreed is passage, because agreeing to it is its final vote. This choice is deliberate: the kind list has no resolution kind.
- Code stores the kind in the revision at draft time, with the cited `Question` line, as `PolicyAreaId` is stored for actions. Publication never derives it again.
- Code also stores the vote day, `VotedOn`, with the cited `Started` line, as it stores the kind. The day is the date of the start time in the offset that the API gives. A vote without a start time blocks with `evidenceUnavailable`, because its day would have no citation. The vote recovers when the API sends a later roll call version, which is a new candidate.
- The citation covers the `Question` line only. The rule test also reads the `Legislation` and `Title` facts. The same revision cites them.
- D-15 (accepted on 2026-10-19): code approves the worker revisions of a vote, as it does for [actions](actions.md#decisions). Public pages label the headline and the summary as an AI summary of the official record when the origin of the published text is Model. They add no label when a person wrote the text.
- A worker revision of a newer candidate gets a new code approval if three rules hold. No concern is open. The revision holds no text that a person wrote. The approval checks pass. A person edit stays with a person even after a person approved it. The release keeps the earlier approved revision until a release publishes the new one. A copy revision for changed member votes follows the same rule.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- The event date of a vote is its vote day. The draft row copies it, the candidate ID, the tracked issue IDs, and the member person IDs from the current revision at each append. Enforced by `VoteDraftRows.Revised`. Test: `PostgresHouseVoteTests.EachWorkerRevisionReplacesTheListedColumnsWithThoseOfTheCurrentRevision`.
- A votes job that skips roll calls for a count mismatch still succeeds, with the code `voteviewCountMismatch`. A warning names the Congress and the number skipped. Enforced by `JobPreparation`. Test: `JobPreparationTests.ARollCallWhoseCountsDisagreeGivesTheMismatchCodeAndALogWithTheCountWhileTheOtherRollCallsStillAdmit`.
- A Voteview votes job whose parent job is missing or not Succeeded is Blocked with `voteviewJobMissing`. Enforced by `JobPreparation`. No test.
- Each daily Voteview sweep makes new job keys for the whole chain, so a failed job blocks only its own day. Enforced by `VoteviewJobKeys`. Tests: `PostgresVoteviewVoteTests.EachDailySweepWalksTheWholeChainAgainSoTheCurrentCongressIsFetchedOnTheSecondDay`, `PostgresVoteviewVoteTests.AFailedMiddleJobStopsThatDaysChainButTheNextDayMakesNewKeysAndReachesTheCurrentCongress`.
- A Voteview roll call gets a new version only when its rendered text differs from the newest version. Enforced by `PostgresCollectionJobStore.AdmitVoteviewVotesAsync`, which compares the hash under the job lock. Test: `PostgresVoteviewVoteTests.ANextDayWithTheSameTextWritesNoVersionButChangedTextWritesOneAndKeepsTheOldVersion`.
- A roll call job of Voteview holds many versions, one for each session and roll number. Enforced by the composite key of `vote_versions`. Test: `PostgresVoteviewVoteTests.OneRollCallsJobHoldsManyVersionsAndOnlyTheRollCallWithNoBillIsQueuedBecauseABillRollCallWaitsForItsBillDetail`.
- A Senate roll call hashes to the record key `senate-votes`, and a House source keeps its own source ID as the key. The key `senate-votes` is the hash key of the public Senate record IDs. No live source may enable that ID. Enforced by `VoteSources.RecordKeyOf`, which `PostgresVoteDraftStore.ReadEvidenceAsync` passes to `HashVoteRecord`. Tests: `VoteSourcesTests.SenateVotesKeepTheRecordKeyOfTheRetiredSenateGovSourceSoPublicRecordIdsDoNotChange` and `VoteSourcesTests.AHouseSourceKeepsItsOwnIdAsRecordKeySoHouseRecordIdsDoNotChange`.
- The Voteview reader matches senators by bioguide ID and reads the saved text only after it renders the same text from the three captures. Enforced by `VoteReader.ReadVoteviewAsync`. Tests: `VoteviewVoteReaderTests.ATrackedSenatorMatchesByBioguideIdAndHisCastCitesAMemberLineWhoseNameHoldsCommas`, `VoteviewVoteReaderTests.ASavedTextThatDiffersFromTheRenderOfItsCapturesBlocksTheDraft`, and `VoteviewVoteReaderTests.AnotherExtractionVersionBlocksBeforeAnyCaptureIsRead`.
- A House revision has the Congress.gov source, and a Senate revision has the Voteview source. Enforced by `VoteDraftRevision.ValidateShape`. Test: `VoteDraftRevisionTests.AVoteSourceThatCannotHaveCarriedTheChamberIsRefusedSoAHouseVoteNeverNamesVoteview`.
- A vote revision names the `VoteSource` of its evidence. A new source for the same facts redrafts the record. An undefined source is refused. `VoteReader` sets it. `VoteDraftContext.ChangeSinceCurrent` and `VoteDraftRevision.ValidateShape` enforce the other two rules. The Senate row with source 9 of the pairing test isolates the undefined rule, because the pairing rule alone accepts it. Tests: `VoteviewVoteReaderTests.ATrackedSenatorMatchesByBioguideIdAndHisCastCitesAMemberLineWhoseNameHoldsCommas` for the value, and `PublishVoteTests.TheRecordNamesTheDataSourceOfItsRevisionSoAReaderKnowsWhoseDataTheCastsRestOn` for the publish. No test of the redraft.
- The President row never counts as a senator, and a member with no bioguide ID is refused. Enforced by `VoteviewParser` and `VoteviewRoll.Join`. Tests: `VoteviewRollTests.ThePresidentRowIsLeftOutBecauseHeIsNotASenatorAndWouldBreakTheYeaCount`, `VoteviewRollTests.ThePresidentOfAnotherTermIsLeftOutByChamberNotByAFixedIcpsrSoANewPresidentNeedsNoCodeChange`, `VoteviewRollTests.ASenateMemberWithNoBioguideIdIsRefusedBecauseTheRecordCouldNotName`.
- A roll call whose member counts differ from its roll call file is skipped, and the job succeeds. Enforced by `VoteviewRoll.Join`. Test: `VoteviewRollTests.ARollCallWhoseMemberCountsDisagreeWithTheFileCountsIsSkippedAndTheOtherRollCallsStillRender`.
- The votes job stops when its three bodies equal those of the newest candidate. Enforced by `PostgresCollectionJobStore.HasVoteviewCandidateOfBodiesAsync`. Tests: `PostgresVoteviewVoteTests.OnlyTheSameThreeBodiesAsTheNewestCandidateCountAsUnchanged`, `JobPreparationTests.AVotesJobWhoseThreeBodiesEqualTheNewestCandidateLoadsNoBodyAndSavesNothing`.
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
- One vote record exists for each chamber, source, and roll call. Enforced by `ux_vote_drafts_roll` and `VoteDraftRows.AppendAnalysisRevisionAsync`. Tests: `PostgresHouseVoteTests.TheDatabaseAllowsOneDraftForEachRollCallAndRefusesABadRecordId`, `PostgresHouseVoteTests.ACollectedAgainVoteAppendsRevisionTwoToTheSameDraftBecauseTheRecordIsOnePerRollCall`, `PostgresVoteviewVoteTests.TheSameRollNumberCanHaveADraftInBothChambersButNotTwoInOne`, and `PostgresRollLookupTests.ARollCallFindsOnlyTheVoteDraftOfItsChamberCongressSessionAndNumber`.
- A newer candidate of another chamber never supersedes a vote. Enforced by the chamber filter of `VoteDraftRows.HasNewerCandidateAsync`. Test: `PostgresHouseVoteTests.ANewerSenateCandidateOfTheSameNumbersDoesNotSupersedeTheHouseVote`.
- A record ID and a candidate ID differ between chambers for the same source, Congress, session, and roll. House IDs keep their old values. Enforced by `PostgresCollectionJobStore.HashVoteRecord` and `HashVoteCandidate`. Test for the record ID: `PostgresHouseVoteTests.HouseIdsKeepTheirOldValuesAndASenateVoteOfTheSameNumbersGetsOtherIds`. Test for the Senate candidate ID: `PostgresVoteviewVoteTests.SenateCandidateIdsAreTheHashOfTheirFramedTextSoTheyNeverChange`.
- A candidate with a bill identity gets no queue row until a bill detail of that bill is saved. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.AVoteWhoseBillHasNoSavedDetailGetsNoQueueRowBecauseTheDraftNeedsTheBillTitle`.
- A Senate candidate is queued like a House candidate. It waits for the bill detail of its bill, and a vote with no bill, such as a nomination, queues at once. Enforced by `PostgresCollectionJobStore.QueueCandidateAsync` and `VoteDraftQueueRows.WaitingForBill`. Test: `PostgresVoteviewVoteTests.OneRollCallsJobHoldsManyVersionsAndOnlyTheRollCallWithNoBillIsQueuedBecauseABillRollCallWaitsForItsBillDetail`.
- The prompt names the chamber of the vote and its source. The House bytes did not change. Enforced by `VoteDraftPrompt`. Tests: `VoteDraftPromptGoldenTests.ThePromptAndSchemaMatchTheirPinnedVersions` and `VoteDraftPromptGoldenTests.TheSenatePromptNamesTheSenateAndItsSourcesWhileTheHouseGoldenAbovePinsTheHouseBytes`.
- The bill detail admission queues the waiting candidates of that bill in its own transaction, once. Enforced by `PostgresCollectionJobStore.AdmitDetailAsync` (bill) and `VoteDraftQueueRows`. Tests: `PostgresHouseVoteTests.TheBillDetailQueuesTheWaitingVoteInTheSameTransactionThatRecordsTheBill` and `PostgresHouseVoteTests.ALaterVersionOfTheBillAddsNoSecondQueueRowBecauseALaterDetailDoesNotRedraftAVote`.
- A candidate without a bill identity is queued at once. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync`. Test: `PostgresHouseVoteTests.AVoteWithoutABillIdentityIsQueuedAtOnceBecauseItHasNoTitleToWaitFor`.
- A vote and its bill cannot race, because both writes take the job coordination lock. Enforced by `PostgresCollectionJobStore.TransactionAsync`. Test: `PostgresHouseVoteTests.TheVoteWriteAndTheBillDetailWriteBothWaitForTheJobLockSoAVoteAndItsBillCannotRace`.
- Evidence holds the newest saved bill detail of the voted bill. Enforced by `BillDetailRows` and `PostgresVoteDraftStore.ReadEvidenceAsync`. Test: `PostgresHouseVoteTests.EvidenceHoldsTheNewestSavedBillDetailOfTheVotedBillAndNoneForAVoteWithoutABill`.
- Evidence holds the subject pages of the newest bill version whose subjects chain is closed. Enforced by `BillDetailRows.SubjectPagesOfNewestVersion`. Test: `PostgresHouseVoteTests.EvidenceHoldsTheSubjectPagesOfTheNewestBillVersionWhoseSubjectsChainIsClosedAndNoneWhenNoVersionHasOne`.
- A missing subjects page never blocks a draft. A subjects page with another version or other text blocks it. Enforced by `VoteReader`. Tests: `VoteDraftStepTests.ABillWithNoSavedSubjectsPageStillDraftsWithTheOldPromptBecauseSubjectsCoverOnlySomeBills` and `VoteDraftStepTests.ASubjectsPageThatNoLongerMatchesItsCaptureBlocksBeforeAnyCallBecauseAFactMustBeTrusted`.
- A prompt over the byte cap is built again without the subjects, as for [actions](actions.md#decisions). `BillIssueChoice.BuildDroppingSubjectsWhenOverTheCap` owns this rule for both. The revision still stores the hash of the dropped subjects, so the same vote does not draft again. Test: `VoteDraftStepTests.AtTheByteCapTheSubjectsAreDroppedAndTheVoteStillDraftsFromTheFactLines`.
- The subject wording stays out of the headline and the summary. Only the shared prompt instruction enforces this. No code checks the text. No test.
- Each candidate gets one queue row in the transaction that writes it, or in the transaction of its bill detail. Enforced by `PostgresCollectionJobStore.AdmitMembersAsync` and `VoteDraftQueueRows`. Tests: `PostgresHouseVoteTests.TheCandidateWriteQueuesOnePendingDraftRowInTheSameTransactionAndReplayKeepsIt`.
- Code writes every fact and cites its exact line in the saved extraction. Enforced by `VoteReader`. Test: `VoteDraftStepTests.CitationsPointAtTheExactRenderedLinesSoAReaderCanCheckEveryFactAgainstTheSavedExtraction`.
- The bill title and the policy area enter the record as cited lines of the saved bill detail. Enforced by `VoteReader`. Test: `VoteDraftStepTests.TheBillTitleAndPolicyAreaAreCitedFromTheSavedBillDetailSoTheModelKnowsWhatTheMeasureIs`.
- A bill detail blocks before any call in three cases. It is missing, it has another extraction version, or its saved text differs from the render of its capture. Enforced by `VoteReader`. Tests: `VoteDraftStepTests.ABillDetailOfAnOlderParserBlocksBeforeAnyCallBecauseItsLinesMayHaveAnotherMeaning`, `VoteDraftStepTests.ASavedBillDetailThatDiffersFromTheRenderOfItsCaptureBlocksSoTheCitedLinesAreNeverTrustedBlindly`, and `VoteDraftStepTests.AVoteOnABillWithoutASavedBillDetailBlocksBecauseTheDraftNeedsTheTitle`.
- The candidate issues come from the policy area on the vote day, and the revision names the issue revision. Enforced by `VoteDraftStep` and `BillIssueChoice.For`. Tests: `VoteDraftStepTests.TheIssueCandidatesComeFromThePolicyAreaOfTheBillAndTheRevisionNamesTheIssueRevision` and `VoteDraftStepTests.TheTrackedIssuesAreCheckedOnTheVoteDaySoAnIssueThatEndedBeforeTheVoteIsNotTracked`.
- The tracked issues of a vote are the issues that the model chose from the candidates. The model may choose none, and code never falls back to the candidate set. A choice that lists an ID outside the candidates, or an ID twice, is rejected with its reason and retried once. The model is asked even when the policy area has one candidate. Enforced by `BillIssueChoice.Check` in `VoteDraftResolver`, and by `VoteDraftPrompt`. Tests: `VoteDraftStepTests.AModelThatPicksOneOfTwoCandidatesStoresOnlyThatIssueAndBothCandidates`, `VoteDraftStepTests.AModelThatPicksNoIssueLeavesTheVoteWithoutOneAndNeverFallsBackToThePolicyAreaSet`, `VoteDraftStepTests.AChoiceOutsideTheCandidatesOrRepeatedIsRejectedWithItsReasonAndRetriedOnce`, and `VoteDraftStepTests.ASingleCandidateIsStillOfferedToTheModelBecauseThePolicyAreaAloneDoesNotMakeAnIssueFit`.
- A change of the title, the candidate issues, the issue revision, or the subjects that the model saw calls the model. The revision stores a hash of those subjects (`SubjectsHash`). It is empty when the model saw none, and the JSON then leaves it out, so older revisions keep their exact JSON. A current revision with no stored candidate set calls it too. The comparison uses the candidates and not the choice. Enforced by `VoteDraftContext.ChangeSinceCurrent`. Tests: `VoteDraftStepTests.AChangedBillTitleCallsTheModelBecauseTheTitleIsAFact`, `VoteDraftStepTests.AnotherIssueRevisionCallsTheModelEvenWhenEveryFactIsTheSameBecauseTheTrackedIssuesCameFromOtherRules`, `VoteDraftStepTests.AVoteThatChangedItsCandidateSetDraftsAgainEvenWhenTheChosenIssuesStayTheSame`, `VoteDraftStepTests.SubjectsThatArriveAfterTheFirstDraftOrChangeDraftAgainAndTheSameSubjectsDoNot`, and `VoteDraftStepTests.ARevisionThatLacksTheCandidateSetDraftsAgainBecauseItPredatesTheModelChoice`.
- Only tracked members enter the record. Enforced by `VoteReader`. Test: `VoteDraftStepTests.MembersWhomNobodyTracksNeverEnterTheRecordBecauseOnlyTrackedVotesAreCited`.
- A member is tracked when the source covers the member on the vote day. Enforced by `VoteReader`, which uses the start time of the vote. Test: `VoteDraftStepTests.CoverageIsCheckedOnTheVoteDayBecauseALaterCorrectionMustNotChangeWhoWasTrackedForTheVote`.
- A revision stores the vote day with a bounded citation of the `Started` line, and a vote without a start time blocks. Enforced by `VoteReader` and `VoteDraftRevision.ValidateShape`. Tests: `VoteDraftStepTests.TheRevisionStoresTheVoteDayAndCitesTheStartLineBecausePublishedDatesMustQuoteTheirSource`, `VoteDraftStepTests.AVoteWithoutAStartLineBlocksBecauseItsDayHasNoCitationToPublish`, and `VoteDraftRevisionTests.TheVoteDayMustCiteItsStartLineSoThePublishedDateIsNeverAnUnbackedClaim`.
- The prompt holds only the agreed fact lines. Enforced by `VoteDraftPrompt`. Test: `VoteDraftStepTests.ThePromptHoldsOnlyTheAgreedFactLinesAndNoMemberBecauseCodeOwnsTheMembersAndTheirVotes`.
- The text names no configured person. Enforced by `VoteDraftResolver`. Test: `VoteDraftStepTests.TextThatNamesAnyConfiguredPersonIsRejectedSoNoCopiedTextCanStateAStaleVote`.
- A change of member votes alone makes no model call and keeps the text, unless the roll call was drafted under an older prompt. Then the step drafts again. Enforced by `VoteDraftContext.ChangeSinceCurrent` and `VoteDraftStep`. Tests: `VoteDraftStepTests.ACorrectedMemberVoteAppendsAtOnceWithoutAModelCallAndKeepsTheTextBecauseCodeOwnsTheVotes` and `VoteDraftStepTests.ACorrectedMemberVoteOnAVoteDraftedUnderAnOlderPromptDraftsAgainInsteadOfCopyingTheOldText`.
- A change of any fact calls the model. A change of the tracked member set alone copies the text, because the text names no member. Enforced by `VoteDraftContext.ChangeSinceCurrent`. Tests: `VoteDraftStepTests.AMemberVoteChangeTogetherWithAChangedResultStillCallsTheModel` and `VoteDraftStepTests.AChangedTrackedMemberSetCopiesTheTextWithoutACallBecauseTheTextNamesNoMember`.
- A vote kind is stored in the revision with a bounded citation of the `Question` line. Only the kind other may have no citation. Enforced by `VoteKindTable` and `VoteDraftRevision.ValidateShape`. Tests: `VoteKindTableTests.EachQuestionLineMapsToTheKindThatNamesItsStageSoTheReviewerCanGroupVotesByWhatTheyDecided`, `VoteDraftStepTests.TheRevisionStoresTheKindOfTheQuestionAndCitesTheQuestionLineSoPublishNeverDerivesItAgain`, and `VoteDraftRevisionTests.AKindOtherThanOtherMustCiteItsQuestionSoTheReviewerCanCheckTheKindAgainstTheSavedLine`.
- A resolution is a rule only when it is a House resolution with a bill title that holds `consideration of`. Enforced by `VoteKindTable`. Tests: `VoteKindTableTests.AHouseResolutionThatProvidesForConsiderationIsARuleBecauseItOnlySetsTheTermsOfDebate` and `VoteKindTableTests.AResolutionThatIsNotARuleIsPassageBecauseAgreeingToItIsItsFinalVote`.
- Public vote text holds no internal word and describes no process of Civic Lens. Enforced only by the instruction in `VoteDraftPrompt`. No code checks the text. The golden test pins the instruction, not the output. Test: `VoteDraftPromptGoldenTests.ThePromptAndSchemaMatchTheirPinnedVersions`.
- Vote text states no count, total, or party split. Enforced only by the instruction in `VoteDraftPrompt`. No code checks the text. The golden test pins the instruction, not the output. Test: `VoteDraftPromptGoldenTests.ThePromptAndSchemaMatchTheirPinnedVersions`.
- A change of the kind alone copies the text and makes no model call, unless the roll call was drafted under an older prompt. Then the step drafts again. A change of a fact still redrafts. The stored kind updates at the next draft of that roll call. Nothing requeues stored votes after a table change. Enforced by `VoteDraftContext.ChangeSinceCurrent` and `VoteDraftStep`. Tests: `VoteDraftStepTests.AChangedKindAloneCopiesTheTextWithoutAModelCallBecauseTheKindComesFromCodeAndNotFromTheText` and `VoteDraftStepTests.AKindChangeOnAVoteDraftedUnderAnOlderPromptDraftsAgainInsteadOfCopyingTheOldText`.
- The waiting count and the queue rule share one definition of a waiting vote. A vote without a bill identity never waits. Enforced by `VoteDraftQueueRows.WaitingForBill`. Test: `PostgresHouseVoteTests.TheWaitingCountHoldsOnlyVotesOnABillWithNoQueueRowAndDropsWhenTheBillDetailQueuesThem`.
- An owner requeue of Blocked votes moves only Blocked entries with exactly the given code. Enforced by `PostgresAnalysisQueue.RequeueAsync` and `VoteDraftLifecycle`. Tests: `PostgresHouseVoteTests.RequeueBlockedMovesOnlyBlockedVotesWithTheGivenCodeAndResetsTheirAttempts` and `VoteDraftLifecycleTests.ABlockedVoteDraftMovesBackToPendingBecauseTheOwnerCommandRequeuesItAfterFixingTheCause`.
- A redraft moves only a Succeeded `drafted` or `unchanged` entry of a vote with a bill. The roll call must have its newest model-drafted run under an older prompt version. The command and the step skip a roll call whose current revision holds person text, an open concern, or a withdrawn approval. Enforced by `PostgresVoteDraftStore.StaleCandidateIdsAsync`. The draft step then calls the model, because `VoteDraftContext.ChangeSinceCurrent` counts `VoteDraftEvidence.DraftedUnderOlderPrompt`, which `PostgresVoteDraftStore.ReadEvidenceAsync` sets with the requeue query. A vote under the current prompt settles `unchanged` with no call. Enforced by `PostgresVoteDraftStore.RequeueOlderPromptAsync`, `VoteDraftContext` and `VoteDraftLifecycle`. Tests: `VoteDraftStepTests.AVoteDraftedUnderAnOlderPromptDraftsAgainOnceAndTheNextRunSpendsNothing`, `VoteDraftStepTests.AVoteDraftedUnderTheCurrentPromptSettlesUnchangedWithNoModelCall`, `PostgresHouseVoteTests.TheEvidenceNamesAnOlderPromptOnlyWhenTheNewestDraftRunUsedOneSoARerunSpendsNothingAfterTheRedraft`, `PostgresHouseVoteTests.AVoteThatAWorkerSettledUnderTheCurrentPromptHasNoOlderPromptSoTheRedraftSettlesItForGood`, `PostgresHouseVoteTests.ARedraftSkipsAVoteWhoseCurrentRevisionHoldsPersonTextAnOpenConcernOrAWithdrawnApproval`, `PostgresHouseVoteTests.TheEvidenceOfAVoteWithANewerCandidateNamesNoOlderPromptBecauseTheNewerCandidateWillDraftIt`, `PostgresHouseVoteTests.RedraftMovesASettledEntryWhoseNewestDraftRunUsedAnOlderPromptAndLeavesASupersededEntryAndResetsAttempts`, `PostgresHouseVoteTests.RedraftMovesNothingWhenTheNewestDraftRunOfTheRollCallUsedTheCurrentPrompt`, `PostgresHouseVoteTests.RedraftIgnoresANewerBypassedRunBecauseOnlyTheNewestDraftedRunNamesThePromptTheDraftCameFrom`, `PostgresHouseVoteTests.RedraftLeavesAnEntryThatIsNotADraftToReplaceEvenWhenItsRunUsedAnOlderPrompt`, `PostgresHouseVoteTests.RedraftLeavesARollCallWithoutAModelDraftedRunBecauseThereIsNoOldPromptToReplace`, `PostgresHouseVoteTests.RedraftLeavesAVoteWithoutABillBecauseNoIssueNarrowingCanChangeIt`, `PostgresHouseVoteTests.RedraftTreatsEachRollCallOfOneBillAlone`, `PostgresHouseVoteTests.RedraftTreatsAVoteThatDiffersInAnyRollCallKeyTermAsAnotherRollCall`, `PostgresHouseVoteTests.RedraftMovesEveryDraftedEntryOfAStaleRollCallAndCountsEntries`, and `VoteDraftLifecycleTests.OnlyASucceededVoteDraftWithADraftToReplaceMayReturnToPendingForARedraft`.
- Only the owner runs `votes draft` commands. Enforced by `ReviewAuthorization.RequireOwner` in each use case. Test: `VoteCommandTests.TheAiDrafterCannotActAsTheOwner`, which covers `redraft` too.
- A re-collected vote with the same facts and votes writes nothing, unless the roll call was drafted under an older prompt. Then the step drafts again, once. Enforced by `VoteDraftContext.ChangeSinceCurrent`. Test: `VoteDraftStepTests.ARecollectedVoteWithTheSameFactsAndVotesSettlesWithoutACallEvenWhenItsUpdateDateMoved`.
- A roll call with no tracked member writes no revision and makes no call. Enforced by `VoteDraftStep`. Test: `VoteDraftStepTests.ARollCallWithNoTrackedMemberSettlesSucceededWithoutACallOrARevision`.
- The saved extraction text must equal the new render of its capture. Enforced by `VoteReader`. Test: `VoteDraftStepTests.ARenderedTextThatDiffersFromTheSavedExtractionBlocksBeforeAnyCall`.
- A saved extraction of another extraction version blocks before any capture is parsed. Enforced by `VoteReader`. Test: `VoteDraftStepTests.AChangedExtractionVersionBlocksBeforeAnyCaptureIsParsedSoAnUpgradeIsNeverReportedAsBrokenData`.
- A candidate with a newer candidate of the same roll call never becomes a revision. Enforced by `VoteDraftStep` and `VoteDraftRows.AppendAnalysisRevisionAsync`. Tests: `VoteDraftStepTests.ACandidateWithANewerCandidateOfTheSameRollCallIsSupersededWithoutACall` and `PostgresHouseVoteTests.ANewerCandidateThatArrivesAfterTheClaimSupersedesTheAppendSoTheOlderDraftNeverBecomesARevision`.
- The vote page links the action draft of its bill only when the vote names a bill and that bill has a draft. Enforced by `PostgresVoteReviewStore.GetAsync`. Tests: `VoteReviewWorkspaceTests.TheVotePageLinksTheActionDraftOfItsBillAndTheRollCallOnTheClerkSite` and `VoteReviewWorkspaceTests.TheVotePageHasNoActionLinkWhenTheVoteNamesNoBillOrTheBillHasNoActionDraft`.
- The clerk link names the calendar year of the session and writes the roll number without padding. Enforced by `SourcePages.RollCall`. Test: `VoteReviewWorkspaceTests.TheClerkLinkNamesTheCalendarYearOfTheSessionAndWritesTheRollNumberWithoutPadding`.
- A person edits only the headline and the summary. Enforced by `VoteDraftRevision.ValidatePersonEditOf`, checked again under the vote review lock. Test: `VoteDraftRevisionTests.AVoteReviewerEditsOnlyTheHeadlineAndTheSummaryBecauseCodeOwnsTheDayTheKindTheFactsAndTheMemberVotes`.
- A person may keep or clear the suggestion, but never change it. Enforced by `ReviewTextRules.RequireSuggestionKeptOrCleared`, checked again under the lock. Tests: `VoteDraftRevisionTests.APersonMayKeepOrClearTheSuggestionButNeverChangeItBecauseOnlyTheModelWritesSuggestions` and `PostgresHouseVoteTests.APersonSaveMayKeepOrClearTheSuggestionButTheStoreRefusesAChangedOne`.
- A later model run keeps the text of a person and offers its own text as a suggestion. Enforced by `ReviewText.AfterModelDraft` and `ReviewTextRules.RequireAnalysisText`. Tests: `VoteDraftStepTests.APersonsTextIsKeptAndTheModelsNewTextBecomesASuggestionBecauseAModelPredictionIsNeverAHumanEdit`, `VoteDraftStepTests.ACorrectedMemberVoteKeepsAPersonsTextAndTheSuggestionWithoutAModelCall`, and `VoteDraftRevisionTests.ARevisionAfterAPersonsTextMustKeepThatTextAndOfferTheModelsAsASuggestion`.
- An approval binds to the exact revision that the page showed. Enforced by `ReviewWriteRules` under the lock. Tests: `PostgresHouseVoteTests.ApprovalBindsToTheExactRevisionSoAWorkerRevisionAfterThePageLoadedTurnsItIntoAConflict` and `PostgresHouseVoteTests.ADecisionWithAStaleReviewStateVersionIsAConflictSoTwoReviewersCannotOverwriteEachOther`.
- A model revision never approves itself. Code approves a worker revision in the same transaction. The revision needs no open concern, no text that a person wrote, and passing approval checks. Enforced by `ReviewRecords.AppendCodeApprovedAsync`. Tests: `PostgresHouseVoteTests.CodeApprovesAWorkerVoteRevisionInTheSameTransactionAndTheRecordIsReadyToPublish`, `PostgresHouseVoteTests.CodeApprovesAWorkerRevisionAfterAnEarlierCodeApprovalAndCountsItAsApproved`, `PostgresHouseVoteTests.AWorkerRevisionKeepsAnOpenConcernSoCodeDoesNotApproveIt`, `PostgresHouseVoteTests.ACodeApprovalLeavesAVoteForAPersonWhenAnApprovalCheckFails`, and `PostgresHouseVoteTests.APersonEditAfterACodeApprovalWaitsForAPersonAndAWorkerRevisionOverItStaysForAPerson`.
- A code approval queues the relationship pairs of the vote, as a person approval does. Enforced by `VoteDraftQueueMapping.AfterTransitionAsync` through `AfterDecisionHooks.ForRecordKind`. Test: `PostgresHouseVoteTests.ACodeApprovalOfAVoteRunsTheRelationshipHookSoAnApprovedStatementOfAMemberQueuesItsPair`.
- An approval checks every citation of the revision against its saved extraction. Enforced by `PostgresVoteReviewStore` and `VoteDraftRevision.ValidateAgainst`. Test: `PostgresHouseVoteTests.ApprovalChecksEveryCitationAgainstItsSavedExtractionSoNoApprovalRestsOnAMissingLine`.
- An append needs the expected revision number and the vote review lock. Enforced by `VoteDraftRows.AppendAnalysisRevisionAsync`. Tests: `PostgresHouseVoteTests.APersonsConcurrentRevisionFailsTheExpectedVersionCheckSoTheRunRetriesWithBackoffAndAppendsNothing` and `PostgresHouseVoteTests.AnAppendWaitsForTheVoteReviewLockSoAHumanSaveAndTheWorkerNeverInterleave`.
- An unchanged or no-member settlement carries its bypassed run. Enforced by `VoteDraftQueueMapping.PrepareSettlementAsync`. Test: `PostgresHouseVoteTests.AnUnchangedOrNoMemberSettlementWithoutItsRunIsRefused`.
- An analysis revision has a run, and a revision follows the current number. Enforced by `ck_vote_revisions_run` and `VoteDraftRevision.ValidateAsAnalysisRevision`. Tests: `PostgresHouseVoteTests.TheDatabaseRefusesAnAnalysisRevisionWithoutARunAndAPersonRevisionWithOne` and `VoteDraftRevisionTests.AnAnalysisRevisionFollowsTheCurrentOneAndAFirstRevisionIsNumberOne`.
- A revision lists 1 to 600 member votes, at most 64 tracked issues, and a valid issue revision ID. Every tracked issue is also a candidate. A legacy revision with no candidate set is exempt from that rule, still publishes and opens, and takes person edits. A cast has at most 32 characters. A bill type has at most 16. Every citation is a bounded span. Enforced by `VoteDraftRevision.ValidateShape`. Tests: `VoteDraftRevisionTests.TheIssueRevisionIsAHashAndTheTrackedIssuesAreBoundedSoTheRecordNamesTheRulesThatChoseThem`, `VoteDraftRevisionTests.TheTrackedIssuesMustComeFromTheIssueCandidatesAndALegacyRevisionWithoutCandidatesStaysValid`, `PostgresHouseVoteTests.ARevisionStoredBeforeTheModelChoiceHasNoCandidateSetAndStillPublishesOpensAndTakesAPersonEdit`, `VoteDraftRevisionTests.ARecordNeedsAtLeastOneMemberVoteBecauseItExistsToListTheTrackedVotes`, `VoteDraftRevisionTests.ACastOrBillTypeOverTheLimitIsRefusedBecauseThePublishedVoteRefusesItToo` and `VoteDraftRevisionTests.EveryCitationMustBeABoundedSpanOfAnExtractionSoTheReviewerCanOpenTheLine`.
- A vote whose cast or bill type is over the limit blocks as `fieldTooLong` before any model call, so it never reaches staging. Enforced by `VoteDraftStep`. Test: `VoteDraftStepTests.ACastOverTheLimitBlocksBeforeAnyCallSoOneBadVoteNeverStopsTheOtherRecordsFromPublishing`.
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
| `HouseVoteListParser` | none, because a list has no extraction | One list page of at most 250 items |
| `HouseVoteParser` | `congress-house-vote-v1` | One detail |
| `HouseVoteMembersParser` | `congress-house-vote-members-v1` | One members response, with at least one member |

`HouseVoteRenderer` renders the parsed detail and members. Its text belongs to the extraction versions `congress-house-vote-v1` and `congress-house-vote-members-v1`. The `Started` line keeps the offset of the API, for example `2025-07-02T21:15:00-04:00`, so the cited day is the House day. A renderer change bumps the extraction version of each page kind it renders.

A list item needs `congress`, `sessionNumber`, `rollCallNumber`, and `updateDate`. Its session must be the session of the page. A detail and a members response need the same roll call as the job URL and an `updateDate`.

The API covers the 118th and 119th Congresses. Each member entry gives `bioguideID`, the name, `voteParty`, `voteState`, and `voteCast`. A member needs `bioguideID` and `voteCast`. The detail parser keeps the question, vote type, result, start time, legislation, party totals, candidate totals, and source data URL. A total is a party total or a candidate total. A party total needs a party. A candidate total needs a candidate and a `total` of 0 or more. The parser ignores other counts on a candidate total. Any other total is invalid. One response can mix both kinds.

An unknown legislation type, such as an amendment, gives no bill identity whatever its number. A known type needs a number of 1 or more. A number without a type is invalid.

### Extractions

The text has one fact on each line, so a citation can quote one line. The extraction version is the `ExtractionVersion` of the parser. The source attempt is the captured attempt of the job. Optional facts that the API omits have no line. Line breaks inside a value become spaces. The `Legislation` line keeps the API spelling, such as `HCONRES`, and the vote row stores the lowercase bill type. `RenderedText` owns the line rules for bills and votes.

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
2. `VoteReader` checks that both pages have the current extraction versions. It parses both captures again and renders them. A render that differs from the saved text blocks with `renderedTextMismatch`.
3. `VoteReader` takes the facts. A fact is a line of the detail text that starts with `Started: `, `Question: `, `Result: `, `Legislation: `, `Party total `, or `Candidate total `. The Speaker election therefore cites its candidate totals. Each fact keeps its text and a span of its line. The vote type, the update time, and the source data line are not facts.
4. `PostgresVoteDraftStore.ReadEvidenceAsync` also returns the subject pages of the bill (see the decisions), and the newest saved bill detail of a voted bill. The newest is the one with the latest update time, and the job ID breaks a tie. `VoteReader` checks that its extraction version is current. It parses the bill capture again with `CongressBillDetailJob`, the code that [action drafting](actions.md#step) also uses. It renders the bill and compares the text with the saved extraction. A voted bill with no saved detail blocks with `evidenceUnavailable`. A bad version blocks with `extractionVersionChanged`. A text that differs blocks with `renderedTextMismatch`. The reader adds the `Title: ` line and the `Policy area: ` line as facts, after the vote facts. The policy area line is omitted when the bill has none. The candidate issues use the policy area of the parsed bill.
5. `VoteReader` takes the member votes. The tracked people are those of `CollectionConfiguration.CoveredBioguidePeopleOf` on the vote day. That is the date of the start time in the offset that the API gives. A vote without a start time blocks before this step. Each tracked member in the members response gives the person, the bioguide ID, the cast, and a span of the member line. The order is the bioguide ID order.
6. The fingerprint is the SHA-256 of the fact lines, with the title and policy area, and the member votes (`VoteFingerprint`, version `vote-fingerprint-v1`). It is the input hash of a run that makes no call.
7. No tracked member settles `Succeeded` with `noTrackedMember`, a bypassed run, and no revision. More than 600 members block with `tooManyMembers`. More than 64 facts, or a fact over 2,000 characters, block with `factTooLong`. A member cast over 32 characters, or a bill type over 16 characters, blocks with `fieldTooLong`.
8. `VoteKindTable.Classify` reads the vote kind from the facts, after the bill facts are added. It returns the kind and the citation of the `Question` line. A roll call with no `Question` line gets the kind other and no citation. The snapshot holds both.
9. `VoteDraftContext.ChangeSinceCurrent` compares the snapshot with the current revision. The first check is the prompt: a roll call whose newest Drafted run names an older prompt version always drafts with the model. Then equal fact texts, an equal kind, equal candidate issues, an equal issue revision, equal subjects, equal members, and equal casts give `unchanged`. Equal facts, issues, and issue revision with other members or casts give `memberVotesChanged`. Equal facts, issues, issue revision, members, and casts with another kind give `kindChanged`. Anything else, or no current revision, drafts with the model.

`memberVotesChanged` and `kindChanged` append a revision at once. `VoteDraftResolver.CopyText` keeps the headline and the summary. The run is bypassed with zero charged tokens. A change of the tracked set is also a member change, because the text names no member.

`VoteDraftStep` builds a `BillIssueChoice` with `BillIssueChoice.For`. It uses the policy area, the vote day, and the issue configuration revision of the worker. The prompt lists the candidates with their definitions, includes, and excludes. When the policy area has a candidate, the subject names of the bill join the prompt as facts. The schema then requires `issueIds`, and the system prompt asks for the choice. A policy area with no candidate gets the schema without `issueIds`. The revision stores the chosen IDs, the candidate IDs, and the issue revision ID. The run context holds the candidate IDs and the issue revision ID, so a rejected run records what the model was offered. Each record chooses its issues separately. One bill can show different issues on its action and on its vote, or on two roll calls. The sponsored and voted counts can then disagree for that bill.

The prompt holds the fact lines and the candidate issues. It tells the model that the `Title` line is the title of the measure that the vote concerns. It tells the model to name no person, because code lists the tracked members and their votes beside the text. It bans internal words such as tracked, entry, and candidate. It bans talk about how Civic Lens works or where the information came from. It bans any vote count, total, or party split in the text, because the page shows the exact counts. A measured run of 60 drafts held counts in 0 of them, and the cost fell from $0.00030 to $0.00027 a draft. The request caches the system prompt, like the statement gate. A measured call read 1,109 cache tokens on average. A prompt under the minimum cache size runs without a cache. The task is `vote-draft` with the model `claude-haiku-5-5`, set as `VoteDraftTask.Model`. It allows two passes and 2,000 output tokens. The shared `AnalysisValidationLoop` runs them.

`VoteDraftResolver` rejects an answer that is not the schema. It rejects text over its bound or with a NUL. It rejects a confidence outside 0 to 1. It rejects text that names any configured person. It matches full names, without regard to case. It rejects an `issueIds` that is missing when candidates exist, lists an ID that is not a candidate, or lists an ID twice. A rejection retries once with the reasons. A second rejection fails the entry with `outputRejected`.

### Outcomes

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | A revision was appended, by the model or as a copy for changed member votes |
| Succeeded | `unchanged` | The facts, the members, and the casts match the current revision, and the roll call was not drafted under an older prompt |
| Succeeded | `noTrackedMember` | No tracked member is in the roll call, so no record is drafted |
| Succeeded | `supersededByNewerCandidate` | The roll call has a candidate with a later update time |
| RetryWaiting | `revisionConflict` | Another writer appended first |
| Blocked | `evidenceUnavailable`, `tooManyMembers`, `factTooLong`, `fieldTooLong`, `extractionVersionChanged`, `renderedTextMismatch`, `overByteCap` | The input cannot be drafted |
| Failed | `outputRejected`, a stop code, or `attemptsExhausted` | Owner requeue needed |

A run that makes no call has the reason `unchanged`, `noTrackedMember`, `memberVotesChanged`, or `kindChanged`.

### Redraft

An owner redraft queues the settled entries of roll calls whose newest model-drafted run used an older prompt version. The newest Drafted run decides for the whole roll call. A copy or unchanged settlement writes a Bypassed run that names the current prompt. The requeue clears the attempts like a Failed requeue. Code approval, the budget gates, and the person-text rule stay as they are. A roll call whose current revision holds text that a person wrote, has an open concern, or has a withdrawn approval is not stale. The command skips it and the step makes no call for it, so no redraft moves it back to a person.

The step calls the model when the stored draft differs from the new input. It also calls the model when the newest Drafted run of the roll call names an older prompt version. The drafting writes a Drafted run under the current prompt, so the next run settles `unchanged` and calls no model. A rerun of the command then requeues nothing. The step needs no command for this. A vote on an older prompt drafts again by itself when it is collected again, corrected, or requeued. That is at most one extra draft for each stale roll call. A Failed redraft keeps the old text and the old revision. Only `votes draft requeue` retries it.

A redraft can write new revisions. Check the effect before you run it. Code approval of a new revision can queue relationship pairs, and a queued pair can cost a model call. An approved link that cites the old revision shows "cited records changed" and needs a person. A vote redraft requeues no brief, because briefs cannot cite votes. Code approves a new revision only when its text came from the model and the approval checks pass. A failed check keeps the revision for a person. The next publish replaces the published text of each approved vote, so the redraft needs no publish option. The model also chooses the tracked issues again, so a redraft can change the issues of a vote as well as its text. The public record of a replaced vote gains one correction entry. It reads "Summary rewritten by the AI model" when only the headline or the summary changed.

### Record and append

The record ID is the SHA-256 of the record key, the congress, the session, and the roll call (`PostgresCollectionJobStore.HashVoteRecord`). It frames the text with the prefix `house-vote-record:` or `senate-vote-record:`. The update time is not part of it. The record key is the source ID for a House source. Every Senate vote uses the one key `senate-votes` (`VoteSources.RecordKeyOf`). That key came from the retired senate.gov source and keeps the public record IDs stable. The revision holds the real source ID and the `VoteSource` of its evidence.

`VoteDraftRevision` (Core/Votes) holds the headline, the summary, the text origin, the suggestion, the candidate identity, and the bill identity. It also holds the facts and the vote kind with the citation of its question. Last come the member votes, the tracked issue IDs, the candidate issue IDs (`issueCandidateIds`), and the issue revision ID. `VoteDraftRows.AppendAnalysisRevisionAsync` appends it in the settlement transaction of the queue row. It takes the `civic-lens-vote-review` lock and refuses a candidate with a newer one. It checks the expected revision number and calls `ReviewRecords.AppendRevision`. A refused append discards the run and keeps the entry for a retry.

### Vote review

Vote review uses the shared decision rules and review cycles in [review](review.md#review-cycles). It copies the shape of [action review](actions.md#action-review). The Host has vote pages on top of the shared handlers.

**Ownership.** Core/Votes holds `VoteReview`, the aggregate of one record, and the review rules on `VoteDraftRevision`. Application/Votes holds `SaveVoteDraft`, `DecideVoteReview`, `GetVoteReview`, and the store boundary `IVoteReviewStore`. Infrastructure/Votes holds `PostgresVoteReviewStore` on the shared `ReviewRecords` core. The core and the draft rows share one instance, `VoteDraftRows.Records`. A person cannot create a vote draft, so the store has no create. The shared text rules of actions and votes are in `ReviewText`, `ReviewTextRules`, and `ReviewTextSuggestion` (Core/Review).

**Save and decide.** `SaveVoteDraft` and `DecideVoteReview` work as the action handlers do. A decision requeues no brief until briefs can cite votes. It still calls `BriefStaleness.AfterDecision`, which takes the brief locks in the action order.

**Reads.** `GetVoteReview` returns the review cycle, the extractions that the loaded revisions cite, and the configured names of the people. The names come from the configuration revisions of the candidates. The Needs a person queue lists the records in Needs action. Needs action means every status except Approved. The queue order is `last_revised_at_utc_ticks` descending, so a new revision of an old record moves it to the top. Every revision sets the column.

**Pages.** No page lists all votes. The Review home page shows the records in Needs action in the Needs a person queue; see [review](review.md#lists-and-paging). A row links to the editor. The rail has no Votes link.

`/Review/Votes/{record-id}` is the editor. It has these parts:

- the change since the last approved revision: the old and the new text side by side, with the facts of each revision beside it
- those facts are the roll call, the kind, the bill, the tracked issues, and the member votes
- the saved revision and the "Edit wording" form, with the suggestion and its "Use suggestion" and "Dismiss" buttons
- the evidence: the vote kind with its cited `Question` line, and each fact with its cited line
- a link to the roll call page on clerk.house.gov
- a link to the action draft of the bill, when the vote names a bill that has a draft
- the tracked members: the name and the cast of each member, with the cited member line
- the shared `ReviewDecisionPanel` and `ReviewHistoryPanel`

The action link comes from `GetVoteReview`. The review read finds the action draft of the bill with `FindBillActionRecordIdsAsync` in the same transaction, which publication uses too. The clerk link is the HTML vote page: `https://clerk.house.gov/Votes/{year}{number}`. The year is the calendar year of the session. The roll number has no padding, so roll 100 of 2025 is `2025100` and roll 5 is `20255`. The format was checked against the live site on 2026-10-19.

A person cannot edit the member votes.

### Owner commands

`votes draft` needs `CIVIC_LENS_DATABASE` and `CIVIC_LENS_REVIEW_OWNER`. `VoteCommand` runs on the shared `AnalysisQueueCommands`, as `ActionCommand` does. The [operations](../operations.md#vote-drafting) guide shows each command.

| Command | Effect |
|---|---|
| `votes draft status` | JSON with `queue` (the status, `chargedToday`, and `counts` for each view) and `waitingForBill` |
| `votes draft list [view] [limit]` | Entries in a view: `drafted`, `unchanged`, `superseded`, `no-member`, `pending`, or `failed` |
| `votes draft requeue <candidate-id\|--all>` | Failed rows, and no others, back to Pending |
| `votes draft requeue --blocked --code <block-code>` | Blocked rows with exactly that code, and no others, back to Pending. The code is required and must not be empty |
| `votes draft redraft` | Succeeded `drafted` and `unchanged` rows of votes with a bill and an older prompt version, and no others, back to Pending. Older entries of a stale roll call requeue too and settle as superseded, so the printed count is entries, not roll calls. Needs no argument and calls no model itself |

`waitingForBill` counts the vote candidates that name a bill and have no queue row. They wait for the bill detail. `VoteDraftQueueRows.WaitingForBill` owns this rule. The bill detail admission uses the same query to queue them.

### Executor

The executor is `AnalysisWorker<VoteDraftRun>` with `VoteDraftStep`. It runs in the `worker` process after action drafting and before brief drafting, with its own wakeup, status row, daily limit, and call cap. It uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`.

Its variables are in [operations](../operations.md#vote-drafting). A daily limit without the key is a configuration error. The status row is in `document_change_analysis_status` under the task `vote-draft`.

### Voteview Senate collection

Senate votes come from the Voteview CSV files, because senate.gov returns a 403 to the collector. Voteview publishes three files for each Congress: roll calls, votes, and members. The files are public, and the host has no `robots.txt`. The shipped source `voteview-senate-votes` sets `apiParser` `voteviewSenateVoteList`, `backfillFrom` 2025-01-03, `minimumCongress` 119, and `maxBytes` 4,000,000. It ships with `enabled: false`. A configuration may enable only one Voteview source. The parser name `senateVoteList` of the retired senate.gov source still reads in a stored configuration. The Host refuses a live configuration file that enables it. Its error names the source and says to disable or remove it (`RetiredSourceException`). Other configuration errors print a generic line, because their text can hold input values.

The source URL is `https://voteview.com/static/data/out`. The file URL is `{source url}/{kind}/S{congress}_{kind}.csv` with the kind `rollcalls`, `votes`, or `members`. `VoteviewUrl` owns it. The resources are `voteviewRollCalls`, `voteviewMembers`, and `voteviewVotes`.

The due check creates one root job for the roll call file of the first Congress. The jobs form a chain, like the House list, detail, and members jobs. The roll call job admits the members job of its Congress. For an earlier Congress it also admits the roll call job of the next Congress. The members job admits the votes job. The key of the members job holds the ID of the roll call job. The key of the votes job holds the ID of the members job. The votes job finds its two sibling captures from these keys. The key of every job after the root holds the ID of its parent job. Each daily sweep therefore walks the whole chain again, and a failed job blocks only that day. The body check below keeps the repeat cheap. `VoteviewJobKeys` owns the keys.

The roll call job and the members job read no body. The votes job reads all three captures. It first reads the three attempts and compares their capture hashes and the extraction version with those of the newest candidate of the Congress. When all match, it stops and loads no body. Otherwise it loads the three bodies and parses the files and joins them by the Voteview roll number and the icpsr number.

`VoteviewParser` refuses a row of another Congress or chamber, an unknown cast code, and an unknown party code. It skips every member row of the chamber `President`, and the join drops the casts of those icpsr numbers. It refuses every other member with no bioguide ID. It refuses a vote by an icpsr that the member file does not list. It refuses a member who votes twice in one roll call. `VoteviewRoll.Join` skips a roll call whose yea or nay member count differs from the roll call file. The job still succeeds with the code `voteviewCountMismatch`, and a warning gives the number skipped. The number is only in the log.

`VoteviewRenderer` renders one roll call as text. The first line is `Senate vote: {congress} {session} {clerk roll}`. The `Started` line holds the day. The fact lines `Question`, `Vote title`, `Result`, `Majority required`, and `Count` follow, with the prefixes of `VoteFactPrefixes.SenateDetail`. One `Member` line for each senator follows: the bioguide ID, the Voteview name, the party letter, the state, and the cast. `RenderedText.MemberLine` owns the line for all three sources. `SenateVoteKindTable` classifies the question text. A test covers the 17 questions of the 119th Congress.

Each rendered roll call is one extraction of the votes capture. The extraction ID holds the Voteview roll number as its part, so one capture gives one extraction for each roll call. The store compares the text hash with the newest version of the roll call. It writes a new version only when the hash differs. Preparation saves only the extractions of changed roll calls. The store checks the hash again under the job lock, like `AdmitPhotoAsync`.

A version row has the roll call job as its detail job. Its update time is the capture time of the votes file. The candidate names the roll call job as detail, the votes job as members, and the members job as roster. Both extraction columns name the one rendered text. The candidate waits for its bill detail like the other vote candidates.

### Voteview drafting

`VoteReader` picks the branch from the `VoteSource` of the candidate source. The Voteview branch checks the extraction version of the one saved text. The candidate names three captures. The roll call file is the detail job, the votes file is the members job, and the members file is the roster job. The detail job holds no extraction, so the saved text belongs to the votes job. The reader parses the three captures for the Congress and joins the one roll call by session and roll number. It renders that roll call and compares the render with the saved text. A roll call that is missing or whose counts now differ blocks the draft. The vote day is the `Started` date. The facts are the lines of `VoteFactPrefixes.SenateDetail`, and `SenateVoteKindTable` classifies the `Question` line. The tracked senators match by bioguide ID. Each cast cites its member line in the saved text. Publication reads the same line from the end, so a name with commas is safe. The prompt names the Senate roll call data and Congress.gov data as the sources.

## Storage and contracts

- The generated [votes schema](schema/votes.md) lists every table, column, key, check, and index.
- `vote_versions` holds one row for each vote version, keyed by its detail job, session, and roll call number. A Voteview roll call job holds many roll calls. The row stores the chamber and the identity, the exact update time, and the nullable bill identity. It also names the extraction of the detail. The store reads it to find duplicates and to write the candidate.
- `vote_candidates` holds one row for each vote version with both pages captured. It stores the chamber, and it names the detail job, the members job, both extractions, and the configuration revision. A composite foreign key and a unique index on the detail job, session, and roll call number tie it to its version. The nullable `roster_job_id` names the members file job of a Voteview candidate. Drafting reads these rows.
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
| Voteview parsers, URLs, keys, rendering | `src/CivicLens.Application/Collection/VoteviewVotes/` |
| Admission port | `src/CivicLens.Application/Collection/Votes/IVoteAdmissionStore.cs` |
| Postgres admission and candidates | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Votes.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.VoteviewVotes.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Scheduling.cs` |
| Table rows | `src/CivicLens.Infrastructure/Collection/Votes/` |
| Drafting step and checks | `src/CivicLens.Application/Votes/VoteDraftStep.cs`, `src/CivicLens.Application/Votes/VoteDraftContext.cs`, `src/CivicLens.Application/Votes/VoteDraftResolver.cs` |
| Bill detail lookup | `src/CivicLens.Infrastructure/Collection/BillStream/BillDetailRows.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.BillFollowOns.cs` |
| Evidence reader, snapshot, fingerprint | `src/CivicLens.Application/Votes/VoteReader.cs`, `src/CivicLens.Application/Votes/VoteSources.cs`, `src/CivicLens.Application/Votes/VoteSnapshot.cs`, `src/CivicLens.Application/Votes/VoteFingerprint.cs`, `src/CivicLens.Application/Documents/LineCitations.cs` |
| Prompt, task identity, settings, codes | `src/CivicLens.Application/Votes/VoteDraftPrompt.cs`, `src/CivicLens.Application/Votes/VoteDraftTask.cs`, `src/CivicLens.Application/Votes/VoteDraftSettings.cs`, `src/CivicLens.Application/Votes/VoteDraftErrorCodes.cs` |
| Queue lifecycle and store boundary | `src/CivicLens.Application/Votes/VoteDraftLifecycle.cs`, `src/CivicLens.Application/Votes/IVoteDraftStore.cs`, `src/CivicLens.Application/Votes/IVoteDraftModel.cs` |
| Vote kind and count labels | `src/CivicLens.Core/Votes/VoteKind.cs`, `src/CivicLens.Application/Votes/VoteKindTable.cs`, `src/CivicLens.Application/Votes/SenateVoteKindTable.cs`, `src/CivicLens.Application/Votes/VoteFactPrefixes.cs`, `src/CivicLens.Application/Votes/VoteCountLabels.cs`, `src/CivicLens.Application/Votes/VoteFacts.cs` |
| Owner command | `src/CivicLens.Host/Votes/VoteCommand.cs`, `src/CivicLens.Application/Votes/GetVoteDraftStatus.cs`, `src/CivicLens.Application/Votes/RequeueBlockedVoteDrafts.cs`, `src/CivicLens.Application/Votes/RedraftVoteDrafts.cs`, `src/CivicLens.Application/Votes/VoteDraftView.cs`, `src/CivicLens.Application/Votes/VoteDraftListItem.cs` |
| Publication reads | `src/CivicLens.Application/Votes/UnpublishedApprovedVote.cs`, `src/CivicLens.Application/Votes/PublishableVote.cs`, `src/CivicLens.Infrastructure/Votes/VoteReviewQueries.cs`, `src/CivicLens.Application/Publication/VoteRecordLoader.cs` |
| Vote revision | `src/CivicLens.Core/Votes/VoteDraftRevision.cs`, `src/CivicLens.Core/Review/ReviewText.cs`, `src/CivicLens.Core/Review/ReviewTextRules.cs`, `src/CivicLens.Core/Review/ReviewTextSuggestion.cs`, `src/CivicLens.Core/Review/IHeadlineSummaryRevision.cs` |
| Review aggregate | `src/CivicLens.Core/Votes/VoteReview.cs` |
| Review commands and reads | `src/CivicLens.Application/Votes/SaveVoteDraft.cs`, `src/CivicLens.Application/Votes/DecideVoteReview.cs`, `src/CivicLens.Application/Votes/GetVoteReview.cs`, `src/CivicLens.Application/Votes/IVoteReviewStore.cs`, `src/CivicLens.Application/Votes/BillVoteDraft.cs` |
| Review store | `src/CivicLens.Infrastructure/Votes/PostgresVoteReviewStore.cs`, `src/CivicLens.Infrastructure/Review/ConfiguredPersonNames.cs`, `src/CivicLens.Infrastructure/Votes/BillVoteDrafts.cs` |
| Review pages | `src/CivicLens.Host/Review/ReviewHome.razor`, `src/CivicLens.Host/Votes/VoteEditor.razor`, `src/CivicLens.Host/Votes/VoteLabels.cs`, `src/CivicLens.Host/Votes/VoteRoutes.cs`, `src/CivicLens.Host/Collection/SourcePages.cs` |
| Postgres drafting store and append | `src/CivicLens.Infrastructure/Votes/PostgresVoteDraftStore.cs`, `src/CivicLens.Infrastructure/Votes/VoteDraftRows.cs`, `src/CivicLens.Infrastructure/Votes/VoteDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Votes/VoteDraftQueueRows.cs` |
| Drafting tables and model adapter | `src/CivicLens.Infrastructure/Votes/Persistence/`, `src/CivicLens.Infrastructure/Votes/AnthropicVoteDraftModel.cs` |
| Worker composition | `src/CivicLens.Host/Votes/VoteDraftComposition.cs`, `src/CivicLens.Host/Votes/VoteDraftConfiguration.cs` |

## Limits and planned work

- The `Down` of the migration `VoteviewSenateVotes` fails once Voteview rows exist, because one roll call job then holds many version rows. It also makes the stored part IDs stale. Reset the development database instead of reverting.
- When code approves a Senate vote from the Voteview source, the approval rests on Voteview data and not on the official Senate roll call. The published vote names its source, links the senate.gov roll call page, and the About and method pages name Voteview.
- Each daily Voteview sweep fetches the three files of every Congress from the minimum Congress on. The body check stops the parse and the render of an unchanged Congress, but the fetch still costs about 3 MB for each Congress.
- Voteview lists no Vice President tie-break vote. Checked on S119 on 2026-10-29. Roll call 15, the Hegseth nomination, ends 50 to 50 as Confirmed. No row of the vice president casts it. The President row (icpsr 99912) casts Yea on 145 roll calls. It is not the Vice President, and the parser leaves it out. A published Voteview roll call therefore shows the result without the tie-break cast. The vote page and the method page say so. No test of the data. `ReleaseRendererTests.AVoteviewVoteSaysThatVoteviewListsNoVicePresidentTieBreakVoteSoAFiftyFiftyResultIsNotMisread` tests the page text.
- The lists of vote records read `vote_drafts` without the partial status indexes of `action_drafts`. The table held 676 roll calls on 2026-10-17 and grows by about 700 a year. `brief_drafts` also has no such indexes. Add the indexes when the table passes about 10,000 rows.
- A prompt bump drafts again every vote on a bill that still has an old draft. The command queues them, and a re-collected, corrected, or requeued vote on an older prompt drafts again with no command. That costs at most one extra draft for each stale roll call. The public record then shows a correction entry. Code compares the new vote with the published vote. When only the headline or the summary changed, the entry reads "Summary rewritten by the AI model". It ends with "The vote facts did not change." A change of the issues keeps "Updated from the source". Action redrafts still show "Updated from the source" for such a change.
- A later bill detail does not redraft a vote that was already drafted. The next change of the vote itself drafts again.
- Subjects that arrive after a vote drafted make the next run of that vote draft again, because the hash differs. Nothing queues that run by itself. `votes draft redraft` queues a vote whose newest draft used an older prompt version. A vote whose newest draft already uses the current prompt waits for a change of the vote itself, or an owner requeue.
- A voted bill from a congress before `minimumCongress` of the bill source would wait forever. No such vote exists today, because the vote sweep starts in 2025.
- Measured on 2026-10-16 with 147 drafts: no headline contradicts the result in 74 checked drafts. But 23 of the 74 summaries say the facts do not describe the measure. 28 explain House procedure from model knowledge.
- Measured on 2026-10-16 over 676 roll calls: 145 are procedural, 96 are amendments, and 324 pass a bill.
- The issue panel counts only the substantive vote kinds, which `PublishedVoteKindExtensions.IsSubstantive` lists; see [publication](publication.md#issue-panel).
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
- The session of today uses the UTC date. The new session therefore starts a few hours early on 1 January in US time zones. Its list is empty then.
- Roll call lookup keys by chamber, Congress, session, and number. It ignores the source of a vote draft, because each chamber has one source and no code owns its ID. A second source for one chamber needs a source filter in `FindRecordIdByRollAsync`. The lookup text `Roll N` means a House roll, and `Senate roll N` means a Senate roll.
