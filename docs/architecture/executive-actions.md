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
- A document job fetches the document with pinned `fields[]`. The pin keeps a changing field such as the page view count out of the capture. The pin also adds `president`, which the API omits otherwise.
- The extraction text has one fact on each line, so each fact is a clean citation. `ExecutiveActionFacts` owns the labels and the order.
- Admission writes revision 1 and the code approval in one transaction. This follows the roster admission, which writes a snapshot in one transaction, and `ReviewRecords.AppendCodeApprovedAsync`. No analysis queue exists, because no model writes text.
- The headline is the official title. Public pages say that it is the title from the Federal Register.
- The signer is the configured person whose `President` office covers the signing date. The person ID must equal the President identifier that the Federal Register cites. Both Presidents hold office on 2025-01-20, so the cited identifier picks one. A document with no match is invalid data and blocks the job.
- Issue and policy area lists are empty at launch.
- The published kind is `executiveAction`. It adds no manifest version bump.
- A reviewer or the owner can withdraw approval on the review page, as for a vote. The page has the decision panel and, for the owner, Take off site; see [review](review.md#take-off-site). Withdrawing approval alone does not change the public site.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it.

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
- The contract labels equal the labels that code writes. Enforced by a test. Test: `PublishedExecutiveActionTests.TheLabelsThatTheContractChecksEqualTheLabelsThatCodeWrites`.
- A published record cites its title, dates, citation, and link. Enforced by `PublishedExecutiveAction.Validate`. Tests: `PublishedExecutiveActionTests.AHeadlineThatIsNotTheCitedTitleIsRefusedSoTheOfficialTitleCannotDrift`, `PublishedExecutiveActionTests.ASigningDateThatTheEvidenceDoesNotShowIsRefused`.
- The worker publishes a record that code approved and leaves a record that a person approved. Enforced by `ExecutiveActionReviewQueries`. Test: `PublishExecutiveActionTests.TheWorkerPublishesARecordThatCodeApprovedAndLeavesOneThatAPersonApproved`.
- Search accepts the kind. Enforced by `RecordReader`. Test: `RecordReaderTests.AnExecutiveActionIndexesNoSummaryAndItsSavedRecordAsSourceSoSearchAcceptsTheNewKind`.

## Behavior

### Source configuration

One `api` source with `apiParser` `federalRegisterDocuments` follows the Federal Register. The source URL is `https://www.federalregister.gov/api/v1/documents.json`. The allowed path prefix is `/api/v1/`, because the list path and the document path need one prefix.

The source sets `backfillFrom` and no credential, no coverage list, no validator, and no Congress option. It covers no people and stays eligible every day. The shipped source `federal-register-documents` sets `backfillFrom` 2024-10-29 and `checkIntervalSeconds` 21600. It ships with `enabled: false`, like the other `api` sources. The admission policy holds 1,000 jobs. [Operations](../operations.md#federal-register-stream) holds the enable steps.

### Window and jobs

The scheduler makes one list job for each due check. Only the schedule makes it. The list URL holds the window, the type filters, the field list, `order=oldest`, and `per_page=1000`. `FederalRegisterUrl` is the one place that builds and reads it.

A list job admits one document job for each item. The key is `federal-register/{source}/document/{number}`. A known key is a duplicate. A document job gets the check priority when the document was published in the last 2 days, else the backfill priority.

### Parsing and extraction

| Parser | Version | Reads |
|---|---|---|
| `FederalRegisterListParser` | none, because a list has no extraction | One list with document numbers and publication dates |
| `FederalRegisterDocumentParser` | `federal-register-document-v1` | One document |

A document needs these fields: its number, the type `Presidential Document`, and a subtype of the four kinds. It also needs a title, two dates, a citation, and a President. The link must be on `www.federalregister.gov`. The number of the response must equal the number in the job URL. A line break in a value becomes a space. A renderer change bumps the extraction version.

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

### Review

The review page at `/Review/ExecutiveActions/{id}` shows the facts, their quotes, the history, the banner of an unanswered reader correction, and the decision panel. It has no editor. A reviewer or the owner can withdraw approval or request changes. A record that needs action appears in the review overview with the other fact records.

### Publication

The release holds the record file and a page for each record. The headline is the title. The page says that the title is the title from the Federal Register, that Civic Lens wrote no summary, and that code checked the facts. It links to the Federal Register page. The President page, the all records list, and search list the record. See [publication](publication.md#release-content).

## Storage and contracts

- The generated [executive actions schema](schema/executive-actions.md) lists every table, column, key, and check.
- `executive_action_drafts` has one row for each record. It holds the current revision, the review state, and the listing columns.
- `executive_action_revisions` holds one row for each revision as JSON.
- `executive_action_review_decisions` holds the decisions, including the code approval.
- `executive_action_documents` has one row for each document. It holds the publication date, which sets the next window. It holds the first revision that has the document. A correction row keeps its notice. It has no revision while it waits.
- `IFederalRegisterAdmissionStore` is the port of list and document admission. `PostgresCollectionJobStore` implements it. `IExecutiveActionReviewStore` is the review and publication port. `PostgresExecutiveActionReviewStore` implements it.
- `PublishedExecutiveAction` is the published record. It has the kind `executiveAction`.

## Code map

| Concern | Files |
|---|---|
| Parsing, URLs, window, keys | `src/CivicLens.Application/Collection/FederalRegister/` |
| Facts, revision, review | `src/CivicLens.Core/ExecutiveActions/` |
| Signer, identity, revision factory, handlers, ports | `src/CivicLens.Application/ExecutiveActions/` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Configuration and job binding | `src/CivicLens.Application/Collection/CollectionConfiguration.cs`, `src/CivicLens.Application/Collection/ConfiguredCollectionSource.cs`, `src/CivicLens.Application/Collection/Jobs/CollectionJobDefinition.cs` |
| Admission and scheduling | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.FederalRegister.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.Scheduling.cs` |
| Rows and review store | `src/CivicLens.Infrastructure/ExecutiveActions/` |
| Publication | `src/CivicLens.Application/Publication/ExecutiveActionRecordLoader.cs`, `src/CivicLens.Application/Publication/PublishedExecutiveActionBuilder.cs`, `src/CivicLens.Publication.Contracts/PublishedExecutiveAction.cs` |
| Pages | `src/CivicLens.Host/Publication/PublishedExecutiveActionPage.razor`, `src/CivicLens.Host/ExecutiveActions/ExecutiveActionEditor.razor` |
| Search | `src/CivicLens.PublicApi/Ingest/RecordReader.cs` |
| Sources and people | `config/federal-coverage.json` |

## Limits and planned work

- A record holds no document text and no summary. The next slice adds the body text. An AI summary and issue tags follow.
- A President's page lists the record. The talk and action counts do not count it.
- Determinations, presidential orders, and other documents are out of scope. The list filter leaves them out.
- A list longer than one page blocks the job. Move `backfillFrom` forward to fit.
- A document whose President is not a configured President blocks its job. Add the person to the configuration.
- The signing date comes from the Federal Register. Code does not check it against another source.
- Nine or more corrections that arrive before the original make revision 1 fail validation. The original is then not recorded.
- A correction waits with no visible sign when the job of its original fails or blocks for good.
