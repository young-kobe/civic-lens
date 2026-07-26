---
trigger: always_on
---

## Design Principles

1. Follow DRY (Don't Repeat Yourself): Extract shared logic into reusable functions, modules, or base classes — *after* the third copy. Three similar lines is better than a premature abstraction.
2. Follow SOLID principles:
   - Single Responsibility: Each module/class handles one concern
   - Open/Closed: Extend via composition or inheritance, not modification
   - Liskov Substitution: Subtypes must be substitutable for their base types
   - Interface Segregation: Prefer small, focused interfaces
   - Dependency Inversion: Depend on abstractions, not concrete implementations
3. Maintain clear layer boundaries: ingest (Go) -> analysis (Python) -> api (FastAPI) -> ui (React)
4. **Avoid unnecessary abstractions.** Don't introduce a helper, wrapper, class, or adapter until a concrete consumer demands it. No speculative base classes, no "might reuse later" utilities, no generic helpers written ahead of the second call site. If two consumers have ~80% shared code, inline both before extracting. MVP constraint: the cost of the wrong abstraction is greater than the cost of some duplication.
5. **Clean up dead code as you go.** In every change, remove code that is no longer referenced by the change you just made — unused imports, now-orphaned helpers, CSS classes with no markup, branches that can't fire, comments describing a state that no longer exists. Don't leave `// deprecated` / `# removed` markers; delete. Don't leave backwards-compat shims; we're pre-1.0. If you add a replacement, delete the replaced thing in the same PR.
6. **No backwards-compat shims.** Breaking an older snapshot shape, API response, or storage layout is fine — the cron rebuilds and the frontend reloads. Don't mark fields `Optional[...]` "in case older data exists" unless older data genuinely coexists in prod right now.

## Python Style

1. Use type hints for all function signatures
2. Use triple-quoted docstrings for public functions and classes
3. Use `snake_case` for functions, variables, and modules
4. Use `PascalCase` for classes
5. Use dataclasses for data models (prefer over dicts for structured data)
6. Handle errors explicitly; never silently swallow exceptions
7. Import from package roots (e.g., `from analysis.src.common.logger import get_logger`)

## Go Style

1. Use short, descriptive function comments starting with the function name
2. Use `fmt.Errorf` with `%w` for error wrapping
3. Propagate `context.Context` as the first parameter
4. Keep functions focused; extract helpers for complex logic
5. Use struct receivers for stateful operations

## TypeScript/React Style

1. Use TypeScript interfaces for props and API response types
2. Use functional components with hooks
3. Use `camelCase` for functions and variables, `PascalCase` for components
4. Define explicit types; avoid `any`

## General

1. No magic numbers; use named constants
2. No hardcoded credentials or secrets
3. Prefer explicit over implicit behavior
4. Write self-documenting code; add comments only for non-obvious logic
5. Keep function and method lengths to ~60 lines MAXIMUM as general rule, unless unavoidable. Do not eclipse 100 lines.
6. **Plan -> audit-trail workflow.** Non-trivial work starts as a checklist in `docs/todos/<initiative>.md`. On merge, the change lands with a dated entry in `docs/audit-trail/<layer>/YYYY-MM-DD-short-slug.md`. Completed todos get deleted; audit-trail entries are permanent. See `docs/audit-trail/README.md` for the entry template.

## Current project shape (reference)

Four layers, strictly ordered (see CLAUDE.md for detail): Go ingest → Python analysis (ETL + engine + scheduler) → FastAPI API (live Postgres queries at request time) → React UI. Public-facing data is editorial, bucketed by the three-way entity frame (News Outlets / Verified Officials / General Public) driven by DB-native entity curation (`corpus.entities`, curated directly in the database — see `docs/audit-trail/analysis/2026-07-22-db-native-entity-curation.md`). Confidence scores, sample labeling, and source-back-links (`docs/INVARIANTS.md` C1) are non-negotiable on every evidence surface.