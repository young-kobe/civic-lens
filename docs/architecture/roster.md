# Roster

## Purpose

The roster feature captures a public member dataset once a day and keeps it as tables. The tables hold current members of Congress with their offices and dated terms. Each row cites the capture it came from. A Host command compares the latest snapshot with the configured people and prints the people to add and the people to review. The roster proposes. A person edits the configuration.

## Decisions

- The dataset is `legislators-current.json` of the `unitedstates/congress-legislators` project. Its data has a CC0 license. The project publishes the file on GitHub Pages, so it needs no credential.
- The source is an `api` source with `apiParser` set to `congressLegislators`. It checks once a day. The shipped source `congress-members` is disabled, like the other `api` sources.
- Only the current file is read. The historical file is out of scope. The 10 MB capture read limit stays as it is.
- A roster source covers no people. It is still eligible on every date, because the file has no coverage list. `CollectionConfiguration.IsActiveOn` owns this rule for admission and for scheduled checks.
- A snapshot is one header row in `roster_snapshots`, with the member rows and term rows below it. The header holds the attempt ID of the capture and the source ID. Every row holds that attempt ID. A row is never updated.
- The header table is the one owner of the snapshot rules. The hash rule and the reader rule below read the headers of one source.
- The daily check is the body hash. The source downloads the file each day, with no ETag and no conditional request. `CollectionConfiguration` enforces this. A capture whose hash matches any snapshot of the source adds only its attempt row. Preparation checks the hash before it parses.
- Readers take the newest captured attempt of the source whose hash matches some snapshot. They read the oldest snapshot with that hash. A file that changes back to an earlier body therefore reads as that earlier snapshot.
- Admission takes no lock. Two concurrent captures of the same new body can both write a snapshot. The duplicate is harmless, because readers pick one snapshot by a fixed order.
- Dates are `date` columns, because the dataset gives calendar dates.
- Rows cite the capture through the attempt. They have no text span and no extraction.
- The office, phone, and URL of a member come from the last term of the member. A member with no `official_full` name gets the first and last name.
- A proposed person ID is the official name in lowercase words joined by hyphens, as in the shipped configuration. It gets the bioguide ID as a suffix when another person holds that ID.
- Press sources of a new member are a manual step. The command says so for each proposed member.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it.

- A configuration of a roster source has no credential, no coverage list, and no Congress options. Enforced by `CollectionConfiguration`. Tests: `RosterSourceConfigurationTests.ACredentialIsRejectedBecauseTheFileIsPublic`, `RosterSourceConfigurationTests.ACoverageListOrCongressOptionIsRejectedBecauseTheRosterCoversNoPeople`.
- A roster source stays eligible with no covered people. Enforced by `CollectionConfiguration.IsActiveOn`. Tests: `RosterSourceConfigurationTests.ARosterSourceCoversNoPeopleYetStaysEligibleSoTheDailyCheckRuns`, `PostgresMemberRosterStoreTests.TheDailyCheckCreatesARosterJobWithNoCredentialEvenThoughTheSourceCoversNoPeople`.
- A roster job fetches the source URL and no other URL. Enforced by `CollectionJobDefinition`. Test: `RosterSourceConfigurationTests.ARosterJobIsBoundToTheSourceUrlAndNoOtherUrl`.
- A roster value is not longer than its column. Enforced by `MemberRosterParser` with the limits of `RosterLimits`, which the row configurations also use. Test: `MemberRosterParserTests.AValueLongerThanItsColumnIsRejectedSoThePreparationBlocksAsInvalidData`.
- A snapshot is complete or absent. Enforced by one `SaveChanges` in `PostgresMemberRosterStore.AdmitAsync`. Test: `PostgresMemberRosterStoreTests.AFailedInsertLeavesNoPartialSnapshot`.
- A member row has a header row. Enforced by the foreign key `FK_roster_members_roster_snapshots_attempt_id`. Test: `PostgresMemberRosterStoreTests.AMemberRowWithoutAHeaderIsRefusedSoNoSnapshotExistsWithoutIt`.
- A snapshot of one source never satisfies the rules of another source. Enforced by `PostgresMemberRosterStore`, which reads the headers of one source. Test: `PostgresMemberRosterStoreTests.TheHeaderNamesTheSourceOfItsAttemptSoASnapshotOfAnotherSourceIsNotKnown`.
- The migration `RosterSnapshotHeaders` adds one header for each existing snapshot, so no reader needs the old shape. Test: `RosterSnapshotHeadersMigrationTests.EachExistingSnapshotGetsOneHeaderWithItsSourceSoNoReaderNeedsTheOldShape`.
- A capture whose body matches any snapshot of the source is not parsed and writes no rows. Enforced by `PostgresMemberRosterStore.HasSnapshotOfCaptureAsync` and `JobPreparation`. Tests: `PostgresMemberRosterStoreTests.ACaptureWithTheBodyOfAnEarlierSnapshotNotOnlyTheLatestIsKnown`, `JobPreparationTests.AKnownRosterCaptureIsNotParsedAndWritesNothing`.
- Readers return the same snapshot for any admission order and any duplicate snapshot. Enforced by `PostgresMemberRosterStore.ReadLatestAsync`. Tests: `PostgresMemberRosterStoreTests.ReaderFollowsTheNewestCaptureWhateverTheAdmissionOrder`, `PostgresMemberRosterStoreTests.TwoSnapshotsOfOneBodyReadAsTheOldestOne`.
- Old snapshots stay when a changed capture arrives. Enforced by the insert-only store. Test: `PostgresMemberRosterStoreTests.AChangedCaptureAppendsASnapshotAndReadersTakeTheLatestWhileTheOldRowsStay`.
- A roster source sets no ETag and no Last-Modified. A 304 would leave the attempt without a capture body and stop the daily check. Enforced by `CollectionConfiguration`. Test: `RosterSourceConfigurationTests.AValidatorIsRejectedBecauseA304WouldStopTheDailyCheck`.
- A configuration has at most one roster source. Enforced by `CollectionConfiguration`. Test: `RosterSourceConfigurationTests.ASecondRosterSourceIsRejectedBecauseProposalsReadOneSource`.
- A term has a chamber `sen` or `rep` and an end date that is not before its start. Enforced by `ck_roster_terms_chamber`, `ck_roster_terms_dates` and `MemberRosterParser`. Tests: `MemberRosterParserTests.ATermWithAnUnknownChamberIsRejected`, `MemberRosterParserTests.ATermThatEndsBeforeItStartsIsRejected`.
- An empty snapshot never reads as every person leaving. Enforced by `ProposeRosterChanges`. Tests: `ProposeRosterChangesTests.NoSnapshotYieldsNoProposalSoAnEmptyRosterNeverReadsAsEveryoneLeaving`, `RosterCommandTests.ProposeWithoutASnapshotFailsInsteadOfProposingEveryoneAsLeaving`.
- The propose command writes nothing. Enforced by `ProposeRosterChanges`, which has only a read port. Test: `RosterCommandTests.ProposeListsNewMembersWithAManualPressStepAndConfiguredMembersWhoLeftAndWritesNothing`.

