# Post-R-68 plan appendix

Civic Lens should let a citizen follow a public claim through a decision to a measured outcome. Start with a question that matters to the reader. Give each answer a date, an accountable actor, and a short path to the original evidence.

The owner approved this direction. The phases below propose work after R-68. Existing [roadmap items](../roadmap.md#accepted-plans) retain their dependencies. The [architecture reference](../architecture.md) describes shipped behavior. Move accepted behavior into its feature reference as each phase ships.

| Phase | Deliverable | Evidence for moving forward |
|---|---|---|
| 1. [Claims and evidence](claims.md) | One veterans-benefits audit and one explanation of the Paine memorial vote | A reviewer can reconstruct each finding from exact source versions and explain the remaining gaps. |
| 1, from the first case. [Evaluation and observability](evaluation.md) | A private run comparison, a labeled holdout and a public methods summary | Show which stage improved, the effect on critical errors and coverage, and the cost per accepted finding. |
| 2. [Graph and statistical research](graph.md) | Typed paths across records and a bounded comparison of retrieval methods | Each additional correct link survives review and leads to cited evidence. Measure the cost of maintaining the projection. |
| 3. [Reader experience](reader.md) | Issue timelines, a changing homepage and a collapsible left navigation | New readers can explain what happened, find their member's part, and distinguish a verified fact from an open question. |
| 4. [Frontend delivery](delivery.md) | One independently deployable issue route using the published API | A shell survives an unavailable API. Deep links, withdrawals, privacy and release consistency pass the route trial. |

## Experiments

| Run on November 18, 2026 | Result and repeatable materials |
|---|---|
| [Published graph on two House bills](experiments/published-graph.md) | In release 1416, 338 sponsorships produced 56,281 member pairs. The same common backer appeared in SQL and NetworkX. [Query](experiments/published-graph.sql) and [analysis](experiments/project-sponsors.py). |
| [Veterans claims backlog at two cutoffs](experiments/va-backlog.md) | VA's inauguration-day count gives a 72.07% decline by Oct 10 and 69.95% by Nov 7. Two weekly baselines also cross the threshold. [Workbook calculation](experiments/va-backlog.py). |

## Decisions to take in order

1. Choose the first claim types, evidence states, and review rule using the two pilot cases. Approve schema and public wording separately. Start the held-out set before prompt tuning.
2. Measure the gain from typed traversal over exact joins and text search. The working recommendation is PostgreSQL for evidence and an offline graph projection for research.
3. Compare branch-first navigation with the proposed reader-first hierarchy on the same tasks. Retain real bill and vote permalinks in both.
4. Choose the frontend runtime after the one-route trial. Keep the public contract independent of which option wins.

The first useful release can be small. A reader should be able to follow the Paine vote to the bill's purpose and the sponsors' own words. The veterans page should let the reader reproduce one number and identify one unanswered question. Repeat with a second issue and agency before generalizing these designs.
