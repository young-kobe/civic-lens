# Roster

## Purpose

The roster feature captures two public datasets once a day and keeps them as tables. The member tables hold current members of Congress with their offices and dated terms. The executive tables hold every president and vice president with dated terms. Each row cites the capture it came from. A Host command compares the latest member snapshot with the configured people and prints the people to add and the people to review. The roster proposes. A person edits the configuration.

The feature also captures one official portrait for each configured member. A photo row cites its capture and carries a copy of the credit.

## Decisions

- The member dataset is `legislators-current.json` of the `unitedstates/congress-legislators` project. The executive dataset is its `executive.json`. Its data has a CC0 license. The project publishes the file on GitHub Pages, so it needs no credential.
- Each dataset is an `api` source. The member source sets `apiParser` to `congressLegislators`. The executive source sets it to `congressLegislatorsExecutive`. Both check once a day. The shipped sources `congress-members` and `executive-officials` are disabled, like the other `api` sources.
- The executive file gives a GovTrack ID to all 80 people. It gives a Wikidata ID to 8 recent people only, and a bioguide ID to 67. So `executive_officials` is keyed by the GovTrack ID. The Wikidata ID is a nullable column. The bioguide ID is not stored, because no code reads it.
- The executive store has the same reader as the member store. `ReadLatestAsync` returns the newest snapshot by `RosterSnapshotRules`. The publish step reads both stores to write `officials.json`; see [publication](publication.md#release-content).
- Only the current file is read. The historical file is out of scope. The 10 MB capture read limit stays as it is.
- A roster source of either kind covers no people. It is still eligible on every date, because the file has no coverage list. `CollectionConfiguration.IsActiveOn` owns this rule for admission and for scheduled checks.
- A snapshot is one header row in `roster_snapshots`, with the rows of one kind below it. The header holds the attempt ID of the capture. The source comes from the attempt. Every row holds that attempt ID. A row is never updated.
- `RosterSnapshotRules` is the one owner of the snapshot rules for both kinds. The hash rule and the reader rule below read the headers of one source.
- The daily check is the body hash. The source downloads the file each day, with no ETag and no conditional request. `CollectionConfiguration` enforces this. A capture whose hash matches any snapshot of the source adds only its attempt row. Preparation checks the hash before it parses.
- Readers take the newest captured attempt of the source whose hash matches some snapshot. They read the oldest snapshot with that hash. A file that changes back to an earlier body therefore reads as that earlier snapshot.
- Admission takes no lock. Two concurrent captures of the same new body can both write a snapshot. The duplicate is harmless, because readers pick one snapshot by a fixed order.
- Dates are `date` columns, because the dataset gives calendar dates.
- Rows cite the capture through the attempt. They have no text span and no extraction.
- The office, phone, and URL of a member come from the last term of the member. A member with no `official_full` name gets the first and last name.
- A proposed person ID is the official name in lowercase words joined by hyphens, as in the shipped configuration. It gets the bioguide ID as a suffix when another person holds that ID.
- Press sources of a new member are a manual step. The command says so for each proposed member.
- Member portraits come from the `unitedstates/images` project. It republishes the GPO Member Guide photos on GitHub Pages. They are public domain. The images are JPEG files of 225 by 275 pixels, named by bioguide ID.
- A photo fetch is an `api` job with the resource `memberPhoto`. It adds no collection mode and changes no wire contract. The collector keeps the bytes as an ordinary capture.
- One source with `apiParser` `memberPhotos` covers all member photos. Its URL is the image directory. Its allowed path prefix is `/images/congress/225x275/`. One job fetches the photo of one person.
- Only configured people with a bioguide ID get a photo job. The roster file adds no people.
- Every member roster job enqueues the photo jobs, also when the roster body is not new. The job key holds a 30 day period. Each person gets at most one job in each period. A later period checks the photo again.
- A photo job runs with the backfill priority, so live checks win.
- With no enabled photo source, the roster job enqueues no photo job.
- The shipped admission policy sets `maxJobs` 600. The job policy allows 2 attempts of 2 requests, 500,000 bytes, and 20 seconds. So each job reserves 4 requests, 1,000,000 bytes, and 40 seconds. That gives `maxTotalRequests` 2,400, `maxTotalBytes` 600,000,000, and `maxTotalTimeoutSeconds` 24,000. Validation refuses a policy that cannot hold one job for each configured bioguide ID.
- Preparation accepts only a JPEG of exactly 225 by 275 pixels. A small parser reads the size from the frame header, because Application takes no image package.
- The credit is the constant `MemberPhoto.MemberGuideCredit`. Each photo row copies the text. A later change to the constant never rewrites provenance. Configured executive photos will take their credit from `PersonConfiguration.photo`.
- A photo row holds no image bytes. The capture holds them.
- A missing photo is not stored. A 404 ends the job `Failed` with no retry. A person with no row has no photo. A later 404 keeps the last good photo. A takedown has no mechanism yet; see the limits.
- The roster feature writes photo rows and reads the newest row of each person for publication. Publication owns the photo files; see [publication](publication.md#release-content).

## Invariants

Each line gives the rule, what enforces it, and the test that protects it.

- A configuration of a roster source has no credential, no coverage list, and no Congress options. Enforced by `CollectionConfiguration`. Tests: `RosterSourceConfigurationTests.ACredentialIsRejectedBecauseTheFileIsPublic`, `RosterSourceConfigurationTests.ACoverageListOrCongressOptionIsRejectedBecauseTheRosterCoversNoPeople`, `RosterSourceConfigurationTests.AnExecutiveSourceFollowsTheRulesOfARosterFileAndStaysEligible`.
- A roster source stays eligible with no covered people. Enforced by `CollectionConfiguration.IsActiveOn`. Tests: `RosterSourceConfigurationTests.ARosterSourceCoversNoPeopleYetStaysEligibleSoTheDailyCheckRuns`, `RosterSourceConfigurationTests.AnExecutiveSourceFollowsTheRulesOfARosterFileAndStaysEligible`, `PostgresMemberRosterStoreTests.TheDailyCheckCreatesARosterJobWithNoCredentialEvenThoughTheSourceCoversNoPeople`, `PostgresExecutiveRosterStoreTests.TheDailyCheckCreatesAnExecutiveJobWithNoCredentialEvenThoughTheSourceCoversNoPeople`.
- A roster job fetches the source URL and no other URL. Enforced by `CollectionJobDefinition`. Tests: `RosterSourceConfigurationTests.ARosterJobIsBoundToTheSourceUrlAndNoOtherUrl`, `RosterSourceConfigurationTests.AnExecutiveJobIsBoundToTheSourceUrlAndTheExecutiveResource`.
- A roster value is not longer than its column. Enforced by `MemberRosterParser` and `ExecutiveRosterParser` with the limits of `RosterLimits`, which the row configurations also use. Tests: `MemberRosterParserTests.AValueLongerThanItsColumnIsRejectedSoThePreparationBlocksAsInvalidData`, `ExecutiveRosterParserTests.AValueLongerThanItsColumnIsRejectedAndOneAtTheLimitIsAccepted`.
- A snapshot is complete or absent. Enforced by one commit in `AdmitAsync` of each store. The member store uses an explicit transaction that also holds the vote requeue and the photo jobs. The executive store uses one `SaveChanges`. Tests: `PostgresMemberRosterStoreTests.AFailedInsertLeavesNoPartialSnapshot`, `PostgresExecutiveRosterStoreTests.AFailedInsertLeavesNoPartialSnapshot`.
- A member row or an executive official row has a header row. Enforced by the foreign keys `FK_roster_members_roster_snapshots_attempt_id` and `FK_executive_officials_roster_snapshots_attempt_id`. Tests: `PostgresMemberRosterStoreTests.AMemberRowWithoutAHeaderIsRefusedSoNoSnapshotExistsWithoutIt`, `PostgresExecutiveRosterStoreTests.AnOfficialRowWithoutAHeaderIsRefusedSoNoSnapshotExistsWithoutIt`.
- A snapshot of one source never satisfies the rules of another source, so a member snapshot never satisfies the executive hash rule and the reverse. Enforced by `RosterSnapshotRules`, which reads the attempts of one source. Tests: `PostgresMemberRosterStoreTests.ASnapshotOfOneSourceIsNotKnownToAnotherSourceThatCapturedTheSameBody`, `PostgresExecutiveRosterStoreTests.ACongressSnapshotNeverSatisfiesTheExecutiveHashRuleAndTheExecutiveSnapshotNeverSatisfiesTheCongressRule`.
- A capture whose body matches any snapshot of the source is not parsed and writes no rows. Enforced by `RosterSnapshotRules.HasSnapshotOfCaptureAsync` and `JobPreparation`. Tests: `PostgresMemberRosterStoreTests.ACaptureWithTheBodyOfAnEarlierSnapshotNotOnlyTheLatestIsKnown`, `PostgresExecutiveRosterStoreTests.ACaptureWithTheBodyOfAnEarlierSnapshotNotOnlyTheLatestIsKnown`, `JobPreparationTests.AKnownRosterCaptureIsNotParsedAndWritesNothing`, `JobPreparationTests.AKnownExecutiveCaptureIsNotParsedAndWritesNothing`.
- `ReadSenatorLisIdsAsync` returns the pairs of bioguide ID and `lis` ID over all snapshots of the source, for members with a Senate term in that snapshot. Enforced by `PostgresMemberRosterStore`. Tests: `PostgresMemberRosterStoreTests.SenatorLisIdsListOnlySenatorsFromEverySnapshotSoASenatorWhoLeftStillMatchesHisOldVotes`, `PostgresMemberRosterStoreTests.ASenatorWithTwoLisIdsInTwoSnapshotsListsBothSoTheCallerCanRefuseTheConflict`, `PostgresMemberRosterStoreTests.ASenatorWithoutALisIdListsWithANullIdSoTheCallerCanBlockInsteadOfSkippingHim`, and `PostgresMemberRosterStoreTests.SenatorLisIdsOfOneSourceExcludeTheSnapshotsOfAnotherSource`.
- Readers return the same snapshot for any admission order and any duplicate snapshot. Enforced by `RosterSnapshotRules.ReadLatestSnapshotAttemptIdAsync`, which `PostgresMemberRosterStore.ReadLatestAsync` and `PostgresExecutiveRosterStore.ReadLatestAsync` call. Tests: `PostgresMemberRosterStoreTests.ReaderFollowsTheNewestCaptureWhateverTheAdmissionOrder`, `PostgresMemberRosterStoreTests.TwoSnapshotsOfOneBodyReadAsTheOldestOne`, `PostgresExecutiveRosterStoreTests.ReaderFollowsTheNewestCaptureWhateverTheAdmissionOrder`.
- Old snapshots stay when a changed capture arrives. Enforced by the insert-only store. Test: `PostgresMemberRosterStoreTests.AChangedCaptureAppendsASnapshotAndReadersTakeTheLatestWhileTheOldRowsStay`.
- A roster source of either kind sets no ETag and no Last-Modified. A 304 would leave the attempt without a capture body and stop the daily check. Enforced by `CollectionConfiguration`. Tests: `RosterSourceConfigurationTests.AValidatorIsRejectedBecauseA304WouldStopTheDailyCheck`, `RosterSourceConfigurationTests.AnExecutiveSourceFollowsTheRulesOfARosterFileAndStaysEligible`.
- A configuration has at most one roster source of each kind, and at most one photo source. Enforced by `CollectionConfiguration`. Tests: `RosterSourceConfigurationTests.ASecondRosterSourceIsRejectedBecauseProposalsReadOneSource`, `RosterSourceConfigurationTests.ASecondExecutiveSourceIsRejectedBecauseEachKindHasOneSource`, `RosterSourceConfigurationTests.ASecondSourceOfTheOtherRosterKindIsAccepted`, `MemberPhotoSourceTests.ASecondPhotoSourceIsRejectedBecauseTheRosterJobsReadOneSource`.
- A term has a chamber `sen` or `rep` and an end date that is not before its start. Enforced by `ck_roster_terms_chamber`, `ck_roster_terms_dates` and `MemberRosterParser`. Tests: `MemberRosterParserTests.ATermWithAnUnknownChamberIsRejected`, `MemberRosterParserTests.ATermThatEndsBeforeItStartsIsRejected`.
- A term keeps its caucus. An Independent compares with the party it caucuses with, and the caucus can change between terms. A caucus is optional and has the same length limit as a party. Vote publication counts a member under the caucus, else the party; see [publication](publication.md). Enforced by `MemberRosterParser` and the `roster_terms.caucus` column. Tests: `MemberRosterParserTests.ATermKeepsItsCaucusSoAnIndependentComparesWithTheCaucusParty`, `PostgresMemberRosterStoreTests.ATermCaucusRoundTripsSoAnIndependentComparesWithTheCaucusParty`.
- An executive term has the office `prez` or `viceprez` and an end date that is not before its start. Enforced by `ck_executive_terms_office`, `ck_executive_terms_dates` and `ExecutiveRosterParser`. Tests: `ExecutiveRosterParserTests.ATermOfAnOfficeOtherThanPresidentOrVicePresidentIsRejected`, `ExecutiveRosterParserTests.ATermThatEndsBeforeItStartsIsRejected`, `PostgresExecutiveRosterStoreTests.AFailedInsertLeavesNoPartialSnapshot`.
- An executive official has a positive GovTrack ID that no other official holds. A Wikidata ID, when present, is `Q` and digits without a leading zero, and no other official holds it. Enforced by `ExecutiveRosterParser`, `WikidataId` and `ck_executive_officials_identity`. Tests: `ExecutiveRosterParserTests.AnOfficialWithoutAPositiveGovtrackIdIsRejected`, `ExecutiveRosterParserTests.AGovtrackIdListedTwiceIsRejectedBecauseItNamesOnePerson`, `ExecutiveRosterParserTests.AWikidataIdListedTwiceIsRejectedBecausePublishJoinsAPersonByIt`, `ExecutiveRosterParserTests.AMalformedWikidataIdIsRejected`, `PostgresExecutiveRosterStoreTests.AMalformedWikidataIdIsRefusedByTheDatabase`.
- An empty snapshot never reads as every person leaving. Enforced by `ProposeRosterChanges`. Tests: `ProposeRosterChangesTests.NoSnapshotYieldsNoProposalSoAnEmptyRosterNeverReadsAsEveryoneLeaving`, `RosterCommandTests.ProposeWithoutASnapshotFailsInsteadOfProposingEveryoneAsLeaving`.
- The propose command writes nothing. Enforced by `ProposeRosterChanges`, which has only a read port. Test: `RosterCommandTests.ProposeListsNewMembersWithAManualPressStepAndConfiguredMembersWhoLeftAndWritesNothing`.
- A photo source has an API parser in API mode, no credential, no validators, and no coverage list. Enforced by `CollectionConfiguration`. Tests: `MemberPhotoSourceTests.ACredentialIsRejectedBecauseTheImagesArePublic`, `MemberPhotoSourceTests.AValidatorIsRejectedBecauseA304WouldLeaveACheckWithoutACapture`, `MemberPhotoSourceTests.APhotoParserOutsideApiModeIsRejectedBecauseOnlyApiModeKeepsTheRawBytes`, `MemberPhotoSourceTests.ThePhotoSourceValidatesCoversNoPeopleAndStaysEligible`.
- A photo job fetches one photo URL under the source prefix, with the photo resource, and no other URL. Enforced by `CollectionJobDefinition` and `MemberPhotoUrl`. Tests: `MemberPhotoSourceTests.AMemberPhotoJobIsBoundToAPhotoUrlUnderTheSourcePrefixAndToNoOtherUrlOrResource`, `MemberPhotoSourceTests.TheRosterSourceRejectsAPhotoUrlSoOnlyThePhotoSourceFetchesPhotos`, `MemberPhotoSourceTests.AUrlThatIsNotAPhotoOfABioguideIdUnderTheSourceReadsAsNone`.
- The photo source makes no scheduled root job. Enforced by `ConfiguredCollectionSource.FixedApiTarget`. Test: `MemberPhotoSourceTests.ThePhotoSourceMakesNoScheduledRootJobBecauseTheRosterJobCreatesThePhotoJobs`.
- A roster job makes one backfill photo job for each configured person with a bioguide ID, and none for other people. Enforced by `ConfiguredCollectionSource.CreateMemberPhotoBatch`. Tests: `MemberPhotoSourceTests.TheBatchHoldsOneBackfillJobForEachConfiguredPersonWithABioguideIdAndNoOtherPerson`, `PostgresMemberPhotoJobTests.ASnapshotAndOneBackfillPhotoJobForEachConfiguredPersonWithABioguideIdAreWrittenTogether`.
- A person gets at most one photo job in each 30 day period, and a new period adds one. Enforced by `MemberPhotoJobKeys` and the unique idempotency key. Tests: `MemberPhotoSourceTests.AKeyChangesEveryThirtyDaysFromTheUnixEpochSoEachPersonGetsOneJobForEachPeriod`, `PostgresMemberPhotoJobTests.ARepeatedRosterJobInTheSamePeriodAddsNoPhotoJobSoTheUniqueKeyMakesTheEnqueueIdempotent`, `PostgresMemberPhotoJobTests.ANewPeriodAddsOneJobForEachPersonSoPhotosAreRecheckedEveryThirtyDays`.
- A roster job enqueues photo jobs also when its body is known. Enforced by `JobPreparation`. Tests: `JobPreparationTests.ANewRosterBodyPassesTheEnabledPhotoSourceSoTheSnapshotAndItsPhotoJobsShareOneTransaction`, `JobPreparationTests.AKnownRosterBodyStillEnqueuesPhotoJobsSoEachDailyCheckRefreshesTheMonthlyPhotoWindow`.
- With no enabled photo source, no photo job exists. Enforced by `JobPreparation.EnabledPhotoSourceOf`. Tests: `JobPreparationTests.WithoutAnEnabledPhotoSourceTheRosterJobEnqueuesNoPhotoJobs`, `PostgresMemberPhotoJobTests.ARosterBodyWithoutAPhotoSourceWritesTheSnapshotAndNoJob`.
- The photo jobs and the snapshot commit together. A failed job insert leaves no snapshot. Enforced by the one transaction of `PostgresMemberRosterStore.AdmitAsync`. Test: `PostgresMemberPhotoJobTests.AFailedPhotoJobInsertLeavesNoPartialSnapshot`.
- The admission policy of the photo source holds one job for each configured bioguide ID. Enforced by `CollectionConfiguration.Validate`. Tests: `MemberPhotoSourceTests.APolicyThatCannotHoldOneJobForEachPersonWithABioguideIdIsRejectedSoTheFirstAdmissionCannotFailLate`, `MemberPhotoSourceTests.TheShippedPhotoSourceIsOffByDefaultAndItsPolicyHoldsAJobForEveryConfiguredBioguideId`.
- A photo row exists only for a JPEG of 225 by 275 pixels with the media type `image/jpeg`. Any other body ends the job `Failed` with the code `InvalidDataException` and writes no row. Enforced by `JobPreparation` and `MemberPhotoImage`. Tests: `JobPreparationTests.APhotoThatIsNotAJpegOfThePublishedSizeEndsFailedAsInvalidDataAndWritesNoRow`, `MemberPhotoImageTests.AnyOtherSizeIsInvalidBecauseTheSourceFixesOneFrame`, `MemberPhotoImageTests.ABodyWithoutTheJpegSignatureIsInvalid`, `MemberPhotoImageTests.AnotherMediaTypeIsInvalidBecauseAnErrorPageCanKeepAPhotoUrl`.
- A photo job of a person who is not in the configuration ends `Failed` with the code `InvalidDataException` and writes no row. Enforced by `JobPreparation`. Test: `JobPreparationTests.APhotoJobOfAPersonNoLongerInTheConfigurationIsInvalidDataSoNoRowNamesAStranger`.
- A photo row holds the person ID, the source URL, a copy of the credit, and the attempt of its capture. Enforced by `PostgresMemberRosterStore.AdmitPhotoAsync`. Test: `PostgresMemberPhotoStoreTests.ARowHoldsThePersonTheSourceUrlTheCopiedCreditAndTheAttemptOfItsCapture`.
- Photo rows are append only. A changed body adds a row and keeps the old row and capture. An unchanged body adds none. An earlier body that returns adds a row. A repeat of one attempt adds none. Each person is compared on their own rows. Enforced by `PostgresMemberRosterStore.AdmitPhotoAsync`, which reads the newest row and inserts under the collection job lock. Tests: `PostgresMemberPhotoStoreTests.TwoConcurrentPreparationsOfTheSameUnchangedBodyForOnePersonAddOneRowBecauseTheJobLockSerializesThem`, `PostgresMemberPhotoStoreTests.AChangedBodyAddsARowAndLeavesTheOldRowAndItsCapture`, `PostgresMemberPhotoStoreTests.AnUnchangedBodyAddsNoRowSoAMonthlyRecheckLeavesNoDuplicate`, `PostgresMemberPhotoStoreTests.AnEarlierBodyThatReturnsAddsARowSoTheNewestRowIsTheLivePhoto`, `PostgresMemberPhotoStoreTests.ThePreparationOfOneAttemptRunTwiceWritesOneRow`, `PostgresMemberPhotoStoreTests.TwoPeopleWithTheSameImageBytesEachGetTheirOwnRow`.
- A missing photo ends the job `Failed` with no retry, and a server error retries. A failed job is never prepared, so a 404 keeps the last good photo. Enforced by `CollectionJobLifecycle` and `JobPreparation`. Test: `CollectionJobLifecycleTests.APhotoThatIsMissingEndsTheJobFailedWithNoRetryWhileAServerErrorRetries`.

## Behavior

### Source configuration

The source sets `mode` `api`, `apiParser` `congressLegislators`, and `checkIntervalSeconds` 86400. It sets no `credential`, no `coverage`, no `minimumCongress`, and no `backfillFrom`. The file is about 1.5 MB, so the shipped source sets `maxBytes` 2,500,000. The collector contract requires `maxRequests` of at least 2, so the source sets 2.

### Executive source

The executive source sets `apiParser` `congressLegislatorsExecutive` and `checkIntervalSeconds` 86400, with the same limits on credentials, coverage, and validators as the member source. The file is about 46 KB, so the shipped source sets `maxBytes` 200,000 and `maxRequests` 2. The scheduler makes one root job with `apiResource` `executiveRoster`. Preparation follows the same hash check as for the member file. It then parses the body with `ExecutiveRosterParser` and the executive store admits it.

### Capture and admission

The scheduler makes one root job for the source URL with `apiResource` `memberRoster`. A parse failure, including a value longer than its column, blocks preparation as invalid data and writes nothing.

Preparation first asks `HasSnapshotOfCaptureAsync`. It is true when any snapshot of the same source has the same capture hash. Then preparation stops, and the capture adds only its attempt row. Otherwise preparation parses the body and `AdmitAsync` inserts the member rows and the term rows in one save.

Each daily check downloads the whole file. No ETag or conditional request is used. An unchanged file adds an attempt row and nothing else.

### Member photos

The photo source sets `mode` `api`, `apiParser` `memberPhotos`, `maxRequests` 2, `maxBytes` 500,000, `timeoutSeconds` 20, and `minDelayMilliseconds` 1000. It sets no credential and no validators. The shipped source `member-photos` is disabled, like the other `api` sources. It shares the origin `unitedstates.github.io` with the roster sources, so they share one pacing clock.

Preparation of each member roster job asks for the enabled photo source. When one exists, the member store writes one job for each configured person with a bioguide ID. The job URL is the source URL, the bioguide ID, and `.jpg`. The key is `member-photo/{sourceId}/{bioguideId}/{period}`. The period is the number of whole 30 day blocks since the Unix epoch.

With a photo source, the store takes the collection job lock first, as every other job writer does. It then writes the jobs and the snapshot in one transaction. Without a photo source, it takes no job lock and writes only the snapshot. When the roster body is known, it writes only the jobs.

Preparation of a photo job reads the bioguide ID from the job URL and finds the configured person. It checks the captured media type, the JPEG signature `FF D8 FF`, and the size in the frame header. Then `AdmitPhotoAsync` compares the capture hash with the newest row of the person. It adds a row only when the hash differs.

### Proposals

`roster propose <config.json>` reads the latest snapshot of the roster source in the configuration. It prints JSON with two lists.

- `joins`: current members whose bioguide ID no configured person holds. Each entry has a proposed `id`, the `name`, the `bioguideId`, and a note that press sources are a manual step.
- `leaves`: configured people with a bioguide ID who are not in the roster.

A person edits the configuration by hand and commits it. Exit codes are 0 for success, 1 when no snapshot exists, and 2 for invalid input or a configuration with no roster source.

## Storage and contracts

- The generated [roster schema](schema/roster.md) lists every table, column, key, and check.
- `roster_members` has one row for each member and snapshot. The key is the attempt ID and the bioguide ID. It holds the Senate `lis` ID, the official full name, and the office, phone, and URL of the last term.
- `roster_terms` has one row for each term. The key is the attempt ID, the bioguide ID, and the term index. It holds the chamber, the start and end dates, the state, the district, the Senate class, the party, and the caucus. The caucus is null when the file sets none. It is also null in snapshots admitted before the column existed. The dev reset after the vote party change admits a new snapshot.
- `roster_snapshots` has one row for each admitted snapshot. The key is the attempt ID, which references `collection_attempts`. It holds nothing else. The source comes from the joined attempt. The member rows and the executive official rows reference this header row, and the term rows reference their official rows. A snapshot is the header row and the rows of its attempt. The reader rule is in [Decisions](#decisions).
- `executive_officials` has one row for each official and snapshot. The key is the attempt ID and the GovTrack ID. It holds the Wikidata ID, which can be null, and the full name.
- `executive_terms` has one row for each term. The key is the attempt ID, the GovTrack ID, and the term index. It holds the office, the start and end dates, and the party.
- `official_photos` has one row for each stored photo. The key is the attempt ID, which references `collection_attempts`. It holds the configured person ID, the source URL, and the credit text. The bytes stay in the capture that the attempt names. The newest row of a person, by the observation time of its attempt, is the live photo.
- `IMemberRosterStore` and `IExecutiveRosterStore` are the ports. Both have `ReadLatestAsync`. `PostgresMemberRosterStore` and `PostgresExecutiveRosterStore` implement them. Both use `RosterSnapshotRules`. `IMemberRosterStore` also has `ReadSenatorLisIdsAsync`, which reads every snapshot of the source. It also has `AdmitPhotoJobsAsync` and `AdmitPhotoAsync` for the photo jobs and the photo rows. `ReadNewestPhotosAsync` reads the newest row of each named person, with the capture hash, in one query with no lock. Publish calls it once. Tests: `PostgresMemberPhotoStoreTests`.

## Code map

The join of a configured person to roster terms is in [publication](publication.md#release-content).

| Concern | Files |
|---|---|
| Parsing | `src/CivicLens.Application/Roster/MemberRosterParser.cs`, `src/CivicLens.Application/Roster/RosterMember.cs`, `src/CivicLens.Application/Roster/RosterTerm.cs`, `src/CivicLens.Application/Roster/ExecutiveRosterParser.cs`, `src/CivicLens.Application/Roster/ExecutiveOfficial.cs`, `src/CivicLens.Application/Roster/ExecutiveTerm.cs`, `src/CivicLens.Application/Roster/RosterWire.cs` |
| Store port and proposals | `src/CivicLens.Application/Roster/IMemberRosterStore.cs`, `src/CivicLens.Application/Roster/IExecutiveRosterStore.cs`, `src/CivicLens.Application/Roster/ProposeRosterChanges.cs`, `src/CivicLens.Application/Roster/RosterProposal.cs` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Eligibility and validation | `src/CivicLens.Application/Collection/CollectionConfiguration.cs` |
| Store and rows | `src/CivicLens.Infrastructure/Roster/PostgresMemberRosterStore.cs`, `src/CivicLens.Infrastructure/Roster/PostgresExecutiveRosterStore.cs`, `src/CivicLens.Infrastructure/Roster/RosterSnapshotRules.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterSnapshotRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterMemberRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterTermRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/ExecutiveOfficialRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/ExecutiveTermRow.cs` |
| Photo jobs and validation | `src/CivicLens.Application/Collection/MemberPhotos/MemberPhotoUrl.cs`, `src/CivicLens.Application/Collection/MemberPhotos/MemberPhotoJobKeys.cs`, `src/CivicLens.Application/Collection/MemberPhotos/MemberPhotoImage.cs`, `src/CivicLens.Application/Roster/MemberPhoto.cs`, `src/CivicLens.Application/Roster/OfficialPhoto.cs`, `src/CivicLens.Application/Collection/ConfiguredCollectionSource.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.ApiJobs.cs` |
| Photo rows | `src/CivicLens.Infrastructure/Roster/Persistence/OfficialPhotoRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/OfficialPhotoRowConfiguration.cs` |
| Command | `src/CivicLens.Host/Roster/RosterCommand.cs` |
| Sources | `config/federal-coverage.json` |

## Limits and planned work

- The roster lists current members only. A past member has no rows. The historical file is not read.
- Publish reads the dated terms of configured people only. It matches a person by bioguide ID, then by Wikidata ID. The official profile pages show them; see [publication](publication.md).
- Senate votes match members through the `lis` ID. `ReadSenatorLisIdsAsync` returns each `lis` ID that any snapshot of the source gives to a person with a Senate term. A person with two `lis` IDs is a conflict, and the vote reader blocks the draft. A senator who left before the first snapshot has no row. A new member snapshot requeues the blocked Senate drafts, so a newly seated senator matches at the next draft; see [votes](votes.md#invariants).
- Bill and vote coverage is not term aware. A member who leaves stays covered until a person edits the configuration.
- Names are not dated. The command proposes the current official name only.
- Publish reads the newest photo of each person with `ReadNewestPhotosAsync` and writes it into the release; see [publication](publication.md#release-content). Cards on the officials list do not show it yet.
- Configured executive photos are planned. They reuse the resource `memberPhoto` and its preparation. Each executive photo has its own source with the exact URL. The credit comes from `PersonConfiguration.photo`. The job match rule then compares the exact URL.
- API mode sends `Accept: application/json` on image requests too. GitHub Pages ignores it.
- Photos are never used in ads.
- A person with no row has no photo. The no-photo state is derived and never stored.
- A person does not review a photo. The source is a public domain portrait set.
- A takedown has no mechanism yet; see [R-44](../roadmap.md#later). Until then, a bad photo stays in each release that holds it, and only a new publish with a changed photo replaces it.
