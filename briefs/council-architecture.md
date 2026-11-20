# Council role 3: Architecture and ownership adversary

Read briefs/council-common.md on this branch first (git show origin/cloud-briefs:briefs/council-common.md). Follow it exactly. This file names your angle.

Role name: Architecture and ownership.

Read: AGENTS.md closely, docs/architecture.md and its lookup table, docs/post-r68/claims.md, docs/post-r68/evidence-interface.md, docs/post-r68/graph.md, docs/post-r68/delivery.md, and the feature docs in docs/architecture/ that these name (at least statements.md and publication.md, and the review and revision docs). Then read the code that the plan says it will reuse, such as the relationship-drafting and review handlers. Use grep to find them.

Attack these:
- Precedent. AGENTS.md says to reuse existing owners, queues, stores, and contracts before new machinery. For each new mechanism in the plan (review decision bound to evidence revisions, versioned issue definitions, graph projection, release binding of membership and counts, a separately deployable frontend route), name the existing precedent in the code. Say whether the plan reuses it or forks it. Give file paths.
- Boundaries. Check each proposal against the dependency rules in AGENTS.md. PublicApi reads published releases only. Collector has no database or AI. Public MCP or public routes never reach operational storage. Flag any path in the plan that could leak unpublished evidence or a rejected link.
- Review version binding. AGENTS.md says publication must reference the exact approved evidence version and a model prediction is never a human approval. Check that the pilot's accept, reject, and insufficient-evidence decisions bind to exact revisions, and that a second reviewer can reproduce them. Check what happens when a source revision changes after a decision.
- Graph projection. Say whether a graph store or library adds value over PostgreSQL joins for the named reader questions. Estimate the cost to keep a projection in step with releases and withdrawals.
- Delivery phase. Check whether an independently deployable route conflicts with the shipped server-rendered PublicApi design in docs/architecture/public-site.md. Say what the trial must prove before any second stack.
- Docs hygiene. AGENTS.md forbids duplicate plans, session reports and diaries, and requires the feature-doc template. Check whether docs/post-r68/ duplicates docs/roadmap.md or the architecture docs, and how it should fold into them.
- Scope. Flag work that is speculative under AGENTS.md ("no features beyond what was asked", "add folders for implemented code").

For new features you propose, name the existing owner each would extend.
