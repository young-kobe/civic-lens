# Civic Lens

Political transparency through officials' statements and actions, document changes, and evidence-backed weekly media briefs.

**Current state:** a watched-page tracer with validated people/source configuration, a separate bounded HTTP collector, immutable gzip captures, and host-verified JSON receipts that preserve response metadata. Core domain rules now model immutable observations, import replay decisions, and conservative 304 evidence linkage. Application import integration, durable jobs/imports, feeds/discovery, database storage, AI, review, publication, and MCP endpoints are not implemented yet. The prior implementation is preserved in [civic-lens-legacy](https://github.com/young-kobe/civic-lens-legacy).

## Start here

Install the .NET SDK selected by `global.json`, then run:

```sh
dotnet restore --locked-mode
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
dotnet run --project src/CivicLens.Host -- status
dotnet run --project src/CivicLens.Collector -- --help
```

No credentials, database, or paid services are required for the tracer. See [operations](docs/operations.md) for the local end-to-end example. Runtime data and generated releases will live under ignored `.runtime/` and `artifacts/` directories.

## Navigate the repository

| Path | Responsibility |
|---|---|
| `src/CivicLens.Core` | `Collection/` owns immutable observations and import decisions |
| `src/CivicLens.Collection.Contracts` | Versioned collection types and shared receipt validation |
| `src/CivicLens.Publication.Contracts` | Public release boundary, currently empty |
| `src/CivicLens.Application` | `Collection/` groups configuration, use cases, and external interfaces |
| `src/CivicLens.Infrastructure` | `Collection/` implements process execution and capture verification |
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
