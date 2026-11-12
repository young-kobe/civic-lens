Visual review of main 4bcd416

## Scope and method

This review covers 11 pages on main at 4bcd416: statement, document change, President, withdrawal notice, reader report, weekly brief, officials list, party boards, search results, glossary, and Compare. It does not cover the vote, bill, official, issue, executive action, Home, map, or list pages.

Method:

- A temporary xunit test (deleted, not committed) used `ApiHost`, `ViewsSeed`, `StatementsSeed`, `OfficialsSeed`, `BillsSeed`, `HomeSeed`, `PublishedBriefContentSeed`, `DocumentChangeEndpointsTests`, and `FakeReleaseSite`. It requested the real `/web` routes for realistic records: a two-speaker statement with a long headline and a short one-speaker statement, a three-sentence document change, a President with 5 actions of 3 types, a withdrawn statement with two corrections, the seeded brief, 31 officials with a delegate and an independent, boards with real breaks and an empty release, and search by name, by bill number, with no result, and with no query. The report and glossary pages have no `/web` route, so the test rendered them with `ReleaseRenderer`, as the static publisher does. Compare came from `/web/compare`.
- The test wrote the HTML and the PublicWeb `wwwroot` files to `/tmp/pages`. `python3 -m http.server` served them. Playwright 1.49.1 drove the preinstalled Chromium.
- I captured each page and the matching mockup (`?skin=mosaic2`) at 360 and 1280 px, light and dark: 96 captures. I looked at each page capture and each mockup capture. I also captured open folds, the report page with a named record, and glossary sections at full scale.
- No page has horizontal scroll at 360 or 1280 px. Each page has one `h1` and one `main`, and no duplicate IDs.
- The Impeccable context launcher was refused by the sandbox, because it runs a downloaded binary. I used SKILL.md, the critique and audit references, and public-site.md.
- No paid API and no production system was called.

## Verdict

No P1. The pages are fit for the gated `/web` preview. Fix the P2 findings before the cutover PR moves these routes to the public paths.

| Page | P1 | P2 | P3 |
|---|---|---|---|
| Statement | 0 | 3 | 2 |
| Document change | 0 | 1 | 2 |
| President | 0 | 1 | 0 |
| Withdrawal notice | 0 | 1 | 2 |
| Reader report | 0 | 2 | 3 |
| Weekly brief | 0 | 0 | 0 |
| Officials list | 0 | 0 | 2 |
| Party boards | 0 | 0 | 1 |
| Search results | 0 | 0 | 1 |
| Glossary | 0 | 1 | 1 |
| Compare | 0 | 0 | 1 |
| Total | 0 | 9 | 15 |

The shared `EmptyState` defect (P2-6) also shows on the search page and the officials list. It is counted once, under the reader report.

## P2 findings

### P2-1 Statement: the speaker seat and party are missing

- Page: statement.
- File: `src/CivicLens.PublicWeb/Publication/StatementDetailPage.razor:8`, `:41-48`; `src/CivicLens.PublicWeb/Publication/RecordOfficialData.cs:3`; `src/CivicLens.PublicWeb/Publication/StatementPageView.cs` (`Eyebrow`).
- What the reader sees: the eyebrow reads "Statement | Elizabeth Warren, Ayanna Pressley". The rail card "Speakers" lists names only. The approved mockup reads "Statement | Mark Kelly, Senator for Arizona, Democrat", and the rail card shows "Senator for Arizona, Democrat" under the name. A reader cannot see who the speaker is without a second click.
- Fix: add the seat and the party of the term that holds the statement date to `RecordOfficialData`, read from the views in `RecordSnapshot`. Take the party from the one shared term-party owner that sponsorship uses. Do not add a second rule. Render "{seat}, {party}" in the eyebrow and under each name in the rail. Use the seat words of the officials list, which already render "Senator for NV" and "Delegate for DC".

### P2-2 Statement: the approval and publication line is missing

