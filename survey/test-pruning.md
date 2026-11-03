# Test pruning survey

Commit: 9b365c85184cf201211305c0ed9379a01bd40560 (origin/main). Line numbers come from this commit. Paths are under tests/CivicLens.Tests.

## Summary

| Kind | Candidates |
|---|---|
| REDUNDANT | 32 |
| OBSOLETE | 0 |
| NO INVARIANT | 2 |
| Total | 34 |

- Estimated time saved by deleting all candidates: 6 seconds of test time (0.4 percent of the 1,404 seconds of summed test time).
- The suite ran once: 6,101 test cases, 348 classes, 29.5 minutes of wall time. One test failed (see Notes).
- Pruning cannot cut run time. Fewer than 40 tests qualify. Run time sits in a few Postgres and workspace classes (see Slow but needed).
- 90 Postgres collection classes cost 1,194 of 1,404 seconds (85 percent).
- The check excluded 1,733 test methods that docs name as protecting tests. No candidate is on that list.
- Many unprotected tests guard a distinct branch. The reviewers listed only tests with a proven cover. Doubtful tests are left out.
- Migration tests stay. The migrations and their Down paths still exist in src and docs. Roadmap R-63 still lists the backfill as open.
- Five reviewers split the folders. The Host reviewer did not read every one of the 253 unprotected Host tests in full.

### 10 slowest classes

| Class | Seconds |
|---|---|
| PostgresHouseVoteTests | 176.8 |
| PostgresBillStreamTests | 142.5 |
| ReviewWorkspaceTests | 140.2 |
| DatabaseCommandTests | 73.4 |
| PublishReleaseTests | 72.8 |
| HtmlParseParityTests | 46.9 |
| StatementReviewWorkspaceTests | 42.9 |
| BillLinksWorkspaceTests | 34.7 |
| ReaderReportWorkspaceTests | 34.7 |
| PostgresDocumentExtractionStoreTests | 31.9 |

## Candidates by feature

## Group A: Collection, documents, roster, collector

### Infrastructure/Collection
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| PostgresHouseVoteTests.TheLastPageOfAnEarlierSessionAdmitsTheFirstPageOfTheFollowingSession | Infrastructure/Collection/HouseVotes/PostgresHouseVoteTests.cs:116 | REDUNDANT | It asserts the same hand-over URL that the replay test asserts first, and the unit test asserts it too. | PostgresHouseVoteTests.ReplayingTheLastPageOfASessionFindsTheFollowingSessionsFirstPageJobKnownBecauseBothRunsShareOnePageKey | 176.8 |
| PostgresBillStreamTests.TheOlderCandidateReadsNoSummaryPageEvenAfterTheSummariesArrive | Infrastructure/Collection/BillStream/PostgresBillStreamTests.ActionDrafts.cs:385 | REDUNDANT | The covering test reads the evidence of the same candidates and asserts the same page lists, plus the subjects case. | PostgresBillStreamTests.AnOlderCandidateIsSupersededByTheOneThatCoversItAndEvidenceListsOnlyThePagesItWasBuiltFrom | 142.5 |
| PostgresBillStreamTests.ABillThatPassesAtACosponsorPageWithoutSummariesAdmitsOnlyActions | Infrastructure/Collection/BillStream/PostgresBillStreamTests.cs:768 | REDUNDANT | The covering test also passes at a later cosponsor page with zero summaries and asserts the single actions job with its exact URL. | PostgresBillStreamTests.ACoveredCosponsorOnALaterPagePassesTheGateAndAdmitsTheActionsFirstPageOnce | 142.5 |
| EvidenceProcessingWorkerTests.CosponsorAndActionsPagesAreExtractedByTheirOwnParsers | Infrastructure/Collection/Processing/EvidenceProcessingWorkerTests.cs:248 | REDUNDANT | It tests only the cosponsor page and asserts version and text. The unit tests assert the same for both parsers. | JobPreparationTests.ACosponsorPageIsExtractedOnceWithTheCosponsorParser and JobPreparationTests.AnActionsPageIsExtractedOnceWithTheActionsParser | 12.7 |
| EvidenceProcessingWorkerTests.ABillDetailWithSummariesAdmitsItsSummariesPageBesideTheOthers | Infrastructure/Collection/Processing/EvidenceProcessingWorkerTests.cs:271 | REDUNDANT | It counts follow-on jobs only. The covering test asserts the summaries URL, the link rows, and the stored summary count. | PostgresBillStreamTests.ACoveredSponsorBillWithSummariesAdmitsASummariesFirstPageBesideItsActionsPage | 12.7 |

