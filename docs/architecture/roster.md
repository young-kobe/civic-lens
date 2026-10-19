# Roster

## Purpose

The roster feature captures a public member dataset once a day and keeps it as tables. The tables hold current members of Congress with their offices and dated terms. Each row cites the capture it came from. A Host command compares the latest snapshot with the configured people and prints the people to add and the people to review. The roster proposes. A person edits the configuration.

## Decisions

- The dataset is `legislators-current.json` of the `unitedstates/congress-legislators` project. Its data has a CC0 license. The project publishes the file on GitHub Pages, so it needs no credential.
- The source is an `api` source with `apiParser` set to `congressLegislators`. It checks once a day. The shipped source `congress-members` is disabled, like the other `api` sources.
- Only the current file is read. The historical file is out of scope. The 10 MB capture read limit stays as it is.
- A roster source covers no people. It is still eligible on every date, because the file has no coverage list. `CollectionConfiguration.IsActiveOn` owns this rule for admission and for scheduled checks.
- A snapshot is one set of member rows and term rows. Every row holds the attempt ID of its capture. A row is never updated.
- A capture with the same body hash as the latest snapshot writes no new snapshot. Readers use the latest snapshot of the source.
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
- A snapshot is complete or absent. Enforced by `MemberRosterParser` and one transaction in `PostgresMemberRosterStore.AdmitAsync`. Test: `MemberRosterParserTests.AnIncompleteRosterIsRejectedSoNoPartialSnapshotIsStored`.
- A capture with an unchanged body writes nothing. Enforced by `PostgresMemberRosterStore.AdmitAsync`. Test: `PostgresMemberRosterStoreTests.ACaptureWithTheSameBodyHashAsTheLatestSnapshotWritesNothingNew`.
- An admitted attempt is admitted once. Enforced by `PostgresMemberRosterStore.AdmitAsync` and the keys of both tables. Test: `PostgresMemberRosterStoreTests.AdmittingTheSameAttemptAgainIsHarmlessSoAReplayedJobKeepsOneSnapshot`.
- Old snapshots stay when a changed capture arrives. Enforced by the insert-only store. Test: `PostgresMemberRosterStoreTests.AChangedCaptureAppendsASnapshotAndReadersTakeTheLatestWhileTheOldRowsStay`.
- A term has a chamber `sen` or `rep` and an end date that is not before its start. Enforced by `ck_roster_terms_chamber`, `ck_roster_terms_dates` and `MemberRosterParser`. Test: `MemberRosterParserTests.AnIncompleteRosterIsRejectedSoNoPartialSnapshotIsStored`.
- An empty snapshot never reads as every person leaving. Enforced by `ProposeRosterChanges`. Tests: `ProposeRosterChangesTests.NoSnapshotYieldsNoProposalSoAnEmptyRosterNeverReadsAsEveryoneLeaving`, `RosterCommandTests.ProposeWithoutASnapshotFailsInsteadOfProposingEveryoneAsLeaving`.
- The propose command writes nothing. Enforced by `ProposeRosterChanges`, which has only a read port. Test: `RosterCommandTests.ProposeListsNewMembersWithAManualPressStepAndConfiguredMembersWhoLeftAndWritesNothing`.

## Behavior

### Source configuration

The source sets `mode` `api`, `apiParser` `congressLegislators`, and `checkIntervalSeconds` 86400. It sets no `credential`, no `coverage`, no `minimumCongress`, and no `backfillFrom`. The file is about 1.5 MB, so the shipped source sets `maxBytes` 2,500,000. The collector needs `maxRequests` of at least 2.

### Capture and admission

The scheduler makes one root job for the source URL with `apiResource` `memberRoster`. Preparation parses the capture with `MemberRosterParser` and gives the members to `IMemberRosterStore.AdmitAsync` with the attempt ID. A parse failure fails preparation and writes nothing.

`AdmitAsync` runs in one transaction. It reads the attempt and its capture hash. It returns when the attempt already has rows. It also returns when the latest snapshot of the same source has the same capture hash. Otherwise it inserts the member rows and the term rows.

A conditional request that gets a 304 has no captured body and no job output. The shipped source sets no validators, so each check fetches the file.

### Proposals

`roster propose <config.json>` reads the latest snapshot of the roster source in the configuration. It prints JSON with two lists.

- `joins`: current members whose bioguide ID no configured person holds. Each entry has a proposed `id`, the `name`, the `bioguideId`, and a note that press sources are a manual step.
- `leaves`: configured people with a bioguide ID who are not in the roster.

A person edits the configuration by hand and commits it. Exit codes are 0 for success, 1 when no snapshot exists, and 2 for invalid input or a configuration with no roster source.

## Storage and contracts

- The generated [roster schema](schema/roster.md) lists every table, column, key, and check.
- `roster_members` has one row for each member and snapshot. The key is the attempt ID and the bioguide ID. It holds the Senate `lis` ID, the official full name, and the office, phone, and URL of the last term.
- `roster_terms` has one row for each term. The key is the attempt ID, the bioguide ID, and the term index. It holds the chamber, the start and end dates, the state, the district, the Senate class, and the party.
- Both tables reference `collection_attempts`. The latest snapshot of a source is the newest attempt of that source that has member rows.
- `IMemberRosterStore` is the port. `PostgresMemberRosterStore` implements it.

## Code map

| Concern | Files |
|---|---|
| Parsing | `src/CivicLens.Application/Roster/MemberRosterParser.cs`, `src/CivicLens.Application/Roster/RosterMember.cs`, `src/CivicLens.Application/Roster/RosterTerm.cs` |
| Store port and proposals | `src/CivicLens.Application/Roster/IMemberRosterStore.cs`, `src/CivicLens.Application/Roster/ProposeRosterChanges.cs`, `src/CivicLens.Application/Roster/RosterProposal.cs` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Eligibility and validation | `src/CivicLens.Application/Collection/CollectionConfiguration.cs` |
| Store and rows | `src/CivicLens.Infrastructure/Roster/PostgresMemberRosterStore.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterMemberRow.cs`, `src/CivicLens.Infrastructure/Roster/Persistence/RosterTermRow.cs` |
| Migration | `src/CivicLens.Infrastructure/Collection/Migrations/20261106120000_RosterSnapshots.cs` |
| Command | `src/CivicLens.Host/Roster/RosterCommand.cs` |
| Source | `config/federal-coverage.json` |

## Limits and planned work

- The roster lists current members only. A past member has no rows. The historical file is not read.
- Nothing reads offices and dated terms yet. Official pages ([R-36](../roadmap.md#next)) will show them.
- Senate votes ([R-42](../roadmap.md#next)) will match members through the `lis` ID.
- Bill and vote coverage is not term aware. A member who leaves stays covered until a person edits the configuration.
- Names are not dated. The command proposes the current official name only.
