# Search

## Purpose

The search service gives readers full-text search over every published record. It is also the public read API. It runs apart from the operational application, with its own Postgres database. It reads published releases only.

## Decisions

- The kind `executiveAction` indexes the saved Federal Register record as source text and has no summary text. Deploy the search service before the publisher. An older service refuses a release that holds the new kind.
- The service is its own Web SDK project, `CivicLens.PublicApi`. It references `CivicLens.Publication.Contracts` and the Npgsql package only. It has no path to operational storage, credentials, job handlers, or unpublished evidence.
- The service has its own Postgres database, named by `CIVIC_LENS_SEARCH_DATABASE`. It never connects to the operational database.
- The service reads the public release files over HTTP. The base address is `CIVIC_LENS_RELEASE_BASE_URL`. A dev or test server can serve the release root.
- The ingest polls `release.json` every 2 minutes with a conditional GET. It sends the ETag and the Last-Modified time that the server gave. An unchanged manifest costs one empty reply.
- The ingest reuses the contract rules. `PublicationRelease.Validate` checks the manifest, and the `Validate` method of each record type checks the record. Search copies no contract rule.
- One release is ingested in one transaction. A release that the service stored already only becomes the active release again. A release that is older than the newest stored release and was never stored is refused, logged, and the service keeps serving the active release.
- Each row stores a range of a record revision: its first release and its last release. The last release is null while the range is open. A release that drops, withdraws, or replaces a revision closes its range. A revision that returns later gets a new row, so a pinned search in the gap never sees it.
- A query reads the active release by default. The active release is the number in `release.json`, so the service follows a rollback of the static site. A query can pin another ingested release with `release=N`.
- The schema is a set of numbered SQL files built into the project as embedded resources. The service applies them in order at startup under a Postgres advisory lock. A `schema_migrations` table records them. This departs from the EF migrations of the operational database. The service uses plain Npgsql, so it has no model to migrate.
- Search weighs the text of a record in three levels. The headline has weight A. The summary, the quoted text, the bill title, the relationship reason and quote, and the brief paragraphs have weight B. The first 200,000 characters of the source text have weight C.
- The summary text stops at 50,000 characters, and the source text stops at `PublicationProtocol.SearchSourceTextCharacters`. A Postgres tsvector holds at most 1 MB, and these limits keep the worst case below it. The search page states the source limit.
- Queries use `websearch_to_tsquery('english', q)` and `ts_rank_cd`. A snippet comes from `ts_headline` over the first 50,000 characters of the record body. The snippet marks each match between U+E000 and U+E001, so a reader script never needs HTML from the service.
- Paging uses a keyset cursor, as the review UI does. The cursor carries its release, so all pages of one search read the same release. This service pages forward only, so the cursor has no direction.
- A request may name `sort` (`date`, `published`, `headline`, or `kind`) and `direction` (`asc` or `desc`). Without a sort, results follow the date order, and text ranks them. A sort overrides rank. The key `date` is the sort date, `published` is the first publication time, and `headline` sorts by the lower-case headline in code order (`lower(headline COLLATE pg_c_utf8) COLLATE "C"`). So case does not matter, "H.R. 10" comes before "H.R. 2", and accented letters sort after z. `kind` uses byte order. Dates and publication time default to newest first, and the other keys to ascending. Record ID breaks each tie in the same direction. A direction without a sort is refused.
- A sorted page has the cursor `s.<release>.<recordId>`. The query compares the key and record ID of each row with those of the cursor row, which it reads in the same release. Only the record ID is in the cursor, so the record ID is the only thing a client can forge. The cursor row must be in the release, or the page is empty. The cursors `d.` and `r.` belong to the date order and the ranked order.
- The public address will be `api.civic-lens.info`. Every response allows any origin. The service sets no cookie. The search routes check no credential. The report pull route checks one shared bearer token.
- The rate limiter is the ASP.NET built-in fixed window. It keys on `CF-Connecting-IP` only when the connection comes from a configured trusted proxy range. In production that range is loopback, because Caddy and the tunnel run on the same machine. Otherwise it keys on the remote address. An IPv6 client counts as its /64 network.
- A record file holds the full source text. The service indexes it and does not serve it. The API returns index rows and the paths of the record files.
- Publish writes `search.html` in every release. Its canonical address, when `CIVIC_LENS_PUBLIC_BASE_URL` is set, is the bare page without a query. The page fills its filters from the manifest. Issue and policy area names come from the publication catalog. A script calls the service at `CIVIC_LENS_PUBLIC_API_URL`. Without the variable, the page says that search is not available.
- The facet pages are the path for readers without JavaScript.
- Readers send a report with `POST /v1/reports`. The body is form-encoded, so the browser sends a CORS simple request and needs no preflight. The route keeps `Access-Control-Allow-Origin: *` and sets no cookie.
- The service stores nothing about the reporter. It keeps no IP address, no hash of one, and no contact field. The client address only keys the in-memory rate limit. The service logs neither it nor the query text of a search. `PublicApiApp` sets the log level of `Microsoft.AspNetCore` to warning, because the information request logs hold the request address with the query. Test: `SearchEndpointsTests.ASearchWritesNoQueryTextToTheLogsBecauseThePrivacyTableOfTheAboutPagePromisesNothingIsSaved`. A response sets no cookie. Tests: `SearchEndpointsTests.EveryResponseAllowsAnyOriginAndSetsNoCookie` and `ReportEndpointsTests.AReportAnswerSetsNoCookieBecauseTheAboutPageToldReadersThatNoCookieIsSet`.
- The checks run in this order: the shape of the form, the Turnstile check, then the database write. The route reads at most 32 KB of body. An unknown pair of record ID and revision answers 404.
- A report binds to a record revision in the `records` table, in any release range. A report on a replaced revision is accepted, because a reader may hold an old page. The row stores the kind from that table.
- Cloudflare Turnstile checks each report in the service. `TurnstileVerifier` sends the token and the secret to the siteverify API and nothing else. It needs `success` and the configured hostname. A 5 second timeout, a failed call, an unreadable reply, or the code "internal-error" gives 503, so the check fails closed.
- `TurnstileVerifier` is a concrete class at the Cloudflare boundary, as `ReleaseFeed` is at the release boundary. The service registers it only when a secret is set. Tests give `PublicApiApp.Build` a fake `HttpMessageHandler`, as they do for the release feed.
- Without `CIVIC_LENS_TURNSTILE_SECRET` the report route answers 503 "reports are not available".
- Publish writes `report.html` in every release, and every record page links to it; see [publication](publication.md#report-a-problem). The page needs `CIVIC_LENS_PUBLIC_API_URL` and `CIVIC_LENS_TURNSTILE_SITE_KEY`. Without them, it says that reporting is not available.
- Three limits protect the route. The `reports` rate limit policy allows 5 reports each hour for each client, and the global limiter still applies. A durable cap allows 200 reports in 24 hours and is checked inside the insert transaction. A unique index on record ID, revision, and payload hash makes a duplicate a no-op with the same success answer.
- The insert takes the advisory lock `civic-lens-search-reports` and holds it to the commit. Identity values then commit in order, so a pull never skips a late commit. No search transaction holds two keys.
- The operational application pulls the reports with `GET /internal/v1/reports?after=<cursor>&limit=<n>`. The route needs `CIVIC_LENS_REPORT_PULL_TOKEN`, at least 32 characters. Without the variable the route does not exist. The token is compared with `CryptographicOperations.FixedTimeEquals`.
- The cursor is `<epoch>.<sequence>` and moves forward only. The epoch is a uuid that the first migration creates once. A cursor of another epoch starts from the first report, so a reset or restore never skips reports. The puller dedupes by report ID.
- The `after` value acknowledges every report at or below it. The acknowledged sequence in `report_pull_state` never moves back.
- `IngestWorker` also purges reports. It deletes a report only when its sequence is at or below the acknowledged sequence and it is older than 7 days. This keeps one timer loop, so no new hosted service exists.
- The wire types `PulledReport`, `PulledReportBatch`, and `ReaderReportProtocol` are in `CivicLens.Publication.Contracts`. They are not release files, so `PublicationProtocol.SchemaVersion` does not change. The cursor format is owned by `ReportCursor` in the service, because it is opaque to the puller. `ReaderReportProtocol` also owns the retention days that publish shows on `about.html`.
- The operational application pulls the reports with the stage `reportPull`; see [operational pull](#operational-pull).
- The route `/internal/*` is not public. The Caddyfile answers `/internal/*` with 404 on the API host; see [Web server](../operations.md#web-server).

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- The service references the publication contracts and Npgsql only. Enforced by the project references. Tests: `ArchitectureTests.ProductionProjectsHaveOnlyApprovedDependencies` and `ArchitectureTests.ThePublicApiReadsReleasesOnlyAndReferencesNoOperationalAssembly`.
- A record is indexed only after the contract validation of its kind. Enforced by `RecordReader.Read`. Tests: `RecordReaderTests.ARecordThatBreaksItsContractIsRefusedByTheContractValidation` and `ReleaseIngestTests.ARecordThatFailsItsContractValidationIsNotIndexedAndTheIngestReportsItWhileTheOthersAreIndexed`.
- A record file matches its manifest entry by ID and revision. Enforced by `RecordReader.Read`. Test: `ReleaseIngestTests.ARecordFileThatDoesNotMatchItsManifestEntryIsNotIndexed`.
- A record with a NUL character is refused, because Postgres cannot store it. Enforced by `RecordReader.Read`. Test: `RecordReaderTests.ARecordWithANulCharacterIsRefusedBecausePostgresCannotStoreIt` and `RecordReaderTests.AnEscapedBackslashBeforeU0000IsPlainTextAndNotANulCharacter`.
- A record that makes the reader throw any error is skipped and reported, and the release still commits. Enforced by `ReleaseIngest`. No test; `PublicationProtocol.JsonOptions` and the contract `Validate` methods throw only `JsonException` or `InvalidDataException` today.
- A timeout of the feed is logged and the worker runs again at the next interval. Enforced by `IngestWorker`. Test: `IngestWorkerTests.AFeedTimeoutIsLoggedAndTheNextTickRunsSoOneSlowReplyNeverStopsTheService`.
- A fetch failure stores nothing from the release, and the next run tries again. Enforced by `ReleaseIngest` and `IndexTransaction`. Test: `ReleaseIngestTests.AFetchFailureRollsBackTheWholeReleaseAndTheNextRunTriesAgain`.
- An ingest of a stored release fetches nothing and only sets the active pointer. Enforced by `ReleaseIngest`. Tests: `ReleaseIngestTests.IngestingTheSameReleaseAgainChangesNothingSoARetryAfterACrashIsSafe`, `ReleaseIngestTests.AManifestThatGoesBackToAStoredReleaseOnlyMovesTheActivePointerAndFetchesNothing`, `ReleaseIngestTests.ARollbackThenANewerReleaseMakesTheNewerReleaseActive`, and `SearchEndpointsTests.WhenTheSiteGoesBackToAnEarlierReleaseTheReleaseEndpointAndAnUnpinnedSearchFollowAndAPinToTheLaterReleaseStillWorks`.
- A release older than the newest stored release, and never stored, is refused. Enforced by `ReleaseIngest`. Test: `ReleaseIngestTests.AnOlderReleaseThatWasNeverStoredIsRefusedBecauseItsRangesCannotBeOrdered`.
- An unchanged manifest causes no record request. Enforced by `ReleaseFeed`. Test: `ReleaseIngestTests.AnUnchangedManifestAnswers304AndTheIngestFetchesNoRecord`.
- A stored revision is not fetched again. Enforced by `ReleaseIngest`. Test: `ReleaseIngestTests.ARecordRevisionThatIsAlreadyStoredIsNotFetchedAgainSoACarriedRecordCostsNoRequest`.
- A release that replaces or drops a revision closes its range at the last release that held it. Enforced by `ReleaseIngest` and `IndexTransaction.CloseAsync`. Tests: `ReleaseIngestTests.AReplacedRevisionClosesItsRangeAtTheLastReleaseThatHeldItAndTheNewRevisionOpensAtTheNewRelease`, `ReleaseIngestTests.ADroppedRevisionClosesItsRangeAndAReturningRevisionGetsANewRangeWithoutAFetch`, and `ReleaseIngestTests.ARevisionThatIsInReleaseFiveGoneInSixAndBackInSevenIsInvisibleToASearchPinnedToSix`.
- A withdrawn record closes its range as a dropped record does, and the rows stay. A pinned earlier release still finds the record. Activating an earlier release shows the record again, with no fetch. A record that returns at a new revision gets a new row, and the old range stays closed. Enforced by `ReleaseIngest`. Tests: `ReleaseIngestTests.AWithdrawnRecordClosesItsRangeLikeADroppedOneAndAPinnedEarlierSearchStillFindsIt`, `ReleaseIngestTests.ARecordWithdrawnInSixAndReturnedInSevenInsertsTheNewRevisionWhileTheOldRangeStaysClosed`, `ReleaseIngestTests.ActivatingAReleaseBeforeTheWithdrawalFindsTheWithdrawnRecordAgainWithoutAFetch`.
- A report on a revision that a later release withdrew is still stored. Enforced by `ReportStore.StoreAsync`, which checks the stored revision rows. Test: `ReportStoreTests.AReportOnARevisionThatALaterReleaseWithdrewIsStillStoredBecauseTheReaderSawThatRevisionBeforeTheWithdrawal`.
- A pinned query sees the revisions that its release held. Enforced by the release range in `SearchIndex`. Tests: `SearchIndexTests.APinnedReleaseSeesTheRevisionThatReleaseHeldAndTheActiveReleaseSeesTheNewOne` and `SearchIndexTests.ARecordDroppedByALaterReleaseIsGoneFromTheActiveReleaseButStaysInThePinnedOne`.
- A release that the service never ingested answers 404 with "release not indexed". Enforced by `SearchEndpoints`. Test: `SearchEndpointsTests.APinnedReleaseThatWasNeverIngestedAnswers404WithTheReasonReleaseNotIndexed`.
- Text beyond the source limit is not searchable, and the vector stays under the Postgres limit. Enforced by `RecordReader` and `IndexedRecord`. Tests: `SearchIndexTests.TextBeyondTheSourceLimitIsNotSearchableAndTextBeforeItIs` and `SearchIndexTests.ASourceTextOfManyDistinctWordsStaysUnderThePostgresVectorLimit`.
- A limit never splits a surrogate pair. Enforced by `RecordReader`. Test: `RecordReaderTests.TheSourceLimitDoesNotSplitASurrogatePairBecauseAHalfPairIsNotValidText`.
- The record file cap holds the largest file that publish can write. No enforcer; the constants are separate copies. Test: `RecordReaderTests.TheRecordFileCapHoldsTheLargestFileThatPublishCanWriteBecauseTheTwoLimitsHaveSeparateOwners`.
- A headline match ranks above a match in the source text. Enforced by the weights in `IndexTransaction.InsertAsync`. Tests: `SearchIndexTests.AHeadlineMatchRanksAboveAMatchInTheSourceTextBecauseTheHeadlineStatesWhatTheRecordIsAbout` and `SearchIndexTests.ASummaryMatchRanksAboveASourceTextMatch`.
- Every kind is found by the text of its source. Enforced by `RecordReader`. Test: `SearchIndexTests.EveryKindIsFoundByTheTextOfItsSource`.
- A snippet is plain text with match markers, never HTML. Enforced by `SearchIndex` and the page script. Test: `SearchIndexTests.TheSnippetShowsTheMatchBetweenMarkersAndLeavesSourceMarkupAsPlainText`.
- Filters narrow the result by the manifest entry fields, and a date filter never matches a record without an event date. Enforced by `SearchIndex.Filters`. Tests: `SearchIndexTests.FiltersNarrowTheResultAndEachOneUsesTheManifestEntryField` and `SearchIndexTests.AFilterThatNeedsAnEventDateNeverMatchesARecordWithoutOne`.
- Without text, results follow the event day, else the UTC day of the first publication, newest first. This is the facet rule of `ReleaseRecordOrder`. `SearchIndex.SortDate` is its SQL copy in this service, because the service cannot reference the Application project. Records of one day follow the first publication time, newest first, then the record ID. Enforced by `SearchIndex`. Test: `SearchIndexTests.WithoutTextTheResultsFollowTheFacetRuleOfEventDayElseTheUtcFirstPublicationDayNewestFirst`.
- Cursor paging walks every record once, in order, with and without text. Enforced by `SearchCursor` and `SearchIndex`. Tests: `SearchIndexTests.CursorPagingWalksEveryRecordOnceInOrderWithNoRepeatAndNoGap` and `SearchRequestTests.TheCursorRoundTripsTheRankExactlyBecauseTheKeysetCompareUsesEquality`.
- A sorted walk visits every record once, in the order of the key, with ties broken by record ID, for each key and direction. Enforced by `SearchIndex.SortedSql`. Test: `SearchIndexTests.ASortedKeysetWalkVisitsEveryRecordOnceInTheOrderOfTheKeyWithTiesBrokenByRecordId`. A headline sort ignores case and the database locale, and an accented capital ties with its lower-case form. Test: `SearchIndexTests.AHeadlineSortLowercasesThenUsesCodeOrderSoCaseDoesNotMatterAndTheOrderDoesNotDependOnTheDatabaseLocale`.
- A cursor fits only a search of its own form, and a request without a sort uses the date order. Enforced by `SearchRequest.Parse`. Tests: `SearchRequestTests.ACursorMustFitTheOrderOfTheSearchBecauseAnotherFormMeansAnotherKeyset`, `SearchRequestTests.WithoutASortTheOrderStaysTheOldOneSoOldClientsAndTheirCursorsKeepWorking`, and `SearchIndexTests.ADefaultOrderSearchStillGivesTheOldDatedCursor`.
- A sorted cursor of a record that the release does not hold gives an empty page, not an error. Enforced by `SearchIndex.SortedSql`. Test: `SearchIndexTests.AForgedSortedCursorForARecordThatOnlyALaterReleaseHoldsGivesAnEmptyPageAndNoError`.
- A page holds at most the page size of the static pages, 50, so a sorted table can ask for a full page. Enforced by `SearchRequest.MaximumLimit`, which is `PublicationProtocol.PageSize`. Test: `SearchEndpointsTests.TheLimitIsHonoredUpToTheMaximumAndTheCursorContinuesInTheSameRelease`.
- A cursor keeps its release, and a named release that differs is refused. Enforced by `SearchRequest.Parse`. Tests: `SearchRequestTests.ACursorPinsItsReleaseAndANamedReleaseThatDiffersIsRefused` and `SearchEndpointsTests.TheLimitIsHonoredUpToTheMaximumAndTheCursorContinuesInTheSameRelease`.
- The query is at most 200 characters, a page holds at most 50 results, and each filter value is bounded. Enforced by `SearchRequest.Parse`. Tests: `SearchEndpointsTests.AQueryOfMoreThanTheMaximumLengthIsRefusedAndOneAtTheMaximumIsAccepted` and `SearchEndpointsTests.BadParametersAreRefusedWith400`.
- A statement that runs over the timeout stops, and the service answers 503. Enforced by the statement timeout of the reader connection. Test: `SearchEndpointsTests.AStatementThatRunsLongerThanTheTimeoutIsStoppedAndAnswers503`.
- The rate limiter trusts `CF-Connecting-IP` only from a configured range. Enforced by `ClientAddress`. Tests: `ClientAddressTests.AllAddressesOfOneIpv6Slash64AreOneClientBecauseAReaderHoldsTheWholeNetwork`, `ClientAddressTests.TheForwardedHeaderIsIgnoredWhenTheConnectionComesFromOutsideTheCloudflareRangesBecauseAnyCallerCanSendIt`, `RateLimitTests.TheRateLimitCountsEachForwardedClientSeparatelyWhenTheConnectionIsTrusted`, and `RateLimitTests.TheRateLimitIgnoresAForgedHeaderFromAnUntrustedConnectionSoRotatingItBuysNoMoreRequests`.
- Every response allows any origin and sets no cookie. Enforced by `PublicApiApp`. Test: `SearchEndpointsTests.EveryResponseAllowsAnyOriginAndSetsNoCookie`.
- A report is stored only for a record revision that the `records` table holds, with a valid record ID, category, and text. Enforced by `ReportSubmission.Parse`, `ReaderReportProtocol`, `PublicationProtocol.IsAnyRecordId`, and `ReportStore.StoreAsync`. The protocol owns the category and text rules, so the table has no CHECK for them. Tests: `PublicationProtocolTests.AnyRecordIdHasExactlyThe32Or64HexLengthsSoARouteCanRefuseAnIdBeforeTheLookup`, `ReportEndpointsTests.AnInvalidReportIsRefusedAndNoRowIsWrittenBecauseTheDatabaseHoldsOnlyReportsAboutRealRecordRevisions`, and `ReportEndpointsTests.AReportOnAReplacedRevisionIsAcceptedBecauseTheReaderMayHoldAnOldPage`.
- A report is stored once. Enforced by the unique index and `ON CONFLICT DO NOTHING`. Test: `ReportEndpointsTests.ADuplicateReportGetsTheSameAnswerAsTheFirstAndIsNotStoredTwiceSoARetryCostsNothing`.
- The database holds nothing about the reporter. Enforced by the table columns. Test: `ReportEndpointsTests.AValidReportIsStoredOnceWithTheKindAndTheActiveReleaseAndNothingAboutTheReader`.
- No report is stored when the Turnstile check fails, times out, or is not configured. Enforced by `ReportEndpoints` and `TurnstileVerifier`. Tests: `ReportEndpointsTests.AFailedBotCheckIsRefusedWithNoRowBecauseTheCheckStandsBeforeTheWrite`, `ReportEndpointsTests.AReplyForAnotherHostnameIsRefusedBecauseAPassedTokenOfAnotherSiteProvesNothingHere`, `ReportEndpointsTests.ABotCheckTimeoutFailsClosedWith503AndNoRow`, `ReportEndpointsTests.AnInternalErrorOfTheBotCheckIsUnavailableNotRejectedBecauseTheReaderDidNothingWrong`, `ReportEndpointsTests.ABotCheckReplyWithAnErrorStatusFailsClosedWith503AndNoRow`, `ReportEndpointsTests.ANullBotCheckReplyFailsClosedWith503AndNoRow`, and `ReportEndpointsTests.WithoutATurnstileSecretTheReportRouteAnswers503AndStoresNothing`.
- The report text never reaches Cloudflare. Enforced by `TurnstileVerifier`. Test: `ReportEndpointsTests.OnlyTheSecretAndTheTokenReachTheBotCheckBecauseTheReportTextIsNotCloudflaresBusiness`.
- The report route reads at most 32 KB and accepts form bodies only. The cap counts bytes, so 2,000 characters of 3-byte text fit. Enforced by `ReportEndpoints`. Tests: `ReportEndpointsTests.TextOfMultiByteCharactersUpToTheMaximumLengthIsAcceptedBecauseTheBodyCapCountsBytesNotCharacters`, `ReportEndpointsTests.ABodyOverTheRouteCapIsRefusedBeforeAnyCheckBecauseTheRouteIsOpenToAnyone`, and `ReportEndpointsTests.AJsonBodyIsRefusedBecauseOnlyTheFormEncodingNeedsNoPreflight`.
- Each client sends at most the configured number of reports each hour. Enforced by the `reports` rate limit policy. Test: `ReportEndpointsTests.EachClientMayReportOnlyTheConfiguredCountEachHourAndTheNextReportGets429`.
- The service stores at most the configured number of reports in 24 hours. Enforced by `ReportStore.StoreAsync` under the reports lock. Test: `ReportEndpointsTests.AtTheDailyCapReportsAreClosedWith503AndNothingMoreIsStored`.
- Reports commit in sequence order, so a pull never skips a late commit. Enforced by the reports lock in `ReportStore.StoreAsync`. Tests: `ReportStoreTests.AStoreWaitsForTheReportsLockBecauseTheSequenceOrderAndTheDailyCapDependOnIt` and `ReportStoreTests.TwoConcurrentStoresAtADailyCapOfOneStoreExactlyOneBecauseTheCountAndTheInsertShareTheLock`.
- The pull route needs the bearer token and does not exist without one. Enforced by `ReportEndpoints`. Tests: `ReportEndpointsTests.ThePullRefusesAMissingOrWrongTokenWith401` and `ReportEndpointsTests.WithoutAPullTokenThePullRouteDoesNotExist`.
- A token under 32 characters stops the service at startup. Enforced by `PublicApiOptions.ParsePullToken`. Test: `PublicApiOptionsTests.APullTokenUnder32CharactersIsRefusedAtStartupBecauseItGuardsEveryReport`.
- Pages come in sequence order and no report is skipped. Enforced by `ReportStore.PullAsync`. Test: `ReportStoreTests.PagesComeInSequenceOrderAndEachReportComesOnceSoThePullerMissesNothing`.
- A cursor of another epoch starts from the first report and acknowledges nothing. Enforced by `ReportStore.PullAsync`. Test: `ReportStoreTests.ACursorOfAnotherEpochStartsFromTheFirstReportAndAcknowledgesNothingSoAResetNeverSkipsReports`.
- The acknowledged sequence never moves back. Enforced by `ReportStore.PullAsync`. Test: `ReportStoreTests.TheAcknowledgementMovesForwardOnlySoAnOldCursorCannotMakeAPulledReportUnpurgeable`.
- The purge deletes only acknowledged reports older than 7 days. Enforced by `ReportStore.PurgeAsync`. Test: `ReportStoreTests.ThePurgeDeletesOnlyReportsThatAreAcknowledgedAndOlderThanSevenDays`.
- A pulled batch is validated before it is stored, and a bad batch keeps the cursor. Enforced by `PullReaderReports` and `PulledReportBatch.Validate`. Tests: `PulledReportTests.ABatchWithOneBadReportADuplicateIdOrABadCursorIsRefusedAsAWhole` and `ReaderReportPullTests.ABatchWithOneBadReportIsRefusedAsAWholeAndTheCursorStaysSoTheNextTickAsksForTheSameReports`.
- The reports and the cursor of a page are stored in one transaction. Enforced by `PostgresReaderReportStore.SavePulledAsync`. Test: `PostgresReaderReportStoreTests.ACursorThatCannotBeSavedLeavesNoReportsBehindSoAReplayNeverSkipsAndNeverHalfStores`.
- A report is stored once, whatever the epoch of the cursor. Enforced by the unique key on the report ID and `ON CONFLICT DO NOTHING`. Test: `PostgresReaderReportStoreTests.AReplayAfterAnEpochChangeStoresEachReportOnceAndKeepsTheResolutionOfOneThatIsAlreadyHandled`.
- A service that sends the same cursor again with reports does not hold the pull in a loop. The count of the pull equals the reports stored. Enforced by `PullReaderReports`. Test: `ReaderReportPullTests.AServiceThatRepeatsItsCursorWithReportsDoesNotHoldThePullInALoop`.
- Each pull asks with the cursor that the store saved. Enforced by `PullReaderReports`. Test: `ReaderReportPullTests.AllPagesAreStoredAndEachPullAsksWithTheCursorThatTheStoreSavedNeverOneThatTheApplicationMadeUp`.
- A service that is down counts one failure, keeps the cursor, and is asked again on the next tick. Enforced by `StageWorker`. Test: `ReaderReportPullTests.WhenTheServiceIsDownTheStageCountsAFailureKeepsTheCursorAndPullsAgainOnTheNextTick`.
- The pull sends the bearer token and never puts it in an error. Enforced by `HttpReportPuller`. Tests: `HttpReportPullerTests.ThePullSendsTheBearerTokenAndTheOpaqueCursorToThePullRouteAndReadsTheBatch` and `HttpReportPullerTests.AFailedCallThrowsWithoutTheTokenSoTheStageCountsItAndNeverLogsTheSecret`.
- The deadline of the stage is the next 5 minute mark of the database clock. Enforced by `PostgresIntervalWakeup`. Test: `PostgresIntervalWakeupTests.TheFiveMinuteWakeupDoesNotReturnBeforeItsDeadlineBecauseNothingNotifiesIt`.
- A service address keeps its path prefix. Enforced by `HttpReportPuller`. Test: `HttpReportPullerTests.AServiceAddressKeepsItsPathPrefixWithOrWithoutATrailingSlash`.
- The token length rule has one owner. Enforced by `ReaderReportProtocol.IsPullToken`, which `PublicApiOptions` and `ReportPullComposition` both call. Tests: `PublicApiOptionsTests.APullTokenUnder32CharactersIsRefusedAtStartupBecauseItGuardsEveryReport` and `ReportPullConfigurationTests.TheWorkerStopsAtStartupWhenTheReportPullIsSwitchedOnWithoutAUsableAddressAndTokenAndNeverPrintsTheToken`.
- The stage stops the worker at startup when its address or token is missing or invalid. Enforced by `ReportPullComposition`. Test: `ReportPullConfigurationTests.TheWorkerStopsAtStartupWhenTheReportPullIsSwitchedOnWithoutAUsableAddressAndTokenAndNeverPrintsTheToken`.
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

`SearchIndex` runs two kinds of query. With text, it matches `websearch_to_tsquery` and orders by `ts_rank_cd`, then by record ID. Without text, it orders by event day, else the UTC day of the first publication, then first publication time newest first, then record ID.

Each filter is one value: `kind`, `official`, `issue`, `policyArea`, `source`, and a `from` and `to` date range on the event date. The filters combine with AND.

Only a search with text returns a snippet. A search without text returns none.

### API

The search routes are GET and read-only. The report routes are the only writes. Errors use problem JSON.

| Route | Answer |
|---|---|
| `/v1/release` | The active release number and its publication time. 503 before the first ingest. |
| `/v1/search` | A page of results with `release`, `hits`, and `nextCursor`. Parameters: `q`, `kind`, `official`, `issue`, `policyArea`, `source`, `from`, `to`, `sort`, `direction`, `release`, `cursor`, and `limit`. |
| `/v1/records/{id}` | The index row of one record in the active release, or in the release of `release=N`. 404 when the release does not hold the record. |
| `POST /v1/reports` | Stores one reader report. Form fields: `recordId`, `revision`, `category`, `text` (optional), and `cf-turnstile-response`. Categories: `wrong-quote`, `wrong-date`, `wrong-attribution`, `missing-context`, `broken-source-link`, and `other`. Text holds at most 2,000 characters and is required for `other`. Answer: 202, also for a duplicate. |
| `GET /internal/v1/reports` | Internal. Bearer token. Parameters: `after` (the cursor) and `limit` (default 100, at most 200). Answer: `reports` and `nextCursor`. |

A result has the manifest entry fields, `firstRelease`, `lastRelease`, `pagePath`, `dataPath`, and the snippet. The paths are relative to the release root. The API does not serve record bodies.

| Status | Cause |
|---|---|
| 400 | A parameter or form field is not valid or exceeds its bound, or the Turnstile check failed. |
| 401 | The pull token is missing or wrong. |
| 404 | The release is not indexed, the release does not hold the record, or no table row holds the reported record revision. |
| 413 | The report body is over 32 KB. |
| 429 | The client passed the rate limit. |
| 503 | One of these cases. No release is indexed yet. The search ran over the timeout. Reports are not available. The Turnstile check is not available. The daily report cap is reached. |

### Configuration

| Variable | Meaning |
|---|---|
| `CIVIC_LENS_RELEASE_BASE_URL` | Required. The address of the release root. |
| `CIVIC_LENS_SEARCH_DATABASE` | Required. The Npgsql connection string of the search database. |
| `CIVIC_LENS_TRUSTED_PROXY_RANGES` | Optional. Comma-separated CIDR ranges of Cloudflare. Empty means that the remote address names the client. |
| `CIVIC_LENS_REQUESTS_PER_MINUTE` | Optional. The rate limit for each client, 1 or more. The default is 60. A lower value stops the service at startup. |
| `CIVIC_LENS_REPORTS_PER_HOUR` | Optional. The report limit for each client, 1 or more. The default is 5. The state is in memory, so a restart resets it. |
| `CIVIC_LENS_REPORTS_PER_DAY` | Optional. The cap of reports in 24 hours, 1 or more. The default is 200. |
| `CIVIC_LENS_TURNSTILE_SECRET` | Optional. The Turnstile secret. Without it, the report route answers 503. |
| `CIVIC_LENS_TURNSTILE_HOSTNAME` | Required when the secret is set. The hostname that siteverify must report. |
| `CIVIC_LENS_REPORT_PULL_TOKEN` | Optional. The bearer token of the pull route, 32 characters or more. Without it, the route does not exist. |
| `CIVIC_LENS_PUBLIC_API_URL` | Read by publish, not by the service. The address that `search.html` and `report.html` call. |
| `CIVIC_LENS_TURNSTILE_SITE_KEY` | Read by publish, not by the service. The public Turnstile site key of `report.html`. |

The poll interval is 2 minutes and the statement timeout is 2 seconds. Both are options of `PublicApiOptions`.

### Search page

`PublishedSearchPage` renders `search.html`. The form stays hidden until `public.js` runs. The kind filter lists the seven kinds. The official and source filters list the distinct values of the release manifest. The issue and policy area filters show catalog names. The script calls `/v1/search`, shows each result with its snippet, and loads more results with the cursor. It builds the snippet with `mark` elements, never with HTML from the service. The page address holds the query as `q` and the filters as `kind`, `official`, `issue`, `policyArea`, `source`, `from`, and `to`, with only the non-empty values. It never holds a cursor, a result page, or a release. The script ignores a `release` parameter. A submit writes history: `pushState` when the parameters change, `replaceState` when they stay the same. Typing does not change the address. Back and forward fill the form from the address and search again, without a history write. An address without parameters clears the results. A page load fills the form from the address, searches when a value is left, and rewrites the address from the form values. A select value that the release does not list becomes Any, and the browser empties a date that is not valid. A new search clears the earlier results and the cursor first. A jump to a fragment, such as the skip link, does not search again. The service owns the limits, and a 400 shows a message to change the words or filters.

### Operational pull

- The worker runs the stage `reportPull` when `CIVIC_LENS_REPORT_PULL=1`. It ticks at start and then every 5 minutes by the database clock, from `PostgresIntervalWakeup`. The publishing stage uses the same class with a one hour interval. The settings are in [operations](../operations.md#reader-report-queue).
- `StageWorker` runs `PullReaderReports` for each tick. The handler asks for a page with the saved cursor, checks the page, and stores it. It repeats until a page is empty or until the service sends the same cursor again. The queue is in [review](review.md#reader-reports).
- `HttpReportPuller` is a concrete class at the service boundary, like `TurnstileVerifier`. It sends the bearer token and the opaque cursor. The service address may hold a path prefix, with or without a trailing slash. A call has a hard limit of 30 seconds. The client reads at most 4 MB of reply, as the release feed of the service caps its reads. A failed status, an unreadable reply, or a timeout throws, and the stage counts one failure. The token is in no message.
- The application never parses or builds the cursor. It saves `NextCursor` as the service sent it and sends it back as the next `after` value.
- `PulledReportBatch.Validate` runs at the pull, because the service is an outside boundary. It checks the report rules of `ReaderReportProtocol`, the record ID rule of `PublicationProtocol.IsAnyRecordId`, the batch size, and the cursor length. A bad batch is refused as a whole. Its reports and its cursor are not stored.
- `PostgresReaderReportStore.SavePulledAsync` inserts the new reports with `ON CONFLICT (report_id) DO NOTHING` and saves the cursor in the same transaction. A replay after a change of epoch adds no second row and does not change a report that is resolved.
- Unlike the service, the operational application purges nothing. A resolved report stays in the queue.

## Storage and contracts

- The generated schema reference does not cover this database. The tables are `releases`, `records`, and `schema_migrations`, in `src/CivicLens.PublicApi/Schema/001_search_index.sql`. The tables `reader_reports` and `report_pull_state` are in `002_reader_reports.sql`. File `003_drop_report_release_number.sql` drops the unused release number column of `reader_reports`.
- `releases` holds each ingested release number and its publication time.
- `records` holds one row for each record revision: the manifest entry fields, the body text, the weighted `search` vector, `first_release`, and `last_release`. GIN indexes cover the vector and the three ID arrays.
- `reader_reports` holds `sequence` (identity), `report_id`, `record_id`, `revision_number`, `kind`, `category`, `body`, `payload_hash`, and `received_at`. CHECK constraints bound the numbers. A unique index covers record ID, revision, and payload hash.
- `report_pull_state` holds one row: the `epoch` uuid, created once, and the `acked_sequence`.
- `PulledReport`, `PulledReportBatch`, and `ReaderReportProtocol` define the pull wire format. `ReaderReportProtocol` also owns the retention days that publish shows on `about.html`.
- The service reads the same contract types as the static site: `PublicationRelease` and the seven record types. `PublicationProtocol.SearchSourceTextCharacters` and `PublicationProtocol.SearchPagePath` are shared with the page.

## Code map

| Concern | Files |
|---|---|
| Startup and options | `src/CivicLens.PublicApi/Program.cs`, `src/CivicLens.PublicApi/PublicApiApp.cs`, `src/CivicLens.PublicApi/PublicApiOptions.cs`, `src/CivicLens.PublicApi/RecordKinds.cs` |
| Schema | `src/CivicLens.PublicApi/Schema/SchemaMigrator.cs`, `src/CivicLens.PublicApi/Schema/SchemaStartup.cs`, `src/CivicLens.PublicApi/Schema/001_search_index.sql` |
| Ingest | `src/CivicLens.PublicApi/Ingest/IngestWorker.cs`, `src/CivicLens.PublicApi/Ingest/ReleaseIngest.cs`, `src/CivicLens.PublicApi/Ingest/ReleaseFeed.cs`, `src/CivicLens.PublicApi/Ingest/RecordReader.cs`, `src/CivicLens.PublicApi/Ingest/IndexedRecord.cs`, `src/CivicLens.PublicApi/Ingest/IndexTransaction.cs`, `src/CivicLens.PublicApi/Ingest/IngestResult.cs` |
| Search and API | `src/CivicLens.PublicApi/Search/SearchEndpoints.cs`, `src/CivicLens.PublicApi/Search/SearchRequest.cs`, `src/CivicLens.PublicApi/Search/SearchIndex.cs`, `src/CivicLens.PublicApi/Search/SearchCursor.cs`, `src/CivicLens.PublicApi/Search/SearchSortKey.cs`, `src/CivicLens.PublicApi/Search/RecordHit.cs`, `src/CivicLens.PublicApi/Search/SearchPage.cs`, `src/CivicLens.PublicApi/Search/ReleaseInfo.cs` |
| Rate limit | `src/CivicLens.PublicApi/Http/ClientAddress.cs` |
| HTTP helpers | `src/CivicLens.PublicApi/Http/ApiResults.cs`, `src/CivicLens.PublicApi/Http/QueryValues.cs` |
| Reader reports | `src/CivicLens.PublicApi/Reports/ReportEndpoints.cs`, `src/CivicLens.PublicApi/Reports/ReportSubmission.cs`, `src/CivicLens.PublicApi/Reports/ReportStore.cs`, `src/CivicLens.PublicApi/Reports/ReportCursor.cs`, `src/CivicLens.PublicApi/Reports/ReportReceipt.cs`, `src/CivicLens.PublicApi/Reports/TurnstileVerifier.cs`, `src/CivicLens.PublicApi/Reports/TurnstileSettings.cs`, `src/CivicLens.PublicApi/Schema/002_reader_reports.sql` |
| Report wire types | `src/CivicLens.Publication.Contracts/ReaderReportProtocol.cs`, `src/CivicLens.Publication.Contracts/PulledReport.cs`, `src/CivicLens.Publication.Contracts/PulledReportBatch.cs` |
| Operational pull | `src/CivicLens.Application/Reports/PullReaderReports.cs`, `src/CivicLens.Application/Reports/IReaderReportSource.cs`, `src/CivicLens.Application/StageWorker.cs`, `src/CivicLens.Infrastructure/Reports/HttpReportPuller.cs`, `src/CivicLens.Infrastructure/PostgresIntervalWakeup.cs`, `src/CivicLens.Host/Reports/ReportPullComposition.cs` |
| Search page | `src/CivicLens.Host/Publication/PublishedSearchPage.razor`, `src/CivicLens.Host/Publication/ReleaseRenderer.cs`, `src/CivicLens.Host/wwwroot/js/public.js` |
| Tests | `tests/CivicLens.Tests/PublicApi/`, `tests/CivicLens.Tests/Infrastructure/Reports/`, `tests/CivicLens.Tests/Application/Reports/` |

Run commands are in [operations](../operations.md#run-the-search-service).

## Limits and planned work

- One worker pulls. Two workers would pull the same pages. The unique report ID keeps the queue correct.
- The pull address and token are set by hand in the worker environment. [R-24](../roadmap.md#later) sets them in the container configuration.
- The queue has no rail link. The Review home page links it. The operational application sends no notice when a report arrives.
- The per-client report limit lives in memory and resets when the service restarts.
- There is no reporter identity. Abuse handling relies on Turnstile and the caps. A flood can fill the daily cap and close reports for the day.
- A duplicate sent at the daily cap gets 503 like any other report.
- The pull trusts the cursor of the token holder. A cursor above the highest sequence acknowledges reports that the puller never saw.
- A search database restored from a backup keeps its epoch. Reports made after the backup are lost, and the puller can skip new reports that reuse the lost sequences.
- The request log filter in `PublicApiApp` has no provider. A provider-specific `Logging` level, such as `Logging__Console__LogLevel__Default`, overrides it and logs the search text again. The [R-24](../roadmap.md#later) deploy must set none.
- The About page says a search keeps nothing. That stays true only while the Caddy deploy keeps no access logs for search requests. The Caddyfile sets that; see [Web server](../operations.md#web-server).
- Edge blocking of `/internal/*` is deferred to [R-24](../roadmap.md#later).
- The search API answers 200 for a withdrawn record in the pinned releases that hold it. A status 410 needs the ingest to read withdrawals and the API to change its status. This waits until after [R-24](../roadmap.md#later).
- Only the first full ingest is long, because it fetches every record inside one transaction. Later ingests fetch only new revisions. Readers are not blocked, because they read committed rows.
- A release that was never stored and is older than the newest stored release is refused and logged. The service keeps serving the active release. This happens only if the service missed a release and the site rolls back to it.
- One service runs per search database. Two replicas could move the active release back and forth when they read different copies of the site.
- A local proxy or tunnel in front of the API makes every reader share one rate limit bucket. The R-24 deployment must pass the client address as `CF-Connecting-IP` from a trusted range.
- The ingest reads and inserts one record at a time. It does not read every record before the first insert, because a full corpus could use more memory than the host has.
- A record that fails validation stays out of the index. Later releases try it again, because it is not stored.
- The index keeps every revision of every release. Storage grows with published revisions. Review this with the retention rule of [publication](publication.md#limits-and-planned-work).
- A search without text returns no snippet.
- A snippet scans the first 50,000 characters of the record body. A match later in the body gives a snippet from the start of the body.
- The `Dockerfile` target `search` builds the service image. [Container images](../operations.md#container-images) describes it. [R-24](../roadmap.md#later) adds the cloud deployment.
- After the first deploy, public MCP ([R-25](../roadmap.md#later)) will use this API and the same release pinning.