### Collector
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| CollectorTests.RobotsDenyPreventsTargetRequest | Collector/Http/CollectorTests.cs:783 | REDUNDANT | It asserts RobotsDenied and one request for a plain disallow. The BOM test asserts the same on the same URL with a harder file. | CollectorTests.BomPrefixedRobotsDenyPreventsTargetRequest | 1.4 |

### Application/Collection
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| CollectionJobWorkerTests.DefaultLeaseMatchesExistingManualJobRunner | Application/Collection/Jobs/CollectionJobWorkerTests.cs:153 | NO INVARIANT | It asserts that a constant equals its own literal. No doc or contract names the 60 second lease. | None. The constant is `CollectionJobWorker.DefaultLeaseDuration`. | 4.8 |

## Group B: Statements, briefs, relationships, review, reports

### Infrastructure/Reports
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| HttpReportPullerTests.TheFirstPullHasNoCursorParameterSoTheServiceStartsFromTheFirstReport | tests/CivicLens.Tests/Infrastructure/Reports/HttpReportPullerTests.cs:45 | REDUNDANT | The first InlineData row pulls with a null cursor and asserts the exact URI, which has no query. | HttpReportPullerTests.AServiceAddressKeepsItsPathPrefixWithOrWithoutATrailingSlash (row `http://search.example.test/api`) | 0.0 |

### Infrastructure/Relationships
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| PostgresRelationshipReviewStoreTests.ATypeThatDoesNotFitTheTargetKindIsRefused | tests/CivicLens.Tests/Infrastructure/Relationships/PostgresRelationshipReviewStoreTests.cs:80 | REDUNDANT | It only asserts an ArgumentException from `ValidateShape`, which runs before the store and writes nothing. | RelationshipRevisionTests.ATypeThatDoesNotFitTheKindOfTheTargetIsRefused (same pair, protected) | 1.4 |

### Infrastructure/Statements
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| PostgresStatementReviewStoreTests.ASpanOutsideTheExtractionOrOnAnotherExtractionIsRefusedAndNothingIsWritten | tests/CivicLens.Tests/Infrastructure/Statements/PostgresStatementReviewStoreTests.cs:614 | REDUNDANT | `SaveStatementDraft` refuses the spans before it calls the store, so the database adds nothing. | StatementReviewHandlerTests.SaveRefusesSpansOutsideTheTextOrOnAnotherExtraction (same two cases, asserts nothing saved); StatementDraftRevisionTests.SpansMustCiteTheExtractionAndFitInsideItsText | 22.2 |

### Application/Statements
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| StatementReviewHandlerTests.SaveForwardsTheDateCheckedFlagSoTheStoreCanDropAnUnconfirmedAiDate | tests/CivicLens.Tests/Application/Statements/StatementReviewHandlerTests.cs:60 | REDUNDANT | It only asserts that a fake store received the flag. The real store test fails if the flag is not forwarded. | PostgresStatementReviewStoreTests.AnAiDateCannotBeApprovedAndSurvivesAPersonsSaveOnlyWhenTheyTickThatTheyCheckedIt | 0.1 |

### Core/Briefs
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| BriefRevisionTests.AWellFormedRevisionPassesTheShapeCheck | tests/CivicLens.Tests/Core/Briefs/BriefRevisionTests.cs:11 | REDUNDANT | The fixture window lasts exactly 168 hours, so the 168 row below validates the same revision. | BriefRevisionTests.ASpringWeekOfAHundredSixtySevenHoursAndAFallWeekOfAHundredSixtyNineAreBothValid (row 168); BriefRevisionTests.AnAnalysisRevisionFollowsTheCurrentRevisionAndKeepsModelTextWithoutASuggestion | 0.0 |

### Core/Relationships
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| RelationshipRevisionTests.AValidRevisionPasses | tests/CivicLens.Tests/Core/Relationships/RelationshipRevisionTests.cs:18 | REDUNDANT | Other tests call `ValidateShape` on the same valid revision and need it to pass. | RelationshipRevisionTests.AnAnalysisRevisionFollowsTheCurrentRevisionOfTheSameRecord | 0.0 |

## Group C: Publication and public API

