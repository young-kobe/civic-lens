# Executive actions

## Purpose

The executive actions feature records presidential documents from the Federal Register. A record holds the official title and the facts of one document: its type, number, dates, citation, and President. Code approves each record after it checks the citations, because the facts come from the Federal Register. A person can only withdraw a record or approve it again. This slice holds no document text and no summary.

## Decisions

- The source is the Federal Register JSON API. It needs no key. The collector sends the shared User-Agent `CivicLens/0.1 (civic-lens.info)` and no email address.
- The scope is executive orders, proclamations, notices, and memoranda. The list filter `presidential_document_type` selects them. The API gives `presidential_document_type` as a filter only, so the parser reads the `subtype` field of each document.
- One record is one original document. Its identity is the Federal Register document number of the original. The record always keeps the number, the dates, the citation, and the link of the original as its facts.
- A correction document names the original in `correction_of`. It adds one `Correction notice` fact to the record. The fact holds the number, the publication date, and the link of the correction, and it cites the capture of the correction. A correction never replaces a fact of the original. A notice for a recorded original makes a new revision.
- A correction that arrives before its original is stored with its notice and makes no record. It admits a job for the original at once. The original then makes revision 1 with every notice that waited. This holds in either arrival order.
- A correction that names a correction is invalid data. It blocks its job with the reason code `CorrectionOfCorrectionException`. The check holds in either arrival order.
- A list job covers a window of publication dates and admits one document job for each listed document. The window needs no cursor table. It starts 2 days before the newest publication date of any stored document, or at `backfillFrom` when none exists. It ends today.
- A window must fit one API page of 1,000 documents. A longer list is invalid data and blocks the job. About 340 documents appear in a year, so the first window from `backfillFrom` fits.
- A new document job fetches the document with pinned `fields[]`, including `disposition_notes`. The pin keeps a changing field such as the page view count out of the capture. The pin also adds `president`, which the API omits otherwise. Jobs with the exact prior pinned field list stay valid and keep their original URL. Other field orders and query forms are invalid.
- The extraction text has each fixed fact on its own line, so each is a clean citation. `ExecutiveActionFacts` owns the labels and the order. Disposition notes, when present, retain their multiline source text as one appended block.
- Admission writes revision 1 and the code approval in one transaction. This follows the roster admission, which writes a snapshot in one transaction, and `ReviewRecords.AppendCodeApprovedAsync`. No analysis queue exists, because no model writes text.
- The headline is the official title. Public pages say that it is the title from the Federal Register.
- The signer is the configured person whose `President` office covers the signing date. The person ID must equal the President identifier that the Federal Register cites. Both Presidents hold office on 2025-01-20, so the cited identifier picks one. A document with no match is invalid data and blocks the job.
- Issue and policy area lists are empty at launch.
- The published kind is `executiveAction`. It adds no manifest version bump.
- A reviewer or the owner can withdraw approval on the review page, as for a vote. The page has the decision panel and, for the owner, Take off site; see [review](review.md#take-off-site). Withdrawing approval alone does not change the public site.

## Invariants

- Disposition notes cite the complete original source block and survive correction revisions. Enforcers: `ExecutiveActionRevision` and its typed revision columns. Tests: `ExecutiveActionRevisionTests`, `PublishExecutiveActionTests`, and the disposition cases in `PostgresExecutiveActionRevisionColumnsTests`.

Each line gives the rule, what enforces it, and the test that protects it.

- Only an exact numbered reference becomes a target. A line must start with `Amends:` or `Revokes:`. A semicolon splits it into references. A reference must be exactly `EO n` or `Proc. n`, with an optional `, Month d, yyyy`. Any other reference, any other relation, and a note with more than 100 targets give no target and stay in the note. Enforced by `ExecutiveActionDispositionTargets`. Tests: `ExecutiveActionDispositionTargetsTests.RealNoteShapesGiveExactTargetsWithTheirSpansInsideTheNote`, `ExecutiveActionDispositionTargetsTests.UnsupportedOrAmbiguousReferencesGiveNoTarget`, `ExecutiveActionDispositionTargetsTests.ASupportedReferenceBesideAnUnsupportedOneStaysATarget`, `ExecutiveActionDispositionTargetsTests.ARepeatedReferenceOfTheSameRelationIsOneTargetButARevocationOfTheSameOrderIsAnother`, `ExecutiveActionDispositionTargetsTests.MoreTargetsThanTheMaximumGiveNoTargetsInsteadOfAGuess`.
- A revision stores its targets once, in the transaction that writes the revision. A reader never parses the notes. A revision with no notes has no target. A correction revision keeps the original notes, so it keeps their targets. Enforced by `ExecutiveActionRevisionRows.FillColumns`. Tests: `PostgresExecutiveActionDispositionTargetsTests.AnAdmittedRevisionStoresExactTargetsWithTheirSpansAndNoteLessRevisionsStoreNone`, `PostgresExecutiveActionDispositionTargetsTests.ACorrectionRevisionKeepsTheOriginalTargetsWithTheOriginalNote`.
- A target links only when exactly one other executive action of the active release has its kind and number. A record of an older release, a second match, the record itself, and a different kind never link. Enforced by the view `public_api.executive_action_targets`. Tests: the `PublicApiExecutiveActionTargetsMigrationTests` link cases and `RecordPagesEndpointsTests.AnExecutiveActionListsItsTypedTargetsWithARecordIdOnlyForAnExactMatchInTheActiveRelease`.
- A target row breaks no rule of the table. The relation is Amends or Revokes. The kind is ExecutiveOrder or Proclamation. The number has 1 to 6 digits and no leading zero. The span is not empty. A revision holds a relation, kind, and number once. A revision with targets cannot be deleted. Enforced by the `ck_executive_action_revision_disposition_targets_*` checks, the unique index, and the foreign key. Tests: `PostgresExecutiveActionDispositionTargetsTests.TheDatabaseRefusesATargetThatBreaksARule`, `PostgresExecutiveActionDispositionTargetsTests.ARevisionWithTargetsCannotBeDeleted`.
- The target backfill has bounded writes and bounded batches, and it is idempotent. Each batch reads at most 25 revisions and is one short transaction that holds the executive action review lock. It writes only revisions that have notes and no target row, so a rerun changes nothing. A run reads forward over revisions that have notes and no target row, in key order. It stops when it has examined `limit` revisions or when the cursor ends, so it always ends. A revision whose notes hold no target is examined again by each run. `executive-actions targets verify` compares the stored rows to a new parse of the notes and exits 1 while a legacy row or a mismatch remains. Enforced by `PostgresExecutiveActionTargetBackfillStore` and `BackfillRevisions`. Tests: `PostgresExecutiveActionDispositionTargetsTests.TheBackfillWritesTargetsForOldRevisionsInBatchesAndARerunChangesNothing`, `PostgresExecutiveActionDispositionTargetsTests.TheBackfillStopsAtTheLimitAndNotesWithNoTargetStayNotesOnly`, `PostgresExecutiveActionDispositionTargetsTests.VerifyReportsAMissingTargetAsLegacyAndAChangedOrOrphanTargetAsAMismatchByIdOnly`.
- A document makes at most one revision. Enforced by the key of `executive_action_documents`. Test: `PostgresFederalRegisterTests.ADocumentThatIsKnownAddsNoSecondRevisionSoAReplayOfPreparationChangesNothing`.
- A revision and its code approval commit together or not at all. Enforced by `ExecutiveActionRows.AppendCodeApprovedAsync` inside the job store transaction. Test: `PostgresFederalRegisterTests.ADocumentWritesRevisionOneAndTheCodeApprovalInOneTransaction`.
- A correction adds a notice and keeps the original facts, in either arrival order. Enforced by `ExecutiveActionRows.AdmitAsync`. Tests: `PostgresFederalRegisterTests.ACorrectionAppendsARevisionThatKeepsTheOriginalFactsAndAddsOneNoticeCitedToItsOwnCapture`, `PostgresFederalRegisterTests.ACorrectionThatArrivesFirstWaitsAndAsksForTheOriginalThenTheOriginalStartsTheRecordWithTheNotice`, `ExecutiveActionRevisionTests.ACorrectionAddsANoticeThatCitesItsOwnExtractionAndLeavesTheOriginalFactsAsTheyWere`.
- A correction of a correction is never stored. Enforced by `ExecutiveActionRows.AdmitAsync`, which throws `CorrectionOfCorrectionException`. Tests: `PostgresFederalRegisterTests.ACorrectionOfACorrectionIsInvalidDataInEitherArrivalOrderSoItNeverWaitsSilently`, `PostgresFederalRegisterTests.ACorrectionThatWaitsBlocksTheCorrectionItNamesWhenThatDocumentIsAlsoACorrection`.
- Every fact equals the text of its citation, and every typed field equals its fact. Enforced by `ExecutiveActionRevision.ValidateShape` and `ValidateAgainst`. Tests: `ExecutiveActionRevisionTests.AFactThatDiffersFromTheTypedFieldIsRefusedSoTheTitleAndItsCitationCannotDrift`, `ExecutiveActionRevisionTests.AFactMustEqualTheTextOfItsCitationInTheSavedExtraction`.
- Only the code writes a revision. Enforced by `ExecutiveActionRevision.ValidateShape`. Test: `ExecutiveActionRevisionTests.ARevisionByAnyoneButTheCodeIsRefusedBecauseNoPersonEditsTheseRecords`.
- The signer is a configured President in office on the signing date, and the Federal Register cites that person. Enforced by `ExecutiveActionSigner`. Tests: `ExecutiveActionSignerTests.ACitedPresidentWhoIsNotInOfficeOnTheSigningDateIsInvalidSoNoRecordNamesTheWrongPerson`, `ExecutiveActionSignerTests.OnTheDayOfTheHandoverTheCitedPresidentPicksBetweenTheTwoOffices`.
- A job fetches the list URL or a document URL of its source and no other URL. Enforced by `CollectionJobDefinition` and `FederalRegisterUrl`. Tests: `FederalRegisterRequestTests.ThePassedWindowReadsBackFromItsListUrlAndNoOtherUrlHasTheForm`, `FederalRegisterSourceConfigurationTests.AJobWhoseUrlHasNotTheFormOfItsResourceIsRefused`.
- A list that does not fit one page is invalid. Enforced by `FederalRegisterListParser`. Test: `FederalRegisterParserTests.AListOfMoreThanOnePageIsInvalidBecauseTheWindowMustFitOneRequest`.
- The next window starts 2 days before the newest stored publication date. Enforced by `FederalRegisterWindow.Next`. Tests: `FederalRegisterRequestTests.AfterADocumentTheWindowStartsTwoDaysBeforeItsPublicationSoALateListingIsNotMissed`, `PostgresFederalRegisterTests.TheNextWindowStartsTwoDaysBeforeTheNewestKnownPublicationDate`.
- A source covers no people, sets no credential, and sets one Federal Register source at most. Enforced by `CollectionConfiguration`. Tests: `FederalRegisterSourceConfigurationTests.ACredentialAValidatorACoverageListOrACongressOptionIsRefused`, `FederalRegisterSourceConfigurationTests.ASecondFederalRegisterSourceIsRefusedBecauseOneStreamHoldsTheNewestDate`.
- An open concern, such as a withdrawal by the owner, blocks a new code approval. Enforced by `ReviewRecords.AppendCodeApprovedAsync`. Test: `PostgresExecutiveActionReviewStoreTests.ACorrectionAfterAWithdrawalWaitsForAPersonBecauseTheOpenConcernBlocksCodeApproval`.
- A revision written by the current code holds the same data in its columns and child rows as in its JSON. Enforced by `ExecutiveActionRevisionRows`, the one mapping in both directions. Tests: `PostgresExecutiveActionRevisionColumnsTests.ALegacyRevisionMovesToColumnsThatEqualItsJsonAndTheJsonStaysUntouched`, `PostgresExecutiveActionRevisionColumnsTests.VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsText`.
- A revision stores the git commit SHA of the running build in `executive_action_revisions.code_version`, because code writes every executive action revision. Revisions from before the column existed hold null, and nothing fills them. A build with no valid SHA fails the write and stores nothing. The worker and the review workspace refuse to start without a valid SHA, because a person approval can append a revision. Enforced by `ExecutiveActionRevisionRows.Create`, `CodeVersion`, `BuildCodeVersion`, and `ck_executive_action_revisions_code_version`. Tests: `CodeVersionTests`, `PostgresExecutiveActionReviewStoreTests.ABuildWithNoShaCannotSaveAPersonApprovalWithoutItsRevisionBecauseTheWholeApprovalRollsBack`, `PostgresExecutiveActionReviewStoreTests.AnAdmittedRevisionStoresTheBuildShaBecauseTheCodeThatWroteTheFactsMustBeTraceable`, and `PostgresExecutiveActionReviewStoreTests.ARevisionAppendedByAPersonApprovalStoresTheBuildShaBecauseCodeWritesItAndNotThePerson`.
- Every C# reader of an executive action revision reads the columns and child rows, and none reads `revision_json`. `ExecutiveActionRevisionRows` and `ExecutiveActionRevisionPage` rebuild the revision in a fixed number of queries. The shared review code takes this reader as the `readRevisions` argument of `ReviewRecords`. `ValidateShape` runs on the rebuilt revision. The headline search reads the `title` column. A row with null columns fails with `InvalidDataException` that names the backfill, and no reader falls back to the JSON. Enforced by those types and `PostgresReviewQueueStore`. Tests: `PostgresExecutiveActionRevisionColumnsTests.AReaderBuildsTheRevisionFromTheColumnsAndNotFromTheJson`, `PostgresExecutiveActionRevisionColumnsTests.AReaderFailsOnARowWithNullColumnsAndTheJsonIsNoFallback`, `PostgresExecutiveActionRevisionColumnsTests.TheHeadlineSearchFailsOnARowWithNullColumnsBecauseItMustNotSkipTheRow`, `PostgresExecutiveActionRevisionColumnsTests.TheHeadlineSearchReadsTheTitleColumnAndNotTheJson`, `PostgresExecutiveActionRevisionColumnsTests.ReadingMoreRecordsCostsTheSameNumberOfQueriesBecauseNoReaderQueriesEachRow`.
- A fact row stores only its citation span. The label and the value come from the typed columns, so one rule owns each fact line. The backfill proves it, because it rebuilds the revision and compares it to the JSON. A notice reads from its document row, and that row must belong to the record and hold a notice. Enforced by `ExecutiveActionRevisionPage.Rebuild`. Tests: `PostgresExecutiveActionRevisionColumnsTests.VerifyFailsWhenAColumnDiffersFromTheJsonAndNamesTheFieldButNeverItsText`.
- A document row that a revision cites never changes. Enforced by `ExecutiveActionRows.AppendWaitingNoticesAsync`, which sets the revision number of waiting rows only. Test: `PostgresFederalRegisterTests.ADocumentRowThatARevisionCitesNeverChangesWhenLaterNoticesAndReplaysArrive`.
- A revision column, child row, or notice column breaks no rule of the revision. Enforced by the `ck_executive_action_revisions_*`, `ck_executive_action_revision_facts_*`, `ck_executive_action_revision_notices_*`, and `ck_executive_action_documents_*` checks, and by the foreign keys and the unique notice key of these tables. The author columns hold the code author only. Tests: `PostgresExecutiveActionRevisionColumnsTests.TheDatabaseRefusesARowThatBreaksARule`, `PostgresExecutiveActionRevisionColumnsTests.TheDatabaseRefusesALinkThatBreaksAKey`.
- A source ID and a person ID have at most 128 characters, the length of a configuration ID. Enforced by `ExecutiveActionRevision.ValidateShape` and `ReviewTextRules.MaximumConfigurationIdLength`. Test: `ExecutiveActionRevisionTests.ASourceIdAndAPersonIdHoldAtMostTheLengthOfAConfigurationId`.
- The backfill moves a legacy revision only when its rows, read back, rebuild a revision that equals the stored JSON. It writes the row in a savepoint, rebuilds the revision from the stored rows, and compares it to the JSON as trees. A row that differs, or that a precheck or the database refuses, is skipped and reported by ID and reason. The row and its JSON never change. The reasons are `UnreadableJson`, `InvalidShape`, `RebuildMismatch`, and `database:<constraint>`. Enforced by `RevisionBackfill` and `PostgresExecutiveActionRevisionBackfillStore`. Tests: `ExecutiveActionRevisionColumnsMigrationTests.ARevisionOfTheOldShapeMovesToColumnsAfterTheMigrationAndVerifiesWithNoMismatchBecauseTheColumnsRebuildTheOriginalRevision`, `PostgresExecutiveActionRevisionColumnsTests.JsonThatIsNoRevisionIsSkippedWithAReasonAndTheRowStaysAsItWas`, `PostgresExecutiveActionRevisionColumnsTests.AnOldRevisionThatFailsTheShapeRulesOfTodayIsSkippedAndNeverChanged`, `PostgresExecutiveActionRevisionColumnsTests.AnIdThatDanglesIsSkippedAndNothingIsGuessedBecauseAForeignKeyRefusesIt`, `PostgresExecutiveActionRevisionColumnsTests.ARowWhoseNoticeDocumentDiffersFromItsJsonIsSkippedWithNoTraceAndTheRowsAroundItStillMove`.
- The backfill is bounded and idempotent. Each batch of at most 25 revisions is one short transaction that holds the executive action review lock and no other key. It writes only rows with null columns, so a rerun changes nothing. Enforced by `RevisionBackfill.BackfillBatchAsync` and `BackfillRevisions`. Tests: `PostgresExecutiveActionRevisionColumnsTests.ARerunChangesNothingBecauseTheBackfillTouchesOnlyRowsWithNullColumns`, `PostgresExecutiveActionRevisionColumnsTests.TheBackfillWaitsForTheExecutiveActionReviewLockBecauseAWriterOfTheSameKeyMustNeverSeeAHalfMovedBatch`.
- `executive-actions revisions verify` rebuilds each revision from its rows and compares it to the stored JSON as trees. It writes nothing, prints counts and key names of the first 20 mismatches by ID, and never prints text. It exits 1 while a legacy row or a mismatch remains. Enforced by `RevisionBackfill.VerifyAsync`. Tests: `PostgresExecutiveActionRevisionColumnsTests.VerifyReportsALegacyRowAsLegacyAndNotAsAMismatch`, `PostgresExecutiveActionRevisionColumnsTests.VerifyReportsAnUnreadableJsonAsAMismatchOfTheJsonAndWritesNothing`.
- The migration moves each `notice_json` to the notice columns and drops the column. It fails and keeps the JSON when a notice differs from its row. It fails and names the row when a `notice_json` or a `revision_json` is not valid JSON. It sets the code author on each old revision, and fails when the JSON names another author. The rollback rebuilds `notice_json`. Enforced by `ExecutiveActionRevisionColumns`. Tests: `ExecutiveActionRevisionColumnsMigrationTests.TheNoticeJsonOfADocumentMovesToTheNoticeColumnsAndTheCodeAuthorFillsTheRevisionBecauseTheColumnsAreTheOnlyCopy`, `ExecutiveActionRevisionColumnsMigrationTests.ANoticeJsonThatDiffersFromItsRowFailsTheMigrationAndKeepsTheJsonBecauseNothingMayBeLostWithoutACopy`, `ExecutiveActionRevisionColumnsMigrationTests.AnUnreadableJsonFailsTheMigrationAndNamesItsRowBecauseAGenericCastErrorDoesNotSayWhichRowToFix`, `ExecutiveActionRevisionColumnsMigrationTests.ARevisionOfAnotherAuthorFailsTheMigrationBecauseCodeIsTheOnlyAuthorOfThisKind`, `ExecutiveActionRevisionColumnsMigrationTests.TheRollbackRebuildsANoticeJsonThatReadsBackAsTheSameNoticeAndTheNextRunKeepsTheColumns`.
- The contract labels equal the labels that code writes. Enforced by a test. Test: `PublishedExecutiveActionTests.TheLabelsThatTheContractChecksEqualTheLabelsThatCodeWrites`.
- A published record cites its title, dates, citation, and link. Enforced by `PublishedExecutiveAction.Validate`. Tests: `PublishedExecutiveActionTests.AHeadlineThatIsNotTheCitedTitleIsRefusedSoTheOfficialTitleCannotDrift`, `PublishedExecutiveActionTests.ASigningDateThatTheEvidenceDoesNotShowIsRefused`.
- The worker publishes each approved, unpublished record, whoever approved it. Enforced by `ExecutiveActionReviewQueries`. Test: `PublishExecutiveActionTests.TheWorkerPublishesAnExecutiveActionWhoeverApprovedIt`.
- Search accepts the kind. Enforced by `RecordReader`. Test: `RecordReaderTests.AnExecutiveActionIndexesNoSummaryAndItsSavedRecordAsSourceSoSearchAcceptsTheNewKind`.

## Behavior

### Source configuration

One `api` source with `apiParser` `federalRegisterDocuments` follows the Federal Register. The source URL is `https://www.federalregister.gov/api/v1/documents.json`. The allowed path prefix is `/api/v1/`, because the list path and the document path need one prefix.

The source sets `backfillFrom` and no credential, no coverage list, no validator, and no Congress option. It covers no people and stays eligible every day. The shipped source `federal-register-documents` sets `backfillFrom` 2024-10-29 and `checkIntervalSeconds` 21600. It ships with `enabled: false`, like the other `api` sources. The admission policy holds 1,000 jobs. [Operations](../operations.md#federal-register-stream) holds the enable steps.

### Window and jobs

The scheduler makes one list job for each due check. Only the schedule makes it. The list URL holds the window, the type filters, the field list, `order=oldest`, and `per_page=1000`. `FederalRegisterUrl` is the one place that builds and reads it. New detail URLs request the exact pinned fields through `disposition_notes`; recovery accepts the exact previous field set for queued jobs without rewriting their requested URL.

A list job admits one document job for each item. The key is `federal-register/{source}/document/{number}`. A known key is a duplicate. A document job gets the check priority when the document was published in the last 2 days, else the backfill priority.

### Parsing and extraction

| Parser | Version | Reads |
|---|---|---|
| `FederalRegisterListParser` | none, because a list has no extraction | One list with document numbers and publication dates |
| `FederalRegisterDocumentParser` | `federal-register-document-v2` | One document |

A document needs these fields: its number, the type `Presidential Document`, and a subtype of the four kinds. It also needs a title, two dates, a citation, and a President. The link must be on `www.federalregister.gov`. The number of the response must equal the number in the job URL. A line break in a fixed fact becomes a space. When the API includes `disposition_notes`, the parser retains its complete text and line breaks; missing or null notes add no text. The existing document extraction limit bounds the output, and notes are never truncated. A renderer change bumps the extraction version.

```
Federal Register document: 2025-01900
Document type: Executive Order
Presidential document number: 14147
Title: Ending the Weaponization of the Federal Government
Signed: 2025-01-20
Published: 2025-01-28
Citation: 90 FR 8235
President: Donald Trump
Document URL: https://www.federalregister.gov/documents/2025/01/28/2025-01900/ending-the-weaponization-of-the-federal-government
```

A notice or memorandum has no number line. The extraction of a correction document adds the line `Correction notice`, which holds its own number, date, and link. The record cites that line.

### Admission

Preparation of a document job parses the body, finds the signer, saves the extraction, and calls `AdmitDocumentAsync`. The store takes the job lock. It looks for the document in `executive_action_documents` under that lock, and a known document ends the work as a duplicate. Then the store takes the executive action review lock.

An original creates the draft row and revision 1, with the notices that waited. A correction adds its notice to the current revision and appends the next revision. A correction with no draft waits. Code approves each revision when the rules allow it. The store writes the document row in the same transaction. The extraction must belong to an attempt of the job.

### Disposition targets

`ExecutiveActionDispositionTargets` reads the verbatim disposition notes of a revision. A line that starts with `Amends:` or `Revokes:` holds references that a semicolon separates. A reference becomes a target only when it is exactly `EO n` or `Proc. n`, with an optional `, Month d, yyyy`. These stay in the note only: other lines such as `See:` and `Supersedes:`, and other documents such as memoranda. Joined references such as `EO 1 and EO 2` and ranges also stay. The rule never guesses. Each target keeps its relation, its kind, its number as text, and the start and length of its reference inside the note value. The start and the length count UTF-16 code units, like the other citation spans. The citation of the note then covers it. A repeat of the same relation, kind, and number gives one target.

`ExecutiveActionRevisionRows.FillColumns` writes the target rows with the revision, in the same transaction. A correction revision copies the original notes, so it writes the same targets. The detail API reads the targets through the view `public_api.executive_action_targets`. Each entry has its relation, kind, number, and the cited text of the note. It has a record ID only when exactly one other executive action of the active release has the same kind and `presidential_number`. A target with no match, with an older release only, or with two matches has no ID and no link, and stays a cited note. The API never parses the notes. See [search](search.md#api) and [publication](publication.md#public-api-views).

### Review

The review page at `/Review/ExecutiveActions/{id}` shows the facts, their quotes, the history, the banner of an unanswered reader correction, and the decision panel. It has no editor. A reviewer or the owner can withdraw approval or request changes. A record that needs action appears in the review overview with the other fact records.

### Publication

The release holds the record file and a page for each record. The headline is the title. The page says that the title is the title from the Federal Register, that Civic Lens wrote no summary, and that code checked the facts. It links to the Federal Register page. The President page, the all records list, and search list the record. See [publication](publication.md#release-content).

## Storage and contracts

- The generated [executive actions schema](schema/executive-actions.md) lists every table, column, key, and check.
- `executive_action_drafts` has one row for each record. It holds the current revision, the review state, and the listing columns.
- `executive_action_revisions` holds one row for each revision. Typed columns hold the document fields and the code author. The row also keeps the revision JSON as a copy, until a later step drops it. Readers use the columns.
- `executive_action_revision_facts` holds one row for each fact line of a revision: its position and the citation span. It stores no label and no value. A reader builds the lines with `ExecutiveActionFacts.Of` from the columns of the revision.
- `executive_action_revision_notices` holds one row for each notice of a revision: its position and the key of the notice document. The document row holds the notice.
- `executive_action_revision_disposition_targets` holds one row for each target of the notes of a revision. A row has its position, relation, kind, and number. It also has the span of the reference inside the note value. It stores no note text. A revision with no notes has no row.
- `executive_action_review_decisions` holds the decisions, including the code approval.
- `executive_action_documents` has one row for each document. It holds the publication date, which sets the next window. It holds the first revision that has the document. A correction row keeps its notice as `notice_url` and an optional citation span, both null or both set. It has no revision while it waits. A revision never changes a document row that it cites, because the revision reads the notice from that row.
- `IFederalRegisterAdmissionStore` is the port of list and document admission. `PostgresCollectionJobStore` implements it. `IExecutiveActionReviewStore` is the review and publication port. `PostgresExecutiveActionReviewStore` implements it.
- `PublishedExecutiveAction` is the published record. It has the kind `executiveAction`.

## Code map

| Concern | Files |
|---|---|
| Parsing, URLs, window, keys | `src/CivicLens.Application/Collection/FederalRegister/` |
| Facts, revision, review, disposition target rule | `src/CivicLens.Core/ExecutiveActions/` |
| Signer, identity, revision factory, handlers, ports | `src/CivicLens.Application/ExecutiveActions/` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Configuration and job binding | `src/CivicLens.Application/Collection/CollectionConfiguration.cs`, `src/CivicLens.Application/Collection/ConfiguredCollectionSource.cs`, `src/CivicLens.Application/Collection/Jobs/CollectionJobDefinition.cs` |
| Admission and scheduling | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.FederalRegister.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Scheduling.cs` |
| Rows and review store | `src/CivicLens.Infrastructure/ExecutiveActions/` |
| Revision mapping and backfill | `src/CivicLens.Infrastructure/ExecutiveActions/ExecutiveActionRevisionRows.cs`, `src/CivicLens.Infrastructure/ExecutiveActions/ExecutiveActionRevisionPage.cs`, `src/CivicLens.Infrastructure/ExecutiveActions/PostgresExecutiveActionRevisionBackfillStore.cs`, `src/CivicLens.Infrastructure/ExecutiveActions/PostgresExecutiveActionTargetBackfillStore.cs`, `src/CivicLens.Host/ExecutiveActions/ExecutiveActionCommand.cs` |
| Publication | `src/CivicLens.Application/Publication/ExecutiveActionRecordLoader.cs`, `src/CivicLens.Application/Publication/PublishedExecutiveActionBuilder.cs`, `src/CivicLens.Publication.Contracts/PublishedExecutiveAction.cs` |
| Pages | `src/CivicLens.Host/Publication/PublishedExecutiveActionPage.razor`, `src/CivicLens.Host/ExecutiveActions/ExecutiveActionEditor.razor` |
| Search | `src/CivicLens.PublicApi/Ingest/RecordReader.cs` |
| Sources and people | `config/federal-coverage.json` |

## Limits and planned work

- A record holds no document text and no summary. The next slice adds the body text. An AI summary and issue tags follow.
- A President's page lists the record. The talk and action counts do not count it.
- Determinations, presidential orders, and other documents are out of scope. The list filter leaves them out.
- The R-63 pilot has four steps for this kind too; see [roadmap](../roadmap.md). Steps 1 to 3 are done in code. Both write paths write the columns and `revision_json`. The commands `executive-actions revisions backfill` and `executive-actions revisions verify` move and check old revisions. The runbook is in [operations](../operations.md#executive-action-revision-backfill). Step 3 stays open until production runs this code. Step 4 drops the JSON. It needs a migration and a change of `ExecutiveActionRevisionRows.Create`.
- A skipped revision stays legacy, and every run examines it again. Fix the cause first.
- A list longer than one page blocks the job. Move `backfillFrom` forward to fit.
- A document whose President is not a configured President blocks its job. Add the person to the configuration.
- The signing date comes from the Federal Register. Code does not check it against another source.
- Nine or more corrections that arrive before the original make revision 1 fail validation. The original is then not recorded.
- A correction waits with no visible sign when the job of its original fails or blocks for good.
- Disposition notes retain their complete value and exact original-extraction span on each immutable revision. Correction revisions preserve that original note and citation. The revision stores the value and optional source-span columns, leaving fixed fact rows unchanged. Publication includes the notes as one cited fact. Old revisions with no captured notes remain null; no historical notes are inferred. Each immutable revision also stores typed Amends and Revokes targets beside its notes; see [Disposition targets](#disposition-targets). The commands `executive-actions targets backfill` and `executive-actions targets verify` fill old revisions; the runbook is in [operations](../operations.md#executive-action-disposition-target-backfill). The detail API returns the targets, and it gives a record ID only for an exact match in the active release. The server-rendered page does not show the links yet. PR 502 or a follow-up renders them from `targets`, and links only an entry that has a `targetRecordId`.