## Behavior

### Source configuration

The source sets `mode` `api`, `apiParser` `congressLegislators`, and `checkIntervalSeconds` 86400. It sets no `credential`, no `coverage`, no `minimumCongress`, and no `backfillFrom`. The file is about 1.5 MB, so the shipped source sets `maxBytes` 2,500,000. The collector contract requires `maxRequests` of at least 2, so the source sets 2.

### Capture and admission

The scheduler makes one root job for the source URL with `apiResource` `memberRoster`. A parse failure, including a value longer than its column, blocks preparation as invalid data and writes nothing.

Preparation first asks `HasSnapshotOfCaptureAsync`. It is true when any snapshot of the same source has the same capture hash. Then preparation stops, and the capture adds only its attempt row. Otherwise preparation parses the body and `AdmitAsync` inserts the member rows and the term rows in one save.

Each daily check downloads the whole file. No ETag or conditional request is used. An unchanged file adds an attempt row and nothing else.

### Proposals

`roster propose <config.json>` reads the latest snapshot of the roster source in the configuration. It prints JSON with two lists.

- `joins`: current members whose bioguide ID no configured person holds. Each entry has a proposed `id`, the `name`, the `bioguideId`, and a note that press sources are a manual step.
- `leaves`: configured people with a bioguide ID who are not in the roster.

A person edits the configuration by hand and commits it. Exit codes are 0 for success, 1 when no snapshot exists, and 2 for invalid input or a configuration with no roster source.

## Storage and contracts

- The generated [roster schema](schema/roster.md) lists every table, column, key, and check.
- `roster_members` has one row for each member and snapshot. The key is the attempt ID and the bioguide ID. It holds the Senate `lis` ID, the official full name, and the office, phone, and URL of the last term.
- `roster_terms` has one row for each term. The key is the attempt ID, the bioguide ID, and the term index. It holds the chamber, the start and end dates, the state, the district, the Senate class, and the party.
- `roster_snapshots` has one row for each admitted snapshot. The key is the attempt ID, which references `collection_attempts`. It also holds the source ID. The member rows reference this header row, and the term rows reference the member rows. A snapshot is the header row and the rows of its attempt. The reader rule is in [Decisions](#decisions).
- `IMemberRosterStore` is the port. `PostgresMemberRosterStore` implements it.

## Code map

| Concern | Files |
|---|---|
| Parsing | `src/CivicLens.Application/Roster/MemberRosterParser.cs`, `src/CivicLens.Application/Roster/RosterMember.cs`, `src/CivicLens.Application/Roster/RosterTerm.cs` |
| Store port and proposals | `src/CivicLens.Application/Roster/IMemberRosterStore.cs`, `src/CivicLens.Application/Roster/ProposeRosterChanges.cs`, `src/CivicLens.Application/Roster/RosterProposal.cs` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Eligibility and validation | `src/CivicLens.Application/Collection/CollectionConfiguration.cs` |
| Store and rows | `src/CivicLens.Infrastructure/Roster/PostgresMemberRosterStore.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterSnapshotRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterMemberRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterTermRow.cs` |
| Migration | `src/CivicLens.Infrastructure/Collection/Migrations/20261106120000_RosterSnapshots.cs`, `src/CivicLens.Infrastructure/Collection/Migrations/20261107120000_RosterSnapshotHeaders.cs` |
| Command | `src/CivicLens.Host/Roster/RosterCommand.cs` |
| Source | `config/federal-coverage.json` |

## Limits and planned work

- The roster lists current members only. A past member has no rows. The historical file is not read.
- Nothing reads offices and dated terms yet. Official pages ([R-36](../roadmap.md#next)) will show them.
- Senate votes ([R-42](../roadmap.md#next)) will match members through the `lis` ID.
- Bill and vote coverage is not term aware. A member who leaves stays covered until a person edits the configuration.
- Names are not dated. The command proposes the current official name only.
