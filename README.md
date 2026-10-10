# Civic Lens

Political transparency through officials' statements and actions, document changes, and evidence-backed weekly media briefs.

The approved product direction serves regular citizens seeking transparency in U.S. national politics. Public readers start with reviewed accounts and can inspect the supporting evidence; authorized reviewers assess the proposed public record beside its sources. State and local coverage are possible future extensions. The [editorial experience](docs/architecture.md#approved-editorial-experience-target) defines the agreed flow. A restrained monochrome identity is selected for both surfaces.

**Current state:** a watched-page tracer with validated v1/v2 people/source configuration, dated coverage and names, a separate bounded HTTP collector, immutable gzip captures, and host-verified JSON receipts that preserve response metadata. Core domain rules model immutable collection attempt results, import decisions for new and duplicate attempts, and conservative 304 evidence linkage. Application maps verified attempts through the Core policy, and Infrastructure implements atomic Postgres imports with EF Core/Npgsql and an initial migration. Database integration tests use disposable Postgres containers. The CLI supports explicit database migrations, collection/import with application-generated attempt IDs, and filesystem receipt handoff with replay after interrupted imports. Recovery revalidates captures and can use relocated runtime storage without collecting again. Managed Postgres jobs provide bounded retries and aggregate budgets, fenced ownership, and shared origin pacing/backoff. A separately launched durable pipeline advances collection, preparation, extraction, and comparison, waking on committed work and recovery deadlines. Configured checks automatically admit a bounded article batch, extract readable text, and queue compatible text changes for review, with durable recovery. Enabled sources are checked hourly by default, with configurable per-source cadence and one catch-up check after downtime. The authenticated review workspace lets the owner start and stop source checks, check a tracked article URL again, and start a draft from a found change. Version 6 collection adds bounded robots redirects/decoding, Allow/Disallow precedence, and durable crawl-delay pacing; version 7 adds tolerant HTML recovery while retaining earlier protocol execution and recovery. RSS/Atom feed and scoped HTML capture retain bounded discovery results and support explicit, budgeted admission of article jobs with durable deduplication. Capture text extraction uses `capture-text-v2` with the same `body-text-v1` normalization. New configuration-based jobs retain immutable configuration revisions and explicit coverage dates. Authenticated document-change drafting and review are implemented; the owner can publish approved accounts as an immutable static release from the Review home page or the CLI, with atomic activation. The page shows release history read-only. Rollback stays in the CLI. When both drafting variables are set, the worker drafts an account of each complete text change with Claude Haiku 5.5, inside a daily token limit. A person edits, approves, and publishes each draft; the AI never decides. Live Auth0 login verification, production deployment, AI classification, and MCP endpoints remain pending. The prior implementation is preserved in [civic-lens-legacy](https://github.com/young-kobe/civic-lens-legacy).

Immutable document text extraction is also available: local commands verify imported captures, retain versioned HTML/plain-text extractions in Postgres, and return exact text-span citations. Configuration v2 supports reusable content-selection profiles whose exact revisions are retained with HTML extractions. Local document history now groups consecutive identical text within matching processing revisions while retaining every observation. Explicit comparisons persist bounded contextual diffs with exact text offsets. These are text-change records, not substantive-change judgments or human approvals. The review workspace shows saved versions, saved text, saved comparisons, and exact citations on read-only evidence pages.

The separate `review [port]` command provides an Auth0-protected review dashboard and draft editor with immutable revisions, exact evidence bindings, version-checked review decisions, and explicit concern resolution. It requires an HTTPS reverse proxy and configured identities; publishing uses the separate `releases` command.

The workspace uses Blazor static server-side rendering with a shared Razor component library. Only the owner Sources page is interactive. The release builder renders the same components to standalone static HTML from published release data. Light and dark themes follow the system setting, and a toggle overrides it per browser. Eligible comparisons can start a private review draft directly. Architecture tests enforce presentation ownership; the temporary mockups remain design references, not production pages.

[The federal pilot](docs/operations.md#federal-source-pilot) configures official Senate sources for Elizabeth Warren and Ted Cruz. Collection can be managed through CLI jobs or the owner-only Sources page. Captured pages and readable extractions are separate outcomes; a first observation establishes a baseline, not a document change.

Production hosting is agreed for the existing Hetzner CPX21, with Terraform-managed infrastructure and CI deployment independent of the developer workstation. This deployment remains planned; see [production hosting](docs/architecture.md#approved-production-hosting-planned).

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
