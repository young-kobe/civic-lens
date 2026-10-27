# Host

## Purpose

The Host is the composition root, the CLI, and the authenticated web workspace. It owns the shared Razor component library that both private pages and static releases render. This page also records the agreed production hosting plan.

## Decisions

- The workspace uses Blazor static server-side rendering. Only the owner Sources page uses interactive Blazor Server, for live check status.
- Enhanced navigation is off, so every page does a full load.
- Razor components are the reusable presentation unit. Shared components take explicit display data and styling. They never read operational storage or decide editorial state.
- Private pages and public release pages render the same components, so evidence looks the same on both surfaces.
- Presentation choices must not move editorial rules into UI code.
- One restrained identity serves both surfaces through shared design tokens. It is monochrome: charcoal, white, and gray give structure. Semantic color marks evidence changes, status, and keyboard focus.
- Interface text uses self-hosted Instrument Sans, and reading text uses Newsreader. Both fonts use the OFL.
- `wwwroot/fonts/newsreader.woff2` is already a Latin subset (262 glyphs, Newsreader 16pt version 1.003, 132000 bytes, axes `wght` and `opsz`). A new subset used the Google Fonts Latin range and Latin Extended-A. fonttools 4.65.0 and brotli 1.2.1 made it, at 128888 bytes. That saves 2.4 percent, so the file stays as it is. Every character of the 43 dev releases is in the font. The size comes from the variable axes. The OFL file names no Reserved Font Name. The release pages preload the fonts; see [publication](publication.md#rendering).
- Four global stylesheets hold all styles: `theme.css`, `app.css`, `public.css`, and `review.css`. Markup has no inline styles.
- Production starts on the existing Hetzner CPX21 with containers, Terraform, and CI deployment. Production must run without the developer workstation.
- Auth0 is the identity provider.
- AI uses hosted inference first. Evaluation can change the model for each task.
- Off-server backups go to Cloudflare R2 through restic. R2 costs nothing under 10 GB and charges no download fee for restores. Backblaze B2 is the fallback if storage grows large.
- Restic encrypts each backup on our server before upload. The vendor stores only data that it cannot read. The key stays with the owner and never goes to a vendor.
- A pg_dump runs every 6 hours, so a failure loses at most 6 hours of database writes. Each run backs up the dump and the capture files together.
- Restic keeps 7 daily, 4 weekly, and 12 monthly snapshots. A restore test runs each month.
- The first deployment accepts single-host downtime during maintenance or failure.
- Build and validate MVP features and the UI on the dev box first. The dev box is for development only. Live infrastructure is not a prerequisite for feature work.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- Only configured subjects get roles, whatever role claims arrive. Enforced by the Host role mapping. Test: `ReviewAuthorizationTests.OnlyExplicitSubjectsReceiveRolesRegardlessOfSuppliedRoleClaims`.
- A post without a valid antiforgery token changes nothing. Enforced by ASP.NET Core antiforgery. Test: `ReviewWorkspaceTests.ForgedPostsCachedPagesAndForeignHostsAreRefusedAsync`.
- A reviewer cannot publish. Enforced by the owner check in the publish handler. Test: `ReviewWorkspaceTests.ReviewerCannotPublishEvenWithAForgedPostAsync`.
- Retained evidence renders as text, never as markup. Enforced by Razor encoding. Test: `ReviewWorkspaceTests.RetainedEvidenceIsShownAsTextNeverAsMarkupAsync`.
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

The shared component library lives in `src/CivicLens.Host/Components`:

| Folder | Components |
|---|---|
| `Layout` | `WorkspaceLayout`, the app shell with a side rail and top bar |
| `Ui` | General controls: `Button`, `Panel`, `PageHeader`, `StatStrip`, `DataTable`, `TableCell`, `Pager`, `FilterTabs`, `Notice`, `EmptyState`, `HealthList`, `ActivityFeed`, `Field`, `TabSet`, `Drawer`, `ThemeToggle`, `ContextLink` (a link whose hidden suffix names its target) |
| `Evidence` | `DocumentComparisonView` (redline and side-by-side), `RecordView`, `StatementRecord`, `CitationQuote`, `DocumentText` |

`StatementRecord` wraps `RecordView` for one statement revision. It shows the headline, the summary, and the statement date with its evidence quote. It also shows speakers by configured name, other speakers, issues, policy areas, bill numbers, and each quotation with its kind.

Pages compose these components. The comparison page, the draft editor, and published record pages render the same `DocumentComparisonView`.

`wwwroot/css/theme.css` owns color tokens, fonts, and both themes. `wwwroot/css/app.css` owns the styles that both the review UI and the published pages use. `wwwroot/css/public.css` owns the styles that only published pages use. `wwwroot/css/review.css` owns the styles that only the review UI uses. A published page loads `theme.css`, `app.css`, then `public.css`. The review UI loads `theme.css`, `app.css`, then `review.css`. A style that both sides use moves to `app.css`. A style with no user is deleted.

`PresentationArchitectureTests` enforces presentation ownership:

- Theme colors live only in `theme.css`, and every dark token has a light default.
- Markup has no style tags or style attributes.
- There are no `*.razor.css` or `*.cshtml` files.
- Only `App.razor` and `PublicPage.razor` own `html`, `link`, and `script` tags, and both shells load the same theme.
- Each shell loads only its own stylesheet and script, after `app.css` and `app.js`. `App.razor` loads `review.css` and `review.js`, and never `public.css` or `public.js`. The release lists `public.css` and `public.js`, and never `review.css` or `review.js`. Enforced by `App.razor`, `PublicPage.razor`, and `PublicAssets`. Test: `PresentationArchitectureTests.EachShellLoadsOnlyItsOwnStylesheetAndScriptAfterTheSharedOnes`.
- Three scripts hold the behavior, after `theme.js`. `app.js` holds what both surfaces use: the theme toggle, the comparison layout switch, the linked highlight, and the segmented thumb. `public.js` holds search and the reader report form. `review.js` holds tabs, notice dismissal, the dialog focus trap, table scroll shadows, the unsaved changes guard, and the start of Blazor. The scripts share no global. No test, because the repository has no JavaScript test harness.
- A selector lives in one of `app.css`, `public.css`, and `review.css`. A selector with a `.public-` class lives only in `public.css`. A rendered public page uses no class that only `review.css` defines; see [publication](publication.md#rendering).
- Only shared components render `del` and `ins`.

## Storage and contracts

- The Razor component build adds the SDK-managed `Microsoft.AspNetCore.App.Internal.Assets` entry to the Host package lock file.
- The release copies the shared stylesheets, scripts, and fonts; see [publication](publication.md#rendering).

## Code map

| Concern | Files |
|---|---|
| Entry point and commands | `src/CivicLens.Host/Program.cs` |
| Owner command rules and JSON output | `src/CivicLens.Host/OwnerCommand.cs`, `src/CivicLens.Host/CommandOutput.cs` |
| App shell and routes | `src/CivicLens.Host/Components/App.razor`, `src/CivicLens.Host/Components/Routes.razor`, `src/CivicLens.Host/Components/Layout/WorkspaceLayout.razor` |
| Shared evidence components | `src/CivicLens.Host/Components/Evidence/DocumentComparisonView.razor`, `src/CivicLens.Host/Components/Evidence/RecordView.razor`, `src/CivicLens.Host/Components/Evidence/StatementRecord.razor` |
| Styles and scripts | `src/CivicLens.Host/wwwroot/css/theme.css`, `src/CivicLens.Host/wwwroot/css/app.css`, `src/CivicLens.Host/wwwroot/css/public.css`, `src/CivicLens.Host/wwwroot/css/review.css`, `src/CivicLens.Host/wwwroot/js/theme.js`, `src/CivicLens.Host/wwwroot/js/app.js`, `src/CivicLens.Host/wwwroot/js/public.js`, `src/CivicLens.Host/wwwroot/js/review.js` |
| Workspace wiring | `src/CivicLens.Host/Review/ReviewWorkspace.cs` |
| Sources page | `src/CivicLens.Host/Collection/SourcesPage.razor` |
| Presentation tests | `tests/CivicLens.Tests/Architecture/PresentationArchitectureTests.cs` |

## Limits and planned work

Precise public layouts are not built yet. The temporary HTML prototype shows the direction with fictional content and simulated decisions. Its switch between public and private views is a demonstration aid, not a public deployment feature.

Production hosting is agreed but not implemented. The [roadmap](../roadmap.md) holds the order. The agreed design:

- Containers host the application, the collection and background jobs, and PostgreSQL with persistent storage.
- Terraform defines all infrastructure at every vendor, and CI automates deployment. Services can use more than one vendor, such as Hetzner, Cloudflare, or AWS, when cost or resilience favors it.
- Production collection, review, publication, and recovery run without the developer workstation. Local development stays supported.
- One CPX21 runs the operational application: Postgres, the worker, the review workspace, and publish. Cloudflare runs public serving and holds the backups. Terraform covers both vendors.
- The search service runs as its own container and database on the same box, with no access to operational storage. Public search and report requests reach it through the Cloudflare proxy.
- Split out more services only when a measurement shows that the box is too small. The search service is the first candidate.
- Both reviewers reach the shared workspace through their browsers. Live Auth0 tenant login and proxy verification happen in the infrastructure stage.
- Retained evidence and database backups need off-server storage and a tested restoration.
- The Cloudflare CDN serves approved releases from R2. It is separate from the operational application and storage, and reads only published files.
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
