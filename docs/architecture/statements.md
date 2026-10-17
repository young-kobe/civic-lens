# Statements

## Purpose

The statements feature finds articles that may hold an official's statement and gates them against tracked issues. It then drafts one statement record for each article that passes. People review and approve each record on the statement review pages.

## Decisions

- A candidate is the first extraction of an article that discovery admission created. Later fetches and parser upgrades never create a second candidate.
- The gate uses the two issue layers in [configuration](configuration.md#decisions).
- Watched pages are never candidates. They stay document-change sources only.
- A later edit to an admitted article is a document change, not a new statement.
- Statements mirror the document-change feature: their own queue, runs, drafts, revisions, and review pages, in the same shape. They share only parts that already have one owner. These are the Core review actor and decision policy, the Anthropic adapter, the citation span checks, and the budget mechanics.
- The gate fails open on two invalid answers or an oversized prompt. A doubtful article then goes to drafting, not to a silent skip.
- Provider, auth, and budget problems are not fail-open. They follow the shared retry and pause rules.
- The gate also keeps a release about a bill or a recorded vote when no tracked issue matches. Two rules decide this: the model answer `namesBillOrVote`, and a bill number that `BillNumberFinder` finds in the text. The bill number rule needs no model and is a floor. Each pass path has its own reason code, so a measurement can count each path.
- A skipped article stays visible with a code reason, so a person can inspect it.
- A rubric change queues nothing by itself. `statements regate` is an explicit owner command.
- The speakers come from the configuration revision that admitted the article, not the current file. A later configuration change cannot change the speakers of an admitted article.
- Code sets the quotation kind and the bill numbers. The model and the reviewer never set them.
- Code never guesses a statement date. When the article states no date, the draft date is null, and the reviewer sets it.
- A gate label holds the tracked issue IDs and the `namesBillOrVote` answer of one release. It holds no quote, policy area, or pass verdict. The gate rule derives a pass from a label, so the rule keeps one owner.
- Labels are append-only rows, like revisions. The newest human row of a candidate is the label of record. A model row never replaces it.
- Code assigns about 30 percent of releases to a held-out set by a hash of the candidate ID. The set is the test set of the gate benchmark ([D-13](../roadmap.md#pending-decisions)). A held-out release never gets a model label.
- Only the owner labels releases and exports labels. `GateLabelEditor` does not extend `ReviewRecordPage`, because that base class serves revisions and decisions and a label has neither.
- Vendor rule: only public press release text and gate labels may go to Cloudflare. Never send review notes, reviewer identity, or credentials.
- Drafting does not cache the prompt. The article text is most of each request, and the schema changes with the covered officials of each source.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- A statement candidate is the source plus the URL, and it never repeats. Enforced by the primary key of `statement_candidates`. Test: `PostgresStatementCandidateRegistrationTests.ConcurrentSavesForTheSameSourceAndUrlLeaveOneCandidate`.
- Only discovery-admitted jobs become candidates. Enforced by `PostgresStatementCandidateRegistration`. Test: `PostgresStatementCandidateRegistrationTests.ExtractionFromAWatchedPageJobRegistersNoCandidate`.
- A Succeeded gate has a known reason. Enforced by `StatementGateLifecycle`. Test: `StatementGateLifecycleTests.ASucceededGateNeedsAKnownReason`.
- When a valid answer has no verified issue and namesBillOrVote is false, a bill number in the text still passes the release. Enforced by `StatementGateResolver`. Test: `StatementGateResolverTests.ABillNumberInTheTextPassesWithoutTheModelAnswerSoTheFloorHoldsWhenTheModelMissesIt`.
- A live gate answer must hold namesBillOrVote. Enforced by `StatementGateResolver`. Test: `StatementGateResolverTests.ALiveAnswerWithoutTheBillOrVoteFieldIsRejectedSoOnlyStoredRunsMayLackIt`.
- Each of the three pass paths has its own reason. Enforced by `StatementGateResolver` and `ck_statement_analysis_runs_reason`. Test: `StatementGateResolverTests.TheThreePassPathsRecordThreeDistinctReasonsSoAMeasurementCanCountEachPath`.
- A draft from a bill or vote pass accepts an empty issue list. Enforced by the draft step and the revision rules. Test: `StatementDraftStepTests.ADraftFromABillOrVotePassHasNoIssueIdsBecauseNoTrackedIssueMatched`.
- Only a skipped gate returns from Succeeded to Pending. Enforced by `StatementGateLifecycle`. Test: `StatementGateLifecycleTests.OnlyASkippedGateMayReturnToPendingForRegate`.
- A pass adds exactly one Pending draft entry in the same transaction. Enforced by `StatementGateQueueMapping`. Test: `PostgresStatementGateStoreTests.APassSettlesTheGateAndInsertsExactlyOnePendingDraftRowInTheSameTransaction`.
- Only an analysis revision has a run ID, and one revision exists for each run. Enforced by `ck_statement_revisions_run` and `IX_statement_revisions_run_id`. Test: `PostgresStatementDraftStoreTests.TheDatabaseRefusesAnAiRevisionThatHasNoRun`.
- NotDrafted holds exactly when the revision number is 0. Enforced by `ck_statement_drafts_not_drafted`. Test: `StatementReviewMigrationTests.TheDatabaseRefusesANotDraftedStatusOnADraftThatHasARevision`.
- A candidate gets one draft and one first revision. Enforced by `StatementDraftRows` under the `PostgresAdvisoryLock.StatementReview` key. Test: `PostgresStatementReviewStoreTests.AHumanCreateAndAnAiSettlementRacingForTheSameCandidateLeaveExactlyOneFirstRevision`.
- A label row is only added. It is never changed or deleted. Enforced by `PostgresGateLabelStore`, which has no update or delete method. Test: `PostgresGateLabelStoreTests.LabelsAreAppendedAndTheNewestHumanRowIsTheLabelOfRecordSoACorrectionNeverEditsHistory`.
- The newest human label is the label of record, and a model label never replaces it. Enforced by `PostgresGateLabelStore`. Test: `PostgresGateLabelStoreTests.AModelLabelNeverReplacesTheHumanLabelOfRecord`.
- A model label has a run ID and a human label has none. Enforced by `ck_statement_gate_labels_run`. Test: `StatementGateLabelsMigrationTests.TheDatabaseRefusesAModelLabelWithoutARunAndAHumanLabelWithOne`.
- A held-out release takes no model label. Enforced by `PostgresGateLabelStore` with `GateLabelSplit`. Test: `PostgresGateLabelStoreTests.AHeldOutReleaseRefusesAModelLabelAndStoresNothingSoTheTestSetStaysBlindToModels`.
- The read of a held-out release never returns a model label, even if a row exists. Enforced by `PostgresGateLabelStore.GetAsync`. Tests: `PostgresGateLabelStoreTests.AModelRowThatReachesAHeldOutCandidateByRawSqlIsStillNeverReturnedSoTheReadKeepsTheTestSetBlind` and `GateLabelPageTests.AModelRowOnAHeldOutReleaseNeverPrefillsTheFormSoTheTestSetStaysBlind`.
- A model label cites a real run, and one run labels one release. Enforced by the foreign key and the unique index on `run_id`. Tests: `PostgresGateLabelStoreTests.AModelLabelNeedsARealRunAndEachRunLabelsOnlyOneRelease` and `StatementGateLabelsMigrationTests.TheRunIdHasAForeignKeyToTheAnalysisRunsAndAUniqueIndexSoOneRunLabelsOneRelease`.
- A candidate ID always has the same held-out answer. Enforced by `GateLabelSplit`. Test: `GateLabelSplitTests.TheSameCandidateAlwaysLandsOnTheSameSideSoALaterRunCannotLeakATestReleaseIntoTraining`.
- A label cites the extraction of its candidate. Enforced by `PostgresGateLabelStore`. Test: `PostgresGateLabelStoreTests.ALabelThatCitesAnotherExtractionOrAnUnknownCandidateIsRefused`.
- A label holds only tracked issue IDs of the issue file. Enforced by `SaveGateLabel`. Test: `SaveGateLabelTests.AnIssueThatTheIssueFileDoesNotTrackIsRefusedSoLabelsStayInTheGateVocabulary`.
- Only the owner labels, reads the label queue, or exports labels. Enforced by `ReviewAuthorization.RequireOwner` in each use case, and by a redirect on the pages. Tests: `SaveGateLabelTests.AReviewerCannotLabelBecauseLabelsFeedTheOwnersBenchmark`, `GateLabelOwnerOnlyTests.AReviewerCannotReadTheLabelQueueATaskOrTheExportBecauseOnlyTheOwnerRunsTheBenchmark` and `GateLabelPageTests.ReviewersAreSentToAccessDeniedSoOnlyTheOwnerFeedsTheBenchmark`.
- An export line never holds an author subject, a review note, or a credential, and the file stays inside `.runtime`. Enforced by `GateLabelExportRow`, which has no such field, and by `GateLabelExport`. Tests: `GateLabelExportTests.EachLineHoldsOnlyTheFieldsTheBenchmarkNeedsSoNoSubjectNoteOrCredentialCanLeave` and `GateLabelExportTests.APathOutsideTheRuntimeDirectoryIsRefused`.
- Each task charges its own budget. Enforced by the primary key of `statement_analysis_budget`. Test: `PostgresStatementGateStoreTests.TheGateChargesItsOwnDailyBudgetAndNotTheDocumentChangeBudget`.

## Behavior

### Candidate queue

`PostgresDocumentExtractionStore.SaveAsync` calls `PostgresStatementCandidateRegistration` after it inserts a new extraction, in the same transaction. An attempt came from admission when its job has an idempotency key that starts with `discovery-admission/`. The job is found through `collection_job_attempts`. Admission never admits a listing page of a source, so the configured page and the allowed path prefix never become candidates. See [collection](collection.md#discovery-and-admission).

The registration inserts the candidate with `ON CONFLICT (candidate_id) DO NOTHING`. When that insert adds a row, it also inserts the Pending `gate` row in `statement_analysis`. A failure rolls back the extraction, the candidate, and the gate row together.

The first extraction of a candidate is the one whose attempt has the earliest observed time. Ties go to the lower attempt ID, then the lower extraction ID. An extraction has no stored creation time, so the observed time of its capture sets the order.

### Issue gate

The gate reads the first extraction of each candidate and decides whether the article touches a tracked issue. It also decides whether the article names a bill or a recorded vote. A pass queues the `statement-draft` task.

The gate executor is `AnalysisWorker<StatementGateRun>` with `StatementGateStep`. It runs beside the collection and drafting loops in the `worker` process. It has its own status row, daily limit, and wakeup. It uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`, the same setting as drafting (default 2, maximum 4). The wakeup uses the `civic_lens_analysis` channel with the payload `statement_analysis:gate`. Its deadline query reads the `gate` rows of `statement_analysis`.

With `--once`, the worker drains collection, then document-change drafting, then the gate, then statement drafting. If one loop fails, all stop.

The gate runs only when `CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS`, `ANTHROPIC_API_KEY`, and `CIVIC_LENS_ISSUES_CONFIG` are set. A daily limit without a key is a configuration error. When the issue file is not set, the gate stage does not run. The worker then records the limit and the hard pause reason `issuesConfigurationUnset` in the gate status row. `statements status` shows both.

**Input.** The input is the extraction text, the tracked issues active on the first capture date, and all policy areas. The date is the UTC date of the observed time of the extraction's attempt. An issue is active when `startsOn <= date < endsBefore`, and a missing bound is open. Issues and policy areas keep the order of the configuration file. The request uses model `claude-haiku-5-5`, effort `medium`, and at most 4,000 output tokens.

**Prompt and cache.** The system prompt holds the instructions, every policy area, and the active tracked issues, in configuration order. It holds no value of the article. The user message holds the source ID, the URL, the capture date, and the article text, marked as untrusted data.

The adapter marks the end of the system prompt with an ephemeral `cache_control` breakpoint (5-minute TTL). The JSON schema holds only the ID enums of the active issues and the policy areas. Two articles with the same active issue set therefore get a byte-identical system prompt and schema. A change to the schema clears the cache. Each run records cache read and cache write tokens, and both count toward the charge. A golden test pins the system prompt, the schema, and one input hash to the prompt and schema versions.

**Output.** Structured outputs fix these fields: `trackedIssues` (each with `id`, `quote`, `confidence`), `policyAreas` (each with `id`, `confidence`), `namesBillOrVote`, and a `confidence` for the whole answer. `namesBillOrVote` is true when the release reports a speaker action on a specific bill, resolution, or recorded vote. The actions are to introduce, cosponsor, vote, or state a position. The run output keeps the answer. It can label examples for a later benchmark ([D-13](../roadmap.md#pending-decisions)). There is no free-text reason.

Code rejects an unlisted ID, a repeated ID, and a confidence that is not finite or is outside 0 to 1. It rejects a quote that is empty, longer than 2,000 characters, missing, or ambiguous, or that splits a character. A quote must occur exactly once in the extraction text. Code then computes the UTF-16 offsets and checks them with `DocumentTextSpan`. Code never repairs an answer.

**Outcome.** The gate row ends Succeeded, and its `error_code` holds the reason code. The run keeps the same code.

| Reason | Meaning | `statement-draft` row |
|---|---|---|
| `passed` | At least one matched tracked issue with a verified quote | Inserted |
| `passed-bill-or-vote` | No tracked issue matched, and the model answered `namesBillOrVote` true | Inserted |
| `passed-bill-number` | No tracked issue matched, the model answered false, and the text holds a bill number | Inserted |
| `no-tracked-issue` | No tracked issue matched, the model answered false, and the text holds no bill number. Or no tracked issue is active on the capture date (no model call). | None |
| `over-byte-cap` | The prompt is over the byte cap, so there is no model call | Inserted |
| `gate-failed-open` | Two invalid answers, or a retry prompt over the cap | Inserted |

The resolver picks the reason in this order: a verified issue, then the model answer, then the bill number. A release that passes only by a bill or a vote has no tracked issue, and its draft starts with an empty issue list. When no tracked issue is active on the capture date, the gate makes no model call and does not look for a bill number.

The byte cap is the smaller of two values: 100,000 UTF-8 bytes for the full prompt, and the run token limit minus the output allowance. The settlement and the `statement-draft` insert share one transaction. A repeat insert keeps one row.

An invalid answer gets one more call, with the rejection reasons added to the prompt. The queue keeps the ID of the rejected run, so a budget wait between the two calls resumes the second call. Both rejected runs stay.

Provider, auth, and budget conditions follow the [drafting rules](analysis.md#retries-and-pauses):

- durable retry with backoff
- a pause until restart for a 401, 402, 403, or 404
- a timed pause after three provider failures in a row
- WaitingForBudget until the next UTC day

A refusal, a `max_tokens` stop, an unreadable response, and other 4xx responses fail the entry. The article then waits for `statements requeue`.

### Statement drafting

The drafting executor is `AnalysisWorker<StatementDraftRun>` with `StatementDraftStep`. It drains the `statement-draft` rows that the gate queues and writes an AI draft of one statement record for each candidate. It runs beside the collection, document-change drafting, and gate loops in the `worker` process. It has its own wakeup, status row, daily limit, and run limit, and it uses `CIVIC_LENS_ANALYSIS_CONCURRENCY`.

The executor runs when `CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS` and `ANTHROPIC_API_KEY` are set. A limit without a key is a configuration error.

**Speakers.** The source of the candidate and the UTC date of its first capture select the covered officials. The configuration is the revision that the collection job saved when it admitted the article. `PostgresStatementDraftStore` reads that revision with the evidence, through the candidate, its attempt, and the job. It decodes the job definition with the job store.

`StatementSpeakerRoster` is a pure function over a configuration revision. It lists the officials that the source covers in that revision on that date, in ID order. The names come from the People list of that revision. A job with no configuration revision, or a revision without the source, covers no official. The count of officials sets the shape of the prompt.

| Covered officials | Shape | Speaker |
|---|---|---|
| None | No call. The entry is Blocked as `speakersUnavailable`. | None |
| One | The schema has no speaker field. | Code sets that official. A speaker field in the answer is rejected. |
| Several | The schema has `speakerOfficialIds` with an enum of their IDs. | The model chooses at least one listed official. An unlisted or repeated ID is rejected. |

`otherSpeakers` holds the names of other people whom the article quotes. A name must be plain: 1 to 128 characters, with no space at either end. It must occur in the article text. It is never a linked identity.

**Prompt and output.** The system prompt holds the instructions only. Each shape has its own fixed system prompt, with no value of the article or the source. The user message holds the source ID, the URL, the capture date, the covered officials, and the article text, marked as untrusted data. The request uses model `claude-sonnet-5-5`, effort `medium`, and at most 16,000 output tokens.

Structured outputs fix these fields:

- `headline` and `summary`
- `statementDate`: a date, an exact quote, and a confidence, or null
- `speakerOfficialIds`, in the several shape only
- `otherSpeakers`
- `quotations`, each a quote and a confidence
- a confidence for the whole answer

The model is not asked for issues or policy areas. The golden test pins the system prompt, the schema, and one input hash for each shape.

**Checks.** Code checks these rules and rejects the whole answer on any failure:

- The headline and summary are present and within their bounds.
- Every confidence is finite and from 0 to 1. This applies to the whole answer, the date, each speaker, each other speaker, and each quotation.
- The statement date is `yyyy-MM-dd`. Its quote occurs exactly once in the article text. The quote states the date in a form that document-change drafting accepts (`AnalysisAnswerRules.StatesDate`). Code then checks the UTF-16 offsets with `DocumentTextSpan`.
- There are 1 to 32 quotations. Each quote occurs exactly once in the article text, and no two quotes are the same span.

A quote error is a citation error. Every other error is an output error. The loop gives one more call with the reasons. After a second rejection, the entry fails with `citationRejected` or `outputRejected`. Both rejected runs stay, and the owner can requeue the entry. A retry prompt over the byte cap also fails the entry with the rejection code. The byte cap uses the same formula as the gate.

**Quotation kind.** `QuotedText` finds the quoted passages once for each article.

- A straight double mark toggles. A curly opening mark opens and a curly closing mark closes.
- A mark that does not fit the current state is plain text. Quotations do not nest.
- A quotation does not cross a line break. An opening mark with no closing mark on its line runs to the end of that line. This matches a quotation that continues into the next paragraph.
- Single quotation marks and apostrophes are ignored.

A span is a `DirectQuotation` when it lies entirely inside one quoted passage. Every other span is `ReleaseText`, also a span that contains the marks.

**Bill numbers.** `BillNumberFinder` finds `H.R.`, `S.`, `H.Res.`, `S.Res.`, `H.J.Res.`, `S.J.Res.`, `H.Con.Res.`, and `S.Con.Res.` numbers of 1 to 5 digits in the article text. It accepts spacing variants and writes each number in one form, such as `H.R. 1234`. It does not accept a form without periods, such as `HR 1234`. Each number appears once, in order of first use. The revision keeps them in `billNumbers`.

**Gate carry-over.** The revision starts with the issue IDs and policy area IDs of the gate. `PostgresStatementDraftStore` reads the latest gate run that has a reason. `StatementGateCarryOver` takes the verified tracked issue IDs and the policy area IDs when the reason is one of the three pass reasons. The two bill or vote reasons give an empty issue list. A failed-open or over-cap gate gives empty lists. Reviewers edit the lists later.

**Settlement.** A drafted settlement runs in one transaction in the C# store. It adds the draft, revision 1, the run with its link, the queue settlement, and the budget change. A draft that exists before the call succeeds the entry as `humanDraftExists`, with no call. A draft that appears during the call keeps the run as `DraftDiscarded` and adds no second draft. The entry then succeeds as `draftDiscarded`.

| State | Code | Meaning |
|---|---|---|
| Succeeded | none | Draft and AI revision 1 written |
| Succeeded | `over-byte-cap` | No model call. A `Bypassed` run and a draft with no revision. Review shows the statement as not drafted, and a reviewer writes the draft. |
| Succeeded | `humanDraftExists`, `draftDiscarded` | A draft already existed |
| Blocked | `evidenceUnavailable`, `speakersUnavailable` | The input is missing |
| Failed | `citationRejected`, `outputRejected`, a stop code, or `attemptsExhausted` | Owner requeue needed |

### Statement review

Statement review mirrors document-change review and uses the shared decision rules in [review](review.md#review-decisions). The Host has statement pages on top of the shared handlers.

**Ownership.** Core/Statements holds `StatementReview`, the aggregate of one candidate, and the rules on `StatementDraftRevision`. Application/Statements holds the commands `CreateStatementDraft`, `SaveStatementDraft`, and `DecideStatementReview`. It also holds the reads `GetStatementReview`, `ListStatementReviews`, and `ListStatementsNotInReview`, and the store boundary `IStatementReviewStore`.

Infrastructure/Statements holds `PostgresStatementReviewStore` and `StatementReviewQueries`. The store runs on the shared `ReviewRecords` core; see [review](review.md#storage-and-contracts). The handlers use `ReviewAuthorization`, `ReviewValidation`, `PageCursor`, `KeysetSlice`, and `PageLimit`. Every command needs a reviewer. The AI author can never decide or author a person revision.

**Write lock.** Statement writes take the statement review key; see [advisory locks](../architecture.md#advisory-locks). The AI settlement and the over-cap draft row take the same lock. A human create and an AI settlement for one candidate therefore run one after the other, and only one first revision exists.

**Create.** `CreateStatementDraft` writes revision 1 with the person as author, empty text, and no selections. It inserts the draft row, or it adds revision 1 to an over-cap row that has no revision. A draft that has a revision raises `StatementDraftExistsException`. The candidate and its extraction must exist.

**Save.** The request names the candidate, the expected revision number, the text, and the date with its evidence span. It also names the speakers, the other speakers, the issue IDs, the policy area IDs, and the quotation spans. A person never types the quotation kind or the bill numbers. `StatementDraftResolver.WithArticleDerivedFields` sets both, and the AI drafting path uses the same method.

- `StatementReviewContextReader` reads the evidence once for each write.
- `GetStatementReview` reads the page once. The store also reads the admitting configuration revision. `StatementSelectionResolver` gives the allowed selections and the officials to both paths.
- The speakers must be covered officials of the admitting configuration revision on the capture date (`StatementSpeakerRoster.OfficialsOn`).
- The issue IDs and policy area IDs must be in the issue configuration.
- Every span must cite the extraction of the candidate and fit in its text. `StatementDraftRevision.ValidateShape` checks the bounds.
- A stale expected revision raises `ReviewConflictException`.

**AI date.** An AI-proposed statement date survives a person's save only when the person ticks that it was checked. `AiProposedDate` in Core/Review owns this rule for statements and document changes. An AI revision that has a date cannot be approved.

**Decide.** `DecideStatementReview` checks the expected revision number and review state version, then applies `ReviewPolicy`. `ReviewValidation.CreateDecision` builds the decision for both record types. An approval runs `StatementDraftRevision.ValidateForApproval`. It needs a headline, a summary, at least one speaker, at least one quotation, and a date with evidence or no date. It also checks the allowed selections again and checks the spans against the extraction.

**Reads.** `GetStatementReview` returns the aggregate, the status, the source ID, the URL, and the extraction for quote display. It also returns the run ID when revision 1 is an AI draft. A candidate with no draft row reads as `NotDrafted`.

`ListStatementReviews` takes the filter `All`, `NeedsAction`, or `Approved`. It orders by creation time and then candidate ID. `NotDrafted` counts as needs action.

`ListStatementsNotInReview` lists candidates that have no draft revision, each with its stage and reason code. These are over-cap draft rows. They are also candidates with no draft row whose gate or draft entry is skipped, failed, or blocked. `StatementReviewQueries` holds the unpublished approved and not-in-review SQL.

**Pages.** `/Review/Statements` lists the drafts and the statements that are not in review.

- The drafts table has the filter tabs All, Needs action, and Approved, and a pager on `ListStatementReviews`. It shows the headline (or "Not drafted"), the source, the status, and the open concern count.
- The query values are `drafts` (filter), `draftsCursor`, `notInReview` (cursor), and `status`, as on the Review home page.
- The "Not in review" table has a pager on `ListStatementsNotInReview`. It shows the source, the URL, and the reason code. Each row has a Start draft post form.

`/Review/Statements/{CandidateId}` mirrors the document-change editor. It shows the AI authorship notice with the analysis run, the open-concern notice, and the compare panel after a stale save.

- The Statement panel has the tabs "Saved revision N" (`StatementRecord`) and "Edit wording".
- The Evidence panel shows the article text with the cited spans marked through `DocumentText` and `CitedText`.
- The Decide panel and the History table are the shared `ReviewDecisionPanel` and `ReviewHistoryPanel`.
- A `NotDrafted` candidate shows the evidence and a Start draft form in place of the editor.
- Both roles can draft and decide. The handlers keep the analysis subject out.

**Forms.** The pages use the static post-back pattern: named forms, an antiforgery token, and idempotency keys for each render. Hidden fields hold the expected revision and review state version. They post, redirect, and get with `?status=saved`, `decided`, or `started`.

`ReviewRecordPage` is the one owner of the failure mapping for both editors:

| Failure | Result |
|---|---|
| `ReviewConflictException` | 409, and the typed wording stays |
| `ArgumentException` | 400 |
| `UnauthorizedAccessException` | Redirect to `/access-denied` |
| Missing record | 404 |
| Any other failure | 503 |

A save checks the expected revision before it resolves the chosen spans, because the offered spans depend on the latest revision.

**Edit form.** The fields are the headline, the summary, and the statement date with a "Date checked" box for an AI-proposed date. The date evidence is a radio group with a "No statement date" choice. The speakers are choices from the officials allowed on the capture date. Other speakers are one name per line, at most `MaximumOtherSpeakers`.

Issues and policy areas are choices from the allowed sets. The quotations are checkboxes. The quotation kind and the bill numbers are not editable; the save handler sets them.

**Span picker rule.** `StatementSelectableSpans` builds the spans that the form offers, in this order:

1. the quotations and date evidence of the saved revision
2. the double-quoted passages that `QuotedText` finds in the article
3. the article paragraphs, each a line from `DocumentLines`, the same splitter that `DocumentComparison` uses

The list drops a repeated (start, length) pair and a span longer than `MaximumQuotationLength`. It stops at 200 spans. The page accepts only a posted key that the list offers.

### Gate labels

A gate label is the answer a person gives for one release. It lists the tracked issues that the release speaks to. It also says whether the release names a bill or a recorded vote. The labels measure the gate and the models that may replace it ([D-13](../roadmap.md#pending-decisions)).

**Store.** `PostgresGateLabelStore` adds one row for each save. A row holds the candidate ID, the extraction ID of the saved text, the author, the optional run ID, and the label JSON. The extraction ID must be the extraction of the candidate. `GetAsync` returns the saved text, the newest human label, and the newest model label.

**Split.** `GateLabelSplit.IsHeldOut` hashes the candidate ID with SHA-256. It holds the release out when the first four bytes, as a number, give a remainder under 30 when divided by 100. The result never changes for an ID. About 30 percent of releases are held out.

The store refuses a model label for a held-out release. A person still labels a held-out release, but never sees a model label for it.

**Page.** `/Review/GateLabels` is for the owner only. A reviewer goes to the access-denied page. It lists unlabeled releases, with held-out releases first. They are the test set, so a person label is the only label they can have. A release with only a model label is still unlabeled. The list shows 25 releases and the total.

`/Review/GateLabels/{CandidateId}` shows the saved text, a checkbox for each tracked issue of the issue file, and a yes or no choice for `namesBillOrVote`. The yes or no choice has no default. The form is prefilled with the human label, or else with the model label when the release is not held out. A save always adds a human row. It confirms or corrects the prefill. After a save, the page opens the next unlabeled release.

**Labeling rules.** People, model pre-labels, and judges use the same rules. They apply where the gate prompt is open to more than one reading.

- A listing page has no issue and no bill. Examples are press release indexes, news pages, issue pages, and newsletter archives. A listing page is not a statement, so the gate must skip it.
- Count the body of the release, including a background paragraph. Ignore sidebars, related links, and navigation.
- `namesBillOrVote` needs a bill title, a bill number, or a roll call that the text identifies. A bill with no name or number gives false.
- `namesBillOrVote` needs the speaker's own action or position on the bill, such as "I voted for" or "I introduced". A law that only funds an announced grant gives false.
- Tag every issue that matches. A conflict tags both `foreign-policy-and-war` and its narrower issue, as the issue file says.

**Export.** `statements labels export <path>` writes one JSON line for each release that has a human label. A line holds `candidateId`, `extractionId`, `heldOut`, `issueIds`, `namesBillOrVote`, and `text`. The text is the public release text. The command refuses a path that is not inside the `.runtime` directory under the current directory. Run it from the repository root.

### Commands

All statement commands are owner commands.

| Command | Effect |
|---|---|
| `statements status` | Gate status, limit, and pause reason |
| `statements list [outcome] [limit]` | Gate rows. Outcomes: `passed` (all three pass reasons), `skipped`, `failed-open`, `over-cap`, `pending`, `failed` |
| `statements run <run-id>` | One gate run |
| `statements requeue <candidate-id\|--all>` | Failed gate rows back to Pending |
| `statements regate [--since <date>]` | Skipped gate rows back to Pending when the latest run used another issue revision than the current file, or another gate prompt version |
| `statements draft status` | Drafting status |
| `statements draft list [view] [limit]` | Draft rows. Views: `drafted`, `over-cap`, `existing`, `pending`, `failed` |
| `statements draft run <run-id>` | One draft run |
| `statements draft requeue <candidate-id\|--all>` | Failed draft rows back to Pending. It never moves a gate row. |
| `statements labels export <path>` | Labels of record as JSON lines, inside `.runtime` only |

With `--since`, `regate` moves only candidates first captured on or after that UTC date. A skip made under another gate prompt version moves too, because the new prompt may find a bill or a vote. `regate` never runs by itself, and it needs `CIVIC_LENS_ISSUES_CONFIG`. A passed, failed-open, or over-cap gate row never moves.

## Storage and contracts

- The generated [statements schema](schema/statements.md) lists every table, column, key, check, index, and trigger.
- Candidate identity is the source plus the admitted URL. It is hashed as `PostgresCollectionJobStore.HashCandidate` hashes it for `collection_candidate_jobs`. The two tables share this value without a foreign key.
- `statement_candidates` keeps the first extraction of the article and its attempt.
- The migration `StatementCandidateQueue` added one candidate and one Pending gate row for each admitted article that already had an extraction.
- `statement_analysis` is keyed by `(candidate_id, task)`. Its tasks are `gate` and `statement-draft`. A `gate` row exists for every candidate. A `statement-draft` row exists after the gate lets the candidate pass.
- `statement_analysis` has the queue columns and checks of `document_change_analysis`, without `draft_id`.
- The trigger `trg_statement_analysis_notify` calls `civic_lens_notify_analysis_change`. The gate and drafting workers therefore wake on the same channel as the document-change worker.
- Gate lifecycle rules: a `Succeeded` entry needs a known reason code. A skipped entry may return to Pending for `regate`. A settled pass queues the `statement-draft` row in the same transaction.
- Core adds the run outcomes `Classified` and `Bypassed`. `AnalysisRun.ComparisonId` holds the candidate ID for gate runs.
- `statement_analysis_runs` keeps one insert-only row for each model call or bypass. It adds the issue revision, the reason, and the verified quotes to the document-change run.
- A gate run keeps the `IssueConfigurationRevision` ID it used. `context_json` holds a copy of the active rubric. An `Interrupted` gate run has no issue revision.
- A `Bypassed` run has no model call and no tokens. Its input hash is the text hash.
- A draft run has the task `statement-draft` and no issue revision ID. A `Bypassed` draft run holds the reason `over-byte-cap`. Its `context_json` holds the covered officials and the carried IDs.
- `statement_analysis_budget` holds the charged tokens for each UTC day and task. The gate, statement drafting, and document-change drafting each have a separate budget. `PostgresAnalysisQueue` runs the budget steps for both budget tables. The table mapping names the budget table and the task column.
- Status rows are in `document_change_analysis_status` under the task `gate` or `statement-draft`. The task name `draft` belongs to document-change drafting.
- `statement_drafts` has one row for each candidate, keyed by the candidate ID. `current_revision_number` is 0 when the draft has no revision.
- Each `statement_revisions` row has the author kind (`ReviewAuthorKind`: `Person` or `Analysis`). Revision 1 by the analysis author is the AI revision. Code only inserts revision rows.
- `StatementDraftRevision` holds the headline, the summary, and the statement date with its evidence span. It also holds `speakerOfficialIds`, `otherSpeakers`, `issueIds`, `policyAreaIds`, the quotations (an extraction span and a kind), and `billNumbers`. Confidence is never in the revision; it stays in the run output.
- The review record ID is the 64-character candidate ID. `ReviewValidation.ValidateCandidateId` accepts a lowercase SHA-256 value only.
- `statement_drafts` also has `review_state_version` and `review_status`. The store computes both with `ReviewPolicy` under the lock.
- A draft with no revision has the status `NotDrafted`. A migration set old rows with revision 0 to `NotDrafted` and every other row to `AwaitingReview`.
- `statement_review_decisions` holds the immutable decision JSON.
- The commands reuse the replay receipt table with the operations `statement-create`, `statement-save`, and `statement-decide`.
- `statement_gate_labels` holds the gate labels. Its key is a 32-character label ID. A row names the candidate and the extraction. It also holds the author subject, the author kind (`Person` or `Analysis`), the run ID, the label JSON, and the creation time. `ck_statement_gate_labels_run` ties the run ID to the `Analysis` kind. The run ID has a foreign key to `statement_analysis_runs` and a unique index, so one run labels one release.
- Version constants are listed in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Revision, quotation kind, bill numbers | `src/CivicLens.Core/Statements/StatementDraftRevision.cs`, `src/CivicLens.Core/Statements/QuotedText.cs`, `src/CivicLens.Core/Statements/BillNumberFinder.cs` |
| Candidate registration | `src/CivicLens.Infrastructure/Statements/PostgresStatementCandidateRegistration.cs` |
| Gate | `src/CivicLens.Application/Statements/StatementGateStep.cs`, `src/CivicLens.Application/Statements/StatementGatePrompt.cs`, `src/CivicLens.Application/Statements/StatementGateResolver.cs`, `src/CivicLens.Application/Statements/StatementGateLifecycle.cs`, `src/CivicLens.Application/Statements/RegateStatementCandidates.cs` |
| Drafting | `src/CivicLens.Application/Statements/StatementDraftStep.cs`, `src/CivicLens.Application/Statements/StatementDraftPrompt.cs`, `src/CivicLens.Application/Statements/StatementDraftResolver.cs`, `src/CivicLens.Application/Statements/StatementDraftLifecycle.cs`, `src/CivicLens.Application/Statements/StatementSpeakerRoster.cs`, `src/CivicLens.Application/Statements/StatementGateCarryOver.cs` |
| Shared quote check | `src/CivicLens.Application/Statements/ArticleQuote.cs`, `src/CivicLens.Core/Documents/UniqueQuote.cs` |
| Stores and models | `src/CivicLens.Infrastructure/Statements/PostgresStatementGateStore.cs`, `src/CivicLens.Infrastructure/Statements/StatementAnalysisListing.cs` (queue table name, shared list and article read), `src/CivicLens.Application/Statements/StatementArticle.cs` (article record), `src/CivicLens.Infrastructure/Statements/PostgresStatementDraftStore.cs`, `src/CivicLens.Infrastructure/Statements/StatementGateQueueMapping.cs`, `src/CivicLens.Infrastructure/Statements/StatementDraftQueueMapping.cs`, `src/CivicLens.Infrastructure/Statements/AnthropicStatementGateModel.cs`, `src/CivicLens.Infrastructure/Statements/AnthropicStatementDraftModel.cs` |
| CLI and composition | `src/CivicLens.Host/Statements/StatementCommand.cs`, `src/CivicLens.Host/Statements/StatementGateComposition.cs`, `src/CivicLens.Host/Statements/StatementDraftComposition.cs` |
| Review aggregate | `src/CivicLens.Core/Statements/StatementReview.cs`, `src/CivicLens.Core/Review/AiProposedDate.cs` |
| Review commands and reads | `src/CivicLens.Application/Statements/CreateStatementDraft.cs`, `src/CivicLens.Application/Statements/SaveStatementDraft.cs`, `src/CivicLens.Application/Statements/DecideStatementReview.cs`, `src/CivicLens.Application/Statements/GetStatementReview.cs`, `src/CivicLens.Application/Statements/ListStatementReviews.cs`, `src/CivicLens.Application/Statements/ListStatementsNotInReview.cs`, `src/CivicLens.Application/Statements/StatementSelectionResolver.cs`, `src/CivicLens.Application/Statements/StatementSelectableSpans.cs` |
| Review store | `src/CivicLens.Infrastructure/Statements/PostgresStatementReviewStore.cs`, `src/CivicLens.Infrastructure/Statements/StatementReviewQueries.cs` |
| Review pages | `src/CivicLens.Host/Review/StatementList.razor`, `src/CivicLens.Host/Review/StatementEditor.razor`, `src/CivicLens.Host/Review/ReviewRecordPage.cs` |
| Gate labels: label, split, store | `src/CivicLens.Core/Statements/GateLabel.cs`, `src/CivicLens.Core/Statements/GateLabelEntry.cs`, `src/CivicLens.Core/Statements/GateLabelSplit.cs`, `src/CivicLens.Application/Statements/IGateLabelStore.cs`, `src/CivicLens.Infrastructure/Statements/PostgresGateLabelStore.cs` |
| Gate label use cases | `src/CivicLens.Application/Statements/SaveGateLabel.cs`, `src/CivicLens.Application/Statements/ListGateLabelQueue.cs`, `src/CivicLens.Application/Statements/GetGateLabelTask.cs`, `src/CivicLens.Application/Statements/ExportGateLabels.cs` |
| Gate label pages and export | `src/CivicLens.Host/Review/GateLabelList.razor`, `src/CivicLens.Host/Review/GateLabelEditor.razor`, `src/CivicLens.Host/Statements/GateLabelExport.cs` |
| Reserved author kind | `src/CivicLens.Core/Review/ReviewAuthor.cs` |

The gate and drafting run on the shared owners in [analysis](analysis.md#code-map).

## Limits and planned work

- Actions are in [actions](actions.md). Relationships and weekly briefs are planned; see [roadmap](../roadmap.md).
- Planned: structured statement extraction, temporal identity resolution, candidate matching, reviewed relationships, and timelines.
- Each quotation stays addressable for later matching with actions.
- Planned after the first deploy ([R-38](../roadmap.md#later)): find the same or nearly the same sentences in releases from different offices. It shows coordinated messaging and who used the words first.
- Gate runs from before `statement-gate-v2` have no `namesBillOrVote` answer. `StatementGateCarryOver` reads them with a null answer.
- Rolling back the `GateBillOrVoteReasons` migration fails while any gate run holds a `passed-bill-or-vote` or `passed-bill-number` reason. Remove or change those rows first.
- The bill or vote answer of the model is unmeasured. The `passed-bill-or-vote` and `passed-bill-number` counts show how often each path fires. The bill number rule finds only the forms that `BillNumberFinder` accepts.
- The gate labels are the first part of D-13. The model pre-label, the model comparison, and the fine-tuning are planned. A model label needs `GateLabelEntry` with the `Analysis` kind and the ID of a run in `statement_analysis_runs`.
- The label page offers every tracked issue of the issue file. It does not hide an issue that was inactive on the capture date.
- The label queue reads every unlabeled candidate (ID, source, and URL) on each list load and each save, because code computes the split. Revisit this if labeling moves to a large set.
- Two labels with equal timestamps fall back to label ID order. This is acceptable for a single owner.
- Pages outside the URL rule of admission, such as `/news/email` subscribe pages, can still reach the gate. The gate does not reliably reject listing pages. In a preview, about half of the releases where models disagreed were listing pages.
- The labeling rules for unnamed bills, bills cited as credit, and sidebars are stricter than the gate prompt. A later prompt version may adopt them after the benchmark.
- A label save has no idempotency key. A double submit adds two equal rows, and the newest is the label of record.
- The export path check compares full paths. It does not resolve symbolic links.
- The split is a hash, so the held-out share is near 30 percent but not exact.
- Measured on 2026-10-16 with 30 House press feeds: the gate dropped 25 of 48 candidates. Most were grants and events. It also dropped a release about a farm bill vote, two bill introductions, and a Senate inquiry.
- Measured on 2026-10-16: 4 of 21 drafts carry a bill number. 2 of them name a bill with a collected House vote, and both members voted on it.
- Measured on 2026-10-16: some issue tags are doubtful. For example, a post office closure was tagged `elections-and-voting`.

R-16 decisions, accepted on 2026-10-16. The build starts after vote review, so that vote links are in the first release.

- A relationship has a named type and no verdict. The types are "describes own bill", "states position on", "claims credit for", and "statement about a vote". A stance such as consistent or inconsistent needs a method note first.
- Code finds the pairs: the same official and a shared bill number. A bill number without a Congress takes the Congress from the statement date. Pairs that share only a tracked issue wait for a measurement.
- Code also finds a pair by bill title, accepted on 2026-10-17. It looks for the saved title of each bill that the official sponsored or voted on. The match ignores case, a leading "the", and a trailing year such as "of 2025". The matched words are the cited span. The titles come from the saved bill details of the [bill stream](bills.md#admission).
- Measured on 2026-10-17 over 263 gated releases: 38 hold a bill number and 127 name something that ends in "Act". Some of those are listing pages or older laws. No release writes a number without periods, such as `HR 1234`.
- Planned after a measurement of the releases that title matching leaves unmatched: code lists the bills and votes of the official in a date window. The model picks one or none. Code checks the quoted span, and a person approves the pair.
- The model proposes the type and one cited sentence of reason. A person approves or rejects the relationship.
- A relationship cites the exact revision of each side, as a brief does. A changed flag is derived at each read. An approved relationship stays approved until a person withdraws it.
- Relationships are their own record kind, with tables, revisions, review pages, a review lock, and publication.
- Publication refuses a relationship when the release lacks either cited revision, or when its changed flag is set.
- Timelines are built from published records by official, issue, and date when a page renders. They need no review, because they only order records.
- Drafting uses Haiku with its own queue, budget table, and variable. No gate runs before it.

Statements and actions acceptance:

- [x] Every statement and action has exact, inspectable provenance.
- [x] Quotation validity and interpretation are separate checks.
- [x] Human review binds to the reviewed version.
- [x] Adding or removing tracked officials needs no code change and keeps history.
