# Public site

## Purpose

The public site lets a regular citizen see what Congress and the President did, and what officials said. Each page gives a plain answer first and the proof one click away. The site is also the project's portfolio, so it must look and work like a finished product.

Every page and component must pass seven owner tests:

1. Engaging: the reader wants to continue.
2. Informative: the page adds facts that the reader did not have.
3. Easy to use: each task takes few clicks.
4. Easy to read: short text, clear type, enough contrast.
5. Easy to navigate: the reader always knows where they are and where to go.
6. Easy to understand: each term, number, and date has its context.
7. Professional look: one consistent system, with no leftover placeholder.

This page holds the design rules and the plan for the reader flows. [Publication](publication.md) owns the release data, the page text rules, and the `public_api` views. [Host](host.md) owns the component library rules. The [roadmap](../roadmap.md#later) step R-68 owns the order of the work.

Status note: the owner accepted the design below on 2026-11-06. The Mosaic v2 palette, type foundation, focus colors, and reduced-motion behavior are implemented. The `/web` home route is a gated PublicApi server-rendered preview. It shows the published featured vote, recent votes, optional brief, configured issue counts, executive action groups, and chamber freshness. Its available-data bento is implemented; map, party bars, and break board remain planned. The existing static home remains live. Each row below says what is implemented and what is planned.

## Decisions

### Theme

- The visual authority is the owner's [Mosaic v2 reference](https://claude.ai/artifact/BNAG2e2nKJVpxfqF2xe2Mr) and its Civic Lens Themes export. Select `mosaic2` when rendering its reader pages. Match the full composition, typography, spacing, colors and responsive behavior. EXAMPLE text requires separate wording approval. The theme-picker toolbar is a prototype control.
- The theme is Mosaic v2. The prototype tokens are in the `mosaic2` block of the prototype skin file. Shared public theme tokens live in `src/CivicLens.PublicWeb/wwwroot/css/theme.css`.
- Color follows a 60-30-10 split. The page is a warm off-white in light mode and a deep ink in dark mode. Navy `#14213d` is the second color. Civic yellow `#f5b301` is the accent.
- Yellow marks primary buttons, the current navigation item, and key highlights. Yellow is never text on a white background.
- Four record types have four colors: vote teal, bill green, statement brown, and executive slate. The four colors have equal OKLCH lightness and chroma. A record color marks only the icon square, a thin edge, and the tint.
- Purple marks AI text only. Red marks errors only.
- The brand has no red and no saturated blue, because party colors may become red and blue.
- Type: Archivo 900 condensed for the h1 and large numbers, Gabarito for headings, Figtree for body text. Font files and OFL texts are self-hosted in PublicWeb.
- At 1280 px and wider, the page has a 1440 px maximum width. Home uses a 12-column bento grid. Vote, bill, and official pages have a sticky 320 px right rail of key facts. Below 1280 px, every page is one column.

### Home

- The hero side tile is a featured vote. A fixed rule in code picks it: the latest passage vote on a bill. The preview shows the result, labeled summary, published counts when available, and a ZIP prompt. A map and party bars remain planned.
- The featured vote does not repeat in the recent votes list. It includes the bill line and what a Yes vote meant.
- Recent votes show up to two House votes and two Senate votes. When all four shown votes have stored split alignment, the server home shows one caption that names those four votes and the opposing party majorities. Missing alignment, mixed majorities, or fewer votes never produce that caption.
- Issue tiles name their substantive vote and bill counts and the Congress.
- Executive actions show one row per published document type, with its newest title, signed date, and record count.
- The freshness line names the newest published roll call date for each chamber. A chamber with no published roll calls is omitted.
- At 1280 px and wider, votes and issues share the first bento row, then executive actions and method share the second. ZIP results and the brief span the grid. Below 1280 px the layout is one column.
- A weekly brief shows as one "This week" lead line above the latest votes, only when a brief exists (DR5). The server home implements this placement.

### Reader-flow decisions

These decisions come from the component audit. The owner accepted every recommendation, the top 10 changes, and every strengthen and merge verdict.

- DR1: Member cards show attendance and one party-line count with its base. They show no bare Yes and No totals. ZIP cards show each positive party-line figure with the party pinned to its votes, its Congress, scope, and dates. Figures with no qualifying split votes are absent.
- DR2: The official vote strip marks only a vote against most of the party and a missed vote. Each mark is a link. The caption names the scope.
- DR3: A vote page puts the answer first. The fact rows and the source texts go into one "Check the source" fold on the same page.
- DR4: The first map layer colors by cast only. The party layer waits for its own step. Then the owner picks the party colors.
- DR5: See Home above.
- DR6: Per-kind list pages replace "All records" in the navigation. The navigation includes Votes, Bills, Executive actions, Statements, and Issues. Policy area pages and old URLs stay, outside the navigation.
- DR7: A record row shows the link type, reason, quote, and fact. The relationship URL stays for old links.
- DR8: Bills get a status stepper. Other kinds put dates in the lede and site events in the fold. The timeline panel is cut on votes, statements, and executive actions.
- DR9: One plain name for each vote kind appears everywhere. The record term appears in the popover and the fold. The owner approves each name.
- DR10: The officials list shows seat, party, term end, and one labeled figure. Until the paging route exists, it shows seat and party only.
- Cut B4: the bill cost and date slot.
- Cut V12: the timeline panel on votes, statements, and executive actions.
- Cut F2: the voting boards panel on the officials list. Party boards remain available through navigation.
- Cut L3: the links panel on official pages. The official website stays in the profile header.
- Merge: the two vote kind blocks into one. Merge the CRS block into a fold under the AI summary. Merge the Congress.gov card into the source fold. Merge the two official party-vote figures into one. Merge the page-foot caveat into the AI summary check line (R5).
- Inner pages have breadcrumbs (X7).
- Party tally legends name the chamber and base, with a mark at the majority needed (H3).
- Boards label each seat and any delegate, and retain the count base (H6, K1). Agreement cards appear once per chamber outside party tabs (K2).
- Empty linked-statement sections are hidden, with one message in the jump row (O6). President speeches stay hidden until a source exists (P2).
- Introduced-bill rows use the official title when no bill line exists, the introduced date, and a status badge (O7).
- Brief pages keep the text, citations, and records list, and drop weak figures (L2).
- Bill cards on votes use the labeled bill line, a status badge, and a bill link (V8).
- Record rows label the date by its event and share one bill-number format (R1).
- Header search appears on every page. Results show an official first and pin an exact bill number.
- Every number and date names its base and time frame. The freshness line names the newest roll call of each chamber, not only the release time.

### Standing rules

- Page text is at grade 12 or below. Exact terms stay and a popover explains them.
- Every public number names who and what it counts, and its time frame.
- AI text always carries its label.
- A fixed rule in code picks featured content. A person never picks it.
- Party colors stay open until the map party layer. Vote Yes and No colors and party colors are chosen together.
- Every UI pull request attaches screenshots at 360 px and 1280 px, light and dark.
- Components have no inline styles. One component owns each markup rule.

## Invariants

- AI text carries its public label. Enforcer: `PublishedRecordLabels` and the label column of the `public_api` views. Tests: `ReleaseRendererTests.ActionAndVotePagesLabelAnAiSummaryOnlyWhenAModelWroteTheText`, `PublicApiViewsTests.EveryAiOrCodeBillHasItsLabelAndAPersonBillHasNone`.
- Components have no inline styles. Enforcer: the component markup. Test: `PresentationArchitectureTests.MarkupCannotStyleItselfSoComponentsStayTheOnlyStyleOwners`.
- Header navigation uses one `PublicSections.Header` link set for desktop and the mobile menu. Both copies keep the configured root path and current section marker. The native menu is closed by default and works without JavaScript. Enforcer: `PublicPage` and responsive header CSS. Test: `PublicNavigationTests`.
- Every vote kind has a glossary term or none. Enforcer: `Glossary`. Test: `ReleaseRendererTests.EveryVoteKindHasItsTermKeyOrNoneForOtherAndEveryKeyIsInTheGlossary`.
- Page text and AI text read at grade 12 or below. Enforcer: `ReadingGrade` checks AI action text at draft time. No test covers fixed page text yet. Planned: one grade test over page and AI text, in R-68. Test: No test.
- Every public number names its base and time frame. Enforcer: review of each UI pull request. Test: No test.
- A fixed rule in code picks featured content. Enforcer: `HomeStore` selects the latest passage vote on a bill. Test: `HomeEndpointsTests.FeaturedVoteUsesLatestPassageOnBillTieOrderAndCarriesItsSummaryAndSourceLabelWithoutCountsFabricated`.
- Every UI pull request has screenshots at 360 px and 1280 px, light and dark. Enforcer: pull request review. Test: No test.
- Public pages read published data only, through `Publication.Contracts` and the `public_api` views. Enforcer: the project references and the view role. Test: `ArchitectureTests` and the `PublicApiViewsTests` role test.
- The SSR home HTML and its ETag use the same `HomeAnswer` release snapshot. Enforcer: `HomePageReader` passes `HomePageData.Release` to the release-number responder overload. Test: `ServerPageTests.TheHomeRouteUsesTheHomeSnapshotReleaseAndAnswers503WithoutCacheWhenItIsMissing`.
- Shared home HTML does not vary by cookie or ZIP query. Enforcer: the home component has no reader-specific input and the page cache is shared. Test: the same home route test.
- The recent-vote split caption describes exactly four displayed votes with stored split alignment. It does not include the featured vote or infer a missing alignment. Enforcer: HomePage. Tests: HomePageTests.TheBriefLeadsTheRecentVotesAndASplitCaptionNamesOnlyThoseFourVotes, HomePageTests.MissingOrDifferentAlignmentCannotBecomeAnAllSplitClaim, and HomePageTests.ThreeRecentVotesAndTheFeaturedVoteCannotMakeAFourVoteCaption.
- Home freshness uses the newest published roll call of each chamber, including procedural votes, and omits a chamber with no published roll calls. Enforcer: `HomeStore` aggregates `public_api.votes` in its highlights statement. Tests: `HomeEndpointsTests.FreshnessUsesNewestPublishedRollCallEvenWhenItIsNotSubstantiveAndOmitsAnEmptyChamber`, `HomeEndpointsTests.NoActiveReleaseHasNoHomeAnswerOrFreshnessDates`, and `HomePageTests.FreshnessOmitsAChamberWithoutPublishedRollCalls`.
- Home issue and executive rows use the mapped counts and record facts, with shared link, enum label, Congress ordinal, and date format owners. Enforcer: `HomePage`. Test: `HomePageTests.IssuesExecutiveGroupsAndFreshnessRenderTheirScopedPublishedFacts`.
- The ZIP lookup restores its controls after the current request, including a malformed success response. An older response cannot replace a newer result. Enforcer: `initZipLookup`. No automated test.
- The browser remembers only a validated five-digit ZIP after its current lookup answer renders. It stores no member details; the home page re-queries published current data. Enforcer: `initRememberedMembers` and `initZipLookup`. No automated test.
- Shared page HTML contains no remembered ZIP or personal lookup results. Enforcer: `PublicPage` emits empty hooks and browser code reads device storage. Tests: `HomePageTests.TheRememberedMembersLinkTargetsTheLookupHeadingInSharedMarkup` and `PublishedHomePageTests.TheResultPanelIsHiddenUntilALookupAndCreditsTheCensusBureauAndSaysAZctaIsNotAZipCode`.

- The glossary index lists each approved term and the party-count note once, in alphabetical order. Letter links reach focusable index headings. Enforcer: `PublishedGlossaryPage`. Test: `GlossaryTests.TheGlossaryIndexGroupsEveryTermAlphabeticallyAndLinksPopulatedLettersAndDefinitions`.

## Behavior

Implemented today: the static pages that [publication](publication.md#public-reader-experience) describes. PublicApi also serves `/web` as the first gated server-rendered route when `CIVIC_LENS_PUBLIC_BASE_URL` is configured. It uses one `HomeAnswer` snapshot and its release number for the page validator. A missing active release returns 503 without cache headers. The browser ZIP form appears only when `CIVIC_LENS_PUBLIC_API_URL` is configured; configure that origin before a future live ZIP cutover. The existing static home remains the live route. After a successful current ZIP lookup, the browser stores the five-digit ZIP on this device. The header then shows "Your members" and a Forget control. Returning to the home fragment re-queries the configured ZIP endpoint against current published data. Personal lookup results are added only in the browser. Other pages move to PublicApi one route at a time in R-68. Rows marked "planned" describe accepted design that is not implemented.

### Reader flow matrix

| # | Reader goal | Pages and components | Status |
|---|---|---|---|
| 1 | Find my members and what they did | Home ZIP form, member cards after a ZIP | Partial. Gated SSR home includes the existing ZIP flow when its browser API origin is configured. The answer scrolls into view and receives focus. Cards show portraits, seats, parties, term ends, scoped attendance, and party-line counts from private HTML fragments. Sponsored-bill and top-issue counts remain planned |
| 2 | See my member on an issue, from the member | Official page, issue chips, vote strip | Partial. Planned: strip marks link to the votes (DR2) |
| 3 | See my member on an issue, from the issue | Issue page, "your members" section | Planned: each issue vote shows my members' casts |
| 4 | Understand one vote | Vote page: AI summary, result card, "what a Yes meant", party bars, source fold | Partial. Planned: answer first, one fold (DR3) |
| 5 | Understand one bill | Bill page: status stepper, AI summary, "who backed it", votes on the bill | Partial. Planned: stepper and party split of cosponsors |
| 6 | Read one executive action | Executive action page, document links | Partial. Planned: body text and an AI "what it states" |
| 7 | See what the President did | President page with per-type sections | Planned: count row becomes jump links |
| 8 | Follow an issue | Issue page: figures, votes, bills, party line-up | Partial. Planned: one count line and party line-up |
| 9 | Compare parties | Party boards, per-kind list pages | Partial. Planned: boards link from the nav, list pages (DR6) |
| 10 | Check a source | "Check the source" fold with an official link first | Partial. Planned: plain labels, raw text one click further (DR3) |
| 11 | Search a name or a bill | Header search on every page, results page | Partial. Planned: official first, bill number pin, filters after results |
| 12 | See a search with no results | Results page empty state | Partial. Planned: message and next steps at the top |
| 13 | Learn a term | `Term` popover, glossary page | Implemented: first-use popovers and alphabetical index with letter shortcuts. Planned: approved definitions for remaining prototype terms |
| 14 | Read the method and the about page | Method page, about page | Implemented |
| 15 | Report a problem | Footer and record report links | Partial. Planned: the footer link names the record |
| 16 | Reach a useful 404 | Not found page | Implemented |
| 17 | Return and find my members again | "Your members" header chip with a Forget control | Implemented. A successful ZIP stays in browser local storage; the home fragment refreshes the answer from the current endpoint. |
| 18 | Compare my members | Compare page: attendance, party line, bills, issues, differing votes | Planned |

### Component inventory

Shared primitives have one owner each, in `src/CivicLens.PublicWeb/Components/Ui` and `Evidence`. They are `Button`, `Field`, `FieldSet`, `Chip`, `Icon`, `IconFrame`, `Panel`. They also include `PageHeader`, `Notice`, `EmptyState`, `InfoPopover`, `DataTable`. The list ends with `Pager`, `FilterTabs`, `TabSet`, `Timestamp`, `StatusBadge`, `CitationQuote`, `DocumentText`, `RedlineText`, `SourceLineLink`, `OfficialPortrait`, and the theme controls. Host owns the controls that only its review and collection pages use: `ActivityFeed`, `HealthList`, `Drawer`, `Toolbar`, `Stat`, `StatStrip`, `Choice`, `TechnicalDetails`, and `LayoutSwitch`.

| Component | Pages | Reader question | Data source | Status |
|---|---|---|---|---|
| Site header, split nav, header search | All | Where can I go? | Fixed links, search service | Partial. `PublicPage` shows site navigation at 1140 px and wider, and a native Menu sheet below that width. The same current links appear in both forms. Header search stays visible at every width. Per-kind list routes and split nav targets remain planned, R-68 |
| Freshness line | All | How current is this? | `active_release`, newest `votes.voted_on` per chamber | Partial. `/web` shows newest published roll call dates; other pages remain planned |
| ZIP form and notices | Home | Who represents me? | `zip_districts`, `officials` | Implemented: visible busy state, answer scroll and focus, and error recovery |
| Featured vote tile | Home | What did Congress just decide? | `votes` | Partial. Gated SSR preview shows the published summary, wording label, result, and Yea/Nay counts. Planned: map and party bars |
| Party tally bar | Home, vote, bill | How did the chamber split? | `votes` counts, `vote_members.party` | Planned: bars; the party column is available |
| Latest votes list | Home | What was voted on lately? | `votes` | Partial. Gated SSR preview shows up to two recent substantive votes per chamber, excluding the featured vote, with their published labels and counts |
| Issue tiles | Home | Where do I start? | `issue_votes`, `bill_issues` | `/web` shows each configured issue's substantive vote count, bill count, and Congress. Other issue layouts remain planned |
| Break board | Home, boards | Who breaks from their party? | `official_party_lines` | Official figure served. Board endpoint and UI planned |
| Latest executive actions | Home | What did the President sign? | `executive_actions` | `/web` shows one row per published document type with count, newest title and signed date. Other executive layouts remain planned |
| Member card | After a ZIP | Who is my member? | `officials`, `official_attendance`, `official_party_lines`, `official_bill_counts`, `official_issue_counts` | Portrait, seat, party, term end, scoped attendance, and scoped party-line counts implemented. Sponsored-bill and top-issue counts remain planned |
| Member votes list | After a ZIP, official | How did my members vote? | `votes`, `member_votes` | Partial. The API serves shared vote rows with recorded casts. ZIP display and issue filter chips remain planned |
| Compare members | Compare page | How do my members differ? | `official_*` views, `member_votes` | Planned |
| Profile header, key figures | Official | Who is this? | `officials`, `official_attendance`, `official_party_lines` | API data implemented. Profile UI integration remains planned. |
| Vote strip | Official | What is their voting pattern? | `member_votes.side` | Planned: marks only breaks and missed votes |
| Statement and vote pairs | Official, vote | Did words match votes? | `record_links` | Planned: page content; related-record data is served by the API |
| Vote header and AI summary | Vote | What was this vote? | `votes` headline, summary, label | Implemented. Planned: "what this vote decides" |
| Fixed explainers | Vote, executive action | What did Yes mean? What next? | Code tables in `Publication.Contracts` | Planned: #407 |
| Result card, party bars | Vote | Did it pass, and by how much? | `votes`, `vote_members.party` | Partial. Party data is available; redesigned bars remain planned |
| Breakers list | Vote | Who broke ranks? | `votes.alignment`, `vote_members.party` | Planned |
| Member list with finder | Vote | How did my member vote? | `vote_members`, `officials` | Implemented. Planned: folded table with filters |
| Vote map | Vote, home | Where did Yes and No come from? | `vote_members`, `release_official_terms` | Planned: R-68 map step |
| Bill status stepper | Bill | Where is the bill now? | `bill_actions`, `bills.public_law` | Planned: route and UI; bill actions and public law data are available |
| Bill summary and CRS fold | Bill | What would the bill do? | `bills.summary`, CRS text and bill-text metadata; see [Publication](publication.md#public-api-views) | The API serves the summary, exact cited CRS body and version, and bill-text metadata. The fold, key terms and server-rendered bill page remain planned |
| Who backed it | Bill | Is it bipartisan? | `bill_sponsorships`, `release_official_terms.party` | Partial. Planned: party split |
| Votes on this bill | Bill | How did Congress vote? | `votes.bill_record_id` | Planned: route and UI; vote-to-bill data is available |
| Executive action page | Executive action, President | What does this document do? | `executive_actions`, `executive_action_notices`, `executive_action_links` | Planned: route and UI; action and notice data are served, links remain pending |
| Statement page, quotations | Statement | What exactly was said? | `statements`, `statement_speakers`, `statement_quotations` | The API serves statement details with bounded source text, checked quotations, and citations. The reader page remains planned |
| Document change page | Document change | What changed on this page? | `document_changes` | Planned: views needed |
| Issue page | Issue | What is there on this issue? | `release_issues`, `issue_votes`, `issue_sponsors` | Partial. Issue counts and sponsor data are available through the API. Planned: page and party line-up |
| Party boards | Boards | Who breaks from their party most? | `official_party_lines`, `party_agreement` | Agreement data and API endpoint are implemented. Board endpoints and pages remain planned |
| List pages for votes, bills, executive actions, statements | Lists | Show me all of one kind | Per-kind views | Planned: R-68 list step |
| Record row, status badge | All lists | What is this record? | Per-kind views | Implemented. Planned: event-named date, one bill number format |
| Search results | Search | Find a person, bill, or vote | Search service | Partial. Planned: ranking and snippets |
| Weekly brief page | Brief | What happened this week? | `briefs`, `brief_paragraphs`, `brief_citations` | Partial. Brief paragraphs and citations are served through the API. Planned: route and page |
| Correction and withdrawal pages | Record | What changed on this record? | `record_corrections`, `withdrawals` | The withdrawal read API is implemented at `/v1/records/{kind}/{recordId}/withdrawal`. Record corrections and the reader page remain planned |

## Storage and contracts

The public read API and the gated SSR home read `public_api` views. Implemented data covers votes, bills, officials, attendance, party-line figures, ZIP districts, issues, related records, executive actions, and briefs. It includes bill status and action history, public-law numbers, vote-to-bill links, relationship reasons, executive action notices, and brief paragraphs and citations. The `public_api.active_release` view supplies release identity even when the active release has no records. Most public pages still read static releases. [Publication](publication.md#public-api-views) owns the view rules and column reference.

`public_api.bills` exposes the bill summary, its text-origin label, and the cited bill-text summary metadata (`why_it_matters`, version name and date, and characters read and total). The view can serve these fields; the bill-text model setting is off by default, and activation awaits sample approval. See [Publication](publication.md#public-api-views) for the view contract.

The home route reads `HomeAnswer` in one RepeatableRead snapshot and maps it into presentation records beside the PublicWeb component. One statement reads highlights, publication date, and newest published roll call dates across all public votes. One batch query counts configured issues in that publication day's Congress. These are page inputs, not durable publication contracts. `CIVIC_LENS_PUBLIC_API_URL` is an optional bare origin for the browser ZIP lookup. The page cache validator includes the normalized public site and browser API origins because both affect the HTML.

The views expose the stored member party and vote alignment, normalized member-vote side, party-majority cast, and official photo path. Member caucus comes from the matching chamber term in the active release on the vote date.

Remaining planned view columns and views, in the order that R-68 builds them:

- Party lines: the board endpoints. `party_agreement` and the official party-line view are implemented.
- Votes: a second AI text column for "what this vote decides".
- Bills: `bill_key_terms`. The cited CRS body and version are implemented.
- Issues: party line-up data.
- Executive actions: `executive_action_links` and the summary columns.
- Other kinds: `document_changes` with a changed-region child view and `record_corrections`; withdrawal notices now have a typed public read API.

Each AI text column carries its text origin and its label. The roadmap steps under [R-68](../roadmap.md#later) name each view.

## Code map

| Concern | Files |
|---|---|
| Shared components | `src/CivicLens.PublicWeb/Components/Ui`, `src/CivicLens.PublicWeb/Components/Evidence` |
| Public components and glossary | `src/CivicLens.PublicWeb/Publication` |
| Public page components, today | `src/CivicLens.Host/Publication` |
| Static render of pages | `src/CivicLens.Host/Publication/ReleaseRenderer.cs` |
| Public API and its views | `src/CivicLens.PublicApi` |
| Gated SSR home page and inputs | `src/CivicLens.PublicWeb/Publication/HomePage.razor`, `HomePageData.cs`, `src/CivicLens.PublicApi/Home/HomePageReader.cs` |
| Style files | `src/CivicLens.PublicWeb/wwwroot/css/theme.css`, `src/CivicLens.PublicWeb/wwwroot/css/app.css`, `src/CivicLens.PublicWeb/wwwroot/css/public.css` |
| Presentation rules test | `tests/CivicLens.Tests/Architecture/PresentationArchitectureTests.cs` |

`PublicApi` renders the gated `/web` home route and the not-found shell under `/web`. Other successful reader routes still use static pages. Shared presentation components use `CivicLens.PublicWeb`, which references `Publication.Contracts` only.

## Limits and planned work

The roadmap step [R-68](../roadmap.md#later) holds the plan: the views, the route moves, and the accepted design changes. This page holds no second plan.

Open choices:

- The party colors, chosen together with the vote Yes and No colors at the map party layer.
- The approved text for each glossary term that still shows example wording.