### Infrastructure/Publication
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| PostgresApprovedOutsideReleaseTests.EveryKindCountsWhenApprovedBecauseTheCapAlarmMustSeeAllSevenKinds | Infrastructure/Publication/PostgresApprovedOutsideReleaseTests.cs:29 | REDUNDANT | The theory below already asserts a count of 1 for an approved draft in each of the seven draft tables. | PostgresApprovedOutsideReleaseTests.ARecordHeldByTheActiveReleaseDoesNotCountBecauseItAlreadyHasASlot (first assertion, one run per table) | 0.8 |
| PostgresPublicationRecordStateTests.AnUnchangedRecordKeepsItsOneRowBecauseOnlyChangesWriteToTheTable | Infrastructure/Publication/PostgresPublicationRecordStateTests.cs:65 | REDUNDANT | The big test asserts one open range for records that stay unchanged across releases (record A "1-2", record X "1-3"). | PostgresPublicationRecordStateTests.TheTableMatchesTheRecordsOfEveryReleaseAfterWithdrawalReturnRevisionChangeAndRollback | 4.1 |
| FileReleaseDirectoryTests.LinkingRejectsPathsThatCouldEscapeTheRelease | Infrastructure/Publication/FileReleaseDirectoryTests.cs:339 | REDUNDANT | Link source resolution calls ReleasePaths.SplitRelativePath, which the staging test checks with more inputs. | FileReleaseDirectoryTests.StagingRejectsPathsThatCouldEscapeTheRelease | 0.1 |
| FileReleaseDirectoryTests.LinkingRejectsMalformedSourceDirectoryNames | Infrastructure/Publication/FileReleaseDirectoryTests.cs:350 | REDUNDANT | Link source resolution calls ReleasePaths.RequireDirectoryName, which the read and delete test checks with the same inputs. | FileReleaseDirectoryTests.ReadsAndDeletesRejectMalformedDirectoryNames | 0.1 |

### PublicApi
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| ClientAddressTests.WithNoConfiguredRangesTheRemoteAddressIsTheClientEvenWhenTheHeaderIsPresent | PublicApi/Http/ClientAddressTests.cs:32 | REDUNDANT | An empty range list is the same branch as a remote address outside the ranges. | ClientAddressTests.TheForwardedHeaderIsIgnoredWhenTheConnectionComesFromOutsideTheCloudflareRangesBecauseAnyCallerCanSendIt | 0.0 |
| ReportEndpointsTests.TextOfExactlyTheMaximumLengthIsAccepted | PublicApi/Reports/ReportEndpointsTests.cs:113 | REDUNDANT | The multi-byte test accepts text of the same maximum length, a harder case, and also checks the stored row count. | ReportEndpointsTests.TextOfMultiByteCharactersUpToTheMaximumLengthIsAcceptedBecauseTheBodyCapCountsBytesNotCharacters | 5.8 |

### Publication/Contracts
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| PublishedStatementTests.RejectsAQuotationThatSplitsASurrogatePair | Publication/Contracts/PublishedStatementTests.cs:73 | REDUNDANT | Statement and document change both call PublishedEvidenceRules.RequireQuote, and the document change test checks both split points and the accept case. | PublishedDocumentChangeTests.RejectsACitationThatSplitsASurrogatePairBecauseTheQuoteWouldNotBeExactText (with AcceptsACitationThatQuotesAWholeSurrogatePair) | 0.0 |
| PublishedVoteTests.RejectsTheSameSourceTwiceBecauseEachExtractionIsEmbeddedOnce | Publication/Contracts/PublishedVoteTests.cs:187 | REDUNDANT | Vote and action both call PublishedEvidenceRules.RequireSources, and the action test is identical. | PublishedActionTests.RejectsTheSameSourceTwiceBecauseEachExtractionIsEmbeddedOnce | 0.0 |

