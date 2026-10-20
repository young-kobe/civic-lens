# Search

## Purpose

The search service gives readers full-text search over every published record. It is also the public read API. It runs apart from the operational application, with its own Postgres database. It reads published releases only.

## Decisions

- The service is its own Web SDK project, `CivicLens.PublicApi`. It references `CivicLens.Publication.Contracts` and the Npgsql package only. It has no path to operational storage, credentials, job handlers, or unpublished evidence.
- The service has its own Postgres database, named by `CIVIC_LENS_SEARCH_DATABASE`. It never connects to the operational database.
- The service reads the public release files over HTTP. The base address is `CIVIC_LENS_RELEASE_BASE_URL`. A dev or test server can serve the release root.
- The ingest polls `release.json` every 2 minutes with a conditional GET. It sends the ETag and the Last-Modified time that the server gave. An unchanged manifest costs one empty reply.
- The ingest reuses the contract rules. `PublicationRelease.Validate` checks the manifest, and the `Validate` method of each record type checks the record. Search copies no contract rule.
- One release is ingested in one transaction. A release that the service stored already only becomes the active release again. A release that is older than the newest stored release and was never stored is refused, logged, and the service keeps serving the active release.
- Each row stores a range of a record revision: its first release and its last release. The last release is null while the range is open. A release that drops or replaces a revision closes its range. A revision that returns later gets a new row, so a pinned search in the gap never sees it.
- A query reads the active release by default. The active release is the number in `release.json`, so the service follows a rollback of the static site. A query can pin another ingested release with `release=N`.
- The schema is a set of numbered SQL files built into the project as embedded resources. The service applies them in order at startup under a Postgres advisory lock. A `schema_migrations` table records them. This departs from the EF migrations of the operational database. The service uses plain Npgsql, so it has no model to migrate.
- Search weighs the text of a record in three levels. The headline has weight A. The summary, the quoted text, the bill title, the relationship reason and quote, and the brief paragraphs have weight B. The first 200,000 characters of the source text have weight C.
- The summary text stops at 50,000 characters, and the source text stops at `PublicationProtocol.SearchSourceTextCharacters`. A Postgres tsvector holds at most 1 MB, and these limits keep the worst case below it. The search page states the source limit.
- Queries use `websearch_to_tsquery('english', q)` and `ts_rank_cd`. A snippet comes from `ts_headline` over the first 50,000 characters of the record body. The snippet marks each match between U+E000 and U+E001, so a reader script never needs HTML from the service.
- Paging uses a keyset cursor, as the review UI does. The cursor carries its release, so all pages of one search read the same release. This service pages forward only, so the cursor has no direction.
- The public address will be `api.civic-lens.info`. Every response allows any origin. The service sets no cookie and checks no credential.
- The rate limiter is the ASP.NET built-in fixed window. It keys on `CF-Connecting-IP` only when the connection comes from a configured Cloudflare range. Otherwise it keys on the remote address. An IPv6 client counts as its /64 network.
- A record file holds the full source text. The service indexes it and does not serve it. The API returns index rows and the paths of the record files.
- Publish writes `search.html` in every release. The page fills its filters from the manifest. Issue and policy area names come from the publication catalog. A script calls the service at `CIVIC_LENS_PUBLIC_API_URL`. Without the variable, the page says that search is not available.
- The facet pages are the path for readers without JavaScript.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- The service references the publication contracts and Npgsql only. Enforced by the project references. Tests: `ArchitectureTests.ProductionProjectsHaveOnlyApprovedDependencies` and `ArchitectureTests.ThePublicApiReadsReleasesOnlyAndReferencesNoOperationalAssembly`.
- A record is indexed only after the contract validation of its kind. Enforced by `RecordReader.Read`. Tests: `RecordReaderTests.ARecordThatBreaksItsContractIsRefusedByTheContractValidation` and `ReleaseIngestTests.ARecordThatFailsItsContractValidationIsNotIndexedAndTheIngestReportsItWhileTheOthersAreIndexed`.
- A record file matches its manifest entry by ID and revision. Enforced by `RecordReader.Read`. Test: `ReleaseIngestTests.ARecordFileThatDoesNotMatchItsManifestEntryIsNotIndexed`.
- A record with a NUL character is refused, because Postgres cannot store it. Enforced by `RecordReader.Read`. Test: `RecordReaderTests.ARecordWithANulCharacterIsRefusedBecausePostgresCannotStoreIt` and `RecordReaderTests.AnEscapedBackslashBeforeU0000IsPlainTextAndNotANulCharacter`.
- A record that makes the reader throw any error is skipped and reported, and the release still commits. Enforced by `ReleaseIngest`. Test: `ReleaseIngestTests.ARecordThatMakesTheReaderThrowAnyOtherErrorIsSkippedAndReportedAndTheReleaseStillCommits`.
- A timeout of the feed is logged and the worker runs again at the next interval. Enforced by `IngestWorker`. Test: `IngestWorkerTests.AFeedTimeoutIsLoggedAndTheNextTickRunsSoOneSlowReplyNeverStopsTheService`.
- A fetch failure stores nothing from the release, and the next run tries again. Enforced by `ReleaseIngest` and `IndexTransaction`. Test: `ReleaseIngestTests.AFetchFailureRollsBackTheWholeReleaseAndTheNextRunTriesAgain`.
- An ingest of a stored release fetches nothing and only sets the active pointer. Enforced by `ReleaseIngest`. Tests: `ReleaseIngestTests.IngestingTheSameReleaseAgainChangesNothingSoARetryAfterACrashIsSafe`, `ReleaseIngestTests.AManifestThatGoesBackToAStoredReleaseOnlyMovesTheActivePointerAndFetchesNothing`, `ReleaseIngestTests.ARollbackThenANewerReleaseMakesTheNewerReleaseActive`, and `SearchEndpointsTests.WhenTheSiteGoesBackToAnEarlierReleaseTheReleaseEndpointAndAnUnpinnedSearchFollowAndAPinToTheLaterReleaseStillWorks`.
- A release older than the newest stored release, and never stored, is refused. Enforced by `ReleaseIngest`. Test: `ReleaseIngestTests.AnOlderReleaseThatWasNeverStoredIsRefusedBecauseItsRangesCannotBeOrdered`.
- An unchanged manifest causes no record request. Enforced by `ReleaseFeed`. Test: `ReleaseIngestTests.AnUnchangedManifestAnswers304AndTheIngestFetchesNoRecord`.
- A stored revision is not fetched again. Enforced by `ReleaseIngest`. Test: `ReleaseIngestTests.ARecordRevisionThatIsAlreadyStoredIsNotFetchedAgainSoACarriedRecordCostsNoRequest`.
- A release that replaces or drops a revision closes its range at the last release that held it. Enforced by `ReleaseIngest` and `IndexTransaction.CloseAsync`. Tests: `ReleaseIngestTests.AReplacedRevisionClosesItsRangeAtTheLastReleaseThatHeldItAndTheNewRevisionOpensAtTheNewRelease`, `ReleaseIngestTests.ADroppedRevisionClosesItsRangeAndAReturningRevisionGetsANewRangeWithoutAFetch`, and `ReleaseIngestTests.ARevisionThatIsInReleaseFiveGoneInSixAndBackInSevenIsInvisibleToASearchPinnedToSix`.
- A pinned query sees the revisions that its release held. Enforced by the release range in `SearchIndex`. Tests: `SearchIndexTests.APinnedReleaseSeesTheRevisionThatReleaseHeldAndTheActiveReleaseSeesTheNewOne` and `SearchIndexTests.ARecordDroppedByALaterReleaseIsGoneFromTheActiveReleaseButStaysInThePinnedOne`.
- A release that the service never ingested answers 404 with "release not indexed". Enforced by `SearchEndpoints`. Test: `SearchEndpointsTests.APinnedReleaseThatWasNeverIngestedAnswers404WithTheReasonReleaseNotIndexed`.
- Text beyond the source limit is not searchable, and the vector stays under the Postgres limit. Enforced by `RecordReader` and `IndexedRecord`. Tests: `SearchIndexTests.TextBeyondTheSourceLimitIsNotSearchableAndTextBeforeItIs` and `SearchIndexTests.ASourceTextOfManyDistinctWordsStaysUnderThePostgresVectorLimit`.
- A limit never splits a surrogate pair. Enforced by `RecordReader`. Test: `RecordReaderTests.TheSourceLimitDoesNotSplitASurrogatePairBecauseAHalfPairIsNotValidText`.
- The record file cap holds the largest file that publish can write. No enforcer; the constants are separate copies. Test: `RecordReaderTests.TheRecordFileCapHoldsTheLargestFileThatPublishCanWriteBecauseTheTwoLimitsHaveSeparateOwners`.
- A headline match ranks above a match in the source text. Enforced by the weights in `IndexTransaction.InsertAsync`. Tests: `SearchIndexTests.AHeadlineMatchRanksAboveAMatchInTheSourceTextBecauseTheHeadlineStatesWhatTheRecordIsAbout` and `SearchIndexTests.ASummaryMatchRanksAboveASourceTextMatch`.
- Every kind is found by the text of its source. Enforced by `RecordReader`. Test: `SearchIndexTests.EveryKindIsFoundByTheTextOfItsSource`.
- A snippet is plain text with match markers, never HTML. Enforced by `SearchIndex` and the page script. Test: `SearchIndexTests.TheSnippetShowsTheMatchBetweenMarkersAndLeavesSourceMarkupAsPlainText`.
- Filters narrow the result by the manifest entry fields, and a date filter never matches a record without an event date. Enforced by `SearchIndex.Filters`. Tests: `SearchIndexTests.FiltersNarrowTheResultAndEachOneUsesTheManifestEntryField` and `SearchIndexTests.AFilterThatNeedsAnEventDateNeverMatchesARecordWithoutOne`.
- Without text, results follow the event date, then the first publication time, newest first, and undated records come last. Enforced by `SearchIndex`. Test: `SearchIndexTests.WithoutTextTheResultsFollowTheEventDateThenTheFirstPublicationTimeNewestFirstAndUndatedRecordsComeLast`.
- Cursor paging walks every record once, in order, with and without text. Enforced by `SearchCursor` and `SearchIndex`. Tests: `SearchIndexTests.CursorPagingWalksEveryRecordOnceInOrderWithNoRepeatAndNoGap` and `SearchRequestTests.TheCursorRoundTripsTheRankExactlyBecauseTheKeysetCompareUsesEquality`.
- A cursor keeps its release, and a named release that differs is refused. Enforced by `SearchRequest.Parse`. Tests: `SearchRequestTests.ACursorPinsItsReleaseAndANamedReleaseThatDiffersIsRefused` and `SearchEndpointsTests.TheLimitIsHonoredUpToTheMaximumAndTheCursorContinuesInTheSameRelease`.
- The query is at most 200 characters, a page holds at most 50 results, and each filter value is bounded. Enforced by `SearchRequest.Parse`. Tests: `SearchEndpointsTests.AQueryOfMoreThanTheMaximumLengthIsRefusedAndOneAtTheMaximumIsAccepted` and `SearchEndpointsTests.BadParametersAreRefusedWith400`.
- A statement that runs over the timeout stops, and the service answers 503. Enforced by the statement timeout of the reader connection. Test: `SearchEndpointsTests.AStatementThatRunsLongerThanTheTimeoutIsStoppedAndAnswers503`.
- The rate limiter trusts `CF-Connecting-IP` only from a configured range. Enforced by `ClientAddress`. Tests: `ClientAddressTests.AllAddressesOfOneIpv6Slash64AreOneClientBecauseAReaderHoldsTheWholeNetwork`, `ClientAddressTests.TheForwardedHeaderIsIgnoredWhenTheConnectionComesFromOutsideTheCloudflareRangesBecauseAnyCallerCanSendIt`, `RateLimitTests.TheRateLimitCountsEachForwardedClientSeparatelyWhenTheConnectionIsTrusted`, and `RateLimitTests.TheRateLimitIgnoresAForgedHeaderFromAnUntrustedConnectionSoRotatingItBuysNoMoreRequests`.
- Every response allows any origin and sets no cookie. Enforced by `PublicApiApp`. Test: `SearchEndpointsTests.EveryResponseAllowsAnyOriginAndSetsNoCookie`.
- Each schema file runs once, even when two services start together. Enforced by `SchemaMigrator`. Test: `SchemaMigratorTests.TwoServicesThatStartTogetherApplyEachFileOnceAndRecordItInSchemaMigrations`.
- Every release holds `search.html`, written before the manifest. Enforced by `PublishRelease`. Test: `PublishReleaseTests.EveryReleaseHoldsASearchPageThatIsWrittenBeforeTheManifestAndListsTheReleaseRecords`.
- The search page states the source text limit, and it says when search is not available. Enforced by `PublishedSearchPage`. Tests: `ReleaseRendererTests.SearchPageStatesTheSourceTextLimitSoReadersKnowWhatSearchReads` and `ReleaseRendererTests.SearchPageWithoutAServiceAddressSaysThatSearchIsNotAvailableAndShowsNoForm`.

