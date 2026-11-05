# Explain legislation and issues: build plan

This survey only plans. It builds nothing. Paths cite origin/main at 77a9651c. The three features ship in this order: the AI text summary, the issue definition, and the shared bill line.

## 1. What exists now

### Feature 1: the action draft step

- `DraftOrWriteAsync` is the one place that chooses the source (src/CivicLens.Application/Actions/ActionDraftStep.cs:60-73). It builds a text request only when `NewestSummary` is present (:63).
- With no summary, it keeps the current text (:65-68), or code writes the text (:69-71), or it runs the issue-only loop (:72).
- Versions: `action-draft-prompt-v5`, `action-draft-schema-v2`, `action-issue-prompt-v3`, and `action-issue-schema-v1` (src/CivicLens.Application/Actions/ActionDraftTask.cs:10-13).
- The model is the constant `claude-haiku-5-5` (ActionDraftTask.cs:14). `CurrentPromptVersions` holds both prompt versions (:21).
- A stale prompt version requeues the draft (src/CivicLens.Infrastructure/Actions/PostgresActionDraftStore.cs:79-85, :130).
- The prompt puts the summary in at one line only (src/CivicLens.Application/Actions/ActionDraftPrompt.cs:73). The output schema is `HeadlineAndSummary` (src/CivicLens.Application/Analysis/AnalysisSchema.cs:22).
- `ActionDraftResolver.Resolve` checks the model output (src/CivicLens.Application/Actions/ActionDraftResolver.cs:68-89). `ActionCitations` finds the summary block span (src/CivicLens.Application/Actions/ActionCitations.cs:41-55).
- Every citation span must sit inside a stored extraction (src/CivicLens.Core/Actions/ActionDraftRevision.cs:137-141). Code approves after that check (src/CivicLens.Infrastructure/Actions/ActionDraftRows.cs:33-34; PostgresActionReviewStore.cs:161-168).

### Feature 1: the code-written text

- `ActionCodeText` writes the text from the label, title, sponsors, and introduced date (src/CivicLens.Application/Actions/ActionCodeText.cs:25-38).
- It adds "Congress.gov has no summary of this bill yet." when no summary exists (ActionCodeText.cs:21, :35). The reason code is `codeWritten` (ActionDraftErrorCodes.cs:14).

### Feature 1: bill text

