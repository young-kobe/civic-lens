# Repository rules

## Start here

Read README.md for current capabilities and commands, then docs/architecture.md for ownership and dependency rules. Read docs/roadmap.md only to select pending work, and docs/operations.md for execution. Inspect the relevant project and tests before changing code. Do not recursively read the legacy repo or generated artifacts by default.

Entry points: src/CivicLens.Host/Program.cs and src/CivicLens.Collector/Program.cs. Boundary tests: tests/CivicLens.Tests/ArchitectureTests.cs. Build policy: Directory.Build.props and global.json. The solution file is the project inventory. Do not create a second navigation index.

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

The application owns durable jobs and aggregate budgets. The collector accepts bounded work and returns captures/discovered links. Captures are immutable; jobs and imports are idempotent. A model prediction is never a human approval. Publication must reference the exact approved evidence version.

## Documentation and repository hygiene

Maintain only README.md, AGENTS.md, docs/architecture.md, docs/operations.md, and docs/roadmap.md as the core prose reference set. Update these in place. Do not add session reports, changelogs, audit trails, implementation diaries, or duplicate plans. Git records history.

Architecture diagrams and the generated HTML explorer share definitions in tools/render-architecture.py. When boundaries change, update the diagram definitions and run the renderer. Generated HTML lives in ignored artifacts/.

Distinguish implemented behavior from planned behavior. Remove completed roadmap work and document its actual behavior in the appropriate reference. Do not scaffold unused folders or packages. Keep only deliberate, small corpus fixtures with provenance; no bulk captures, live databases, downloaded model weights, or generated evaluation outputs in Git. Never import legacy credentials or operational files.

## Verification

Run locked restore, Release build, and tests. Run `dotnet format CivicLens.slnx --verify-no-changes --no-restore`. Check diagrams with `python3 tools/render-architecture.py --check`. Protect collector contracts, evidence fidelity, review version binding, job recovery, and publication isolation with meaningful regression tests. Architecture checks enforce the explicit project-boundary requirement.

New dependencies and projects require updating architecture checks and documentation intentionally. SDK and package versions are pinned; commit package lock files. Keep network/model evaluations opt-in and budgeted; ordinary CI must not call paid APIs.