## Behavior

### Ingest

`IngestWorker` runs `ReleaseIngest` when the service starts and then every 2 minutes. A failed run logs the error and waits for the next interval.

1. `ReleaseFeed` sends a conditional GET for `release.json`. A 304 reply ends the run.
2. It checks the size against `PublicationProtocol.MaximumManifestBytes`. It reads the manifest under `PublicationProtocol.JsonOptions` and calls `PublicationRelease.Validate`.
3. `ReleaseIngest` opens one transaction and takes an advisory lock. If the manifest release is stored already, it only sets the active release and ends the run.
4. It stores the release row. It reads the stored revisions. A revision that the manifest lists and the table holds only as closed gets a new row that copies the stored text. An open revision that the manifest lacks is closed at the newest stored release.
5. For each listed revision that the table lacks, it fetches `records/{id}.json`. `RecordReader` reads the file as the type of its kind and calls its `Validate` method. It builds the weighted texts and the service inserts the row. It then sets the active release.
6. A record that fails to read or validate, for any reason except cancellation, is skipped and reported in the result. The worker logs it. A failed fetch or a database error rolls the transaction back.
7. The feed keeps the ETag and Last-Modified values after the release is stored, so a failed run reads the manifest again.

### Search

`SearchIndex` runs two kinds of query. With text, it matches `websearch_to_tsquery` and orders by `ts_rank_cd`, then by record ID. Without text, it orders by event date, then first publication time, then record ID. A record without an event date sorts last.

