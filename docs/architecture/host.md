# Host

## Purpose

The Host is the composition root, the CLI, and the authenticated web workspace. The shared Razor component library that both private pages and static releases render lives in `CivicLens.PublicWeb`. Host keeps the review pages and the public page components that need Application types. This page also records the agreed production hosting plan.

## Decisions

- The workspace uses Blazor static server-side rendering. Only the owner Sources page uses interactive Blazor Server, for live check status.
- Enhanced navigation is off, so every page does a full load.
- Razor components are the reusable presentation unit. Shared components take explicit display data and styling. They never read operational storage or decide editorial state.
- Private pages and public release pages render the same components, so evidence looks the same on both surfaces.
- Presentation choices must not move editorial rules into UI code.
- The review UI keeps a restrained monochrome identity: charcoal, white, and gray give structure. Semantic color marks evidence changes, status, and keyboard focus. Published pages set `class="public"` on `body`. The `body.public` block of `theme.css` then replaces the shared tokens with the public palette. The palette has navy `#14213D`, cobalt `#2747D9`, marigold `#F5B301`, mist `#EEF1F8`, slate text `#4A5570`, and line `#D8DEEA`. Each token has one `light-dark()` value, so both themes come from one declaration. A browser without `light-dark()` gets a fallback copy of the light values. A second block sets the light scheme and the six tone tokens of insert, delete, and warning to their light values. The test checks that both copies equal the light side of the `light-dark()` values and the light tone tokens. Dark mode uses `data-theme` on `html` or the system setting. Invariant: every text and background pair of the public tokens reaches 4.5 to 1 in both themes. Enforced by `theme.css`. Test: `PublicFoundationTests.ThePublicTokensReachFourAndAHalfToOneInBothThemesOnTheSurfacesThatShowThem`. The same test checks two marks that carry no text. The against-party mark `--against` reaches 3 to 1 on the ground, the surface, and the raised surface. The header band edge `--bar-edge` reaches 3 to 1 on the ground. The dark header band is `#060b17`, darker than the dark ground, and its edge sets it apart.
- Review interface text uses self-hosted Instrument Sans. Published pages use self-hosted Schibsted Grotesk for interface text and Newsreader for reading text. All three fonts use the OFL, and each license file sits beside its font. The files of the published fonts sit in `src/CivicLens.PublicWeb/wwwroot/fonts/`, and the files of Instrument Sans sit in `src/CivicLens.Host/wwwroot/fonts/`. `schibsted-grotesk.woff2` is the Latin variable file (weights 400 to 800) of `@fontsource-variable/schibsted-grotesk` 5.2.8. The release does not ship Instrument Sans.
- `src/CivicLens.PublicWeb/wwwroot/fonts/newsreader.woff2` is a Latin subset of Newsreader 16pt version 1.003 with axes `wght` and `opsz`. It is 132000 bytes. The OFL file names no Reserved Font Name. The release pages preload the fonts; see [publication](publication.md#rendering).
- The district shapes of the vote map are a static asset in the PublicWeb wwwroot, `data/districts-119.json`. `tools/render-district-geometry.py` makes it from the Census cartographic boundary file `cb_2024_us_cd119_20m.zip`. The file covers the 119th Congress (2024 release, 1:20,000,000 scale). The tool pins its KML member `cb_2024_us_cd119_20m.kml`, which has 1551207 bytes and sha256 `c24be3bdb6afd3b82e853fde440046b20b6c8b9988fde5f371f4657148a9c471`. It fails when the size or the hash differ. The Census Bureau publishes it at `https://www2.census.gov/geo/tiger/GENZ2024/kml/cb_2024_us_cd119_20m.zip`. The data is public domain under 17 U.S.C. 105. The tool projects the lower 48 with an Albers equal-area conic projection. It moves Alaska and Hawaii into scaled insets. It keeps about 25 percent of the points and rounds to whole units at 800 px wide. It builds each state outline from the district shapes, so borders match. The tool lists DC, PR, GU, VI, AS, and MP as delegate areas with no shape. See [operations](../operations.md#verification) for the command. The PublicWeb assembly embeds the asset, and `VoteMapGeometry` is its one reader.
- `OfficialPortrait` is the one component that shows the portrait of an official. It takes the profile and the release root path. It shows a figure with the image at its size, the alt text "Official portrait of {name}", and the credit. With no photo it shows an inline placeholder of the same frame. It adds the line "No portrait is shown for this official." A withheld photo gets the same line. The `Compact` option is for the cards of the officials list. It makes the figure small and loads the image lazily. It drops the placeholder line. The `ShowCredit` option hides the credit line of a photo. The frame is in `public.css`, with theme tokens only and no filter in dark mode. The current Congress page and the officials list use it.
- Four global stylesheets hold all styles: `theme.css`, `app.css`, `public.css`, and `review.css`. Markup has no inline styles.
- Temporary production runs on a Jetson with Docker Compose. The long-term cloud move uses containers, Terraform, and CI deployment. Production must run without the developer workstation.
- Auth0 is the identity provider.
- AI uses hosted inference first. Evaluation can change the model for each task.
- Off-server backups go to Cloudflare R2 through restic. R2 costs nothing under 10 GB and charges no download fee for restores. Backblaze B2 is the fallback if storage grows large.
- Restic encrypts each backup on our server before upload. The vendor stores only data that it cannot read. The key stays with the owner and never goes to a vendor.
- A nightly timer runs `pg_dump` inside the Postgres container. A failure loses at most one day of database writes. Each run backs up the dump, the config files, the capture files, and the releases together. It does not back up the search database, env files, or the review key directory.
- Restic keeps 7 daily, 4 weekly, and 6 monthly snapshots. A restore test runs each month.
- The first deployment accepts single-host downtime during maintenance or failure.
- Build and validate MVP features and the UI on the dev box first. The dev box is for development only. Live infrastructure is not a prerequisite for feature work.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- Only configured subjects get roles, whatever role claims arrive. Enforced by the Host role mapping. Test: `ReviewAuthorizationTests.OnlyExplicitSubjectsReceiveRolesRegardlessOfSuppliedRoleClaims`.
- A post without a valid antiforgery token changes nothing. Enforced by ASP.NET Core antiforgery. Test: `ReviewWorkspaceTests.ForgedPostsCachedPagesAndForeignHostsAreRefusedAsync`.
- A reviewer cannot publish. Enforced by `ReviewAuthorization.RequireOwner` in `PublishRelease`. Test: `PublishReleaseTests.ReviewerCannotPublishBecauseOnlyTheOwnerSpeaksForTheSite`.
- Retained evidence renders as text, never as markup. Enforced by Razor encoding. Test: `ReviewWorkspaceTests.RetainedEvidenceIsShownAsTextNeverAsMarkupAsync`.
- Only `worker health` defines a healthy worker. The host alert script uses its exit status and has no second rule. Enforced by `deploy/scripts/check-health.sh`. No test.
- `worker health` exits 1 for a dead worker, and 3 (`WorkerHealthCommand.ReleaseFullExitCode`) when the worker is healthy and the release is full. A dead worker wins, so exit 1 also covers a full release. Enforced by `WorkerHealthCommand`. Tests: `WorkerHealthReleaseCapacityTests.ADeadWorkerWithAFullReleaseExitsOneAndStillPrintsTheCapLineBecauseTheDeadWorkerAlarmMustNotBeHidden` and `WorkerHealthReleaseCapacityTests.AHealthyWorkerWithAFullReleaseExitsWithTheReleaseFullCodeBecauseTheCapNeedsAnOwnerAndNotARestart`.
- The alert script keeps one marker for each problem: `disk`, `worker`, and `release-full`. One alarm does not silence another, and a clear check removes only its own marker. Enforced by `deploy/scripts/check-health.sh`. No test.
- One rule turns off every CSS transition and animation when the reader asks for reduced motion. Enforced by the `prefers-reduced-motion` block of `app.css`, which both shells load. No other stylesheet holds a motion guard. Test: `PresentationArchitectureTests.OneRuleInTheSharedStylesheetTurnsOffEveryTransitionAndAnimationForReducedMotion`.
- Markup has no inline styles. Enforced by `PresentationArchitectureTests`. Test: `PresentationArchitectureTests.MarkupCannotStyleItselfSoComponentsStayTheOnlyStyleOwners`.

## Behavior

### Web host

Host uses the ASP.NET Core Web SDK and its shared framework. `review [port]` serves the workspace on IPv4 loopback, behind an HTTPS reverse proxy. It is not deployed yet. The default port is 5081.

- Startup ignores ambient web endpoint configuration and serves only its own static assets.
- A request whose Host header does not match the configured origin gets 400.
- Responses send a strict content security policy that includes `font-src 'self'` and `base-uri 'self'`.
- Sign-out is `POST /Review/Logout` with an antiforgery token. It clears the application session, not the Auth0 identity-provider session.
- The workspace pins its effective scheme to HTTPS and does not trust forwarded headers.
- The listener expects a same-host proxy. Isolated container networking needs an explicitly reviewed deployment configuration.
- Previously issued cookies are checked again against the server subject configuration, so an access change also applies to them.
- Cookies are secure, HttpOnly, and non-sliding, and they expire after eight hours.
- Data-protection keys persist across restarts in `CIVIC_LENS_REVIEW_KEY_DIRECTORY`. On Unix the application restricts that directory to its owner. The application does not encrypt the keys at rest.
- Offline tests check subject authorization and HTTP workflows with isolated test signing keys. They do not exercise a live Auth0 tenant.

Pages call Application handlers for editorial and collection actions. Evidence pages are described in [documents](documents.md#evidence-pages).

### Logging

- Workers and review pages log each caught failure through `ILogger<T>` with the full exception.
- Among the batch CLI commands, only `worker` writes logs. It writes them to stderr, so JSON on stdout stays clean.
- The review workspace builds its own console provider. It writes warnings and errors to stderr.
- CLI results and usage errors stay on the console.

### Presentation

Light and dark themes follow the system setting. A toggle on both surfaces stores an override for each browser. The review workspace uses a denser layout where the task needs it.

The shared component library lives in two projects. `src/CivicLens.PublicWeb/Components` holds the components that published pages use. `src/CivicLens.Host/Components` holds review and collection controls and components that need Host types.

Host holds these components:

| Folder | Components |
|---|---|
| `Layout` | `WorkspaceLayout`, the app shell with a side rail and top bar |
| `Ui` | `IssueIcon`, `ActivityFeed`, `HealthList`, `Drawer`, `Toolbar`, `Stat`, `StatStrip`, `Choice`, `TechnicalDetails`, and `LayoutSwitch`; `FeedItem` and `HealthItem` hold their review data |
| `Evidence` | `DocumentComparisonView` (redline and side-by-side), `Redline`, `RedlineExcerpt` |

PublicWeb holds these components:

| Folder | Components |
|---|---|
| `Ui` | General controls: `Button`, `Panel`, `PageHeader`, `DataTable`, `TableCell`, `Pager`, `FilterTabs` (all links from `FilterTab.Link`, or all buttons from `FilterTab.Button` that a script reads; a mixed list throws `ArgumentException`, tested by `FilterTabsTests`), `Notice`, `EmptyState`, `Field`, `TabSet`, `ThemeToggle`, `IconFrame` (owns the SVG tag), `Icon` (the closed `IconName` list), `PartyBar` (one bar of labeled party segments with patterns and a legend; `PartyTones` owns the red and blue party colors and the party order), `InfoPopover` (a button with an accessible name that opens a short note; `Term` uses the same popover), `ContextLink` (a link whose hidden suffix names its target), `CellTitle` (a table title that is a link when it has an address, else plain text) |
| `Evidence` | `RecordView`, `StatementRecord`, `CitationQuote`, `DateEvidenceLine`, `SourceLineLink`, `SourceLinesNote`, `DocumentText` |

`Tone` and `ToneClass.Suffix` remain in PublicWeb. Public and Host controls use the same CSS suffix rule.

`StatementRecord` wraps `RecordView` for one statement revision. It shows the headline, the summary, and the statement date with its evidence quote. It also shows speakers by configured name, other speakers, issues, policy areas, bill numbers, and each quotation with its kind.

Pages compose these components. The comparison page, the draft editor, and published record pages render the same `DocumentComparisonView`.

`CivicLens.PublicWeb` owns every static file that published pages use, including the files that the review UI also uses. They sit in `src/CivicLens.PublicWeb/wwwroot/`. Host serves them from the library as static web assets at the root, so the review URLs do not change. `StaticWebAssetBasePath` in the PublicWeb project file sets the root path. The static release build reads the same files as embedded resources of PublicWeb. Enforced by the two project files. Test: `PresentationArchitectureTests.PublicWebOwnsEverySharedStaticFileAndHostKeepsOnlyReviewFiles`. Files that only the review UI uses stay in `src/CivicLens.Host/wwwroot/`. The `verify` job in `.github/workflows/ci.yml` publishes Host and fails unless `out/wwwroot` holds `css/app.css`, `js/app.js`, `favicon.svg`, and `fonts/newsreader.woff2`. Removing `StaticWebAssetBasePath` makes that step fail. No test checks the served URLs. `theme.css` owns color tokens, fonts, and both themes. `app.css` owns the styles that both the review UI and the published pages use. `public.css` owns the styles that only published pages use. `review.css` owns the styles that only the review UI uses. A published page loads `theme.css`, `app.css`, then `public.css`. The review UI loads `theme.css`, `app.css`, then `review.css`. A style that both sides use moves to `app.css`. A style with no user is deleted.

`PresentationArchitectureTests` enforces presentation ownership:

- Theme colors live only in `theme.css`, and every dark token has a light default.
- Markup has no style tags or style attributes.
- There are no `*.razor.css` or `*.cshtml` files.
- Only `App.razor` and `PublicPage.razor` own `html`, `link`, and `script` tags, and both shells load the same theme.
- Each shell loads only its own stylesheet and script, after `app.css` and `app.js`. `App.razor` loads `review.css` and `review.js`, and never `public.css` or `public.js`. It loads `blazor.web.js` before `review.js`, because `review.js` starts Blazor. The release lists `public.css` and `public.js`, and never `review.css` or `review.js`. Enforced by `App.razor`, `PublicPage.razor`, and `PublicAssets`. Test: `PresentationArchitectureTests.EachShellLoadsOnlyItsOwnStylesheetAndScriptAfterTheSharedOnes`.
- Three scripts hold the behavior, after `theme.js`. `app.js` holds what both surfaces use: the theme toggle, the comparison layout switch, the linked highlight, and the segmented thumb. `public.js` holds search, the officials list filter, the Records list filter, table sorting, table paging, and the reader report form. Search keeps its query and filters in the page address; see [search](search.md#search-page). `review.js` holds tabs, notice dismissal, the dialog focus trap, table scroll shadows, the unsaved changes guard, and the start of Blazor. The scripts share no global. A tab switch measures only the tabs, so a segmented switch inside a hidden tab pane keeps its plain fallback style. No pane holds one today. No test, because the repository has no JavaScript test harness.
- A selector lives in one of `app.css`, `public.css`, and `review.css`. A selector with a `.public-` class lives only in `public.css`. A rendered public page uses no class that only `review.css` defines; see [publication](publication.md#rendering).
- Only shared components render `del` and `ins`.
- One shared vote row serves every public page that lists votes. `SharedVoteRows` in `PublicWeb` has a list mode, which Home uses, and a single-member mode, which the official page uses and the bill page will reuse. No page adds its own vote row markup or CSS. The cast badge has one owner, `SharedVoteCast`, which `SharedVoteRows`, the linked vote of a statement, and the member table of the vote page use. Enforcers: `SharedVoteRows` and `SharedVoteCast`. Tests: `SharedVoteRowsTests`, `VoteDetailPageTests.TheMemberTableShowsEachCastThroughTheSharedCastBadgeWithItsSide`, and `OfficialProfilePageTests.AVoteRowKeepsItsCongressSessionKindWordsAndOnlyTheCastOfTheOfficialWithoutAName`.
- The lede rules of an office holder have one owner, `OfficeLede` in `PublicWeb`. `PublishedOfficialView` calls it for the static official page. Enforcer: `OfficeLede`. Test: `PublishedOfficialPageTests.TheLedeOfTheStaticAndTheServerPageComeFromOneOwner`.

### Health alerts

A systemd timer runs `deploy/scripts/check-health.sh` every 10 minutes on the host. The script runs `worker health` and treats exit 1 and any code except 0 and 3 as an unhealthy worker. It has no second definition of healthy. Exit 3 means a healthy worker with a full release. It sends its own "release is full" email. It also checks that free space under the data root is at least 10 GB. For each problem, it sends one email through `deploy/scripts/send-alert.sh`, which the unit failure alert also uses. A marker file for each problem in `/run/civic-lens` stops repeat email until the problem clears or the host reboots. See [health alerts](../operations.md#health-alerts).

## Storage and contracts

- The Razor component build adds the SDK-managed `Microsoft.AspNetCore.App.Internal.Assets` entry to the Host package lock file.
- The release copies the shared and public stylesheets and scripts, and the fonts; see [publication](publication.md#rendering).

## Code map

| Concern | Files |
|---|---|
| Entry point and commands | `src/CivicLens.Host/Program.cs` |
| Owner command rules and JSON output | `src/CivicLens.Host/OwnerCommand.cs`, `src/CivicLens.Host/CommandOutput.cs` |
| App shell and routes | `src/CivicLens.Host/Components/App.razor`, `src/CivicLens.Host/Components/Routes.razor`, `src/CivicLens.Host/Components/Layout/WorkspaceLayout.razor` |
| Public portrait | `src/CivicLens.PublicWeb/Publication/OfficialPortrait.razor` |
| Shared evidence components | `src/CivicLens.Host/Components/Evidence/DocumentComparisonView.razor`, `src/CivicLens.PublicWeb/Components/Evidence/RecordView.razor`, `src/CivicLens.PublicWeb/Components/Evidence/StatementRecord.razor` |
| Styles and scripts | `src/CivicLens.PublicWeb/wwwroot/css/theme.css`, `src/CivicLens.PublicWeb/wwwroot/css/app.css`, `src/CivicLens.PublicWeb/wwwroot/css/public.css`, `src/CivicLens.Host/wwwroot/css/review.css`, `src/CivicLens.PublicWeb/wwwroot/js/theme.js`, `src/CivicLens.PublicWeb/wwwroot/js/app.js`, `src/CivicLens.PublicWeb/wwwroot/js/public.js`, `src/CivicLens.Host/wwwroot/js/review.js` |
| Workspace wiring | `src/CivicLens.Host/Review/ReviewWorkspace.cs` |
| Sources page | `src/CivicLens.Host/Collection/SourcesPage.razor` |
| Health and failure alerts | `deploy/scripts/check-health.sh`, `deploy/scripts/send-alert.sh`, `deploy/scripts/alert.sh`, `deploy/systemd/civic-lens-health.service`, `deploy/systemd/civic-lens-health.timer` |
| Presentation tests | `tests/CivicLens.Tests/Architecture/PresentationArchitectureTests.cs` |

## Limits and planned work

The Jetson runs temporary production; see the [production runbook](../operations.md#production-runbook). The long-term cloud move is agreed but not implemented. The [roadmap](../roadmap.md) holds the order. The agreed design:

- Containers host the application, the collection and background jobs, and PostgreSQL with persistent storage.
- For the cloud move, Terraform defines all infrastructure at every vendor, and CI automates deployment. Terraform state never goes to R2. Services can use more than one vendor, such as Hetzner, Cloudflare, or AWS, when cost or resilience favors it.
- Production collection, review, publication, and recovery run without the developer workstation. Local development stays supported.
- In the cloud move, one CPX21 runs the operational application: Postgres, the worker, the review workspace, and publish. Cloudflare runs public serving and holds the backups. Terraform covers both vendors.
- The search service runs as its own container and database on the same box, with no access to operational storage. Public search and report requests reach it through the Cloudflare proxy.
- Split out more services only when a measurement shows that the box is too small. The search service is the first candidate.
- Both reviewers reach the shared workspace through their browsers. Live Auth0 tenant login and proxy verification happen in the infrastructure stage.
- Retained evidence and database backups need off-server storage and a tested restoration.
- Caddy serves approved releases through the Cloudflare tunnel. It is separate from the operational application and storage, and reads only published files.
- A release is a self-contained, immutable directory. Switching the active release is atomic, and earlier releases stay for rollback.
- The public release must stay usable when the operational application, worker, or database is down while the host is up.
- Production needs durable recovery, backup monitoring, bounded worker concurrency, capacity checks, and a restoration exercise.
- Terraform rebuilds infrastructure. Restoring persisted data needs backups.
- Application boundaries isolate storage, execution, and model adapters from domain rules.
- No production availability or recovery guarantee has been demonstrated yet.
- A dynamic SVG view, planned after the first deploy (R-21), shows collection and AI processing from the retained process state. It shows waiting, failure, recovery, and budget-limit states, and supports reduced motion. AI stages stay marked unavailable until they exist.

Before the shared workspace opens, verify:

- [ ] access controls and live Auth0 login
- [x] controlled migrations
- [ ] worker restart and recovery
- [ ] off-server backups to R2 and a restoration
- [ ] operation with the developer workstation off
