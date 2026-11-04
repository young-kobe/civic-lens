# Survey: published entries backfill and proof (R-63 PR 5)

This survey is docs only. It changes no code, config, or test. All facts come from `origin/main` at `14586748`. Each line reference is `file:line` on that commit. The section "Not checked" lists what this survey could not see.

## Context that this survey takes as decided

- PR 4 (#341) writes one `published_entries` row for each record revision that a commit adds. The row holds the revision facts only.
- D4 A: a later contract field reaches existing rows through a command. The command fills NULL columns only after it compares every other column.
- D5 B: a revision that no kept folder holds gets no entry row. The later foreign key from the state table covers open ranges only.
- PR 6 adds that foreign key after PR 5 reports zero gaps on open ranges. PR 7 and PR 8 move the publish path and the other readers off the manifest.

## What the entry row holds and what it leaves out

The manifest entry is `PublishedRecordEntry` (`src/CivicLens.Publication.Contracts/PublishedRecordEntry.cs:3`). The row holds kind, record ID, revision, headline, source host, event date, bill title, link columns, and document type. The child tables hold official IDs, issue IDs, and policy area IDs in manifest order (`src/CivicLens.Infrastructure/Publication/PublishedEntries.cs:74-82`).

The row leaves out these entry fields:

- `officials[].name` (`PublishedOfficial.Name`).
- `firstPublishedAtUtc` (`PublishedRecordEntry.cs:12`). The table `published_records` owns it.
- `yeaCount`, `nayCount`, and `alignment` (`PublishedRecordEntry.cs:25-27`).
- `cosponsorIds` (`PublishedRecordEntry.cs:28`).

The manifest also has fields outside the entries: `schemaVersion`, `releaseNumber`, `publishedAtUtc`, and `withdrawals` (`src/CivicLens.Publication.Contracts/PublicationRelease.cs:5-9`). A byte rebuild needs them too.

## Owners of the left-out fields today

| Field | C# owner today | Input of the owner | Input in the database today? | Recomputable for an old release? |
|---|---|---|---|---|
| Official name | `PublishedRecordRules.MapOfficial` (`src/CivicLens.Application/Publication/PublishedRecordRules.cs:59-65`) through `PublicationCatalog.OfficialNames` | The current configuration: `ReviewOfficials.From(collection).Names` (`src/CivicLens.Host/Publication/PublicationComposition.cs:49`) | No. The configuration file is read at start. `collection_configuration_revisions` holds snapshots, but no release row names the snapshot that it used. | No. See "Fields that cannot be recomputed". |
| `firstPublishedAtUtc` | `PublicationBuild.FirstPublishedAt`, which reads `published_records` (`src/CivicLens.Infrastructure/Publication/PublishedRecords.cs:21`) | `published_records.first_published_at_utc_ticks` | Yes, after `releases first-published backfill`. That backfill copies only the active manifest (`src/CivicLens.Application/Publication/BackfillFirstPublished.cs:13-17`). | Yes for the active manifest. Not proven for older kept folders. |
| `yeaCount`, `nayCount` | `PublishedVote.ToEntry` (`src/CivicLens.Publication.Contracts/PublishedVote.cs:98-99`) from `Totals` | `VoteTally.Read` (`src/CivicLens.Application/Publication/VoteTally.cs:30-48`). House: the sum of the "Party total" fact lines. Senate: the "Count" fact line. Candidate votes: none. | Yes, in `vote_revision_facts`, for a vote revision that has its columns. Old rows need `votes revisions backfill`. | Yes. The fact lines are immutable revision rows. |
| `alignment` | `PublishedVote.AlignmentOfParties` (`PublishedVote.cs:103-107`) | Kind, totals, and `PartyMajorities`. `VoteTally.Read` groups member lines by `VoteParties.PartyOn` (`src/CivicLens.Application/Publication/VoteParties.cs:137-140`). That uses the newest roster snapshot at build time (`PublishRelease.cs:79`, `PublicationOfficials.cs:19-22`, `PostgresMemberRosterStore.cs:92-102`). A member outside the roster takes the letter on the member line in the extraction text. | Partly. Roster snapshots are append-only rows, and extractions are immutable. No release row names the roster snapshot that it used. | Not provably. See below. |
| `cosponsorIds` | `PublishedAction.CosponsorIdsOfEntry` (`src/CivicLens.Publication.Contracts/PublishedAction.cs:47-53`) | The sponsorships of the published action record: role, `withdrawnOn`, and official ID | Yes, in `action_revision_sponsorships`, for new action revisions only. Old action revisions hold only `revision_json`. The column backfill is planned (`docs/architecture/actions.md:319`). | Yes, after the action column backfill. Before it, only from `revision_json` or the record file. |
| `publishedAtUtc` | `PublishRelease.StageAsync` sets it to the build clock (`src/CivicLens.Application/Publication/PublishRelease.cs:187`) | The application clock at build time | No. `publication_releases.published_at_utc_ticks` holds the commit clock (`src/CivicLens.Infrastructure/Publication/PostgresPublicationStore.cs:189`). The doc says that the two can differ (`docs/architecture/publication.md:109`). | No. |
| `withdrawals` | `PublishRelease.StageAsync` (`PublishRelease.cs:189-190`) | The withdrawal list and corrections | Yes, but only for releases committed after the migration `PublicationWithdrawals` (`docs/architecture/publication.md:81`). | Only for releases after that migration. |
| Record order | `ReleaseRecordOrder.NewestFirst` (`PublishRelease.cs:188`) | Event day, `firstPublishedAtUtc`, record ID | Yes, if the two inputs above are equal. | Yes. |

A second owner already exists for the vote counts. The view `public_api.votes` counts the member casts in `vote_member_list_entries` (`src/CivicLens.Infrastructure/Collection/Migrations/20261103234036_PublicApiVoteFacts.cs:20-34`). `VoteTally` takes the totals from the fact lines and refuses member lines that disagree (`VoteTally.cs:40-41`). The view gives null when a cast is not one of six words. The manifest gives null for a candidate vote. These two rules probably agree, but no test compares them. Not checked.

### Fields that cannot be recomputed now

- **Official name.** A carried entry keeps the name from the build that first added the revision. A plain publish copies carried entries as they are (`docs/architecture/publication.md:239`). A name changed in configuration after that build gives a different name now. An official who left the configuration makes `MapOfficial` throw (`PublishedRecordRules.cs:64`). So the current owner cannot rebuild that entry at all.
- **Alignment.** The party of a member comes from the newest roster snapshot at build time. A later snapshot can change a term, a caucus, or an end date. Then `PartyOn` gives another party, and the alignment can change. A member who left the roster falls back to the line letter. No release row names its roster snapshot, so a recompute cannot pick the same input.
- **`publishedAtUtc`.** The build clock is not stored.
- **`withdrawals` of a release before `PublicationWithdrawals`.** The rows do not exist.
- **`cosponsorIds` and the vote facts of an entry from before those fields.** The file holds no value for them. A rebuild from owners gives a value. Since `PublicationProtocol.JsonOptions` writes nulls (`src/CivicLens.Publication.Contracts/PublicationProtocol.cs:75-82`), a release written after the fields existed holds `null` keys. A rebuild from owners gives `[]` or numbers. The bytes then differ.

## Question 1: can PR 5 rebuild the full bytes from tables plus owners?

No. Three inputs are not in the database for every kept release. They are the build clock `publishedAtUtc`, the configuration names at build time, and the roster snapshot behind each alignment. Two more inputs exist only after other backfills: action sponsorship columns for `cosponsorIds`, and withdrawal rows for releases before `PublicationWithdrawals`. The database can recompute the yea and nay counts, the first times, the order, and the revision facts. This needs the vote column backfill and the first-published backfill.

## Question 2: what the PR 5 proof should compare

Precedents checked:

- `votes revisions verify` rebuilds each revision from rows and compares trees, with one named exception (`docs/architecture/votes.md:150`). `RevisionBackfill.VerifyAsync` counts checked, legacy, and mismatched rows and lists the first mismatches (`src/CivicLens.Infrastructure/Review/RevisionBackfill.cs:37-56`). `OwnerCommand.VerifyRevisionsAsync` exits 1 on any mismatch or legacy row (`src/CivicLens.Host/OwnerCommand.cs:29-34`).
- `PublishedReleaseOutlivesDatabaseTests` compares the rows with the manifest entry with the names blanked (`tests/CivicLens.Tests/Host/PublishedReleaseOutlivesDatabaseTests.cs:64-66`).
- `ReleaseEntryRewrite.RequireSameOutsideNewFields` compares an entry with `ToEntry()` of its record file, without the four new fields (`src/CivicLens.Application/Publication/ReleaseEntryRewrite.cs:46-52`).
- `PublishedEntries.RequireSameAsync` compares the stored facts of a key with a new entry (`PublishedEntries.cs:95-105`).
- `PublishReleaseTests.PrecompressedManifestsHoldExactlyTheManifestBytesSoTheServerNeverSendsADifferentRelease` is the only byte equality precedent for the manifest.

### Option (a): full bytes from tables plus owners

Rebuild `release.json` for each kept folder from tables and the current owners. Compare bytes.

- Proves: the PR 7 writer can produce the published file with no file read.
- Cannot prove: anything for a folder whose names, alignment, or `publishedAtUtc` the owners cannot recompute. Those folders always fail. The fail does not show a table defect.
- Cost: high. It needs the vote builder inputs, the action sponsorship columns, the configuration, and the roster per release. It also needs a stored `publishedAtUtc` or a per-release clock column. Both are schema changes outside PR 5.
- Precedent: none for a rebuilt manifest. Only the precompressed bytes test.

### Option (b): revision facts field by field, plus a separate check of computed fields

For each kept folder, compare each manifest entry with its row and child rows, field by field. Separately, compute the left-out fields from their owners and count the differences by field.

- Proves: every row equals the published facts in every kept folder. The computed-field report shows how often a view would differ from the published value.
- Cannot prove: that a rebuilt file has the same bytes. It does not prove the order, the top-level fields, or the serializer.
- Cost: low. One manifest read per folder and one batched row read per folder. The computed-field check reads vote facts and action sponsorships in batches.
- Precedent: `votes revisions verify` and the "except the names" test.

### Option (c): spliced bytes

For each kept folder, build a `PublicationRelease` from the tables. Membership comes from `publication_record_states` at that release, and facts come from the entry rows. First times come from `published_records`, and withdrawals come from the withdrawal rows. Take the values that the tables leave out from the same file: names, the four optional fields, `publishedAtUtc`, and, before `PublicationWithdrawals`, the withdrawals. Order with `ReleaseRecordOrder.NewestFirst`. Serialize with `PublicationProtocol.JsonOptions`. Compare bytes.

- Proves: the tables hold every fact, the membership, the first times, and the order. It also proves that the serializer reproduces the file. A first step reads and writes the file with no change and compares bytes. That step isolates a serializer change from a table defect.
- Cannot prove: that the owners compute the left-out values. It needs (b) second half for that.
- Cost: medium. One manifest read and parse per folder, about 15 MB each (`docs/architecture/publication.md:728`). About 31 folders, read one at a time.
- Precedent: the "except the names" test, generalized to every left-out field, and the original plan's byte comparison.

### Option (d): membership only

Compare the state rows at release R with the manifest records of folder R. Compare the open and closed withdrawal rows at R with the manifest withdrawals of R.

- Proves: the state table and withdrawal tables match every kept folder. PR 6 and PR 7 depend on that.
- Cannot prove: facts.
- Cost: low.
- Precedent: the state table invariant (`docs/architecture/publication.md:69`).

### Recommendation

Use (c), which includes (d), as the blocking proof. Add the computed-field half of (b) as a report that does not block. Option (c) shows that only the left-out fields stand between the tables and the file. The report then tells the owner how big the remaining gap is. Option (a) cannot pass for old folders, so it gives no signal.

## Question 3: backfill source, order, rerun, and output

### Source and which folder wins

- Candidates: every row of `publication_record_states` with no `published_entries` row. Read them in key order: kind, record ID, revision.
- Kept folders: the rows of `publication_releases` whose folder still holds `release.json`. `IReleaseDirectory` has no list of release folders (`src/CivicLens.Application/Publication/IReleaseDirectory.cs:3-13`). `FileReleaseDirectory.ReadFileAsync` throws `FileNotFoundException` for a missing file (`src/CivicLens.Infrastructure/Publication/ReleasePaths.cs:73`). So the command reads each release row and treats a missing manifest as a retired folder.
- Retention keeps the 30 highest numbers, the newest release of each of the last 7 days, the active row, and the `current` target (`docs/architecture/publication.md:144`). The highest committed release is always kept. Every open range is in the highest release. So every open range has a kept folder, unless a file is missing or invalid.
- Which folder wins: the oldest kept folder whose release lies in the range of the state row. It is nearest to the commit that added the revision. A plain publish copies carried entries as they are. Only `--rewrite-entries` changes an entry, and it changes only the four new fields (`docs/architecture/publication.md:239`). So every kept folder in the range holds the same revision facts. The command still compares the entry in each later kept folder of the range. A difference between folders is a finding and blocks the row.
- Record file check: read `records/<id>.json` of the winning folder. Run `Validate()` and `ToEntry()` for its kind. Compare with the manifest entry by the rule of `RequireSameOutsideNewFields`. `ReleaseEntryRewrite` applies this to votes and actions only (`ReleaseEntryRewrite.cs:23-28`). Every kind has `ToEntry()` (`git grep "ToEntry() =>"` finds seven), so the rule can cover all seven kinds.

### Withdrawn records

- A withdrawal has no record file and no entry in `records` (`docs/architecture/publication.md:56`). It never yields an entry row.
- A withdrawn revision has a closed state range. If a kept folder in that range lists it in `records`, the backfill fills it from that folder. Otherwise D5 B gives no row.
- A returned record has a new, higher revision and a new open range. It is an ordinary candidate.
- The withdrawal list serves as a check. A record ID in `withdrawals` of folder R must have no state row that covers R. A record in `records` of R must not be in `withdrawals` of R. `PublicationRelease.Validate` already refuses the second case (`PublicationRelease.cs:19-22`).

### Rerunnable and bounded

Follow `BackfillRevisions` and `RevisionBackfill`:

- `releases entries backfill <limit>`. The limit counts inserted rows, as in `BackfillRevisions.ExecuteAsync` (`src/CivicLens.Application/Review/BackfillRevisions.cs:10-28`). Batch size 25, as there (`BackfillRevisions.cs:7`).
- Each batch is one short transaction. It writes through `PublishedEntries.InsertAsync` (`PublishedEntries.cs:27`), the only writer that `PublishedEntriesInsertOnlyTests` allows. `ON CONFLICT DO NOTHING` (`PublishedEntries.cs:54`) and `RequireSameAsync` (`PublishedEntries.cs:36`) make a rerun and a race with a commit safe.
- The command compares before it inserts, so a mismatch is a skip with a reason, as in `RevisionBackfill.TryMoveAsync` (`RevisionBackfill.cs:62-80`). It never fixes a value.
- The run walks to the end or to the limit, so a skipped row never stalls a run (`docs/architecture/votes.md:149`).
- Manifest reads: read each kept manifest once per run, one at a time. Walk folders from oldest to newest. In each folder, take the candidates whose range covers it. This keeps memory to one manifest and gives "oldest kept folder wins" for free.
- A relationship row needs `published_records` rows for its statement and target (`PublishedEntries.cs:59-72`). A missing row is a skip with its own reason, not a crash.

### Output

Print one JSON object through `CommandOutput.Write`, as the other backfills do (`OwnerCommand.cs:21-27`):

- `examined`, `added`, `alreadyPresent`, `skipped`.
- `skippedByReason`, for example `recordFileInvalid`, `recordFileDiffers`, `foldersDiffer`, `firstPublishedMissing`, `manifestInvalid`.
- The first 50 skips by kind, record ID, revision, and reason (`BackfillRevisions.cs:8`).
- `openRangesWithoutRow` and `closedRangesWithoutKeptFolder`. PR 6 waits for the first to be zero. The second is expected under D5 B.
- Exit 0 only when `skipped` and `openRangesWithoutRow` are zero. Else exit 1.

### Operator allow list

Add `releases entries backfill <count>` and `releases entries verify` to `deploy/scripts/operator-command.sh`, beside `releases first-published backfill` (`deploy/scripts/operator-command.sh:99-100`). Use the `number` guard (`operator-command.sh:18`). Also update the list in `docs/operations.md:1235`. No test covers the script; `git grep operator-command` finds only `deploy/scripts/install-operator-access.sh:30`.

## Question 4: readers and writers that PR 5 touches

### Writers

- `PublishedEntries.InsertAsync` (`src/CivicLens.Infrastructure/Publication/PublishedEntries.cs:27-37`): the only entry writer. Reused by the backfill. Its conflict message says "Rebuild the release" (`PublishedEntries.cs:102-103`). That text is wrong for a backfill caller, so the backfill compares first.
- `PostgresPublicationStore.CommitAsync` (`src/CivicLens.Infrastructure/Publication/PostgresPublicationStore.cs:166-199`): calls `PublishedEntries.InsertAsync` at line 198, under the review locks taken at line 174. PR 5 does not change it.
- `PublishedRecords.InsertAsync` (`PublishedRecords.cs:32`): not called by the backfill. A missing first time is a skip.

### Readers

- `PreviousRelease.LoadManifestAsync(releases, directoryName, ...)` (`src/CivicLens.Application/Publication/PreviousRelease.cs:50-58`): reads and validates any folder's manifest. Reused.
- `ReleaseRecordFiles.ReadAsync` (`src/CivicLens.Application/Publication/ReleaseRecordFiles.cs:9-18`): reads from `previous.DirectoryName` only. The backfill needs any folder, so it calls `IReleaseDirectory.ReadFileAsync` with `PublishedRecordFiles.MaximumCitedRecordBytes` (`src/CivicLens.Application/Publication/PublishedRecordFiles.cs:17`).
- `PublishedEntries.ReadFactsAsync` (`PublishedEntries.cs:113-130`): private. The verify command needs a production reader of rows. It must live in `PublishedEntries.cs`, because `PublishedEntriesInsertOnlyTests` allows no other file to name the tables.
- `PublishedRecords.ReadAsync` (`PublishedRecords.cs:21`): first times for the spliced rebuild.
- `PublicationRecordStates` (`src/CivicLens.Infrastructure/Publication/PublicationRecordStates.cs:8`): has a writer only (`RecordAsync`, line 21). The backfill needs a bounded read of ranges without an entry row.
- `PublicationWithdrawalRows` (`src/CivicLens.Infrastructure/Publication/PublicationWithdrawalRows.cs:9`): writer only. The verify needs a read of withdrawals at release R.
- `IPublicationStore` (`src/CivicLens.Application/Publication/IPublicationStore.cs:5-30`): new members for the batch write and the reads. `ListAsync` (line 10) lists release rows.
- `ReleaseEntryRewrite.RequireSameOutsideNewFields` and `WithoutNewFields` (`ReleaseEntryRewrite.cs:46-52`): private. The backfill needs the same rule.

### Commands, composition, and docs

- `ReleaseCommand.ExecuteAsync` (`src/CivicLens.Host/Publication/ReleaseCommand.cs:19-55`): new cases beside line 43, the usage text at line 47, and the handled exceptions at line 51.
- `Program.cs` composition (`src/CivicLens.Host/Program.cs:414-417`) and the usage list (`Program.cs:110-112`). That list already lacks `releases first-published backfill`.
- `deploy/scripts/operator-command.sh:99-100`.
- `docs/architecture/publication.md:74`, `:619`, and `:659-660`: these say that no backfill fills the tables.
- `docs/operations.md:875` area: a new runbook section, and the list at line 1235.

### Members that would lose their last caller

- `ReleaseEntryRewrite.RequireSameOutsideNewFields` and `WithoutNewFields` (`ReleaseEntryRewrite.cs:46-52`) lose their place if the rule moves to a shared owner. Delete them there, so one owner remains.
- `tests/CivicLens.Tests/Host/PublishedEntryRows.cs:6` is a test copy of the row reader. A production reader makes it a second owner of the mapping. Replace its use in `PublishedReleaseOutlivesDatabaseTests.cs:57` and delete it.
- No production member loses its last caller otherwise. `BackfillFirstPublished` stays, because the entry backfill depends on it.

## Question 5: PR plan

Recommendation: split PR 5 in two. Each part has one command and one runbook step.

### PR 5a: entries backfill

- Move the "same outside the new fields" rule to one owner, for example an internal `ReleaseEntryComparison` in Application/Publication. `ReleaseEntryRewrite` and the backfill both call it.
- Add `BackfillPublishedEntries` in Application/Publication, a store batch method, and a bounded read of open and closed ranges without rows.
- Add `releases entries backfill <limit>`, the allow list line, and the runbook.
- Update the publication doc invariants and limits.

Tests that would fail if the rule broke:

- `ABackfillFillsAnEarlierRevisionFromTheOldestKeptFolderAndTheRowEqualsTheRowThatACommitWrites`.
- `AManifestEntryThatDiffersFromItsRecordFileOutsideTheNewFieldsIsSkippedWithItsReasonAndNoRowIsWritten`.
- `TwoKeptFoldersThatHoldOneRevisionWithDifferentFactsBlockTheRowBecauseTheBackfillNeverPicksAValue`.
- `ARerunAddsNothingAndCountsTheRowsAlreadyPresent`.
- `ALimitBoundsTheInsertedRowsAndAFailureInALaterBatchKeepsTheEarlierBatch`.
- `ARevisionThatOnlyARetiredFolderHeldGetsNoRowAndCountsAsAClosedRangeWithoutAKeptFolder`.
- `AnOpenRangeWithoutAKeptEntryIsReportedAndTheCommandExitsWithOne`.
- `AWithdrawalNeverYieldsAnEntryRowAndAReturnedRevisionIsFilledFromItsFolder`.
- `ARelationshipWhoseTargetHasNoFirstPublicationTimeIsSkippedWithItsReason`.
- `OnlyTheOwnerMayRunTheEntriesBackfill`.
- `PublishedEntriesInsertOnlyTests` must still pass unchanged.
- `PublishEntryRewriteTests` must still pass after the rule moves.

### PR 5b: entries verify

- Add a production row reader in `PublishedEntries.cs`, reads of state ranges and withdrawals at release R, and `VerifyPublishedEntries`.
- Add `releases entries verify`: the round-trip check, the spliced byte check (c), the membership check (d), and the computed-field report (b). It writes nothing.
- Replace the test helper `PublishedEntryRows` with the production reader.

Tests that would fail if the rule broke:

- `VerifyReportsNoDifferenceForAReleaseThatTheTablesHoldExactly`.
- `VerifyReportsTheFolderAndTheRecordWhenOneEntryFactDiffersFromItsRow`.
- `VerifyReportsAStateRowThatTheManifestOfItsReleaseDoesNotList`.
- `VerifyReportsAManifestThatDoesNotRoundTripBeforeItComparesTheTables`.
- `TheComputedFieldReportCountsAVoteWhoseAlignmentDiffersWithoutFailingTheCommand`.
- `VerifyWritesNoRow`.
- `PublishedReleaseOutlivesDatabaseTests.PublishedReleaseServesFromFilesAloneAfterTheDatabaseIsGone`, with the production reader.

## Question 6: open owner decisions

### Decision 1: the blocking proof

- Problem: "rebuild release.json from tables and compare bytes" cannot pass for old folders, because some inputs are not stored.
- Options: (a) full bytes from owners; (b) facts field by field plus a computed-field report; (c) spliced bytes plus membership; (d) membership only.
- Tradeoffs: (a) gives no signal for old folders. (b) is cheap but skips order and serializer. (c) costs one manifest parse per folder and proves all but the left-out values. (d) proves no fact.
- Precedent: `votes revisions verify`, the "except the names" test, and the precompressed bytes test.
- Recommendation: (c) as the gate, with the report from (b).

### Decision 2: which kept folder wins, and folder disagreement

- Problem: one revision can sit in up to about 31 kept folders.
- Options: the oldest kept folder; the newest kept folder; the oldest, and compare all others in its range.
- Tradeoffs: the oldest is nearest the commit. The newest has the latest `--rewrite-entries` values, which the row does not hold anyway. Comparing all costs no extra file read, because each manifest is read once.
- Precedent: `PublishedEntries.RequireSameAsync` refuses one key with two fact sets.
- Recommendation: the oldest, and block the row when any later folder in its range differs.

### Decision 3: a mismatch stops the run or skips the row

- Problem: an entry can differ from its record file or from another folder.
- Options: stop the run, as `ReleaseEntryRewrite` does (`ReleaseEntryRewrite.cs:49`); skip and report, as `RevisionBackfill` does.
- Tradeoffs: a stop protects nothing more here, because the backfill writes only proven rows. A skip lets one bad folder not block thousands of good rows.
- Precedent: `RevisionBackfill`, `BackfillRevisions`.
- Recommendation: skip and report, and exit 1.

### Decision 4: the lock and the worker during the backfill

- Problem: a commit inserts entry rows under the six review locks (`PostgresPublicationStore.cs:174`). The backfill inserts the same tables.
- Options: no advisory lock, with `ON CONFLICT DO NOTHING` and `RequireSameAsync` as the guard; `ReviewKeysBeforeBriefs` per batch; or a stopped worker during the run. The first-published runbook stops the worker (`docs/operations.md:887`).
- Tradeoffs: no lock is safe for duplicates, but a commit can add a row between the candidate read and the insert. The equal-facts check then accepts it. The full lock set blocks reviews for each batch. Stopping the worker is simple but manual.
- Precedent: `RevisionBackfill` takes the lock of its kind only (`RevisionBackfill.cs:27`).
- Recommendation: no advisory lock, rely on the insert rules, and add a race test.

### Decision 5: where the comparison rule lives

- Problem: the "same outside the new fields" rule is private to `ReleaseEntryRewrite`, and the backfill needs it.
- Options: an internal Application class that both call; a method on `PublishedRecordEntry` in the contract; a copy in the backfill.
- Tradeoffs: the contract project is the public wire contract, and the four field names are a migration detail. A copy breaks "one owner".
- Precedent: `PublishedRecordRules` holds shared publish rules in Application.
- Recommendation: an internal Application class.

### Decision 6: the left-out values for PR 7 (new evidence on "revision facts only")

- Problem: the survey found that the publish-time alignment depends on an unpinned roster snapshot. Names depend on the configuration at first build. `publishedAtUtc` is the build clock.
- Options:
  - Keep "revision facts only" and let PR 7 recompute. Old entries may then change name or alignment on the next publish.
  - Pin the inputs. For example, store the roster snapshot ID and the configuration revision ID on `publication_releases`.
  - Copy the four values and the names from the previous manifest. PR 7 wants to avoid that read.
- Tradeoffs: recompute is simple but changes published bytes of carried entries without a person. Pinning adds two columns and keeps recompute exact for new releases only. Copying keeps a manifest read.
- Precedent: "A bill moves after approval, and a record revision stays frozen" (`docs/architecture/publication.md:40`) keeps changing data out of the frozen record.
- Recommendation: the owner decides. This survey does not reopen the choice. It reports the evidence so that PR 7 does not assume that recompute is exact.

### Decision 7: two owners of the vote counts

- Problem: `public_api.votes` counts member casts. The manifest takes the fact line totals.
- Options: keep both and add a test that they agree on fixtures; make the view read fact lines; make the manifest count casts.
- Tradeoffs: a test is cheap and keeps both. Changing either rule changes a published value.
- Precedent: `PublicApiViewsTests` already compares view labels with `PublishedRecordLabels`.
- Recommendation: add the agreement test in PR 5b's computed-field report scope, and decide the owner before PR 8.

### Decision 8: order of other backfills

- Problem: the spliced check needs withdrawal rows, and the computed-field report needs vote and action columns.
- Options: run PR 5 after the vote and action column backfills in production; run it now and report "legacy" counts.
- Tradeoffs: waiting delays PR 6. Reporting legacy counts follows `VerifyRevisions`, which reports a legacy count (`RevisionBackfill.cs:47`).
- Precedent: `VerifyRevisions`.
- Recommendation: run now, and report legacy counts separately. They do not block PR 6, because PR 6 needs only open range rows.

## Not checked

- Production data: the number of kept folders, their code versions, and whether any kept manifest does not round trip under the current serializer.
- Whether `votes revisions backfill` and `executive-actions revisions backfill` have run in production.
- Whether any kept folder predates `PublicationWithdrawals`, or holds a `firstPublishedAtUtc` that differs from `published_records`.
- Whether `public_api.votes` and `VoteTally` agree on every production vote.
- Whether the configuration that a publish used is always stored in `collection_configuration_revisions`.
- The brief gives the retention rule "newest 30 plus the active one". The code also keeps the newest release of each of the last 7 days (`docs/architecture/publication.md:144`). The survey uses the code rule.
