# Review report for PR #493

PR #493: Move the officials list, party boards, and search to server pages. Head ddfea12, stacked on #501.

## Summary

The review found 3 P1, 8 P2, and 7 P3 findings. The PR's tests and checks pass.

These checks pass: locked restore, Release build, `dotnet format CivicLens.slnx --verify-no-changes --no-restore`, and `python3 tools/render-architecture.py --check`. I ran 383 tests, and all passed. They are the PR's listed test groups plus SearchBillNumberTests and BillNumbersTests. I did not run the full suite.

## Method

- A temporary xunit test on `ApiHost` used the seed helpers of `ListPageTests` and `BoardEndpointsTests`. It seeded these records: 33 officials (with a delegate, a resident commissioner, an independent, and a long name), 80 roll calls, H.R. 5334 and S. 5334, and two issues.
- The test rendered 11 pages: officials, officials with no match, boards, and eight search states.
- I captured each page and its Mosaic v2 mockup (`?skin=mosaic2`) at 360 and 1280 px, in light and dark. No page scrolls sideways.
- I deleted the temporary test after the run.
- The Impeccable context launcher was denied. I used SKILL.md, critique.md, audit.md, and mode-read.md instead.
- I used the Playwright that is already installed, not `npm i playwright@1.49.1`. The environment says that another version will not find its browser.

## P1 findings

### P1-1. The boards numbers do not show their time frame

The mockup eyebrow "119th Congress | Four lists, one for each chamber and party" is missing. The line "The newest roll call counted here, in the 119th Congress, is on {date}. No count includes a later day." is also missing. The panel lede names no Congress. The only Congress is in a closed fold, as the raw `PartyLineScope` text "…of the House in Congress 119. With party and against party compare…". When `NewerCongress` is set, the reader cannot see that an older Congress is in use.

- Where: `src/CivicLens.PublicWeb/Publication/BoardsPage.razor:5-7` and `:52`. `src/CivicLens.PublicApi/Boards/BoardsPageReader.cs:23` drops `NewerCongress` and `NewestRollCall`.
- Fix: Carry the Congress and the newest roll call into `BoardPanelData`. Render the approved eyebrow and the freshness line. Remove the raw scope line from the public text.

### P1-2. The bill number pin is missing approved parts

The mockup pin has "Sponsored by {name}", "Became law on {date} as Public Law {law}", and the "House vote on it" button. It also names the other match: "S. 5334, a Senate bill introduced by John Cornyn, is also in the list." The page shows only a vague line. In my render, S. 5334 cannot be reached from the page.

- Where: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor:41-52` and `src/CivicLens.PublicApi/Search/SearchPageReader.cs:54-62`.
- New string, not in the mockup: `" Became law as Public Law {law}."`. It also drops the date.
- Fix: Read the sponsor (through the owner of the term party rule), the law date, the vote link, and the other bill lines from `BillStore`. Render the mockup lines.

### P1-3. "Records that name {name}" gives the wrong set

The link keeps `q`. Thus a result must contain the typed words and also name the official. Records that name the official without those words disappear, under a label that promises all of them. The page also never shows that an official filter is on, and the reader cannot remove it.

- Where: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor:142`.
- Fix: Use `?official={id}` only. Show the active official in the status line or the filter fold. Any new text for that is an owner question.

## P2 findings

### P2-1. Members with zero breaks are ranked as breakers

The panels showed "Member 10 Long, 0 of 39 split votes, 0%" under "highest share of split votes cast against most Democrats". `Breakers` ranks every member with 25 or more split votes cast.

- Where: `src/CivicLens.PublicApi/Boards/BoardsPageReader.cs:22`.
- Fix: Filter `Breaks > 0` before `Take(PanelSize)`.

### P2-2. The officials page shows internal API text

The page renders: "Officials that the active release lists, with or without a term. A filter does not change this number. Each figure counts: Records of the active release that name the official and have an entry row. Votes are not counted." The words "entry row" and "active release" are jargon. The text is not in the mockup, and the PR body does not list it.

- Where: `src/CivicLens.PublicWeb/Publication/OfficialsPage.razor:51`, with the text from `src/CivicLens.PublicApi/Officials/OfficialScopes.cs:8,10`.
- Fix: Remove the line, or ask the owner for reader text. List it under Open questions.

### P2-3. The boards breadcrumb repeats itself

The breadcrumb reads "Home / Compare parties / Compare the parties", and the middle link goes to the static board. The mockup reads "Home / Compare parties".

- Where: the `PublicSections.Trail` call for `Section=Boards` with the `BoardsPage` title (`src/CivicLens.PublicWeb/Publication/PublicPage.razor:119`).
- Fix: When the section link is the page itself, end the trail at the section.

### P2-4. The boards page has no method link

The mockup panel "How these lists are made" ends with "Read the method note: how Civic Lens counts split votes and agreed votes".

- Where: `src/CivicLens.PublicWeb/Publication/BoardsPage.razor:85-87`.
- Fix: Add the approved link to the method page.

