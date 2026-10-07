# Civic Lens

Political transparency through officials' statements and actions, document changes, and evidence-backed weekly media briefs.

**Current state:** a watched-page tracer with validated people/source configuration, a separate bounded HTTP collector, immutable gzip captures, and host-verified JSON receipts that preserve response metadata. Core domain rules now model immutable collection attempt results, import decisions for new and duplicate attempts, and conservative 304 evidence linkage. Application maps verified attempts through the Core policy, and Infrastructure implements atomic Postgres imports with EF Core/Npgsql and an initial migration. Database integration tests use disposable Postgres containers. The CLI supports explicit database migrations, collection/import with application-generated attempt IDs, and filesystem receipt handoff with replay after interrupted imports. Recovery revalidates captures and can use relocated runtime storage without collecting again. Managed Postgres jobs now add a guarded lifecycle, bounded retries and aggregate budgets, fenced ownership, and shared origin pacing/backoff through explicit CLI commands. RSS/Atom feed and scoped HTML capture retain bounded discovery results and support explicit, budgeted admission of article jobs with durable deduplication. AI, review, publication, and MCP endpoints are not implemented yet. The prior implementation is preserved in [civic-lens-legacy](https://github.com/young-kobe/civic-lens-legacy).

## Start here

Install the .NET SDK selected by `global.json`, then run:

```sh
dotnet restore --locked-mode
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
dotnet run --project src/CivicLens.Host -- status
dotnet run --project src/CivicLens.Collector -- --help
```

No credentials, database, or paid services are required for the tracer. The full test suite requires a working Docker daemon and starts disposable Postgres containers; see operations for database-free checks. See [operations](docs/operations.md) for the local end-to-end example. Runtime data and generated releases will live under ignored `.runtime/` and `artifacts/` directories.

## Navigate the repository

| Path | Responsibility |
|---|---|
| `src/CivicLens.Core` | `Collection/` owns immutable collection attempt results and import decisions |
| `src/CivicLens.Collection.Contracts` | Versioned collection types and shared receipt validation |
| `src/CivicLens.Publication.Contracts` | Public release boundary, currently empty |
| `src/CivicLens.Application` | `Collection/` groups configuration, collection/import use cases, and external interfaces |
| `src/CivicLens.Infrastructure` | `Collection/` implements process execution, capture verification, and Postgres persistence |
| `src/CivicLens.Host` | Local application entry point |
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
