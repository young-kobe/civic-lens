# Operations

## Verification

Prerequisites: .NET SDK selected by global.json. Python 3 is needed only to regenerate the architecture visual. No database, model account, Node installation, or deployment credentials are needed yet.

```sh
dotnet restore --locked-mode
dotnet build CivicLens.slnx --configuration Release --no-restore
dotnet test CivicLens.slnx --configuration Release --no-build
dotnet format CivicLens.slnx --verify-no-changes --no-restore
python3 tools/render-architecture.py --check
```

`dotnet run --project src/CivicLens.Host -- status` describes implemented capabilities. `dotnet run --project src/CivicLens.Collector -- --help` describes the manifest-driven collector. Unsupported commands return nonzero exit codes.

Generate the self-contained architecture explorer with `python3 tools/render-architecture.py`; open artifacts/architecture.html in a browser. The HTML requires no remote assets or network access. The same script updates the Mermaid blocks in architecture.md. `--check` detects diagram drift without changing files.

## Dependency changes

Update explicit package versions, run `dotnet restore --force-evaluate`, review and commit packages.lock.json changes, then run the checks above. CI restores in locked mode. Update global.json deliberately when adopting an SDK patch or feature band.

## Watched-page tracer

Use the installed SDK on `PATH`. Build Release first. Validate the example registry without making requests:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- validate config/example.json
```

The example contains a fictitious official and one localhost watched page. Start a local source in one terminal:

```sh
mkdir -p .runtime/example-source
printf 'User-agent: *\nDisallow:\n' > .runtime/example-source/robots.txt
printf '<html><body>A local evidence example.</body></html>\n' > .runtime/example-source/page.html
python3 -m http.server 8765 --bind 127.0.0.1 --directory .runtime/example-source
```

In another terminal, collect through the real process boundary:

```sh
dotnet run --no-build --configuration Release --project src/CivicLens.Host -- collect config/example.json example-local-source src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll .runtime/captures
```

The host prints a verified JSON receipt. A captured result points to `capture.relativePath` (`<sha256>.gz`) relative to `.runtime/captures`; `gzip -dc .runtime/captures/<sha256>.gz` inspects the original response bytes. Repeating the command produces a new observation receipt but reuses an identical artifact. Receipts separate `capture` (hash, relative path and byte length) from `response` (status, validators, content type and ordered content encodings). The storage gzip envelope is separate from HTTP content encoding; decoding a gzip-encoded HTTP response requires both layers. Receipts are not yet imported or persisted by the host. Save stdout if needed for inspection; do not treat it as a durable job store. No database is needed at this checkpoint.

Edit configuration to add people or shared sources. A source URL occurs once, with multiple `personIds` when appropriate. `enabled: false` prevents collection without removing the configuration. `allowedOrigin` includes scheme and port; `allowedPathPrefix` permits that exact path and descendants. The local example explicitly permits loopback; this tool is for trusted local configuration, not arbitrary public fetch requests. Keep credentials out of source URLs and manifests.

Standalone collection accepts `CivicLens.Collector collect <manifest.json>` (or `dotnet <collector.dll> collect <manifest.json>`). Version 2 request fields are defined by `CollectionRequest` in Collection.Contracts: `version`, `jobId`, `sourceId`, `url`, `allowedOrigin`, `allowedPathPrefix`, and absolute `artifactDirectory`, plus optional bounded `maxRequests`, `maxBytes`, `timeoutSeconds`, `minDelayMilliseconds`, `eTag`, and `lastModified`. JSON names are case-sensitive. Use version 2 in standalone manifests and rebuild host and collector together; version 1 collection messages are rejected. Registry configuration remains version 1. Validators are explicit in standalone manifests; the host does not yet retain or replay validators. A 304 emits `notModified` without a capture. A 429 emits `deferred`, with optional `retryAfterSeconds`; wait before rerunning. HTTP failures, robots denial/unavailability, budget exhaustion, incomplete bodies, and cancellation are explicit failures rather than source deletion.

Do not run concurrent collectors for the same origin. Host invocations sharing a capture directory are mutually exclusive, but separate directories and standalone invocations are not coordinated. Robots support and protocol limits are documented in [architecture](architecture.md). A killed child can leave `.capture-*.tmp`; these files are never valid captures and can be removed when no collector is running. Do not remove hash-named captures as temporary files. Durable jobs, imports, retry scheduling, cross-run pacing, and crash recovery are the next Phase 1 checkpoint.

## Runtime and future operations

Runtime data belongs in ignored .runtime/; generated publication and evaluation artifacts in ignored artifacts/. Never commit credentials or copy operational directories from the legacy repository. There is currently no deploy command and no public endpoint.

Durable collection/import, review, backup/restore, publication, rollback, and MCP runbooks will be added here when implemented. Do not treat target architecture descriptions as executable procedures.