### Application/Publication
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| PublishReleaseTests.RejectsRepublishingTheSameRevisionBecauseTheReleaseWouldChangeNothing | Application/Publication/PublishReleaseTests.cs:497 | REDUNDANT | All loaders share PublishedRecordLoader.RequirePublishable, and the action test also checks the exact message. | PublishActionTests.ActionAlreadyPublishedAtItsCurrentRevisionIsRejectedSoReleasesDoNotRepeatIt | 72.8 |
| PublishStatementTests.StatementAlreadyPublishedAtItsCurrentRevisionIsRejectedSoReleasesDoNotRepeatIt | Application/Publication/PublishStatementTests.cs:184 | REDUNDANT | Same shared check in PublishedRecordLoader.RequirePublishable, with the same assertions. | PublishActionTests.ActionAlreadyPublishedAtItsCurrentRevisionIsRejectedSoReleasesDoNotRepeatIt | 0.1 |
| PublishReleaseTests.RejectsDuplicateDraftIds | Application/Publication/PublishReleaseTests.cs:522 | REDUNDANT | One check in PublishRelease owns the distinct-records rule for every kind. | PublishStatementTests.ReleaseCannotNameTheSameRecordTwice | 72.8 |
| PublishExecutiveActionTests.TheCodeSubjectMayPublishItBecauseItIsAFactRecord | Application/Publication/PublishExecutiveActionTests.cs:94 | REDUNDANT | The worker runs as the same code owner actor and publishes a code-approved executive action, with stronger checks. | PublishExecutiveActionTests.TheWorkerPublishesAnExecutiveActionWhoeverApprovedIt | 0.4 |

## Group D: Host

### Worker configuration (Host composition)
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| DocumentChangeDraftConfigurationTests.TheWorkerRefusesAGateLimitWithoutAKeyOrWithAnInvalidLimit | tests/CivicLens.Tests/Host/DocumentChangeDraftConfigurationTests.cs:114 | REDUNDANT | It repeats the same two cases (limit without key, bad limit with key) that `AnalysisEnvironment.Read` owns for every prefix. | DocumentChangeDraftConfigurationTests.TheWorkerRefusesAPartialOrInvalidDraftingConfiguration. Only the gate prefix wiring is unique. | 7.3 |
| DocumentChangeDraftConfigurationTests.TheWorkerRefusesAStatementDraftingLimitWithoutAKeyOrWithAnInvalidLimit | tests/CivicLens.Tests/Host/DocumentChangeDraftConfigurationTests.cs:144 | REDUNDANT | It repeats the same two cases as the gate test and the analysis test through the same shared reader. | DocumentChangeDraftConfigurationTests.TheWorkerRefusesAPartialOrInvalidDraftingConfiguration. Only the drafting prefix wiring is unique. | 7.3 |

### Review CLI (analysis and publication commands)
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| AnalysisCommandTests.TheAiDrafterCannotActAsTheOwner | tests/CivicLens.Tests/Host/AnalysisCommandTests.cs:51 | REDUNDANT | The handler rule has a direct unit test, and `OwnerCommand.RunAsync` maps the exception to exit 2 for all commands. | RequeueAnalysesTests.OnlyAHumanOwnerRequeues (Application) and ActionCommandTests.TheAiDrafterCannotActAsTheOwner for the exit code. | 3.6 |
| AnalysisCommandTests.AnInvalidComparisonIdIsRejected | tests/CivicLens.Tests/Host/AnalysisCommandTests.cs:42 | REDUNDANT | The hash rule lives in `RequeueAnalyses` and the exit 2 mapping lives in `OwnerCommand.RunAsync`. | RequeueAnalysesTests.ASubjectIdMustBeALowercaseSha256 (Application, also checks uppercase) and ActionCommandTests.RequeueReportsZeroWhenNothingFailedAndRejectsAnIdThatIsNotAHash for exit 2. | 3.6 |
| ReleaseCommandTests.MatchesOnlyReleaseCommands | tests/CivicLens.Tests/Host/ReleaseCommandTests.cs:408 | REDUNDANT | `Matches` is a one-line pattern, and any `releases` test run through `Program` fails if the route breaks. | ReleaseCommandTests.PublishingRefusesToStartWithoutTheArtifactDirectoryBecauseANewPhotoNeedsItsCapture (exit 2 and exit 1 prove the route). | 5.0 |

### Review workspace (statements)
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| StatementReviewWorkspaceTests.PostsWithoutTheAntiforgeryTokenAreRefusedAndWriteNothingAsync | tests/CivicLens.Tests/Host/StatementReviewWorkspaceTests.cs:338 | REDUNDANT | One global `app.UseAntiforgery()` in ReviewWorkspace.cs:136 owns the rule, and the covering test asserts the same 400 and no write. | ReviewWorkspaceTests.ForgedPostsCachedPagesAndForeignHostsAreRefusedAsync (named in host.md). | 42.9 |