- Page: statement.
- File: `src/CivicLens.PublicWeb/Publication/StatementDetailPage.razor:10`; `src/CivicLens.PublicWeb/Publication/StatementPageView.cs:56`; `src/CivicLens.PublicWeb/Publication/StatementPageData.cs`.
- What the reader sees: no line says who approved the record and when it was first published. The approved mockup shows "Approved by code 30 Oct 2026, 00:42 UTC. First published 30 Oct 2026, 00:44 UTC. Revision 1." under the lede. The source fold shows only "Revision". The brief page already shows "Approved" and "First published" facts from the same `published_entries` times (#504, #524).
- Fix: add the approval time and the first publication time to `public_api` statement detail and to `StatementPageData`. Add them as fact rows with the approved labels "Approved" and "First published" of `PublishedBriefView.Facts`, or show the mockup line under the lede. Reuse the brief rule; do not copy it.

### P2-3 Statement: the lede shows the raw source URL

- Page: statement.
- File: `src/CivicLens.PublicWeb/Publication/StatementDetailPage.razor:10`.
- What the reader sees: "Said on 3 Mar 2026. From https://article.example/story." A real press release URL is long and hard to read at the top of the page. The approved mockup reads "From a press release on kelly.senate.gov." and puts the link in a "Source" rail card.
- Fix: show the host name as the link text in the lede, and keep the full URL in the source fold, where it already is. The exact lede words are new text, so the owner must approve them (see Open questions).

### P2-4 Document change: the redline uses red and green

- Page: document change.
- File: `src/CivicLens.PublicWeb/wwwroot/css/theme.css:111-114`, `:243-246`, `:276-279`, `:323-326`; used at `src/CivicLens.PublicWeb/wwwroot/css/app.css:144-145`.
- What the reader sees: removed words are dark red on pink, and added words are dark green on mint. The theme decision says "Red marks errors only", and the brand has no red. The approved mockup marks removed text with an amber tint and added text with a navy tint (`docs/design/mosaic-v2/styles.css:321-322`). A reader can read red as an error on the record.
- Fix: set `--del` and `--del-bg` to the amber of the mockup (`rgba(180,95,0,.14)` tint with ink text) and `--ins` and `--ins-bg` to the navy tint (`rgba(39,71,217,.12)` with ink text), in light and dark. The strike and the underline keep the meaning without color. Check that `.notice-ok` and `.notice-bad` (app.css:97, :99) still read as status colors, or give them their own tokens.

### P2-5 President: the page gutter is 16 px, not 32 px

- Page: President (and the member official page, which shares the rule).
- File: `src/CivicLens.PublicWeb/wwwroot/css/public.css:616`.
- What the reader sees: at 1280 px the breadcrumb starts at x 32, but the eyebrow, the h1, the count cards, and the sections start at x 16. The content does not line up with the trail, the header logo, or the footer. Every other reviewed page uses the 32 px frame.
- Fix: delete the `padding: 24px 16px 48px` override in `.public-main:has(> .content > .official-page)`, or set its inline padding to `var(--frame-pad)`. Coordinate with the job that changes the official page, because the rule is shared.

### P2-6 Reader report, search, officials: the empty state heading skips a level

- Page: reader report ("Find the record first"), search with no result, officials list with no match.
- File: `src/CivicLens.PublicWeb/Components/Ui/EmptyState.razor:2`.
- What the reader sees: the heading order is h1, then h3. A screen reader user who moves by heading finds a missing level. This is an accessibility failure (WCAG 1.3.1).
- Fix: render `h2` in `EmptyState`, or add a required `Level` parameter so each page sets the level that follows its own outline. Add a test in `EmptyStateTests` that checks the level.

### P2-7 Reader report: the subject line shows a 64-character ID and the word "version"

- Page: reader report.
- File: `src/CivicLens.PublicWeb/wwwroot/js/public.js:164`.
- What the reader sees: "You are reporting the record "Warren and Pressley say ...", ID aaaaaaaa... (64 characters), version 1." The hex ID is noise for a reader. The word "version" conflicts with "Revision", which every record page and the withdrawal notice use. The approved mockup shows "You are reporting this record", the title as a link, then "Statement, revision 1, first published {time}. Wrong record? Choose another."
- Fix: drop the ID from the visible sentence and keep it in the hidden form field. Use "revision". Follow the mockup block: title link, kind, revision. The exact sentence is approved mockup text, so no new string is needed when it follows the mockup.

### P2-8 Withdrawal notice: the "What you can do" card is missing

- Page: withdrawal notice.
- File: `src/CivicLens.PublicWeb/Publication/PublishedWithdrawalPage.razor:12-18`.
- What the reader sees: the page ends after the fact list and the corrections. A reader who arrives from an old link gets no next step. The approved mockup has a card "What you can do" with "Search the records", "Report a problem with this notice", and "How we withdraw a record". PR #504 dropped the card because "the data holds none of them". That is true only for the first link ("Go to the page of this official"). The other three links need no record data.
- Fix: add the card with the three approved links that need no data: search, the report page, and the method page. Keep the official link out until the withdrawal stores the speaker.

### P2-9 Glossary: definitions run the full card width

- Page: glossary.
- File: `src/CivicLens.PublicWeb/wwwroot/css/public.css:338-340`.
- What the reader sees: at 1280 px each "Means" and "Why it matters" line is about 1,170 px wide, near 170 characters. Long lines are hard to track, and the page is about 13,000 px tall. Other text pages cap prose at `--measure`.
- Fix: add `.glossary-term dl { max-width: var(--measure); }` (or on `dd`). Keep the index full width.

## P3 findings

### P3-1 Statement: an issue that is not configured shows its raw ID

- File: `src/CivicLens.PublicWeb/Publication/StatementDetailPage.razor:64`; `src/CivicLens.PublicApi/Records/RecordSnapshot.cs:45`.
- What the reader sees: next to the chip "Health care costs" the plain text "unlisted" shows. A real issue ID is a slug, such as "health-care", with no meaning for a reader.
- Fix: leave out issues that the release does not configure, or show them under a muted label. The static page does the same, so fix both through the one view.

### P3-2 Statement: the "Source" rail card is missing

- File: `src/CivicLens.PublicWeb/Publication/StatementDetailPage.razor:40-69`.
- What the reader sees: the rail has speakers and issues only. The approved mockup has a "Source" card with the host link and "Seen {time}". The data is on the page in the fold.
- Fix: add the card with the source host link and the existing `SourceSeen` label. This also carries the fix of P2-3.

### P3-3 Document change: the redline has no visible "Removed" and "Added" labels

- File: `src/CivicLens.PublicWeb/Components/Evidence/RedlineText.razor`.
- What the reader sees: the labels exist only for screen readers. The approved mockup shows small "REMOVED" and "ADDED" labels before each mark, so a reader who does not see the strike still knows the change.
- Fix: render the existing hidden labels as visible small caps, as in the mockup.

### P3-4 Document change: the main cards and the source fold have two widths

- File: `src/CivicLens.PublicWeb/wwwroot/css/public.css:557-559`.
- What the reader sees: at 1280 px the cards stop at about 992 px, but the "Check the source" fold runs the full 1248 px. The right edge does not line up. The mockup uses one width.
- Fix: give the fold the same maximum width as `.detail-stack` on pages with no rail, or let the cards span the frame as in the mockup.

### P3-5 Withdrawal notice: the record fact and the document title show the raw ID

- File: `src/CivicLens.PublicWeb/Publication/PublishedWithdrawalView.cs:23`; `src/CivicLens.PublicWeb/Publication/PublishedWithdrawalPage.razor:24`.
- What the reader sees: the fact "Record" shows 64 hex characters, and the tab title is "Withdrawn statement: cccccccccccc". The mockup shows no ID.
- Fix: move the ID into the source fold or a small muted line, and title the tab "Withdrawn record | Civic Lens".

### P3-6 Withdrawal notice: the banner uses the blue info tone, and the corrections have no heading

- File: `src/CivicLens.PublicWeb/Publication/PublishedWithdrawalPage.razor:7`, `:17`; `src/CivicLens.PublicWeb/wwwroot/css/app.css:95`.
- What the reader sees: `Tone.Neutral` falls back to the base `.notice` style, which is blue info. The mockup uses a neutral box with a yellow edge. The correction list follows the fact list with no heading, so it reads as part of the facts.
- Fix: give `Tone.Neutral` its own neutral class. Put the corrections under an h2. The heading text is new, so the owner must approve it.

### P3-7 Reader report: the category is a select, not the approved radio list

- File: `src/CivicLens.PublicWeb/Publication/PublishedReportPage.razor:21-28`.
- What the reader sees: "Choose one" in a drop-down. The approved mockup shows the six choices as radio rows, so the reader sees every choice at once.
- Fix: render the six options as a radio group in a `FieldSet`, with the same values. Keep `required` on the group.

### P3-8 Reader report: the lede says "checks", the approved text says "reads"

- File: `src/CivicLens.PublicWeb/Publication/PublishedReportPage.razor:3-4`.
- What the reader sees: "A person checks each report." The approved mockup says "A person reads each report."
- Fix: use the mockup text in the lede and the description.

### P3-9 Reader report: a large gap sits between the form card and "About reports"

- File: `src/CivicLens.PublicWeb/Publication/PublishedReportPage.razor`; `src/CivicLens.PublicWeb/wwwroot/css/public.css`.
- What the reader sees: about 70 px of empty space after the form card at 1280 px, then the next card. Other gaps on the page are about 24 px.
- Fix: remove the extra block margin so all cards use the stack gap.

### P3-10 Officials list: the status line shows the release number and no range

- File: `src/CivicLens.PublicWeb/Publication/OfficialsPage.razor:54`.
- What the reader sees: "Showing 25 officials on this page. Release 1." The approved mockup reads "Showing 1 to 25 of 542 officials." and "Page 1 of 22" by the pager. "Release 1" is internal jargon on a list page.
- Fix: use the approved mockup text. The total is on the page already. Keep the release in the page metadata only.

### P3-11 Officials list: the pager has no position and no Previous link

- File: `src/CivicLens.PublicWeb/Components/Ui/Pager.razor`; `src/CivicLens.PublicWeb/Publication/OfficialsPage.razor`.
- What the reader sees: only "Next page". The mockup shows "Page 1 of 22", "Previous 25", and "Next 25". On page 2 the reader has no way back except the browser.
- Fix: carry the previous cursor or the page number in the query, and show the approved position text.

### P3-12 Party boards: the gaps between panels are not equal

- File: `src/CivicLens.PublicWeb/wwwroot/css/public.css` (board panel rules).
- What the reader sees: at 360 px the gap between two board panels is about 48 px, but the gap before "How these lists are made" is about 24 px.
- Fix: use one stack gap for all panels.

### P3-13 Search: the result count and the "Records" heading float in large gaps

- File: `src/CivicLens.PublicWeb/Publication/SearchResultsPage.razor`; `src/CivicLens.PublicWeb/wwwroot/css/public.css`.
- What the reader sees: at 1280 px there is about 50 px above the count line and about 70 px above "Records". The mockup keeps the count close to the results.
- Fix: put the count line directly above the record list and use the stack gap.

### P3-14 Glossary: one source shows a link name as plain text

- File: `src/CivicLens.PublicWeb/Publication/Glossary.cs:47`.
- What the reader sees: under "Votes against their party" the source reads "Our page How the counts work" as plain text. "Split vote" and "Substantive vote" show "How the counts work" as a link.
- Fix: make this source the same link to the method page as the other two terms.

### P3-15 Compare: the footer does not reach the bottom of the window

- File: `src/CivicLens.PublicWeb/wwwroot/css/public.css:322` and the page shell.
- What the reader sees: the page is short, so at 1280 by 900 the body color shows under the footer. In dark mode a dark band also shows between the hero and the footer. The page looks unfinished.
- Fix: make the page shell a column with `min-height: 100dvh` and let `main` grow, so the footer sits at the bottom.

## Code review

- Shared owners: the pages reuse `AiSummary`, `SourceFold`, `Facts`, `PublicRecordRow` (search), `PublishedBriefCitations`, `PublishedCorrectionList`, and `PublicSections.Trail`. I found no copy of `SharedVoteRows`, `PartyBar`, `BillStatusMeaning`, or the bill-number table. P2-1 must reuse the one term-party owner and must not add a second one.
- `PublishedCorrectionView` (`src/CivicLens.PublicWeb/Publication/PublishedCorrectionView.cs:10`) says "The vote facts did not change." for every kind. Today only a vote gets `FactsUnchanged`, so no reader sees it on another kind. This is safe now, but the wording has a vote-only rule with no guard. A guard in `PublishedCorrection.ValidateAll` or a kind-aware text would protect it. No finding is counted.
- I found no test that cannot fail in the endpoint and component tests of these pages. `RecordPageEndpointsTests` and `ListPageTests` assert exact text, order, and links.
- New public strings: each string I traced came from a merged PR that the owner reviewed (#493, #495, #504, and the record page PRs). I found no new unapproved string on these pages. The fixes above that need new text are listed under Open questions.
- Seed limits: the statement seed stores a capture time of tick 123, so "Source seen" shows 1 Jan 0001. The document change seed saves both captures at the same time. These are test data, not defects.

## Approved departures

- Statement: no "Votes linked to this statement" section. The section is empty and its fixed text is marked EXAMPLE. Decision O6 hides empty linked sections.
- Statement: the AI label reads "An AI summary of this release." It comes from the view label column, as the standing rule requires.
- Statement: the check line adds the capture caveat. It is the approved caveat of the static page, merged into the check line (R5).
- Document change: no AI summary box. The seeded summary is person text, so no AI label applies. The "Passage 1 of 1" label replaces the mockup excerpt label, because the stored data has no section name.
- President: no "What the President said" section. Decision P2 hides speeches until a source exists. No "Offices held" fold, because the official page job owns terms.
- Withdrawal notice: the heading "This record was withdrawn", the labels "What was withdrawn" and "Note", the neutral `Notice`, and no "Who decided" or title line. PR #504 recorded these as approved or as data the store does not hold. Only the card in P2-8 needs no stored data.
- Weekly brief: no figure tiles (L2 drops weak figures), no "Earlier week" link (marked EXAMPLE), cited rows with kind and revision, and the fact list "How this brief was made". PR #504 recorded each as approved.
- Party boards: four stacked panels with jump chips and one agreement card per chamber outside the party panels (K2). The mockup puts the agreement inside a tab.
- Search: the filter fold comes after the results (reader flow row 11). The record rows carry a kind icon square, which the theme decision allows for record colors.
- All pages: the header has no "Statements" item. The owner removed it on 2026-11-12 (DR6). The header is a site part, not part of these pages.
- All pages: no freshness line "Records as of", no EXAMPLE marks, and no skin switcher. These belong to the prototype or carry EXAMPLE time.
- Glossary and Compare have no mockup. I judged them by the Mosaic v2 tokens and the Impeccable rules. Both use the theme tokens, the type scale, and 44 px targets.

## Open questions

These fixes need owner text. I propose no wording.

- P2-3: the lede source sentence for a statement. The mockup form "From a press release on {host}." needs a source type that the data does not store.
- P3-6: the heading above the correction list on the withdrawal notice.
