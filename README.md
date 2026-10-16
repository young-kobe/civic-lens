# Civic Lens

Political transparency through officials' statements and actions, document changes, and evidence-backed weekly media briefs.

The approved product direction serves regular citizens seeking transparency in U.S. national politics. Public readers start with reviewed accounts and can inspect the supporting evidence; authorized reviewers assess the proposed public record beside its sources. State and local coverage are possible future extensions. The [editorial experience](docs/architecture/publication.md#public-reader-experience) defines the agreed flow. A restrained monochrome identity is selected for both surfaces.

**Current state:** a durable worker collects configured official sources and the Congress.gov bill list through a separate bounded collector. It stores immutable captures, extracts text, and compares versions in Postgres, with schema changes applied by checked-in EF migrations. Claude drafts document-change accounts and statement records inside daily token limits, and a person edits, approves, and publishes each draft in an authenticated review workspace. Approved records publish as immutable static releases. The worker also drafts one action record for each tracked Congress.gov bill, with every tracked official's sponsorship and cosponsorship. A person reviews, approves, and publishes each action record, and bulk approval covers revisions that change only sponsorships. The worker also drafts one weekly brief for each closed week, from the records that releases published in that week. A person reviews and approves each brief, and the owner publishes it with its cited records. Live Auth0 verification, production deployment, and MCP are pending. See [status](docs/architecture.md#status) for the full list. The prior implementation is preserved in [civic-lens-legacy](https://github.com/young-kobe/civic-lens-legacy).

Immutable document text extraction is also available: local commands verify imported captures, retain versioned HTML/plain-text extractions in Postgres, and return exact text-span citations. Configuration v2 supports reusable content-selection profiles whose exact revisions are retained with HTML extractions. Local document history now groups consecutive identical text within matching processing revisions while retaining every observation. Explicit comparisons persist bounded contextual diffs with exact text offsets. These are text-change records, not substantive-change judgments or human approvals. The review workspace shows saved versions, saved text, saved comparisons, and exact citations on read-only evidence pages.

The separate `review [port]` command provides an Auth0-protected review dashboard and draft editor with immutable revisions, exact evidence bindings, version-checked review decisions, and explicit concern resolution. It requires an HTTPS reverse proxy and configured identities; publishing uses the separate `releases` command.

The workspace uses Blazor static server-side rendering with a shared Razor component library. Only the owner Sources page is interactive. The release builder renders the same components to standalone static HTML from published release data. Light and dark themes follow the system setting, and a toggle overrides it per browser. Eligible comparisons can start a private review draft directly. Architecture tests enforce presentation ownership; the temporary mockups remain design references, not production pages.

[The federal pilot](docs/operations.md#federal-source-pilot) configures official Senate sources for Elizabeth Warren and Ted Cruz. Collection can be managed through CLI jobs or the owner-only Sources page. Captured pages and readable extractions are separate outcomes; a first observation establishes a baseline, not a document change.

Production hosting is agreed for the existing Hetzner CPX21, with Terraform-managed infrastructure and CI deployment independent of the developer workstation. This deployment remains planned; see [production hosting](docs/architecture/host.md#limits-and-planned-work).

## Start here

Install the .NET SDK selected by `global.json`, then run:

```sh
dotnet restore --locked-mode
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
dotnet run --project src/CivicLens.Host -- status
dotnet run --project src/CivicLens.Collector -- --help
```

No credentials, database, or paid services are required for the tracer. Tests never call the paid model API; one live test runs only on request. The full test suite requires a working Docker daemon and starts disposable Postgres containers; see operations for database-free checks. See [operations](docs/operations.md) for the local end-to-end example. Runtime data and generated releases will live under ignored `.runtime/` and `artifacts/` directories.

## Navigate the repository

| Path | Responsibility |
|---|---|
| `src/CivicLens.Core` | `Analysis/` owns immutable model runs; `Collection/` owns immutable collection attempt results and import decisions; `Registry/` owns immutable date ranges and dated names; `Documents/` owns immutable text extractions and citation spans |
| `src/CivicLens.Collection.Contracts` | Versioned collection types and shared receipt validation |
| `src/CivicLens.Publication.Contracts` | Public release boundary, currently empty |
| `src/CivicLens.Application` | `Analysis/` owns the drafting queue lifecycle, versioned prompt, and citation resolver; `Collection/` groups configuration and collection/import use cases; `Documents/` coordinates extraction and citations through external interfaces |
| `src/CivicLens.Infrastructure` | `Analysis/` holds the drafting queue store and the Claude API adapter; `Collection/` implements process execution, capture verification, and Postgres persistence; `Documents/` parses verified captures and persists text extractions |
| `src/CivicLens.Host` | CLI, worker, authenticated Blazor review workspace, and static release builder |
| `src/CivicLens.Collector` | `Http/` contains bounded collection and immutable capture production |
| `tests/CivicLens.Tests` | Tests mirror their owning layer and feature; `Architecture/` checks dependencies |
| `tools/render-architecture.py` | Shared diagram definitions and offline visual generator |

Projects define architectural layers; feature folders group related code within them. Small contract projects stay flat. Read `AGENTS.md` before implementation or delegation. The solution is the authoritative project inventory. Add source/configuration/fixture directories only when a feature needs them.

## Reference

- [Architecture and visual system map](docs/architecture.md)
- [Operations and verification](docs/operations.md)
- [Remaining implementation phases](docs/roadmap.md)
- [Contributor and agent rules](AGENTS.md)

The project uses AGPL-3.0; see [LICENSE](LICENSE). Documentation describes the current implementation and clearly marks planned behavior. Git holds history.
