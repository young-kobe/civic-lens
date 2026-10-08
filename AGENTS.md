# Repository rules

## Start here

Read README.md for current capabilities and commands, then docs/architecture.md for ownership and dependency rules. Read docs/roadmap.md only to select pending work, and docs/operations.md for execution. Inspect the relevant project and tests before changing code. Do not recursively read the legacy repo or generated artifacts by default.

Entry points: src/CivicLens.Host/Program.cs and src/CivicLens.Collector/Program.cs. Boundary tests: tests/CivicLens.Tests/Architecture/ArchitectureTests.cs. Build policy: Directory.Build.props and global.json. The solution file is the project inventory. Do not create a second navigation index.

## Product and ownership

Civic Lens collects evidence for statements/actions, document changes, and weekly briefs. Officials, issues, and supported sources are configuration, never per-person branches. Collection and review run locally; public serving reads published releases.

Keep these dependencies acyclic and exact (enforced by tests):

- Core: no project or third-party package dependencies.
- Collection.Contracts and Publication.Contracts: no project or third-party package dependencies.
- Application: Core, Collection.Contracts, Publication.Contracts.
- Infrastructure: Application (other dependencies only through an explicitly reviewed boundary change).
- Host: Application, Infrastructure.
- Collector: Collection.Contracts only. No database, AI, editorial, or publishing dependencies.

Local CLI, review UI, and local MCP will call shared application handlers. Public MCP must live in a separate deployment and consume published contracts only. It must never reach operational storage, credentials, job handlers, or unpublished evidence.

## Implementation

Use C#/.NET for the application and independently runnable collector. Keep packages feature-focused. Prefer concrete types; interfaces belong at real external boundaries. Do not introduce microservices, a workflow DSL, broad repositories, dynamic plugins, or generic helpers without a concrete requirement.

Keep cyclomatic complexity low and code easy for a human to read. Prefer small, cohesive methods, explicit names, guard clauses, and readable control flow over deeply nested branches or long compound conditions. Encourage well-designed abstractions that give a responsibility a clear home, protect an invariant, or isolate a dependency. Apply SOLID at those boundaries and DRY by giving each rule one owner. Do not hide similar-looking code behind a generic abstraction when its responsibilities differ, or build frameworks for hypothetical variation.

Avoid unnecessary repeated reads and computation throughout the application. Reuse already validated data within an operation and use bounded batch reads where appropriate. Repeat reads only when required for correctness, authorization, freshness, transaction isolation, or recovery, and make that reason clear. Preserve trust-boundary validation and concurrency guarantees; do not introduce shared caches or generic infrastructure merely to hide redundant work. Verify performance-sensitive access patterns with focused tests or measurements.

Choose design patterns and data structures for the invariant or failure mode they address, without limiting consideration to a fixed set of patterns. At each step that requires such a decision, surface the concrete problem, a list of viable options, their tradeoffs, and a recommendation. Wait for the user's choice before implementing that decision so they retain cognitive ownership of the architecture. State machines, immutable types, and pipelines are options, not defaults for every feature. Include ownership, public contracts, persistence semantics, and workflow behavior in these discussions when affected. Continue independent work while a decision is pending; do not reopen an already agreed choice unless new evidence changes the tradeoff.

Use conventions consistently across the repository and across layers where responsibilities are comparable. Prefer an established pattern or data structure when it fits; introduce a different approach only for a concrete benefit, and explain the departure in the design discussion. In production code, use file-scoped namespaces, PascalCase types and members, camelCase parameters and private fields, Async suffixes and CancellationToken parameters for asynchronous operations, records for wire data, and interfaces at external boundaries. Keep substantive public types in their own files; related enums may live beside the type that uses them. Keep validation at its owning layer, invoke it at trust boundaries, and distinguish invalid input from operational failure results. Apply new conventions deliberately across affected code and document significant departures in the architecture reference.

Organize projects by architectural layer and code within each project by feature. Keep a feature's use cases, configuration, and external interfaces together, and reuse feature names across layers (for example, Application/Collection and Infrastructure/Collection). Namespaces follow the project root namespace and feature folders. Avoid global Models, Services, or Interfaces buckets. Keep small contract projects flat; leave executable entry points and genuinely project-wide types at the project root. Mirror the owning layer and feature under tests, keep architecture checks in Architecture, and put shared test data factories in Fixtures only when several test groups need them. MVC or Razor Pages organizes the Host's future web presentation only. Add folders for implemented code, not anticipated features.

The application owns durable jobs and aggregate budgets. The collector accepts bounded work and returns captures/discovered links. Captures are immutable; jobs and imports are idempotent. A model prediction is never a human approval. Publication must reference the exact approved evidence version.

## Documentation and repository hygiene

Maintain only README.md, AGENTS.md, docs/architecture.md, docs/operations.md, and docs/roadmap.md as the core prose reference set. Update these in place. Do not add session reports, changelogs, audit trails, implementation diaries, or duplicate plans. Git records history.

Architecture diagrams and the generated HTML explorer share definitions in tools/render-architecture.py. When boundaries change, update the diagram definitions and run the renderer. Generated HTML lives in ignored artifacts/.

Distinguish implemented behavior from planned behavior. Remove completed roadmap work and document its actual behavior in the appropriate reference. Do not scaffold unused folders or packages. Keep only deliberate, small corpus fixtures with provenance; no bulk captures, live databases, downloaded model weights, or generated evaluation outputs in Git. Never import legacy credentials or operational files.

## Verification

Run locked restore, Release build, and tests. Run `dotnet format CivicLens.slnx --verify-no-changes --no-restore`. Check diagrams with `python3 tools/render-architecture.py --check`. Protect collector contracts, evidence fidelity, review version binding, job recovery, and publication isolation with meaningful regression tests. Architecture checks enforce the explicit project-boundary requirement.

New dependencies and projects require updating architecture checks and documentation intentionally. SDK and package versions are pinned; commit package lock files. Keep network/model evaluations opt-in and budgeted; ordinary CI must not call paid APIs.
