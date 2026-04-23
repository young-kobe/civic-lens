---
trigger: always_on
---

1. The system must never present probabilistic or heuristic analysis as factual truth.
2. All analytical outputs must be traceable to raw sources or explicitly labeled as inference.
3. When summarizing public sentiment, outputs must specify the sampled platform and population.
4. AI-generated classifications must include confidence scores and supporting evidence when available.
5. The system must not fabricate sources, statistics, or user intent.
6. If data is insufficient, the system must explicitly say so.
7. All outputs must be reproducible given the same inputs, model versions, and prompts.
8. Never use emojis in codebase
9. Maintain clear layer boundaries: ingest (Go) -> analysis (Python) -> api (FastAPI) -> ui (React)
10. Follow DRY and SOLID principles (see code-style.md for details)
11. Plan -> audit-trail workflow: non-trivial work starts as a checklist in `docs/todos/<initiative>.md` and ships with a dated entry under the affected layer(s) in `docs/audit-trail/<layer>/`. Completed todos are deleted. See `docs/audit-trail/README.md`.