- `bill_text_versions` holds `package_id`, `version_type`, `version_date`, `source_url`, `extraction_id`, `chars_total`, and `chars_read` (src/CivicLens.Infrastructure/Collection/Migrations/20261101165811_BillTextVersions.cs:28-57).
- A check caps `chars_read` at 60,000 (BillTextVersions.cs:47). The gate holds `text_package_id` and `text_state` (:14-26).
- A gate holds text only when `summary_count = 0` (BillTextVersions.cs:77). The job store admits text only then (PostgresCollectionJobStore.BillFollowOns.cs:123-124).
- No action code reads bill text yet. The plan is in docs/architecture/bills.md:332. A reader takes the text through `text_package_id` (bills.md:47).
- Commits: 588c8d52, 9a3913b7, 18c6c474, 6c925890, merged in 79162ad3 (#288).

### Feature 1: revision columns, contract, and page

- `action_revisions` holds `headline`, `summary`, `summary_version`, the `summary_citation_*` columns, and `text_origin` (20261103165049_ActionRevisionColumns.cs:91, :186-218). No column names the source kind.
- Citation child tables exist, such as `action_revision_sponsorship_citations` (ActionRevisionColumns.cs:282).
- Prompt version and model live on `action_analysis_runs` (docs/architecture/schema/actions.md:31-45).
- `PublishedAction` holds `Headline`, `Summary`, `TextOrigin`, `BillTitle`, `SummaryCitation`, and `BillCitation` (src/CivicLens.Publication.Contracts/PublishedAction.cs:12-24).
- `PublishedTextOrigin` is `Model`, `Person`, or `Code` (PublishedTextOrigin.cs:6). It cannot say "from the bill text".
- The labels are `ModelWording` and `CodeWording` (src/CivicLens.Host/Publication/PublishedRecordLabels.cs:81-83). The About page repeats them (PublishedAboutPage.razor:19).
- The action page shows a "Bill evidence" panel with "CRS summary" (src/CivicLens.Host/Publication/PublishedActionPage.razor:30-36).
- `public_api.bills` repeats both labels in SQL (20261104135239_PublicApiBillViews.cs:15-19).

### Feature 1: measure and backfill precedents

- R-67 asks for a live, budgeted, opt-in measure on fixed bills, with numbers on the PR (docs/roadmap.md:77). Its list has no action prompt.
- No measure command exists. The only paid path is the `LiveAi` test category (docs/operations.md:21-24; tests/CivicLens.Tests/Infrastructure/Actions/AnthropicActionDraftLiveTests.cs:9-15).
- No backfill has a drain guard. The nearest precedent is `bills subjects backfill [limit]` (src/CivicLens.Host/Collection/BillBackfillCommand.cs:18-23; src/CivicLens.Application/Collection/Bills/BackfillBillSubjects.cs:13-39).

### Feature 2: issue definitions

- Each of the 16 issues has `definition`, `includes`, and `excludes` (config/issues.json:133 onward). 12 issues have excludes.
- `TrackedIssueConfiguration` requires a definition (src/CivicLens.Application/Issues/TrackedIssueConfiguration.cs:7-26). No length cap applies to the definition or the terms.
- Only the model prompt reads them today (TrackedIssuePromptDocument.cs:5-13).
- `PublishedCatalogEntry` holds `Id` and `Name` only (src/CivicLens.Publication.Contracts/PublishedCatalogEntry.cs:3-7). Nothing reads `catalog.json` back (docs/architecture/publication.md:242). PublicApi never fetches it (ReleaseFeed.cs:19-47).
- The facet page gets a `ReleaseFacet` (PublishedFacetPage.razor:49). `ReleaseFacet` holds Kind, Id, Name, Records, and Icon (src/CivicLens.Application/Publication/ReleaseFacet.cs:13).
- `PublicationCatalog` owns issue names and icons (src/CivicLens.Application/Publication/PublicationCatalog.cs:18-20).
- D2 is in survey/schema-coherence.md:148 on the branch survey-schema-coherence. It is not on main. No name rows exist (publication.md:79).

### Feature 3: places that list a bill

Every list shows `entry.Headline`. No list carries a CRS or AI text summary.

- Record table: PublishedRecordTable.razor:38. Used by all records (PublishedAllRecordsPage.razor:14), facets (PublishedFacetPage.razor:40), and briefs (PublishedBriefsPage.razor:16).
- Official rows: PublishedRecordRows.razor:7-11, with the vote detail from `RollCall` (PublishedOfficialView.cs:269-273).
- Official issue pane: `bill.BillTitle` (PublishedRecordList.razor:55). Linked pairs: `BillTitle ?? Headline` (src/CivicLens.Application/Publication/LinkedPair.cs:28).
- Board closest vote: PublishedBoardPage.razor:10. Home latest: PublishedHomeLatest.razor:25. Related panel: PublishedRelatedPanel.razor:14.
- Search hits: `RecordHit` has no bill fields (src/CivicLens.PublicApi/Search/RecordHit.cs:10-25).
- Vote page bill panel: PublishedVotePage.razor:20-30. It links the action through `BillActionRecordId` (VoteRecordLoader.cs:60-66).
- `PublishedRecordEntry.BillTitle` is set on action entries only (PublishedRecordEntry.cs:18, :40-41). Vote entries carry no bill. `PublishedVote` holds `BillType` and `BillNumber` (PublishedVote.cs:128-129).
- `published_entries` stores entry facts (PublishedEntryRowConfiguration.cs:21). `ReleaseSplice` rebuilds entries byte for byte (ReleaseSplice.cs:38-66).
- `public_api.bills.summary` is the action text, not CRS (docs/architecture/public-api-columns.txt:20-35).
- The CRS text is published only as `PublishedAction.SummaryCitation.Quote` (PublishedAction.cs:23).
- No shared bill component exists. `ActionLabels.BillNumber` owns the bill number string (src/CivicLens.Host/Actions/ActionLabels.cs:11).
- The ZIP lookup has no page yet (docs/architecture/districts.md:7). R-47 lists insight boards (docs/roadmap.md:41).

## 2. PR list

Each PR starts from origin/main. A later PR waits for the one before it to merge.

### Feature 1

1. **Read the bill text for the draft. Small.** Add the gate text to `ActionDraftEvidence` through `text_package_id`. No prompt change. Tests: reader returns the current version; a bill with a summary gets no text. Precedent: the summary read in `ActionBillReader` (ActionBillReader.cs:37-64), 588c8d52.
2. **Store text citations. Medium.** Migration per D1. Core rules: a revision cites the CRS summary or the text, never both. Regenerate schema docs. Tests: Core shape rules, `ValidateAgainst` on a text span, a row round trip. Precedent: ActionRevisionColumns (20261103165049) and its sponsorship citation child table.
3. **Draft from the text. Large.** Add the text branch between the summary test and the code write (ActionDraftStep.cs:63-69). New text prompt and schema per D2. Send the first 60,000 characters. Check that each sentence quotes the text. The setting from D3 keeps it off. Tests: resolver refuses a sentence with no quote; source order; the code write still runs when text is absent. Precedent: 19c94142 and e9c28a14.
4. **Measure the text prompt. Small.** An opt-in, budgeted run on 20 fixed bills, per D4. The owner runs it and puts the numbers on the PR. Ordinary CI never calls it. Precedent: `LiveAi` tests (operations.md:21-24) and R-67.
5. **Publish the text summary. Medium.** Optional `PublishedAction` fields for the text source: version label, date, URL, characters read, and characters total. Page label, heading, source line, and cut note. Update the About page, the `public_api.bills` label CASE, and public-api-columns.txt. Tests: contract round trip, an old file reads, renderer shows the cut note only when cut. Browser checks at 360 and 1280 px. Precedent: c78689c2 and 2c6f0eeb.
6. **Backfill older bills. Medium.** `bills text backfill [limit]` admits text jobs for bills that the gate passed before text existed. The drain guard refuses while action draft work is pending, per D5. Tests: guard refuses, bounded limit, idempotent rerun. Precedent: `BackfillBillSubjects` (BackfillBillSubjects.cs:13-39) and operations.md:163-177.

The owner turns on code approval after reading the measured sample. That is a setting change, not a PR.

### Feature 2

7. **Show the definition on the issue page. Small.** Carry the definition, includes, and excludes from `PublicationCatalog` to `ReleaseFacet` and the page, per D6. Add length caps in `TrackedIssueConfiguration`. Tests: config validation, renderer shows the block on issue pages only. Precedent: the issue icon, 0fb99d10.
8. **Add the definition to the D2 issue rows. Small.** Do this in the D2 PR from the schema coherence survey, from the same `PublicationCatalog` owner. Precedent: `published_entries` (publication.md:51-90).

### Feature 3

9. **One owner for the bill line. Medium.** `BillLine` in Application/Publication picks the CRS summary, then the AI text summary, else nothing, per D7. Add an optional entry field and a `published_entries` column. Copy it in the splice. Tests: source order, never the code text, the splice and verify byte for byte, an old entry reads. Precedent: 2c6f0eeb, 8446c584, 4f63c56b.
10. **Show the line beside every bill. Medium.** One shared Razor component in the record table, official rows, board, home latest, related panel, and vote page. Vote rows find the action entry through the vote bill lookup, per D8. Browser checks in both themes. Precedent: 59f6cd64 and dc8e98c7.
11. **Serve the line in the public API. Small.** A `bill_line` column on `public_api.bills` and public-api-columns.txt. The later ZIP lookup reads it. Precedent: 3bc651a2.

## 3. Open decisions

### D1. How a revision stores the text citations

Problem: each of 2 or 3 sentences needs its own quote. The summary has one citation in columns (ActionRevisionColumns.cs:192-211). "Why it matters" needs quotes too.

- **Recommended: A.** Add a child table `action_revision_text_citations`, keyed on position, with a `part` column for summary or why it matters. Add `text_package_id` and `why_it_matters` to the revision. Pro: real rows, one span each, fits R-63. Con: a migration and a reader.
- B. Put the text quote in the summary citation columns. Pro: no table. Con: one span only, and the columns would mean two things.
- C. Store spans in JSON. Con: R-63 moves our data out of JSON (roadmap.md:96).

Precedent checked: `action_revision_sponsorship_citations` (ActionRevisionColumns.cs:282).

### D2. Prompt version for the text prompt

Problem: a bump of `action-draft-prompt-v5` requeues every older draft (PostgresActionDraftStore.cs:130). The CRS prompt does not change.

- **Recommended: A.** A separate `action-text-prompt-v1` and `action-text-schema-v1`, added to `CurrentPromptVersions`. Only text drafts requeue when it changes.
- B. Bump the shared prompt to v6. Con: a paid redraft of every CRS draft for no gain.

Precedent checked: the separate `IssuePromptVersion` (ActionDraftTask.cs:12).

### D3. What happens before the owner reads the sample

Problem: code approval must wait for the owner. The draft queue keeps running.

- **Recommended: A.** A setting turns the text branch off by default. Off, the step uses the code write as today. On, code approves after its checks. Pro: no unapproved text reaches review or release.
- B. Draft from text, but send each record to person review until the owner flips a setting. Con: a review load of up to 8,729 bills.
- C. No setting. Merge PR 3 after the measure. Con: PR 3 cannot merge before PR 4 runs on main code.

Precedent checked: `EnabledBillTextSource()` gates the text collection (PostgresCollectionJobStore.BillFollowOns.cs:123).

### D4. Form of the measure

Problem: no measure command exists. Twenty texts of up to 60,000 characters must not go into Git as bulk captures.

- **Recommended: A.** A Host command reads the 20 bills from the dev database, caps spend, and writes a report to the ignored artifacts/ folder. A fixed list of 20 bill IDs goes in Git.
- B. A `LiveAi` test with committed text fixtures. Con: about 1.2 MB of captures in Git.
- C. A `LiveAi` test that reads the dev database. Con: tests then need a live database.

Precedent checked: `AnthropicActionDraftLiveTests` (AnthropicActionDraftLiveTests.cs:9-15).

### D5. What "drained" means for the backfill

Problem: the backfill must not compete with current drafts.

- **Recommended: A.** Refuse when any action draft job is queued or leased. Print the count. The operator runs it again later.
- B. Admit backfill jobs at a lower priority. Con: no priority field exists; a new mechanism.

Precedent checked: `BackfillBillSubjects` runs bounded and idempotent, with no guard (BackfillBillSubjects.cs:13-39).

### D6. Where the issue definition travels

Problem: the page needs the text now. D2 rows need it later. One owner must hold it.

- **Recommended: A.** Config owns it. `PublicationCatalog` reads it once. Publish passes it to `ReleaseFacet` for the page, and to the D2 issue rows later. `catalog.json` stays as it is. Pro: no contract change, and the API reads the D2 rows.
- B. Add optional fields to `PublishedCatalogEntry`. Con: no reader exists (publication.md:242), and PublicApi never fetches it.
- C. Wait for the D2 rows and render from them. Con: blocks the feature on R-68.

Precedent checked: the issue icon path, 0fb99d10.

### D7. How short the bill line is

Problem: a CRS summary can run to 8,000 characters (ActionDraftRevision.cs:23). A row needs one line.

- **Recommended: A.** Code takes the first sentence of the CRS summary, or the first AI text sentence. It caps the line at 200 characters at a word boundary. Pro: no model call, exact words.
- B. A model writes a one-line version with a quote. Con: a new prompt, a measure, and a label.
- C. Show the full summary in a collapsed block. Con: long rows on phones.

Precedent checked: `ActionCodeText` builds text by code rules (ActionCodeText.cs:25-38).

### D8. How vote rows get the bill line

Problem: vote entries carry no bill (PublishedRecordEntry.cs:40-41).

- **Recommended: A.** Rows look up the action entry of the same release through the one vote bill lookup. Pro: one stored copy.
- B. Copy the line onto vote entries. Con: two copies of one fact.

Precedent checked: dc8e98c7 gave the vote bill lookup one owner.

## 4. New public strings for owner approval

The approved Feature 1 strings are not listed again.

1. Issue page heading: "What this issue covers"
2. Issue page list label: "Includes"
3. Issue page list label: "Does not include"
4. Bill line source mark: "From the CRS summary."
5. Bill line source mark: "AI summary of the bill text."
6. Version labels from govinfo codes, for the source line, such as "Introduced in Senate", "Reported in House", and "Enrolled bill". The full map comes to the owner with PR 5.
7. About page sentence: "When a bill has no CRS summary, an AI model summarizes the official bill text. Code checks each quote."
