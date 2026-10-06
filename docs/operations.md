# Operations

## Current scaffold

Prerequisites: .NET SDK selected by global.json. Python 3 is needed only to regenerate the architecture visual. No database, model account, Node installation, or deployment credentials are needed yet.

```sh
dotnet restore --locked-mode
dotnet build CivicLens.slnx --configuration Release --no-restore
dotnet test CivicLens.slnx --configuration Release --no-build
dotnet format CivicLens.slnx --verify-no-changes --no-restore
python3 tools/render-architecture.py --check
```

`dotnet run --project src/CivicLens.Host -- status` describes implemented capabilities. `dotnet run --project src/CivicLens.Collector -- --help` describes the collector boundary; collection is deliberately unavailable. Unsupported commands return nonzero exit codes.

Generate the self-contained architecture explorer with `python3 tools/render-architecture.py`; open artifacts/architecture.html in a browser. The HTML requires no remote assets or network access. The same script updates the Mermaid blocks in architecture.md. `--check` detects diagram drift without changing files.

## Dependency changes

Update explicit package versions, run `dotnet restore --force-evaluate`, review and commit packages.lock.json changes, then run the checks above. CI restores in locked mode. Update global.json deliberately when adopting an SDK patch or feature band.

## Runtime and future operations

Runtime data belongs in ignored .runtime/; generated publication and evaluation artifacts in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. There is currently no deploy command and no public endpoint.

Collection, configuration validation, review, backup/restore, publication, rollback, and MCP runbooks will be added here when implemented. Do not treat target architecture descriptions as executable procedures.
