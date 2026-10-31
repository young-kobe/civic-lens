# Civic Lens

Political transparency through officials' statements and actions, document changes, and evidence-backed weekly media briefs.

The approved product direction serves regular citizens seeking transparency in U.S. national politics. Public readers start with reviewed accounts and can inspect the supporting evidence; authorized reviewers assess the proposed public record beside its sources. State and local coverage are possible future extensions. The [editorial experience](docs/architecture/publication.md#public-reader-experience) defines the agreed flow. A restrained monochrome identity is selected for both surfaces.

**Current state:** A durable worker collects configured official sources, the Congress.gov bill list, the Congress.gov House vote list, and the Senate vote files. A separate bounded collector fetches them. The worker stores immutable captures, extracts text, and compares versions in Postgres. Checked-in EF migrations apply schema changes. The `worker health` command reports whether every worker loop has written a heartbeat in the last 10 minutes.

Each release holds the public pages: officials, official profiles, voting boards for each chamber and party, issues, weekly briefs, all records, about, method, reader reports, and photos. The owner can withdraw a published record from the CLI. The worker keeps a member roster with photos.

Claude drafts document-change accounts and statement records inside daily token limits. A person edits and approves each draft in an authenticated review workspace. Approved records publish as immutable static releases. A separate search service, `src/CivicLens.PublicApi`, reads each published release into its own Postgres database. It serves full-text search and a read API, and it takes reader reports; see [search](docs/architecture/search.md). The worker pulls those reports into a review queue, and a person resolves each one; see [review](docs/architecture/review.md#reader-reports).

The worker drafts one action record for each tracked Congress.gov bill. Each record lists every tracked official's sponsorship and cosponsorship. Code approves each action record after it checks the citations. A person can edit or withdraw a record.

The worker collects House and Senate roll-call votes and drafts one vote record for each roll call. Each record lists the vote of every tracked member. Code approves each vote record after it checks the citations. A person can edit or withdraw a record on the vote pages. When `CIVIC_LENS_PUBLISHING=1`, the worker publishes every approved record in a static release each hour, whoever approved it; see [publication](docs/architecture/publication.md#automatic-publication). The owner can also publish by hand.

The worker can also capture the public member roster once a day. It keeps offices and dated terms as tables that cite the capture. The `roster propose` command lists members to add to the configuration and people to review.

The worker drafts a relationship for each pair of an approved statement and an approved action or vote that share an official and a bill. A person reviews each relationship. An approved relationship publishes in a static release, and the statement, action, and vote pages list it.

The worker drafts one weekly brief for each closed week. It uses the records that releases published in that week. A person reviews and approves each brief. The worker publishes it after approval, when its citations are current in the active release.

Live Auth0 verification, production deployment, and MCP are pending. See [status](docs/architecture.md#status) for the full list. The prior implementation is preserved in [civic-lens-legacy](https://github.com/young-kobe/civic-lens-legacy).

Immutable document text extraction is also available: local commands verify imported captures, retain versioned HTML/plain-text extractions in Postgres, and return exact text-span citations. Configuration supports reusable content-selection profiles whose exact revisions are retained with HTML extractions. Local document history groups consecutive identical text within matching processing revisions while retaining every observation. Explicit comparisons persist bounded contextual diffs with exact text offsets. These are text-change records, not substantive-change judgments or human approvals. The review workspace shows saved versions, saved text, saved comparisons, and exact citations on read-only evidence pages.

The separate `review [port]` command provides an Auth0-protected review dashboard and draft editor with immutable revisions, exact evidence bindings, version-checked review decisions, and explicit concern resolution. It requires an HTTPS reverse proxy and configured identities; publishing uses the separate `releases` command.

The workspace uses Blazor static server-side rendering with a shared Razor component library. Only the owner Sources page is interactive. The release builder renders the same components to standalone static HTML from published release data. Light and dark themes follow the system setting, and a toggle overrides it per browser. Eligible comparisons can start a private review draft directly. Architecture tests enforce presentation ownership.

[The federal pilot](docs/operations.md#federal-source-pilot) configures official Senate sources for Elizabeth Warren and Ted Cruz. Collection can be managed through CLI jobs or the owner-only Sources page. Captured pages and readable extractions are separate outcomes; a first observation establishes a baseline, not a document change.

A Jetson runs temporary production with Docker Compose; see the [production runbook](docs/operations.md#production-runbook). The long-term cloud move, with Terraform, stays planned; see [production hosting](docs/architecture/host.md#limits-and-planned-work).

## Start here

Install the .NET SDK selected by `global.json`, then run:

```sh
dotnet restore --locked-mode
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
dotnet run --project src/CivicLens.Collector -- --help
```

No credentials, database, or paid services are required for the tracer. Tests never call the paid model API; one live test runs only on request. The full test suite requires a working Docker daemon and starts disposable Postgres containers; see operations for database-free checks. See [operations](docs/operations.md) for the local end-to-end example. Runtime data and generated releases will live under ignored `.runtime/` and `artifacts/` directories.

## Navigate the repository

| Path | Responsibility |
|---|---|
| `src/CivicLens.Core` | Domain rules and immutable types |
| `src/CivicLens.Collection.Contracts` | Versioned collection types and shared receipt validation |
| `src/CivicLens.Publication.Contracts` | The public release contract: manifest, record and brief types, schema version, and limits |
| `src/CivicLens.Application` | Use cases and external interfaces |
| `src/CivicLens.Infrastructure` | Postgres stores, process execution, parsers, and the Claude API adapter |
| `src/CivicLens.Host` | CLI, worker, authenticated Blazor review workspace, and static release builder |
| `src/CivicLens.PublicApi` | The public search service: release ingest, its own search index, the read API, and reader report intake. It references the publication contract only |
| `src/CivicLens.Collector` | `Http/` contains bounded collection and immutable capture production |
| `tests/CivicLens.Tests` | Tests mirror their owning layer and feature; `Architecture/` checks dependencies |
| `tools/render-architecture.py` | Shared diagram definitions and offline visual generator |

Projects define architectural layers; feature folders group related code within them. The feature folders in Core, Application, and Infrastructure mirror the lookup table in [docs/architecture.md](docs/architecture.md#lookup-table). Small contract projects stay flat. Read `AGENTS.md` before implementation or delegation. The solution is the authoritative project inventory. Add source/configuration/fixture directories only when a feature needs them.

## Reference

- [Architecture and visual system map](docs/architecture.md)
- [Operations and verification](docs/operations.md)
- [Remaining implementation phases](docs/roadmap.md)
- [Contributor and agent rules](AGENTS.md)

The code uses AGPL-3.0; see [LICENSE](LICENSE). Published record text and data files use CC BY 4.0. Documentation describes the current implementation and clearly marks planned behavior. Git holds history.
