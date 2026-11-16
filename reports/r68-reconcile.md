# R-68 final reconcile

This report checks every unchecked R-68 item in `docs/roadmap.md` against main at `4969ed76`. Production runs that commit since 2026-11-16 22:00Z.

Status words:

- DONE: the code on main does it. The item leaves the roadmap. The owning feature doc holds the behavior.
- REMAINING: true work. The roadmap item now names only what remains.
- OUT: it needs a paid measure, a historical pilot, or an owner activation. It stays in the roadmap until the owner moves it. See Open questions.

Test paths are relative to `tests/CivicLens.Tests/`. Pages are in `src/CivicLens.PublicWeb/Publication/` unless the path says otherwise.

History note: main was rewritten on 2026-11-12. Work before #493 sits in one root commit, so its PR numbers do not come from `git log`. For that work the table gives the file and the test only.

## Counts

| Status | Roadmap items | Reader flows | Owner tests |
|---|---|---|---|
| DONE | 16 | 15 | 3 |
| REMAINING | 10 | 3 | 4 |
| OUT | 7 | 0 | 0 |
| Left for #604 | 1 | | |

## Roadmap items

### Routes

| # | Item | Status | Evidence | Smallest next step |
|---|---|---|---|---|
| 1 | Finish Home, Members, and the shared members strip | DONE | `HomePage.razor`, `ZipLookupResults.razor`, `PublicPage.razor:63` (strip slot in the shell of every page), `src/CivicLens.PublicApi/Pages/OldPageRedirects.cs:49` (compare redirect). Tests: `PublicApi/Pages/OldPageRedirectTests` (compare to `index.html#zip-results`), `PublicApi/Pages/ServerPageTests.TheNotFoundPageCarriesTheEmptyMembersStripAndTheSiteSearch`, `PublicWeb/MembersStripTests`, `PublicWeb/ZipMemberCardsTests.TheHomeAnswerLeadsWithHowTheMembersVotedThenTheCardsWithTheirIssueRowsFolded`. PRs #564, #580, #583. Doc: public-site.md, Members and Compare | |
| 2 | Complete the shared shell | DONE | `PublicPage.razor`, `PublicSections`. Tests: `PublicWeb/PublicNavigationTests` (21 tests: header, menu sheet, trail, footer report link, freshness line). PRs #551, #564, #580, #600. Doc: public-site.md invariants | |
| 3 | Kind parameter for `PublicationProtocol.ReportPageLink` | REMAINING | `src/CivicLens.Publication.Contracts/PublicationProtocol.cs:73` has no kind. `src/CivicLens.PublicWeb/wwwroot/js/public.js:51` writes "Revision {n}." | Add the kind to `ReportPageLink` and the report query. The new subject text is a public string, so it needs owner approval first |
| 4 | Finish vote pages | DONE | `VoteDetailPage.razor` (linked statements, bill status badge), `VotePageView.ThresholdOf` (two-thirds on suspension and veto override). Tests: `PublicWeb/VoteStatementsAndStatusTests` (10 tests), `PublicWeb/PartyBarTests.TheMarkSitsAtTheWholeNumberOfYesVotesThatTheVoteNeeds`, `PublicWeb/VoteDetailPageTests.EachVoteKindDrawsOnlyTheMarkItNeedsOnTheChamberBarAndNoPartyBarHasAMark`. PRs #527, #584. Doc: public-site.md `PartyBar` invariant | |
| 5 | Map page and Home map | DONE, except later layers | Done: vote map, tiles, table, map page with party mode, Home map. `VoteMapPage.razor`, `VoteMap`. Tests: `PublicWeb/VoteMapTests`, `PublicWeb/VoteMapPageTests`. PRs #519, #526, #600 | Later layers are OUT (item 5b) |
| 5b | Later map layers | OUT | Party breaks, seats up for election, issue activity, attendance, boundaries by Congress. No code | Owner moves it to a later roadmap item |
| 6 | Finish bill pages (correction banner) | DONE | `BillDetailPage.razor` renders `PublishedCorrectionBanner`. Tests: `PublicWeb/PublishedCorrectionBannerTests` (6 tests). PR #537. Doc: public-site.md, Correction and withdrawal pages row | |
| 7 | Finish the official and President pages | DONE | `OfficialProfilePage.razor`, `PresidentPage.razor`, `OfficialRecordPage`. Tests: `PublicWeb/OfficialProfilePageTests.ThePresidentPageCountsEachTypeLinksItsSectionAndShowsNoSpeechesSection`, `PublicApi/Officials/OfficialPageEndpointsTests.APresidentGetsPerTypeSectionsWithCountsAndNewestFirstRowsAndNoSpeeches`. PR #535. Doc: public-site.md inventory | |
| 8 | Move issues (stored line-up) | DONE in code | `src/CivicLens.PublicApi/Issues/IssueStore.cs:69` reads `public_api.release_issue_party_lineups`. Test: `PublicWeb/IssuePageTests.TheLineUpIsOnePartyBarWhoseSegmentsAddUpToTheBillCount`. The production run is item 13 | |
| 9 | Finish the other record pages | REMAINING | Done: typed Amends and Revokes links (`ExecutiveActionWords.TargetRelation`, test `PublicWeb/ExecutiveActionDetailPageTests.DocumentTargetsKeepTheirOrderExactReferencesAndOnlyLinkTargetsInTheRelease`). Missing: `StatementDetailPage.razor` shows no linked votes. `PublishedCorrectionList` is used only by `PublishedWithdrawalPage.razor` | Add a linked votes section to the statement page from `record_links`, as the vote page does for statements (#527) |
| 10 | Move lists, boards, search, and fixed pages | DONE, except the document changes list | Bill list order: `src/CivicLens.PublicApi/Bills/BillStore.cs:25` (#589). Officials last name order: `src/CivicLens.PublicApi/Officials/OfficialStore.cs:17` (#587). Previous link (#594). Tests: `PublicApi/Pages/RecordListPageTests.ACursorOfTheEarlierSponsorshipSortAnswersTheRefusedListPage`, `OfficialEndpointsTests.OfficialsSortByTheWholeRosterLastNameThenTheNameThenTheIdAndAWalkKeepsThatOrderBothWays`. Doc: public-site.md and search.md | |
| 10b | Document changes list | OUT | No document change is published yet. public-site.md limits say so | Owner moves it to a later item |

### Data

| # | Item | Status | Evidence | Smallest next step |
|---|---|---|---|---|
| 11 | Correction-history rollout | REMAINING (owner confirm) | Code done: `src/CivicLens.PublicApi/Records/CorrectionHistoryStore.cs`, banner on every record page (#472, #537). The cutover plan made the bootstrap a gate of PR 9 (#564), which is deployed. I have no production access, so I cannot see the run result | Owner confirms `releases corrections verify` passed with `failed` 0, then deletes the item |
| 12 | Stored-column backfill and `releases tallies verify` | REMAINING (owner confirm) | Code done: verifier and operator wrapper entry (`deploy/scripts/operator-command.sh:142`). Same gate as item 11 | Owner confirms the result, then deletes the item |
| 13 | Party line-up backfill and verify | REMAINING (owner confirm) | Code done: `releases lineups backfill` and `verify` (operations.md). The issue page already reads the stored line-up. Same gate as item 11 | Owner confirms the result, then deletes the item |
| 14 | Executive document summary input | OUT | Publication, label, and page slot are built (#531, #533). The step is off. It waits for a paid measure of 20 documents | Owner moves it |
| 15 | Historical bill action recovery | OUT | Source support is merged. The pilot needs separate approval | Owner moves it |

### AI text and vocabulary

| # | Item | Status | Evidence | Smallest next step |
|---|---|---|---|---|
| 16 | Keep the page text gate whole | REMAINING | `PublicWeb/PublicPageReadingGradeTests` grades every server page (#532, #543, #559). Missing: AI-text checks beyond the draft check, first-use popovers for exact terms in AI text | Owner names the exact terms and the AI-text checks to add |
| 17 | Measure each changed prompt | OUT | Needs a paid measure within a budget | Owner moves it |
| 18 | Bill key terms | OUT | Built behind `CIVIC_LENS_ACTION_DRAFTING_KEY_TERMS`, which is off (#539). Waits for the owner to read the sample | Owner moves it |

### Production review of 2026-11-14

| # | Item | Status | Evidence |
|---|---|---|---|
| 19 | Trust defects | DONE | Introduction date: #568, #581, test `Infrastructure/Collection/BillStream/PostgresBillStreamTests.AWorkerRevisionAndAPersonEditBothStoreTheIntroducedDateOfTheStoredBillRecord`. Search ingest: #569, #582. Error page: #570, test `PublicApi/Pages/ServerErrorPageFilterTests`. Update line: #575, test `PublicWeb/PublishedUpdateLineTests.ARoutineUpdateFromTheSourceShowsTheApprovedHistoryLabelWithTheUtcDayOfTheNewestUpdateAsAMutedLine`. Bill list timeouts: #571, #590. Senate party bars: #574, test `PublicApi/Votes/VotePageEndpointsTests.ASenateSplitVoteDrawsItsPartyBarsFromTheMemberListAndListsTheBreakersOfThePartyLineView`. Snippets and total: #576, test `PublicWeb/SearchResultsPageTests.TheStatusLineNamesTheTotalOfTheReleaseAndHowManyThisPageShowsAndTheFirstPageOffersNoWayBack` |
| 20 | Cut AI text that only restates facts | DONE | Code vote text: #572, test `PublicWeb/VoteDetailPageTests.TheAboutLineIsTheWholeBillLineWithItsSourceNoteRightUnderTheHeadline`. CRS text, no AI rewrite: #578, #592, test `PublicWeb/BillDetailPageTests.ACodeBillShowsTheCrsTextWordForWordUnderTheBillLineWithTheCrsTagAndNoAiLabel`. Short press release: #579, test `PublicWeb/StatementDetailPageTests.AShortReleaseShowsItsOwnSourceTextWithEachQuoteMarkedAndTheSourceTextLabelInPlaceOfASummary` |
| 21 | Delegates in their own list, no district 0 | DONE | #573. Tests: `PublicWeb/HomePageTests.ADelegateNeverTakesABreakCardAndShowsInTheDelegateListUnderTheCards`, `PublicWeb/OfficialProfilePageTests.ASeatWithDistrictZeroNamesItsTitleAndPlaceAndNeverDistrictZero` |
| 22 | Navigation and lists | DONE | #556, #580, #594. Tests: `PublicWeb/PublicNavigationTests.TheHeaderAndTheMenuSheetLinkEachSectionInTheMockupOrderWithStatementsAndThenANarrowOnlySearchAndNoLinkForTheOldAllRecordsList`, `PublicApi/Pages/FixedPageTests.WhileNoBriefIsPublishedTheBriefsListAnswersTheNotFoundPageAndAboutDoesNotLinkIt`, `PublicApi/Pages/OldPageRedirectTests` |
| 23 | Unclear numbers and copy | DONE | #583. Test: `PublicWeb/ZipMemberCardsTests.TheHomeAnswerLeadsWithHowTheMembersVotedThenTheCardsWithTheirIssueRowsFolded` |
| 24 | Visual cohesion pass | DONE | #600. Tests: `Architecture/PresentationArchitectureTests.EachTypeRoleHasOneSizeOnEveryPageAndPublicBodyTextIs16Pixels`, `RecordAndMemberTitlesShareOneLinkLookWithA44PixelTapHeight`. `MemberListFold` folds lists over 20 names. Home executive rows use `PublicRecordRow` |

### Gates

| # | Item | Status | Evidence | Smallest next step |
|---|---|---|---|---|
| 25 | Reconcile audit rows, M1 to M10, 18 flows, seven owner tests | REMAINING | See the tables below. The repository holds no audit file and no M1 to M10 list | Owner supplies the audit rows or confirms DR1 to DR10 and the cuts in public-site.md are the full ledger |
| 25b | "Get this data" link on every table | OUT | Owner rule: these links come after the cutover. No code | Owner moves it |
| 26 | Cold reads, traffic, query work | REMAINING | Owner local work. No change | Owner runs the cold-read check |
| 27 | Deploy and final live check, Cloudflare caching | REMAINING | Owner local work. No change | Owner runs the final live check |
| 28 | Cutover (checked item) | DONE | #564. Removed from the roadmap. public-site.md Behavior describes it | |
| 29 | Prune, then the migration squash | Left for #604 | Prune batches merged: #567, #595, #598, #602. The squash is open in #604 | #604 removes the item |
| 30 | Remove public `release.json` | DONE | Caddy serves only `/photos/` (`deploy/caddy/Caddyfile:36`). #564, #567. Doc: search.md says the publisher keeps a private snapshot that Caddy does not serve. R-63 owns the private file | |
| 31 | Final source, data, privacy, reader-flow, and design audit | REMAINING | Owner work at close | Run after items 25 to 27 |

## Reader flows

| # | Flow | Page | Test | Status |
|---|---|---|---|---|
| 1 | Find my members | `HomePage.razor`, `ZipLookupResults.razor`, `ZipMemberCards.razor` | `PublicWeb/ZipMemberCardsTests`, `PublicApi/ZipDistricts/ZipMemberCardsEndpointsTests` | DONE |
| 2 | My member on an issue, from the member | `OfficialProfilePage.razor`, `OfficialMembersFragment.razor` | `PublicWeb/OfficialProfilePageTests.TheStripShowsEachLatestRollCallAsAYesOrNoSquareOldestFirstWithBreaksAndMissesMarkedAndLabeled` | DONE |
| 3 | My member on an issue, from the issue | `IssuePage.razor`, `IssueMemberVotes.razor` | `PublicWeb/IssueMemberVotesTests` | DONE |
| 4 | Understand one vote | `VoteDetailPage.razor` | `PublicWeb/VoteDetailPageTests.TheAnswerComesFirstThenTheResultTheExplainersThePartyBarsAndTheMembers` | DONE |
| 5 | Understand one bill | `BillDetailPage.razor`, `BillStepper.razor` | `PublicWeb/BillDetailPageTests`, `PublicApi/Bills/BillPageTests` | DONE. Key terms stay off (item 18) |
| 6 | Read one executive action | `ExecutiveActionDetailPage.razor` | `PublicWeb/ExecutiveActionDetailPageTests.TheSourceFoldShowsEachSummarySentenceWithItsExactQuoteAndLinksTheBodyTextWithoutShowingIt` | REMAINING. The page links the body text and does not show it. The test locks that in |
| 7 | What the President did | `PresidentPage.razor` | `PublicWeb/OfficialProfilePageTests.ThePresidentPageCountsEachTypeLinksItsSectionAndShowsNoSpeechesSection` | DONE. Matrix row updated |
| 8 | Follow an issue | `IssuePage.razor` | `PublicApi/Issues/IssuePageTests`, `PublicWeb/IssuePageTests` | DONE |
| 9 | Compare parties | `BoardsPage.razor`, `BoardListPage.razor`, list pages | `PublicWeb/BoardsPageTests`, `PublicWeb/BoardListPageTests`, `PublicApi/Pages/RecordListPageTests` | DONE. "Split nav targets remain planned" in the inventory has no defined scope (Open questions) |
| 10 | Check a source | `Components/Ui/SourceFold.razor` on each record page | `PublicWeb/VoteDetailPageTests.OneCheckTheSourceFoldHoldsTheCitedFactsAndTheSourceTextAndNoTimelineExists` | REMAINING. The raw text sits inside the fold, not one click further. The vote fold starts with cited facts, not the official link |
| 11 | Search a name or a bill | `SearchResultsPage.razor` | `PublicWeb/SearchResultsPageTests.OfficialThenBillPinThenResultsThenFiltersInThatOrder` | DONE. Matrix row updated |
| 12 | Search with no results | `SearchResultsPage.razor` | `PublicWeb/SearchResultsPageTests.NoResultsShowGuidanceFirstAndLeaveOutTheResultListAndTheStatusLine` | DONE |
| 13 | Learn a term | `Term.razor`, `Glossary.cs`, glossary and vote-kind pages | `PublicWeb/GlossaryTests`, `PublicWeb/VoteKindsPageTests` | REMAINING. Glossary terms with example wording wait for approved text (public-site.md, Open choices) |
| 14 | Method and about | `MethodPage.razor`, `AboutPage.razor` | `PublicApi/Pages/FixedPageTests` | DONE |
| 15 | Report a problem | `PublicPage.razor` footer, `PublishedWithdrawalPage.razor` | `PublicWeb/PublicNavigationTests.TheFooterReportLinkNamesTheRecordAndBindsItsExactRevisionAndOtherPagesKeepTheFixedPageLink`, `PublicWeb/WithdrawalNoticePageTests` | DONE. Matrix row updated. Item 3 is a separate subject line fix |
| 16 | Useful 404 | `PublishedNotFoundPage.razor`, `PublishedServerErrorPage.razor` | `PublicApi/Pages/ServerPageTests`, `PublicApi/Pages/ServerErrorPageFilterTests` | DONE |
| 17 | Return and find my members | `PublicPage.razor` strip, `MembersStrip.razor` | `PublicWeb/MembersStripTests`, `PublicApi/Officials/OfficialPageEndpointsTests.ThePageCarriesTheReleaseETagTheMembersStripSlotAndTheSharedTrail` | DONE. Matrix row updated. No test checks the strip on every route; the shell owns it |
| 18 | Compare my members | `ZipLookupResults.razor`, `SharedVoteRows.razor` | `PublicWeb/SharedVoteRowsTests`, `PublicApi/Pages/OldPageRedirectTests` | DONE |

## Owner tests

| Owner test | Page | Test | Status |
|---|---|---|---|
| 1. Engaging | Every page | No test | REMAINING. Owner judgment at the cold read |
| 2. Informative | Every page | No direct test. Side checks: `PublicWeb/HomePageTests.UnknownTotalsStayAbsentButThePublishedResultRemainsVisible` | REMAINING |
| 3. Easy to use | Every page | No click-count test. Side checks: 44 px tap height in `PresentationArchitectureTests` | REMAINING |
| 4. Easy to read | Every page | `PublicWeb/PublicPageReadingGradeTests`, `PublicWeb/PublicFoundationTests.ThePublicTokensReachFourAndAHalfToOneInBothThemesOnTheSurfacesThatShowThem` | DONE |
| 5. Easy to navigate | Every page | `PublicWeb/PublicNavigationTests`, `PublicApi/Pages/PublicRouteMapTests` | DONE |
| 6. Easy to understand | Every page | `PublicWeb/GlossaryTests`, `PublicWeb/PublicFoundationTests.AnInfoPopoverHasAnAccessibleNameAndOpensTheSamePopoverASingleTermUses` | DONE |
| 7. Professional look | Every page | `Architecture/PresentationArchitectureTests` (one style owner). No test rejects placeholder or EXAMPLE text | REMAINING |

## Doc changes in this PR

- `docs/roadmap.md`: removed 13 DONE items and the checked cutover item. Items 5 and 10 keep only their OUT part. Rewrote items 9, 11 to 14, 16, and 25 to name only what remains. Removed the empty production review heading.
- `docs/architecture/public-site.md`: matrix rows 1, 2, 7, 11, 15, and 17 now say Implemented. Removed two stale limits: the bill list sort and the officials sort. Both orders are built (#587, #589) and documented.
