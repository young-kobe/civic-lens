# Publication

## Purpose

The publication feature turns approved revisions into immutable, cumulative static releases. It activates a release atomically. A separate static file server reads only published files.

## Decisions

- Only the owner publishes or activates a release.
- A record is published at its current revision, and only when that revision is approved and has no unresolved concerns.
- A newer draft never replaces published content automatically.
- A release is cumulative. It holds the base release records plus explicit additions or replacements.
- Carried records are hard links to the immutable files of the base release. Disk therefore grows with published records, not with the number of releases.
- Each publish rebuilds only `release.json`, the index pages, and the assets. It reads the previous manifest once, under the 32 MB manifest cap, `PublicationProtocol.MaximumManifestBytes`. `PublicationProtocol.MaximumRecordsPerRelease` is the one owner of the record limit, which is 20,000. The database has no count check, because C# owns the rule.
- Postgres records which release is active. The `current` link is only a copy for the static server, so a failed link swap never drops committed records.
- Every publication write takes all five review locks in one fixed order, which prevents a deadlock. The order is in [advisory locks](../architecture.md#advisory-locks).
- A release has no status column. `is_active` marks the one active release: a commit sets it on the new row and clears the old one, and rollback moves it.
- Rollback is a CLI command only. The workspace has no rollback or activate control.
- All releases are kept until a retention policy is agreed.
- Host renders the shared Razor components to static HTML, so publication needs no new project.
- Each manifest entry carries search filters, so search needs no record file reads. Each published record type owns the mapping in its `ToEntry` method. A filter that the kind has no fact for stays empty; publish never guesses it.
- One loader owns each record kind: its ID shape, its ready list, and how publish loads and builds it. `PublishedRecordLoaders` lists them, and the publish and ready-list handlers go through that list. One loader owns the Application side of a kind. The other per-kind places are the `PublicationProtocol.IsRecordId` arm, the store state check, `PublishedRecordLabels.Kind`, the review link, and the `IReleaseRenderer` method.
- The loader base class gives each kind one owner for its Application rules. A non-generic base exists only so `PublishedRecordLoaders` can hold all record kinds in one list. The five kinds are real variation, with different IDs, stores, and records. The precedents are `PostgresAnalysisStore<TRun>` and `ReviewRecordPage`.
- `PublishedRecordLoader<TCandidate, TAddition>` owns the ready-list and load steps once. A kind supplies only its store reads, its ready row, and how it builds and renders one record. `PublishedRecordRules.Cite` is the one owner of a published citation for every kind.
- Official names in every record kind come from the current `PublicationCatalog`, not from stored revisions. The action `policyAreaIds` filter comes from the policy area ID that the draft stored. Drafting resolved it through `IssueConfiguration.PolicyAreaIdFor`, and the reviewer approved it with the revision. Publish never reads the issue configuration. A label that the configuration lacked at draft time gives an empty filter.
- A record ID is unique across all kinds, because all kinds share the flat `records/{id}` paths.
- A brief record embeds no source text. It holds the wording that a person approved and a list of citations. Each citation names a published record by kind, ID, and revision, and carries a copy of the cited headline. A brief is a weekly summary over other records, so it publishes only when the new release holds every record that it cites; see [briefs](briefs.md#publication).
- An action record embeds the full text of each cited extraction once. Each citation names its extraction, and the contract checks the quote against that source.
- A vote record embeds the full text of each cited extraction once, as an action record does. Its citations are the vote day, the kind, the result facts, and the cast of each tracked member. `PublishedEvidenceRules` owns the source and citation checks for actions and votes.
- The manifest schema version is 3. Actions and briefs added record kinds without a version bump, because every reader ships with or after the release it reads. Versions flatten under roadmap R-23. The publisher reads releases that it or an older publisher wrote. `app.js` ships inside each release. An older publisher fails loudly on the unknown kind.
- Release JSON uses relaxed escaping (`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`), so non-ASCII letters and punctuation take UTF-8 bytes, not 6-byte `\uXXXX` escapes. This is safe because release JSON is fetched as JSON and never inlined in HTML or a script tag. Readers do not change.
- Unreviewed proposals, internal reviewer notes, operational job details, and mutation controls stay private.
- Public serving reads release contracts only. It never reads operational storage or private notes.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- Only an approved record with no open concern publishes. Enforced by `PublishedRecordRules.IsPublishable`, which each loader applies to its publish and its ready list. Tests: `PublishReleaseTests.RejectsDraftsThatAreNotCleanlyApproved`, `PublishActionTests.ActionThatIsNotApprovedIsRefusedBecauseNoModelOrDraftMaySpeakForTheSite`, and `ListPublishableTests.ApprovedActionIsListedWithItsKindAndAnActionWithAConcernIsNot`, `PublishVoteTests.AVoteWhoseRevisionNobodyApprovedIsRefusedBecauseAModelRevisionNeverPublishesAlone`, `PublishVoteTests.AVoteWithAnOpenConcernIsRefusedAndIsNotListed`, and `ListPublishableTests.ApprovedVoteIsListedWithItsKindAndAVoteWithAConcernIsNot`.
- A release holds at most `PublicationProtocol.MaximumRecordsPerRelease` records, and the manifest stays under the manifest cap at that count. Enforced by the `PublishRelease` record count check and manifest size check. Test: `PublishReleaseTests.RejectsMoreRecordsThanTheCapBeforeAnythingIsBuilt`, `PublishReleaseTests.AReleaseAtTheRecordLimitOfFilledStatementEntriesKeepsTheManifestUnderThreeQuartersOfTheManifestCap`, `PublishReleaseTests.AReleaseAtTheRecordLimitOfFilledActionEntriesKeepsTheManifestUnderThreeQuartersOfTheManifestCap`, and `PublishReleaseTests.RefusesAManifestOverTheManifestCapAndStagesNothing`.
- Manifest filters hold at most `PublishedRecordEntry.MaximumFilterValues` distinct, nonblank values, each at most `PublishedRecordEntry.MaximumFilterValueLength` characters. Enforced by `PublishedRecordEntry.Validate`, which `PublicationRelease.Validate` calls. Test: `PublicationReleaseTests.FiltersAreBoundedAndDistinctSoOneEntryCannotSwellTheManifest`.
- An entry's filters come from the revision that the release publishes. Enforced by `PublishedDocumentChange.ToEntry`, `PublishedStatement.ToEntry`, and `PublishedAction.ToEntry`. Tests: `PublishStatementTests.EachKindFillsItsSearchFiltersFromTheRevisionItPublishes`, `PublishStatementTests.ARepublishedStatementReplacesItsCarriedEntryInsteadOfDuplicatingIt`, `PublishActionTests.AnActionFillsItsSearchFiltersFromTheRevisionItPublishesAndNamesOnlyItsSponsor`, and `PublishActionTests.ARepublishedActionReplacesItsCarriedEntryInsteadOfDuplicatingIt`.
- A record ID is unique across all kinds in a manifest. Enforced by `PublicationRelease.Validate`. Tests: `PublicationReleaseTests.ARecordIdIsUniqueAcrossKindsBecauseAllKindsShareOneRecordFilePath` and `PublishActionTests.AnActionAndAStatementCannotShareOneIdBecauseTheyWouldShareOneRecordFile`.
- A record ID has the shape of its kind. Enforced by each loader's `ValidateId` for requests and by `PublicationProtocol.IsRecordId` for files. Test: `PublishStatementTests.RecordIdMustMatchTheIdShapeOfItsKindSoAKindCannotBeMislabelled`.
- Each action citation quotes the embedded source that it names, and each embedded source is cited. Enforced by `PublishedAction.Validate`. Tests: `PublishedActionTests.RejectsACitationWhoseQuoteMatchesAnotherSourceButNotTheOneItNames`, `PublishedActionTests.RejectsACitationThatNamesNoEmbeddedSource`, and `PublishedActionTests.RejectsASourceThatNoCitationNamesBecauseTheFileEmbedsOnlyCitedEvidence`.
- An action file holds 1 to `PublishedAction.MaximumSources` sources and 1 to `PublishedAction.MaximumSponsorships` sponsorships. Enforced by `PublishedAction.Validate`. Tests: `PublishedActionTests.RequiresOneToEightSourcesSoTheRecordFileStaysBounded` and `PublishedActionTests.RequiresOneToSixHundredSponsorships`.
- The contract limits for actions equal the Core limits. No enforcer; the constants are separate copies. Test: `PublishedActionTests.ContractLimitsEqualTheCoreLimits`.
- An action sponsorship is never withdrawn before it began. Enforced by `ActionDraftRevision.ValidateShape` and, at the trust boundary, `PublishedAction.Validate`. Tests: `ActionDraftRevisionTests.ASponsorshipCannotBeWithdrawnBeforeItBegan` and `PublishedActionTests.ASponsorshipCannotBeWithdrawnBeforeItBegan`.
- Each vote citation quotes the embedded source that it names, and each embedded source is cited. Enforced by `PublishedVote.Validate` through `PublishedEvidenceRules`. Tests: `PublishedVoteTests.RejectsACitationWhoseQuoteMatchesAnotherSourceButNotTheOneItNames`, `PublishedVoteTests.RejectsACitationThatDoesNotQuoteItsSourceExactly`, `PublishedVoteTests.RejectsACitationThatNamesNoEmbeddedSource`, and `PublishedVoteTests.RejectsASourceThatNoCitationNamesBecauseTheFileEmbedsOnlyCitedEvidence`.
- A published fact equals its cited quote, and a cast is the last field of its cited member line. Enforced by `PublishedVote.Validate`. Tests: `PublishedVoteTests.AFactMustEqualItsCitedQuoteSoNoFactStatesMoreThanTheSourceSays` and `PublishedVoteTests.ACastMustBeTheWordsItsCitationShowsSoNoVoteIsPublishedThatTheLineDoesNotState`.
- A published vote kind other than Other has a citation. Enforced by `PublishedVote.Validate`. Test: `PublishedVoteTests.AKindOtherThanOtherNeedsItsCitationAndOtherNeedsNone`.
- A vote file holds 1 to `PublishedVote.MaximumSources` sources and 1 to `PublishedVote.MaximumMembers` members, each official once. Enforced by `PublishedVote.Validate`. Tests: `PublishedVoteTests.RequiresOneToThreeSourcesSoTheRecordFileStaysBounded`, `PublishedVoteTests.RequiresOneToSixHundredMembers`, and `PublishedVoteTests.ARecordListsEachOfficialOnceSoTheFilterNeverRepeatsAnId`.
- The contract limits for votes equal the Core limits. No enforcer; the constants are separate copies. Test: `PublishedVoteTests.ContractLimitsEqualTheCoreLimits`.
- The vote day is the date of the cited start line, which keeps the offset of the API. Enforced by `PublishedVote.Validate`. Test: `PublishedVoteTests.TheVoteDayIsTheDateOfTheCitedStartLineSoALateVoteKeepsItsHouseDayAcrossUtcMidnight`.
- The vote page links to the action record of the bill only when the caller passes its ID. Otherwise it shows the bill as text. The loader passes an ID only when the release holds that action record, and the action loader runs before the vote loader. Enforced by `PublishedVotePage`, `VoteRecordLoader.BillActionRecordIn`, and the loader order in `PublishedRecordLoaders`. Tests: `ReleaseRendererTests.TheVotePageLinksToTheBillActionOnlyWhenTheReleaseHoldsThatAction`, `ReleaseRendererTests.AVoteWithoutABillShowsNoBillPanel`, `PublishVoteTests.TheBillLinksToTheActionRecordOfTheReleaseWhenTheActionIsPublishedEarlier`, `PublishVoteTests.TheBillLinksToAnActionAddedInTheSameReleaseBecauseActionsLoadBeforeVotes`, `PublishVoteTests.TheBillShowsAsTextWhenTheActionRecordIsNotInTheRelease`, `PublishVoteTests.TheLinkUsesTheActionThatTheReleaseHoldsWhenSeveralSourcesDraftedTheSameBill`, and `PostgresHouseVoteTests.PublicationReadReturnsTheApprovedVoteWithItsExtractionsAndTheActionRecordsOfTheSameBillOnly`.
- The vote entry names no official, has no policy area filter, and dates the vote by its day. Enforced by `PublishedVote.ToEntry`. Test: `PublishedVoteTests.TheEntryNamesNoOfficialUntilAnOfficialPageOrSearchServiceIndexesVotesAndDatesTheVoteByItsDay`.
- The action `policyAreaIds` filter holds the policy area ID that the approved revision stored. Enforced by `PublishedActionBuilder` and `PublishedAction.ToEntry`. Tests: `PublishActionTests.AnActionFillsItsSearchFiltersFromTheRevisionItPublishesAndNamesOnlyItsSponsor` and `PublishActionTests.AnActionWithoutAStoredAreaIDPublishesNoAreaFilterBecauseTheReviewerApprovedNone`.
- A brief publishes only when every citation is current in the new release. Current means that the release holds the cited kind, record, and revision, and that the latest decision on that revision is an approval. The loader checks this at build and in the ready list. The store checks it again at commit. Enforced by `BriefRecordLoader` and `PostgresPublicationStore.RequireCurrentCitationsAsync`, both through `BriefRecordReader.FindChangedAsync`. Tests: `PublishBriefTests.ABriefIsRefusedWhenTheSamePublishReplacesItsCitedRecordWithANewerRevision` and `PostgresBriefPublicationTests.TheCommitRefusesABriefWhenACitedRecordChangedBetweenTheBuildAndTheCommit`, `PublishBriefTests.ABriefIsRefusedWhenTheNewReleaseDoesNotHoldTheCitedRecordBecauseThePageWouldLinkToNothing`, `PublishBriefTests.ABriefThatIsNotApprovedIsRefusedBecauseNoModelOrDraftMaySpeakForTheSite`, and `PostgresBriefPublicationTests.APublishRefusesABriefWhoseCitedRevisionWasWithdrawnAfterTheBriefWasApproved`.
- A brief entry fills only `eventDate`, with the Monday of its week. Enforced by `PublishedBrief.ToEntry`. Tests: `PublishedBriefTests.TheEntryFillsOnlyTheWeekStartAsTheEventDate` and `PublishBriefTests.ABriefFillsOnlyItsWeekAsTheEventDateAndCopiesTheHeadlineOfEachCitedRecord`.
- A brief file holds 1 to `PublishedBrief.MaximumParagraphs` paragraphs and 1 to `PublishedBrief.MaximumCitations` citations. Each paragraph cites listed IDs, and each listed ID is cited. Enforced by `PublishedBrief.Validate`. Tests: `PublishedBriefTests.RequiresOneToTwelveParagraphsSoThePageStaysBounded`, `PublishedBriefTests.RejectsAParagraphThatCitesAnIdTheBriefDoesNotListBecauseTheLinkWouldHaveNoTarget`, and `PublishedBriefTests.RejectsAListedCitationThatNoParagraphUsesBecauseThePageWouldListARecordTheTextNeverCites`.
- The contract limits for briefs equal the Core limits. No enforcer; the constants are separate copies. Test: `PublishedBriefTests.ContractLimitsEqualTheCoreLimits`.
- A commit waits for the action review lock and the vote review lock. Enforced by `PostgresAdvisoryLock.ReviewKeysBeforeBriefs`. Tests: `PostgresPublicationStoreTests.CommitWaitsForTheActionLockBecauseAnActionDecisionMustNotSlipInDuringThePublish` and `PostgresPublicationStoreTests.CommitWaitsForTheVoteLockBecauseAVoteDecisionMustNotSlipInDuringThePublish`.
- The manifest `publishedAtUtc` and each `firstPublishedAtUtc` come from the application clock at build time. They can be earlier than the stored release time by the time that the commit waits. Only the stored release time places a release in a brief week. No enforcer. No test.
- A commit also waits for the brief review lock. Enforced by `BriefStaleness.TakeLocksAsync`. Test: `BriefStalenessTests.ThePublishWaitsForTheBriefLockLastInTheOrderBecauseAnAppendOfABriefMustNotSlipInDuringIt`.
- The stored release time is the database time of the commit, read after the commit holds the brief lock. Enforced by `PostgresPublicationStore.CommitAsync`. Tests: `PostgresPublicationStoreTests.TheReleaseTimeIsTheDatabaseTimeOfTheCommitSoABriefWeekNeverMissesARelease` and `BriefStalenessTests.TheReleaseTimeIsStampedAfterTheBriefLockSoABriefReadThatWaitedForTheCommitFindsTheRelease`.
- The precompressed manifests hold exactly the manifest bytes. Enforced by `PublishRelease`. Test: `PublishReleaseTests.PrecompressedManifestsHoldExactlyTheManifestBytesSoTheServerNeverSendsADifferentRelease`.
- A commit holds the exact revision and review state version that the build read, in the draft table of the record's kind. Enforced by `PostgresPublicationStore`. Tests: `PostgresPublicationStoreTests.ADecisionAfterTheBuildBlocksTheCommitBecauseApprovalMayHaveChanged`, `PostgresBillStreamTests.AnActionDecisionAfterTheBuildBlocksTheCommitBecauseApprovalMayHaveChanged`, `PostgresPublicationStoreTests.AStatementIsNeverCheckedAgainstTheActionTableBecauseTheKindPicksTheDraftTable`, `PostgresBriefPublicationTests.TheCommitChecksTheBriefTableForABriefAndRefusesAChangedReviewState`, and `PostgresHouseVoteTests.AVoteCommitWithAStaleReviewStateVersionIsAConflictBecauseTheKindPicksTheVoteTable`.
- The kinds share the ready list limit equally, and unused share goes to the other kinds. Enforced by `ListPublishable`. Tests: `ListPublishableTests.FourKindsShareTheLimitEquallyWhenEachHasMoreThanItsShare` and `ListPublishableTests.AKindWithFewReadyRecordsGivesItsUnusedShareToTheOtherKinds`.
- One release is active. Enforced by `IX_publication_releases_is_active`. Test: `PostgresPublicationStoreTests.EachCommitBecomesTheActiveReleaseSoAFailedLinkSwapCannotHideIt`.
- Release numbers have no gaps. Enforced by `PostgresPublicationStore`. Test: `PostgresPublicationStoreTests.ReleaseNumbersMustFollowTheLatestCommittedOneWithoutGaps`.
- A replay with another payload fails. Enforced by `PostgresPublicationStore`. Test: `PostgresPublicationStoreTests.ReplayReturnsTheOriginalOutcomeAndRejectsADifferentPayloadForTheSameKey`.
- A published release serves without the database. Enforced by the static release files. Test: `PublishedReleaseOutlivesDatabaseTests.PublishedReleaseServesFromFilesAloneAfterTheDatabaseIsGone`.

## Behavior

### Public reader experience

A record explains what happened, who was involved, and when. Where evidence supports it, it explains why the event matters. It states what the evidence can and cannot establish. Readers move from the account into retained passages and exact citations. They do not need to read collection jobs or extraction metadata first.

A document-change record shows both versions, and observation history where needed to explain gaps. A current source link supplements the retained evidence; it does not replace it. Public records show their review and version context and published corrections.

### Release content

- Each document-change record carries the full extracted text of both versions, their source URLs, and their observation times. Changed regions are offsets into those texts.
- A citation holds an extraction ID, UTF-16 offsets, and the quoted text. The contract rejects a quote that does not match its offsets.
- Records show the approval date and the first-publication date. The release index shows the release date.
- The index lists 50 records on each page, newest first. The pages are `index.html`, then `page-2.html` and so on, with relative previous and next links.
- Officials appear by their configured name. Issues appear by their configured identifier.
- Only the release manifest shows the schema version.

A release holds five kinds of record. Each manifest entry has a `kind`: `documentChange`, `statement`, `action`, `vote`, or `brief`, a lowercase camel string. All kinds share the `records/` folder, the record data path, and the record page path.

| Kind | Record ID |
|---|---|
| `documentChange` | The draft ID: 32 lowercase hex characters |
| `statement` | The candidate ID: 64 characters |
| `action` | The action record ID: 64 lowercase hex characters |
| `brief` | The brief record ID: 64 lowercase hex characters; the [briefs](briefs.md#decisions) page defines it |

A statement record carries one source version with the full extracted text, the source URL, and the observation time. It also carries:

- speakers by configured name, and other speakers
- issues, policy areas, and bill numbers
- the optional statement date with its evidence
- 1 to 32 quotations, each with its kind: `directQuotation` or `releaseText`

Every quotation must equal the source text at its offsets.

An action record carries:

- the bill identity (congress, type, and number) and the bill title
- the policy area with its citation, the policy area ID stored at draft time, and the tracked issue IDs
- the CRS summary citation, when the revision has one
- 1 to 600 sponsorships. Each has the official by configured name, the role (`sponsor` or `cosponsor`), and the original flag. It also has the sponsored date, the optional withdrawn date, and one or two citations.
- 1 to 8 sources, each with the full extracted text of one cited Congress.gov API page, its URL, and its observation time

The sources are the distinct cited extractions in citation order, so the first one is the page of the first sponsorship. Every citation must name one of the sources and quote its text exactly. A source that no citation names is rejected.

A brief record carries:

- the headline and 1 to 12 paragraphs, each with the citation IDs that it uses
- the week start (a Monday), the cutoff release number, and the coverage counts: considered, sent to the model, and cited
- 1 to 256 citations. Each has its ID, kind, record ID, revision number, and a copy of the cited headline

The approval time of a record is the latest approval of its current revision, for every kind. One Application rule owns that time.

### Publish

One publish request names 1 to 64 records, each as a kind and an ID. For each kind, publication loads all selected drafts and their verified evidence in one repeatable-read transaction. Each read uses a fixed number of queries for any selection of 1 to 64 drafts. The publisher builds records from this evidence and does not read it again.

1. Application builds the release contract, and Host renders the static HTML.
2. The owner action builds the release in a staging directory, verifies it, and moves it to a uniquely named directory under `releases/`.
3. Under all five review locks, the commit checks each added record's revision and review-state version again, in the draft table of its kind. For each added brief it also checks that every citation is current in the records of the new release. It also checks the next release number and the active release it built on. It then requeues the brief of the week of each record that the release changes, drops, or adds again; see [briefs](briefs.md#staleness).
4. In the same transaction, it records a sequentially numbered release and marks it as active. It stamps the release time from the database clock after it holds all review locks. The commit does not take a time from the caller.
5. After the commit, it takes all five review locks again, reads the active release, and atomically replaces the relative `current` symbolic link.

Publish, replay, and activate all end with step 5. An older command therefore cannot move the link back after a newer one.

The review state is checked at commit, so a concurrent blocking decision cannot be ignored. A commit that fails on a conflict or invalid input deletes its directory. If that cleanup fails, or an unknown failure occurs, the directory stays. Only committed releases can be activated.

Each publication builds on the recorded active release, not on the link. A replayed publish request and the activate command both point the link at the recorded active release. This repairs a link that is out of date. Publish requests reuse the review replay receipts.

### Rollback

Rollback records an earlier release as active and then points the link at it. It also requeues the brief of the week of each record that differs between the two releases. The next publication builds on the rolled-back release, so it does not restore the records that were rolled back. A publication built before a rollback fails on commit.

### Owner publish section

The Review home page shows a publish section to the owner only, and the handlers also enforce the owner role. The ready-to-publish read lists approved document changes, statements, actions, and briefs that the active release does not hold at their current revision. A brief whose citations are not all current in the active release is not listed. A record is a replacement when the active release holds an older revision. Otherwise it is new.

SQL selects a bounded candidate set for each kind from the review and active-release tables. Application then applies the one publishability rule in `PublishedRecordRules`, which the publish handler also uses. The kinds share the publish limit equally. A kind with fewer ready records gives its unused share to the other kinds.

The owner selects records with checkboxes. A summary step lists each headline, its kind, and the next release number. Only the confirm form calls the publish handler, with one idempotency key for each page render. The page lists recent releases read-only.

### Corrections and withdrawal

Withdrawing approval does not change published artifacts. The UI flags affected published records for an explicit correction or withdrawal release. A correction needs a new revision, an approval, a public correction note, and a new release.

### Vote record

`PublishedVote` is the contract of a vote record. `PublishedVoteBuilder` builds it from an approved `VoteDraftRevision`, the review decisions, and the cited extractions. It maps each member official through `PublishedRecordRules.MapOfficial` and copies each citation through `PublishedRecordRules.Cite`. The vote kind enum mirrors the Core `VoteKind`. `PublishVoteTests` tests the builder through `PublishRelease`, as for the other kinds. The official filter does not find votes until the official pages (R-36) or the search service (R-40) index them. `VoteRecordLoader` owns the vote kind for publish and the ready list. `PostgresVoteReviewStore.GetForPublicationAsync` reads the selected drafts, their review cycles, and the cited extractions. It also reads the IDs of the action records with the same congress, bill type, and bill number. Several bill sources can draft one bill, so there can be more than one ID. The loader passes the first ID whose action record is in the release to `RenderVoteAsync`. The release holds an action record when the base release has it or the same publish adds it. Loaders run in list order and action comes before vote, so a same-release action counts. The page links to it. Otherwise the page shows the bill as text.

### Rendering

The release builder renders public components to static HTML through `PublicPage`, `PublishedIndexPage`, and one record page for each kind: `PublishedRecordPage`, `PublishedStatementPage`, `PublishedActionPage`, `PublishedVotePage`, and `PublishedBriefPage`.

An action page shows the headline, the summary, the bill, the title, the policy area, and the tracked issues. It has a sponsorship table with each official's configured name, role, original flag, dates, and cited quotes. It shows the policy area and CRS summary citations. Each source shows its URL and observation time as plain text, with no link, and its full text with the quotes marked. Its caveat says that a sponsorship is a proposal, not a law, and that the record lists tracked officials only.

A vote page shows the headline, the summary, the vote day, the kind, the roll call, and the tracked issues. A "Bill" panel shows the bill identity as plain text. It shows a link to `records/{id}.html` instead when the caller passes the ID of an action record in the same release. The "Result" panel lists the day, the kind, and each result fact as a cited quote. The members table shows each official's configured name, cast, and cited member line. Each source shows as on an action page. The caveat says that the record lists tracked officials only. It also says that each cast is the wording of the House Clerk roll call.

A brief page shows the headline and the paragraphs, each with links to its citations. A "Cited records" panel lists each citation. Each row shows the ID, the kind, and the revision number. A link to `records/{id}.html` shows the copied headline. The facts name the week, the cutoff release, and the coverage counts. The caveat is the sample note. The brief cites some of the new records that Civic Lens considered for the week. It is not a full account.

A release copies `assets/css/theme.css`, `assets/css/app.css`, `assets/js/theme.js`, `assets/js/app.js`, and the fonts. Pages also work without JavaScript. Shared components are described in [host](host.md#presentation).

### Search

Each index page has a search panel. It stays hidden until `app.js` runs, because search needs JavaScript. On the first input, the script fetches `release.json` once and fills the official, issue, and policy area lists from the entries. It matches headline text and filters by kind, official, issue, policy area, and a date range. A date filter matches the event date only, so a record without an event date never matches it. The panel shows the first 50 matches, each linked to its record page. Search reads only the release files, so it works without the operational application.

## Storage and contracts

- The generated [publication schema](schema/publication.md) lists every table, column, key, check, index, and trigger.
- Each release row lists its records as kind, record ID, and revision. A row written before kinds existed has no kind and counts as a document change.
- The ready lists read the active row and match by kind, so a statement ID cannot hide a document change.
- Schema version 2 adds the entry kind. Readers accept versions 1 to 3. A version 1 entry has no kind and means a document change. A version 1 release on disk can therefore still be the base of the next release.
- Schema version 3 adds the entry filters. An entry from version 1 or 2 has empty filters, and publish does not backfill them.

| Filter | Document change | Statement | Action | Brief | Vote |
|---|---|---|---|---|---|
| `officials` (ID and configured name) | Officials | Speakers | The sponsor, when tracked | Empty | Empty |
| `issueIds` | Issue IDs | Issue IDs | Tracked issue IDs | Empty | Tracked issue IDs |
| `policyAreaIds` | Empty | Policy area IDs | The policy area ID stored with the approved revision, or empty | Empty | Empty |
| `sourceHost` | Host of the after version URL | Host of the source URL | Host of the first source URL | None | Host of the first source URL |
| `eventDate` | Evidenced change date, or empty | Evidenced statement date, or empty | Latest sponsored or withdrawn date | The Monday that starts the week | The vote day |

- New manifests use version 3. `src/CivicLens.Publication.Contracts/PublicationProtocol.cs` owns the version; see [Behavior versions](../architecture.md#behavior-versions).
- Each release also holds `release.json.gz` and `release.json.br`, at `PublicationProtocol.ManifestGzipPath` and `ManifestBrotliPath`. They compress the exact manifest bytes, so a static server can send them without compressing on each request.
- A separate static file server serves the release root read-only. It has no access to operational storage.

## Code map

| Concern | Files |
|---|---|
| Release contract | `src/CivicLens.Publication.Contracts/PublicationRelease.cs`, `src/CivicLens.Publication.Contracts/PublishedRecordEntry.cs`, `src/CivicLens.Publication.Contracts/PublishedDocumentChange.cs`, `src/CivicLens.Publication.Contracts/PublishedStatement.cs`, `src/CivicLens.Publication.Contracts/PublishedAction.cs`, `src/CivicLens.Publication.Contracts/PublishedSponsorship.cs`, `src/CivicLens.Publication.Contracts/PublishedVote.cs`, `src/CivicLens.Publication.Contracts/PublishedVoteFact.cs`, `src/CivicLens.Publication.Contracts/PublishedMemberVote.cs`, `src/CivicLens.Publication.Contracts/PublishedVoteKind.cs`, `src/CivicLens.Publication.Contracts/PublishedBrief.cs`, `src/CivicLens.Publication.Contracts/PublishedBriefParagraph.cs`, `src/CivicLens.Publication.Contracts/PublishedBriefCitation.cs`, `src/CivicLens.Publication.Contracts/PublishedBriefCoverage.cs`, `src/CivicLens.Publication.Contracts/PublishedEvidenceRules.cs` |
| Publish, list, activate | `src/CivicLens.Application/Publication/PublishRelease.cs`, `src/CivicLens.Application/Publication/ListPublishable.cs`, `src/CivicLens.Application/Publication/ActivatePublicationRelease.cs`, `src/CivicLens.Application/Publication/ListPublicationReleases.cs` |
| Kind loaders | `src/CivicLens.Application/Publication/PublishedRecordLoaders.cs`, `src/CivicLens.Application/Publication/PublishedRecordLoader.cs`, `src/CivicLens.Application/Publication/DocumentChangeRecordLoader.cs`, `src/CivicLens.Application/Publication/StatementRecordLoader.cs`, `src/CivicLens.Application/Publication/ActionRecordLoader.cs`, `src/CivicLens.Application/Publication/VoteRecordLoader.cs`, `src/CivicLens.Application/Publication/BriefRecordLoader.cs` |
| Record rules and builders | `src/CivicLens.Application/Publication/PublishedRecordRules.cs`, `src/CivicLens.Application/Publication/PublishedRecordBuilder.cs`, `src/CivicLens.Application/Publication/PublishedStatementBuilder.cs`, `src/CivicLens.Application/Publication/PublishedActionBuilder.cs`, `src/CivicLens.Application/Publication/PublishedVoteBuilder.cs`, `src/CivicLens.Application/Publication/PublishedBriefBuilder.cs` |
| Postgres and files | `src/CivicLens.Infrastructure/Publication/PostgresPublicationStore.cs`, `src/CivicLens.Infrastructure/Publication/FileReleaseStaging.cs`, `src/CivicLens.Infrastructure/Publication/FileReleaseDirectory.cs`, `src/CivicLens.Infrastructure/Publication/ReleasePaths.cs` |
| Rendering and CLI | `src/CivicLens.Host/Publication/ReleaseRenderer.cs`, `src/CivicLens.Host/Publication/PublicPage.razor`, `src/CivicLens.Host/Publication/PublishedIndexPage.razor`, `src/CivicLens.Host/Publication/PublishedRecordPage.razor`, `src/CivicLens.Host/Publication/PublishedStatementPage.razor`, `src/CivicLens.Host/Publication/PublishedActionPage.razor`, `src/CivicLens.Host/Publication/PublishedVotePage.razor`, `src/CivicLens.Host/Publication/PublishedVoteView.cs`, `src/CivicLens.Host/Publication/PublishedBriefPage.razor`, `src/CivicLens.Host/Publication/ReleaseCommand.cs` |
| Owner publish section | `src/CivicLens.Host/Review/ReviewHome.Publication.cs` |

Release and rollback commands are in [operations](../operations.md#publish-a-static-release). Backup, retention, and monitoring rules are in [Limits and planned work](#limits-and-planned-work).

## Limits and planned work

- A vote record embeds at most `PublishedVote.MaximumSources` sources: the detail, the members, and the bill detail.
- An action record embeds at most `PublishedAction.MaximumSources` sources. The worst case is the detail page, 3 cosponsor pages (250 rows), and the summaries page, which is 5 sources. A record over the cap fails publish loudly.
- Correction and withdrawal releases come later. No kind can publish one yet. See R-32 in the [roadmap](../roadmap.md).
- How the static file server runs locally is not decided. It must send the precompressed manifest, for example Caddy `file_server { precompressed br gzip }` or nginx `gzip_static on`. Compression by the server is the fallback.
- Issue, official, and briefing pages are planned in [R-36](../roadmap.md#next). Readers will start with a weekly briefing and recent reviewed developments. Issue pages will connect related records across officials and institutions. Official pages will show dated statements, actions, and reviewed relationships.
- Search filter pages, timelines, expandable evidence, and shareable records are planned against published releases.
- Public filter pages are an MVP requirement. The R-36 official, issue, and policy area pages serve as these pages.
- Planned, not built: a source filter and filters that are easy to clear, in R-36. The manifest already holds `sourceHost` for the source filter.
- The R-36 pages will work at phone width. Each result will show the kind, headline, officials, issues, and dates of its record.
- Later, in [R-40](../roadmap.md#later) after the first cloud deployment: a public search service with its own Postgres database, fed from releases. It gives full-text search over the whole processed corpus.
- Split the manifest by kind when it grows over 4 MB.
- At 20,000 entries the worst realistic manifest is about 14.6 MB, under three quarters of the 32 MB cap. `PublishRelease` refuses a manifest over the cap.
- Issue and policy area filters show raw IDs, because the manifest has no names for them. R-36 owns the fix. The names come from `config/issues.json` through `PublicationCatalog`, as official names do.
- After the first deploy, the public search service replaces the client search and the schema 3 filter fields. Delete them then.
- Each release row stores `records_json`, about 2 MB at 20,000 records. Review queries expand the active one.
- Rolling back the `DropReleaseCountCheck` migration fails while any release holds more than 2000 records.
- A future live public Blazor deployment must read published releases only and stay separate from the operational application.
- The public UI shape is open in [D-14](../roadmap.md#pending-decisions). Interactive Blazor Server does not suit public pages, because search engines need complete HTML at stable URLs.
- A future mobile client would read public release contracts through an API, not operational storage.
- Public MCP will expose `search_records`, `get_record`, `get_official_timeline`, and `compare_document_versions`. It returns published data with IDs, citations, dates, review status, coverage caveats, and the release ID.
- Public MCP cannot start collection, inference, or edits. It bounds and pages responses, limits requests, and accepts record IDs, not arbitrary fetch URLs. It treats source passages as untrusted attributed content.
- Public MCP will search the same manifest entries as the website. The website and public MCP will share one release, and clients can pin it across calls.
- Public MCP needs request compute, but never the local database or AI inference.
- Postgres and capture files need coordinated backups and a tested restore. Production backup rules are in [host](host.md#limits-and-planned-work).
- Retention must keep the evidence that published releases reference. Disposable captures follow an explicit policy, which is not agreed yet.
- Monitoring must cover freshness, skipped and deferred work, source failures, storage, and spend.
- See [roadmap](../roadmap.md) for order.

Public MCP and operations acceptance:

- [ ] Public tools never access operational data, change records, crawl, or call models.
- [ ] The website and MCP use the same release.
- [ ] Show restore, rollback, release-pinned reads, pagination, bounded traffic behavior, and visible coverage gaps.
