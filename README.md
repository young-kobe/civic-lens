# Civic Lens

Political transparency through officials' statements and actions, document changes, and evidence-backed weekly media briefs.

**Current state:** a buildable .NET foundation with project-boundary tests and architecture documentation. Collection, storage, AI, review, publication, and MCP endpoints are not implemented yet. The prior implementation is preserved in [civic-lens-legacy](https://github.com/young-kobe/civic-lens-legacy).

## Start here

Install the .NET SDK selected by `global.json`, then run:

```sh
dotnet restore --locked-mode
dotnet build --no-restore --configuration Release
dotnet test --no-build --configuration Release
dotnet run --project src/CivicLens.Host -- status
dotnet run --project src/CivicLens.Collector -- --help
```

No credentials, database, or paid services are required for the scaffold. Runtime data and generated releases will live under ignored `.runtime/` and `artifacts/` directories.

## Navigate the repository

| Path | Responsibility |
|---|---|
| `src/CivicLens.Core` | Domain boundary, currently empty |
| `src/CivicLens.Collection.Contracts` | Collector protocol boundary, currently empty |
| `src/CivicLens.Publication.Contracts` | Public release boundary, currently empty |
| `src/CivicLens.Application` | Use cases; currently foundation status only |
| `src/CivicLens.Infrastructure` | External adapters, currently empty |
| `src/CivicLens.Host` | Local application entry point |
| `src/CivicLens.Collector` | Independently runnable collector entry point |
| `tests/CivicLens.Tests` | Dependency-boundary regression checks |
| `tools/render-architecture.py` | Shared diagram definitions and offline visual generator |

Read `AGENTS.md` before implementation or delegation. The solution is the authoritative project inventory. Add source/configuration/fixture directories only when a feature needs them.

## Reference

- [Architecture and visual system map](docs/architecture.md)
- [Operations and verification](docs/operations.md)
- [Remaining implementation phases](docs/roadmap.md)
- [Contributor and agent rules](AGENTS.md)

The project uses AGPL-3.0; see [LICENSE](LICENSE). Documentation describes the current implementation and clearly marks planned behavior. Git holds history.