### P2-5. The search count looks like a total

"25 results for 'x'" counts only the current page when a next cursor exists. The Pager line on the second page, "Results 1 to {n} on this page.", is also wrong after a cursor.

- Where: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor:55`.
- Fix: The owner must approve the text (for example, a form that names the page). Thus this is a question, not an edit.

### P2-6. Some searches give no word about records

A search with officials but no record hits shows no message about records. The officials after the first three are cut off silently. My `q=member` render showed three officials and nothing else.

- Where: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor:16`.
- Fix: Show the approved empty-state block whenever `Hits.Count == 0`, below the official cards.

### P2-7. The officials pager has no working previous link

The first page shows a disabled button with the text "Newer", on a list sorted by name. The mockup has "Page 1 of 22 / Previous 25 / Next 25".

- Where: `src/CivicLens.PublicWeb/Publication/OfficialsPage.razor:76`.
- Fix: Hide the button, or keep a back cursor and use "Previous 25".

### P2-8. A doc contradicts the code

`docs/architecture/publication.md:346` says "Compare parties leads to `boards.html`". The code and the owner decision use `StaticBoardsPath`, which is `board-house-democrat.html`.

- Fix: Correct the doc line.

## P3 findings

### P3-1. An executive official's seat reads "Executive."

The search card passes `office: null`, but the officials list passes the office.

- Where: `src/CivicLens.PublicApi/Search/SearchPageReader.cs:52`.
- Fix: Add `Office` to `SearchOfficialData`.

### P3-2. Unused data member

`OfficialListRowData.Caucus` has no caller.

- Where: `src/CivicLens.PublicWeb/Publication/OfficialsPageData.cs`.
- Fix: Delete it, or show the caucus for independents.

### P3-3. Search landmarks have no names

There are two `role="search"` forms with no accessible name. The same `aria-label="Official"` repeats on each official card.

- Where: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor:6,28` and `src/CivicLens.PublicWeb/Publication/PublicPage.razor:44`.
- Fix: Give each one a different name.

### P3-4. The guidance does not fit some searches

A search with only filters still says "Check the spelling, or type fewer words." The blank page says "choose an official above", but no official is above.

- Where: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor:12-24`.
- Fix: Show the spelling step only when text is present. Correct the blank-page text, with owner approval.

### P3-5. The search page reads without one snapshot

`SearchPageReader` reads the release from the index. It reads issues, officials, and bills from the views, with no shared snapshot. For a short time, the "Release r" of the page can be different from the release of a card.

- Where: `src/CivicLens.PublicApi/Search/SearchPageReader.cs:23-35`.
- Fix: Compare the view release with the index release, or read the view parts in one snapshot.

### P3-6. Spacing and row style are different from the mockup

The pin has no space between its lines and its button. The board rows are cards, but the mockup uses ruled rows.

- Where: `.search-pin p` and `.list-row` in `src/CivicLens.PublicWeb/wwwroot/css/public.css`.
- Fix: Add space below the pin lines. Use ruled rows for the board list.

### P3-7. The snippet marker parser loses text

`Marked()` drops the text after a second end mark in one part. It also wraps text that has no start mark.

- Where: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor:149-164`.
- Fix: Track the open state across the parts, and add text only from balanced marks.

## Requested checks

- Search ranking: `ts_rank_cd` is unchanged. Exact name first lives in `OfficialStore` only. The store refuses it with no name or with a cursor. The test fails if the rule is removed.
- Empty result page: the message and the next steps come first, they match the mockup, and dark mode reads well.
- Boards: delegates and bases are labeled. Agreement shows once per chamber, outside the panels.
- Issue options: the SQL has one owner, `IssueStore.OptionsSql`. The bill type check uses `BillNumbers.IsType`.
- Shared owners: I found no copies of SharedVoteRows, PartyBar, AiSummary, SourceFold, or BillStatusMeaning.
- PR body claims: they are correct, except the doc line in P2-8.

## Approved departures

- Filters come after the results, and the label is "Filter by kind, issue or date". The owner approved this.
- The status line is "Showing {n} officials on this page. Release {r}." and not "1 to 25 of 542". Rows show "Term ends {date}". The owner approved both.
- The chamber options show no counts. That follows from the approved status line, which names no filtered total.
- The four board panels are stacked, with anchor tabs and not switching tabs. They work without JavaScript and keep agreement outside the panels, as the approved rule requires.
- The official card has no pronoun sentence, because the data holds no pronouns.
- Approved header changes: the kind links go to the search page filtered by kind, Statements is in the header, and the header links to the static board.
- The 14 px lede (18 px in the mockup) comes from the shared `.page-header-lede` style in `src/CivicLens.PublicWeb/wwwroot/css/app.css:24`, not from this PR. I flag it for a shared fix.

## Open questions to add to the PR body (exact text)

1. " Became law as Public Law {law}."
2. "No records match these filters."
3. "Officials that the active release lists, with or without a term. A filter does not change this number."
4. "Records of the active release that name the official and have an entry row. Votes are not counted."
