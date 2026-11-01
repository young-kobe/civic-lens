# Bills

## Purpose

The bills feature follows the Congress.gov bill list each day. It fetches the detail, cosponsors, actions, summaries, and legislative subjects of each bill as durable jobs. It also fetches the official text of a bill that has no Congress.gov summary. A relevance gate decides which bills matter. Each passed bill version with complete evidence becomes an action candidate, which [action drafting](actions.md) drains.

## Decisions

- One durable job fetches one page. A page job never loops over pages.
- A `congressBillList` source has no `coverage`. It covers every configured person with a `bioguideId`, from `backfillFrom` on. This is the one exception to the coverage rule.
- `ListedPersonIdsOf` returns no people for this source, so source labels do not list every member.
- A UTC day is closed 6 hours after it ends. The request starts one day early and ends one day late, so a late update is not missed.
- The list uses the fixed sort `updateDate+asc`, so every page of a pass has the same row order.
- Congress.gov pages by offset. A bill updated during a pass leaves its place and can push another bill past the reader. One pass is therefore not enough: the cursor moves only after a pass that admits no new bills.
- The parser builds the detail URL itself and ignores the item `url`.
- An item with an unknown bill type is skipped and counted, not malformed. One odd record cannot block a window. Congress.gov lists historical documents among bills, such as type `CEN_DOC_H` from the 16th Congress.
- Preparation selects its work from `apiResource`, never from the URL text.
- A deferred bill job would be lost. Admission therefore throws `InvalidDataException` and saves nothing when the budget defers a job.
- Configuration validation checks the 251-job rule in [Admission](#admission), so a valid revision never defers a job.
- `BillGate` and `CongressBillRenderer` are pure types, so their tests need no database.
- The renderer writes one fact per line, so each fact is a clean citation span.
- A summaries page holds one summary (`limit=1`), so each summary gets its own extraction.
- The summaries extraction version includes the document extractor version. A change to the HTML rules therefore changes the identity of the summary extraction.
- The extraction ID is fixed by the attempt and the extraction version, and a replay with different text fails. A change to rendered text or parser output therefore needs a new version.
- The summaries chain never blocks a candidate, because a record must work without a CRS summary. CRS often publishes weeks after introduction.
- The subjects chain never blocks a candidate, because Congress.gov gives subjects to only some bills.
- `subjects.count` of the detail includes the policy area. A bill has legislative subjects when the count is above 1 with a policy area, or above 0 with none (`CongressBill.HasLegislativeSubjects`). A bill with only a policy area gets no subjects chain and no candidate with subjects.
- A subjects chain is admitted at the detail for every bill that has legislative subjects, also when the gate is `notRelevant`. A [vote](votes.md) can name a bill that never passed the gate.
- The gate row of each bill version stores the `latestAction` of the detail: its date and its text. The row is the status of the bill as of the fetch. The newest version of a bill holds its current status. Status is never part of an action candidate or an action revision, because a bill moves after approval. [Publication](publication.md#bill-status-line) reads it.
- A bill with `summary_count` 0 gets a text versions job at the detail, whatever the gate decision is, when an enabled [bill text source](#bill-text) exists. The text is for a later AI summary. A bill with a Congress.gov summary needs no text.
- The text comes from the official API of the Government Publishing Office (`api.govinfo.gov`), never from `www.congress.gov`. The Congress.gov `/text` list names the versions. The package ID is the file name of the Formatted Text URL.
- A text read never blocks a candidate. The text chain has no `bill_pages` row.
- A candidate is an immutable input. Late summaries or subjects add a new candidate and never change an older one.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- One gate exists for each bill version. Enforced by `ux_bill_gates_bill_version`. Test: `PostgresBillStreamTests.ASecondDetailJobForTheSameBillVersionRecordsNothingAndAdmitsNoFollowOns`.
- The reason fits the decision, and only a decided gate has a decision time. Enforced by `ck_bill_gates_reason` and `ck_bill_gates_decided`. Test: `PostgresBillStreamTests.TheDatabaseRefusesAReasonThatDoesNotBelongToItsDecision`.
- A decided gate never changes. Enforced by `PostgresCollectionJobStore`, which settles only a Pending gate. Test: `PostgresBillStreamTests.ALaterCosponsorPageNeverChangesAGateThatAnEarlierStepDecided`.
- A bill with only a policy area admits no subjects chain, and the backfill is bounded and idempotent. Enforced by `BillGateRow.HasSubjectsChain` and `PostgresCollectionJobStore.AdmitSubjectsChainAsync`. Tests: `PostgresBillStreamTests.TheSubjectsCountIncludesThePolicyAreaSoAPolicyOnlyBillAdmitsNoSubjectsChain`, `PostgresBillStreamTests.TheBackfillAdmitsTheChainOfTheNewestVersionOnceStoresTheCountAndNeverListsAnExaminedBillAgain`, and `PostgresBillStreamTests.TheBackfillOfAPolicyOnlyBillAdmitsNothingButRecordsTheCountSoItIsNotListedAgain`.
- A bill gets a text versions job only when its `summary_count` is 0 and an enabled bill text source exists. A replay admits no second job. Enforced by `PostgresCollectionJobStore.DetailFollowOns` and the unique job key. Tests: `PostgresBillStreamTests.ABillWithNoSummaryGetsAVersionsJobAtDetailForEveryGateOutcomeAndItsTextStateStartsUnread`, `PostgresBillStreamTests.ABillWithACongressGovSummaryGetsNoVersionsJobBecauseTheSummaryAlreadyExplainsIt`, `PostgresBillStreamTests.ADisabledTextSourceMakesNoVersionsJob`, `PostgresBillStreamTests.AConfigurationWithoutATextSourceMakesNoVersionsJob`, `PostgresBillStreamTests.ReadingTheSameVersionsPageTwiceAdmitsOneTextJob`.
- The text of a bill comes from the newest version that has a Formatted Text entry. An undated Enrolled Bill is chosen unless a dated Public Law or Private Law exists. A bill package must belong to the bill. A Public Law or Private Law package must belong to the Congress. The Congress.gov URL must start with `https://www.congress.gov/`. Anything else is invalid data. Enforced by `CongressBillTextVersionsPage.Newest` and `CongressBillTextVersionsParser`. Tests: `CongressBillTextVersionsParserTests.TheNewestVersionByDateIsChosenWhateverTheListOrder`, `CongressBillTextVersionsParserTests.AnEnrolledBillWithNoDateIsChosenWhenNoPublicLawExistsBecauseItIsTheFinalTextEvenThoughCongressGovGivesItNoDate`, `CongressBillTextVersionsParserTests.ThePublicLawIsChosenOverTheUndatedEnrolledBillBecauseItIsLaterAndListedLast`, `CongressBillTextVersionsParserTests.ATieOnTheNewestDateKeepsTheFirstListedVersionBecauseCongressGovListsTheLaterStageFirst`, `CongressBillTextVersionsParserTests.AnUndatedVersionThatIsNotAnEnrolledBillCountsAsOldestSoItNeverBeatsADatedVersion`, `CongressBillTextVersionsParserTests.APublicLawPackageNamesALawNumberSoItIsAcceptedForAnyBillOfTheSameCongressAndNotForAnotherCongress`, `CongressBillTextVersionsParserTests.APrivateLawPackageIsTiedToTheCongressOnlyLikeAPublicLawPackage`, `CongressBillTextVersionsParserTests.ANewerVersionWithoutFormattedTextIsSkippedSoTheNewestReadableVersionWins`, `CongressBillTextVersionsParserTests.ABillWithNoFormattedTextHasNoNewestVersionSoTheReadEndsAsNoText`, `CongressBillTextVersionsParserTests.AFileNameThatIsNotAPackageOfThisBillIsInvalidSoOneBillNeverStoresTheTextOfAnother`.
- The stored text window is at most 60,000 characters, cut at the last whitespace, and its header states the characters read and the total. Empty text is invalid. Enforced by `CongressBillRenderer.RenderText`. Tests: `CongressBillTextRendererTests.LongTextIsCutAtTheLastLineBreakInsideTheWindowAndTheHeaderStatesTheHonestCounts`, `CongressBillTextRendererTests.FlatTextFromTheExtractorIsCutAtTheLastSpaceInsideTheWindowSoNoWordIsCut`, `CongressBillTextRendererTests.TextOfExactlyTheWindowIsNotCut`, `CongressBillTextRendererTests.LongTextWithNoWhitespaceInsideTheWindowIsInvalidInsteadOfCutThroughAWord`, `CongressBillTextRendererTests.EmptyTextIsInvalidBecauseAnEmptyRowCouldNeverBeSummarized`, `CaptureDocumentTextExtractorTests.AGovinfoPreBodyOverTheWindowFlattensToSpacesAndTheRendererStillCutsItAtASpaceWithHonestCounts`.
- A bill has at most one text row for each package. A row names a package of its own bill or Congress. Its window agrees with its counts. It cites a Congress.gov URL. Its extraction belongs to the text job. Enforced by the primary key of `bill_text_versions`, `ck_bill_text_versions_package`, `ck_bill_text_versions_chars`, `ck_bill_text_versions_url`, the foreign key to `document_extractions`, and `PostgresCollectionJobStore.AdmitTextAsync`. Tests: `PostgresBillStreamTests.StoringTheSameTextTwiceAddsNoSecondRow`, `PostgresBillStreamTests.ADifferentVersionOfTheSameBillGetsItsOwnRow`, `PostgresBillStreamTests.TheDatabaseRefusesATextRowWhoseCountsUrlOrPackageContradictItsBill`, `PostgresBillStreamTests.TheDatabaseRefusesATextWindowOutsideOneToTheWindowLengthEvenWhenItMatchesTheTotal`, `PostgresBillStreamTests.ALawPackageOfTheBillsCongressIsAcceptedAndTheGateCanPointAtIt`, `PostgresBillStreamTests.AVersionRowNeedsItsExtraction`, `PostgresBillStreamTests.ATextExtractionOfAnotherJobIsRefusedSoARowNeverCitesEvidenceThatTheJobDidNotCapture`.
- A null `text_state` means unread. `NoText` means that no version has Formatted Text. `Read` means that `text_package_id` names a stored row, so a reader takes the current text from the gate and never repeats the `Newest` rule. Only a bill with `summary_count` 0 has a state. Gate rows never change `summary_count` in place, so this check holds. A later update of a bill whose version is stored reads it without a second download. Enforced by `ck_bill_gates_text_state`, `ck_bill_gates_text_package`, `ck_bill_gates_text_without_summary`, the composite foreign key to `bill_text_versions`, and `PostgresCollectionJobStore.AdmitTextVersionsAsync`. Tests: `PostgresBillStreamTests.TheDatabaseRefusesATextStateOtherThanNoTextOrRead`, `PostgresBillStreamTests.AReadStateNeedsItsPackageAndAPackageNeedsTheReadStateSoThePackageIsNeverGuessedFromNewest`, `PostgresBillStreamTests.AGateCannotPointAtAPackageThatHasNoRowSoReadAlwaysMeansTheTextIsStored`, `PostgresBillStreamTests.ABillWithACongressGovSummaryCannotHaveATextStateBecauseNoTextJobExistsForIt`, `PostgresBillStreamTests.ABillWithNoFormattedTextEndsAsNoTextAndMakesNoTextJob`, `PostgresBillStreamTests.ALaterUpdateOfABillWhoseVersionIsStoredReadsItWithoutAnotherDownload`.
- A stored latest action has both a date and a trimmed text of 1 to 1,000 characters, and only a row marked read holds one. The parser gives no status for an unusable `latestAction`. That means a missing field, a text over the cap, or a control character. A detail with no `latestAction` stores no status. The status backfill lists only the newest unread version of a bill, once. Enforced by `ck_bill_gates_latest_action`, `CongressBillParser`, and `PostgresCollectionJobStore.RecordStatusAsync`. Tests: `PostgresBillStreamTests.TheDatabaseRefusesALatestActionWithOnlyADateOrWithBlankOrPaddedTextSoNoHalfStatusIsStored`, `CongressBillParserTests.AnUnusableLatestActionGivesNoStatusAndOneWarningAndNeverBlocksTheBill`, `CongressBillParserTests.ADetailWithNoLatestActionHasNoStatusSoNoReaderSeesAFalseNoActionLine`, `PostgresBillStreamTests.TheGateStoresTheLatestActionOfTheDetailAndMarksItReadAlsoWhenTheDetailHasNoneSoNoBackfillListsItAgain`, and `PostgresBillStreamTests.TheStatusBackfillListsOnlyTheNewestUnreadVersionRecordsItOnceAndNeverListsItAgain`.
- A bill version has at most three action candidates, one for each growing set of closed chains. Enforced by `ux_action_candidates_detail_job` and `ActionCandidateInputs`. Test: `PostgresBillStreamTests.SubjectsAndSummariesThatCloseLaterGiveAtMostThreeCandidatesWhicheverComesFirstAndReplayAddsNoMore`.
- A candidate ID of an older input set never changes. Enforced by `PostgresCollectionJobStore.HashActionCandidate`. Test: `PostgresBillStreamTests.TheCandidateIdsOfTheOldInputCombinationsStayTheSameAndTheSubjectsSuffixComesLast`.
- A candidate needs a Passed gate and closed chains. Enforced by `PostgresCollectionJobStore.RecordActionCandidateAsync`. Test: `PostgresBillStreamTests.NoCandidateIsWrittenForABillTheGateMarksNotRelevant`.
- Each chain position has one page row, and a Detail row is its own root. Enforced by `ux_bill_pages_chain_order` and `ck_bill_pages_detail`. Test: `PostgresBillStreamTests.TheDatabaseRefusesADetailRowWhoseJobIsNotTheRoot`.
- A page extraction belongs to the attempt of its job. Enforced by `PostgresCollectionJobStore`. Test: `PostgresBillStreamTests.AnExtractionOfAnotherJobsAttemptIsInvalidDataAndWritesNoPageRowOrCandidate`.
- Bill admission never defers a job; a deferral fails the transaction. Enforced by `PostgresCollectionJobStore`. No test.
- The cursor never moves backward. Enforced by `PostgresCollectionJobStore`. Test: `PostgresBillStreamTests.CursorNeverMovesBackward`.

## Behavior

### Source configuration

One `api` source with `apiParser` set to `congressBillList` follows the bill list. `ApiParser` in Application/Collection is the closed list of parsers. It holds `congressBillList`, `congressHouseVoteList`, `voteviewSenateVoteList`, `congressLegislators`, `congressLegislatorsExecutive`, `memberPhotos`, and `federalRegisterDocuments`. The retired `senateVoteList` still parses. The second and third are in [votes](votes.md). The fourth to sixth are in [roster](roster.md). The last is in [executive actions](executive-actions.md). The `ApiParser` JSON converter is registered in `ConfigurationJson`, which also serves the configuration snapshots.

A Congress.gov source that sets `apiParser` also sets `credential`, `minimumCongress` (1 or more), and `backfillFrom`. Configuration allows these fields only with mode `api`. Null fields stay out of revision snapshots, so existing revision IDs do not change.

`CollectionConfiguration.CoveredPersonIdsOf` owns the coverage exception. `CollectionConfiguration.IsActiveOn` reads coverage from it and adds the roster case. The eligibility checks before a scheduled check and before a request go through `IsActiveOn`. The statement speaker roster and source labels read `CoveredPersonIdsOf`.

The shipped source `congress-bills` sets `backfillFrom` 2025-01-03 and `minimumCongress` 119. Enable steps are in [operations](../operations.md#congressgov-bill-stream).

### Daily window

`BillStreamWindow` owns the window rules. The cursor is the last closed day that was processed. The next day is the cursor plus one day, or `backfillFrom` when there is no cursor.

If that day is not closed, the due check creates no job and waits one interval. Otherwise the window ends on the last closed day and covers at most 7 days. The request starts at 00:00:00Z one day before the first day. It ends at 00:00:00Z one day after the last day.

`BillStreamWindow.RequestUrl` is the one place that builds the query. It adds `fromDateTime`, `toDateTime`, `sort=updateDate+asc`, `limit=250`, and `format=json` to the source URL. A URL without that sort has no window, so a `next` link that drops it is rejected. The root job URL holds the window.

A hand check (`jobs enqueue` or Check now) of the source only makes its schedule due. The scheduler admits the check with the stored cursor, so a hand check cannot reset the pass counter.

A scheduled check does not start while a list page job (`apiResource` `billList`) of the source is active. The source checks once a day.

### Cursor and passes

The first page of a pass (no `offset`) sets `pass_admitted_bills` to its newly admitted detail jobs. Each later page adds its own count. At the last page, the counter goes back to 0.

The cursor moves to the last day of the window only when the pass admitted no new bills. Otherwise the schedule is due at once and the same window is listed again. A window therefore settles after the first full pass that admits no new bills. A repeat pass costs only list pages, because known detail keys are duplicates.

When the window completes and the cursor is still behind the last closed day, the same transaction sets the schedule due time to now. The next window then starts at once until the source reaches the last closed day. The one-slot-per-interval rule of the [scheduler](collection.md#autonomous-collection-engine) still coalesces missed days, so this needs no new queue.

A page prepared again after a crash or a lost lease finds its `bill_stream_pages` row and changes nothing. It cannot settle a window or erase a count. A pass that stops early leaves no count behind, because the next first page sets the counter again. The cursor never moves backward.

A malformed list page blocks preparation, and the cursor stays. The next due check repeats the same window from its first page, which resets the counter.

### Resource kinds and URLs

A job definition of a bill list source carries `apiResource`. The kinds are `billList` (a list page), `bill` (a detail), and `billCosponsors`, `billActions`, `billSummaries`, `billSubjects`, and `billTextVersions` (the follow-on pages of one bill). A kind needs a source with `apiParser` `congressBillList` and a bound configuration revision. The kind `billText` belongs to the [bill text source](#bill-text) instead. A job without a kind keeps the source URL.

The configuration binding check requires the exact URL form of each kind:

- `billList`: the source URL, a query, and a window with the fixed sort (`BillStreamWindow.IsRequestUrl`).
- `bill`: exactly the detail form `{source url}/{congress}/{type}/{number}?format=json` that `CongressBillListItem.DetailUrl` builds (`CongressBillListItem.IsDetailUrl`).
- Follow-on pages: `{source url}/{congress}/{type}/{number}/cosponsors`, `/actions`, `/summaries`, `/subjects`, or `/text`, with `format=json&limit=250` and optionally one `offset` of 0 or more.
- `billSummaries` uses `limit=1` (`BillPageUrl.SummariesPageLimit`, chosen through `BillPageUrl.LimitOf`). A summaries page with another `limit` is not a valid page URL.

`BillPageUrl` builds the first page and reads every page, including the `next` form of the API, which orders the parameters differently. Numbers must be canonical, and the type must be known. Any other or repeated parameter, including `api_key`, fails. `ApiPageQuery` owns the page parameter rule (`limit`, `format`, and `offset`) and the canonical number rule. It also owns the page split. It owns the next link check too: scope, exact form, same series, and advancing offset. `BillPageUrl` adds the bill identity and resource segment. The list window uses `ApiPageQuery` too.

The enum `ApiResource` in Application/Collection has its own camelCase JSON converter, because job definitions use the collector JSON options.

### Parsing

Each succeeded page job goes through [preparation](collection.md#preparation). The parsers read the capture through `ICaptureBodyReader`. A malformed response is invalid data, so preparation blocks the job.

| Parser | Version | Reads |
|---|---|---|
| `CongressBillListParser` | none, because a list has no extraction | One list page of at most 250 items |
| `CongressBillParser` | `congress-bill-detail-v1` | One detail, including `summaries.count`, `subjects.count` (a missing object means 0), and the optional `latestAction` |
| `CongressBillCosponsorsParser` | `congress-bill-cosponsors-v1` | One cosponsors page of at most 250 items |
| `CongressBillActionsParser` | `congress-bill-actions-v1` | One actions page of at most 250 items |
| `CongressBillSummariesParser` | `congress-bill-summaries-v1+html-text-v1` | One summaries page of at most 1 item |
| `CongressBillSubjectsParser` | `congress-bill-subjects-v1` | One subjects page of at most 250 names |
| `CongressBillTextVersionsParser` | none, because a versions page has no extraction | One text versions page of at most 250 versions |
| `CongressBillTextParser` | `congress-bill-text-v1+html-text-v1` | The govinfo HTML of one package |

The list parser checks the type against `hr`, `s`, `hjres`, `sjres`, `hconres`, `sconres`, `hres`, and `sres`. A list `next` link must stay inside the source origin and path prefix. It must carry the same window and must not carry `api_key`.

A follow-on `next` link must be inside the source scope and must be the next page URL form of the same bill and kind. Its offset must be larger than the offset of the current page.

A summary has `versionCode`, `actionDate`, `actionDesc`, `updateDate` (an exact time), and `text` (HTML).

The `latestAction` object of the detail has `actionDate` and `text`. It has no action code; the Congress.gov documentation lists `actionCode` only for items of the actions list. A missing object means no status. An object with no usable date, a blank or over-long text, or a control character gives no status. It logs one warning in preparation. It never blocks the bill. A live scan of 16,819 dev bill details found `latestAction` in 16,808. It found no bad date, no control character, and a longest text of 652 characters.

A subjects page has `subjects.legislativeSubjects`, a list of items with a `name`. The parser keeps the trimmed names and ignores the `policyArea` object of the page, because the detail already holds the policy area. A blank name is invalid data. A live check on 119th bills showed that `legislativeSubjects` is present and empty for a bill that has only a policy area. It also showed that a page can hold fewer names than the limit and still have `next`.

### Admission

`IBillStreamAdmissionStore` admits one page in one transaction, with the same job coordination lock as article admission. Follow-on jobs use `AdmitApiJobsAsync`, the same code path as list page admission.

For each list item with a congress of at least `minimumCongress`, the store admits one detail job. A known key is a duplicate and is skipped, so a bill admitted with the same update date is not admitted again. When the page has a `next` link, the store admits one page job. When there is no `next` link, it applies the cursor rule. If a job fails to save, the jobs and the cursor change roll back together.

The aggregate budgets come from the source `admissionPolicy` through `DiscoveryAdmissionPolicy`, the same owner as [article admission](collection.md#discovery-and-admission). A page of 250 items plus the next page needs 251 jobs. `CollectionConfiguration.Validate` rejects a bill list source whose policy cannot reserve `ApiPageQuery.PageLimit + 1` jobs. A source without `admissionPolicy` uses the small defaults and fails this check.

The configured source sets `maxJobs` 251, and request, byte, and time totals for 251 jobs with two attempts each. A detail admits at most four follow-on jobs and a follow-on page admits at most three, so the 251 job limit holds. The text versions job replaces the summaries job, because a bill has one or the other, so the limit of four holds. The worker treats an admission that throws `InvalidDataException` as a permanent failure.

These jobs never register statement candidates, because only the key prefix `discovery-admission/` does that.

The detail admission also queues the [vote](votes.md#admission) candidates that wait for the bill, in the same transaction.

### Job keys

`BillStreamJobKeys` owns the keys. A replayed preparation finds the known keys and creates no job.

| Job | Key |
|---|---|
| Detail | `bill-stream/{source}/bill/{congress}/{type}/{number}/{updateDate}` |
| Next list page | `bill-stream/{source}/page/{list job ID}` |
| First follow-on page | `bill-stream/{source}/{cosponsors, actions, summaries, subjects, or text}/{congress}/{type}/{number}/{update date}` |
| Next follow-on page | `bill-stream/{source}/{cosponsors, actions, summaries, or subjects}/page/{parent job ID}` |
| Bill text | `bill-stream/{text source}/text/{text versions job ID}` |

The follow-on update date is the exact UTC time as `yyyy-MM-ddTHH:mm:ssZ`.

### Bill gate

Each detail job decides whether its bill is relevant. The decision is `pending`, `passed`, or `notRelevant`, with a reason. Preparation parses the detail and decides in this order:

1. A sponsor bioguide ID belongs to a covered person: `passed` with `coveredSponsor`.
2. The policy area is tracked: `passed` with `trackedPolicyArea`.
3. The cosponsor count is 0: `notRelevant`, with `noPolicyArea` when the policy area is missing and `untrackedPolicyArea` otherwise.
4. Otherwise: `pending` with no reason.

`ck_bill_gates_reason` admits the stored decisions Pending, Passed, and NotRelevant. `BillGate` decides each move, and `PostgresCollectionJobStore` applies it.

| From | To | Write |
|---|---|---|
| (new) | Pending, Passed, or NotRelevant | `BillGate.AtDetail` at the detail page |
| Pending | Passed | `BillGate.AtCosponsorPage` finds a covered cosponsor |
| Pending | NotRelevant | `BillGate.AtCosponsorPage` reaches the last cosponsor page without one |

A decided gate never changes.

The covered people are `CollectionConfiguration.CoveredBioguideIdsOf` for the UTC day of the exact `updateDate` of the detail. They come from the configuration revision of the job.

A policy area is tracked when two conditions hold. Its name equals (ordinal) a policy area name in the issue configuration. At least one tracked issue, active on that UTC day, lists that policy area. `BillGate.TrackedIssueIdsFor` owns this map. Action drafting uses it for the candidate issues; see [actions](actions.md#prompt-and-output).

The issue configuration comes from `CIVIC_LENS_ISSUES_CONFIG`, as an `IssueConfigurationRevision` given to `JobPreparation`. The worker refuses to start with exit code 2 when a bill list source is enabled and the variable is not set. `JobPreparation` still blocks a detail job with `issuesConfigurationUnavailable` as a defensive path. The gate stores the revision ID.

### Follow-on pages

A detail job with a cosponsor count above 0 always admits the first cosponsor page, as evidence. It admits the first subjects page the same way when the bill has legislative subjects. It admits the first text versions page when `summaries.count` is 0 and an enabled bill text source exists. A detail job whose gate passed also admits the first actions page. It admits the first summaries page when `summaries.count` is above 0.

A cosponsor page job reads its page and finds its gate. The gate may be pending, and the page may list a covered cosponsor who is not withdrawn. Then the gate passes with `coveredCosponsor`, and the job admits the first actions page. It admits the first summaries page when the stored `summary_count` is above 0.

`PassedFirstPages` owns this rule for both pass points. The covered people use the UTC day of the gate update date.

If a cosponsor page has a `next` link, the next cosponsor page is admitted. With no `next` link and a gate that is still pending, the gate becomes `notRelevant`. The reason is `noPolicyArea` or `untrackedPolicyArea` from the stored policy area. A later page never changes a decided gate.

An actions, summaries, or subjects page job admits the next page of its own kind when it has a `next` link. The next page key uses the parent job ID, like the cosponsor chain.

### Extraction

Every captured API page of a bill is saved as one `DocumentExtraction`: the detail and each cosponsor, actions, summaries, and subjects page. A bill list page is not saved. API jobs get no processing stage.

`CongressBillRenderer` has one method for each page kind and owns the text. A renderer change bumps the extraction version of each page kind it renders. The extraction version is the `ExtractionVersion` constant of the parser that read the page. The source attempt of the extraction is the captured attempt of the job.

A text versions page has no extraction and no `bill_pages` row. The govinfo text of a package is saved as one extraction; see [Bill text](#bill-text).

A summaries page is the exception. Its extraction version is `{CongressBillSummariesParser.Version}+{extractor.ExtractionVersion}`. Today that is `congress-bill-summaries-v1+html-text-v1`.

The text has one fact per line, in a fixed order, with invariant culture, `\n` line endings, and no trailing whitespace.

- A detail has the lines `Bill: {congress} {type} {number}`, `Title`, `Introduced`, `Updated` (UTC, `yyyy-MM-ddTHH:mm:ssZ`), and `Policy area` (omitted when missing). Then it has one `Sponsor` line for each sponsor, `Cosponsors (including withdrawn)` (the count), and `Actions` (the count).
- A cosponsor page, an actions page, and a subjects page start with the `Bill` line.
- A subject line is `Subject: {name}`.
- A cosponsor line is `Cosponsor: {bioguide ID}, [original, ]{date}[, withdrawn {date}]`.
- An action line is `Action {date}[ {time}][ [type {type}]][ [code {code}]][ [source {code} {name}]]: {text}`. Each tag is omitted when its field is null. The source tag holds the source system code, the name, or both.
- A summaries page starts with the `Bill` line, then one block for each summary. A blank line separates the blocks.
- A block has the lines `Summary version`, `Action date`, `Action`, `Updated`, and `Text:`, then the converted summary text, which can span lines.

`JobPreparation` converts each summary with `IDocumentTextExtractor.ExtractHtmlFragmentAsync`. The extracted text has no blank lines, so a blank line only separates blocks.

A value is written as received, so a `]` inside a value is not escaped. A line break inside a value becomes one space. The breaks are `\r\n`, `\r`, `\n`, `\v`, `\f`, U+0085, U+2028, and U+2029, and a `\r\n` pair counts as one break. Each value loses leading and trailing whitespace.

If the rendered text of a page is over the extraction limit of 2,000,000 characters, the extraction is invalid. Preparation then blocks the job and saves nothing.

`JobPreparation` saves the extraction right after the parse, before the gate or follow-on step. It does this for every parsed page, also for a bill that the gate marks not relevant. A blocked detail job (no issue configuration) saves nothing.

The extraction ID is deterministic and the save is idempotent. A replayed preparation repeats safe work and creates no second extraction. The extraction store registers a statement candidate only for the key prefix `discovery-admission/`, so a bill extraction never creates one. Tests protect this against a real database.

### Bill text

The source `congress-bill-text` has `apiParser` `congressBillText`. Its URL is `https://api.govinfo.gov/packages` and its allowed path prefix is `/packages`. It sends the Congress.gov credential in the `X-Api-Key` header, because both hosts accept an `api.data.gov` key. The shipped source is `enabled: false`. A disabled or missing source makes no text job. The text source is one of the sources that cover no people, and a configuration has at most one. Its admission policy holds exactly the one job of a versions read. The origin of govinfo has its own pacing key.

1. A detail with `summaries.count` 0 admits a text versions job at `{congress source url}/{congress}/{type}/{number}/text`. This job belongs to the bill list source and shares the Congress.gov pacing key.
2. `CongressBillTextVersionsParser` reads `textVersions`. Each entry has a `type` such as "Introduced in House", a `date` that can be null, and `formats`. An entry without a Formatted Text format is skipped. The package ID is the file name of the Formatted Text URL without `.htm`, for example `BILLS-119hr2658ih`, `PLAW-119publ21` for a Public Law entry. A Private Law entry gives `PLAW-119pvtl1`. A bill file name must belong to the bill. A Public Law name must belong to the Congress, because a law number is not a bill number. The URL must start with `https://www.congress.gov/`. A page with a `next` link is invalid, because a later page could hold a newer version.
3. `CongressBillTextVersionsPage.Newest` picks one version. A live sample of 10 bills of the 119th Congress showed that Congress.gov gives no date to the Enrolled Bill entry, which it lists first. It lists the Public Law entry last, with the newest date. The rule is: the newest dated entry wins if its type is Public Law or Private Law. Otherwise an undated entry of type Enrolled Bill wins. Otherwise the newest date wins, an undated entry of another type counts as the oldest, and the first listed wins a tie. The stored date of an undated pick stays null, and the header says `not given`.
4. With no version, `AdmitTextVersionsAsync` sets `text_state` `NoText` and admits nothing. If a row for the package exists, it sets `Read` and admits nothing. Otherwise it admits one `billText` job on the text source in the same transaction. The job key holds the versions job ID, and the job is linked to the detail job in `bill_follow_on_jobs`.
5. The text job fetches `https://api.govinfo.gov/packages/{packageId}/htm`. The collector keeps the raw bytes as an immutable capture. The job URL form is the only form the configuration binding accepts for `billText`.
6. Preparation of the text job finds its versions job from the last segment of the job key. It reads the versions capture again, because the version type and date exist only there. It extracts the text of the captured HTML with `IDocumentTextExtractor.ExtractAsync`. The extractor turns every whitespace run into one space. That includes the line breaks of the govinfo `<pre>` body. The text of a long bill is therefore one flat line. The extractor and its version stay unchanged, because flat text suits quote matching.
7. `CongressBillRenderer.RenderText` keeps the first 60,000 characters, cut at the last whitespace inside the window. Text of 60,000 characters or less stays whole. Empty text is invalid data. The header has the lines `Bill`, `Version`, `Version date` (or `not given`), `Package`, `Characters read: {read} of {total}`, and `Text:`. The extraction holds the header and the window. Publication will embed only this window.
8. `AdmitTextAsync` adds the `bill_text_versions` row and sets `text_state` `Read` in one transaction. A repeat adds no second row. It first checks that the extraction belongs to an attempt of the text job, like a page chain does. `AdmitTextVersionsAsync` and `AdmitTextAsync` each set `text_package_id` when they set `Read`. The store holds the job lock for the whole check and insert. Two text jobs of one package therefore cannot write at the same time, and no conflict clause is needed.

A bill text job whose versions job is missing or not succeeded blocks with `billTextVersionsMissing`. A package that the versions page does not list is invalid data.

### Page chains

`JobPreparation` saves the extraction first and passes its ID to the admission store. The store writes the `bill_pages` row in the same transaction as the page admission. It first checks that the extraction belongs to an attempt of that job. A mismatch is invalid data, so preparation blocks.

The order of a page is the count of earlier rows of the same role, because only its parent admits each next page. A page with no `next` link closes its chain, and the detail always closes its own. A replayed admission finds its row and writes nothing. A failed or blocked page writes no row, so its chain stays open.

A chain is closed when exactly one row closes it, that row has the highest order, and the orders have no gap.

### Subjects backfill

Bills that the gate saw before the subjects column existed have a null `subject_count` and no subjects chain. The owner command `bills subjects backfill [limit]` adds the chains. The limit is 100 when you give none. The command has the shape of the other owner commands. Its precedent is `relationships draft refresh`, which fills a feature from records that already exist.

`BackfillBillSubjects` asks for the newest bill versions whose gate has a null `subject_count`, at most the limit, in detail job ID order. For each, it reads the saved detail capture with `CongressBillDetailJob`. It then calls `IBillSubjectsBackfillStore.AdmitSubjectsChainAsync` with the count and the source of the detail job. The store runs in one transaction under the job lock. It stores the count it read in the gate row, also when the count is 0. If the bill has legislative subjects, it admits the first subjects page through `AdmitFollowOnsAsync`, like the detail admission. The command prints `examined`, `admitted`, and `failed`. A missing or unreadable detail capture counts as failed, and the run continues.

A replay is safe, because the stored count takes the bill out of the list and the job key is fixed. A bill with only a policy area, or with no subjects at all, gets its count and no chain. A bill whose capture failed keeps a null count. It stays in the list and counts as failed on each run, and it stays ahead of the later bills in the order. A subjects chain that closes adds a new candidate to a passed bill, so the action worker drafts that bill again. Start with a small limit, because each such candidate spends model budget.

### Status backfill

Gate rows written before the status columns existed have `latest_action_read` false. The owner command `bills status backfill [limit]` fills them. The limit is 100 when you give none. It has the shape of the subjects backfill.

`BackfillBillStatus` asks for the newest bill versions that are unread, at most the limit, in detail job ID order. For each, it reads the saved detail capture with `CongressBillDetailJob` and calls `IBillStatusBackfillStore.RecordStatusAsync`. The store marks the row read and stores the date and text, or stores none when the capture has no `latestAction`. The command prints `examined`, `recorded`, and `failed`. A missing or unreadable capture counts as failed, and the run continues. The command makes no Congress.gov request, because the capture holds the detail. The status is then as of the original fetch, and the next update of the bill replaces it. Older versions of a bill stay unread, because only the newest version is ever shown.

### Action candidate

`RecordActionCandidateAsync` owns the completeness rule. It runs under the global job lock, in the transaction that admits a closing page of any chain. The evidence is complete when all of these hold:

- The gate is `Passed`.
- The detail row exists.
- The actions chain is closed.
- The cosponsors chain is closed, when `cosponsor_count` is above 0.

When the evidence is complete, the candidate includes summaries if the summaries chain is closed (`summary_count` above 0). It includes subjects if the subjects chain is closed. The subjects chain can close before the gate passes.

A new candidate is written only when no candidate of the bill version already holds all of its inputs (`ActionCandidateInputs.AtLeast`). Each later chain closing therefore adds a candidate that holds one input more. The candidate sets only grow, so a bill version has at most three candidates. The order is one of these:

- without summaries or subjects, then with summaries, then with both
- without summaries or subjects, then with subjects, then with both

The older candidates and their queue rows stay as they are, whatever their state. A candidate with both inputs ends the chain.

The write that creates a candidate also adds its `action_analysis` queue row in the same transaction; see [action drafting](actions.md#queue). A candidate compares nothing. A replay of any admission changes nothing. A bill whose gate is not relevant never gets a candidate.

## Storage and contracts

- The generated [bills schema](schema/bills.md) lists every table, column, key, check, index, and trigger.
- `bill_stream_cursors` holds one row for each source. `last_processed_day` is null until the first window settles.
- `bill_stream_pages` records each list job that changed the counter. `unknown_type_count` counts the items on that page with an unknown bill type.
- `bill_gates` holds one row for each bill version, keyed by its detail job.
- `issue_revision_id` names the issue configuration revision that the gate used. It has no foreign key, because issue revisions are not stored in a table.
- `summary_count` is 0 or more. Rows written before the column existed have 0, so those bills fetch no summaries until their update date changes.
- `subject_count` is null or 0 or more. Null means that nobody has read the count. A gate write stores `subjects.count` of the detail, which includes the policy area. Rows written before the column existed stay null, and the backfill reads them. The [subjects backfill](#subjects-backfill) fills the null rows. `BillGateRow.HasSubjectsChain` is false for a null count. A bill with legislative subjects admits the chain whatever the gate decision is.
- `latest_action_read` is false for rows written before the status columns existed and true for every row that a detail admission writes. `latest_action_date` and `latest_action_text` are both null or both set, and set only on a row marked read. A null pair on a read row means that the detail had no `latestAction`. The time that Civic Lens read the status is `created_at_utc_ticks` of the row. The [status backfill](#status-backfill) fills the unread rows.
- `text_state` is null, `NoText`, or `Read`. It is stored as a PascalCase string. Null means that no text read has finished for this bill version. The precedent is `subject_count`. `text_package_id` is set exactly when `text_state` is `Read`, and it is a composite foreign key with the bill identity to `bill_text_versions`.
- `bill_text_versions` holds one row for each package of a bill, keyed by congress, bill type, bill number, and package ID. It also holds the version type, the version date, and the Formatted Text URL of Congress.gov. The date is null when Congress.gov gives none. The row holds the extraction ID, `chars_total`, and `chars_read`. The capture hash is reachable through the extraction and its attempt. `chars_read` is from 1 to 60,000, and it equals `chars_total` unless `chars_total` is above 60,000. A row keeps its first write, so an Enrolled Bill row stays undated if Congress.gov adds a date later.
- `cosponsor_count` stores the detail cosponsor count, because the completeness check must know whether a cosponsor chain exists. Gates written before this column existed have 0 and no detail page row, so they never get a candidate.
- The decision and reason are stored as PascalCase strings, like other enums.
- A second detail job for a gated version records nothing, admits nothing, and counts as a duplicate.
- `bill_follow_on_jobs` holds one row for each cosponsor, actions, summaries, subjects, or text versions page job, first or next, and for each bill text job. Its `detail_job_id` points to the root row in `bill_gates`.
- The store writes the `bill_follow_on_jobs` row in the transaction that inserts the job. A page job without a row is invalid data.
- `bill_pages` holds one row for the detail and one for each admitted follow-on page. Each row names its chain root, its role, its order, its extraction, and whether it closes the chain.
- `action_candidates` holds one row for each passed bill version with complete evidence. A later closing summaries or subjects chain adds a row. `includes_summaries` and `includes_subjects` tell which chains the row holds. Rows written before `includes_subjects` existed have false.
- The candidate ID is a SHA-256 hash of the source and the bill identity plus the exact update date (`PostgresCollectionJobStore.HashActionCandidate`), framed like `HashCandidate`. A candidate with summaries adds the suffix `:summaries` to the hashed text. A candidate with subjects then adds `:subjects`. The IDs of earlier candidates therefore never change.
- A bill version has at most three candidates. `ux_action_candidates_detail_job` is unique on the root, `includes_summaries`, and `includes_subjects`. The evidence of a candidate is the page rows of its root, without the roles that the candidate does not include.
- The index `ix_action_candidates_bill` on the source, the bill, and the update date serves the newest-candidate check of [action drafting](actions.md#queue).
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Window, page query, URLs, keys | `src/CivicLens.Application/Collection/Bills/BillStreamWindow.cs`, `src/CivicLens.Application/Collection/ApiPageQuery.cs`, `src/CivicLens.Application/Collection/Bills/BillPageUrl.cs`, `src/CivicLens.Application/Collection/Bills/BillStreamJobKeys.cs`, `src/CivicLens.Application/Collection/ApiJobKeys.cs` |
| Parsers | `src/CivicLens.Application/Collection/Bills/CongressBillListParser.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillParser.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillCosponsorsParser.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillActionsParser.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillSummariesParser.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillSubjectsParser.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillSubjectsJob.cs` |
| Gate and renderer | `src/CivicLens.Application/Collection/Bills/BillGate.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillRenderer.cs` |
| Resource kinds and parser list | `src/CivicLens.Application/Collection/ApiResource.cs`, `src/CivicLens.Application/Collection/ApiParser.cs` |
| Coverage exception | `src/CivicLens.Application/Collection/CollectionConfiguration.cs` |
| Bill text | `src/CivicLens.Application/Collection/Bills/CongressBillTextVersionsParser.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillTextVersionsPage.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillTextVersionsJob.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillTextParser.cs`, `src/CivicLens.Application/Collection/Bills/BillTextUrl.cs`, `src/CivicLens.Application/Collection/Bills/BillTextState.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.BillText.cs`, `src/CivicLens.Infrastructure/Collection/BillStream/BillTextVersionRow.cs` |
| Subjects backfill | `src/CivicLens.Application/Collection/Bills/BackfillBillSubjects.cs`, `src/CivicLens.Application/Collection/Bills/IBillSubjectsBackfillStore.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.SubjectsBackfill.cs`, `src/CivicLens.Host/Collection/BillBackfillCommand.cs` |
| Status backfill | `src/CivicLens.Application/Collection/Bills/BackfillBillStatus.cs`, `src/CivicLens.Application/Collection/Bills/IBillStatusBackfillStore.cs`, `src/CivicLens.Application/Collection/Bills/CongressBillLatestAction.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.StatusBackfill.cs`, `src/CivicLens.Host/Collection/BillBackfillCommand.cs` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs`, `src/CivicLens.Application/Collection/Processing/ApiResponseReader.cs` |
| Admission port | `src/CivicLens.Application/Collection/Bills/IBillStreamAdmissionStore.cs`, `src/CivicLens.Application/Collection/Bills/BillStreamAdmission.cs` |
| Postgres admission, follow-ons, candidates | `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.BillStream.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.ApiJobs.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.BillFollowOns.cs`, `src/CivicLens.Infrastructure/Collection/Jobs/PostgresCollectionJobStore.ActionCandidates.cs` |
| Table rows | `src/CivicLens.Infrastructure/Collection/BillStream/` |

## Limits and planned work

- The Sources page shows no check state for the bill source yet, because its jobs use windowed URLs.
- A Blocked cosponsor or actions page leaves its chain open, so the bill gets no candidate. [Operations](../operations.md#congressgov-bill-stream) has queries that list these bills.
- Planned: list bills for each Congress from `/v3/bill/{congress}`, for each Congress at or above the configured minimum. The date-window bill list pages through thousands of older bills.
- Planned: pull the shared line-format rules out of the bill renderer when a second renderer needs them.
- Text for a bill without a summary is collected only. Drafting an AI summary from it, a backfill command for older bills, and publication of the summary are planned.
- A gate with `NoText` is read again only when the bill gets a new `updateDate`. A later PR relists these bills.
- govinfo sends the bill text with no charset, so the extractor decodes it as windows-1252. Measured bodies (BILLS-119hr1eh, PLAW-119publ21, PLAW-119pvtl1) are pure ASCII, so nothing changes today. A UTF-8 body would decode wrongly with no error.
- Bill text over 2,000,000 converted characters fails with `InvalidDataException` and stays unread.
- The text versions job runs for every bill with `summary_count` 0, also for bills that the gate marks `notRelevant`. A later change can limit it to bills that can reach a reader.
- Planned: use the saved subjects in the search filters when they are designed. Today they only inform action and vote drafting.