### Public rendering (glossary and official page)
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| GlossaryTests.EveryTermKeyIsUniqueSoNoTwoTermsShareAnAnchorOrAPopover | tests/CivicLens.Tests/Host/GlossaryTests.cs:34 | REDUNDANT | `Glossary.ByKey` uses `ToDictionary`, so a duplicate key throws when the type loads, and the wording hash covers every key and the count. | GlossaryTests.TheApprovedWordingIsUnchangedBecauseOnlyTheOwnerMayApproveANewWord. | 0.4 |
| PublishedOfficialPageTests.ThePageWithoutAnOfficialHasNoPortraitBecauseOnlyAProfileIsAboutOnePerson | tests/CivicLens.Tests/Host/PublishedOfficialPageTests.cs:393 | NO INVARIANT | It renders the Method page and finds no portrait div, so a break in official portrait logic would not fail it. | Portrait presence and absence are asserted on the official page by PublishedOfficialPageTests.AnOfficialWithNoPhotoShowsAHiddenPlaceholderInTheSameFrameAndSaysSo and the past Congress portrait test. | 0.6 |

## Group E: Roster configuration

### Application/Roster
| Test (Class.Method) | Line | Kind | Reason | Covering test or removed code | Class seconds |
|---|---|---|---|---|---|
| RosterSourceConfigurationTests.ARosterSourceValidatesWithoutACredentialOrCoverageList | tests/CivicLens.Tests/Application/Roster/RosterSourceConfigurationTests.cs:13 | REDUNDANT | It only calls Validate on a plain roster source, and the covering test validates the same source plus an executive source. | RosterSourceConfigurationTests.ASecondSourceOfTheOtherRosterKindIsAccepted | 0.1 |

## Slow but needed

These tests protect rules. These are ideas only. Nothing here changes a test.

- PostgresHouseVoteTests (177 s) and PostgresBillStreamTests (143 s): read-only assertions on one seeded bill or vote could share one database per class. Write tests keep a reset per test.
- ReviewWorkspaceTests (140 s) and the other workspace classes (about 330 s together): each test starts a new dotnet process and a new database. One host per class with unique record ids, or an in-process test server, would cut most of this.
- DatabaseCommandTests (73 s): four tests spool a receipt with a real collector process. One receipt per class could serve the replay tests.
- Command classes (Action, Vote, Statement, Relationship, Analysis, Brief, Roster, WorkerHealth): zero-count and usage cases only read. One migrated database per class would serve them. Usage cases could call the command class directly.
- Configuration classes (DocumentChangeDraftConfigurationTests, ReportPullConfigurationTests, EvidenceProcessingConfigurationTests): each theory row starts a worker process to check an exit code. Calling the configuration reader directly keeps the rule and drops the process cost.
- PublishReleaseTests (73 s): check where the time goes before you change it. This survey did not find the cause.
- PostgresDocumentExtractionStoreTests (32 s): read-only tests could share one seeded database.
- PostgresCollectionJobStoreTests: the group pacing tests each enqueue and claim real jobs. One seeded set of grouped sources could serve the read-only queue-key tests.
- Sleeps: PostgresWorkerHeartbeatStoreTests uses Task.Delay(300). PostgresIntervalWakeupTests waits 2.5 s. The PostgresHouseVoteTests backfill lock test uses Task.Delay(500). Use a database clock offset, a shorter interval, or a pg_locks poll.
- PostgresPublicationRecordStateTests: the 15,000 row timing test could use a template database with rows already inserted, or an opt-in category.
- PublicApi classes (SearchIndexTests, SearchEndpointsTests, ReportEndpointsTests): each test starts a full ApiHost. Tests that do not change the release could share one started host.
- PresentationArchitectureTests and ArchitectureTests read many files and assemblies per test. Cache the file list and the assembly metadata in a static lazy field.
- Postgres classes already clone a migrated template database, so migration cost is paid once per run. Keep it that way.

## Notes for the owner

- Test failure: Host.ReviewWorkspaceTests.AFailedServeKeepsTheSavedDecisionAndAPostFromAFreshPageServesItWithoutASecondOneAsync failed once in this run (6.8 s, Assert.Equal mismatch). The cause is not known. Run it again to see if it is flaky.
- Test defect: EvidencePagesTests.StartDraftRequiresAntiforgeryAndReplaysToTheSameDraft (EvidencePagesTests.cs:142) asserts created.StatusCode twice and never asserts replay.StatusCode.
- Test gap: ReleaseCommandTests.AnUnknownKindIsAUsageErrorThatNamesTheValidKinds checks only the first three kind names.
- The tests listed for Group D, Host, as REDUNDANT rest on one shared reader or one global check. Confirm that reading before you delete.
