# R-68 cutover plan: Caddy cutover, static retirement, prune, and migration squash

This report is a reading job. It changes no code. It plans the last R-68 steps in [docs/roadmap.md](../docs/roadmap.md#active-r-68-redesign). The facts come from `origin/main` at `288dc91`.

Owner decisions in force are not open here. Caddy rules change only in the one cutover PR. Retirement, prune, and squash are separate later steps.

## 1. Route map

### How the paths fit together today

Each server page already names its public path as its canonical path. For example, `OfficialsPage` uses `officials.html`, and the record pages use `records/{id}.html`. Server pages link other pages with `RootPath` plus the public path. `RootPath` is the absolute public base URL. So most links already point at the final public paths. Today those paths reach the static pages.

`PublicApi` serves every server page under the gated prefix `/web`. Only `api.civic-lens.info` proxies `/web`. The public host proxies only `/compare.html` to `/web/compare`. Every other public path is a file of the active static release.

### Map

"Gap" names a blocker of the cutover. "None" means the server route is ready.

| Public path today | Static writer | Server route today | Gap |
|---|---|---|---|
| `/`, `index.html` | `PublishedHomePage` | `/web` | None |
| `compare.html` | No static page | `/web/compare`, already live through Caddy | None |
| `officials.html` | `PublishedOfficialsPage` | `/web/officials` | None. A refused query still answers JSON problem (known limit) |
| `officials/{id}.html` | `PublishedOfficialPage` | `/web/officials/{id}` | Path shape differs: the route has no `.html` |
| `officials/{id}.html`, President | `PublishedOfficialPage` | `/web/officials/{id}`, President layout | Same path shape gap |
| `officials/{id}/record.html` | `PublishedOfficialRecordPage` | `/web/officials/{id}/record.html` | None. The "Against their party" filter is dropped (owner accepted) |
| `officials/{id}/{congress}.html` | `PublishedOfficialPage` per older Congress | No server page yet | Gap G1: old URL needs a redirect target |
| `officials/{id}/votes-{congress}.json` | `ReleaseOfficialVotes` | No server route | Data file. See section 3 |
| `issues.html` | `PublishedFacetsPage` | `/web/issues.html` | None. The server index lists issues only, no policy areas (known limit) |
| `issues/{id}.html` | `PublishedFacetPage`, page 1 | `/web/issues/{issueId}` | Path shape differs: the route has no `.html` |
| `issues/{id}.page-{n}.html` | `PublishedFacetPage`, later pages | No server page yet | Gap G2: the full issue list route is pending |
| `policy-areas/{id}.html`, `.page-{n}.html` | `PublishedFacetPage` | No server page yet | Gap G3: old URL needs a target |
| `board-{chamber}-{party}.html` (4 pages) | `PublishedBoardPage` | `/web/boards` shows 10 members a panel | Gap G4: no full board list. `BoardsPage` and `VoteDetailPage` link the static boards |
| `boards.html` (new) | None | `/web/boards` | New public path only |
| `all.html`, `all-{n}.html` | `PublishedAllRecordsPage` | No server page | Gap G5: old URL needs a target. `BriefsPage` links `all.html` |
| `votes`, `bills`, `statements`, `executive-actions` (new) | None | `/web/votes`, `/web/bills`, `/web/statements`, `/web/executive-actions` | Gap G6: no public path exists. Canonical path is `web/votes` |
| `search.html` and `search.html?kind=` | `PublishedSearchPage` with `public.js` | `/web/search` | None. It reads `q`, `kind`, and the filters |
| `glossary.html` | `PublishedGlossaryPage` (in PublicWeb) | No server route | Gap G7: every footer and `VoteKindsPage` link it |
| `vote-kinds.html` (new) | None | `/web/vote-kinds.html` | New public path only |
| `report.html` | `PublishedReportPage` (in PublicWeb) | No server route | Gap G8: every footer links it. The route needs the Turnstile site key and API URL in `PublicApi` |
| `about.html`, `method.html` | `PublishedAboutPage`, `PublishedMethodPage` (Host) | `/web/about.html`, `/web/method.html` | None |
| `briefs.html`, `briefs-{n}.html` | `PublishedBriefsPage` | `/web/briefs.html`, `/web/briefs-{n}.html` | None |
| `records/{id}.html`: vote, bill, statement, executive action, document change, brief | Host record pages | `/web/records/{id}.html` | None in code. Deploy gates: tallies verify and the correction history bootstrap |
| `records/{id}.html`: relationship | `PublishedRelationshipPage` | Route answers not found | Gap G9: no relationship page and no view |
| `records/{id}.html`: withdrawn record | `PublishedWithdrawalPage` | Same route, notice with status 200 | None |
| `records/{id}/map.html` (new) | None | `/web/records/{id}/map.html` | New public path only |
| `records/{id}.json` | `PublishRelease` | No server route | Gap G10: `/v1/search` returns `dataPath` to this file |
| `404.html` | `PublishedNotFoundPage` (in PublicWeb), written by `ReleaseSite` | Catch-all `/web/{**path}` | Gap G11: no catch-all outside `/web`. Caddy `handle_errors 404` reads the release file |
| `sitemap.xml` | `ReleaseSite` | No server route | Gap G12 |
| `robots.txt` | `ReleaseSite` | No server route | Gap G12 |
| `release.json`, `.gz`, `.br` | `PublishRelease` | No server route | No public reader in production. Remove at retirement |
| `officials.json`, `officials-summary.json`, `catalog.json`, `bill-status.json` | `ReleaseDataFiles`, `ReleaseOfficialSummaries`, `ReleaseBillStatuses` | No server route | Private readers only. See section 3 |
| `photos/{hash}.jpg` | `PublishRelease` photo step | No server route | Gap G13: server pages link photos on the public host. Something must keep serving them |
| Shared assets: CSS, JS, fonts, favicon, `assets/data/districts-119.json` | `PublicAssetFiles` | `SharedAssetEndpoints`, same paths | None |

### Gaps that block the cutover

- G1, G3, G5: old URLs with no server page. They need redirects or a 404 choice. See question Q3.
- G2: the issue page shows 10 votes and 5 bills only. Later static pages need a target.
- G4: the full board lists. Two server pages link the static boards. See question Q4.
- G6: the four list pages have no public path. See question Q2.
- G7, G8, G11: glossary, report, and 404 under the new shell. The roadmap lists them as remaining.
- G9: the relationship page. It needs a new `public_api` view. See question Q5.
- G10: the search contract field `dataPath`. See question Q6.
- G12: sitemap and robots. See question Q7.
- G13: photos. See question Q8.
- Path shape: two routes have no `.html` where the public path has one. This is part of question Q1.

A second gap class is in the route code itself. A Caddy rewrite keeps `Request.Path` as `/web/...`. These parts then send the reader to `/web/...` on the public host, which Caddy does not route:

- `ListPageEndpoints.FirstPage` and the official record cursor redirect use `context.Request.Path`.
- `VoteDetailPage.PagePath` is `RecordRoute`, an origin-relative `/web/records/{id}.html` form action.
- `HomePage.FeaturedMapHref` is the origin-relative `/web/records/{id}/map.html`.
- `PublicSections.PreviewHeader` links `/web/issues.html` and `/web/votes` and the other lists.

So a rewrite-only cutover is not enough. Question Q1 holds the choice.

## 2. Code that changes at the cutover

Each row names the rule today and what it becomes. Paths assume the recommended answer to Q1 and Q2.

| Code | Rule today | At the cutover |
|---|---|---|
| `PublicSections.Header` | Kind links go to `search.html?kind=`. Boards link goes to `StaticBoardsPath` | Kind links go to the public list paths. Boards link goes to `BoardsPath` |
| `PublicSections.PreviewHeader`, `PreviewLink`, `PreviewKind`, `PreviewIssuesPath` | Server pages get origin-relative `/web` links | Delete. One header for every page |
| `PublicSections.StaticBoardsPath` | Header and `OfficialsPage` link the House Democrat board | Delete when the full board question (Q4) lands. `OfficialsPage` links `BoardsPath` |
| `PublicSections.IsBoardsPage`, `IsListPage` | Trail special cases while the header links static pages | Delete. The general rule "no link to the page itself" covers both |
| `PublicSections.RecordSection` | Kind crumbs go to `search.html?kind=` | Kind crumbs go to the list pages |
| `PublicSections.RecordList.Href`, `PreviewFolder` | `/web/votes` and the other lists, origin-relative | Public list paths with `RootPath`, like other links |
| `PublicPage.OnPublicRoute` | Compare keeps the static header | Delete. Every server page is on a public route |
| `PublicPage` header choice | `Context is null` picks the static header | One header. The static branch goes at retirement |
| `ServerPageEndpoints.Prefix` and every route | All routes under `/web` | Routes at the public paths (Q1) |
| `ServerPageEndpoints.RecordRoute`, `VoteMapRoute` | Origin-relative `/web` paths | `RootPath` plus `PublicationProtocol.RecordPagePath` and `VoteMapPagePath` |
| `ServerPageEndpoints` catch-all | `/web/{**path}` answers the not found page | A root fallback answers it for page paths |
| `FixedPageEndpoints.Path` | `/web/{public path}` | The public path |
| Absolute public base URL (`RootPath`, `PublicBaseUrl`) | Server links are absolute so they leave the API host | Keep. Canonical links and photo links still need it. The origin-relative exceptions above go |
| `HomePage.FeaturedMapHref` comment and parameter | Map lives under `/web` "until the cutover" | The page builds the link from `RootPath`. The parameter can go |
| `OfficialsPage` meta link | `StaticBoardsPath` | `BoardsPath` |
| `BriefsPage` "All records" button | Links `all.html` | Follows Q3 |
| `VoteDetailPage` and `BoardsPage` board links | `BoardPagePath`, the static board | Follows Q4 |
| `BoardsPageReader` comment | "The static board page holds the full list" | Follows Q4 |
| `ServerPageContext` comment | "A gated preview page" | "A server page" |
| `deploy/caddy/Caddyfile` | Public host serves files and proxies `/compare.html` | Public host proxies pages to `PublicApi`. API host keeps `/v1` only (Q1) |
| Caddy `handle_errors 404` | Rewrites to the release `404.html` | Delete. `PublicApi` answers 404 itself |
| Caddy `@web` encode block on the API host | Encodes `/web` and the ZIP fragments | Encodes the page routes on the public host and the fragments on the API host |
| `deploy/scripts/check-live.sh` | Expects the release `404.html` | Expects the server not found page |

Docs that change in the same PR start with the `public-site.md` invariants. They are at lines 122, 141, and 211 (header), 212 (Caddy), 216 (boards trail), and 255 (map link). The reader flow matrix rows 2, 4, 7, 9, and 11 change too. Also `operations.md` "Web server", the roadmap R-68 items, and `README.md` "Current state".

Tests that change: `PublicNavigationTests.TheHeaderHasOneLinkForEachKindAndTheStaticPartyBoardUntilTheCutover...`, `PublicNavigationTests.APageOnAPublicRouteKeepsTheStaticIssuesLink`, `PublicNavigationTests.OnlyAServerPageShowsTheFreshnessLineAndTheIssuesLinkOfThePreview`, and `PublicNavigationTests.TheServerBoardsPageTrailEndsAtItsSectionWithNoLinkToTheStaticBoard`. Also `VotePageEndpointsTests.TheFilterFormAndTheClearLinkPointAtTheRouteThatServedThePage` and the map link test in `VoteMapPageEndpointsTests`. Each changed rule needs a mutation check.

## 3. Static retirement

The roadmap rule is: retire static page rendering and the rewrite options at once, so publication writes data only. There is no rollback period.

### What the publisher still renders

`PublishRelease.StageAsync` writes these page files through `IReleaseRenderer`:

- Record pages and withdrawal notices: `records/{id}.html`.
- `ReleaseIndexPages`: all records, briefs, issue and policy area pages, `issues.html`, `search.html`, `report.html`, `method.html`, `about.html`, `glossary.html`, and Home.
- `ReleaseOfficialPages`: official pages, older Congress pages, record pages, four boards, and `officials.html`.
- `ReleaseSite`: `404.html`, `sitemap.xml`, and `robots.txt`.
- `PublicAssetFiles`: the shared assets.

It also writes these data files:

- `release.json` with `.gz` and `.br`.
- `records/{id}.json`.
- `officials.json`, `officials-summary.json`, `catalog.json`, and `bill-status.json`.
- `officials/{id}/votes-{congress}.json`.
- `photos/{hash}.jpg`.

The rewrite options are `--rewrite-pages`, `--rewrite-officials`, and `--rewrite-entries` of `releases publish`.

### Who still reads each file

| File | Reader today | After retirement |
|---|---|---|
| Every `.html` page | Caddy, the public reader | Nobody. Delete the writers |
| Shared assets in the release | Caddy | Nobody. `SharedAssetEndpoints` serves the same paths |
| `release.json` (public) | No production reader. `ReleaseFeed` and `FeedReleaseSource` serve tests only | Stop serving it. Remove `ReleaseFeed`, `FeedReleaseSource`, and `CIVIC_LENS_RELEASE_BASE_URL` |
| `release.json` (private) | `releases entries backfill` and `verify`, the correction history bootstrap and verify, the bill count and line-up backfills, `PreviousRelease` | Keep while a backfill, verify, or publish reads it (R-63) |
| `records/{id}.json` | `PublishRelease` (corrections copy, brief load, related records), the backfills, and the `dataPath` of `/v1/search` | Keep the private files. Decide the public field (Q6) |
| `officials.json`, `officials-summary.json` | `PreviousRelease` for carry-forward, and the bill count backfill | Keep while the publisher reads them, then move to tables (R-63) |
| `catalog.json` | Never read back | Delete the writer |
| `bill-status.json` | Never read by a server page. The commit takes its rows from `ReleaseBillStatuses.RowsOf` | Delete the file writer. Keep `RowsOf` for the commit |
| `votes-{congress}.json` | `ReleaseOfficialVotes` links them from the previous release | Delete with the official page writer, unless "Get this data" needs them later |
| `photos/{hash}.jpg` | Caddy, linked by the server pages | Must stay served (Q8) |
| `sitemap.xml`, `robots.txt` | Search engines | Replace with server routes (Q7) |

### What can be deleted and what must stay

Delete at retirement:

- `IReleaseRenderer`, `ReleaseRenderer`, `PublicAssetFiles`, and every page call in `PublishRelease`.
- `ReleaseIndexPages`, the page parts of `ReleaseOfficialPages`, and the sitemap part of `ReleaseSite`.
- `ReleaseFacets` page paging and `ReleaseBillLines` if no data writer needs them.
- `--rewrite-pages`. Check `--rewrite-officials` against its data use first.
- `ReleaseRenderer.PublicApiUrlVariable` and `TurnstileSiteKeyVariable` in the Host, after the report route moves them to `PublicApi`.
- The Caddy `file_server` block, except the photo route.

Must stay:

- The release folder and the `current` link, while private readers use them.
- `release.json` and the record files as private snapshots, until R-63 moves the last reader.
- The photo files and a route that serves them.
- `ReleaseBillStatuses.RowsOf` and the commit of every typed table.

The invariant "A published release serves without the database" (`PublishedReleaseOutlivesDatabaseTests`) ends at retirement. AGENTS.md already names the new rule: an operational database outage stops live read routes while cached pages stay up. Update AGENTS.md "Public serving reads published data only" in the same PR.

## 4. Prune list

The prune comes after retirement. Each batch deletes code that lost its last caller in an earlier batch. Each batch keeps the build green.

### Batch P1: Host static pages (about 3,400 lines in 56 files)

- Every `Published*Page.razor` in `src/CivicLens.Host/Publication`.
- Their parts: `PublishedBillTextSummary`, `PublishedBreakCard`, `PublishedFacetBrowser`, `PublishedFacetTable`, `PublishedHomeIssues`, `PublishedHomeLatest`, `PublishedKeyFigures`, `PublishedOfficialName`, `PublishedOfficialSections`, `PublishedRecordList`, `PublishedRecordRows`, `PublishedRecordTable`, `PublishedRelatedPanel`, `PublishedVoteListFacts`, `PublishedVoteMember`, and `PublishedVoteStrip`.
- Their views: `Published*View.cs`, `PublishedKeyFigure`, `PublishedOfficialRow`, and `OfficialSection`.
- `ReleaseRenderer`, `PublicAssetFiles`, and the renderer wiring in `PublicationComposition` and `ReviewWorkspace`.
- Tests: `ReleaseRendererTests`, `PublishedBoardPageTests`, `PublishedOfficialPageTests`, `PublishedHomePageTests`, `PublishedExecutiveActionPageTests`, `PublishedRelatedRecordsTests`, `PublicFoundationTests` (static parts), and `PublishedReleaseOutlivesDatabaseTests`.

This batch can go in the retirement PR, because retirement removes the renderer calls.

### Batch P2: Application static writers (about 660 lines)

- `IReleaseRenderer`, `ReleaseIndexPages`, `ReleaseSite` (sitemap part), `ReleasePages`, the page parts of `ReleaseOfficialPages`, and `ReleaseOfficialVotes`.
- `ReleaseDataFiles` parts for `catalog.json`. The `bill-status.json` file part of `ReleaseBillStatuses`.
- Test fixtures: the renderer in `PublicationScenario`, and the page assertions in the `Publish*Tests` classes.

### Batch P3: PublicWeb copies and static-only shared code

- `PublishedRecordLabels` (used in 62 files): keep the rules that server pages call. Delete members whose only callers were in P1. The owners named "the static page and the server page both call it" keep one caller. Their comments change.
- `StatementPageView`, `DocumentChangeWords`, `ExecutiveActionWords`, `OfficialRecordText`, `BoardWords`, `PublishedBriefCitations`, and `PublishedWithdrawalPage`: drop the static branches and the "static page" comments.
- `PublishedFacetSummaryView` and `PublishedWording.IssueTagCaveat`: keep the server caller only.
- `BoardWords` "this page keeps" wording for a static board: delete if Q4 does not keep a full board page.
- About and Method copies: delete `PublishedAboutPage` and `PublishedMethodPage`. `AboutPage` and `MethodPage` stay the only owners.

### Batch P4: static CSS and script

- `public.js` static-only parts: the officials card filter, the static search page, and the table sort that calls `/v1/search` for `all.html` and the facet pages.
- `public.css` rules whose selectors no server component renders. Find them with a selector audit against the rendered server pages.
- Tests of the removed script parts.

### Batch P5: search release feed and configuration

- `ReleaseFeed`, `FeedReleaseSource`, `FakeReleaseSite`, and `PublicApiOptions.ReleaseBaseUrl`. Search tests then build the index from `TypedReleaseSource`.
- `CIVIC_LENS_RELEASE_BASE_URL` in `deploy/compose.yaml`, `search.md`, and `operations.md`.
- `RecordHit.DataPath`, if Q6 removes it.
- The Caddy `http://127.0.0.1` site name, which exists so the search service can read the release.

### Batch P6: roadmap prune items

- The `/v1/home` freshness fields and their SQL in `HomeStore`.
- `ReleaseIssuePartyCounts` drops its copy of the term party rule and reads `PublicApiStatementSpeakerTerms.TermPartyOn`. Do this before the squash, because the squash moves `TermPartyOn` (section 5).
- Stale limits in `search.md`. Example: "The ingest does not read them yet" is false, because `TypedReleaseSource` is the production source.

Each batch needs the Verify steps in the brief. Every batch must also show that each deleted member had no production caller left.

## 5. Migration squash

### Count

`src/CivicLens.Infrastructure/Collection/Migrations` holds 115 migrations, 115 designers, and the model snapshot. The folder is 51 MB. Each designer is about 6,000 lines. 81 migrations run hand SQL, and 59 create or replace `public_api` views. 49 test classes test single migration steps.

Open PRs add more. #540, #541, #543, and #544 carry the same migration `20261113202000_PublicApiExecutiveActionEvidence`. #542 adds `20261113210000_PublicApiBillTextQuotes` and edits the merged migration `ActionRevisionKeyTerms`. The squash waits until these merge or close.

The search database is separate. `PublicApi/Schema` holds five SQL files that `SchemaMigrator` applies. The squash does not need to touch them.

### Precedent

Commit `2654607b` closed R-23 before the first production deploy. It replaced 56 migrations with one `InitialSchema` that EF generated from the model. It then appended the final hand SQL: the notify functions and triggers, the composite foreign key, and the descending and partial indexes. It dropped one-time data fixes and stale defaults. It deleted 17 migration step test classes. It moved live schema checks to the feature tests. It added the dev database reset runbook.

That precedent had no production database. A dev reset was enough. Now production holds live data, so the same file approach needs a history step.

### Recommended approach

1. Wait until production runs the newest pre-squash migration. Every open PR with a migration must be merged or closed first.
2. Move every rule owner out of migration classes into one non-migration owner. Examples: `PublicApiStatementSpeakerTerms.TermPartyOn`, `ActionRevisionKeyTerms.ViewSql` (after #542), and the migration constants that later migrations read. A precedent for shared SQL owners is `EnumChecks` and `RecordIdChecks` in Infrastructure.
3. Generate one baseline migration from the model with the pinned EF tool. Append the final hand SQL in dependency order: functions, triggers, indexes, the `public_api` schema, the owner role, and every view in its final form.
4. Give `db migrate` one explicit history step, in C#, before `MigrateAsync`. When `__EFMigrationsHistory` holds the last pre-squash ID, it replaces all old rows with the baseline ID in one transaction. A database with no history runs the baseline. Any other history fails with a clear message. This follows "the C# application owns workflow logic".
5. Prove schema equality before merge. Build database A from the old chain on the pre-squash commit. Build database B from the baseline. Compare `pg_dump --schema-only` output, normalized for column order. Compare the generated `docs/architecture/schema/` reference and the view definitions too.
6. Delete the old migrations, designers, and the 49 migration step tests. Move any live schema check that still guards a rule to its feature tests, as `2654607b` did.
7. Remove the rollback notes in `operations.md` that name deleted migrations, such as `DropDecisionJson` and `JobAttemptColumns`. Keep the facts that still hold.

The history step needs no data change. Production keeps every table and row. Only the history rows change.

### Risks

- Column order. A fresh baseline orders columns by the model. Production keeps the order of its `ALTER TABLE` history. The `2654607b` diff shows this for `action_drafts`. SQL that names columns is safe. Any `SELECT *` or positional `INSERT` is not. The schema docs will differ unless the generator sorts columns.
- Lost data fixes. A migration that also moved or fixed data has no effect in the baseline. That is correct for a new database, but each one must be checked. A fix that a fresh database needs must move to the baseline.
- View drift. 59 migrations rebuilt views. The baseline must hold exactly the final text. A missed `security_barrier` or grant would widen public access. Step 5 must compare `pg_views` and `pg_class.reloptions`.
- Roles and grants. `PublicApiViews` creates `civic_lens_public_owner`. `db migrate` creates `civic_lens_public` and the grants. The baseline must keep that split. A restore into a new cluster still needs `ALTER ROLE` by hand.
- Rollback. After the history step, an older image cannot run its old migrations. Rollback past the squash needs a backup restore. Take a fresh backup before the deploy.
- Timing. A migration PR that merges after the squash but was branched before it breaks the history step. Freeze migration PRs for the squash window.
- Size. One generated designer holds the whole model. It is still about 6,000 lines, but one file replaces 115.

## 6. Proposed PR order

Sizes: S is under 300 changed lines, M is 300 to 1,500, and L is over 1,500, deletions included.

| # | PR | Depends on | Size | Owner decisions |
|---|---|---|---|---|
| 1 | Glossary, report, and 404 under the new shell, on gated `/web` routes. Report moves the Turnstile site key and API URL into `PublicApi` | None | M | Q9 |
| 2 | Full board lists as a server page or a filter of `boards.html` | None | M | Q4 |
| 3 | Full issue list route ("See all" votes and bills of an issue) | None | M | None new. Open questions for any new string |
| 4 | Relationship page and its `public_api` view | None | M to L | Q5 |
| 5 | Sitemap and robots server routes from the views | None | S to M | Q7 |
| 6 | Redirects for old URLs: older Congress pages, policy areas, all records, later facet pages | 2, 3 | S | Q3 |
| 7 | Search contract: `dataPath` change | None | S | Q6 |
| 8 | Prune item: `ReleaseIssuePartyCounts` reads `TermPartyOn` | None | S | None |
| 9 | Cutover: routes at public paths, one header, Caddy rewrite, docs, and tests | 1 to 7, the deploy gates (tallies verify, correction bootstrap, line-up verify) | M | Q1, Q2, Q8 |
| 10 | Retirement: publisher writes data only. Remove page writers and rewrite options. Batch P1 | 9 deployed and checked live | L | Q10 |
| 11 | Prune P2 and P3 | 10 | L | None |
| 12 | Prune P4 | 10 | M | None |
| 13 | Prune P5 and P6 | 10 | M | None |
| 14 | Migration squash | 11 to 13, every open migration PR closed, production on the newest migration | L | Q11 |

PRs 1 to 8 can run in parallel. They serve under `/web` only, so they change no Caddy rule. Only PR 9 touches Caddy.

## Open questions

### Q1. How do public paths reach the server pages?

Problem: a Caddy rewrite keeps `/web` in `Request.Path`. Redirects and form actions then send the reader to `/web` paths that the public host does not route.

- Option A: map each route at its public path in `PublicApi` and delete `/web`. Caddy proxies the public host to `PublicApi`, except `/photos/*`. The API host proxies `/v1/*` only. One owner of paths remains: `PublicationProtocol`. Cost: the cutover PR touches every route and its tests.
- Option B: keep `/web` and add a Caddy rewrite for each path. Also route `/web/*` on the public host, so redirects work. Cost: two path tables, and readers see `/web` URLs after a redirect.
- Option C: keep `/web` and fix each redirect and form action to use the public path. Caddy rewrites each path. Cost: a rewrite regex for every path shape, and a second path owner in the Caddyfile.

Recommendation: A. It removes the path copy and the origin-relative exceptions. Precedent: `FixedPageEndpoints` already maps each page by its public path under one prefix.

### Q2. What are the public paths of the four list pages?

Problem: the lists have no public path. Their canonical path is `web/votes`.

- Option A: `votes.html`, `bills.html`, `statements.html`, `executive-actions.html`. Matches the other pages, such as `officials.html` and `issues.html`.
- Option B: `votes`, `bills`, `statements`, `executive-actions` with no extension. Shorter, but a new path style on this site.

Recommendation: A, for one path convention.

### Q3. What do old URLs with no server page answer?

Paths: `officials/{id}/{congress}.html`, `policy-areas/{id}.html` and later pages, `all.html` and `all-{n}.html`, `issues/{id}.page-{n}.html`.

- Option A: a permanent redirect for each. Older Congress pages go to the official page. Policy areas go to `search.html?policyArea={id}`. All records goes to the votes list. Later issue pages go to the full issue list (PR 3).
- Option B: the not found page for each. Simple, but it breaks old links and search engine entries.

Recommendation: A. The roadmap says old URLs stay outside navigation, not that they break. `BriefsPage` must then link the votes list, or drop the "All records" button. That button change is a public wording question for the PR.

### Q4. Where do the full board lists live?

Problem: `/web/boards` shows 10 members a panel. `BoardsPage` and `VoteDetailPage` link the four static board pages.

- Option A: a server page for each board at `board-{chamber}-{party}.html`, from the same `BoardStore` read. Old links keep working.
- Option B: a "show all" state of `boards.html`, such as `boards.html?board=house-democrat`. One page, but old links need redirects.

Recommendation: A. It keeps the old URLs and reuses `BoardStore`. It also keeps `BoardWords` with one server caller.

### Q5. What does the relationship page show?

Problem: the relationship kind has no server page and no view. The roadmap says keep reason, quote, and fact together, and keep relationship URLs.

- Option A: a page on the one record route, from a new view `public_api.relationships`, with the reason, the quote, the fact, and both records.
- Option B: no page. Redirect each relationship URL to the statement page, which already lists its links.

Recommendation: A, because the roadmap asks to keep relationship URLs.

### Q6. What happens to `dataPath` in `/v1/search`?

Problem: each hit holds `dataPath`, the path of `records/{id}.json`. After retirement nothing serves that file.

- Option A: remove the field from the response. A public contract change.
- Option B: keep serving the record files on the public host until "Get this data" replaces them.

Recommendation: A, in PR 7 before the cutover. No server page reads the field. "Get this data" comes later with its own contract.

### Q7. How are the sitemap and robots built?

- Option A: server routes that read `public_api.active_records`, officials, and issues, with the release ETag rule. A sitemap index when the count passes 50,000.
- Option B: the publisher keeps writing them as data files, and Caddy serves them.

Recommendation: A. Option B keeps a page writer alive after retirement.

### Q8. Who serves the photos?

- Option A: the publisher keeps writing `photos/{hash}.jpg`. Caddy keeps one `file_server` route for `/photos/*` with the immutable cache value.
- Option B: `PublicApi` serves the photos from a store.

Recommendation: A. It is the existing mechanism and needs no new storage.

### Q9. Report page configuration

Problem: the report page needs the Turnstile site key and the API URL. Today publish reads `CIVIC_LENS_TURNSTILE_SITE_KEY` and `CIVIC_LENS_PUBLIC_API_URL`.

- Option A: `PublicApiOptions` reads the same two variables. The Host copy goes at retirement.
- Option B: new variable names for `PublicApi`.

Recommendation: A. It keeps one name for each setting.

### Q10. Do the rewrite options all go at retirement?

Problem: `--rewrite-officials` and `--rewrite-entries` also rewrite data files and entries, not only pages.

- Option A: remove `--rewrite-pages` only. Keep the others while a data file or table needs them.
- Option B: remove all three as the roadmap text says.

Recommendation: A, then remove each option when its last data reader leaves (R-63).

### Q11. How does production move to the squashed baseline?

- Option A: the C# history step in `db migrate` (section 5). It runs once and only when the history holds the exact last pre-squash ID.
- Option B: an owner runs a documented SQL statement on the history table before the deploy.
- Option C: keep the old migrations and add no baseline. The folder stays at 51 MB and grows.

Recommendation: A. It keeps workflow logic in C#, needs no hand step, and fails closed on any other history.