Each filter is one value: `kind`, `official`, `issue`, `policyArea`, `source`, and a `from` and `to` date range on the event date. The filters combine with AND.

Only a search with text returns a snippet. A search without text returns none.

### API

All routes are GET and read-only. Errors use problem JSON.

| Route | Answer |
|---|---|
| `/v1/release` | The active release number and its publication time. 503 before the first ingest. |
| `/v1/search` | A page of results with `release`, `hits`, and `nextCursor`. Parameters: `q`, `kind`, `official`, `issue`, `policyArea`, `source`, `from`, `to`, `release`, `cursor`, and `limit`. |
| `/v1/records/{id}` | The index row of one record in the active release, or in the release of `release=N`. 404 when the release does not hold the record. |

A result has the manifest entry fields, `firstRelease`, `lastRelease`, `pagePath`, `dataPath`, and the snippet. The paths are relative to the release root. The API does not serve record bodies.

| Status | Cause |
|---|---|
| 400 | A parameter is not valid or exceeds its bound. |
| 404 | The release is not indexed, or the release does not hold the record. |
| 429 | The client passed the rate limit. |
| 503 | No release is indexed yet, or the search ran over the timeout. |

### Configuration

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_RELEASE_BASE_URL` | Required. The address of the release root. |
| `CIVIC_LENS_SEARCH_DATABASE` | Required. The Npgsql connection string of the search database. |
| `CIVIC_LENS_TRUSTED_PROXY_RANGES` | Optional. Comma-separated CIDR ranges of Cloudflare. Empty means that the remote address names the client. |
| `CIVIC_LENS_REQUESTS_PER_MINUTE` | Optional. The rate limit for each client, 1 or more. The default is 60. A lower value stops the service at startup. |
| `CIVIC_LENS_PUBLIC_API_URL` | Read by publish, not by the service. The address that `search.html` calls. |

The poll interval is 2 minutes and the statement timeout is 2 seconds. Both are options of `PublicApiOptions`.

### Search page

`PublishedSearchPage` renders `search.html`. The form stays hidden until `app.js` runs. The kind filter lists the six kinds. The official and source filters list the distinct values of the release manifest. The issue and policy area filters show catalog names. The script calls `/v1/search`, shows each result with its snippet, and loads more results with the cursor. It builds the snippet with `mark` elements, never with HTML from the service.

## Storage and contracts

- The generated schema reference does not cover this database. The tables are `releases`, `records`, and `schema_migrations`, in `src/CivicLens.PublicApi/Schema/001_search_index.sql`.
- `releases` holds each ingested release number and its publication time.
- `records` holds one row for each record revision: the manifest entry fields, the body text, the weighted `search` vector, `first_release`, and `last_release`. GIN indexes cover the vector and the three ID arrays.
- The service reads the same contract types as the static site: `PublicationRelease` and the six record types. `PublicationProtocol.SearchSourceTextCharacters` and `PublicationProtocol.SearchPagePath` are shared with the page.

## Code map

| Concern | Files |
|---|---|
| Startup and options | `src/CivicLens.PublicApi/Program.cs`, `src/CivicLens.PublicApi/PublicApiApp.cs`, `src/CivicLens.PublicApi/PublicApiOptions.cs`, `src/CivicLens.PublicApi/RecordKinds.cs` |
| Schema | `src/CivicLens.PublicApi/Schema/SchemaMigrator.cs`, `src/CivicLens.PublicApi/Schema/SchemaStartup.cs`, `src/CivicLens.PublicApi/Schema/001_search_index.sql` |
| Ingest | `src/CivicLens.PublicApi/Ingest/IngestWorker.cs`, `src/CivicLens.PublicApi/Ingest/ReleaseIngest.cs`, `src/CivicLens.PublicApi/Ingest/ReleaseFeed.cs`, `src/CivicLens.PublicApi/Ingest/RecordReader.cs`, `src/CivicLens.PublicApi/Ingest/IndexedRecord.cs`, `src/CivicLens.PublicApi/Ingest/IndexTransaction.cs`, `src/CivicLens.PublicApi/Ingest/IngestResult.cs` |
| Search and API | `src/CivicLens.PublicApi/Search/SearchEndpoints.cs`, `src/CivicLens.PublicApi/Search/SearchRequest.cs`, `src/CivicLens.PublicApi/Search/SearchIndex.cs`, `src/CivicLens.PublicApi/Search/SearchCursor.cs`, `src/CivicLens.PublicApi/Search/RecordHit.cs`, `src/CivicLens.PublicApi/Search/SearchPage.cs`, `src/CivicLens.PublicApi/Search/ReleaseInfo.cs` |
| Rate limit | `src/CivicLens.PublicApi/Http/ClientAddress.cs` |
| Search page | `src/CivicLens.Host/Publication/PublishedSearchPage.razor`, `src/CivicLens.Host/Publication/ReleaseRenderer.cs`, `src/CivicLens.Host/wwwroot/js/app.js` |
| Tests | `tests/CivicLens.Tests/PublicApi/` |

Run commands are in [operations](../operations.md#run-the-search-service).

## Limits and planned work

- Reader reports on a published record are planned in [R-40](../roadmap.md#next) as the second part. They are not built.
- Only the first full ingest is long, because it fetches every record inside one transaction. Later ingests fetch only new revisions. Readers are not blocked, because they read committed rows.
- A release that was never stored and is older than the newest stored release is refused and logged. The service keeps serving the active release. This happens only if the service missed a release and the site rolls back to it.
- One service runs per search database. Two replicas could move the active release back and forth when they read different copies of the site.
- A local proxy or tunnel in front of the API makes every reader share one rate limit bucket. The R-24 deployment must pass the client address as `CF-Connecting-IP` from a trusted range.
- The ingest reads and inserts one record at a time. It does not read every record before the first insert, because a full corpus could use more memory than the host has.
- A record that fails validation stays out of the index. Later releases try it again, because it is not stored.
- The index keeps every revision of every release. Storage grows with published revisions. Review this with the retention rule of [publication](publication.md#limits-and-planned-work).
- A search without text returns no snippet.
- A snippet scans the first 50,000 characters of the record body. A match later in the body gives a snippet from the start of the body.
- The service has no Dockerfile. [R-24](../roadmap.md#later) adds the container.
- Public MCP ([R-25](../roadmap.md#next)) will use this API and the same release pinning.
