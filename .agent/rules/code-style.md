---
trigger: always_on
---

## Design Principles

1. Follow DRY (Don't Repeat Yourself): Extract shared logic into reusable functions, modules, or base classes.
2. Follow SOLID principles:
   - Single Responsibility: Each module/class handles one concern
   - Open/Closed: Extend via composition or inheritance, not modification
   - Liskov Substitution: Subtypes must be substitutable for their base types
   - Interface Segregation: Prefer small, focused interfaces
   - Dependency Inversion: Depend on abstractions, not concrete implementations
3. Maintain clear layer boundaries: ingest (Go) -> analysis (Python) -> api (FastAPI) -> ui (React)

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
