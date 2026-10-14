# Repository rules

## Start here

Read README.md for current capabilities and commands, then docs/architecture.md for ownership and dependency rules. Start at its lookup table and read only the feature docs in docs/architecture/ that the task needs. Read docs/roadmap.md only to select pending work, and docs/operations.md for execution. Inspect the relevant project and tests before changing code. Do not recursively read the legacy repo or generated artifacts by default.

Entry points: src/CivicLens.Host/Program.cs and src/CivicLens.Collector/Program.cs. Boundary tests: tests/CivicLens.Tests/Architecture/ArchitectureTests.cs. Build policy: Directory.Build.props and global.json. The solution file is the project inventory.

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

The C# application owns workflow logic. Create queue and job rows in the C# store that writes the triggering row, inside the same transaction. Use database triggers only for worker wakeups (`pg_notify`) and for concurrency guarantees that need several writers to share one lock order. Prefer readable C# over SQL when both can hold the guarantee.

Keep cyclomatic complexity low and code easy for a human to read. Prefer small, cohesive methods, explicit names, guard clauses, and readable control flow over deeply nested branches or long compound conditions. Encourage well-designed abstractions that give a responsibility a clear home, protect an invariant, or isolate a dependency. Apply SOLID at those boundaries and DRY by giving each rule one owner. Do not hide similar-looking code behind a generic abstraction when its responsibilities differ, or build frameworks for hypothetical variation.

Avoid unnecessary repeated reads and computation throughout the application. Reuse already validated data within an operation and use bounded batch reads where appropriate. Repeat reads only when required for correctness, authorization, freshness, transaction isolation, or recovery, and make that reason clear. Preserve trust-boundary validation and concurrency guarantees; do not introduce shared caches or generic infrastructure merely to hide redundant work. Verify performance-sensitive access patterns with focused tests or measurements.

Choose design patterns and data structures for the invariant or failure mode they address. The core patterns are now established: before proposing a mechanism, find how a comparable feature solves the same problem shape and reuse that solution. Fix the root cause, such as a mutable input, rather than add a mechanism that compensates for it. Propose a new mechanism only when the existing ones cannot serve, and name the precedent you checked. At each step that requires such a decision, surface the concrete problem, a list of viable options, their tradeoffs, and a recommendation. Wait for the user's choice before implementing that decision so they retain cognitive ownership of the architecture. State machines, immutable types, and pipelines are options, not defaults for every feature. Include ownership, public contracts, persistence semantics, and workflow behavior in these discussions when affected. Continue independent work while a decision is pending; do not reopen an already agreed choice unless new evidence changes the tradeoff.

Use conventions consistently across the repository and across layers where responsibilities are comparable. Prefer an established pattern or data structure when it fits; introduce a different approach only for a concrete benefit, and explain the departure in the design discussion. In production code, use file-scoped namespaces, PascalCase types and members, camelCase parameters and private fields, Async suffixes and CancellationToken parameters for asynchronous operations, records for wire data, and interfaces at external boundaries. Keep substantive public types in their own files; related enums may live beside the type that uses them. Keep validation at its owning layer, invoke it at trust boundaries, and distinguish invalid input from operational failure results. Apply new conventions deliberately across affected code and document significant departures in the architecture reference.

Keep the codebase legible and coherent, with clear patterns, boundaries, and abstractions. In each change, delete code that loses its last caller and remove code that the change makes redundant. Simplify the code that the change touches; do not refactor unrelated code in the same change. Apply DRY and SOLID within reason: one owner for each rule, without abstractions that serve one caller. Each feature extends existing machinery (shared owners, queues, stores, contracts, helpers) before it adds its own.

Organize projects by architectural layer and code within each project by feature. Keep a feature's use cases, configuration, and external interfaces together, and reuse feature names across layers (for example, Application/Collection and Infrastructure/Collection). Namespaces follow the project root namespace and feature folders. Avoid global Models, Services, or Interfaces buckets. Keep small contract projects flat; leave executable entry points and genuinely project-wide types at the project root. Mirror the owning layer and feature under tests, keep architecture checks in Architecture, and put shared test data factories in Fixtures only when several test groups need them. Blazor Razor components organize the Host's web presentation. Add folders for implemented code, not anticipated features.

The application owns durable jobs and aggregate budgets. The collector accepts bounded work and returns captures/discovered links. Captures are immutable; jobs and imports are idempotent. A model prediction is never a human approval. Publication must reference the exact approved evidence version.

## Documentation and repository hygiene

The core prose reference set is README.md, AGENTS.md, docs/architecture.md (system overview and the only lookup table), one doc for each feature in docs/architecture/, docs/operations.md, and docs/roadmap.md. Every feature doc uses the template headings: Purpose, Decisions, Invariants, Behavior, Storage and contracts, Code map, and Limits and planned work. Each invariant names its rule, its enforcer, and its protecting test, or says "No test". Update these in place. Do not add session reports, changelogs, audit trails, implementation diaries, or duplicate plans. Git records history.

Architecture diagrams and the generated HTML explorer share definitions in tools/render-architecture.py. When boundaries change, update the diagram definitions and run the renderer. Generated HTML lives in ignored artifacts/.

Distinguish implemented behavior from planned behavior. Remove completed roadmap work and document its actual behavior in the appropriate reference. Do not scaffold unused folders or packages. Keep only deliberate, small corpus fixtures with provenance; no bulk captures, live databases, downloaded model weights, or generated evaluation outputs in Git. Never import legacy credentials or operational files.

## Verification

Run locked restore, Release build, and tests. Run `dotnet format CivicLens.slnx --verify-no-changes --no-restore`. Check diagrams with `python3 tools/render-architecture.py --check`. Protect collector contracts, evidence fidelity, review version binding, job recovery, and publication isolation with meaningful regression tests. Architecture checks enforce the explicit project-boundary requirement. Before each commit, review the diff: every added member has a production caller, nothing left behind is unused, and no rule has a second owner.

New dependencies and projects require updating architecture checks and documentation intentionally. SDK and package versions are pinned; commit package lock files. Keep network/model evaluations opt-in and budgeted; ordinary CI must not call paid APIs.
